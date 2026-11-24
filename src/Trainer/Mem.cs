using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ONITrainer
{

    public static class Mem
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, int len);

        [StructLayout(LayoutKind.Sequential)]
        struct MBI
        {
            public IntPtr BaseAddress, AllocationBase;
            public int AllocationProtect, Pad1;
            public long RegionSize;
            public int State, Protect, Type, Pad2;
        }

        const int MEM_COMMIT = 0x1000, PAGE_RW = 0x04, MEM_PRIVATE = 0x20000;
        static IntPtr h = IntPtr.Zero;

        public static bool Attach(int pid)
        {
            Detach();
            h = OpenProcess(0x1F0FFF, false, pid);
            return h != IntPtr.Zero;
        }

        public static void Detach()
        {
            IntPtr old = h;
            h = IntPtr.Zero;
            if (old != IntPtr.Zero) { try { CloseHandle(old); } catch { } }
        }

        public static bool Read(IntPtr addr, byte[] buf, int size)
        {
            int read;
            return ReadProcessMemory(h, addr, buf, size, out read) && read == size;
        }

        public static bool Write(IntPtr addr, byte[] buf)
        {
            int written;
            return WriteProcessMemory(h, addr, buf, buf.Length, out written) && written == buf.Length;
        }

        public static byte ReadByte(long addr)
        {
            byte[] b = new byte[1];
            return Read(new IntPtr(addr), b, 1) ? b[0] : (byte)0;
        }
        public static void WriteByte(long addr, byte v) { Write(new IntPtr(addr), new byte[] { v }); }

        public static ushort ReadU16(long addr)
        {
            byte[] b = new byte[2];
            return Read(new IntPtr(addr), b, 2) ? BitConverter.ToUInt16(b, 0) : (ushort)0;
        }
        public static void WriteU16(long addr, ushort v) { Write(new IntPtr(addr), BitConverter.GetBytes(v)); }

        public static int ReadInt(long addr)
        {
            byte[] b = new byte[4];
            return Read(new IntPtr(addr), b, 4) ? BitConverter.ToInt32(b, 0) : int.MinValue;
        }
        public static void WriteInt(long addr, int v) { Write(new IntPtr(addr), BitConverter.GetBytes(v)); }

        public static long ReadU64(long addr)
        {
            byte[] b = new byte[8];
            return Read(new IntPtr(addr), b, 8) ? BitConverter.ToInt64(b, 0) : 0;
        }

        public static float ReadFloat(long addr)
        {
            byte[] b = new byte[4];
            return Read(new IntPtr(addr), b, 4) ? BitConverter.ToSingle(b, 0) : float.NaN;
        }
        public static void WriteFloat(long addr, float v) { Write(new IntPtr(addr), BitConverter.GetBytes(v)); }


        public static string ReadMonoString(long addr)
        {
            if (addr == 0) return "";
            int len = ReadInt(addr + 16);
            if (len <= 0 || len > 4096) return "";
            byte[] b = new byte[len * 2];
            if (!Read(new IntPtr(addr + 20), b, b.Length)) return "";
            return Encoding.Unicode.GetString(b);
        }

        struct Region { public long Base, Size; public int Protect, Type; }

        static List<Region> Regions()
        {
            List<Region> list = new List<Region>();
            IntPtr addr = IntPtr.Zero;
            MBI mbi;
            int sz = Marshal.SizeOf(typeof(MBI));
            while (VirtualQueryEx(h, addr, out mbi, sz) != 0)
            {
                long b = mbi.BaseAddress.ToInt64();
                long n = b + mbi.RegionSize;
                if (mbi.State == MEM_COMMIT && mbi.RegionSize > 0)
                    list.Add(new Region { Base = b, Size = mbi.RegionSize, Protect = mbi.Protect, Type = mbi.Type });
                if (n <= addr.ToInt64()) break;
                addr = new IntPtr(n);
            }
            return list;
        }


        public static unsafe List<long> ScanPointer(ulong needle)
        {
            List<long> res = new List<long>();
            foreach (Region r in Regions())
            {
                if (r.Type != MEM_PRIVATE || r.Protect == 1 || (r.Protect & 0x100) != 0) continue;
                long pos = r.Base, end = r.Base + r.Size;
                while (pos < end)
                {
                    int want = (int)Math.Min(16L * 1024 * 1024, end - pos);
                    byte[] buf = new byte[want];
                    int got;
                    if (!ReadProcessMemory(h, new IntPtr(pos), buf, want, out got) || got < 8)
                    {
                        pos += want;
                        continue;
                    }
                    fixed (byte* bp = buf)
                    {
                        ulong* q = (ulong*)bp;
                        int n = got / 8;
                        for (int i = 0; i < n; i++)
                            if (q[i] == needle) res.Add(pos + i * 8L);
                    }
                    pos += want;
                }
            }
            return res;
        }


        public static IntPtr FindTimeScaleAddress()
        {
            foreach (Region r in Regions())
                if (r.Protect == PAGE_RW && r.Size >= 0x9F00000L && r.Size <= 0xAB00000L)
                    return new IntPtr(r.Base + 0x67A1C);
            return IntPtr.Zero;
        }

        public static long FindMaximumDeltaTimeAddress()
        {
            foreach (Region r in Regions())
                if (r.Protect == PAGE_RW && r.Size >= 0x9F00000L && r.Size <= 0xAB00000L)
                    return r.Base + 0x67A20;
            return 0;
        }
    }
}
