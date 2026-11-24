using System;
using System.Collections.Generic;
using System.IO;
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
            if (changed) RaiseChanged();
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
