using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace ONITrainer
{
    public class DlcEntry
    {
        public string Id;
        public string SteamDlcAppId;
        public string Folder;
        public string Scope;
        public string DisplayName;
        public bool Installed;
        public bool Enabled;
    }

    public class WorldTraitDef
    {
        public int Index;
        public string Id;
        public string NameKey;
        public string DescriptionKey;
        public string DisplayName;
        public string Description;
        public string ColorHex;
        public List<string> ForbiddenDlcIds = new List<string>();
        public List<string> ExclusiveWith = new List<string>();
        public List<string> ExclusiveWithTags = new List<string>();
        public List<string> TraitTags = new List<string>();
        public Dictionary<string, int> GlobalFeatureMods = new Dictionary<string, int>();
        public ulong ExWithMask;
        public ulong ExTagMask;

        public override string ToString() { return DisplayName; }
    }

    public class TraitRuleDef
    {
        public int Min;
        public int Max;
        public List<string> RequiredTags = new List<string>();
        public List<string> ForbiddenTags = new List<string>();
        public List<string> ForbiddenTraits = new List<string>();
        public List<string> SpecificTraits = new List<string>();
    }

    public class WorldDef
    {
        public string Id;
        public string NameKey;
        public string DisplayName;
        public bool DisableWorldTraits;
        public float WorldTraitScale = 1f;
        public List<TraitRuleDef> Rules = new List<TraitRuleDef>();
    }

    public class WorldPlacementDef
    {
        public int PlacementIndex;
        public string WorldId;
        public string LocationType;
        public WorldDef World;

        public string GroupLabel()
        {
            if (string.Equals(LocationType, "StartWorld", StringComparison.OrdinalIgnoreCase)) return "初始";
            if (string.Equals(LocationType, "InnerCluster", StringComparison.OrdinalIgnoreCase)) return "临近";
            return "遥远";
        }
    }

    public class ClusterDef
    {
        public string FilePath;
        public string NameKey;
        public string DisplayName;
        public string CoordinatePrefix;
        public string ClusterCategory;
        public int MenuOrder;
        public int StartWorldIndex;
        public int FixedCoordinate = -1;
        public bool Skip;
        public List<string> RequiredDlcIds = new List<string>();
        public List<string> ForbiddenDlcIds = new List<string>();
        public List<WorldPlacementDef> Placements = new List<WorldPlacementDef>();

        public List<WorldPlacementDef> GetOrderedPlacements()
        {
            List<WorldPlacementDef> ordered = new List<WorldPlacementDef>();
            for (int i = 0; i < Placements.Count; i++)
                if (Placements[i].PlacementIndex == StartWorldIndex ||
                    string.Equals(Placements[i].LocationType, "StartWorld", StringComparison.OrdinalIgnoreCase))
                    ordered.Add(Placements[i]);
            for (int i = 0; i < Placements.Count; i++)
                if (!ordered.Contains(Placements[i]) &&
                    string.Equals(Placements[i].LocationType, "InnerCluster", StringComparison.OrdinalIgnoreCase))
                    ordered.Add(Placements[i]);
            for (int i = 0; i < Placements.Count; i++)
                if (!ordered.Contains(Placements[i]))
                    ordered.Add(Placements[i]);
            return ordered;
        }

        public override string ToString()
        {
            return DisplayName + "  [" + CoordinatePrefix + "]";
        }
    }

    public class WorldTraitLegalInfo
    {
        public int MinTraits;
        public int MaxTraits;
        public bool IsFixedNone;
        public List<WorldTraitDef> FixedTraits = new List<WorldTraitDef>();
        public List<WorldTraitDef> CandidateTraits = new List<WorldTraitDef>();
    }

    public class PlanetConstraint
    {
        public int PlacementIndex;
        public ulong RequiredMask;
        public ulong ForbiddenMask;
        public bool RequireNoTraits;
    }

    public class SearchControl
    {
        public volatile bool Stop;
        public long Tested;
    }

    public class SeedSearchResult
    {
        public int Seed;
        public string Coordinate;
        public long SeedsTested;
        public double ElapsedMs;
        public Dictionary<int, List<WorldTraitDef>> PlanetTraits = new Dictionary<int, List<WorldTraitDef>>();
    }

    public static class GameInstallationDetector
    {
        static DlcEntry[] CreateKnownDlcs()
        {
            return new DlcEntry[]
            {
                new DlcEntry { Id = "EXPANSION1_ID", SteamDlcAppId = "1452490", Folder = "expansion1", Scope = "expansion1::", DisplayName = "眼冒金星！" },
                new DlcEntry { Id = "DLC2_ID", SteamDlcAppId = "2952300", Folder = "dlc2", Scope = "dlc2::", DisplayName = "寒霜行星包" },
                new DlcEntry { Id = "DLC3_ID", SteamDlcAppId = "3302470", Folder = "dlc3", Scope = "dlc3::", DisplayName = "仿生增幅包" },
                new DlcEntry { Id = "DLC4_ID", SteamDlcAppId = "3655420", Folder = "dlc4", Scope = "dlc4::", DisplayName = "史前行星包" },
                new DlcEntry { Id = "DLC5_ID", SteamDlcAppId = "4310080", Folder = "dlc5", Scope = "dlc5::", DisplayName = "汪洋行星包" }
            };
        }

        public static bool IsValidGameDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try
            {
                string sa = Path.Combine(dir, "OxygenNotIncluded_Data", "StreamingAssets", "worldgen");
                return Directory.Exists(sa);
            }
            catch { return false; }
        }

        public static string DetectGameDir()
        {

            try
            {
                Process[] ps = Process.GetProcessesByName("OxygenNotIncluded");
                for (int i = 0; i < ps.Length; i++)
                {
                    try
                    {
                        string exe = ps[i].MainModule != null ? ps[i].MainModule.FileName : null;
                        if (!string.IsNullOrEmpty(exe))
                        {
                            string d = Path.GetDirectoryName(exe);
                            if (IsValidGameDir(d)) return d;
                        }
                    }
                    catch { }
                    finally { try { ps[i].Dispose(); } catch { } }
                }
            }
            catch { }


            List<string> steamRoots = new List<string>();
            TryAddRegPath(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath", steamRoots);
            TryAddRegPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", steamRoots);
            TryAddRegPath(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", steamRoots);

            List<string> libs = new List<string>();
            for (int i = 0; i < steamRoots.Count; i++)
            {
                AddUnique(libs, steamRoots[i]);
                string vdf = Path.Combine(steamRoots[i], "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    try
                    {
                        string text = File.ReadAllText(vdf, Encoding.UTF8);
                        MatchCollection mc = Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        foreach (Match m in mc)
                        {
                            string p = m.Groups[1].Value.Replace("\\\\", "\\").Replace('/', '\\');
                            AddUnique(libs, p);
                        }
                    }
                    catch { }
                }
            }


            string[] drives = { @"C:\", @"D:\", @"E:\", @"F:\", @"G:\" };
            for (int i = 0; i < drives.Length; i++)
            {
                AddUnique(libs, Path.Combine(drives[i], "SteamLibrary"));
                AddUnique(libs, Path.Combine(drives[i], "Steam"));
                AddUnique(libs, Path.Combine(drives[i], "Program Files (x86)", "Steam"));
            }

            for (int i = 0; i < libs.Count; i++)
            {
                string cand = Path.Combine(libs[i], "steamapps", "common", "OxygenNotIncluded");
                if (IsValidGameDir(cand)) return cand;
            }
            return null;
        }

        static void TryAddRegPath(RegistryKey hive, string subKey, string valName, List<string> dst)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(subKey))
                {
                    if (k == null) return;
                    string v = k.GetValue(valName) as string;
                    if (!string.IsNullOrEmpty(v))
                        AddUnique(dst, v.Replace('/', '\\'));
                }
            }
            catch { }
        }

        static void AddUnique(List<string> list, string item)
        {
            if (string.IsNullOrEmpty(item)) return;
            string norm = item.TrimEnd('\\');
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], norm, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(norm);
        }

        public static List<DlcEntry> DetectInstalledDlcs(string gameDir)
        {
            DlcEntry[] list = CreateKnownDlcs();
            if (!IsValidGameDir(gameDir))
                return new List<DlcEntry>(list);

            HashSet<string> acfDlcApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasAcfDepots = false;
            try
            {
                string acf = Path.GetFullPath(Path.Combine(gameDir, "..", "..", "appmanifest_457140.acf"));
                if (File.Exists(acf))
                {
                    string txt = File.ReadAllText(acf, Encoding.UTF8);
                    MatchCollection mc = Regex.Matches(txt, "\"dlcappid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase);
                    foreach (Match m in mc)
                    {
                        acfDlcApps.Add(m.Groups[1].Value);
                        hasAcfDepots = true;
                    }
                    if (!hasAcfDepots && txt.IndexOf("InstalledDepots", StringComparison.OrdinalIgnoreCase) >= 0)
                        hasAcfDepots = true;
                }
            }
            catch { }

            bool? exp1Pref = ReadExpansion1Pref();
            string sa = Path.Combine(gameDir, "OxygenNotIncluded_Data", "StreamingAssets");
            for (int i = 0; i < list.Length; i++)
            {
                DlcEntry d = list[i];
                bool filesOk = File.Exists(Path.Combine(sa, d.Folder + "_bundle")) ||
                               Directory.Exists(Path.Combine(sa, "dlc", d.Folder));
                d.Installed = hasAcfDepots
                    ? (acfDlcApps.Contains(d.SteamDlcAppId) && filesOk)
                    : filesOk;
                if (d.Id == "EXPANSION1_ID" && d.Installed && exp1Pref.HasValue)
                    d.Enabled = exp1Pref.Value;
                else
                    d.Enabled = d.Installed;
            }
            return new List<DlcEntry>(list);
        }

        static bool? ReadExpansion1Pref()
        {
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string kp = Path.Combine(docs, "Klei", "OxygenNotIncluded", "kplayerprefs.yaml");
                if (!File.Exists(kp)) return null;
                string txt = File.ReadAllText(kp, Encoding.UTF8);
                Match m = Regex.Match(txt, @"EXPANSION1_ID\.ENABLED:\s*(\d+)");
                if (m.Success) return m.Groups[1].Value != "0";
            }
            catch { }
            return null;
        }

        public static string SummarizeEnabledDlcs(List<DlcEntry> dlcs)
        {
            List<string> names = new List<string>();
            if (dlcs != null)
            {
                for (int i = 0; i < dlcs.Count; i++)
                    if (dlcs[i].Enabled) names.Add(dlcs[i].DisplayName);
            }
            if (names.Count == 0) return "仅本体（无扩展 DLC）";
            return string.Join("、", names.ToArray());
        }
    }

    public class WorldGenCatalog
    {

        static readonly string[] ReleaseOrderIds = { "", "EXPANSION1_ID", "DLC2_ID", "DLC3_ID", "DLC4_ID", "DLC5_ID" };
        static readonly string[] ReleaseOrderScopes = { "", "expansion1::", "dlc2::", "dlc3::", "dlc4::", "dlc5::" };
        static readonly string[] ReleaseOrderFolders = { "", "expansion1", "dlc2", "dlc3", "dlc4", "dlc5" };

        public string GameDir { get; private set; }
        public HashSet<string> ActiveDlcIds { get; private set; }
        public List<WorldTraitDef> ActiveTraits { get; private set; }
        public Dictionary<string, WorldTraitDef> TraitsById { get; private set; }
        public List<ClusterDef> Clusters { get; private set; }
        public Dictionary<string, WorldDef> WorldsById { get; private set; }
        public bool HasZhStrings { get; private set; }

        readonly Dictionary<string, string> _po = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, WorldTraitLegalInfo> _legalCache = new Dictionary<string, WorldTraitLegalInfo>(StringComparer.Ordinal);

        WorldGenCatalog()
        {
            ActiveTraits = new List<WorldTraitDef>();
            TraitsById = new Dictionary<string, WorldTraitDef>(StringComparer.Ordinal);
            Clusters = new List<ClusterDef>();
            WorldsById = new Dictionary<string, WorldDef>(StringComparer.Ordinal);
        }

        public static WorldGenCatalog Load(string gameDir, HashSet<string> activeDlcIds)
        {
            WorldGenCatalog cat = new WorldGenCatalog();
            cat.GameDir = gameDir;
            cat.ActiveDlcIds = new HashSet<string>(StringComparer.Ordinal);
            cat.ActiveDlcIds.Add("");
            if (activeDlcIds != null)
            {
                foreach (string id in activeDlcIds)
                    if (!string.IsNullOrEmpty(id)) cat.ActiveDlcIds.Add(id);
            }

            string sa = Path.Combine(gameDir, "OxygenNotIncluded_Data", "StreamingAssets");
            cat.LoadChineseStrings(sa);
            cat.LoadWorldTraits(sa);
            cat.LoadClustersAndWorlds(sa);
            return cat;
        }

        void LoadChineseStrings(string sa)
        {
            string poPath = Path.Combine(sa, "strings", "strings_preinstalled_zh_klei.po");
            if (!File.Exists(poPath)) return;
            HasZhStrings = true;
            string curCtx = null;
            using (StreamReader sr = new StreamReader(poPath, Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("msgctxt \"", StringComparison.Ordinal))
                    {
                        string ctx = line.Length >= 10 ? line.Substring(9, line.Length - 10) : null;
                        if (ctx != null && (ctx.StartsWith("STRINGS.WORLD_TRAITS.", StringComparison.Ordinal) ||
                                            ctx.StartsWith("STRINGS.WORLDS.", StringComparison.Ordinal) ||
                                            ctx.StartsWith("STRINGS.CLUSTER_NAMES.", StringComparison.Ordinal)))
                            curCtx = ctx;
                        else
                            curCtx = null;
                    }
                    else if (curCtx != null && line.StartsWith("msgstr \"", StringComparison.Ordinal))
                    {
                        if (line.Length >= 9)
                        {
                            string val = line.Substring(8, line.Length - 9).Replace("\\\"", "\"");
                            val = CleanRichText(val);
                            if (val.Length > 0) _po[curCtx] = val;
                        }
                        curCtx = null;
                    }
                }
            }
        }

        static string CleanRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf('<') >= 0)
                s = Regex.Replace(s, "<[^>]+>", "");
            return s.Trim();
        }

        string Translate(string key, string fallback)
        {
            string v;
            if (!string.IsNullOrEmpty(key) && _po.TryGetValue(key, out v) && v.Length > 0)
                return v;
            return fallback;
        }

        static string StripComment(string raw)
        {
            char inQ = '\0';
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (ch == '"' || ch == '\'')
                {
                    if (inQ == ch) inQ = '\0';
                    else if (inQ == '\0') inQ = ch;
                }
                else if (ch == '#' && inQ == '\0')
                {
                    return raw.Substring(0, i).TrimEnd();
                }
            }
            return raw.TrimEnd();
        }

        static int LeadingSpaces(string line)
        {
            int n = 0;
            while (n < line.Length && line[n] == ' ') n++;
            return n;
        }

        static string[] SplitInlineList(string v)
        {
            if (v.Length < 2 || v[0] != '[' || v[v.Length - 1] != ']') return null;
            string inner = v.Substring(1, v.Length - 2).Trim();
            if (inner.Length == 0) return new string[0];
            string[] parts = inner.Split(',');
            List<string> res = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string item = parts[i].Trim().Trim('"', '\'');
                if (item.Length > 0) res.Add(item);
            }
            return res.ToArray();
        }

        void LoadWorldTraits(string sa)
        {
            for (int d = 0; d < ReleaseOrderIds.Length; d++)
            {
                string dlcId = ReleaseOrderIds[d];
                if (!ActiveDlcIds.Contains(dlcId)) continue;
                string wg = dlcId.Length == 0
                    ? Path.Combine(sa, "worldgen")
                    : Path.Combine(sa, "dlc", ReleaseOrderFolders[d], "worldgen");
                string tdir = Path.Combine(wg, "traits");
                if (!Directory.Exists(tdir)) continue;

                string[] files = Directory.GetFiles(tdir, "*.yaml");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    WorldTraitDef t = ParseTraitYaml(files[i]);
                    bool forbidden = false;
                    for (int f = 0; f < t.ForbiddenDlcIds.Count; f++)
                    {
                        if (ActiveDlcIds.Contains(t.ForbiddenDlcIds[f])) { forbidden = true; break; }
                    }
                    if (forbidden) continue;

                    string stem = Path.GetFileNameWithoutExtension(files[i]);
                    t.Id = ReleaseOrderScopes[d] + "traits/" + stem;
                    t.DisplayName = Translate(t.NameKey, stem);
                    string descKey = t.DescriptionKey;
                    if (string.IsNullOrEmpty(descKey) && !string.IsNullOrEmpty(t.NameKey) &&
                        t.NameKey.EndsWith(".NAME", StringComparison.Ordinal))
                    {
                        descKey = t.NameKey.Substring(0, t.NameKey.Length - 5) + ".DESCRIPTION";
                    }
                    t.Description = Translate(descKey, "").Replace("\\n", " ").Trim();
                    t.Index = ActiveTraits.Count;
                    ActiveTraits.Add(t);
                    TraitsById[t.Id] = t;
                }
            }


            Dictionary<string, int> exTagIdx = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < ActiveTraits.Count; i++)
            {
                WorldTraitDef t = ActiveTraits[i];
                ulong exIds = 0;
                for (int j = 0; j < t.ExclusiveWith.Count; j++)
                {
                    WorldTraitDef target;
                    if (TraitsById.TryGetValue(t.ExclusiveWith[j], out target) && target.Index < 64)
                        exIds |= (1UL << target.Index);
                }
                t.ExWithMask = exIds;

                ulong exTags = 0;
                for (int j = 0; j < t.ExclusiveWithTags.Count; j++)
                {
                    string tag = t.ExclusiveWithTags[j];
                    int bit;
                    if (!exTagIdx.TryGetValue(tag, out bit))
                    {
                        bit = exTagIdx.Count;
                        exTagIdx[tag] = bit;
                    }
                    if (bit < 64) exTags |= (1UL << bit);
                }
                t.ExTagMask = exTags;
            }
        }

        static WorldTraitDef ParseTraitYaml(string fp)
        {
            WorldTraitDef t = new WorldTraitDef();
            string sec = null;
            using (StreamReader sr = new StreamReader(fp, Encoding.UTF8))
            {
                string raw;
                while ((raw = sr.ReadLine()) != null)
                {
                    string line = StripComment(raw);
                    if (line.Trim().Length == 0) continue;
                    int ind = LeadingSpaces(line);
                    string s = line.Trim();
                    if (ind == 0 && !s.StartsWith("- ", StringComparison.Ordinal))
                    {
                        sec = null;
                        int colon = s.IndexOf(':');
                        if (colon > 0)
                        {
                            string k = s.Substring(0, colon).Trim();
                            string v = s.Substring(colon + 1).Trim();
                            if (k == "name") t.NameKey = v.Trim('"', '\'');
                            else if (k == "description") t.DescriptionKey = v.Trim('"', '\'');
                            else if (k == "colorHex") t.ColorHex = v.Trim('"', '\'');
                            else if (k == "forbiddenDLCIds" || k == "exclusiveWith" || k == "exclusiveWithTags" || k == "traitTags")
                            {
                                string[] inline = SplitInlineList(v);
                                List<string> targetList =
                                    k == "forbiddenDLCIds" ? t.ForbiddenDlcIds :
                                    k == "exclusiveWith" ? t.ExclusiveWith :
                                    k == "exclusiveWithTags" ? t.ExclusiveWithTags : t.TraitTags;
                                if (inline != null) targetList.AddRange(inline);
                                else sec = k;
                            }
                            else if (k == "globalFeatureMods") sec = k;
                        }
                    }
                    else if (sec != null)
                    {
                        if (sec == "globalFeatureMods")
                        {

                            int last = s.LastIndexOf(':');
                            if (last > 0)
                            {
                                string fk = s.Substring(0, last).Trim();
                                int fv;
                                if (int.TryParse(s.Substring(last + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out fv))
                                    t.GlobalFeatureMods[fk] = fv;
                            }
                        }
                        else if (s.StartsWith("- ", StringComparison.Ordinal))
                        {
                            string val = s.Substring(2).Trim().Trim('"', '\'');
                            if (val.Length > 0)
                            {
                                if (sec == "forbiddenDLCIds") t.ForbiddenDlcIds.Add(val);
                                else if (sec == "exclusiveWith") t.ExclusiveWith.Add(val);
                                else if (sec == "exclusiveWithTags") t.ExclusiveWithTags.Add(val);
                                else if (sec == "traitTags") t.TraitTags.Add(val);
                            }
                        }
                    }
                }
            }
            return t;
        }

        void LoadClustersAndWorlds(string sa)
        {
            bool spacedOut = ActiveDlcIds.Contains("EXPANSION1_ID");
            for (int d = 0; d < ReleaseOrderIds.Length; d++)
            {
                string dlcId = ReleaseOrderIds[d];
                if (!ActiveDlcIds.Contains(dlcId)) continue;
                string wg = dlcId.Length == 0
                    ? Path.Combine(sa, "worldgen")
                    : Path.Combine(sa, "dlc", ReleaseOrderFolders[d], "worldgen");
                string cdir = Path.Combine(wg, "clusters");
                if (!Directory.Exists(cdir)) continue;

                string[] files = Directory.GetFiles(cdir, "*.yaml");
                for (int i = 0; i < files.Length; i++)
                {
                    ClusterDef c = ParseClusterYaml(files[i], ReleaseOrderScopes[d]);
                    if (c.Skip) continue;

                    bool ok = true;
                    for (int r = 0; r < c.RequiredDlcIds.Count; r++)
                        if (!ActiveDlcIds.Contains(c.RequiredDlcIds[r])) { ok = false; break; }
                    if (!ok) continue;
                    for (int f = 0; f < c.ForbiddenDlcIds.Count; f++)
                        if (ActiveDlcIds.Contains(c.ForbiddenDlcIds[f])) { ok = false; break; }
                    if (!ok) continue;


                    if (spacedOut && string.Equals(c.FilePath, "clusters/SandstoneDefault", StringComparison.OrdinalIgnoreCase))
                        continue;

                    for (int p = 0; p < c.Placements.Count; p++)
                    {
                        WorldPlacementDef wp = c.Placements[p];
                        wp.World = GetOrLoadWorld(sa, wp.WorldId);
                    }
                    c.DisplayName = Translate(c.NameKey, Path.GetFileNameWithoutExtension(files[i]));
                    Clusters.Add(c);
                }
            }

            Clusters.Sort((a, b) =>
            {
                int cmp = a.MenuOrder.CompareTo(b.MenuOrder);
                if (cmp != 0) return cmp;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture);
            });
        }

        ClusterDef ParseClusterYaml(string fp, string scope)
        {
            ClusterDef c = new ClusterDef();
            c.FilePath = scope + "clusters/" + Path.GetFileNameWithoutExtension(fp);
            c.ClusterCategory = "Vanilla";
            string topSec = null;
            WorldPlacementDef curWp = null;
            int wpItemInd = -1;

            using (StreamReader sr = new StreamReader(fp, Encoding.UTF8))
            {
                string raw;
                while ((raw = sr.ReadLine()) != null)
                {
                    string line = StripComment(raw);
                    if (line.Trim().Length == 0) continue;
                    int ind = LeadingSpaces(line);
                    string s = line.Trim();

                    if (ind == 0 && !s.StartsWith("- ", StringComparison.Ordinal))
                    {
                        topSec = null;
                        curWp = null;
                        int colon = s.IndexOf(':');
                        if (colon > 0)
                        {
                            string k = s.Substring(0, colon).Trim();
                            string v = s.Substring(colon + 1).Trim();
                            if (k == "name") c.NameKey = v.Trim('"', '\'');
                            else if (k == "coordinatePrefix") c.CoordinatePrefix = v.Trim('"', '\'');
                            else if (k == "clusterCategory")
                            {
                                string cat = v.Trim('"', '\'');
                                if (cat == "0") cat = "Vanilla";
                                else if (cat == "1") cat = "SpacedOutVanillaStyle";
                                else if (cat == "2") cat = "SpacedOutStyle";
                                else if (cat == "3") cat = "Special";
                                c.ClusterCategory = cat;
                            }
                            else if (k == "menuOrder") int.TryParse(v, out c.MenuOrder);
                            else if (k == "startWorldIndex") int.TryParse(v, out c.StartWorldIndex);
                            else if (k == "fixedCoordinate") int.TryParse(v, out c.FixedCoordinate);
                            else if (k == "skip")
                            {
                                string sv = v.Trim('"', '\'');
                                c.Skip = sv.Length > 0 &&
                                    !string.Equals(sv, "false", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(sv, "Never", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(sv, "0", StringComparison.OrdinalIgnoreCase);
                            }
                            else if (k == "requiredDlcId" && v.Length > 0) c.RequiredDlcIds.Add(v.Trim('"', '\''));
                            else if (k == "forbiddenDlcId" && v.Length > 0) c.ForbiddenDlcIds.Add(v.Trim('"', '\''));
                            else if (k == "requiredDlcIds" || k == "forbiddenDlcIds")
                            {
                                string[] inline = SplitInlineList(v);
                                List<string> target = (k == "requiredDlcIds") ? c.RequiredDlcIds : c.ForbiddenDlcIds;
                                if (inline != null) target.AddRange(inline);
                                else topSec = k;
                            }
                            else if (k == "worldPlacements") topSec = k;
                        }
                    }
                    else if (topSec == "requiredDlcIds" || topSec == "forbiddenDlcIds")
                    {
                        if (s.StartsWith("- ", StringComparison.Ordinal))
                        {
                            string val = s.Substring(2).Trim().Trim('"', '\'');
                            if (val.Length > 0)
                            {
                                if (topSec == "requiredDlcIds") c.RequiredDlcIds.Add(val);
                                else c.ForbiddenDlcIds.Add(val);
                            }
                        }
                    }
                    else if (topSec == "worldPlacements")
                    {
                        if (s.StartsWith("- world:", StringComparison.Ordinal))
                        {
                            if (wpItemInd == -1) wpItemInd = ind;
                            if (ind == wpItemInd)
                            {
                                curWp = new WorldPlacementDef
                                {
                                    PlacementIndex = c.Placements.Count,
                                    WorldId = s.Substring(8).Trim().Trim('"', '\''),
                                    LocationType = "Outer"
                                };
                                c.Placements.Add(curWp);
                            }
                        }
                        else if (curWp != null && ind <= wpItemInd + 3 && s.StartsWith("locationType:", StringComparison.Ordinal))
                        {
                            curWp.LocationType = s.Substring(13).Trim().Trim('"', '\'');
                        }
                    }
                }
            }
            return c;
        }

        WorldDef GetOrLoadWorld(string sa, string scopedWorldId)
        {
            WorldDef existing;
            if (WorldsById.TryGetValue(scopedWorldId, out existing)) return existing;

            string fp;
            int sep = scopedWorldId.IndexOf("::", StringComparison.Ordinal);
            if (sep > 0)
            {
                string sc = scopedWorldId.Substring(0, sep);
                string rel = scopedWorldId.Substring(sep + 2).Replace('/', '\\');
                fp = Path.Combine(sa, "dlc", sc, "worldgen", rel + ".yaml");
            }
            else
            {
                fp = Path.Combine(sa, "worldgen", scopedWorldId.Replace('/', '\\') + ".yaml");
            }

            WorldDef w = new WorldDef { Id = scopedWorldId };
            if (File.Exists(fp)) ParseWorldYaml(fp, w);
            w.DisplayName = Translate(w.NameKey, scopedWorldId.Substring(scopedWorldId.LastIndexOf('/') + 1));
            WorldsById[scopedWorldId] = w;
            return w;
        }

        static void ParseWorldYaml(string fp, WorldDef w)
        {
            bool inRules = false;
            int ruleInd = -1;
            TraitRuleDef curRule = null;
            string subList = null;

            using (StreamReader sr = new StreamReader(fp, Encoding.UTF8))
            {
                string raw;
                while ((raw = sr.ReadLine()) != null)
                {
                    string line = StripComment(raw);
                    if (line.Trim().Length == 0) continue;
                    int ind = LeadingSpaces(line);
                    string s = line.Trim();

                    if (ind == 0 && !s.StartsWith("- ", StringComparison.Ordinal))
                    {
                        inRules = false;
                        ruleInd = -1;
                        curRule = null;
                        subList = null;
                        int colon = s.IndexOf(':');
                        if (colon > 0)
                        {
                            string k = s.Substring(0, colon).Trim();
                            string v = s.Substring(colon + 1).Trim();
                            if (k == "name") w.NameKey = v.Trim('"', '\'');
                            else if (k == "disableWorldTraits") w.DisableWorldTraits = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                            else if (k == "worldTraitScale")
                            {
                                float sc;
                                if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out sc))
                                    w.WorldTraitScale = sc;
                            }
                            else if (k == "worldTraitRules")
                            {
                                inRules = true;
                            }
                        }
                    }
                    else if (inRules)
                    {
                        if (s.StartsWith("- ", StringComparison.Ordinal) && (ruleInd == -1 || ind == ruleInd))
                        {
                            ruleInd = ind;
                            curRule = new TraitRuleDef();
                            w.Rules.Add(curRule);
                            subList = null;
                            string rest = s.Substring(2).Trim();
                            AssignRuleProp(curRule, rest, ref subList);
                        }
                        else if (curRule != null)
                        {
                            if (s.StartsWith("- ", StringComparison.Ordinal))
                            {
                                string val = s.Substring(2).Trim().Trim('"', '\'');
                                if (val.Length > 0 && subList != null)
                                    GetRuleList(curRule, subList).Add(val);
                            }
                            else
                            {
                                subList = null;
                                AssignRuleProp(curRule, s, ref subList);
                            }
                        }
                    }
                }
            }
        }

        static List<string> GetRuleList(TraitRuleDef r, string name)
        {
            if (name == "requiredTags") return r.RequiredTags;
            if (name == "forbiddenTags") return r.ForbiddenTags;
            if (name == "forbiddenTraits") return r.ForbiddenTraits;
            return r.SpecificTraits;
        }

        static void AssignRuleProp(TraitRuleDef r, string kvLine, ref string subList)
        {
            int colon = kvLine.IndexOf(':');
            if (colon <= 0) return;
            string k = kvLine.Substring(0, colon).Trim();
            string v = kvLine.Substring(colon + 1).Trim();
            if (k == "min") int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Min);
            else if (k == "max") int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Max);
            else if (k == "requiredTags" || k == "forbiddenTags" || k == "forbiddenTraits" || k == "specificTraits")
            {
                string[] inline = SplitInlineList(v);
                if (inline != null) GetRuleList(r, k).AddRange(inline);
                else subList = k;
            }
        }

        public List<KeyValuePair<string, string>> GetStyleOptions()
        {
            List<KeyValuePair<string, string>> styles = new List<KeyValuePair<string, string>>();
            if (ActiveDlcIds.Contains("EXPANSION1_ID"))
            {
                AddStyleIfAny(styles, "SpacedOutVanillaStyle", "经典（Spaced Out 经典风格）");
                AddStyleIfAny(styles, "SpacedOutStyle", "眼冒金星！");
                AddStyleIfAny(styles, "Special", "实验室");
            }
            else
            {
                AddStyleIfAny(styles, "Vanilla", "经典（本体）");
                AddStyleIfAny(styles, "Special", "实验室");
            }
            return styles;
        }

        void AddStyleIfAny(List<KeyValuePair<string, string>> styles, string cat, string title)
        {
            for (int i = 0; i < Clusters.Count; i++)
            {
                if (string.Equals(Clusters[i].ClusterCategory, cat, StringComparison.OrdinalIgnoreCase))
                {
                    styles.Add(new KeyValuePair<string, string>(cat, title));
                    return;
                }
            }
        }

        public List<ClusterDef> GetClustersForStyle(string clusterCategory)
        {
            List<ClusterDef> list = new List<ClusterDef>();
            for (int i = 0; i < Clusters.Count; i++)
                if (string.Equals(Clusters[i].ClusterCategory, clusterCategory, StringComparison.OrdinalIgnoreCase))
                    list.Add(Clusters[i]);
            return list;
        }

        public static bool IsTraitValidForWorld(WorldTraitDef t, WorldDef w)
        {
            if (t.GlobalFeatureMods.Count == 0) return true;
            int totalScaled = 0;
            foreach (KeyValuePair<string, int> kv in t.GlobalFeatureMods)
            {
                float prod = w.WorldTraitScale * (float)kv.Value;
                totalScaled += (int)Math.Floor((double)prod);
            }
            return totalScaled != 0;
        }

        public bool TraitMatchesRule(WorldTraitDef t, TraitRuleDef r, WorldDef w)
        {
            for (int i = 0; i < r.RequiredTags.Count; i++)
                if (!t.TraitTags.Contains(r.RequiredTags[i])) return false;
            for (int i = 0; i < r.ForbiddenTags.Count; i++)
                if (t.TraitTags.Contains(r.ForbiddenTags[i])) return false;
            if (r.ForbiddenTraits.Contains(t.Id)) return false;
            return IsTraitValidForWorld(t, w);
        }

        public WorldTraitLegalInfo GetLegalTraits(WorldDef world)
        {
            WorldTraitLegalInfo cached;
            if (world != null && _legalCache.TryGetValue(world.Id, out cached)) return cached;

            WorldTraitLegalInfo info = new WorldTraitLegalInfo();
            if (world == null || world.DisableWorldTraits || world.Rules.Count == 0)
            {
                info.IsFixedNone = true;
                if (world != null) _legalCache[world.Id] = info;
                return info;
            }

            HashSet<int> candSet = new HashSet<int>();
            HashSet<int> fixedSet = new HashSet<int>();
            int minSum = 0, maxSum = 0;
            for (int r = 0; r < world.Rules.Count; r++)
            {
                TraitRuleDef rule = world.Rules[r];
                for (int s = 0; s < rule.SpecificTraits.Count; s++)
                {
                    WorldTraitDef st;
                    if (TraitsById.TryGetValue(rule.SpecificTraits[s], out st) && fixedSet.Add(st.Index))
                        info.FixedTraits.Add(st);
                }
                int ruleCands = 0;
                for (int t = 0; t < ActiveTraits.Count; t++)
                {
                    if (TraitMatchesRule(ActiveTraits[t], rule, world))
                    {
                        ruleCands++;
                        candSet.Add(t);
                    }
                }
                int rmin = Math.Min(rule.Min, ruleCands);
                int rmax = Math.Min(Math.Max(rule.Min, rule.Max), ruleCands);
                minSum += rmin;
                maxSum += rmax;
            }

            info.MinTraits = minSum + info.FixedTraits.Count;
            info.MaxTraits = maxSum + info.FixedTraits.Count;
            info.IsFixedNone = (info.MaxTraits == 0 && info.FixedTraits.Count == 0);

            foreach (int idx in candSet)
                info.CandidateTraits.Add(ActiveTraits[idx]);
            info.CandidateTraits.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture));

            _legalCache[world.Id] = info;
            return info;
        }


        public List<WorldTraitDef> GetRandomTraitsForWorld(int worldSeed, WorldDef world)
        {
            List<WorldTraitDef> chosen = new List<WorldTraitDef>();
            if (world == null || world.DisableWorldTraits || world.Rules.Count == 0 || worldSeed == 0)
                return chosen;

            SeedSearcher.KRandomFast rng = new SeedSearcher.KRandomFast();
            rng.Reset(worldSeed);

            List<WorldTraitDef> pool = new List<WorldTraitDef>(ActiveTraits);
            HashSet<string> exTags = new HashSet<string>(StringComparer.Ordinal);
            List<WorldTraitDef> cands = new List<WorldTraitDef>(ActiveTraits.Count);

            for (int r = 0; r < world.Rules.Count; r++)
            {
                TraitRuleDef rule = world.Rules[r];
                for (int s = 0; s < rule.SpecificTraits.Count; s++)
                {
                    WorldTraitDef st;
                    if (TraitsById.TryGetValue(rule.SpecificTraits[s], out st))
                        chosen.Add(st);
                }

                cands.Clear();
                for (int i = 0; i < pool.Count; i++)
                    if (TraitMatchesRule(pool[i], rule, world))
                        cands.Add(pool[i]);

                int count = rng.NextRange(rule.Min, Math.Max(rule.Min, rule.Max + 1));
                int target = chosen.Count + count;
                while (chosen.Count < target && cands.Count > 0)
                {
                    int idx = rng.NextMax(cands.Count);
                    WorldTraitDef pick = cands[idx];
                    bool conflict = false;
                    for (int e = 0; e < pick.ExclusiveWith.Count; e++)
                    {
                        string exId = pick.ExclusiveWith[e];
                        for (int c = 0; c < chosen.Count; c++)
                            if (chosen[c].Id == exId) { conflict = true; break; }
                        if (conflict) break;
                    }
                    if (!conflict)
                    {
                        for (int e = 0; e < pick.ExclusiveWithTags.Count; e++)
                            if (exTags.Contains(pick.ExclusiveWithTags[e])) { conflict = true; break; }
                    }
                    if (!conflict)
                    {
                        chosen.Add(pick);
                        pool.Remove(pick);
                        for (int e = 0; e < pick.ExclusiveWithTags.Count; e++)
                            exTags.Add(pick.ExclusiveWithTags[e]);
                    }
                    cands.RemoveAt(idx);
                }
            }
            return chosen;
        }

        public Dictionary<int, List<WorldTraitDef>> EvaluateClusterSeed(ClusterDef cluster, int seed)
        {
            Dictionary<int, List<WorldTraitDef>> res = new Dictionary<int, List<WorldTraitDef>>();
            if (cluster == null) return res;
            for (int i = 0; i < cluster.Placements.Count; i++)
            {
                WorldPlacementDef wp = cluster.Placements[i];
                int wseed = (seed > 0) ? unchecked(seed + wp.PlacementIndex) : seed;
                res[wp.PlacementIndex] = GetRandomTraitsForWorld(wseed, wp.World);
            }
            return res;
        }
    }

    public static class SeedSearcher
    {
        public const int MaxSearchSeed = 2147483000;



        public sealed class KRandomFast
        {
            readonly int[] SeedArray = new int[56];
            int _inext;
            int _inextp;

            public void Reset(int seed)
            {
                unchecked
                {
                    Array.Clear(SeedArray, 0, 56);
                    int sub = (seed == int.MinValue) ? int.MaxValue : Math.Abs(seed);
                    int mj = 161803398 - sub;
                    SeedArray[55] = mj;
                    int mk = 1;
                    for (int i = 1; i < 55; i++)
                    {
                        int ii = (21 * i) % 55;
                        SeedArray[ii] = mk;
                        mk = mj - mk;
                        if (mk < 0) mk += int.MaxValue;
                        mj = SeedArray[ii];
                    }
                    for (int k = 1; k < 5; k++)
                    {
                        for (int i = 1; i < 56; i++)
                        {
                            SeedArray[i] -= SeedArray[1 + (i + 30) % 55];
                            if (SeedArray[i] < 0) SeedArray[i] += int.MaxValue;
                        }
                    }
                    _inext = 0;
                    _inextp = 21;
                }
            }

            int InternalSample()
            {
                unchecked
                {
                    int loc1 = _inext + 1;
                    int loc2 = _inextp + 1;
                    if (loc1 >= 56) loc1 = 1;
                    if (loc2 >= 56) loc2 = 1;
                    int ret = SeedArray[loc1] - SeedArray[loc2];
                    if (ret == int.MaxValue) ret--;
                    if (ret < 0) ret += int.MaxValue;
                    SeedArray[loc1] = ret;
                    _inext = loc1;
                    _inextp = loc2;
                    return ret;
                }
            }

            public int NextMax(int maxValue)
            {
                return (int)(InternalSample() * 4.6566128752457969E-10 * (double)maxValue);
            }

            public int NextRange(int minValue, int maxValue)
            {
                long range = (long)maxValue - (long)minValue;
                return (int)(InternalSample() * 4.6566128752457969E-10 * (double)range) + minValue;
            }
        }

        sealed class CompiledRule
        {
            public int Min;
            public int Max;
            public int[] SpecificIdx;
            public int[] CandIdx;
        }

        sealed class CompiledWorldCheck
        {
            public int PlacementIndex;
            public bool DisableTraits;
            public ulong RequiredMask;
            public ulong ForbiddenMask;
            public bool RequireNoTraits;
            public int RequiredCount;
            public CompiledRule[] Rules;
        }

        static int PopCount(ulong v)
        {
            int c = 0;
            while (v != 0) { v &= (v - 1); c++; }
            return c;
        }

        public static string FormatCoordinate(string prefix, int seed, string templateCoord)
        {
            string pfx = string.IsNullOrEmpty(prefix) ? "V-SNDST-C" : prefix;
            string sSeed = seed.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(templateCoord))
            {
                string trimmed = templateCoord.Trim();


                Match m = Regex.Match(trimmed, @"^.+?-\d+-(.+)$");
                if (m.Success && m.Groups[1].Value.Length > 0)
                    return pfx + "-" + sSeed + "-" + m.Groups[1].Value;
            }
            return pfx + "-" + sSeed + "-0-0-0";
        }

        public static bool ValidateConstraints(WorldGenCatalog cat, ClusterDef cluster, List<PlanetConstraint> constraints, out string errorMsg)
        {
            errorMsg = null;
            if (cat == null || cluster == null) { errorMsg = "未加载星群数据。"; return false; }
            if (cat.ActiveTraits.Count > 64) { errorMsg = "特质数量超过 64，当前版本不支持。"; return false; }

            bool anyConstraint = false;
            for (int c = 0; c < constraints.Count; c++)
            {
                PlanetConstraint pc = constraints[c];
                if (pc.RequiredMask == 0 && pc.ForbiddenMask == 0 && !pc.RequireNoTraits) continue;
                anyConstraint = true;

                WorldPlacementDef wp = null;
                for (int i = 0; i < cluster.Placements.Count; i++)
                    if (cluster.Placements[i].PlacementIndex == pc.PlacementIndex) { wp = cluster.Placements[i]; break; }
                if (wp == null || wp.World == null) continue;

                WorldTraitLegalInfo legal = cat.GetLegalTraits(wp.World);
                string wname = wp.World.DisplayName;

                if ((pc.RequiredMask & pc.ForbiddenMask) != 0)
                {
                    errorMsg = wname + "：存在同时被「必选」和「排除」的特质。";
                    return false;
                }

                if (pc.RequireNoTraits)
                {
                    if (pc.RequiredMask != 0)
                    {
                        errorMsg = wname + "：不能同时勾选「要求无特质」和指定特质。";
                        return false;
                    }
                    if (legal.FixedTraits.Count > 0 || legal.MinTraits > 0)
                    {
                        errorMsg = wname + "：游戏规则至少需要 " + Math.Max(legal.MinTraits, legal.FixedTraits.Count) + " 个特质，无法刷出「无特质」。";
                        return false;
                    }
                    continue;
                }

                if (pc.RequiredMask != 0 && legal.IsFixedNone)
                {
                    errorMsg = wname + "：该行星固定无特质，不能指定特质。";
                    return false;
                }

                int reqCount = PopCount(pc.RequiredMask);
                if (reqCount > legal.MaxTraits)
                {
                    errorMsg = string.Format("{0}：已选 {1} 个特质，但该行星最多只能生成 {2} 个特质。", wname, reqCount, legal.MaxTraits);
                    return false;
                }


                ulong fixedMask = 0;
                for (int f = 0; f < legal.FixedTraits.Count; f++)
                    fixedMask |= (1UL << legal.FixedTraits[f].Index);

                ulong legalMask = fixedMask;
                for (int l = 0; l < legal.CandidateTraits.Count; l++)
                    legalMask |= (1UL << legal.CandidateTraits[l].Index);

                if ((pc.RequiredMask & ~legalMask) != 0)
                {
                    errorMsg = wname + "：包含该行星规则禁止的特质。";
                    return false;
                }

                List<WorldTraitDef> reqList = new List<WorldTraitDef>();
                for (int t = 0; t < cat.ActiveTraits.Count; t++)
                    if ((pc.RequiredMask & (1UL << t)) != 0)
                        reqList.Add(cat.ActiveTraits[t]);

                for (int a = 0; a < reqList.Count; a++)
                {
                    WorldTraitDef ta = reqList[a];
                    for (int b = a + 1; b < reqList.Count; b++)
                    {
                        WorldTraitDef tb = reqList[b];
                        if ((ta.ExTagMask & tb.ExTagMask) != 0)
                        {
                            errorMsg = string.Format("{0}：「{1}」与「{2}」同属互斥分类，不能同时出现。", wname, ta.DisplayName, tb.DisplayName);
                            return false;
                        }
                        bool aExclB = (ta.ExWithMask & (1UL << tb.Index)) != 0;
                        bool bExclA = (tb.ExWithMask & (1UL << ta.Index)) != 0;
                        if (aExclB && bExclA)
                        {
                            errorMsg = string.Format("{0}：「{1}」与「{2}」互相排斥，不能同时出现。", wname, ta.DisplayName, tb.DisplayName);
                            return false;
                        }
                    }
                }
            }

            if (!anyConstraint)
            {
                errorMsg = "请先在左侧选择至少一颗行星，并勾选希望刷出的特质（或「要求无特质」）。";
                return false;
            }

            if (cluster.FixedCoordinate > 0)
            {

                Dictionary<int, List<WorldTraitDef>> preview = cat.EvaluateClusterSeed(cluster, cluster.FixedCoordinate);
                for (int i = 0; i < constraints.Count; i++)
                {
                    PlanetConstraint pc = constraints[i];
                    List<WorldTraitDef> got;
                    if (!preview.TryGetValue(pc.PlacementIndex, out got)) got = new List<WorldTraitDef>();
                    ulong gotMask = 0;
                    for (int k = 0; k < got.Count; k++) gotMask |= (1UL << got[k].Index);
                    if (pc.RequireNoTraits && got.Count != 0 ||
                        (gotMask & pc.RequiredMask) != pc.RequiredMask ||
                        (gotMask & pc.ForbiddenMask) != 0)
                    {
                        errorMsg = cluster.DisplayName + " 为固定种子（" + cluster.FixedCoordinate + "），不支持自由刷新出所选条件。";
                        return false;
                    }
                }
            }

            return true;
        }

        static CompiledWorldCheck[] CompileChecks(WorldGenCatalog cat, ClusterDef cluster, List<PlanetConstraint> constraints)
        {
            List<CompiledWorldCheck> list = new List<CompiledWorldCheck>();
            for (int c = 0; c < constraints.Count; c++)
            {
                PlanetConstraint pc = constraints[c];
                if (pc.RequiredMask == 0 && pc.ForbiddenMask == 0 && !pc.RequireNoTraits) continue;

                WorldPlacementDef wp = null;
                for (int i = 0; i < cluster.Placements.Count; i++)
                    if (cluster.Placements[i].PlacementIndex == pc.PlacementIndex) { wp = cluster.Placements[i]; break; }
                if (wp == null || wp.World == null) continue;

                WorldDef w = wp.World;
                CompiledWorldCheck ck = new CompiledWorldCheck
                {
                    PlacementIndex = pc.PlacementIndex,
                    DisableTraits = w.DisableWorldTraits || w.Rules.Count == 0,
                    RequiredMask = pc.RequiredMask,
                    ForbiddenMask = pc.ForbiddenMask,
                    RequireNoTraits = pc.RequireNoTraits,
                    RequiredCount = PopCount(pc.RequiredMask),
                    Rules = new CompiledRule[w.Rules.Count]
                };

                for (int r = 0; r < w.Rules.Count; r++)
                {
                    TraitRuleDef rule = w.Rules[r];
                    List<int> spIdx = new List<int>();
                    for (int s = 0; s < rule.SpecificTraits.Count; s++)
                    {
                        WorldTraitDef st;
                        if (cat.TraitsById.TryGetValue(rule.SpecificTraits[s], out st))
                            spIdx.Add(st.Index);
                    }
                    List<int> candIdx = new List<int>();
                    for (int t = 0; t < cat.ActiveTraits.Count; t++)
                        if (cat.TraitMatchesRule(cat.ActiveTraits[t], rule, w))
                            candIdx.Add(t);

                    ck.Rules[r] = new CompiledRule
                    {
                        Min = rule.Min,
                        Max = rule.Max,
                        SpecificIdx = spIdx.ToArray(),
                        CandIdx = candIdx.ToArray()
                    };
                }
                list.Add(ck);
            }


            list.Sort((a, b) =>
            {
                int sa = a.RequireNoTraits ? 100 : a.RequiredCount * 10 + (a.PlacementIndex == 0 ? 5 : 0);
                int sb = b.RequireNoTraits ? 100 : b.RequiredCount * 10 + (b.PlacementIndex == 0 ? 5 : 0);
                return sb.CompareTo(sa);
            });
            return list.ToArray();
        }

        static bool MatchesWorldFast(
            CompiledWorldCheck ck,
            int worldSeed,
            KRandomFast rng,
            ulong[] traitExWith,
            ulong[] traitExTags,
            int traitCount,
            bool[] inPool,
            int[] cands)
        {
            if (ck.DisableTraits || worldSeed == 0)
                return ck.RequiredMask == 0;

            rng.Reset(worldSeed);
            for (int i = 0; i < traitCount; i++) inPool[i] = true;

            ulong chosenIdsMask = 0;
            ulong chosenExTagMask = 0;
            int chosenCount = 0;

            for (int r = 0; r < ck.Rules.Length; r++)
            {
                CompiledRule rule = ck.Rules[r];
                for (int s = 0; s < rule.SpecificIdx.Length; s++)
                {
                    int sid = rule.SpecificIdx[s];
                    ulong sbit = 1UL << sid;
                    if ((ck.ForbiddenMask & sbit) != 0) return false;
                    chosenIdsMask |= sbit;
                    chosenCount++;
                }

                int candLen = 0;
                int[] src = rule.CandIdx;
                for (int i = 0; i < src.Length; i++)
                {
                    int idx = src[i];
                    if (inPool[idx]) cands[candLen++] = idx;
                }

                int count = rng.NextRange(rule.Min, Math.Max(rule.Min, rule.Max + 1));



                if (ck.RequireNoTraits && chosenCount == 0 && count > 0 && candLen > 0)
                    return false;


                if (r == ck.Rules.Length - 1)
                {
                    int missingReq = PopCount(ck.RequiredMask & ~chosenIdsMask);
                    if (missingReq > count || missingReq > candLen) return false;
                }

                int target = chosenCount + count;
                while (chosenCount < target && candLen > 0)
                {
                    int pickPos = rng.NextMax(candLen);
                    int pick = cands[pickPos];

                    if ((chosenIdsMask & traitExWith[pick]) == 0 && (chosenExTagMask & traitExTags[pick]) == 0)
                    {
                        ulong bit = 1UL << pick;
                        if ((ck.ForbiddenMask & bit) != 0) return false;
                        chosenIdsMask |= bit;
                        chosenExTagMask |= traitExTags[pick];
                        inPool[pick] = false;
                        chosenCount++;

                        if (r == ck.Rules.Length - 1)
                        {
                            int remDraws = target - chosenCount;
                            int remReq = PopCount(ck.RequiredMask & ~chosenIdsMask);
                            if (remReq > remDraws) return false;
                        }
                    }


                    int tail = candLen - 1 - pickPos;
                    if (tail > 0) Array.Copy(cands, pickPos + 1, cands, pickPos, tail);
                    candLen--;
                }
            }

            if (ck.RequireNoTraits) return chosenCount == 0;
            if ((chosenIdsMask & ck.RequiredMask) != ck.RequiredMask) return false;
            if ((chosenIdsMask & ck.ForbiddenMask) != 0) return false;
            return true;
        }

        public static SeedSearchResult SearchSync(
            WorldGenCatalog cat,
            ClusterDef cluster,
            List<PlanetConstraint> constraints,
            int startSeed,
            string templateCoord,
            SearchControl ctrl,
            Action<long, double> onProgress)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (cluster.FixedCoordinate > 0)
            {
                SeedSearchResult fixedRes = new SeedSearchResult
                {
                    Seed = cluster.FixedCoordinate,
                    Coordinate = FormatCoordinate(cluster.CoordinatePrefix, cluster.FixedCoordinate, templateCoord),
                    SeedsTested = 1,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                    PlanetTraits = cat.EvaluateClusterSeed(cluster, cluster.FixedCoordinate)
                };
                return fixedRes;
            }

            CompiledWorldCheck[] checks = CompileChecks(cat, cluster, constraints);
            int traitCount = cat.ActiveTraits.Count;
            ulong[] traitExWith = new ulong[traitCount];
            ulong[] traitExTags = new ulong[traitCount];
            for (int i = 0; i < traitCount; i++)
            {
                traitExWith[i] = cat.ActiveTraits[i].ExWithMask;
                traitExTags[i] = cat.ActiveTraits[i].ExTagMask;
            }

            if (startSeed < 1 || startSeed > MaxSearchSeed) startSeed = 1;
            int totalRange = MaxSearchSeed;
            long cursorOffset = -16384;
            const int BatchSize = 16384;

            int workerCount = Math.Max(1, Math.Min(Environment.ProcessorCount, 16));
            int foundSeed = 0;
            object foundLock = new object();
            ManualResetEventSlim doneEvt = new ManualResetEventSlim(false);
            int finishedWorkers = 0;

            Thread[] threads = new Thread[workerCount];
            for (int w = 0; w < workerCount; w++)
            {
                threads[w] = new Thread(() =>
                {
                    KRandomFast rng = new KRandomFast();
                    bool[] inPool = new bool[traitCount];
                    int[] cands = new int[traitCount];

                    while (!ctrl.Stop && VolatileReadInt(ref foundSeed) == 0)
                    {
                        long batchStart = Interlocked.Add(ref cursorOffset, BatchSize);
                        if (batchStart >= totalRange) break;
                        int count = (int)Math.Min(BatchSize, totalRange - batchStart);

                        for (int i = 0; i < count; i++)
                        {
                            if (ctrl.Stop || (i & 255) == 0 && VolatileReadInt(ref foundSeed) != 0)
                                break;

                            long off = batchStart + i;
                            int seed = (int)(((long)(startSeed - 1) + off) % MaxSearchSeed) + 1;

                            bool ok = true;
                            for (int c = 0; c < checks.Length; c++)
                            {
                                CompiledWorldCheck ck = checks[c];
                                if (!MatchesWorldFast(ck, seed + ck.PlacementIndex, rng, traitExWith, traitExTags, traitCount, inPool, cands))
                                {
                                    ok = false;
                                    break;
                                }
                            }
                            if (ok)
                            {
                                lock (foundLock)
                                {
                                    if (foundSeed == 0) foundSeed = seed;
                                }
                                Interlocked.Add(ref ctrl.Tested, i + 1);
                                doneEvt.Set();
                                return;
                            }
                        }
                        Interlocked.Add(ref ctrl.Tested, count);
                    }

                    if (Interlocked.Increment(ref finishedWorkers) == workerCount)
                        doneEvt.Set();
                });
                threads[w].IsBackground = true;
                threads[w].Start();
            }

            while (!doneEvt.Wait(120))
            {
                if (ctrl.Stop) break;
                if (onProgress != null)
                    onProgress(Interlocked.Read(ref ctrl.Tested), sw.Elapsed.TotalMilliseconds);
            }

            for (int w = 0; w < workerCount; w++)
                try { threads[w].Join(500); } catch { }
            sw.Stop();
            try { doneEvt.Dispose(); } catch { }

            if (foundSeed == 0) return null;
            return new SeedSearchResult
            {
                Seed = foundSeed,
                Coordinate = FormatCoordinate(cluster.CoordinatePrefix, foundSeed, templateCoord),
                SeedsTested = Interlocked.Read(ref ctrl.Tested),
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                PlanetTraits = cat.EvaluateClusterSeed(cluster, foundSeed)
            };
        }

        static int VolatileReadInt(ref int location)
        {
            return Thread.VolatileRead(ref location);
        }
    }
}
