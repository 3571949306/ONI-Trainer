using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ONITrainer
{
    public sealed class CommunityPoiEntry
    {
        public string PrefabId = "";
        public string DisplayName = "";
        public string Category = "geyser";
        public int X;
        public int Y;
        public int DistFromStart;
        public double AvgYieldGps;
        public char Grade = ' ';
        public double Percentile;
    }

    public sealed class CommunityPlanetEntry
    {
        public int WorldIndex;
        public string WorldId = "";
        public string WorldName = "";
        public bool PoisScanned;
        public List<string> Traits = new List<string>();
        public List<string> TraitIds = new List<string>();
        public List<CommunityPoiEntry> Pois = new List<CommunityPoiEntry>();
    }

    public sealed class CommunitySeedEntry
    {
        public int SchemaVersion = 1;
        public string ClusterPrefix = "";
        public string ClusterPath = "";
        public string ClusterName = "";
        public int Seed;
        public string Coordinate = "";
        public string StoryCode = "0";
        public string MixingCode = "0";
        public string DlcFlags = "";
        public string Author = "";
        public string SubmittedAt = "";
        public string TraitSummary = "";
        public string PoiSummary = "";
        public string Note = "";
        public List<CommunityPlanetEntry> Planets = new List<CommunityPlanetEntry>();

        public string GetClusterFileKey()
        {
            string p = (ClusterPrefix ?? "").Trim().ToUpperInvariant();
            if (CommunitySeedDb.IsValidClusterPrefix(p)) return p;
            if (!string.IsNullOrEmpty(Coordinate))
            {
                Match m = Regex.Match(Coordinate.Trim().ToUpperInvariant(), @"^([A-Z0-9-]{2,24})-(\d+)(?:-.*)?$");
                if (m.Success && CommunitySeedDb.IsValidClusterPrefix(m.Groups[1].Value))
                    return m.Groups[1].Value;
            }
            return "";
        }
    }

    public sealed class CommunityManifestItem
    {
        public string ClusterPrefix = "";
        public string File = "";
        public int Count;
        public string Sha256 = "";
    }

    public sealed class CommunityManifest
    {
        public int SchemaVersion = 1;
        public string UpdatedAt = "";
        public int TotalSeeds;
        public List<CommunityManifestItem> Clusters = new List<CommunityManifestItem>();
    }

    public sealed class CommunitySyncResult
    {
        public bool Success;
        public int UpdatedClusters;
        public int TotalSeeds;
        public string UsedSource = "";
        public string Error = "";
        public CommunityManifest Manifest;
    }

    public sealed class CommunityQueryResult
    {
        public CommunitySeedEntry Entry;
        public Dictionary<int, List<WorldTraitDef>> EvalTraits;
    }

    public static class CommunitySeedDb
    {
        static readonly Regex PrefixRegex = new Regex(@"^[A-Z0-9-]{2,24}$", RegexOptions.Compiled);
        static readonly Regex RepoRegex = new Regex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
        static readonly object _syncLock = new object();
        static readonly object _dbLock = new object();
        static int _bgPullRunning;


        static readonly Dictionary<string, Dictionary<string, CommunitySeedEntry>> _localMem =
            new Dictionary<string, Dictionary<string, CommunitySeedEntry>>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Dictionary<string, CommunitySeedEntry>> _pendingMem =
            new Dictionary<string, Dictionary<string, CommunitySeedEntry>>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, List<CommunitySeedEntry>> _mergedCache =
            new Dictionary<string, List<CommunitySeedEntry>>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> _dirtyLocalPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> _dirtyPendingPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool _pendingLoadedAll;

        public static event Action StateChanged;

        public static string CacheDir
        {
            get
            {
                string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(Path.Combine(app, "ONITrainer"), "seed-db");
            }
        }

        public static string DataCacheDir
        {
            get { return Path.Combine(CacheDir, "data"); }
        }

        public static string LocalRecordedDir
        {
            get { return Path.Combine(CacheDir, "local"); }
        }

        public static string PendingUploadDir
        {
            get { return Path.Combine(CacheDir, "pending"); }
        }

        static void NotifyStateChanged()
        {
            Action cb = StateChanged;
            if (cb != null)
            {
                try { cb(); } catch { }
            }
        }

        public static bool IsValidClusterPrefix(string prefix)
        {
            return !string.IsNullOrEmpty(prefix) && PrefixRegex.IsMatch(prefix);
        }

        public static bool IsValidRepoSlug(string repo)
        {
            return !string.IsNullOrEmpty(repo) && RepoRegex.IsMatch(repo.Trim());
        }

        public static string NormalizeDlcKey(string dlcFlags)
        {
            if (string.IsNullOrEmpty(dlcFlags)) return "";
            string[] ps = dlcFlags.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (ps.Length == 0) return "";
            List<string> list = new List<string>(ps.Length);
            for (int i = 0; i < ps.Length; i++)
            {
                string u = ps[i].Trim().ToUpperInvariant();

                if (u.Length > 0 && !list.Contains(u)) list.Add(u);
            }
            list.Sort(StringComparer.Ordinal);
            return string.Join(",", list.ToArray());
        }

        static string NormalizeWorldGenDlcKey(string dlcFlags)
        {
            if (string.IsNullOrEmpty(dlcFlags)) return "";
            string[] ps = dlcFlags.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> list = new List<string>(ps.Length);
            for (int i = 0; i < ps.Length; i++)
            {
                string u = ps[i].Trim().ToUpperInvariant();
                if (u == "DLC3_ID") continue;
                if (u.Length > 0 && !list.Contains(u)) list.Add(u);
            }
            list.Sort(StringComparer.Ordinal);
            return string.Join(",", list.ToArray());
        }

        static string NormCode(string c)
        {
            if (string.IsNullOrEmpty(c)) return "0";
            string t = c.Trim();
            return t.Length == 0 ? "0" : t;
        }

        public static string ComputeSha256Hex(byte[] data)
        {
            if (data == null) return "";
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static DateTime? GetLastSyncUtc()
        {
            try
            {
                string p = Path.Combine(CacheDir, "last_sync.txt");
                if (!File.Exists(p)) return null;
                string txt = File.ReadAllText(p, Encoding.UTF8).Trim();
                DateTime dt;
                if (DateTime.TryParse(txt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
                    return dt;
            }
            catch { }
            return null;
        }

        static void WriteLastSyncUtc(DateTime utc)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(Path.Combine(CacheDir, "last_sync.txt"),
                    utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    new UTF8Encoding(false));
            }
            catch { }
        }

        public static CommunityManifest LoadLocalManifest()
        {
            try
            {
                string p = Path.Combine(CacheDir, "manifest.json");
                if (!File.Exists(p)) return new CommunityManifest();
                string raw = File.ReadAllText(p, Encoding.UTF8);
                return ParseManifestJson(raw);
            }
            catch
            {
                return new CommunityManifest();
            }
        }

        static string DedupKey(CommunitySeedEntry e)
        {
            return e.Seed.ToString(CultureInfo.InvariantCulture) + "|" +
                   NormalizeWorldGenDlcKey(e.DlcFlags) + "|" +
                   NormCode(e.StoryCode) + "|" +
                   NormCode(e.MixingCode);
        }

        static CommunitySeedEntry CloneEntry(CommunitySeedEntry src)
        {
            CommunitySeedEntry dst = new CommunitySeedEntry
            {
                SchemaVersion = src.SchemaVersion,
                ClusterPrefix = src.ClusterPrefix,
                ClusterPath = src.ClusterPath,
                ClusterName = src.ClusterName,
                Seed = src.Seed,
                Coordinate = src.Coordinate,
                StoryCode = src.StoryCode,
                MixingCode = src.MixingCode,
                DlcFlags = src.DlcFlags,
                Author = src.Author,
                SubmittedAt = src.SubmittedAt,
                TraitSummary = src.TraitSummary,
                PoiSummary = src.PoiSummary,
                Note = src.Note
            };
            if (src.Planets != null)
            {
                for (int i = 0; i < src.Planets.Count; i++)
                {
                    CommunityPlanetEntry sp = src.Planets[i];
                    CommunityPlanetEntry dp = new CommunityPlanetEntry
                    {
                        WorldIndex = sp.WorldIndex,
                        WorldId = sp.WorldId,
                        WorldName = sp.WorldName,
                        PoisScanned = sp.PoisScanned,
                        Traits = new List<string>(sp.Traits),
                        TraitIds = new List<string>(sp.TraitIds)
                    };
                    for (int k = 0; k < sp.Pois.Count; k++)
                    {
                        CommunityPoiEntry o = sp.Pois[k];
                        dp.Pois.Add(new CommunityPoiEntry
                        {
                            PrefabId = o.PrefabId,
                            DisplayName = o.DisplayName,
                            Category = o.Category,
                            X = o.X,
                            Y = o.Y,
                            DistFromStart = o.DistFromStart,
                            AvgYieldGps = o.AvgYieldGps,
                            Grade = o.Grade,
                            Percentile = o.Percentile
                        });
                    }
                    dst.Planets.Add(dp);
                }
            }
            return dst;
        }

        static void MergeIntoEntry(CommunitySeedEntry target, CommunitySeedEntry inc)
        {
            if (string.IsNullOrEmpty(target.ClusterPath) && !string.IsNullOrEmpty(inc.ClusterPath))
                target.ClusterPath = inc.ClusterPath;
            if (string.IsNullOrEmpty(target.ClusterName) && !string.IsNullOrEmpty(inc.ClusterName))
                target.ClusterName = inc.ClusterName;
            if (string.IsNullOrEmpty(target.DlcFlags) && !string.IsNullOrEmpty(inc.DlcFlags))
                target.DlcFlags = inc.DlcFlags;
            if (string.IsNullOrEmpty(target.TraitSummary) && !string.IsNullOrEmpty(inc.TraitSummary))
                target.TraitSummary = inc.TraitSummary;
            if (!string.IsNullOrEmpty(inc.PoiSummary) &&
                (string.IsNullOrEmpty(target.PoiSummary) || inc.PoiSummary.Length > target.PoiSummary.Length))
                target.PoiSummary = inc.PoiSummary;
            if (!string.IsNullOrEmpty(inc.Note) && (string.IsNullOrEmpty(target.Note) || target.Note.IndexOf(inc.Note, StringComparison.Ordinal) < 0))
                target.Note = string.IsNullOrEmpty(target.Note) ? inc.Note : (target.Note + "; " + inc.Note);

            Dictionary<int, CommunityPlanetEntry> byWorld = new Dictionary<int, CommunityPlanetEntry>();
            for (int i = 0; i < target.Planets.Count; i++)
                byWorld[target.Planets[i].WorldIndex] = target.Planets[i];

            for (int i = 0; i < inc.Planets.Count; i++)
            {
                CommunityPlanetEntry ip = inc.Planets[i];
                CommunityPlanetEntry cur;
                if (!byWorld.TryGetValue(ip.WorldIndex, out cur))
                {
                    byWorld[ip.WorldIndex] = ClonePlanet(ip);
                }
                else
                {
                    if (!cur.PoisScanned && (ip.PoisScanned || ip.Pois.Count > 0))
                    {
                        cur.PoisScanned = true;
                        cur.Pois = ClonePois(ip.Pois);
                    }
                    else if (ip.Pois.Count > cur.Pois.Count)
                    {
                        cur.PoisScanned = cur.PoisScanned || ip.PoisScanned;
                        cur.Pois = ClonePois(ip.Pois);
                    }
                    if (cur.Traits.Count == 0 && ip.Traits.Count > 0)
                        cur.Traits = new List<string>(ip.Traits);
                    if (cur.TraitIds.Count == 0 && ip.TraitIds.Count > 0)
                        cur.TraitIds = new List<string>(ip.TraitIds);
                    if (string.IsNullOrEmpty(cur.WorldName) && !string.IsNullOrEmpty(ip.WorldName))
                        cur.WorldName = ip.WorldName;
                    if (string.IsNullOrEmpty(cur.WorldId) && !string.IsNullOrEmpty(ip.WorldId))
                        cur.WorldId = ip.WorldId;
                }
            }

            List<int> keys = new List<int>(byWorld.Keys);
            keys.Sort();
            target.Planets.Clear();
            for (int i = 0; i < keys.Count; i++)
                target.Planets.Add(byWorld[keys[i]]);


            RebuildPoiSummary(target);
        }

        static CommunityPlanetEntry ClonePlanet(CommunityPlanetEntry sp)
        {
            return new CommunityPlanetEntry
            {
                WorldIndex = sp.WorldIndex,
                WorldId = sp.WorldId,
                WorldName = sp.WorldName,
                PoisScanned = sp.PoisScanned || sp.Pois.Count > 0,
                Traits = new List<string>(sp.Traits),
                TraitIds = new List<string>(sp.TraitIds),
                Pois = ClonePois(sp.Pois)
            };
        }

        static List<CommunityPoiEntry> ClonePois(List<CommunityPoiEntry> src)
        {
            List<CommunityPoiEntry> list = new List<CommunityPoiEntry>(src != null ? src.Count : 0);
            if (src == null) return list;
            for (int i = 0; i < src.Count; i++)
            {
                CommunityPoiEntry o = src[i];
                list.Add(new CommunityPoiEntry
                {
                    PrefabId = o.PrefabId,
                    DisplayName = o.DisplayName,
                    Category = o.Category,
                    X = o.X,
                    Y = o.Y,
                    DistFromStart = o.DistFromStart,
                    AvgYieldGps = o.AvgYieldGps,
                    Grade = o.Grade,
                    Percentile = o.Percentile
                });
            }
            return list;
        }

        static void RebuildPoiSummary(CommunitySeedEntry ent)
        {
            if (ent == null || ent.Planets == null) return;
            List<string> poiParts = new List<string>();
            for (int i = 0; i < ent.Planets.Count; i++)
            {
                CommunityPlanetEntry pe = ent.Planets[i];
                if (pe.Pois == null || pe.Pois.Count == 0) continue;
                Dictionary<string, int> gCount = new Dictionary<string, int>(StringComparer.CurrentCulture);
                Dictionary<string, char> bestGrade = new Dictionary<string, char>(StringComparer.CurrentCulture);
                for (int pi = 0; pi < pe.Pois.Count; pi++)
                {
                    CommunityPoiEntry item = pe.Pois[pi];
                    if (item.Category == "metal_volcano" ||
                        item.Category == "volcano" ||
                        item.Category == "water_geyser" ||
                        item.Category == "gas_geyser" ||
                        item.Category == "geyser")
                    {
                        string nm = !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : MapPreviewData.GetPoiFriendlyName(item.PrefabId);
                        int prevC;
                        gCount.TryGetValue(nm, out prevC);
                        gCount[nm] = prevC + 1;
                        char prevG;
                        bestGrade.TryGetValue(nm, out prevG);
                        if (GradeRank(item.Grade) > GradeRank(prevG))
                            bestGrade[nm] = item.Grade;
                    }
                }
                if (gCount.Count > 0)
                {
                    List<string> gps = new List<string>();
                    foreach (KeyValuePair<string, int> gkv in gCount)
                    {
                        char bg = bestGrade.ContainsKey(gkv.Key) ? bestGrade[gkv.Key] : ' ';
                        string gSuffix = (bg == 'S' || bg == 'A' || bg == 'B' || bg == 'C') ? ("(" + bg + ")") : "";
                        gps.Add((gkv.Value > 1 ? (gkv.Key + "×" + gkv.Value) : gkv.Key) + gSuffix);
                    }
                    string wn = !string.IsNullOrEmpty(pe.WorldName) ? pe.WorldName : ("行星#" + pe.WorldIndex);
                    poiParts.Add(wn + "：" + string.Join("、", gps.ToArray()));
                }
            }
            if (poiParts.Count > 0)
                ent.PoiSummary = string.Join("；", poiParts.ToArray());
        }

        static Dictionary<string, CommunitySeedEntry> EnsureShardLoadedLocked(
            Dictionary<string, Dictionary<string, CommunitySeedEntry>> store,
            string dirPath,
            string prefix)
        {
            Dictionary<string, CommunitySeedEntry> map;
            if (store.TryGetValue(prefix, out map)) return map;
            map = new Dictionary<string, CommunitySeedEntry>(StringComparer.Ordinal);
            string fpath = Path.Combine(dirPath, prefix + ".json");
            List<CommunitySeedEntry> disk = LoadClusterSeedsFromFile(fpath);
            for (int i = 0; i < disk.Count; i++)
            {
                string dk = DedupKey(disk[i]);
                CommunitySeedEntry ex;
                if (map.TryGetValue(dk, out ex)) MergeIntoEntry(ex, disk[i]);
                else map[dk] = disk[i];
            }
            store[prefix] = map;
            return map;
        }

        static void EnsureAllPendingLoadedLocked()
        {
            if (_pendingLoadedAll) return;
            _pendingLoadedAll = true;
            try
            {
                if (!Directory.Exists(PendingUploadDir)) return;
                string[] files = Directory.GetFiles(PendingUploadDir, "*.json");
                for (int i = 0; i < files.Length; i++)
                {
                    string pref = Path.GetFileNameWithoutExtension(files[i]).ToUpperInvariant();
                    if (IsValidClusterPrefix(pref))
                        EnsureShardLoadedLocked(_pendingMem, PendingUploadDir, pref);
                }
            }
            catch { }
        }

        public static int GetPendingUploadCount()
        {
            lock (_dbLock)
            {
                EnsureAllPendingLoadedLocked();
                int total = 0;
                foreach (Dictionary<string, CommunitySeedEntry> m in _pendingMem.Values)
                    total += m.Count;
                return total;
            }
        }

        public static Dictionary<string, int> GetPendingCountsByCluster()
        {
            lock (_dbLock)
            {
                EnsureAllPendingLoadedLocked();
                Dictionary<string, int> res = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, Dictionary<string, CommunitySeedEntry>> kv in _pendingMem)
                {
                    if (kv.Value.Count > 0) res[kv.Key] = kv.Value.Count;
                }
                return res;
            }
        }

        public static List<CommunitySeedEntry> LoadAllPendingSeeds()
        {
            FlushPendingToDisk();
            lock (_dbLock)
            {
                EnsureAllPendingLoadedLocked();
                List<CommunitySeedEntry> all = new List<CommunitySeedEntry>();
                foreach (KeyValuePair<string, Dictionary<string, CommunitySeedEntry>> kv in _pendingMem)
                {
                    foreach (CommunitySeedEntry e in kv.Value.Values)
                        all.Add(CloneEntry(e));
                }
                all.Sort((a, b) =>
                {
                    int c = string.Compare(a.ClusterPrefix, b.ClusterPrefix, StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : a.Seed.CompareTo(b.Seed);
                });
                return all;
            }
        }

        public static int GetCachedTotalCount()
        {
            HashSet<string> prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(DataCacheDir))
                {
                    string[] fs = Directory.GetFiles(DataCacheDir, "*.json");
                    for (int i = 0; i < fs.Length; i++)
                        prefixes.Add(Path.GetFileNameWithoutExtension(fs[i]).ToUpperInvariant());
                }
                if (Directory.Exists(LocalRecordedDir))
                {
                    string[] fs = Directory.GetFiles(LocalRecordedDir, "*.json");
                    for (int i = 0; i < fs.Length; i++)
                        prefixes.Add(Path.GetFileNameWithoutExtension(fs[i]).ToUpperInvariant());
                }
            }
            catch { }
            lock (_dbLock)
            {
                foreach (string k in _localMem.Keys) prefixes.Add(k.ToUpperInvariant());
            }

            int total = 0;
            foreach (string p in prefixes)
            {
                if (IsValidClusterPrefix(p))
                    total += LoadClusterSeeds(p).Count;
            }
            if (total == 0)
            {
                CommunityManifest m = LoadLocalManifest();
                if (m != null && m.TotalSeeds > 0) return m.TotalSeeds;
            }
            return total;
        }


        public static List<CommunitySeedEntry> LoadClusterSeeds(string clusterPrefix)
        {
            string key = (clusterPrefix ?? "").Trim().ToUpperInvariant();
            if (!IsValidClusterPrefix(key)) return new List<CommunitySeedEntry>();
            lock (_dbLock)
            {
                List<CommunitySeedEntry> cached;
                if (_mergedCache.TryGetValue(key, out cached))
                    return cached;

                Dictionary<string, CommunitySeedEntry> merged =
                    new Dictionary<string, CommunitySeedEntry>(StringComparer.Ordinal);

                string remotePath = Path.Combine(DataCacheDir, key + ".json");
                List<CommunitySeedEntry> rem = LoadClusterSeedsFromFile(remotePath);
                for (int i = 0; i < rem.Count; i++)
                {
                    string dk = DedupKey(rem[i]);
                    CommunitySeedEntry ex;
                    if (merged.TryGetValue(dk, out ex)) MergeIntoEntry(ex, rem[i]);
                    else merged[dk] = rem[i];
                }

                Dictionary<string, CommunitySeedEntry> locMap = EnsureShardLoadedLocked(_localMem, LocalRecordedDir, key);
                foreach (KeyValuePair<string, CommunitySeedEntry> kv in locMap)
                {
                    CommunitySeedEntry ex;
                    if (merged.TryGetValue(kv.Key, out ex)) MergeIntoEntry(ex, kv.Value);
                    else merged[kv.Key] = CloneEntry(kv.Value);
                }

                List<CommunitySeedEntry> list = new List<CommunitySeedEntry>(merged.Values);
                list.Sort((a, b) => a.Seed.CompareTo(b.Seed));
                _mergedCache[key] = list;
                return list;
            }
        }

        public static List<CommunitySeedEntry> LoadClusterSeedsFromFile(string path)
        {
            List<CommunitySeedEntry> result = new List<CommunitySeedEntry>();
            try
            {
                if (!File.Exists(path)) return result;
                string raw = File.ReadAllText(path, Encoding.UTF8);
                JsonVal root = JsonVal.Parse(raw);
                if (root == null || root.Kind != JsonKind.Object) return result;
                JsonVal arr = root.Get("seeds");
                if (arr == null || arr.Kind != JsonKind.Array) return result;
                for (int i = 0; i < arr.Arr.Count; i++)
                {
                    CommunitySeedEntry ent = ParseSeedEntry(arr.Arr[i]);
                    if (ent != null && ent.Seed > 0) result.Add(ent);
                }
            }
            catch { }
            return result;
        }



        static void RecordEntryInternalLocked(CommunitySeedEntry entry)
        {
            if (entry == null || entry.Seed <= 0) return;
            string prefix = entry.GetClusterFileKey();
            if (!IsValidClusterPrefix(prefix)) return;
            entry.ClusterPrefix = prefix;

            Dictionary<string, CommunitySeedEntry> loc = EnsureShardLoadedLocked(_localMem, LocalRecordedDir, prefix);
            Dictionary<string, CommunitySeedEntry> pend = EnsureShardLoadedLocked(_pendingMem, PendingUploadDir, prefix);
            string dk = DedupKey(entry);

            CommunitySeedEntry exLoc;
            if (loc.TryGetValue(dk, out exLoc)) MergeIntoEntry(exLoc, entry);
            else loc[dk] = CloneEntry(entry);

            CommunitySeedEntry exPend;
            if (pend.TryGetValue(dk, out exPend)) MergeIntoEntry(exPend, entry);
            else pend[dk] = CloneEntry(entry);

            _dirtyLocalPrefixes.Add(prefix);
            _dirtyPendingPrefixes.Add(prefix);
            _mergedCache.Remove(prefix);
        }

        public static int RecordScreenedBatchFile(
            string tsvPath,
            ClusterDef cluster,
            string dlcFlags,
            string storyCode,
            string mixingCode,
            string tplCoord,
            WorldGenCatalog catalog)
        {
            if (string.IsNullOrEmpty(tsvPath) || !File.Exists(tsvPath) || cluster == null) return 0;
            string[] lines;
            try { lines = File.ReadAllLines(tsvPath, Encoding.UTF8); }
            catch { return 0; }
            if (lines == null || lines.Length == 0) return 0;

            string normDlc = NormalizeDlcKey(dlcFlags);
            string normStory = NormCode(storyCode);
            string normMix = NormCode(mixingCode);
            string author = string.IsNullOrEmpty(Environment.UserName) ? "Player" : Environment.UserName;
            string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            List<CommunitySeedEntry> parsedEntries = new List<CommunitySeedEntry>(lines.Length);
            for (int li = 0; li < lines.Length; li++)
            {
                string ln = lines[li];
                if (string.IsNullOrEmpty(ln)) continue;
                string[] cols = ln.Split('\t');
                if (cols.Length < 5) continue;

                int seed, wIdx;
                if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed) || seed <= 0) continue;
                if (!int.TryParse(cols[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out wIdx) || wIdx < 0) continue;
                string targetWorldId = cols[2];
                string poiSpec = cols[4];

                List<CommunityPoiEntry> pois = new List<CommunityPoiEntry>();
                if (!string.IsNullOrEmpty(poiSpec))
                {
                    string[] pItems = poiSpec.Split(';');
                    for (int pi = 0; pi < pItems.Length; pi++)
                    {
                        if (string.IsNullOrEmpty(pItems[pi])) continue;
                        string[] seg = pItems[pi].Split('~');
                        if (seg.Length < 8) continue;
                        string pid = seg[0];
                        string cat = seg[1];
                        int px, py, dist;
                        double avg, pct;
                        byte grd;
                        int.TryParse(seg[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out px);
                        int.TryParse(seg[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out py);
                        int.TryParse(seg[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out dist);
                        double.TryParse(seg[5], NumberStyles.Float, CultureInfo.InvariantCulture, out avg);
                        byte.TryParse(seg[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out grd);
                        double.TryParse(seg[7], NumberStyles.Float, CultureInfo.InvariantCulture, out pct);
                        pois.Add(new CommunityPoiEntry
                        {
                            PrefabId = pid,
                            DisplayName = MapPreviewData.GetPoiFriendlyName(pid),
                            Category = cat,
                            X = px,
                            Y = py,
                            DistFromStart = dist,
                            AvgYieldGps = avg,
                            Grade = ByteGradeToChar(grd),
                            Percentile = pct
                        });
                    }
                }

                CommunitySeedEntry ent = BuildMetadataEntry(
                    cluster, seed, tplCoord, normStory, normMix, normDlc, catalog, author, nowIso);

                bool attached = false;
                for (int k = 0; k < ent.Planets.Count; k++)
                {
                    if (ent.Planets[k].WorldIndex == wIdx)
                    {
                        ent.Planets[k].PoisScanned = true;
                        ent.Planets[k].Pois = pois;
                        if (string.IsNullOrEmpty(ent.Planets[k].WorldId)) ent.Planets[k].WorldId = targetWorldId;
                        attached = true;
                        break;
                    }
                }
                if (!attached)
                {
                    ent.Planets.Add(new CommunityPlanetEntry
                    {
                        WorldIndex = wIdx,
                        WorldId = targetWorldId ?? "",
                        WorldName = "行星#" + wIdx,
                        PoisScanned = true,
                        Pois = pois
                    });
                }
                RebuildPoiSummary(ent);
                parsedEntries.Add(ent);
            }

            if (parsedEntries.Count == 0) return 0;
            lock (_dbLock)
            {
                for (int i = 0; i < parsedEntries.Count; i++)
                    RecordEntryInternalLocked(parsedEntries[i]);
            }
            NotifyStateChanged();
            return parsedEntries.Count;
        }

        public static void RecordScreenedMap(
            MapPreviewData md,
            ClusterDef cluster,
            string dlcFlags,
            string storyCode,
            string mixingCode,
            string tplCoord,
            WorldGenCatalog catalog,
            bool flushNow)
        {
            if (md == null || md.Seed <= 0 || cluster == null) return;
            Dictionary<int, MapPreviewData> one = new Dictionary<int, MapPreviewData>();
            one[md.WorldIndex] = md;
            RecordScreenedMaps(one, md.Seed, cluster, dlcFlags, storyCode, mixingCode, tplCoord, catalog, flushNow);
        }

        public static void RecordScreenedMaps(
            Dictionary<int, MapPreviewData> maps,
            int seed,
            ClusterDef cluster,
            string dlcFlags,
            string storyCode,
            string mixingCode,
            string tplCoord,
            WorldGenCatalog catalog,
            bool flushNow)
        {
            if (seed <= 0 || cluster == null) return;
            string author = string.IsNullOrEmpty(Environment.UserName) ? "Player" : Environment.UserName;
            string coord = SeedSearcher.FormatCoordinate(cluster.CoordinatePrefix, seed, tplCoord);
            CommunitySeedEntry ent = BuildEntryFromContext(
                cluster, seed, coord, NormCode(storyCode), NormCode(mixingCode),
                NormalizeDlcKey(dlcFlags), maps, catalog, author, "");
            lock (_dbLock)
            {
                RecordEntryInternalLocked(ent);
            }
            if (flushNow) FlushPendingToDisk();
            else NotifyStateChanged();
        }

        public static void RecordTraitMatchSeed(
            SeedSearchResult res,
            ClusterDef cluster,
            string dlcFlags,
            string storyCode,
            string mixingCode,
            WorldGenCatalog catalog,
            bool flushNow)
        {
            if (res == null || res.Seed <= 0 || cluster == null) return;
            string author = string.IsNullOrEmpty(Environment.UserName) ? "Player" : Environment.UserName;
            string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            CommunitySeedEntry ent = BuildMetadataEntry(
                cluster, res.Seed, "", NormCode(storyCode), NormCode(mixingCode),
                NormalizeDlcKey(dlcFlags), catalog, author, nowIso);
            if (!string.IsNullOrEmpty(res.Coordinate)) ent.Coordinate = res.Coordinate;
            lock (_dbLock)
            {
                RecordEntryInternalLocked(ent);
            }
            if (flushNow) FlushPendingToDisk();
            else NotifyStateChanged();
        }

        static CommunitySeedEntry BuildMetadataEntry(
            ClusterDef cluster,
            int seed,
            string tplCoord,
            string normStory,
            string normMix,
            string normDlc,
            WorldGenCatalog catalog,
            string author,
            string nowIso)
        {
            Dictionary<int, List<WorldTraitDef>> tmap = (catalog != null && cluster != null)
                ? catalog.EvaluateClusterSeed(cluster, seed)
                : null;
            List<string> traitParts = new List<string>();
            List<CommunityPlanetEntry> planets = new List<CommunityPlanetEntry>();

            if (cluster != null)
            {
                List<WorldPlacementDef> ordered = cluster.GetOrderedPlacements();
                for (int i = 0; i < ordered.Count; i++)
                {
                    WorldPlacementDef wp = ordered[i];
                    CommunityPlanetEntry pe = new CommunityPlanetEntry
                    {
                        WorldIndex = wp.PlacementIndex,
                        WorldId = wp.WorldId ?? "",
                        WorldName = wp.World != null ? wp.World.DisplayName : (wp.WorldId ?? ""),
                        PoisScanned = false
                    };
                    List<WorldTraitDef> wts;
                    if (tmap != null && tmap.TryGetValue(wp.PlacementIndex, out wts) && wts != null)
                    {
                        for (int k = 0; k < wts.Count; k++)
                        {
                            pe.Traits.Add(wts[k].DisplayName);
                            pe.TraitIds.Add(wts[k].Id);
                        }
                        if (wts.Count > 0)
                            traitParts.Add(string.Format("[{0}] {1}：{2}", wp.GroupLabel(), pe.WorldName, string.Join("、", pe.Traits.ToArray())));
                    }
                    planets.Add(pe);
                }
            }

            string suffix = !string.IsNullOrEmpty(tplCoord) ? tplCoord : ("0-" + normStory + "-" + normMix);
            return new CommunitySeedEntry
            {
                SchemaVersion = 1,
                ClusterPrefix = cluster != null ? cluster.CoordinatePrefix : "",
                ClusterPath = cluster != null ? cluster.FilePath : "",
                ClusterName = cluster != null ? cluster.DisplayName : "",
                Seed = seed,
                Coordinate = SeedSearcher.FormatCoordinate(cluster != null ? cluster.CoordinatePrefix : "", seed, suffix),
                StoryCode = normStory,
                MixingCode = normMix,
                DlcFlags = normDlc,
                Author = author,
                SubmittedAt = nowIso,
                TraitSummary = string.Join(" | ", traitParts.ToArray()),
                PoiSummary = "",
                Note = "",
                Planets = planets
            };
        }

        public static void FlushPendingToDisk()
        {
            Dictionary<string, List<CommunitySeedEntry>> locToSave = null;
            Dictionary<string, List<CommunitySeedEntry>> pendToSave = null;
            HashSet<string> emptyPendingToDelete = null;

            lock (_dbLock)
            {
                if (_dirtyLocalPrefixes.Count > 0)
                {
                    locToSave = new Dictionary<string, List<CommunitySeedEntry>>(StringComparer.OrdinalIgnoreCase);
                    foreach (string pref in _dirtyLocalPrefixes)
                    {
                        Dictionary<string, CommunitySeedEntry> m;
                        if (_localMem.TryGetValue(pref, out m))
                        {
                            List<CommunitySeedEntry> list = new List<CommunitySeedEntry>(m.Values);
                            list.Sort((a, b) => a.Seed.CompareTo(b.Seed));
                            locToSave[pref] = list;
                        }
                    }
                    _dirtyLocalPrefixes.Clear();
                }
                if (_dirtyPendingPrefixes.Count > 0)
                {
                    pendToSave = new Dictionary<string, List<CommunitySeedEntry>>(StringComparer.OrdinalIgnoreCase);
                    emptyPendingToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string pref in _dirtyPendingPrefixes)
                    {
                        Dictionary<string, CommunitySeedEntry> m;
                        if (_pendingMem.TryGetValue(pref, out m) && m.Count > 0)
                        {
                            List<CommunitySeedEntry> list = new List<CommunitySeedEntry>(m.Values);
                            list.Sort((a, b) => a.Seed.CompareTo(b.Seed));
                            pendToSave[pref] = list;
                        }
                        else
                        {
                            emptyPendingToDelete.Add(pref);
                        }
                    }
                    _dirtyPendingPrefixes.Clear();
                }
            }

            try
            {
                if (locToSave != null)
                {
                    Directory.CreateDirectory(LocalRecordedDir);
                    foreach (KeyValuePair<string, List<CommunitySeedEntry>> kv in locToSave)
                    {
                        string p = Path.Combine(LocalRecordedDir, kv.Key + ".json");
                        WriteBytesAtomic(p, Encoding.UTF8.GetBytes(SerializeClusterDocCompact(kv.Key, kv.Value)));
                    }
                }
                if (pendToSave != null)
                {
                    Directory.CreateDirectory(PendingUploadDir);
                    foreach (KeyValuePair<string, List<CommunitySeedEntry>> kv in pendToSave)
                    {
                        string p = Path.Combine(PendingUploadDir, kv.Key + ".json");
                        WriteBytesAtomic(p, Encoding.UTF8.GetBytes(SerializeClusterDocCompact(kv.Key, kv.Value)));
                    }
                }
                if (emptyPendingToDelete != null)
                {
                    foreach (string pref in emptyPendingToDelete)
                    {
                        string p = Path.Combine(PendingUploadDir, pref + ".json");
                        if (File.Exists(p)) File.Delete(p);
                    }
                }
            }
            catch { }
            NotifyStateChanged();
        }



        static bool MatchesEntryTraitConstraints(
            WorldGenCatalog catalog,
            ClusterDef cluster,
            int seed,
            List<PlanetConstraint> constraints,
            bool hasTraitConstraint,
            out Dictionary<int, List<WorldTraitDef>> evalTraits)
        {
            evalTraits = (catalog != null && cluster != null && seed > 0)
                ? catalog.EvaluateClusterSeed(cluster, seed)
                : null;
            if (!hasTraitConstraint || constraints == null || evalTraits == null)
                return true;

            for (int ci = 0; ci < constraints.Count; ci++)
            {
                PlanetConstraint pc = constraints[ci];
                if (pc.RequiredMask == 0 && pc.ForbiddenMask == 0 && !pc.RequireNoTraits) continue;
                List<WorldTraitDef> got;
                if (!evalTraits.TryGetValue(pc.PlacementIndex, out got)) got = new List<WorldTraitDef>();
                ulong gotMask = 0;
                for (int k = 0; k < got.Count; k++) gotMask |= (1UL << got[k].Index);
                if (pc.RequireNoTraits && got.Count != 0) return false;
                if ((gotMask & pc.RequiredMask) != pc.RequiredMask) return false;
                if ((gotMask & pc.ForbiddenMask) != 0) return false;
            }
            return true;
        }

        static CommunityPlanetEntry FindScannedPlanet(CommunitySeedEntry ent, int worldIndex)
        {
            if (ent == null || ent.Planets == null) return null;
            for (int i = 0; i < ent.Planets.Count; i++)
            {
                CommunityPlanetEntry p = ent.Planets[i];
                if (p.WorldIndex == worldIndex && (p.PoisScanned || p.Pois.Count > 0))
                    return p;
            }
            return null;
        }

        public static bool TryFastIndexMatch(
            WorldGenCatalog catalog,
            ClusterDef cluster,
            List<PlanetConstraint> constraints,
            bool hasTraitConstraint,
            List<KeyValuePair<int, List<PoiFilterRule>>> orderedPoiGroups,
            string dlcCsv,
            string mixingCode,
            string tplCoord,
            int minSeedExclusive,
            out SeedSearchResult matchedResult,
            out Dictionary<int, MapPreviewData> synthMaps,
            out int totalIndexMatches)
        {
            matchedResult = null;
            synthMaps = null;
            totalIndexMatches = 0;
            if (cluster == null || catalog == null) return false;

            List<CommunitySeedEntry> entries = LoadClusterSeeds(cluster.CoordinatePrefix);
            if (entries.Count == 0) return false;

            string wantMix = NormCode(mixingCode);
            string wantDlc = NormalizeWorldGenDlcKey(dlcCsv);
            bool needPoi = orderedPoiGroups != null && orderedPoiGroups.Count > 0;

            CommunitySeedEntry chosen = null;
            Dictionary<int, List<WorldTraitDef>> chosenTraits = null;

            for (int i = 0; i < entries.Count; i++)
            {
                CommunitySeedEntry ent = entries[i];
                if (ent.Seed <= 0) continue;
                if (cluster.FixedCoordinate > 0 && ent.Seed != cluster.FixedCoordinate) continue;
                if (!string.Equals(NormCode(ent.MixingCode), wantMix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (needPoi && !string.IsNullOrEmpty(ent.DlcFlags))
                {
                    if (!string.Equals(NormalizeWorldGenDlcKey(ent.DlcFlags), wantDlc, StringComparison.Ordinal))
                        continue;
                }

                Dictionary<int, List<WorldTraitDef>> evalTraits;
                if (!MatchesEntryTraitConstraints(catalog, cluster, ent.Seed, constraints, hasTraitConstraint, out evalTraits))
                    continue;

                if (needPoi)
                {
                    bool allPoiOk = true;
                    for (int gi = 0; gi < orderedPoiGroups.Count; gi++)
                    {
                        KeyValuePair<int, List<PoiFilterRule>> grp = orderedPoiGroups[gi];
                        CommunityPlanetEntry pe = FindScannedPlanet(ent, grp.Key);
                        if (pe == null) { allPoiOk = false; break; }
                        for (int ri = 0; ri < grp.Value.Count; ri++)
                        {
                            if (!MatchesPoiRule(pe, grp.Value[ri]))
                            {
                                allPoiOk = false;
                                break;
                            }
                        }
                        if (!allPoiOk) break;
                    }
                    if (!allPoiOk) continue;
                }

                totalIndexMatches++;
                if (chosen == null && ent.Seed > minSeedExclusive)
                {
                    chosen = ent;
                    chosenTraits = evalTraits;
                }
            }

            if (chosen == null) return false;

            synthMaps = new Dictionary<int, MapPreviewData>();
            for (int i = 0; i < chosen.Planets.Count; i++)
            {
                CommunityPlanetEntry pe = chosen.Planets[i];
                if (!pe.PoisScanned && pe.Pois.Count == 0) continue;
                MapPreviewData md = new MapPreviewData
                {
                    ClusterFilePath = cluster.FilePath,
                    Seed = chosen.Seed,
                    WorldIndex = pe.WorldIndex,
                    WorldName = !string.IsNullOrEmpty(pe.WorldName) ? pe.WorldName : ("行星#" + pe.WorldIndex),
                    IsPoiOnly = true,
                    TraitsSummary = pe.Traits.Count > 0 ? string.Join("、", pe.Traits.ToArray()) : "无特质"
                };
                for (int k = 0; k < pe.Pois.Count; k++)
                {
                    CommunityPoiEntry cp = pe.Pois[k];
                    bool hasStats = cp.Grade == 'S' || cp.Grade == 'A' || cp.Grade == 'B' || cp.Grade == 'C' || cp.AvgYieldGps > 0.001;
                    md.Pois.Add(new MapPoiItem
                    {
                        PrefabId = cp.PrefabId,
                        DisplayName = !string.IsNullOrEmpty(cp.DisplayName) ? cp.DisplayName : MapPreviewData.GetPoiFriendlyName(cp.PrefabId),
                        Category = cp.Category,
                        X = cp.X,
                        Y = cp.Y,
                        DistFromStart = cp.DistFromStart,
                        HasGeyserStats = hasStats,
                        AvgYieldGps = (float)cp.AvgYieldGps,
                        Grade = CharGradeToByte(cp.Grade),
                        Percentile = (float)cp.Percentile
                    });
                }
                synthMaps[pe.WorldIndex] = md;
            }

            matchedResult = new SeedSearchResult
            {
                Seed = chosen.Seed,
                Coordinate = SeedSearcher.FormatCoordinate(cluster.CoordinatePrefix, chosen.Seed, tplCoord),
                SeedsTested = entries.Count,
                ElapsedMs = 0.0,
                PlanetTraits = chosenTraits ?? catalog.EvaluateClusterSeed(cluster, chosen.Seed)
            };
            return true;
        }


        public static void CollectIndexAcceleration(
            WorldGenCatalog catalog,
            ClusterDef cluster,
            List<PlanetConstraint> constraints,
            bool hasTraitConstraint,
            List<KeyValuePair<int, List<PoiFilterRule>>> orderedPoiGroups,
            string dlcCsv,
            string mixingCode,
            int minSeedInclusive,
            out List<int> partialPrimaryPassSeeds,
            out HashSet<int> skipPrimarySeeds)
        {
            partialPrimaryPassSeeds = new List<int>();
            skipPrimarySeeds = new HashSet<int>();
            if (cluster == null || orderedPoiGroups == null || orderedPoiGroups.Count == 0) return;

            List<CommunitySeedEntry> entries = LoadClusterSeeds(cluster.CoordinatePrefix);
            if (entries.Count == 0) return;

            string wantMix = NormCode(mixingCode);
            string wantDlc = NormalizeWorldGenDlcKey(dlcCsv);
            int primaryWorldIdx = orderedPoiGroups[0].Key;
            List<PoiFilterRule> primaryRules = orderedPoiGroups[0].Value;

            for (int i = 0; i < entries.Count; i++)
            {
                CommunitySeedEntry ent = entries[i];
                if (ent.Seed <= 0) continue;
                if (!string.Equals(NormCode(ent.MixingCode), wantMix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(ent.DlcFlags) &&
                    !string.Equals(NormalizeWorldGenDlcKey(ent.DlcFlags), wantDlc, StringComparison.Ordinal))
                    continue;

                CommunityPlanetEntry p0 = FindScannedPlanet(ent, primaryWorldIdx);
                if (p0 == null) continue;


                skipPrimarySeeds.Add(ent.Seed);

                if (ent.Seed < minSeedInclusive || orderedPoiGroups.Count <= 1) continue;

                Dictionary<int, List<WorldTraitDef>> evalTraits;
                if (!MatchesEntryTraitConstraints(catalog, cluster, ent.Seed, constraints, hasTraitConstraint, out evalTraits))
                    continue;

                bool p0Ok = true;
                for (int ri = 0; ri < primaryRules.Count; ri++)
                {
                    if (!MatchesPoiRule(p0, primaryRules[ri])) { p0Ok = false; break; }
                }
                if (!p0Ok) continue;


                bool secKnownFail = false;
                bool hasMissingPlanet = false;
                for (int gi = 1; gi < orderedPoiGroups.Count; gi++)
                {
                    CommunityPlanetEntry sp = FindScannedPlanet(ent, orderedPoiGroups[gi].Key);
                    if (sp == null) { hasMissingPlanet = true; continue; }
                    for (int ri = 0; ri < orderedPoiGroups[gi].Value.Count; ri++)
                    {
                        if (!MatchesPoiRule(sp, orderedPoiGroups[gi].Value[ri])) { secKnownFail = true; break; }
                    }
                    if (secKnownFail) break;
                }
                if (!secKnownFail && hasMissingPlanet)
                    partialPrimaryPassSeeds.Add(ent.Seed);
            }
        }



        public static string BuildCompressedBatchPayload(List<CommunitySeedEntry> seeds)
        {
            string json = BuildBatchSubmissionJson(seeds);
            byte[] raw = Encoding.UTF8.GetBytes(json);
            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipStream gz = new GZipStream(ms, CompressionMode.Compress, true))
                {
                    gz.Write(raw, 0, raw.Length);
                }
                string b64 = Convert.ToBase64String(ms.ToArray());
                return "[seed-db-gzip]\n" + b64 + "\n[/seed-db-gzip]\n";
            }
        }

        public static string BuildBatchSubmissionJson(List<CommunitySeedEntry> seeds)
        {
            StringBuilder sb = new StringBuilder((seeds != null ? seeds.Count : 1) * 420 + 64);
            sb.Append("{\"schemaVersion\":1,\"seeds\":[\n");
            if (seeds != null)
            {
                for (int i = 0; i < seeds.Count; i++)
                {
                    AppendSeedJsonCompact(sb, seeds[i]);
                    if (i < seeds.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static string BuildBatchBrowserIssueUrl(
            string repo,
            List<CommunitySeedEntry> seeds,
            out string clipboardPayload,
            out bool bodyTooLongForUrl)
        {
            string cleanRepo = IsValidRepoSlug(repo) ? repo.Trim() : "3571949306/ONI-Seed-DB";
            int n = seeds != null ? seeds.Count : 0;
            string title = string.Format(CultureInfo.InvariantCulture, "[seed-batch] 批量上传 {0} 条筛选种子索引", n);
            clipboardPayload = BuildCompressedBatchPayload(seeds);

            string fullUrl = string.Format("https://github.com/{0}/issues/new?labels=seed-submission&title={1}&body={2}",
                cleanRepo, Uri.EscapeDataString(title), Uri.EscapeDataString(clipboardPayload));

            if (fullUrl.Length <= 7200)
            {
                bodyTooLongForUrl = false;
                return fullUrl;
            }

            bodyTooLongForUrl = true;
            string shortBody = "<!-- 压缩索引数据包已自动复制到剪贴板，请直接按 Ctrl+V 粘贴到下方空白处后点击 Submit new issue -->\n\n";
            return string.Format("https://github.com/{0}/issues/new?labels=seed-submission&title={1}&body={2}",
                cleanRepo, Uri.EscapeDataString(title), Uri.EscapeDataString(shortBody));
        }

        public static bool UploadAllPendingViaGitHubApi(
            string repo,
            string token,
            string author,
            Action<string> onProgress,
            out int uploadedCount,
            out List<string> issueUrls,
            out string err)
        {
            uploadedCount = 0;
            issueUrls = new List<string>();
            err = null;

            AppUpdater.EnsureTls12();
            if (!IsValidRepoSlug(repo)) { err = "仓库格式无效，请使用 owner/repo 格式。"; return false; }
            if (string.IsNullOrEmpty(token)) { err = "未配置 GitHub Personal Access Token。"; return false; }

            List<CommunitySeedEntry> allPending = LoadAllPendingSeeds();
            if (allPending.Count == 0) { err = "当前没有待上传的种子记录。"; return false; }

            string cleanAuthor = string.IsNullOrEmpty(author) ? "Player" : author.Trim();
            for (int i = 0; i < allPending.Count; i++)
                allPending[i].Author = cleanAuthor;

            const int chunkSize = 400;
            int totalChunks = (allPending.Count + chunkSize - 1) / chunkSize;

            for (int ci = 0; ci < totalChunks; ci++)
            {
                int start = ci * chunkSize;
                int count = Math.Min(chunkSize, allPending.Count - start);
                List<CommunitySeedEntry> chunk = allPending.GetRange(start, count);

                if (onProgress != null)
                    onProgress(string.Format("正在上传第 {0}/{1} 批（{2} 条种子记录）…", ci + 1, totalChunks, count));

                string gzBody = BuildCompressedBatchPayload(chunk);
                string title = totalChunks == 1
                    ? string.Format(CultureInfo.InvariantCulture, "[seed-batch] {0} 贡献 {1} 条筛选索引", cleanAuthor, count)
                    : string.Format(CultureInfo.InvariantCulture, "[seed-batch] {0} 贡献 {1} 条筛选索引 ({2}/{3})", cleanAuthor, count, ci + 1, totalChunks);

                string iUrl, iErr;
                if (!PostSubmissionIssue(repo, token, title, gzBody, out iUrl, out iErr))
                {
                    err = iErr;
                    FlushPendingToDisk();
                    return false;
                }
                if (!string.IsNullOrEmpty(iUrl)) issueUrls.Add(iUrl);
                RemoveUploadedFromPending(chunk);
                uploadedCount += count;
            }

            FlushPendingToDisk();
            return true;
        }

        public static void ClearAllPendingAfterManualUpload()
        {
            lock (_dbLock)
            {
                EnsureAllPendingLoadedLocked();
                foreach (string pref in _pendingMem.Keys)
                    _dirtyPendingPrefixes.Add(pref);
                _pendingMem.Clear();
            }
            FlushPendingToDisk();
        }

        static void RemoveUploadedFromPending(List<CommunitySeedEntry> uploaded)
        {
            if (uploaded == null || uploaded.Count == 0) return;
            lock (_dbLock)
            {
                for (int i = 0; i < uploaded.Count; i++)
                {
                    string pref = uploaded[i].GetClusterFileKey();
                    Dictionary<string, CommunitySeedEntry> m;
                    if (_pendingMem.TryGetValue(pref, out m))
                    {
                        if (m.Remove(DedupKey(uploaded[i])))
                            _dirtyPendingPrefixes.Add(pref);
                    }
                }
            }
        }

        static bool PostSubmissionIssue(string repo, string token, string title, string body, out string issueUrl, out string err)
        {
            issueUrl = "";
            err = null;
            try
            {
                StringBuilder json = new StringBuilder(body.Length + 256);
                json.Append('{');
                json.Append("\"title\":").Append(JsonStr(title)).Append(',');
                json.Append("\"body\":").Append(JsonStr(body)).Append(',');
                json.Append("\"labels\":[\"seed-submission\"]");
                json.Append('}');

                string apiUrl = string.Format("https://api.github.com/repos/{0}/issues", repo.Trim());
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(apiUrl);
                req.Method = "POST";
                req.Timeout = 20000;
                req.ReadWriteTimeout = 20000;
                req.ContentType = "application/json; charset=utf-8";
                req.UserAgent = "ONI-Trainer/2.0";
                req.Headers.Add("Authorization", "Bearer " + token.Trim());
                byte[] payload = Encoding.UTF8.GetBytes(json.ToString());
                req.ContentLength = payload.Length;
                using (Stream rs = req.GetRequestStream())
                    rs.Write(payload, 0, payload.Length);

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(st, Encoding.UTF8))
                {
                    string rtxt = sr.ReadToEnd();
                    JsonVal rv = JsonVal.Parse(rtxt);
                    if (rv != null) issueUrl = rv.GetStr("html_url");
                    return true;
                }
            }
            catch (WebException wex)
            {
                string msg = wex.Message;
                try
                {
                    if (wex.Response != null)
                        using (StreamReader sr = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8))
                            msg += " " + sr.ReadToEnd();
                }
                catch { }
                err = msg;
                return false;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        public static void TryAutoPullBackground(Action<CommunitySyncResult> onDone)
        {
            if (!TrainerSettings.SeedDbAutoPull) return;
            DateTime? last = GetLastSyncUtc();
            if (last.HasValue && (DateTime.UtcNow - last.Value).TotalHours < 6.0)
                return;
            if (Interlocked.CompareExchange(ref _bgPullRunning, 1, 0) != 0) return;
            Thread t = new Thread(() =>
            {
                try
                {
                    CommunitySyncResult res = SyncNow(false, null);
                    if (onDone != null) onDone(res);
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref _bgPullRunning, 0);
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        public static CommunitySyncResult SyncNow(bool forceAll, Action<string> onStatus)
        {
            CommunitySyncResult res = new CommunitySyncResult();
            lock (_syncLock)
            {
                AppUpdater.EnsureTls12();
                string repo = (TrainerSettings.SeedDbRepo ?? "").Trim();
                string branch = (TrainerSettings.SeedDbBranch ?? "main").Trim();
                string mirror = (TrainerSettings.SeedDbMirror ?? "").Trim();
                if (!IsValidRepoSlug(repo))
                {
                    res.Error = "仓库地址格式无效，请使用 owner/repo 格式（例如 3571949306/ONI-Seed-DB）。";
                    return res;
                }
                if (string.IsNullOrEmpty(branch)) branch = "main";

                if (onStatus != null) onStatus("正在拉取社区库索引 manifest.json…");

                string manifestErr;
                string usedSource;
                byte[] manifestBytes = DownloadRelPath("manifest.json", repo, branch, mirror, null, out usedSource, out manifestErr);
                if (manifestBytes == null)
                {
                    res.Error = string.IsNullOrEmpty(manifestErr) ? "无法连接社区仓库获取索引。" : manifestErr;
                    return res;
                }

                CommunityManifest manifest;
                try
                {
                    manifest = ParseManifestJson(Encoding.UTF8.GetString(manifestBytes));
                }
                catch (Exception ex)
                {
                    res.Error = "解析 manifest.json 失败：" + ex.Message;
                    return res;
                }

                Directory.CreateDirectory(CacheDir);
                Directory.CreateDirectory(DataCacheDir);

                int updated = 0;
                for (int i = 0; i < manifest.Clusters.Count; i++)
                {
                    CommunityManifestItem item = manifest.Clusters[i];
                    if (!IsValidClusterPrefix(item.ClusterPrefix)) continue;

                    string localPath = Path.Combine(DataCacheDir, item.ClusterPrefix + ".json");
                    bool needDownload = forceAll || !File.Exists(localPath);
                    if (!needDownload && !string.IsNullOrEmpty(item.Sha256))
                    {
                        try
                        {
                            byte[] curBytes = File.ReadAllBytes(localPath);
                            string curSha = ComputeSha256Hex(curBytes);
                            if (!string.Equals(curSha, item.Sha256, StringComparison.OrdinalIgnoreCase))
                                needDownload = true;
                        }
                        catch { needDownload = true; }
                    }
                    if (!needDownload) continue;

                    if (onStatus != null)
                        onStatus(string.Format("同步分片 {0} ({1}/{2})…", item.ClusterPrefix, i + 1, manifest.Clusters.Count));

                    string rel = string.IsNullOrEmpty(item.File) ? ("data/" + item.ClusterPrefix + ".json") : item.File.TrimStart('/');
                    string cErr;
                    string cSrc;
                    byte[] clusterBytes = DownloadRelPath(rel, repo, branch, mirror, usedSource, out cSrc, out cErr);
                    if (clusterBytes == null)
                    {
                        res.Error = string.Format("下载分片 {0} 失败：{1}", item.ClusterPrefix, cErr ?? "网络异常");
                        return res;
                    }

                    if (!string.IsNullOrEmpty(item.Sha256))
                    {
                        string gotSha = ComputeSha256Hex(clusterBytes);
                        if (!string.Equals(gotSha, item.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            res.Error = string.Format("分片 {0} SHA-256 校验失败（文件可能已损坏或缓存未刷新）。", item.ClusterPrefix);
                            return res;
                        }
                    }

                    WriteBytesAtomic(localPath, clusterBytes);
                    lock (_dbLock) { _mergedCache.Remove(item.ClusterPrefix.ToUpperInvariant()); }
                    updated++;
                }

                WriteBytesAtomic(Path.Combine(CacheDir, "manifest.json"), manifestBytes);
                WriteLastSyncUtc(DateTime.UtcNow);

                res.Success = true;
                res.UpdatedClusters = updated;
                res.TotalSeeds = manifest.TotalSeeds;
                res.UsedSource = usedSource ?? "";
                res.Manifest = manifest;
                NotifyStateChanged();
                return res;
            }
        }

        static List<string> BuildCandidateUrls(string relPath, string repo, string branch, string mirror, string preferredBase)
        {
            List<string> urls = new List<string>();
            string cleanRel = relPath.TrimStart('/');
            string rawDirect = string.Format("https://raw.githubusercontent.com/{0}/{1}/{2}", repo, branch, cleanRel);
            string jsdUrl = string.Format("https://cdn.jsdelivr.net/gh/{0}@{1}/{2}", repo, branch, cleanRel);

            if (!string.IsNullOrEmpty(mirror))
            {
                string m = mirror.Trim();
                if (m.IndexOf("{url}", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    urls.Add(m.Replace("{url}", Uri.EscapeUriString(rawDirect)));
                }
                else if (m.IndexOf("{path}", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    urls.Add(m.Replace("{repo}", repo).Replace("{branch}", branch).Replace("{path}", cleanRel));
                }
                else
                {
                    string mTrim = m.TrimEnd('/');
                    bool isLocal = mTrim.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                                   mTrim.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase);
                    urls.Add(mTrim + "/" + cleanRel);
                    if (!isLocal)
                        urls.Add(mTrim + "/" + rawDirect);
                }
            }

            if (!string.IsNullOrEmpty(preferredBase) && string.Equals(preferredBase, "jsdelivr", StringComparison.Ordinal))
            {
                urls.Add(jsdUrl);
                urls.Add(rawDirect);
            }
            else
            {
                urls.Add(rawDirect);
                urls.Add(jsdUrl);
            }
            return urls;
        }

        static byte[] DownloadRelPath(
            string relPath, string repo, string branch, string mirror, string preferredSource,
            out string usedSource, out string err)
        {
            usedSource = "";
            err = null;
            List<string> urls = BuildCandidateUrls(relPath, repo, branch, mirror, preferredSource);
            StringBuilder errs = new StringBuilder();
            for (int i = 0; i < urls.Count; i++)
            {
                string u = urls[i];
                string e;
                byte[] bytes = HttpGetBytes(u, 12000, out e);
                if (bytes != null && bytes.Length > 0)
                {
                    if (u.IndexOf("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase) >= 0)
                        usedSource = "jsdelivr";
                    else if (u.IndexOf("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) >= 0 &&
                             string.IsNullOrEmpty(mirror))
                        usedSource = "GitHub Raw";
                    else
                        usedSource = "镜像源";
                    return bytes;
                }
                if (errs.Length > 0) errs.Append("; ");
                errs.Append(e ?? "fail");
            }
            err = errs.ToString();
            return null;
        }

        static byte[] HttpGetBytes(string url, int timeoutMs, out string err)
        {
            err = null;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "ONI-Trainer/2.0";
                req.Headers.Add("Cache-Control", "no-cache");
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[16384];
                    int n;
                    while ((n = st.Read(buf, 0, buf.Length)) > 0)
                    {
                        ms.Write(buf, 0, n);
                        if (ms.Length > 64 * 1024 * 1024)
                            throw new InvalidOperationException("响应体积过大 (>64MB)");
                    }
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return null;
            }
        }

        static void WriteBytesAtomic(string targetPath, byte[] data)
        {
            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string tmp = targetPath + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(tmp, targetPath);
        }

        static string ExtractPayloadJsonText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            Match gz = Regex.Match(raw, @"\[seed-db-gzip\]\s*([\s\S]*?)\s*\[/seed-db-gzip\]", RegexOptions.IgnoreCase);
            if (gz.Success)
            {
                string b64 = Regex.Replace(gz.Groups[1].Value, @"\s+", "");
                byte[] comp = Convert.FromBase64String(b64);
                using (MemoryStream msIn = new MemoryStream(comp))
                using (GZipStream gzs = new GZipStream(msIn, CompressionMode.Decompress))
                using (MemoryStream msOut = new MemoryStream())
                {
                    byte[] buf = new byte[16384];
                    int n;
                    while ((n = gzs.Read(buf, 0, buf.Length)) > 0)
                        msOut.Write(buf, 0, n);
                    return Encoding.UTF8.GetString(msOut.ToArray());
                }
            }
            Match jm = Regex.Match(raw, @"\[seed-db-json\]\s*([\s\S]*?)\s*\[/seed-db-json\]", RegexOptions.IgnoreCase);
            if (jm.Success) return jm.Groups[1].Value.Trim();
            return raw;
        }

        public static int ImportLocalJsonFile(string filePath, out string err)
        {
            err = null;
            try
            {
                string rawFile = File.ReadAllText(filePath, Encoding.UTF8);
                string raw = ExtractPayloadJsonText(rawFile);
                JsonVal root = JsonVal.Parse(raw);
                if (root == null || root.Kind != JsonKind.Object)
                {
                    err = "JSON 根节点必须是对象";
                    return 0;
                }

                List<CommunitySeedEntry> imported = new List<CommunitySeedEntry>();
                JsonVal arr = root.Get("seeds");
                if (arr != null && arr.Kind == JsonKind.Array)
                {
                    for (int i = 0; i < arr.Arr.Count; i++)
                    {
                        CommunitySeedEntry ent = ParseSeedEntry(arr.Arr[i]);
                        if (ent != null && ent.Seed > 0) imported.Add(ent);
                    }
                }
                else
                {
                    CommunitySeedEntry single = ParseSeedEntry(root);
                    if (single != null && single.Seed > 0) imported.Add(single);
                }

                if (imported.Count == 0)
                {
                    err = "文件中未包含有效种子记录";
                    return 0;
                }

                Dictionary<string, List<CommunitySeedEntry>> byPrefix =
                    new Dictionary<string, List<CommunitySeedEntry>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < imported.Count; i++)
                {
                    string key = imported[i].GetClusterFileKey();
                    if (string.IsNullOrEmpty(key)) continue;
                    List<CommunitySeedEntry> list;
                    if (!byPrefix.TryGetValue(key, out list))
                    {
                        list = new List<CommunitySeedEntry>();
                        byPrefix[key] = list;
                    }
                    list.Add(imported[i]);
                }

                if (byPrefix.Count == 0)
                {
                    err = "记录缺少有效的 clusterPrefix 或坐标前缀";
                    return 0;
                }

                Directory.CreateDirectory(CacheDir);
                Directory.CreateDirectory(DataCacheDir);
                int totalMerged = 0;
                foreach (KeyValuePair<string, List<CommunitySeedEntry>> kv in byPrefix)
                {
                    string uKey = kv.Key.ToUpperInvariant();
                    string cpath = Path.Combine(DataCacheDir, uKey + ".json");
                    List<CommunitySeedEntry> existing = LoadClusterSeedsFromFile(cpath);
                    Dictionary<string, int> keyIdx = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (int i = 0; i < existing.Count; i++)
                        keyIdx[DedupKey(existing[i])] = i;

                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        CommunitySeedEntry inc = kv.Value[i];
                        inc.ClusterPrefix = uKey;
                        string dk = DedupKey(inc);
                        int pos;
                        if (keyIdx.TryGetValue(dk, out pos))
                        {
                            MergeIntoEntry(existing[pos], inc);
                        }
                        else
                        {
                            keyIdx[dk] = existing.Count;
                            existing.Add(inc);
                        }
                        totalMerged++;
                    }
                    existing.Sort((a, b) => a.Seed.CompareTo(b.Seed));
                    string outJson = SerializeClusterDocCompact(uKey, existing);
                    WriteBytesAtomic(cpath, Encoding.UTF8.GetBytes(outJson));
                    lock (_dbLock) { _mergedCache.Remove(uKey); }
                }

                UpdateLocalManifestCounts();
                NotifyStateChanged();
                return totalMerged;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return 0;
            }
        }

        static void UpdateLocalManifestCounts()
        {
            try
            {
                CommunityManifest man = LoadLocalManifest();
                string[] files = Directory.GetFiles(DataCacheDir, "*.json");
                int total = 0;
                List<CommunityManifestItem> items = new List<CommunityManifestItem>();
                for (int i = 0; i < files.Length; i++)
                {
                    string pref = Path.GetFileNameWithoutExtension(files[i]).ToUpperInvariant();
                    if (!IsValidClusterPrefix(pref)) continue;
                    List<CommunitySeedEntry> list = LoadClusterSeedsFromFile(files[i]);
                    byte[] raw = File.ReadAllBytes(files[i]);
                    total += list.Count;
                    items.Add(new CommunityManifestItem
                    {
                        ClusterPrefix = pref,
                        File = "data/" + pref + ".json",
                        Count = list.Count,
                        Sha256 = ComputeSha256Hex(raw)
                    });
                }
                man.TotalSeeds = total;
                man.UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                man.Clusters = items;
                string mJson = SerializeManifest(man);
                WriteBytesAtomic(Path.Combine(CacheDir, "manifest.json"), Encoding.UTF8.GetBytes(mJson));
            }
            catch { }
        }

        public static CommunitySeedEntry BuildEntryFromContext(
            ClusterDef cluster,
            int seed,
            string coord,
            string storyCode,
            string mixingCode,
            string dlcFlags,
            Dictionary<int, MapPreviewData> maps,
            WorldGenCatalog catalog,
            string author,
            string note)
        {
            string prefix = cluster != null ? cluster.CoordinatePrefix : "";
            string cPath = cluster != null ? cluster.FilePath : "";
            string cName = cluster != null ? cluster.DisplayName : "";
            if (string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(coord))
            {
                Match m = Regex.Match(coord.Trim().ToUpperInvariant(), @"^([A-Z0-9-]{2,24})-(\d+)");
                if (m.Success) prefix = m.Groups[1].Value;
            }

            Dictionary<int, List<WorldTraitDef>> tmap = null;
            if (catalog != null && cluster != null && seed > 0)
                tmap = catalog.EvaluateClusterSeed(cluster, seed);

            List<string> traitParts = new List<string>();
            List<CommunityPlanetEntry> planets = new List<CommunityPlanetEntry>();

            if (cluster != null)
            {
                List<WorldPlacementDef> ordered = cluster.GetOrderedPlacements();
                for (int i = 0; i < ordered.Count; i++)
                {
                    WorldPlacementDef wp = ordered[i];
                    CommunityPlanetEntry pe = new CommunityPlanetEntry
                    {
                        WorldIndex = wp.PlacementIndex,
                        WorldId = wp.WorldId ?? "",
                        WorldName = wp.World != null ? wp.World.DisplayName : (wp.WorldId ?? "")
                    };
                    List<WorldTraitDef> wts;
                    if (tmap != null && tmap.TryGetValue(wp.PlacementIndex, out wts) && wts != null)
                    {
                        for (int k = 0; k < wts.Count; k++)
                        {
                            pe.Traits.Add(wts[k].DisplayName);
                            pe.TraitIds.Add(wts[k].Id);
                        }
                        if (wts.Count > 0)
                            traitParts.Add(string.Format("[{0}] {1}：{2}", wp.GroupLabel(), pe.WorldName, string.Join("、", pe.Traits.ToArray())));
                    }

                    MapPreviewData md;
                    if (maps != null && maps.TryGetValue(wp.PlacementIndex, out md) && md != null)
                    {
                        pe.PoisScanned = true;
                        if (!string.IsNullOrEmpty(md.WorldName)) pe.WorldName = md.WorldName;
                        for (int pi = 0; pi < md.Pois.Count; pi++)
                        {
                            MapPoiItem item = md.Pois[pi];
                            if (item.Category == "start") continue;
                            pe.Pois.Add(new CommunityPoiEntry
                            {
                                PrefabId = item.PrefabId ?? "",
                                DisplayName = item.DisplayName ?? "",
                                Category = item.Category ?? "geyser",
                                X = item.X,
                                Y = item.Y,
                                DistFromStart = item.DistFromStart,
                                AvgYieldGps = Math.Round(item.AvgYieldGps, 2),
                                Grade = ByteGradeToChar(item.Grade),
                                Percentile = Math.Round(item.Percentile, 1)
                            });
                        }
                    }

                    if (pe.Traits.Count > 0 || pe.PoisScanned || pe.Pois.Count > 0)
                        planets.Add(pe);
                }
            }

            CommunitySeedEntry res = new CommunitySeedEntry
            {
                SchemaVersion = 1,
                ClusterPrefix = prefix,
                ClusterPath = cPath,
                ClusterName = cName,
                Seed = seed,
                Coordinate = !string.IsNullOrEmpty(coord)
                    ? coord
                    : SeedSearcher.FormatCoordinate(prefix, seed, "0-" + NormCode(storyCode) + "-" + NormCode(mixingCode)),
                StoryCode = NormCode(storyCode),
                MixingCode = NormCode(mixingCode),
                DlcFlags = NormalizeDlcKey(dlcFlags),
                Author = string.IsNullOrEmpty(author) ? "Player" : author.Trim(),
                SubmittedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                TraitSummary = string.Join(" | ", traitParts.ToArray()),
                PoiSummary = "",
                Note = (note ?? "").Trim(),
                Planets = planets
            };
            RebuildPoiSummary(res);
            return res;
        }

        static char ByteGradeToChar(byte g)
        {
            if (g == 1) return 'S';
            if (g == 2) return 'A';
            if (g == 3) return 'B';
            if (g == 4) return 'C';
            return ' ';
        }

        static byte CharGradeToByte(char g)
        {
            if (g == 'S') return 1;
            if (g == 'A') return 2;
            if (g == 'B') return 3;
            if (g == 'C') return 4;
            return 0;
        }

        static int GradeRank(char g)
        {
            if (g == 'S') return 4;
            if (g == 'A') return 3;
            if (g == 'B') return 2;
            if (g == 'C') return 1;
            return 0;
        }

        public static string BuildSubmissionJson(CommunitySeedEntry entry)
        {
            List<CommunitySeedEntry> one = new List<CommunitySeedEntry>();
            if (entry != null) one.Add(entry);
            return BuildBatchSubmissionJson(one);
        }

        public static string BuildGitHubIssueUrl(
            string repo,
            CommunitySeedEntry entry,
            string submissionJson,
            out bool bodyTooLongForUrl)
        {
            string cleanRepo = IsValidRepoSlug(repo) ? repo.Trim() : "3571949306/ONI-Seed-DB";
            string title = string.Format(CultureInfo.InvariantCulture,
                "[seed-submit] {0} {1}", entry != null ? entry.Coordinate : "", entry != null ? entry.Note : "").Trim();
            if (title.Length > 100) title = title.Substring(0, 100);

            string fullBody = "[seed-db-json]\n" + (submissionJson ?? "") + "\n[/seed-db-json]\n";
            string fullUrl = string.Format("https://github.com/{0}/issues/new?labels=seed-submission&title={1}&body={2}",
                cleanRepo, Uri.EscapeDataString(title), Uri.EscapeDataString(fullBody));

            if (fullUrl.Length <= 6500)
            {
                bodyTooLongForUrl = false;
                return fullUrl;
            }

            bodyTooLongForUrl = true;
            string shortBody = "<!-- 完整 JSON 已自动复制到剪贴板，请直接全选粘贴替换下方占位块后点击 Submit new issue -->\n[seed-db-json]\n请在此处按 Ctrl+V 粘贴完整 JSON\n[/seed-db-json]\n";
            return string.Format("https://github.com/{0}/issues/new?labels=seed-submission&title={1}&body={2}",
                cleanRepo, Uri.EscapeDataString(title), Uri.EscapeDataString(shortBody));
        }

        public static bool SubmitViaGitHubApi(
            string repo,
            string token,
            CommunitySeedEntry entry,
            string submissionJson,
            out string issueUrl,
            out string err)
        {
            issueUrl = "";
            err = null;
            AppUpdater.EnsureTls12();
            if (!IsValidRepoSlug(repo)) { err = "仓库格式无效"; return false; }
            if (string.IsNullOrEmpty(token)) { err = "未配置 GitHub Personal Access Token"; return false; }
            string title = string.Format(CultureInfo.InvariantCulture,
                "[seed-submit] {0} {1}", entry != null ? entry.Coordinate : "", entry != null ? entry.Note : "").Trim();
            string body = "[seed-db-json]\n" + (submissionJson ?? "") + "\n[/seed-db-json]\n";
            return PostSubmissionIssue(repo, token, title, body, out issueUrl, out err);
        }

        public static bool MatchesPoiRule(CommunityPlanetEntry planet, PoiFilterRule rule)
        {
            if (rule == null) return true;
            if (planet == null) return false;
            bool isWildcard = !string.IsNullOrEmpty(rule.PrefabId) && rule.PrefabId.StartsWith("@", StringComparison.Ordinal);
            string wildCat = isWildcard ? rule.PrefabId.Substring(1) : null;
            int minGradeRank = 0;
            if (rule.MinGrade == 1) minGradeRank = 4;
            else if (rule.MinGrade == 2) minGradeRank = 3;
            else if (rule.MinGrade == 3) minGradeRank = 2;

            int count = 0;
            for (int i = 0; i < planet.Pois.Count; i++)
            {
                CommunityPoiEntry p = planet.Pois[i];
                if (isWildcard)
                {
                    if (!string.Equals(p.Category, wildCat, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                else
                {
                    if (!string.Equals(p.PrefabId, rule.PrefabId, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                if (rule.MaxDist > 0 && p.DistFromStart > rule.MaxDist)
                    continue;
                if (rule.MinYieldGps > 0.001 && p.AvgYieldGps + 0.01 < rule.MinYieldGps)
                    continue;
                if (minGradeRank > 0 && GradeRank(p.Grade) < minGradeRank)
                    continue;
                count++;
            }
            return rule.CompareAtMost ? (count <= rule.MinCount) : (count >= rule.MinCount);
        }

        public static List<CommunityQueryResult> Query(
            WorldGenCatalog catalog,
            ClusterDef cluster,
            List<PlanetConstraint> constraints,
            bool hasTraitConstraint,
            Dictionary<int, List<PoiFilterRule>> groupedPoiRules,
            string keyword)
        {
            List<CommunityQueryResult> results = new List<CommunityQueryResult>();
            if (cluster == null) return results;

            List<CommunitySeedEntry> raw = LoadClusterSeeds(cluster.CoordinatePrefix);
            string kw = string.IsNullOrEmpty(keyword) ? "" : keyword.Trim();

            for (int i = 0; i < raw.Count; i++)
            {
                CommunitySeedEntry ent = raw[i];
                Dictionary<int, List<WorldTraitDef>> evalTraits;
                if (!MatchesEntryTraitConstraints(catalog, cluster, ent.Seed, constraints, hasTraitConstraint, out evalTraits))
                    continue;

                if (groupedPoiRules != null && groupedPoiRules.Count > 0)
                {
                    bool poiOk = true;
                    foreach (KeyValuePair<int, List<PoiFilterRule>> kv in groupedPoiRules)
                    {
                        CommunityPlanetEntry targetPlanet = FindScannedPlanet(ent, kv.Key);
                        if (targetPlanet == null)
                        {
                            poiOk = false;
                            break;
                        }
                        for (int ri = 0; ri < kv.Value.Count; ri++)
                        {
                            if (!MatchesPoiRule(targetPlanet, kv.Value[ri]))
                            {
                                poiOk = false;
                                break;
                            }
                        }
                        if (!poiOk) break;
                    }
                    if (!poiOk) continue;
                }

                if (!string.IsNullOrEmpty(kw))
                {
                    bool kwHit =
                        ContainsIc(ent.Coordinate, kw) ||
                        ContainsIc(ent.Seed.ToString(CultureInfo.InvariantCulture), kw) ||
                        ContainsIc(ent.TraitSummary, kw) ||
                        ContainsIc(ent.PoiSummary, kw) ||
                        ContainsIc(ent.Note, kw) ||
                        ContainsIc(ent.Author, kw);
                    if (!kwHit) continue;
                }

                results.Add(new CommunityQueryResult { Entry = ent, EvalTraits = evalTraits });
            }
            return results;
        }

        static bool ContainsIc(string hay, string needle)
        {
            return !string.IsNullOrEmpty(hay) &&
                   hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }



        static string JsonStr(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '"') sb.Append("\\\"");
                else if (ch == '\\') sb.Append("\\\\");
                else if (ch == '\r') sb.Append("\\r");
                else if (ch == '\n') sb.Append("\\n");
                else if (ch == '\t') sb.Append("\\t");
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(ch);
            }
            sb.Append('"');
            return sb.ToString();
        }

        static void AppendSeedJsonCompact(StringBuilder sb, CommunitySeedEntry e)
        {
            sb.Append("{\"schemaVersion\":1")
              .Append(",\"clusterPrefix\":").Append(JsonStr(e.ClusterPrefix))
              .Append(",\"clusterPath\":").Append(JsonStr(e.ClusterPath))
              .Append(",\"clusterName\":").Append(JsonStr(e.ClusterName))
              .Append(",\"seed\":").Append(e.Seed.ToString(CultureInfo.InvariantCulture))
              .Append(",\"coordinate\":").Append(JsonStr(e.Coordinate))
              .Append(",\"storyCode\":").Append(JsonStr(e.StoryCode))
              .Append(",\"mixingCode\":").Append(JsonStr(e.MixingCode))
              .Append(",\"dlcFlags\":").Append(JsonStr(e.DlcFlags))
              .Append(",\"author\":").Append(JsonStr(e.Author))
              .Append(",\"submittedAt\":").Append(JsonStr(e.SubmittedAt))
              .Append(",\"traitSummary\":").Append(JsonStr(e.TraitSummary))
              .Append(",\"poiSummary\":").Append(JsonStr(e.PoiSummary))
              .Append(",\"note\":").Append(JsonStr(e.Note))
              .Append(",\"planets\":[");
            if (e.Planets != null)
            {
                for (int pi = 0; pi < e.Planets.Count; pi++)
                {
                    if (pi > 0) sb.Append(',');
                    CommunityPlanetEntry p = e.Planets[pi];
                    sb.Append("{\"worldIndex\":").Append(p.WorldIndex.ToString(CultureInfo.InvariantCulture))
                      .Append(",\"worldId\":").Append(JsonStr(p.WorldId))
                      .Append(",\"worldName\":").Append(JsonStr(p.WorldName))
                      .Append(",\"poisScanned\":").Append(p.PoisScanned || (p.Pois != null && p.Pois.Count > 0) ? "true" : "false")
                      .Append(",\"traits\":[");
                    for (int ti = 0; ti < p.Traits.Count; ti++)
                    {
                        if (ti > 0) sb.Append(',');
                        sb.Append(JsonStr(p.Traits[ti]));
                    }
                    sb.Append("],\"traitIds\":[");
                    for (int ti = 0; ti < p.TraitIds.Count; ti++)
                    {
                        if (ti > 0) sb.Append(',');
                        sb.Append(JsonStr(p.TraitIds[ti]));
                    }
                    sb.Append("],\"pois\":[");
                    if (p.Pois != null)
                    {
                        for (int oi = 0; oi < p.Pois.Count; oi++)
                        {
                            if (oi > 0) sb.Append(',');
                            CommunityPoiEntry o = p.Pois[oi];
                            sb.Append("{\"prefabId\":").Append(JsonStr(o.PrefabId))
                              .Append(",\"displayName\":").Append(JsonStr(o.DisplayName))
                              .Append(",\"category\":").Append(JsonStr(o.Category))
                              .Append(",\"x\":").Append(o.X.ToString(CultureInfo.InvariantCulture))
                              .Append(",\"y\":").Append(o.Y.ToString(CultureInfo.InvariantCulture))
                              .Append(",\"dist\":").Append(o.DistFromStart.ToString(CultureInfo.InvariantCulture))
                              .Append(",\"avgYieldGps\":").Append(o.AvgYieldGps.ToString("0.##", CultureInfo.InvariantCulture))
                              .Append(",\"grade\":").Append(JsonStr(o.Grade == ' ' ? "" : o.Grade.ToString()))
                              .Append(",\"percentile\":").Append(o.Percentile.ToString("0.#", CultureInfo.InvariantCulture))
                              .Append('}');
                        }
                    }
                    sb.Append("]}");
                }
            }
            sb.Append("]}");
        }

        static string SerializeClusterDocCompact(string prefix, List<CommunitySeedEntry> seeds)
        {
            StringBuilder sb = new StringBuilder((seeds != null ? seeds.Count : 0) * 420 + 128);
            sb.Append("{\n");
            sb.Append("  \"schemaVersion\": 1,\n");
            sb.Append("  \"clusterPrefix\": ").Append(JsonStr(prefix)).Append(",\n");
            sb.Append("  \"updatedAt\": ").Append(JsonStr(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))).Append(",\n");
            sb.Append("  \"count\": ").Append(seeds.Count.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"seeds\": [\n");
            for (int i = 0; i < seeds.Count; i++)
            {
                sb.Append("    ");
                AppendSeedJsonCompact(sb, seeds[i]);
                if (i < seeds.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        static string SerializeManifest(CommunityManifest m)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"schemaVersion\": 1,\n");
            sb.Append("  \"updatedAt\": ").Append(JsonStr(m.UpdatedAt)).Append(",\n");
            sb.Append("  \"totalSeeds\": ").Append(m.TotalSeeds.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"clusters\": [\n");
            for (int i = 0; i < m.Clusters.Count; i++)
            {
                CommunityManifestItem c = m.Clusters[i];
                sb.Append("    { ")
                  .Append("\"clusterPrefix\": ").Append(JsonStr(c.ClusterPrefix)).Append(", ")
                  .Append("\"file\": ").Append(JsonStr(c.File)).Append(", ")
                  .Append("\"count\": ").Append(c.Count.ToString(CultureInfo.InvariantCulture)).Append(", ")
                  .Append("\"sha256\": ").Append(JsonStr(c.Sha256))
                  .Append(" }");
                if (i < m.Clusters.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        enum JsonKind { Null, Bool, Number, String, Array, Object }

        sealed class JsonVal
        {
            public JsonKind Kind;
            public double Num;
            public string Str;
            public List<JsonVal> Arr;
            public Dictionary<string, JsonVal> Obj;

            public JsonVal Get(string key)
            {
                if (Kind != JsonKind.Object || Obj == null) return null;
                JsonVal v;
                return Obj.TryGetValue(key, out v) ? v : null;
            }

            public string GetStr(string key)
            {
                JsonVal v = Get(key);
                if (v == null || v.Kind == JsonKind.Null) return "";
                if (v.Kind == JsonKind.String) return v.Str ?? "";
                if (v.Kind == JsonKind.Number) return v.Num.ToString(CultureInfo.InvariantCulture);
                return "";
            }

            public int GetInt(string key, int defVal)
            {
                JsonVal v = Get(key);
                if (v == null) return defVal;
                if (v.Kind == JsonKind.Number) return (int)Math.Round(v.Num);
                if (v.Kind == JsonKind.String)
                {
                    int iv;
                    if (int.TryParse(v.Str, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv)) return iv;
                }
                return defVal;
            }

            public bool GetBool(string key, bool defVal)
            {
                JsonVal v = Get(key);
                if (v == null) return defVal;
                if (v.Kind == JsonKind.Bool || v.Kind == JsonKind.Number) return Math.Abs(v.Num) > 0.5;
                if (v.Kind == JsonKind.String) return string.Equals(v.Str, "true", StringComparison.OrdinalIgnoreCase) || v.Str == "1";
                return defVal;
            }

            public double GetDbl(string key, double defVal)
            {
                JsonVal v = Get(key);
                if (v == null) return defVal;
                if (v.Kind == JsonKind.Number) return v.Num;
                if (v.Kind == JsonKind.String)
                {
                    double dv;
                    if (double.TryParse(v.Str, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) return dv;
                }
                return defVal;
            }

            public static JsonVal Parse(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return null;
                int p = 0;
                return ReadValue(raw, ref p);
            }

            static void SkipWs(string s, ref int p)
            {
                while (p < s.Length)
                {
                    char c = s[p];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') p++;
                    else break;
                }
            }

            static JsonVal ReadValue(string s, ref int p)
            {
                SkipWs(s, ref p);
                if (p >= s.Length) return null;
                char c = s[p];
                if (c == '{') return ReadObject(s, ref p);
                if (c == '[') return ReadArray(s, ref p);
                if (c == '"') return new JsonVal { Kind = JsonKind.String, Str = ReadString(s, ref p) };
                if (c == 't' && p + 4 <= s.Length && string.CompareOrdinal(s, p, "true", 0, 4) == 0)
                {
                    p += 4;
                    return new JsonVal { Kind = JsonKind.Bool, Num = 1 };
                }
                if (c == 'f' && p + 5 <= s.Length && string.CompareOrdinal(s, p, "false", 0, 5) == 0)
                {
                    p += 5;
                    return new JsonVal { Kind = JsonKind.Bool, Num = 0 };
                }
                if (c == 'n' && p + 4 <= s.Length && string.CompareOrdinal(s, p, "null", 0, 4) == 0)
                {
                    p += 4;
                    return new JsonVal { Kind = JsonKind.Null };
                }
                int start = p;
                if (s[p] == '-') p++;
                while (p < s.Length)
                {
                    char nc = s[p];
                    if ((nc >= '0' && nc <= '9') || nc == '.' || nc == 'e' || nc == 'E' || nc == '+' || nc == '-') p++;
                    else break;
                }
                if (p > start)
                {
                    double dv;
                    if (double.TryParse(s.Substring(start, p - start), NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                        return new JsonVal { Kind = JsonKind.Number, Num = dv };
                }
                throw new FormatException("Invalid JSON token at index " + p);
            }

            static string ReadString(string s, ref int p)
            {
                if (s[p] != '"') throw new FormatException("Expected string quote");
                p++;
                StringBuilder sb = new StringBuilder();
                while (p < s.Length)
                {
                    char c = s[p++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (p >= s.Length) break;
                        char esc = s[p++];
                        if (esc == '"' || esc == '\\' || esc == '/') sb.Append(esc);
                        else if (esc == 'b') sb.Append('\b');
                        else if (esc == 'f') sb.Append('\f');
                        else if (esc == 'n') sb.Append('\n');
                        else if (esc == 'r') sb.Append('\r');
                        else if (esc == 't') sb.Append('\t');
                        else if (esc == 'u' && p + 4 <= s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                sb.Append((char)code);
                            p += 4;
                        }
                    }
                    else sb.Append(c);
                }
                throw new FormatException("Unterminated JSON string");
            }

            static JsonVal ReadArray(string s, ref int p)
            {
                p++;
                List<JsonVal> list = new List<JsonVal>();
                SkipWs(s, ref p);
                if (p < s.Length && s[p] == ']') { p++; return new JsonVal { Kind = JsonKind.Array, Arr = list }; }
                while (p < s.Length)
                {
                    list.Add(ReadValue(s, ref p));
                    SkipWs(s, ref p);
                    if (p < s.Length && s[p] == ',') { p++; continue; }
                    if (p < s.Length && s[p] == ']') { p++; break; }
                    throw new FormatException("Malformed JSON array at " + p);
                }
                return new JsonVal { Kind = JsonKind.Array, Arr = list };
            }

            static JsonVal ReadObject(string s, ref int p)
            {
                p++;
                Dictionary<string, JsonVal> dict = new Dictionary<string, JsonVal>(StringComparer.Ordinal);
                SkipWs(s, ref p);
                if (p < s.Length && s[p] == '}') { p++; return new JsonVal { Kind = JsonKind.Object, Obj = dict }; }
                while (p < s.Length)
                {
                    SkipWs(s, ref p);
                    string key = ReadString(s, ref p);
                    SkipWs(s, ref p);
                    if (p >= s.Length || s[p] != ':') throw new FormatException("Expected ':' at " + p);
                    p++;
                    dict[key] = ReadValue(s, ref p);
                    SkipWs(s, ref p);
                    if (p < s.Length && s[p] == ',') { p++; continue; }
                    if (p < s.Length && s[p] == '}') { p++; break; }
                    throw new FormatException("Malformed JSON object at " + p);
                }
                return new JsonVal { Kind = JsonKind.Object, Obj = dict };
            }
        }

        static CommunityManifest ParseManifestJson(string raw)
        {
            CommunityManifest m = new CommunityManifest();
            JsonVal root = JsonVal.Parse(raw);
            if (root == null || root.Kind != JsonKind.Object) return m;
            m.SchemaVersion = root.GetInt("schemaVersion", 1);
            m.UpdatedAt = root.GetStr("updatedAt");
            m.TotalSeeds = root.GetInt("totalSeeds", 0);
            JsonVal cls = root.Get("clusters");
            if (cls != null && cls.Kind == JsonKind.Array)
            {
                for (int i = 0; i < cls.Arr.Count; i++)
                {
                    JsonVal item = cls.Arr[i];
                    if (item.Kind != JsonKind.Object) continue;
                    m.Clusters.Add(new CommunityManifestItem
                    {
                        ClusterPrefix = item.GetStr("clusterPrefix").ToUpperInvariant(),
                        File = item.GetStr("file"),
                        Count = item.GetInt("count", 0),
                        Sha256 = item.GetStr("sha256")
                    });
                }
            }
            return m;
        }

        static CommunitySeedEntry ParseSeedEntry(JsonVal obj)
        {
            if (obj == null || obj.Kind != JsonKind.Object) return null;
            CommunitySeedEntry e = new CommunitySeedEntry
            {
                SchemaVersion = obj.GetInt("schemaVersion", 1),
                ClusterPrefix = obj.GetStr("clusterPrefix").ToUpperInvariant(),
                ClusterPath = obj.GetStr("clusterPath"),
                ClusterName = obj.GetStr("clusterName"),
                Seed = obj.GetInt("seed", 0),
                Coordinate = obj.GetStr("coordinate"),
                StoryCode = obj.GetStr("storyCode"),
                MixingCode = obj.GetStr("mixingCode"),
                DlcFlags = obj.GetStr("dlcFlags"),
                Author = obj.GetStr("author"),
                SubmittedAt = obj.GetStr("submittedAt"),
                TraitSummary = obj.GetStr("traitSummary"),
                PoiSummary = obj.GetStr("poiSummary"),
                Note = obj.GetStr("note")
            };
            if (string.IsNullOrEmpty(e.StoryCode)) e.StoryCode = "0";
            if (string.IsNullOrEmpty(e.MixingCode)) e.MixingCode = "0";
            if (string.IsNullOrEmpty(e.ClusterPrefix)) e.ClusterPrefix = e.GetClusterFileKey();

            JsonVal planets = obj.Get("planets");
            if (planets != null && planets.Kind == JsonKind.Array)
            {
                for (int i = 0; i < planets.Arr.Count; i++)
                {
                    JsonVal pv = planets.Arr[i];
                    if (pv.Kind != JsonKind.Object) continue;
                    CommunityPlanetEntry pe = new CommunityPlanetEntry
                    {
                        WorldIndex = pv.GetInt("worldIndex", 0),
                        WorldId = pv.GetStr("worldId"),
                        WorldName = pv.GetStr("worldName")
                    };
                    JsonVal ts = pv.Get("traits");
                    if (ts != null && ts.Kind == JsonKind.Array)
                        for (int k = 0; k < ts.Arr.Count; k++)
                            if (ts.Arr[k].Kind == JsonKind.String) pe.Traits.Add(ts.Arr[k].Str);
                    JsonVal tids = pv.Get("traitIds");
                    if (tids != null && tids.Kind == JsonKind.Array)
                        for (int k = 0; k < tids.Arr.Count; k++)
                            if (tids.Arr[k].Kind == JsonKind.String) pe.TraitIds.Add(tids.Arr[k].Str);
                    JsonVal pois = pv.Get("pois");
                    if (pois != null && pois.Kind == JsonKind.Array)
                    {
                        for (int k = 0; k < pois.Arr.Count; k++)
                        {
                            JsonVal ov = pois.Arr[k];
                            if (ov.Kind != JsonKind.Object) continue;
                            string gs = ov.GetStr("grade");
                            pe.Pois.Add(new CommunityPoiEntry
                            {
                                PrefabId = ov.GetStr("prefabId"),
                                DisplayName = ov.GetStr("displayName"),
                                Category = ov.GetStr("category"),
                                X = ov.GetInt("x", 0),
                                Y = ov.GetInt("y", 0),
                                DistFromStart = ov.GetInt("dist", 0),
                                AvgYieldGps = ov.GetDbl("avgYieldGps", 0.0),
                                Grade = string.IsNullOrEmpty(gs) ? ' ' : gs[0],
                                Percentile = ov.GetDbl("percentile", 0.0)
                            });
                        }
                    }
                    pe.PoisScanned = pv.GetBool("poisScanned", pe.Pois.Count > 0);
                    e.Planets.Add(pe);
                }
            }
            return e;
        }
    }
}
