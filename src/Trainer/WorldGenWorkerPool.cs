using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ONITrainer
{
    public sealed class WorldGenWorkerPool : IDisposable
    {

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        const int JobObjectExtendedLimitInformation = 9;
        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        static IntPtr CreateKillOnCloseJob()
        {
            try
            {
                IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
                if (job == IntPtr.Zero) return IntPtr.Zero;

                JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int sz = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(sz);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)sz);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
                return job;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }



        public static string WorkerCatalogCachePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ONITrainer");
                return Path.Combine(dir, "worker_catalog_cache.tsv");
            }
        }

        public static string EnsureWorkerExe(out string err)
        {
            err = null;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("ONIWorldGenWorker.exe"))
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
                        string dir = Bridge.RuntimeDir;
                        string dst = Path.Combine(dir, "ONIWorldGenWorker_" + hash8 + ".exe");
                        if (!File.Exists(dst) || new FileInfo(dst).Length != data.Length)
                            File.WriteAllBytes(dst, data);
                        try
                        {
                            foreach (string old in Directory.GetFiles(dir, "ONIWorldGenWorker_*.exe"))
                            {
                                if (!string.Equals(old, dst, StringComparison.OrdinalIgnoreCase))
                                    try { File.Delete(old); } catch { }
                            }
                        }
                        catch { }
                        return dst;
                    }
                }
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ONIWorldGenWorker.exe");
                if (File.Exists(local)) return local;
                err = "缺少内置独立进程组件 ONIWorldGenWorker.exe";
                return null;
            }
            catch (Exception ex)
            {
                err = "释放独立进程组件失败：" + ex.Message;
                return null;
            }
        }

        static volatile bool _catalogRefreshedThisSession;

        public static string PrepareCatalogFile(BridgePreviewState bridgeState)
        {
            string cachePath = WorkerCatalogCachePath;
            try
            {
                if ((!File.Exists(cachePath) || !_catalogRefreshedThisSession) &&
                    bridgeState != null && bridgeState.Bridge != null)
                {
                    string tmp = Path.Combine(Bridge.RuntimeDir, "wcat_" + Guid.NewGuid().ToString("N") + ".tsv");
                    string err;
                    string resp = bridgeState.InSave
                        ? bridgeState.Bridge.RunHelperCmd("GETWORKERDATA " + tmp, out err, 8000)
                        : bridgeState.Bridge.RunMenuCmd("GETWORKERDATA " + tmp, out err);
                    if (resp != null && File.Exists(tmp) && new FileInfo(tmp).Length > 100)
                    {
                        string dir = Path.GetDirectoryName(cachePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        if (File.Exists(cachePath)) File.Delete(cachePath);
                        File.Move(tmp, cachePath);
                        _catalogRefreshedThisSession = true;
                    }
                    else
                    {
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    }
                }
            }
            catch { }
            return File.Exists(cachePath) ? cachePath : "";
        }



        sealed class WorkerProc : IDisposable
        {
            readonly object _ioLock = new object();
            Process _proc;
            StreamWriter _stdin;
            StreamReader _stdout;
            readonly StringBuilder _errTail = new StringBuilder();
            bool _disposed;

            public bool IsAlive
            {
                get
                {
                    Process p = _proc;
                    if (p == null || _disposed) return false;
                    try { return !p.HasExited; }
                    catch { return false; }
                }
            }

            public bool Start(string exePath, string gameDir, string helperPath, IntPtr jobHandle,
                string activeDlcCsv, string catalogPath, out string err)
            {
                err = null;
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "\"" + gameDir.TrimEnd('\\') + "\" \"" + helperPath + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    _proc = Process.Start(psi);
                    if (_proc == null)
                    {
                        err = "无法启动工作进程";
                        return false;
                    }
                    try { _proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    if (jobHandle != IntPtr.Zero)
                    {
                        try { AssignProcessToJobObject(jobHandle, _proc.Handle); } catch { }
                    }

                    _proc.ErrorDataReceived += (s, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;
                        lock (_errTail)
                        {
                            if (_errTail.Length < 1024)
                                _errTail.AppendLine(e.Data);
                        }
                    };
                    _proc.BeginErrorReadLine();

                    _stdin = new StreamWriter(_proc.StandardInput.BaseStream, new UTF8Encoding(false));
                    _stdin.AutoFlush = true;
                    _stdout = _proc.StandardOutput;

                    string ready = ReadLineWithTimeout(15000);
                    if (!string.Equals(ready, "READY", StringComparison.Ordinal))
                    {
                        string stderrMsg;
                        lock (_errTail) { stderrMsg = _errTail.ToString().Trim(); }
                        err = "工作进程启动失败：" + (ready ?? (string.IsNullOrEmpty(stderrMsg) ? "超时无响应" : stderrMsg));
                        Dispose();
                        return false;
                    }

                    string initCmd = "INIT " + (activeDlcCsv ?? "") + "|" + (catalogPath ?? "");
                    string initResp = SendCommand(initCmd, 25000, out err);
                    if (initResp == null || !initResp.StartsWith("OK init", StringComparison.Ordinal))
                    {
                        err = "工作进程初始化失败：" + (initResp ?? err ?? "无响应");
                        Dispose();
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    err = "启动工作进程异常：" + ex.Message;
                    Dispose();
                    return false;
                }
            }

            string ReadLineWithTimeout(int timeoutMs)
            {
                string result = null;
                Exception readErr = null;
                ManualResetEventSlim done = new ManualResetEventSlim(false);
                Thread rt = new Thread(() =>
                {
                    try { result = _stdout.ReadLine(); }
                    catch (Exception ex) { readErr = ex; }
                    finally { try { done.Set(); } catch { } }
                });
                rt.IsBackground = true;
                rt.Start();
                if (!done.Wait(timeoutMs))
                {
                    done.Dispose();
                    return null;
                }
                done.Dispose();
                if (readErr != null) return null;
                return result;
            }

            public string SendCommand(string line, int timeoutMs, out string err)
            {
                err = null;
                lock (_ioLock)
                {
                    if (_disposed || _proc == null) { err = "worker-disposed"; return null; }
                    try
                    {
                        if (_proc.HasExited) { err = "worker-exited"; return null; }
                        _stdin.WriteLine(line);
                        _stdin.Flush();
                        string resp = ReadLineWithTimeout(timeoutMs);
                        if (resp == null)
                        {
                            err = "工作进程响应超时";
                            return null;
                        }
                        return resp;
                    }
                    catch (Exception ex)
                    {
                        err = ex.Message;
                        return null;
                    }
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Process p = _proc;
                _proc = null;
                try
                {
                    if (_stdin != null)
                    {
                        try { _stdin.WriteLine("QUIT"); _stdin.Flush(); } catch { }
                        try { _stdin.Dispose(); } catch { }
                    }
                }
                catch { }
                try { if (_stdout != null) _stdout.Dispose(); } catch { }
                if (p != null)
                {
                    try
                    {
                        if (!p.HasExited && !p.WaitForExit(350))
                            p.Kill();
                    }
                    catch { }
                    try { p.Dispose(); } catch { }
                }
            }
        }



        readonly string _gameDir;
        readonly string _activeDlcCsv;
        readonly int _requestedWorkers;
        readonly string _catalogPath;
        readonly IntPtr _jobHandle;
        readonly List<WorkerProc> _workers = new List<WorkerProc>();
        readonly object _poolLock = new object();
        volatile bool _disposed;

        public int ActiveWorkerCount
        {
            get { lock (_poolLock) { return _workers.Count; } }
        }

        public WorldGenWorkerPool(string gameDir, string activeDlcCsv, int workerCount, string catalogPath)
        {
            _gameDir = gameDir ?? "";
            _activeDlcCsv = activeDlcCsv ?? "";
            _requestedWorkers = Math.Max(1, Math.Min(12, workerCount));
            _catalogPath = catalogPath ?? "";
            _jobHandle = CreateKillOnCloseJob();
        }

        public bool Initialize(out string err)
        {
            err = null;
            if (string.IsNullOrEmpty(_gameDir) || !GameInstallationDetector.IsValidGameDir(_gameDir))
            {
                err = "游戏目录无效，无法启动独立多进程扫描。";
                return false;
            }
            string exePath;
            if ((exePath = EnsureWorkerExe(out err)) == null) return false;
            string helperPath = Bridge.EnsurePreviewHelperDll(out err);
            if (string.IsNullOrEmpty(helperPath)) return false;

            string[] workerErrs = new string[_requestedWorkers];
            WorkerProc[] created = new WorkerProc[_requestedWorkers];
            Thread[] initThreads = new Thread[_requestedWorkers];

            for (int i = 0; i < _requestedWorkers; i++)
            {
                int idx = i;
                initThreads[i] = new Thread(() =>
                {
                    if (_disposed) return;
                    WorkerProc wp = new WorkerProc();
                    string wErr;
                    if (wp.Start(exePath, _gameDir, helperPath, _jobHandle, _activeDlcCsv, _catalogPath, out wErr))
                        created[idx] = wp;
                    else
                        workerErrs[idx] = wErr;
                });
                initThreads[i].IsBackground = true;
                initThreads[i].Start();
            }
            for (int i = 0; i < _requestedWorkers; i++)
                initThreads[i].Join();

            lock (_poolLock)
            {
                for (int i = 0; i < _requestedWorkers; i++)
                {
                    if (created[i] != null)
                    {
                        if (_disposed) created[i].Dispose();
                        else _workers.Add(created[i]);
                    }
                    else if (string.IsNullOrEmpty(err) && !string.IsNullOrEmpty(workerErrs[i]))
                    {
                        err = workerErrs[i];
                    }
                }
                if (_workers.Count == 0)
                {
                    if (string.IsNullOrEmpty(err)) err = "未能成功启动任何工作进程";
                    return false;
                }
            }
            err = null;
            return true;
        }

        public static string BuildRulesSpec(List<PoiFilterRule> rules)
        {
            StringBuilder rsb = new StringBuilder();
            if (rules == null) return "";
            for (int i = 0; i < rules.Count; i++)
            {
                PoiFilterRule r = rules[i];
                if (i > 0) rsb.Append(';');
                rsb.Append(r.PrefabId ?? "").Append('~')
                   .Append(r.CompareAtMost ? '1' : '0').Append('~')
                   .Append(r.MinCount).Append('~')
                   .Append(r.MaxDist).Append('~')
                   .Append(r.MinYieldGps.ToString(CultureInfo.InvariantCulture)).Append('~')
                   .Append(r.MinGrade);
            }
            return rsb.ToString();
        }

        public static string NormalizeStoryArgForHeadless(string storyArgOrIds)
        {
            if (string.IsNullOrEmpty(storyArgOrIds)) return "";
            if (storyArgOrIds.StartsWith("CODE:", StringComparison.OrdinalIgnoreCase))
                return "";
            return storyArgOrIds.Trim();
        }

        public bool ScreenParallel(
            CandidateSeedProducer producer,
            bool hasTraitConstraint,
            string clusterPath,
            int worldIndex,
            string activeDlcCsv,
            string storyIdsCsv,
            List<PoiFilterRule> rules,
            int startSeed,
            SearchControl outerCtrl,
            Action<long, int, int, int> onProgress,
            out int matchedSeed,
            out int checkedTotal,
            out int skippedTotal,
            out string err,
            HashSet<int> skipSeeds = null,
            Action<string> onBatchRecordFile = null,
            Action<MapPreviewData> onPreviewRecorded = null)
        {
            matchedSeed = 0;
            checkedTotal = 0;
            skippedTotal = 0;
            err = null;

            List<WorkerProc> snapshot;
            lock (_poolLock)
            {
                if (_disposed || _workers.Count == 0)
                {
                    err = "no-workers";
                    return false;
                }
                snapshot = new List<WorkerProc>(_workers);
            }

            string rulesSpec = BuildRulesSpec(rules);
            string sCsv = NormalizeStoryArgForHeadless(storyIdsCsv);
            int seqSeedCursor = startSeed - 1;
            int foundSeed = 0;
            int totalChecked = 0;
            int totalSkipped = 0;
            int consecBad = 0;
            int stopLocalFlag = 0;
            string firstFatal = null;
            object errSync = new object();

            Thread[] drivers = new Thread[snapshot.Count];
            for (int wi = 0; wi < snapshot.Count; wi++)
            {
                WorkerProc wp = snapshot[wi];
                drivers[wi] = new Thread(() =>
                {
                    int[] candBuf = new int[4];
                    int[] rawBuf = new int[4];
                    StringBuilder ssb = new StringBuilder(64);
                    bool preferPreviewFallback = false;
                    string recFile = onBatchRecordFile != null
                        ? Path.Combine(Bridge.RuntimeDir, "wrec_" + Guid.NewGuid().ToString("N") + ".tsv")
                        : "";
                    try
                    {
                        while (Interlocked.CompareExchange(ref stopLocalFlag, 0, 0) == 0 && !outerCtrl.Stop && !_disposed)
                        {
                            int n = 0;
                            if (hasTraitConstraint && producer != null)
                            {
                                while (n == 0 && Interlocked.CompareExchange(ref stopLocalFlag, 0, 0) == 0 && !outerCtrl.Stop && !_disposed)
                                {
                                    int got = producer.TakeBatch(rawBuf, rawBuf.Length, 150);
                                    for (int gi = 0; gi < got; gi++)
                                    {
                                        if (skipSeeds != null && skipSeeds.Contains(rawBuf[gi])) continue;
                                        candBuf[n++] = rawBuf[gi];
                                    }
                                    if (n > 0 || producer.IsExhausted) break;
                                    if (onProgress != null)
                                        onProgress(Interlocked.Read(ref outerCtrl.Tested), totalChecked, totalSkipped, rawBuf[0]);
                                }
                                if (n == 0)
                                {
                                    if (producer.IsExhausted)
                                        lock (errSync) { if (firstFatal == null && foundSeed == 0) firstFatal = "未在种子空间内找到满足特质条件的候选种子。"; }
                                    break;
                                }
                            }
                            else
                            {
                                int guard = 0;
                                while (n < candBuf.Length && guard < 1000000)
                                {
                                    guard++;
                                    int cur = Interlocked.Increment(ref seqSeedCursor);
                                    if (cur > SeedSearcher.MaxSearchSeed)
                                    {
                                        cur = ((cur - 1) % SeedSearcher.MaxSearchSeed) + 1;
                                    }
                                    if (skipSeeds != null && skipSeeds.Contains(cur)) continue;
                                    candBuf[n++] = cur;
                                }
                                if (n == 0) break;
                                Interlocked.Add(ref outerCtrl.Tested, n);
                            }

                            if (Interlocked.CompareExchange(ref stopLocalFlag, 0, 0) != 0 || outerCtrl.Stop || _disposed) break;

                            string resp = null;
                            if (!preferPreviewFallback)
                            {
                                ssb.Length = 0;
                                for (int i = 0; i < n; i++)
                                {
                                    if (i > 0) ssb.Append(',');
                                    ssb.Append(candBuf[i].ToString(CultureInfo.InvariantCulture));
                                }

                                string cmd = string.Format(CultureInfo.InvariantCulture,
                                    "BATCH {0}|{1}|{2}|{3}|{4}|{5}|{6}",
                                    clusterPath ?? "", worldIndex, activeDlcCsv ?? "", sCsv, rulesSpec, ssb.ToString(), recFile);

                                string cErr;
                                resp = wp.SendCommand(cmd, 60000, out cErr);
                                if (!string.IsNullOrEmpty(recFile) && File.Exists(recFile))
                                {
                                    try { onBatchRecordFile(recFile); } catch { }
                                    try { File.Delete(recFile); } catch { }
                                }
                                if (resp == null)
                                {
                                    if (Interlocked.CompareExchange(ref stopLocalFlag, 0, 0) == 0 && !outerCtrl.Stop && !_disposed)
                                        lock (errSync) { if (firstFatal == null) firstFatal = cErr ?? "工作进程意外退出"; }
                                    break;
                                }
                                if (resp.IndexOf("batch-fallback", StringComparison.OrdinalIgnoreCase) >= 0)
                                    preferPreviewFallback = true;
                            }

                            if (preferPreviewFallback)
                            {
                                for (int bi = 0; bi < n; bi++)
                                {
                                    if (Interlocked.CompareExchange(ref stopLocalFlag, 0, 0) != 0 || outerCtrl.Stop || _disposed) break;
                                    string pErr;
                                    MapPreviewData md = RunPreviewOnWorker(wp, clusterPath, candBuf[bi], worldIndex, activeDlcCsv, sCsv, false, out pErr);
                                    if (md != null)
                                    {
                                        if (onPreviewRecorded != null)
                                        {
                                            try { onPreviewRecorded(md); } catch { }
                                        }
                                        int newChk = Interlocked.Increment(ref totalChecked);
                                        Interlocked.Exchange(ref consecBad, 0);
                                        if (onProgress != null)
                                            onProgress(Interlocked.Read(ref outerCtrl.Tested), newChk, totalSkipped, candBuf[bi]);
                                        if (md.MatchesAllRules(rules))
                                        {
                                            if (Interlocked.CompareExchange(ref foundSeed, candBuf[bi], 0) == 0)
                                                Interlocked.Exchange(ref stopLocalFlag, 1);
                                            break;
                                        }
                                    }
                                    else if (pErr != null && (
                                        pErr.IndexOf("gen-offline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        pErr.IndexOf("render-offline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        pErr.IndexOf("bad-seed", StringComparison.OrdinalIgnoreCase) >= 0))
                                    {
                                        Interlocked.Increment(ref totalSkipped);
                                        int cb = Interlocked.Increment(ref consecBad);
                                        if (cb >= 25)
                                        {
                                            lock (errSync)
                                            {
                                                if (firstFatal == null)
                                                    firstFatal = "连续 25 个候选种子均因特质布局冲突无法生成地形，请减少部分互斥或高占比特质后重试。";
                                            }
                                            Interlocked.Exchange(ref stopLocalFlag, 1);
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        if (Interlocked.CompareExchange(ref stopLocalFlag, 0, 0) == 0 && !outerCtrl.Stop && !_disposed)
                                            lock (errSync) { if (firstFatal == null) firstFatal = pErr ?? "工作进程意外退出"; }
                                        Interlocked.Exchange(ref stopLocalFlag, 1);
                                        break;
                                    }
                                }
                                continue;
                            }

                            if (resp.StartsWith("OK bmatch:", StringComparison.Ordinal) ||
                                resp.StartsWith("OK bmiss:", StringComparison.Ordinal))
                            {
                                int colon = resp.IndexOf(':');
                                string[] f = resp.Substring(colon + 1).Split('|');
                                int mSeed = 0, chk = 0, skp = 0;
                                if (f.Length >= 3)
                                {
                                    int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out mSeed);
                                    int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out chk);
                                    int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out skp);
                                }
                                int newChk = Interlocked.Add(ref totalChecked, chk);
                                int newSkp = Interlocked.Add(ref totalSkipped, skp);
                                if (chk > 0)
                                    Interlocked.Exchange(ref consecBad, 0);
                                else if (skp > 0)
                                {
                                    int cb = Interlocked.Add(ref consecBad, skp);
                                    if (cb >= 25)
                                    {
                                        lock (errSync)
                                        {
                                            if (firstFatal == null)
                                                firstFatal = "连续 25 个候选种子均因特质布局冲突无法生成地形，请减少部分互斥或高占比特质后重试。";
                                        }
                                        Interlocked.Exchange(ref stopLocalFlag, 1);
                                        break;
                                    }
                                }

                                if (onProgress != null)
                                    onProgress(Interlocked.Read(ref outerCtrl.Tested), newChk, newSkp, candBuf[0]);

                                if (mSeed > 0)
                                {
                                    if (Interlocked.CompareExchange(ref foundSeed, mSeed, 0) == 0)
                                        Interlocked.Exchange(ref stopLocalFlag, 1);
                                    break;
                                }
                            }
                            else
                            {
                                lock (errSync)
                                {
                                    if (firstFatal == null)
                                        firstFatal = resp.Replace('\r', ' ').Replace('\n', ' ').Trim();
                                }
                                Interlocked.Exchange(ref stopLocalFlag, 1);
                                break;
                            }
                        }
                    }
                    finally
                    {
                        if (!string.IsNullOrEmpty(recFile))
                        {
                            try { if (File.Exists(recFile)) File.Delete(recFile); } catch { }
                        }
                    }
                });
                drivers[wi].IsBackground = true;
                drivers[wi].Start();
            }


            while (true)
            {
                bool anyAlive = false;
                for (int i = 0; i < drivers.Length; i++)
                {
                    if (drivers[i].Join(160)) continue;
                    anyAlive = true;
                }
                if (!anyAlive) break;
                if (outerCtrl.Stop) Interlocked.Exchange(ref stopLocalFlag, 1);
                if (onProgress != null && !outerCtrl.Stop && foundSeed == 0)
                    onProgress(Interlocked.Read(ref outerCtrl.Tested), totalChecked, totalSkipped, 0);
            }

            matchedSeed = foundSeed;
            checkedTotal = totalChecked;
            skippedTotal = totalSkipped;
            err = matchedSeed > 0 ? null : firstFatal;
            return matchedSeed > 0;
        }

        public MapPreviewData RunPreviewCommand(
            string clusterPath, int seed, int worldIndex, string activeDlcCsv, string storyIdsCsv,
            bool fastPoi, int workerSlot, out string err)
        {
            err = null;
            WorkerProc wp = null;
            lock (_poolLock)
            {
                if (_disposed || _workers.Count == 0) { err = "no-workers"; return null; }
                wp = _workers[Math.Abs(workerSlot) % _workers.Count];
            }
            return RunPreviewOnWorker(wp, clusterPath, seed, worldIndex, activeDlcCsv, storyIdsCsv, fastPoi, out err);
        }

        static MapPreviewData RunPreviewOnWorker(
            WorkerProc wp, string clusterPath, int seed, int worldIndex,
            string activeDlcCsv, string storyIdsCsv, bool fastPoi, out string err)
        {
            err = null;
            string outPath = Path.Combine(Bridge.RuntimeDir, "wprev_" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                string verb = fastPoi ? "FAST" : "PREVIEW";
                string sCsv = NormalizeStoryArgForHeadless(storyIdsCsv);
                string cmd = string.Format(CultureInfo.InvariantCulture,
                    "{0} {1}|{2}|{3}|{4}|{5}|{6}",
                    verb, clusterPath ?? "", seed, worldIndex, activeDlcCsv ?? "", sCsv, outPath);

                string resp = wp.SendCommand(cmd, 60000, out err);
                if (resp == null) return null;
                if (!resp.StartsWith("OK", StringComparison.Ordinal))
                {
                    err = resp.Replace('\r', ' ').Replace('\n', ' ').Trim();
                    return null;
                }
                if (!File.Exists(outPath))
                {
                    err = "未找到生成的预览文件";
                    return null;
                }
                return MapPreviewData.LoadFromFile(outPath);
            }
            catch (Exception ex)
            {
                err = "解析预览数据失败：" + ex.Message;
                return null;
            }
            finally
            {
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_poolLock)
            {
                for (int i = 0; i < _workers.Count; i++)
                    _workers[i].Dispose();
                _workers.Clear();
            }
            if (_jobHandle != IntPtr.Zero)
            {
                try { CloseHandle(_jobHandle); } catch { }
            }
        }
    }


    public static class HeadlessPreviewHost
    {
        static readonly object _lock = new object();
        static WorldGenWorkerPool _pool;
        static string _poolGameDir = "";
        static string _poolDlcCsv = "";

        public static MapPreviewData GeneratePreview(
            string gameDir,
            string clusterPath,
            int seed,
            int worldIndex,
            string activeDlcCsv,
            string storyIdsCsv,
            string mixingCode,
            bool fastPoi,
            BridgePreviewState bridgeState,
            out string err)
        {
            err = null;
            if (!string.IsNullOrEmpty(mixingCode) && !string.Equals(mixingCode.Trim(), "0", StringComparison.Ordinal))
            {
                err = "当前配置包含自定义 DLC 混搭设置（" + mixingCode + "）；为避免游戏闪退已禁止在存档进程内生成地图，请先返回主菜单再预览。";
                return null;
            }

            if (string.IsNullOrEmpty(gameDir) || !GameInstallationDetector.IsValidGameDir(gameDir))
            {
                gameDir = GameInstallationDetector.DetectGameDir();
                if (string.IsNullOrEmpty(gameDir))
                {
                    err = "未识别到《缺氧》游戏目录，请先点击「选择游戏目录…」。";
                    return null;
                }
            }

            string catPath = WorldGenWorkerPool.PrepareCatalogFile(bridgeState);
            WorldGenWorkerPool p;
            lock (_lock)
            {
                if (_pool == null ||
                    !string.Equals(_poolGameDir, gameDir, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_poolDlcCsv, activeDlcCsv ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    if (_pool != null) { _pool.Dispose(); _pool = null; }
                    WorldGenWorkerPool created = new WorldGenWorkerPool(gameDir, activeDlcCsv, 1, catPath);
                    if (!created.Initialize(out err))
                    {
                        created.Dispose();
                        return null;
                    }
                    _pool = created;
                    _poolGameDir = gameDir;
                    _poolDlcCsv = activeDlcCsv ?? "";
                }
                p = _pool;
            }

            MapPreviewData data = p.RunPreviewCommand(clusterPath, seed, worldIndex, activeDlcCsv, storyIdsCsv, fastPoi, 0, out err);
            if (data == null && err != null && (err.IndexOf("exited", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                err.IndexOf("超时", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                lock (_lock)
                {
                    if (_pool == p) { _pool.Dispose(); _pool = null; }
                }
            }
            return data;
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                if (_pool != null)
                {
                    _pool.Dispose();
                    _pool = null;
                }
            }
        }
    }
}
