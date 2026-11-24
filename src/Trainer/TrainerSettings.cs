using System;
using System.IO;
using System.Text;

namespace ONITrainer
{
    public static class TrainerSettings
    {
        public static readonly int[] DefaultHotkeys = new int[] { 0x74, 0x75, 0x76, 0x77, 0x78, 0x79 };

        static readonly object _lock = new object();
        static bool _loaded;
        static bool _advancedMode;
        static bool _enableHotkeys = true;
        static int _hotkeyTechAll = 0x74;
        static int _hotkeySkillAdd = 0x75;
        static int _hotkeySkillAll = 0x76;
        static int _hotkeySpawn = 0x77;
        static int _hotkeyTechPath = 0x78;
        static int _hotkeyDupeSkill = 0x79;
        static bool _showInGameToast = true;
        static bool _autoBackupSave = true;
        static bool _autoCheckUpdate;
        static string _lastSeedCoord = "";

        public static bool AdvancedMode { get { EnsureLoaded(); return _advancedMode; } }
        public static bool EnableHotkeys { get { EnsureLoaded(); return _enableHotkeys; } }
        public static int HotkeyTechAll { get { EnsureLoaded(); return _hotkeyTechAll; } }
        public static int HotkeySkillAdd { get { EnsureLoaded(); return _hotkeySkillAdd; } }
        public static int HotkeySkillAll { get { EnsureLoaded(); return _hotkeySkillAll; } }
        public static int HotkeySpawn { get { EnsureLoaded(); return _hotkeySpawn; } }
        public static int HotkeyTechPath { get { EnsureLoaded(); return _hotkeyTechPath; } }
        public static int HotkeyDupeSkill { get { EnsureLoaded(); return _hotkeyDupeSkill; } }
        public static bool ShowInGameToast { get { EnsureLoaded(); return _showInGameToast; } }
        public static bool AutoBackupSave { get { EnsureLoaded(); return _autoBackupSave; } }
        public static bool AutoCheckUpdate { get { EnsureLoaded(); return _autoCheckUpdate; } }
        public static string LastSeedCoord { get { EnsureLoaded(); return _lastSeedCoord; } }

        public static event Action AdvancedModeChanged;
        public static event Action SettingsChanged;

        public static string SettingsPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ONITrainer");
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static int[] GetHotkeys()
        {
            lock (_lock)
            {
                EnsureLoaded();
                return new int[]
                {
                    _hotkeyTechAll, _hotkeySkillAdd, _hotkeySkillAll,
                    _hotkeySpawn, _hotkeyTechPath, _hotkeyDupeSkill
                };
            }
        }

