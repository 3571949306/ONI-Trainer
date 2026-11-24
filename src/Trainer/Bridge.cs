using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ONITrainer
{

    public class MonoInfo
    {
        public ulong VtTech, VtSkill, VtResearchScreen;
        public int OffComplete, OffXp, OffMouse, OffIdentity, OffIdentityName, OffCurrentResearch, OffTech;
        public List<long> ResearchScreenObjects = new List<long>();
        public int OffElemId, OffElemIdx, OffElemMaxMass, OffElemName, OffElemState, OffElemTag;
        public int OffMoraleMod, OffMoraleVal;

        public long GridElemField, GridMassField, GridWidthField, GridCellCountField;
        public long PlayerField, ElementsField;
        public long EarnedField, CycleField, PowerField;

        public long ElemData, MassData, PlayerInstance, ElemList;
        public int Width, CellCount, Earned, Cycle, ElemCount;
        public float Power;

        public List<long> TechObjects = new List<long>();
        public List<long> SkillObjects = new List<long>();
    }

    public class Bridge
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, UIntPtr size, out UIntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateRemoteThread(IntPtr h, IntPtr sa, UIntPtr stack, IntPtr start, IntPtr arg, uint flags, out uint tid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualFreeEx(IntPtr h, IntPtr addr, UIntPtr size, uint type);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern IntPtr CreateFileA(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern bool WaitNamedPipeA(string name, uint timeoutMs);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetNamedPipeHandleState(IntPtr h, ref uint mode, IntPtr maxCollection, IntPtr collectTimeout);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(IntPtr h, IntPtr buf, uint n, out uint written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(IntPtr h, IntPtr buf, uint n, out uint read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetOverlappedResult(IntPtr h, IntPtr ov, out uint transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CancelIoEx(IntPtr h, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateEventA(IntPtr sa, bool manualReset, bool initialState, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Module32FirstW(IntPtr snap, ref MODULEENTRY32W me);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Module32NextW(IntPtr snap, ref MODULEENTRY32W me);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MODULEENTRY32W
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public IntPtr modBaseAddr;
            public uint modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExePath;
        }

        const uint TH32CS_SNAPMODULE = 0x00000008;
        const uint TH32CS_SNAPMODULE32 = 0x00000010;
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        const uint PIPE_READMODE_MESSAGE = 0x00000002;
        const int ERROR_IO_PENDING = 997;
        const int ERROR_PIPE_BUSY = 231;
        const int ERROR_MORE_DATA = 234;
        static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        readonly object _ioLock = new object();
        string _name;

        public static string RuntimeDir
        {
            get
            {
                string dir = Path.Combine(Path.GetTempPath(), "ONITrainer");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public static string EnsureBridgeDll(out string err)
        {
            err = null;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("ONIBridge.dll"))
                {
                    if (s != null)
                    {
                        byte[] data;
                        using (MemoryStream ms = new MemoryStream())
                        {
                            s.CopyTo(ms);
                            data = ms.ToArray();
                        }
                        string hash8;
                        using (SHA256 sha = SHA256.Create())
                        {
                            byte[] h = sha.ComputeHash(data);
                            StringBuilder sb = new StringBuilder(8);
                            for (int i = 0; i < 4; i++) sb.Append(h[i].ToString("x2"));
                            hash8 = sb.ToString();
                        }
                        string dir = RuntimeDir;
                        string dst = Path.Combine(dir, "ONIBridge_" + hash8 + ".dll");
                        if (!File.Exists(dst) || new FileInfo(dst).Length != data.Length)
                            File.WriteAllBytes(dst, data);
                        try
                        {
                            foreach (string old in Directory.GetFiles(dir, "ONIBridge_*.dll"))
                            {
                                if (!string.Equals(old, dst, StringComparison.OrdinalIgnoreCase))
                                    try { File.Delete(old); } catch { }
                            }
                        }
                        catch { }
                        return dst;
                    }
                }
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ONIBridge.dll");
                if (File.Exists(local)) return local;
                err = "缺少运行所需组件，请重新下载修改器";
                return null;
            }
            catch (Exception e)
            {
                err = "初始化失败：" + e.Message;
                return null;
            }
        }

        public static bool HasMonoModule(int pid)
        {
            if (pid <= 0) return false;
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)pid);
            if (snap == INVALID_HANDLE_VALUE || snap == IntPtr.Zero) return false;
            try
            {
                MODULEENTRY32W me = new MODULEENTRY32W();
                me.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));
                if (!Module32FirstW(snap, ref me)) return false;
                do
                {
                    if (string.Equals(me.szModule, "mono-2.0-bdwgc.dll", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                while (Module32NextW(snap, ref me));
                return false;
            }
            catch { return false; }
            finally { CloseHandle(snap); }
        }

        public static bool IsProcessAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return !p.HasExited;
            }
            catch { return false; }
        }

        public bool QuerySaveState(out bool inSave, out long gamePtr, out int width, out int height)
        {
            inSave = false; gamePtr = 0; width = 0; height = 0;
            string r = Cmd("SAVESTATE", 400, 1000);
            if (string.IsNullOrEmpty(r) || !r.StartsWith("OK")) return false;
            if (!r.StartsWith("OK insave")) return true;
            Match mg = Regex.Match(r, @"game:([0-9a-fA-F]+)");
            Match mw = Regex.Match(r, @"w:(\d+)");
            Match mh = Regex.Match(r, @"h:(\d+)");
            if (mg.Success) long.TryParse(mg.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out gamePtr);
            if (mw.Success) int.TryParse(mw.Groups[1].Value, out width);
            if (mh.Success) int.TryParse(mh.Groups[1].Value, out height);
            inSave = (gamePtr != 0 && width > 0);
            return true;
        }

        bool Ping()
        {
            string pong = Cmd("PING", 400, 1200);
            return pong != null && pong.StartsWith("OK");
        }

        static bool WaitPing(Bridge b, int attempts, int delayMs)
        {
            for (int i = 0; i < attempts; i++)
            {
                if (b.Ping()) return true;
                Thread.Sleep(delayMs);
            }
            return false;
        }

        public static Bridge Connect(int pid, string dllPath, out string err)
        {
            err = null;
            string dllDir = string.IsNullOrEmpty(dllPath) ? RuntimeDir : Path.GetDirectoryName(dllPath);
            string pipe = FindPipe(pid, dllDir);
            if (pipe != null)
            {
                Bridge old = new Bridge { _name = pipe };
                if (WaitPing(old, 6, 200)) return old;
                pipe = null;
            }

            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                err = "缺少运行所需组件，请重新下载修改器";
                return null;
            }
            if (!Inject(pid, dllPath, out err)) return null;

            for (int i = 0; i < 50 && pipe == null; i++)
            {
                if (!IsProcessAlive(pid)) { err = "游戏已退出"; return null; }
                Thread.Sleep(200);
                pipe = FindPipe(pid, dllDir);
            }
            if (pipe == null) { err = "连接游戏失败，请检查安全软件是否拦截了修改器"; return null; }

            Bridge b = new Bridge { _name = pipe };
            if (!WaitPing(b, 20, 250))
            {
                err = "连接超时，请重启修改器后重试";
                return null;
            }
            return b;
        }

        static string FindPipe(int pid, string dllDir)
        {
            string prefix = "oni_trainer_" + pid + "_";

            try
            {
                string best = null;
                foreach (string f in Directory.GetFiles(@"\\.\pipe\"))
                {
                    string n = Path.GetFileName(f);
                    if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) best = n;
                }
                if (best != null) return best;
            }
            catch { }

            string[] dirs = { dllDir, AppDomain.CurrentDomain.BaseDirectory };
            foreach (string d in dirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                try
                {
                    string log = Path.Combine(d, "bridge.log");
                    if (!File.Exists(log)) continue;
                    string last = null;
                    foreach (string line in File.ReadAllLines(log))
                        if (line.Contains("pipe:")) last = line.Substring(line.IndexOf("pipe:") + 5).Trim();
                    if (!string.IsNullOrEmpty(last) && last.StartsWith(@"\\.\pipe\"))
                    {
                        string n = last.Substring(9);
                        if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return n;
                    }
                }
                catch { }
            }
            return null;
        }

        static bool Inject(int pid, string dll, out string err)
        {
            err = null;
            IntPtr h = OpenProcess(0x1F0FFF, false, pid);
            if (h == IntPtr.Zero)
            {
                err = "连接游戏失败，请尝试右键以管理员身份运行修改器";
                return false;
            }

            byte[] path = Encoding.Unicode.GetBytes(dll + "\0");
            IntPtr mem = VirtualAllocEx(h, IntPtr.Zero, (UIntPtr)path.Length, 0x3000, 0x04);
            if (mem == IntPtr.Zero)
            {
                err = "初始化失败（错误码 " + Marshal.GetLastWin32Error() + "）";
                CloseHandle(h);
                return false;
            }
            UIntPtr written;
            if (!WriteProcessMemory(h, mem, path, (UIntPtr)path.Length, out written))
            {
                err = "写入组件路径失败（错误码 " + Marshal.GetLastWin32Error() + "）";
                VirtualFreeEx(h, mem, UIntPtr.Zero, 0x8000);
                CloseHandle(h);
                return false;
            }
            IntPtr k32 = GetModuleHandle("kernel32.dll");
            IntPtr load = GetProcAddress(k32, "LoadLibraryW");
            uint tid;
            IntPtr th = CreateRemoteThread(h, IntPtr.Zero, UIntPtr.Zero, load, mem, 0, out tid);
            if (th == IntPtr.Zero)
            {
                err = "加载组件失败（错误码 " + Marshal.GetLastWin32Error() + "，可能被安全软件拦截）";
                VirtualFreeEx(h, mem, UIntPtr.Zero, 0x8000);
                CloseHandle(h);
                return false;
            }
            WaitForSingleObject(th, 15000);
            CloseHandle(th);
            VirtualFreeEx(h, mem, UIntPtr.Zero, 0x8000);
            CloseHandle(h);
            return true;
        }

        static bool OverlappedIo(IntPtr hPipe, IntPtr hEvent, IntPtr ovPtr, bool isWrite, byte[] buf, int len, int timeoutMs, out uint transferred)
        {
            transferred = 0;
            ResetEvent(hEvent);
            NativeOverlapped ov = new NativeOverlapped { EventHandle = hEvent };
            Marshal.StructureToPtr(ov, ovPtr, false);

            GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = pin.AddrOfPinnedObject();
                uint syncBytes;
                bool ok = isWrite
                    ? WriteFile(hPipe, ptr, (uint)len, out syncBytes, ovPtr)
                    : ReadFile(hPipe, ptr, (uint)len, out syncBytes, ovPtr);
                if (ok)
                {
                    transferred = syncBytes;
                    return true;
                }
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_MORE_DATA && !isWrite)
                {
                    transferred = syncBytes;
                    return true;
                }
                if (err != ERROR_IO_PENDING) return false;

                uint wr = WaitForSingleObject(hEvent, (uint)Math.Max(1, timeoutMs));
                if (wr == 0)
                {
                    if (GetOverlappedResult(hPipe, ovPtr, out transferred, false)) return true;
                    return !isWrite && Marshal.GetLastWin32Error() == ERROR_MORE_DATA && transferred > 0;
                }



                CancelIoEx(hPipe, ovPtr);
                uint dummy;
                GetOverlappedResult(hPipe, ovPtr, out dummy, true);
                return false;
            }
            finally
            {
                pin.Free();
            }
        }

        public string Cmd(string cmd) { return Cmd(cmd, 2000, 8000); }

        public string Cmd(string cmd, int connectTimeoutMs, int ioTimeoutMs)
        {
            if (string.IsNullOrEmpty(_name)) return "ERR no-pipe";
            string fullPath = @"\\.\pipe\" + _name;
            bool taken = false;
            try
            {
                Monitor.TryEnter(_ioLock, Math.Max(1, connectTimeoutMs), ref taken);
                if (!taken) return "ERR busy";
                IntPtr hPipe = INVALID_HANDLE_VALUE;
                IntPtr hEvent = IntPtr.Zero;
                IntPtr ovPtr = IntPtr.Zero;
                try
                {
                    int deadline = Environment.TickCount + Math.Max(1, connectTimeoutMs);
                    while (true)
                    {
                        hPipe = CreateFileA(fullPath, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                        if (hPipe != INVALID_HANDLE_VALUE) break;
                        int err = Marshal.GetLastWin32Error();
                        int rem = deadline - Environment.TickCount;
                        if (rem <= 0) return "ERR timeout";
                        if (err == ERROR_PIPE_BUSY)
                        {
                            WaitNamedPipeA(fullPath, (uint)Math.Min(rem, 100));
                        }
                        else if (err == 2)
                        {

                            Thread.Sleep(Math.Min(rem, 25));
                        }
                        else
                        {
                            return "ERR connect:" + err;
                        }
                    }

                    uint mode = PIPE_READMODE_MESSAGE;
                    SetNamedPipeHandleState(hPipe, ref mode, IntPtr.Zero, IntPtr.Zero);

                    hEvent = CreateEventA(IntPtr.Zero, true, false, null);
                    if (hEvent == IntPtr.Zero) return "ERR event";
                    ovPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));

                    byte[] req = Encoding.ASCII.GetBytes(cmd);
                    uint written;
                    if (!OverlappedIo(hPipe, hEvent, ovPtr, true, req, req.Length, ioTimeoutMs, out written))
                        return "ERR write-timeout";

                    byte[] buf = new byte[65536];
                    uint read;
                    if (!OverlappedIo(hPipe, hEvent, ovPtr, false, buf, buf.Length, ioTimeoutMs, out read))
                        return "ERR timeout";
                    if (read == 0) return null;
                    int end = Array.IndexOf(buf, (byte)0, 0, (int)read);
                    if (end < 0) end = (int)read;
                    return Encoding.UTF8.GetString(buf, 0, end);
                }
                catch (Exception e) { return "ERR " + e.Message; }
                finally
                {
                    if (hPipe != INVALID_HANDLE_VALUE) CloseHandle(hPipe);
                    if (hEvent != IntPtr.Zero) CloseHandle(hEvent);
                    if (ovPtr != IntPtr.Zero) Marshal.FreeHGlobal(ovPtr);
                }
            }
            finally
            {
                if (taken) Monitor.Exit(_ioLock);
            }
        }

        ulong QPtr(string cmd)
        {
            string s = Cmd(cmd);
            if (string.IsNullOrEmpty(s) || !s.StartsWith("OK")) return 0;
            foreach (string token in s.Split(' '))
            {
                string x = token.Trim();
                if (x.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) x = x.Substring(2);
                if (x.Length < 6) continue;
                ulong v;
                if (ulong.TryParse(x, System.Globalization.NumberStyles.HexNumber, null, out v)) return v;
            }
            return 0;
        }

        int QOff(string cmd)
        {
            string s = Cmd(cmd);
            if (string.IsNullOrEmpty(s)) return -1;
            Match m = Regex.Match(s, "@(\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }

        public bool Init(MonoInfo m)
        {
            m.VtTech = QPtr("VTABLE TechInstance");
            m.VtSkill = QPtr("VTABLE MinionResume");
            m.VtResearchScreen = QPtr("VTABLE ResearchScreen");
            m.OffComplete = QOff("FIELD TechInstance complete");
            m.OffXp = QOff("FIELD MinionResume totalExperienceGained");
            m.OffIdentity = QOff("FIELD MinionResume identity");
            m.OffIdentityName = QOff("FIELD MinionIdentity name");
            m.OffCurrentResearch = QOff("FIELD ResearchScreen currentResearch");
            m.OffTech = QOff("FIELD TechInstance tech");
            m.OffMouse = QOff("FIELD PlayerController prevMousePos");
            m.OffElemId = QOff("FIELD Element id");
            m.OffElemIdx = QOff("FIELD Element idx");
            m.OffElemMaxMass = QOff("FIELD Element maxMass");
            m.OffElemName = QOff("FIELD Element <name>k__BackingField");
            m.OffElemState = QOff("FIELD Element state");
            m.OffElemTag = QOff("FIELD Element tag");
            m.OffMoraleMod = QOff("FIELD MinionResume skillsMoraleModifier");
            m.OffMoraleVal = QOff("FIELD Klei.AI.AttributeModifier <Value>k__BackingField");

            m.GridElemField = (long)QPtr("STATIC Grid elementIdx");
            m.GridMassField = (long)QPtr("STATIC Grid mass");
            m.GridWidthField = (long)QPtr("STATIC Grid WidthInCells");
            m.GridCellCountField = (long)QPtr("STATIC Grid CellCount");
            m.PlayerField = (long)QPtr("STATIC PlayerController <Instance>k__BackingField");
            m.ElementsField = (long)QPtr("STATIC ElementLoader elements");
            m.EarnedField = (long)QPtr("STATIC TUNING.SKILLS TARGET_SKILLS_EARNED");
            m.CycleField = (long)QPtr("STATIC TUNING.SKILLS TARGET_SKILLS_CYCLE");
            m.PowerField = (long)QPtr("STATIC TUNING.SKILLS EXPERIENCE_LEVEL_POWER");

            if (m.VtTech == 0 || m.VtSkill == 0 || m.GridElemField == 0 || m.ElementsField == 0)
                return false;

            m.ElemData = Mem.ReadU64(m.GridElemField);
            m.MassData = Mem.ReadU64(m.GridMassField);
            m.Width = Mem.ReadInt(m.GridWidthField);
            m.CellCount = Mem.ReadInt(m.GridCellCountField);
            m.Earned = Mem.ReadInt(m.EarnedField);
            m.Cycle = Mem.ReadInt(m.CycleField);
            m.Power = Mem.ReadFloat(m.PowerField);
            m.PlayerInstance = Mem.ReadU64(m.PlayerField);

            long p = Mem.ReadU64(m.ElementsField);
            m.ElemList = p;
            m.ElemCount = Mem.ReadInt(p + 24);
            if (m.ElemCount < 0 || m.ElemCount > 1000)
            {
                long arr = Mem.ReadU64(p + 16);
                m.ElemCount = Mem.ReadInt(arr + 24);
                m.ElemList = arr;
            }

            return m.Width > 0 && m.ElemData != 0 && m.MassData != 0;
        }
    }
}
