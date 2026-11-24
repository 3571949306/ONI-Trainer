using System;
using System.IO;
using System.Text;

namespace ONITrainer
{
    public static class TrainerSettings
    {
        static readonly object _lock = new object();
        static bool _loaded;
        static bool _advancedMode;

        public static bool AdvancedMode
        {
            get
            {
                EnsureLoaded();
                return _advancedMode;
            }
        }

        public static event Action AdvancedModeChanged;

        static string SettingsPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ONITrainer");
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static void Load()
        {
            lock (_lock)
            {
                _loaded = true;
                _advancedMode = false;
                try
                {
                    string path = SettingsPath;
                    if (!File.Exists(path)) return;
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string raw = lines[i];
                        if (string.IsNullOrEmpty(raw)) continue;
                        string s = raw.Trim();
                        if (s.Length == 0 || s[0] == '#' || s[0] == ';') continue;
                        int eq = s.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = s.Substring(0, eq).Trim();
                        string v = s.Substring(eq + 1).Trim();
                        if (string.Equals(k, "AdvancedMode", StringComparison.OrdinalIgnoreCase))
                        {
                            _advancedMode = string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
                catch { }
            }
        }

        static void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        public static void SetAdvancedMode(bool enabled)
        {
            bool changed;
            lock (_lock)
            {
                EnsureLoaded();
                changed = (_advancedMode != enabled);
                _advancedMode = enabled;
                SaveInternal();
            }
            if (changed)
            {
                Action cb = AdvancedModeChanged;
                if (cb != null)
                {
                    try { cb(); }
                    catch { }
                }
            }
        }

        static void SaveInternal()
        {
            try
            {
                string path = SettingsPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                string content = "# ONI-Trainer configuration\r\nAdvancedMode=" + (_advancedMode ? "1" : "0") + "\r\n";
                File.WriteAllText(path, content, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
