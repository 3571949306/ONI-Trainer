using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ONITrainer
{
    public sealed class SeedRecord
    {
        public int Seed;
        public string Coordinate;
        public string ClusterPath;
        public string ClusterName;
        public string StoryCode;
        public string MixingCode;
        public string TraitSummary;
        public string PoiSummary;
        public string Note;
        public string Timestamp;

        public SeedRecord Clone()
        {
            return new SeedRecord
            {
                Seed = Seed,
                Coordinate = Coordinate ?? "",
                ClusterPath = ClusterPath ?? "",
                ClusterName = ClusterName ?? "",
                StoryCode = StoryCode ?? "",
                MixingCode = MixingCode ?? "",
                TraitSummary = TraitSummary ?? "",
                PoiSummary = PoiSummary ?? "",
                Note = Note ?? "",
                Timestamp = Timestamp ?? ""
            };
        }
    }

    public static class SeedStore
    {
        static readonly object _lock = new object();
        static readonly List<SeedRecord> _history = new List<SeedRecord>();
        static List<SeedRecord> _favorites;

        public static event Action StoreChanged;

        public static string FavoritesPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ONITrainer");
                return Path.Combine(dir, "favorites.tsv");
            }
        }

        public static string PreviewsRootDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ONITrainer");
                return Path.Combine(Path.Combine(dir, "favorites"), "previews");
            }
        }

        public static string GetPreviewDir(string coord, int seed)
        {
            string key = MakeKey(coord, seed);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                StringBuilder sb = new StringBuilder(24);
                sb.Append("s").Append(seed).Append('_');
                for (int i = 0; i < 8; i++)
                    sb.Append(hash[i].ToString("x2"));
                return Path.Combine(PreviewsRootDir, sb.ToString());
            }
        }

        public static int GetSavedPreviewCount(string coord, int seed)
        {
            try
            {
                if (seed <= 0 || !IsFavorite(coord, seed)) return 0;
                string dir = GetPreviewDir(coord, seed);
                if (!Directory.Exists(dir)) return 0;
                return Directory.GetFiles(dir, "w*.onip.gz").Length;
            }
            catch
            {
                return 0;
            }
        }

        public static void SaveFavoritePreviews(string coord, int seed, Dictionary<int, MapPreviewData> maps)
        {
            if (seed <= 0 || maps == null || maps.Count == 0) return;
            try
            {
                string dir = GetPreviewDir(coord, seed);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                foreach (MapPreviewData md in maps.Values)
                {
                    if (md == null) continue;
                    string fp = Path.Combine(dir, "w" + md.WorldIndex + ".onip.gz");
                    md.SaveToGzipFile(fp);
                }
            }
            catch { }
        }

        public static void SaveSingleFavoritePreview(string coord, int seed, MapPreviewData data)
        {
            if (seed <= 0 || data == null) return;
            try
            {
                string dir = GetPreviewDir(coord, seed);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string fp = Path.Combine(dir, "w" + data.WorldIndex + ".onip.gz");
                data.SaveToGzipFile(fp);

                Dictionary<int, MapPreviewData> all = LoadFavoritePreviews(coord, seed);
                string sum = BuildPoiSummaryFromMaps(all);
                if (!string.IsNullOrEmpty(sum))
                {
                    bool changed = false;
                    lock (_lock)
                    {
                        EnsureFavoritesLoadedLocked();
                        string k = MakeKey(coord, seed);
                        for (int i = 0; i < _favorites.Count; i++)
                        {
                            if (string.Equals(MakeKey(_favorites[i].Coordinate, _favorites[i].Seed), k, StringComparison.Ordinal))
                            {
                                _favorites[i].PoiSummary = sum;
                                changed = true;
                            }
                        }
                        if (changed) SaveFavoritesLocked(FavoritesPath);
                    }
                    if (changed) RaiseChanged();
                }
            }
            catch { }
        }

        static string BuildPoiSummaryFromMaps(Dictionary<int, MapPreviewData> maps)
        {
            if (maps == null || maps.Count == 0) return "";
            List<int> keys = new List<int>(maps.Keys);
            keys.Sort();
            List<string> ps = new List<string>();
            for (int i = 0; i < keys.Count; i++)
            {
                MapPreviewData md = maps[keys[i]];
                if (md == null) continue;
                int c = 0;
                for (int p = 0; p < md.Pois.Count; p++)
                    if (md.Pois[p].Category != "start") c++;
                ps.Add(md.WorldName + "(" + c + "POI)");
            }
            return string.Join(", ", ps.ToArray());
        }

        public static Dictionary<int, MapPreviewData> LoadFavoritePreviews(string coord, int seed)
        {
            Dictionary<int, MapPreviewData> res = new Dictionary<int, MapPreviewData>();
            if (seed <= 0) return res;
            try
            {
                string dir = GetPreviewDir(coord, seed);
                if (!Directory.Exists(dir)) return res;
                string[] files = Directory.GetFiles(dir, "w*.onip.gz");
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        MapPreviewData d = MapPreviewData.LoadFromFile(files[i]);
                        if (d != null) res[d.WorldIndex] = d;
                    }
                    catch { }
                }
            }
            catch { }
            return res;
        }

        public static void DeleteFavoritePreviews(string coord, int seed)
        {
            if (seed <= 0) return;
            try
            {
                string dir = GetPreviewDir(coord, seed);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch { }
        }

        public static void UpsertFavorite(SeedRecord rec, Dictionary<int, MapPreviewData> maps)
        {
            if (rec == null || rec.Seed <= 0) return;
            lock (_lock)
            {
                EnsureFavoritesLoadedLocked();
                SeedRecord item = rec.Clone();
                if (string.IsNullOrEmpty(item.Timestamp))
                    item.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (string.IsNullOrEmpty(item.PoiSummary) && maps != null && maps.Count > 0)
                    item.PoiSummary = BuildPoiSummaryFromMaps(maps);

                string k = MakeKey(item.Coordinate, item.Seed);
                int idx = -1;
                for (int i = 0; i < _favorites.Count; i++)
                {
                    if (string.Equals(MakeKey(_favorites[i].Coordinate, _favorites[i].Seed), k, StringComparison.Ordinal))
                    {
                        idx = i;
                        break;
                    }
                }
                if (idx >= 0)
                {
                    SeedRecord existing = _favorites[idx];
                    if (string.IsNullOrEmpty(item.Note)) item.Note = existing.Note;
                    if (string.IsNullOrEmpty(item.TraitSummary)) item.TraitSummary = existing.TraitSummary;
                    if (string.IsNullOrEmpty(item.PoiSummary)) item.PoiSummary = existing.PoiSummary;
                    if (string.IsNullOrEmpty(item.ClusterPath)) item.ClusterPath = existing.ClusterPath;
                    if (string.IsNullOrEmpty(item.ClusterName)) item.ClusterName = existing.ClusterName;
                    _favorites[idx] = item;
                }
                else
                {
                    _favorites.Insert(0, item);
                }
                SaveFavoritesLocked(FavoritesPath);
            }
            if (maps != null && maps.Count > 0)
                SaveFavoritePreviews(rec.Coordinate, rec.Seed, maps);
            RaiseChanged();
        }

        static string MakeKey(string coord, int seed)
        {
            return (coord ?? "").Trim().ToUpperInvariant() + "#" + seed;
        }

        public static List<SeedRecord> GetHistory()
        {
            lock (_lock)
            {
                List<SeedRecord> copy = new List<SeedRecord>(_history.Count);
                for (int i = 0; i < _history.Count; i++) copy.Add(_history[i].Clone());
                return copy;
            }
        }

        public static List<SeedRecord> GetFavorites()
        {
            lock (_lock)
            {
                EnsureFavoritesLoadedLocked();
                List<SeedRecord> copy = new List<SeedRecord>(_favorites.Count);
                for (int i = 0; i < _favorites.Count; i++) copy.Add(_favorites[i].Clone());
                return copy;
            }
        }

        public static void AddHistory(SeedRecord rec)
        {
            if (rec == null || rec.Seed <= 0) return;
            lock (_lock)
            {
                SeedRecord item = rec.Clone();
                if (string.IsNullOrEmpty(item.Timestamp))
                    item.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string k = MakeKey(item.Coordinate, item.Seed);
                for (int i = _history.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(MakeKey(_history[i].Coordinate, _history[i].Seed), k, StringComparison.Ordinal))
                        _history.RemoveAt(i);
                }
                _history.Insert(0, item);
                if (_history.Count > 100) _history.RemoveRange(100, _history.Count - 100);
            }
            RaiseChanged();
        }

        public static bool IsFavorite(string coord, int seed)
        {
            lock (_lock)
            {
                EnsureFavoritesLoadedLocked();
                string k = MakeKey(coord, seed);
                for (int i = 0; i < _favorites.Count; i++)
                {
                    if (string.Equals(MakeKey(_favorites[i].Coordinate, _favorites[i].Seed), k, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
        }

        public static bool ToggleFavorite(SeedRecord rec)
        {
            if (rec == null || rec.Seed <= 0) return false;
            bool nowFav;
            lock (_lock)
            {
                EnsureFavoritesLoadedLocked();
                string k = MakeKey(rec.Coordinate, rec.Seed);
                int idx = -1;
                for (int i = 0; i < _favorites.Count; i++)
                {
                    if (string.Equals(MakeKey(_favorites[i].Coordinate, _favorites[i].Seed), k, StringComparison.Ordinal))
                    {
                        idx = i;
                        break;
                    }
                }
                if (idx >= 0)
                {
                    _favorites.RemoveAt(idx);
                    nowFav = false;
                }
                else
                {
                    SeedRecord item = rec.Clone();
                    if (string.IsNullOrEmpty(item.Timestamp))
                        item.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    _favorites.Insert(0, item);
                    nowFav = true;
                }
                SaveFavoritesLocked(FavoritesPath);
            }
            if (!nowFav) DeleteFavoritePreviews(rec.Coordinate, rec.Seed);
            RaiseChanged();
            return nowFav;
        }

        public static void RemoveFavorite(string coord, int seed)
        {
            bool changed = false;
            lock (_lock)
            {
                EnsureFavoritesLoadedLocked();
                string k = MakeKey(coord, seed);
                for (int i = _favorites.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(MakeKey(_favorites[i].Coordinate, _favorites[i].Seed), k, StringComparison.Ordinal))
                    {
                        _favorites.RemoveAt(i);
                        changed = true;
                    }
                }
                if (changed) SaveFavoritesLocked(FavoritesPath);
            }
            if (changed)
            {
                DeleteFavoritePreviews(coord, seed);
                RaiseChanged();
            }
        }

        public static void UpdateNote(string coord, int seed, string note)
        {
            string cleanNote = note ?? "";
            bool favChanged = false;
            bool histChanged = false;
            lock (_lock)
            {
                EnsureFavoritesLoadedLocked();
                string k = MakeKey(coord, seed);
                for (int i = 0; i < _favorites.Count; i++)
                {
                    if (string.Equals(MakeKey(_favorites[i].Coordinate, _favorites[i].Seed), k, StringComparison.Ordinal))
                    {
                        _favorites[i].Note = cleanNote;
                        favChanged = true;
                    }
                }
                for (int i = 0; i < _history.Count; i++)
                {
                    if (string.Equals(MakeKey(_history[i].Coordinate, _history[i].Seed), k, StringComparison.Ordinal))
                    {
                        _history[i].Note = cleanNote;
                        histChanged = true;
                    }
                }
                if (favChanged) SaveFavoritesLocked(FavoritesPath);
            }
            if (favChanged || histChanged) RaiseChanged();
        }

        public static List<SeedRecord> LoadFavoritesFromPath(string path)
        {
            List<SeedRecord> list = new List<SeedRecord>();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] cols = line.Split('\t');
                    if (cols.Length < 4) continue;
                    int seed;
                    if (!int.TryParse(Unescape(cols[0]), out seed) || seed <= 0) continue;
                    list.Add(new SeedRecord
                    {
                        Seed = seed,
                        Coordinate = Unescape(cols.Length > 1 ? cols[1] : ""),
                        ClusterPath = Unescape(cols.Length > 2 ? cols[2] : ""),
                        ClusterName = Unescape(cols.Length > 3 ? cols[3] : ""),
                        StoryCode = Unescape(cols.Length > 4 ? cols[4] : ""),
                        MixingCode = Unescape(cols.Length > 5 ? cols[5] : ""),
                        TraitSummary = Unescape(cols.Length > 6 ? cols[6] : ""),
                        PoiSummary = Unescape(cols.Length > 7 ? cols[7] : ""),
                        Note = Unescape(cols.Length > 8 ? cols[8] : ""),
                        Timestamp = Unescape(cols.Length > 9 ? cols[9] : "")
                    });
                }
            }
            catch { }
            return list;
        }

        public static void SaveFavoritesToPath(string path, List<SeedRecord> items)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ONI-Trainer seed favorites (TSV)");
                for (int i = 0; i < items.Count; i++)
                {
                    SeedRecord r = items[i];
                    sb.Append(r.Seed).Append('\t')
                      .Append(Escape(r.Coordinate)).Append('\t')
                      .Append(Escape(r.ClusterPath)).Append('\t')
                      .Append(Escape(r.ClusterName)).Append('\t')
                      .Append(Escape(r.StoryCode)).Append('\t')
                      .Append(Escape(r.MixingCode)).Append('\t')
                      .Append(Escape(r.TraitSummary)).Append('\t')
                      .Append(Escape(r.PoiSummary)).Append('\t')
                      .Append(Escape(r.Note)).Append('\t')
                      .AppendLine(Escape(r.Timestamp));
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        static void EnsureFavoritesLoadedLocked()
        {
            if (_favorites != null) return;
            _favorites = LoadFavoritesFromPath(FavoritesPath);
        }

        static void SaveFavoritesLocked(string path)
        {
            if (_favorites == null) return;
            SaveFavoritesToPath(path, _favorites);
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\' && i + 1 < s.Length)
                {
                    char nx = s[++i];
                    if (nx == 't') sb.Append('\t');
                    else if (nx == 'r') sb.Append('\r');
                    else if (nx == 'n') sb.Append('\n');
                    else sb.Append(nx);
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        static void RaiseChanged()
        {
            Action cb = StoreChanged;
            if (cb != null)
            {
                try { cb(); }
                catch { }
            }
        }
    }
}