        public static void Load()
        {
            lock (_lock)
            {
                _loaded = true;
                ResetDefaultsLocked();
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
                        int iv;
                        if (string.Equals(k, "AdvancedMode", StringComparison.OrdinalIgnoreCase))
                            _advancedMode = ParseBool(v, false);
                        else if (string.Equals(k, "EnableHotkeys", StringComparison.OrdinalIgnoreCase))
                            _enableHotkeys = ParseBool(v, true);
                        else if (string.Equals(k, "HotkeyTechAll", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out iv))
                            _hotkeyTechAll = ClampVk(iv);
                        else if (string.Equals(k, "HotkeySkillAdd", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out iv))
                            _hotkeySkillAdd = ClampVk(iv);
                        else if (string.Equals(k, "HotkeySkillAll", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out iv))
                            _hotkeySkillAll = ClampVk(iv);
                        else if (string.Equals(k, "HotkeySpawn", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out iv))
                            _hotkeySpawn = ClampVk(iv);
                        else if (string.Equals(k, "HotkeyTechPath", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out iv))
                            _hotkeyTechPath = ClampVk(iv);
                        else if (string.Equals(k, "HotkeyDupeSkill", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out iv))
                            _hotkeyDupeSkill = ClampVk(iv);
                        else if (string.Equals(k, "ShowInGameToast", StringComparison.OrdinalIgnoreCase))
                            _showInGameToast = ParseBool(v, true);
                        else if (string.Equals(k, "AutoBackupSave", StringComparison.OrdinalIgnoreCase))
                            _autoBackupSave = ParseBool(v, true);
                        else if (string.Equals(k, "AutoCheckUpdate", StringComparison.OrdinalIgnoreCase))
                            _autoCheckUpdate = ParseBool(v, false);
                        else if (string.Equals(k, "LastSeedCoord", StringComparison.OrdinalIgnoreCase))
                            _lastSeedCoord = v ?? "";
                    }
                }
                catch { }
            }
        }

        static void ResetDefaultsLocked()
        {
            _advancedMode = false;
            _enableHotkeys = true;
            _hotkeyTechAll = DefaultHotkeys[0];
            _hotkeySkillAdd = DefaultHotkeys[1];
            _hotkeySkillAll = DefaultHotkeys[2];
            _hotkeySpawn = DefaultHotkeys[3];
            _hotkeyTechPath = DefaultHotkeys[4];
            _hotkeyDupeSkill = DefaultHotkeys[5];
            _showInGameToast = true;
            _autoBackupSave = true;
            _autoCheckUpdate = false;
            _lastSeedCoord = "";
        }

        static bool ParseBool(string v, bool defVal)
        {
            if (string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(v, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
                return false;
            return defVal;
        }

        static int ClampVk(int vk)
        {
            if (vk <= 0 || vk > 254) return 0;
            return vk;
        }

        static void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        public static void SetLastSeedCoord(string coord)
        {
            string clean = (coord ?? "").Trim();
            bool changed;
            lock (_lock)
            {
                EnsureLoaded();
                changed = !string.Equals(_lastSeedCoord, clean, StringComparison.Ordinal);
                if (changed)
                {
                    _lastSeedCoord = clean;
                    SaveInternal();
                }
            }
            if (changed) RaiseEvent(SettingsChanged);
        }

        public static void SetAdvancedMode(bool enabled)
        {
            bool advChanged;
            lock (_lock)
            {
                EnsureLoaded();
                advChanged = (_advancedMode != enabled);
                _advancedMode = enabled;
                SaveInternal();
            }
            if (advChanged) RaiseEvent(AdvancedModeChanged);
            if (advChanged) RaiseEvent(SettingsChanged);
        }

        public static void SaveAll(bool advancedMode, bool enableHotkeys, int[] hotkeys6, bool showInGameToast, bool autoBackupSave)
        {
            bool currentAutoCheck;
            lock (_lock)
            {
                EnsureLoaded();
                currentAutoCheck = _autoCheckUpdate;
            }
            SaveAll(advancedMode, enableHotkeys, hotkeys6, showInGameToast, autoBackupSave, currentAutoCheck);
        }

        public static void SaveAll(bool advancedMode, bool enableHotkeys, int[] hotkeys6, bool showInGameToast, bool autoBackupSave, bool autoCheckUpdate)
        {
            bool advChanged;
            lock (_lock)
            {
                EnsureLoaded();
                advChanged = (_advancedMode != advancedMode);
                _advancedMode = advancedMode;
                _enableHotkeys = enableHotkeys;
                if (hotkeys6 != null && hotkeys6.Length >= 6)
                {
                    _hotkeyTechAll = ClampVk(hotkeys6[0]);
                    _hotkeySkillAdd = ClampVk(hotkeys6[1]);
                    _hotkeySkillAll = ClampVk(hotkeys6[2]);
                    _hotkeySpawn = ClampVk(hotkeys6[3]);
                    _hotkeyTechPath = ClampVk(hotkeys6[4]);
                    _hotkeyDupeSkill = ClampVk(hotkeys6[5]);
                }
                _showInGameToast = showInGameToast;
                _autoBackupSave = autoBackupSave;
                _autoCheckUpdate = autoCheckUpdate;
                SaveInternal();
            }
            if (advChanged) RaiseEvent(AdvancedModeChanged);
            RaiseEvent(SettingsChanged);
        }

        static void RaiseEvent(Action cb)
        {
            if (cb != null)
            {
                try { cb(); }
                catch { }
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
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ONI-Trainer configuration");
                sb.AppendLine("AdvancedMode=" + (_advancedMode ? "1" : "0"));
                sb.AppendLine("EnableHotkeys=" + (_enableHotkeys ? "1" : "0"));
                sb.AppendLine("HotkeyTechAll=" + _hotkeyTechAll);
                sb.AppendLine("HotkeySkillAdd=" + _hotkeySkillAdd);
                sb.AppendLine("HotkeySkillAll=" + _hotkeySkillAll);
                sb.AppendLine("HotkeySpawn=" + _hotkeySpawn);
                sb.AppendLine("HotkeyTechPath=" + _hotkeyTechPath);
                sb.AppendLine("HotkeyDupeSkill=" + _hotkeyDupeSkill);
                sb.AppendLine("ShowInGameToast=" + (_showInGameToast ? "1" : "0"));
                sb.AppendLine("AutoBackupSave=" + (_autoBackupSave ? "1" : "0"));
                sb.AppendLine("AutoCheckUpdate=" + (_autoCheckUpdate ? "1" : "0"));
                sb.AppendLine("LastSeedCoord=" + (_lastSeedCoord ?? ""));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public static string FormatVkName(int vk)
        {
            if (vk <= 0) return "禁用";
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
            if (vk >= '0' && vk <= '9') return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);
            switch (vk)
            {
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x24: return "Home";
                case 0x23: return "End";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x13: return "Pause";
                case 0x6A: return "Num*";
                case 0x6B: return "Num+";
                case 0x6D: return "Num-";
                case 0x6F: return "Num/";
                case 0xC0: return "~";
                case 0xBD: return "-";
                case 0xBB: return "=";
                case 0xDB: return "[";
                case 0xDD: return "]";
                case 0xBA: return ";";
                case 0xDE: return "'";
                case 0xBC: return ",";
                case 0xBE: return ".";
                case 0xBF: return "/";
            }
            return string.Format("VK 0x{0:X2}", vk);
        }
    }
}
