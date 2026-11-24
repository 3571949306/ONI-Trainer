using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ONITrainer
{



    internal static class DpiHelper
    {
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        public static float GetDpiScale()
        {
            int dpi = 96;
            try
            {
                IntPtr hdc = GetDC(IntPtr.Zero);
                dpi = GetDeviceCaps(hdc, 88);
                ReleaseDC(IntPtr.Zero, hdc);
            }
            catch { }
            return dpi / 96f;
        }

        public static void ScaleLayout(Form f)
        {
            float s = GetDpiScale();
            if (Math.Abs(s - 1f) < 0.01f) return;

            f.AutoScaleMode = AutoScaleMode.None;
            int ncW = Math.Max(0, f.Width - f.ClientSize.Width);
            int ncH = Math.Max(0, f.Height - f.ClientSize.Height);
            int targetW = (int)Math.Round(f.ClientSize.Width * s) + ncW;
            int targetH = (int)Math.Round(f.ClientSize.Height * s) + ncH;
            ScaleBounds(f, s);
            Size sz = new Size(targetW, targetH);
            Screen scr = Screen.FromControl(f);
            int maxW = Math.Max(480, scr.WorkingArea.Width - 32);
            int maxH = Math.Max(320, scr.WorkingArea.Height - 36);
            if (sz.Width > maxW) sz = new Size(maxW, sz.Height);
            if (sz.Height > maxH) sz = new Size(sz.Width, maxH);
            f.Size = sz;
            f.MinimumSize = sz;
        }

        static void ScaleBounds(Control c, float s)
        {
            foreach (Control ch in c.Controls)
            {
                ch.Location = new Point((int)Math.Round(ch.Left * s), (int)Math.Round(ch.Top * s));
                ch.Size = new Size((int)Math.Round(ch.Width * s), (int)Math.Round(ch.Height * s));
                ScaleBounds(ch, s);
            }
        }
    }

    public class DupeInfo
    {
        public long Resume;
        public string Name;
        public int Points;
        public override string ToString() { return Name + "（" + Points + " 点）"; }
    }

    public class Elem
    {
        public int Idx;
        public int Hash;
        public float MaxMass;
        public string Name;
        public int State;
        public float LowTemp;
        public float HighTemp;

        public string StateLabel()
        {
            int b = State & 3;
            if (b == 0) return "真空";
            if (b == 1) return "气体";
            if (b == 2) return "液体";
            return "固体";
        }
        public override string ToString()
        {
            double lo = LowTemp - 273.15, hi = HighTemp - 273.15;
            return Name + "  [" + StateLabel() + "]  温度 " + lo.ToString("0.#") + "℃ ~ " + hi.ToString("0.#") + "℃";
        }
    }

    public class MainForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, INPUT = 0x292C36, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, TXT = 0xECEEF2, SUB = 0x949AA8, WARN = 0xF2B138, ERR = 0xF06464;

        enum ConnPhase { WaitingGame, WaitingMono, WaitingSave, Ready, Error }

        int _pid;
        long _lastGamePtr;
        IntPtr _timeScale = IntPtr.Zero;
        Bridge _bridge;
        MonoInfo _info;
        volatile bool _busy;
        volatile bool _closing;
        volatile bool _inSave;
        bool _speedApplied, _stressActive, _moraleActive, _moraleExpActive;
        ConnPhase _phase = ConnPhase.WaitingGame;
        readonly ManualResetEventSlim _stopWatch = new ManualResetEventSlim(false);
        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        Label _status;
        TextBox _log;
        NumericUpDown _numSpeed, _numStress, _numMorale, _numMoraleExp, _numMass, _numTemp;
        Elem _curElem;
        List<Elem> _allElems = new List<Elem>();
        Button _btnPickElem, _btnTech, _btnTechPath, _btnSkillAdd, _btnSkillAll, _btnDupe, _btnSpawn, _btnReveal;
        Label _lblElem, _lblMassHint, _lblTempHint;
        Label _lblStressState, _lblMoraleState, _lblMoraleExpState;
        string _detectedGameDir;
        List<DlcEntry> _detectedDlcs;
        SeedFinderForm _seedFinderForm;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainForm()
        {
            Text = "ONI 修改器  ·  Oxygen Not Included";
            ClientSize = new Size(840, 634);
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C(BG);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = _fontReg;
            BuildUI();
            DpiHelper.ScaleLayout(this);
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void BuildUI()
        {

            Panel top = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = C(CARD) };
            top.Controls.Add(new Label
            {
                Text = "ONI 修改器",
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                ForeColor = C(ACC),
                Location = new Point(20, 6),
                Size = new Size(200, 28)
            });
            top.Controls.Add(Lbl("缺氧修改器 · 自动检测游戏与存档 · 简单易用", 22, 35, 336, SUB));

            Button btnSeed = B("开局种子筛选", 362, 13, 110, 32, false);
            btnSeed.Click += (s, e) => OpenSeedFinder();
            top.Controls.Add(btnSeed);

            Button btnSettings = B("设置", 478, 13, 62, 32, false);
            btnSettings.Click += (s, e) => OpenSettings();
            top.Controls.Add(btnSettings);

            Panel badge = new Panel { Location = new Point(548, 11), Size = new Size(276, 36), BackColor = C(BG) };
            _status = new Label
            {
                Text = "● 等待启动游戏…",
                ForeColor = C(SUB),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(4, 2),
                Size = new Size(268, 32)
            };
            badge.Controls.Add(_status);
            top.Controls.Add(badge);

            Panel body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };


            GroupBox g1 = Card("01  科技解锁", 16, 10, 398, 98);
            g1.Controls.Add(Lbl("F5 解锁全部科技；科技页点选目标后按 F9 解锁前置链", 16, 24, 368, SUB));
            _btnTech = B("全部解锁  [F5]", 16, 54, 184, 32, true);
            _btnTech.Click += (s, e) => TechAll();
            _btnTechPath = B("选中路径  [F9]", 210, 54, 172, 32, false);
            _btnTechPath.Click += (s, e) => TechPath();
            g1.Controls.Add(_btnTech);
            g1.Controls.Add(_btnTechPath);

            GroupBox g2 = Card("02  复制人技能", 426, 10, 398, 98);
            _btnSkillAdd = B("全体技能点 +1  [F6]", 16, 24, 180, 30, false);
            _btnSkillAll = B("全体技能学满  [F7]", 206, 24, 176, 30, true);
            _btnSkillAdd.Click += (s, e) => Skill(false);
            _btnSkillAll.Click += (s, e) => Skill(true);
            _btnDupe = B("按名字指定复制人技能点…  [F10]", 16, 60, 366, 30, false);
            _btnDupe.Click += (s, e) => OpenDupeSkillForm();
            g2.Controls.Add(_btnSkillAdd);
            g2.Controls.Add(_btnSkillAll);
            g2.Controls.Add(_btnDupe);


            GroupBox g3 = Card("03  单格刷资源", 16, 116, 808, 112);
            g3.Controls.Add(Lbl("元素", 16, 27, 40, TXT));
            _btnPickElem = B("选择元素…", 60, 23, 116, 28, false);
            _btnPickElem.Click += (s, e) => PickElement();
            g3.Controls.Add(_btnPickElem);
            _lblElem = new Label
            {
                Text = "（进入存档后自动加载）",
                ForeColor = C(ACC),
                Font = _fontBold,
                Location = new Point(188, 27),
                Size = new Size(604, 22)
            };
            g3.Controls.Add(_lblElem);

            g3.Controls.Add(Lbl("质量", 16, 61, 40, TXT));
            _numMass = Num(60, 58, 92, 0.01M, 100000M, 1.8M, 2, 0.1M);
            g3.Controls.Add(_numMass);
            g3.Controls.Add(Lbl("kg", 156, 61, 28, SUB));

            g3.Controls.Add(Lbl("温度", 196, 61, 40, TXT));
            _numTemp = Num(240, 58, 92, -273M, 9726M, 20M, 1, 1M);
            g3.Controls.Add(_numTemp);
            g3.Controls.Add(Lbl("℃", 336, 61, 24, SUB));

            _btnSpawn = B("在鼠标格刷出  [F8]", 384, 56, 206, 28, true);
            _btnSpawn.Click += (s, e) => Spawn();
            g3.Controls.Add(_btnSpawn);
            g3.Controls.Add(Lbl("仅支持气体 / 液体 / 真空格", 602, 61, 192, SUB));

            _lblMassHint = Lbl("质量建议：—", 16, 88, 368, SUB);
            _lblTempHint = Lbl("温度建议：—", 388, 88, 404, SUB);
            g3.Controls.Add(_lblMassHint);
            g3.Controls.Add(_lblTempHint);


            GroupBox g4 = Card("04  游戏倍速", 16, 236, 398, 68);
            g4.Controls.Add(Lbl("倍速", 16, 29, 40, TXT));
            _numSpeed = Num(60, 26, 74, 1M, 100M, 5M, 0, 1M);
            g4.Controls.Add(_numSpeed);
            g4.Controls.Add(Lbl("倍", 138, 29, 24, SUB));
            Button bsApply = B("应用", 170, 25, 98, 28, true);
            bsApply.Click += (s, e) => ApplySpeed((int)_numSpeed.Value);
            Button bsReset = B("恢复 1x", 278, 25, 104, 28, false);
            bsReset.Click += (s, e) => ApplySpeed(1);
            g4.Controls.Add(bsApply);
            g4.Controls.Add(bsReset);

            GroupBox g5 = Card("05  地图视野", 426, 236, 398, 68);
            _btnReveal = B("一键揭开全图迷雾", 16, 25, 186, 28, true);
            _btnReveal.Click += (s, e) => RevealAll();
            g5.Controls.Add(_btnReveal);
            g5.Controls.Add(Lbl("揭开已探索星球并显影火山/遗迹", 210, 29, 178, SUB));


            GroupBox g6 = Card("06  复制人状态（压力 / 士气 / 士气需求）", 16, 312, 808, 134);


            g6.Controls.Add(Lbl("压力上限", 16, 28, 92, TXT));
            _numStress = Num(112, 25, 64, 0M, 100M, 0M, 0, 1M);
            g6.Controls.Add(_numStress);
            g6.Controls.Add(Lbl("%（0 = 无压力，持续校准）", 182, 28, 204, SUB));
            Button bStrOn = B("应用", 392, 24, 82, 26, true); bStrOn.Click += (s, e) => ApplyStress();
            Button bStrOff = B("关闭", 482, 24, 82, 26, false); bStrOff.Click += (s, e) => OffStress();
            _lblStressState = StateLbl(582, 28);
            g6.Controls.Add(bStrOn); g6.Controls.Add(bStrOff); g6.Controls.Add(_lblStressState);


            g6.Controls.Add(Lbl("士气加成", 16, 62, 92, TXT));
            _numMorale = Num(112, 59, 64, 0M, 90M, 30M, 0, 1M);
            g6.Controls.Add(_numMorale);
            g6.Controls.Add(Lbl("点（普通 / 仿生人均支持）", 182, 62, 204, SUB));
            Button bMorOn = B("应用", 392, 58, 82, 26, true); bMorOn.Click += (s, e) => ApplyMorale();
            Button bMorOff = B("关闭", 482, 58, 82, 26, false); bMorOff.Click += (s, e) => OffMorale();
            _lblMoraleState = StateLbl(582, 62);
            g6.Controls.Add(bMorOn); g6.Controls.Add(bMorOff); g6.Controls.Add(_lblMoraleState);


            g6.Controls.Add(Lbl("士气需求上限", 16, 96, 92, TXT));
            _numMoraleExp = Num(112, 93, 64, 0M, 100M, 0M, 0, 1M);
            g6.Controls.Add(_numMoraleExp);
            g6.Controls.Add(Lbl("点（0 = 无需求，不写存档）", 182, 96, 204, SUB));
            Button bExpOn = B("应用", 392, 92, 82, 26, true); bExpOn.Click += (s, e) => ApplyMoraleExp();
            Button bExpOff = B("关闭", 482, 92, 82, 26, false); bExpOff.Click += (s, e) => OffMoraleExp();
            _lblMoraleExpState = StateLbl(582, 96);
            g6.Controls.Add(bExpOn); g6.Controls.Add(bExpOff); g6.Controls.Add(_lblMoraleExpState);


            body.Controls.Add(new Label
            {
                Text = "任务日志",
                ForeColor = C(ACC),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                Location = new Point(18, 454),
                Size = new Size(120, 22)
            });
            Button btnClearLog = B("清空日志", 744, 452, 80, 24, false);
            btnClearLog.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnClearLog.Click += (s, e) => { _log.Clear(); };
            body.Controls.Add(btnClearLog);

            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(16, 480),
                Size = new Size(808, 86),
                BackColor = C(CARD),
                ForeColor = C(TXT),
                Font = new Font("Consolas", 9F)
            };

            body.Controls.Add(g1);
            body.Controls.Add(g2);
            body.Controls.Add(g3);
            body.Controls.Add(g4);
            body.Controls.Add(g5);
            body.Controls.Add(g6);
            body.Controls.Add(_log);

            Controls.Add(body);
            Controls.Add(top);
        }

        GroupBox Card(string t, int x, int y, int w, int h)
        {
            return new GroupBox
            {
                Text = t,
                Location = new Point(x, y),
                Size = new Size(w, h),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold)
            };
        }

        Label Lbl(string t, int x, int y, int w, int rgb)
        {
            return new Label
            {
                Text = t,
                ForeColor = C(rgb),
                Font = _fontReg,
                Location = new Point(x, y),
                Size = new Size(w, 22)
            };
        }

        NumericUpDown Num(int x, int y, int w, decimal min, decimal max, decimal val, int dec, decimal inc)
        {
            return new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(w, 26),
                Minimum = min,
                Maximum = max,
                Value = val,
                DecimalPlaces = dec,
                Increment = inc,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                Font = _fontReg
            };
        }

        Label StateLbl(int x, int y)
        {
            return new Label
            {
                Text = "未开启",
                ForeColor = C(SUB),
                Font = _fontReg,
                Location = new Point(x, y),
                Size = new Size(210, 22)
            };
        }

        Button B(string t, int x, int y, int w, int h, bool accent)
        {
            Button b = new Button
            {
                Text = t,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                Font = _fontBold,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = accent ? C(ACC_HOV) : C(BTN_HOV);
            b.BackColor = accent ? C(ACC) : C(BTN);
            b.ForeColor = accent ? C(0x0E231C) : C(TXT);
            return b;
        }

        void SetUi(Action act)
        {
            if (_closing || IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => { if (!_closing && !IsDisposed) act(); })); }
                catch { }
                return;
            }
            act();
        }

        void SetStatus(ConnPhase phase, string text, int rgb)
        {
            _phase = phase;
            SetUi(() =>
            {
                _status.Text = text;
                _status.ForeColor = C(rgb);
            });
        }

        void SetToggleBadge(Label lbl, bool active, string text)
        {
            SetUi(() =>
            {
                if (lbl == null) return;
                lbl.Text = text;
                lbl.ForeColor = active ? C(ACC) : C(SUB);
            });
        }

        void ResetToggleBadges()
        {
            _speedApplied = false;
            _stressActive = false;
            _moraleActive = false;
            _moraleExpActive = false;
            SetToggleBadge(_lblStressState, false, "未开启");
            SetToggleBadge(_lblMoraleState, false, "未开启");
            SetToggleBadge(_lblMoraleExpState, false, "未开启");
        }

        void Log(string s)
        {
            if (_closing || _log == null || _log.IsDisposed || !IsHandleCreated) return;
            if (_log.InvokeRequired)
            {
                try { _log.BeginInvoke((Action)(() => Log(s))); } catch { }
                return;
            }
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s;
            _log.AppendText(line + "\r\n");
            _log.SelectionStart = _log.Text.Length;
            _log.ScrollToCaret();
            try { File.AppendAllText(Path.Combine(Bridge.RuntimeDir, "trainer.log"), line + "\r\n"); }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            const uint MOD_NOREPEAT = 0x4000;
            RegisterHotKey(Handle, 1, MOD_NOREPEAT, 0x74);
            RegisterHotKey(Handle, 2, MOD_NOREPEAT, 0x75);
            RegisterHotKey(Handle, 3, MOD_NOREPEAT, 0x76);
            RegisterHotKey(Handle, 4, MOD_NOREPEAT, 0x77);
            RegisterHotKey(Handle, 5, MOD_NOREPEAT, 0x78);
            RegisterHotKey(Handle, 6, MOD_NOREPEAT, 0x79);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Log("[状态] 等待启动游戏…");
            Thread watcher = new Thread(AutoWatchLoop);
            watcher.IsBackground = true;
            watcher.Start();

            Thread dlcScan = new Thread(() =>
            {
                try
                {
                    string gdir = GameInstallationDetector.DetectGameDir();
                    if (!string.IsNullOrEmpty(gdir))
                    {
                        List<DlcEntry> dlcs = GameInstallationDetector.DetectInstalledDlcs(gdir);
                        _detectedGameDir = gdir;
                        _detectedDlcs = dlcs;
                        Log("[就绪] 已识别本机 DLC：" + GameInstallationDetector.SummarizeEnabledDlcs(dlcs) + "（点击顶部「开局种子筛选」可定制开局坐标）");
                    }
                    else
                    {
                        Log("[提示] 未自动定位游戏安装目录，打开「开局种子筛选」时可手动选择目录");
                    }
                }
                catch { }
            });
            dlcScan.IsBackground = true;
            dlcScan.Start();
        }

        void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm())
                f.ShowDialog(this);
        }

        internal BridgePreviewState GetBridgePreviewState()
        {
            return new BridgePreviewState
            {
                Bridge = _bridge,
                InSave = _inSave
            };
        }

        void OpenSeedFinder()
        {
            if (_seedFinderForm != null && !_seedFinderForm.IsDisposed)
            {
                _seedFinderForm.Activate();
                return;
            }
            _seedFinderForm = new SeedFinderForm(_detectedGameDir, _detectedDlcs, GetBridgePreviewState, OpenSettings);
            _seedFinderForm.Show(this);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                switch (m.WParam.ToInt32())
                {
                    case 1: TechAll(); return;
                    case 2: Skill(false); return;
                    case 3: Skill(true); return;
                    case 4: Spawn(); return;
                    case 5: TechPath(); return;
                    case 6: OpenDupeSkillForm(); return;
                }
            }
            base.WndProc(ref m);
        }


        void AutoWatchLoop()
        {
            int failedPid = 0;
            int connectRetries = 0;
            int queryFails = 0;
            while (!_closing)
            {
                try
                {
                    if (_busy)
                    {
                        _stopWatch.Wait(500);
                        continue;
                    }

                    Process[] ps = Process.GetProcessesByName("OxygenNotIncluded");
                    if (ps.Length == 0)
                    {
                        if (_pid != 0)
                        {
                            _pid = 0;
                            _bridge = null;
                            _info = null;
                            _inSave = false;
                            _lastGamePtr = 0;
                            _timeScale = IntPtr.Zero;
                            failedPid = 0;
                            connectRetries = 0;
                            queryFails = 0;
                            Mem.Detach();
                            ResetToggleBadges();
                            SetStatus(ConnPhase.WaitingGame, "● 等待启动游戏…", SUB);
                            SetUi(() => { _curElem = null; _allElems.Clear(); UpdateElemLabel(); });
                            Log("[状态] 游戏已关闭，等待重新启动…");
                        }
                        _stopWatch.Wait(1200);
                        continue;
                    }

                    int pid = ps[0].Id;
                    foreach (Process p in ps) { try { p.Dispose(); } catch { } }


                    if (pid != _pid)
                    {
                        _pid = pid;
                        _bridge = null;
                        _info = null;
                        _inSave = false;
                        _lastGamePtr = 0;
                        _timeScale = IntPtr.Zero;
                        failedPid = 0;
                        connectRetries = 0;
                        queryFails = 0;
                        Mem.Detach();
                        ResetToggleBadges();
                    }


                    if (_bridge == null)
                    {
                        if (failedPid == _pid)
                        {
                            _stopWatch.Wait(2500);
                            continue;
                        }
                        if (!Bridge.HasMonoModule(_pid))
                        {
                            if (_phase != ConnPhase.WaitingMono)
                                SetStatus(ConnPhase.WaitingMono, "● 检测到游戏，正在等待启动…", WARN);
                            _stopWatch.Wait(800);
                            continue;
                        }

                        if (!Mem.Attach(_pid))
                        {
                            failedPid = _pid;
                            SetStatus(ConnPhase.Error, "● 权限不足，请以管理员运行", ERR);
                            Log("[失败] 无法访问游戏，请右键以管理员身份运行修改器");
                            _stopWatch.Wait(2500);
                            continue;
                        }

                        string dllErr;
                        string dllPath = Bridge.EnsureBridgeDll(out dllErr);
                        if (dllPath == null)
                        {
                            failedPid = _pid;
                            SetStatus(ConnPhase.Error, "● 连接异常", ERR);
                            Log("[失败] " + dllErr);
                            _stopWatch.Wait(2500);
                            continue;
                        }

                        SetStatus(ConnPhase.WaitingMono, "● 正在连接游戏…", WARN);
                        string connErr;
                        Bridge br = Bridge.Connect(_pid, dllPath, out connErr);
                        if (br == null)
                        {
                            connectRetries++;
                            bool fatal = connErr != null && (connErr.Contains("管理员") || connErr.Contains("缺少"));
                            if (fatal || connectRetries >= 3)
                            {
                                failedPid = _pid;
                                SetStatus(ConnPhase.Error, "● 连接游戏失败", ERR);
                                Log("[失败] " + (connErr ?? "连接游戏失败"));
                            }
                            _stopWatch.Wait(2500);
                            continue;
                        }
                        _bridge = br;
                        connectRetries = 0;
                        queryFails = 0;
                        SetStatus(ConnPhase.WaitingSave, "● 游戏已运行，等待进入存档…", WARN);
                        Log("[状态] 检测到游戏，等待进入存档…");
                    }


                    bool inSave, busy;
                    long gamePtr;
                    int gw, gh;
                    if (!_bridge.QuerySaveState(out inSave, out gamePtr, out gw, out gh, out busy))
                    {
                        if (!Bridge.IsProcessAlive(_pid)) continue;
                        if (++queryFails >= 3)
                        {
                            _bridge = null;
                            _inSave = false;
                            _info = null;
                            _lastGamePtr = 0;
                            queryFails = 0;
                            ResetToggleBadges();
                        }
                        _stopWatch.Wait(1000);
                        continue;
                    }
                    queryFails = 0;
                    if (busy)
                    {
                        _stopWatch.Wait(500);
                        continue;
                    }

                    if (!inSave)
                    {
                        if (_inSave)
                        {
                            _inSave = false;
                            _info = null;
                            _lastGamePtr = 0;
                            ResetToggleBadges();
                            SetStatus(ConnPhase.WaitingSave, "● 游戏已运行，等待进入存档…", WARN);
                            Log("[状态] 已退出存档，等待重新载入…");
                        }
                        else if (_phase != ConnPhase.WaitingSave)
                        {
                            SetStatus(ConnPhase.WaitingSave, "● 游戏已运行，等待进入存档…", WARN);
                        }
                        _stopWatch.Wait(1000);
                        continue;
                    }


                    if (!_inSave || gamePtr != _lastGamePtr || _info == null)
                    {
                        MonoInfo mi = new MonoInfo();
                        if (_bridge.Init(mi))
                        {
                            _info = mi;
                            _lastGamePtr = gamePtr;
                            _inSave = true;
                            _timeScale = Mem.FindTimeScaleAddress();
                            ResetToggleBadges();
                            LoadElements();
                            SetStatus(ConnPhase.Ready, "● 已连接存档 · 功能就绪", ACC);
                            Log("[就绪] 已自动连接存档（地图 " + gw + "x" + gh + "），全部功能可用");
                        }
                    }
                }
                catch { }

                _stopWatch.Wait(1200);
            }
        }

        bool EnsureReady()
        {
            if (_inSave && _info != null && _bridge != null) return true;
            if (_pid == 0) Log("[未就绪] 请先启动《缺氧》并进入存档");
            else Log("[未就绪] 等待进入游戏存档后再操作");
            return false;
        }

        void RunBg(string label, Action work)
        {
            if (_busy) { Log("[提示] 上一个任务尚未完成，请稍候…"); return; }
            _busy = true;
            Thread t = new Thread(() =>
            {
                try { work(); }
                catch (Exception e) { Log("[失败] " + label + "：" + e.Message); }
                finally { _busy = false; }
            });
            t.IsBackground = true;
            t.Start();
        }

        static bool ParseDone(string resp, out int val)
        {
            val = 0;
            if (string.IsNullOrEmpty(resp) || !resp.StartsWith("OK")) return false;
            Match m = Regex.Match(resp, @"done:(-?\d+)");
            if (m.Success) int.TryParse(m.Groups[1].Value, out val);
            return true;
        }

        static string ErrReason(string resp)
        {
            if (string.IsNullOrEmpty(resp)) return "无响应";
            if (resp.Contains("not-in-save")) return "当前不在存档中";
            if (resp.Contains("timeout")) return "游戏主线程响应超时";
            if (resp.Contains("no-selection")) return "未选中目标科技";
            return resp;
        }

        void RefreshWorld()
        {
            if (_info == null) return;
            _info.ElemData = Mem.ReadU64(_info.GridElemField);
            _info.MassData = Mem.ReadU64(_info.GridMassField);
            _info.Width = Mem.ReadInt(_info.GridWidthField);
            _info.CellCount = Mem.ReadInt(_info.GridCellCountField);
            _info.Earned = Mem.ReadInt(_info.EarnedField);
            _info.Cycle = Mem.ReadInt(_info.CycleField);
            _info.Power = Mem.ReadFloat(_info.PowerField);
        }

        void LoadElements()
        {
            try
            {
                string resp = _bridge.Cmd("ELEMENTS");
                if (resp == null || !resp.StartsWith("OK elems:")) return;
                List<Elem> list = new List<Elem>();
                foreach (string line in resp.Split('\n'))
                {
                    string s = line.Trim();
                    if (!s.Contains("|")) continue;
                    string[] p = s.Split('|');
                    if (p.Length < 7) continue;
                    int idx, hash, state;
                    float mm, lo, hi;
                    if (!int.TryParse(p[0], out idx)) continue;
                    if (!int.TryParse(p[1], out hash)) continue;
                    if (!int.TryParse(p[2], out state)) continue;
                    float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out mm);
                    float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out lo);
                    float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out hi);
                    list.Add(new Elem { Idx = idx, Hash = hash, MaxMass = mm, Name = CleanName(p[6]), State = state, LowTemp = lo, HighTemp = hi });
                }
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
                Elem defElem = list.Find(el => el.Name.Contains("氧") && el.Name.Contains("气"));
                if (defElem == null && list.Count > 0) defElem = list[0];
                SetUi(() =>
                {
                    _allElems = list;
                    if (_curElem == null || list.Find(x => x.Hash == _curElem.Hash) == null)
                        _curElem = defElem;
                    UpdateElemLabel();
                });
            }
            catch { }
        }

        void UpdateElemLabel()
        {
            if (_lblElem == null) return;
            if (_curElem == null)
            {
                _lblElem.Text = _inSave ? "（未选择）" : "（进入存档后自动加载）";
                if (_lblMassHint != null) _lblMassHint.Text = "质量建议：—";
                if (_lblTempHint != null) _lblTempHint.Text = "温度建议：—";
                return;
            }
            double lo = _curElem.LowTemp - 273.15, hi = _curElem.HighTemp - 273.15;
            _lblElem.Text = _curElem.Name + "（" + _curElem.StateLabel() + "，相变 " +
                lo.ToString("0.#") + "℃ ~ " + hi.ToString("0.#") + "℃，上限 " + _curElem.MaxMass.ToString("0.#") + " kg）";
            if (_lblMassHint != null)
                _lblMassHint.Text = "质量建议：0 ~ " + _curElem.MaxMass.ToString("0.#") + " kg";
            if (_lblTempHint != null)
                _lblTempHint.Text = "温度建议：" + lo.ToString("0.#") + "℃ ~ " + hi.ToString("0.#") + "℃";
        }

        void PickElement()
        {
            if (_allElems.Count == 0) { Log("[未就绪] 进入存档后即可选择元素"); return; }
            using (ElementPickerForm f = new ElementPickerForm(_allElems, _curElem))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.Selected != null)
                {
                    _curElem = f.Selected;
                    UpdateElemLabel();
                    float def = _curElem.MaxMass;
                    if (def > 0f && def <= (float)_numMass.Maximum)
                        _numMass.Value = (decimal)Math.Round(def, 2);
                }
            }
        }

        static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            int gt = raw.IndexOf('>');
            int end = raw.IndexOf("</link>", StringComparison.OrdinalIgnoreCase);
            string s;
            if (gt >= 0 && end > gt) s = raw.Substring(gt + 1, end - gt - 1);
            else if (gt >= 0) s = raw.Substring(gt + 1);
            else s = raw;
            int lt = s.IndexOf('<');
            if (lt > 0) s = s.Substring(0, lt);
            return s.Trim();
        }


        void TechAll()
        {
            if (!EnsureReady()) return;
            RunBg("科技全部解锁", () =>
            {
                string resp = _bridge.Cmd("RESEARCHALL", 1000, 6000);
                int count;
                if (ParseDone(resp, out count))
                    Log("[完成] 科技树已全部解锁（" + count + " 项，建造菜单已刷新）");
                else
                    Log("[失败] 科技全部解锁：" + ErrReason(resp));
            });
        }

        void TechPath()
        {
            if (!EnsureReady()) return;
            RunBg("选中科技路径解锁", () =>
            {
                string resp = _bridge.Cmd("RESEARCHPATHCUR", 1000, 5000);
                if (resp == "ERR no-selection")
                {
                    ShowPopup("请先在科技页面点击一个目标科技，再按 F9。");
                    Log("[取消] 未在科技页点选目标科技");
                    return;
                }
                int count;
                if (ParseDone(resp, out count))
                    Log("[完成] 选中科技及前置链已解锁（" + count + " 项）");
                else
                    Log("[失败] 选中路径解锁：" + ErrReason(resp));
            });
        }


        static int PointsFromXp(float xp, int earned, int cycle, float power)
        {
            if (xp <= 0) return 0;
            double v = Math.Pow(xp / (cycle * 600.0), 1.0 / power) * earned;
            return (int)Math.Floor(v);
        }

        static float XpForPoints(int n, int earned, int cycle, float power)
        {
            if (n <= 0) return 0f;
            double v = Math.Pow(n / (double)earned, power) * cycle * 600.0;
            return (float)v + 1f;
        }

        HashSet<long> ReadLiveIds()
        {
            HashSet<long> set = new HashSet<long>();
            if (_bridge == null) return set;
            string r = _bridge.Cmd("LIVEIDS");
            if (string.IsNullOrEmpty(r) || !r.StartsWith("OK liveids")) return set;
            foreach (string tok in r.Split(' '))
            {
                string x = tok.Trim();
                if (x.Length < 6) continue;
                long v;
                if (long.TryParse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) set.Add(v);
            }
            return set;
        }

        void EnsureSkillScan()
        {
            if (_info.SkillObjects.Count == 0)
            {
                Log("[执行中] 正在读取复制人数据，请稍候…");
                _info.SkillObjects = Mem.ScanPointer(_info.VtSkill);
            }
        }

        List<long> CollectLiveResumes(HashSet<long> live)
        {
            List<long> res = new List<long>();
            foreach (long a in _info.SkillObjects)
            {
                if ((ulong)Mem.ReadU64(a) != _info.VtSkill) continue;
                long ident = Mem.ReadU64(a + _info.OffIdentity);
                if (ident == 0) continue;
                if (live.Count > 0 && !live.Contains(ident)) continue;
                float xp = Mem.ReadFloat(a + _info.OffXp);
                if (float.IsNaN(xp) || xp < 0) continue;
                res.Add(a);
            }
            return res;
        }

        void Skill(bool all)
        {
            if (!EnsureReady()) return;
            RunBg(all ? "全体技能学满" : "全体技能点 +1", () =>
            {
                RefreshWorld();
                EnsureSkillScan();
                HashSet<long> live = ReadLiveIds();
                List<long> targets = CollectLiveResumes(live);
                if (targets.Count == 0)
                {
                    _info.SkillObjects = Mem.ScanPointer(_info.VtSkill);
                    targets = CollectLiveResumes(live);
                }
                if (targets.Count == 0)
                {
                    Log("[失败] 未找到存活复制人");
                    return;
                }
                foreach (long a in targets)
                {
                    float xp = Mem.ReadFloat(a + _info.OffXp);
                    if (all)
                    {
                        Mem.WriteFloat(a + _info.OffXp, XpForPoints(50, _info.Earned, _info.Cycle, _info.Power));
                    }
                    else
                    {
                        int pts = PointsFromXp(xp, _info.Earned, _info.Cycle, _info.Power);
                        int np = Math.Min(pts + 1, 50);
                        Mem.WriteFloat(a + _info.OffXp, XpForPoints(np, _info.Earned, _info.Cycle, _info.Power) + 1f);
                    }
                }
                if (all)
                {
                    int ok = 0;
                    foreach (long a in targets)
                    {
                        string resp = _bridge.Cmd("SWAPDICT " + a.ToString("x"), 1000, 6000);
                        int n;
                        if (ParseDone(resp, out n)) ok++;
                    }
                    if (ok == targets.Count)
                        Log("[完成] 全体技能已学满（" + ok + " 名复制人）");
                    else
                        Log("[部分完成] 技能学满：" + ok + "/" + targets.Count + " 名复制人");
                }
                else
                {
                    Log("[完成] 全体技能点 +1（" + targets.Count + " 名复制人，上限 50）");
                }
            });
        }

        void OpenDupeSkillForm()
        {
            if (!EnsureReady()) return;
            RunBg("读取复制人列表", () =>
            {
                EnsureSkillScan();
                HashSet<long> live = ReadLiveIds();
                List<DupeInfo> list = new List<DupeInfo>();
                int anon = 0;
                foreach (long a in _info.SkillObjects)
                {
                    if ((ulong)Mem.ReadU64(a) != _info.VtSkill) continue;
                    long ident = Mem.ReadU64(a + _info.OffIdentity);
                    if (ident == 0) continue;
                    if (live.Count > 0 && !live.Contains(ident)) continue;
                    string name = "", model = "";
                    string nr = _bridge.Cmd("NAME " + ident.ToString("x"));
                    if (nr != null && nr.StartsWith("OK name:"))
                    {
                        string payload = nr.Substring(8).Trim();
                        int bar = payload.IndexOf('|');
                        if (bar >= 0)
                        {
                            name = payload.Substring(0, bar).Trim();
                            model = payload.Substring(bar + 1).Trim();
                            if (model.StartsWith("model:")) model = model.Substring(6).Trim();
                        }
                        else name = payload;
                    }
                    if (string.IsNullOrEmpty(name))
                        name = (model.Contains("Bionic") ? "仿生人#" : "复制人#") + (++anon);
                    float xp = Mem.ReadFloat(a + _info.OffXp);
                    list.Add(new DupeInfo
                    {
                        Resume = a,
                        Name = name,
                        Points = PointsFromXp(xp, _info.Earned, _info.Cycle, _info.Power)
                    });
                }
                list.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCulture));
                if (list.Count == 0) { Log("[失败] 未找到存活复制人"); return; }
                SetUi(() =>
                {
                    using (DupeSkillForm f = new DupeSkillForm(list, ApplyDupePoints))
                        f.ShowDialog(this);
                });
            });
        }

        void ApplyDupePoints(DupeInfo d, int add)
        {
            if (d == null || add <= 0 || _info == null) return;
            float xp = Mem.ReadFloat(d.Resume + _info.OffXp);
            int pts = PointsFromXp(xp, _info.Earned, _info.Cycle, _info.Power);
            int np = Math.Min(pts + add, 50);
            if (np == pts)
            {
                Log("[提示] " + d.Name + " 已达技能点上限（50 点）");
                return;
            }
            float nxp = XpForPoints(np, _info.Earned, _info.Cycle, _info.Power) + 1f;
            Mem.WriteFloat(d.Resume + _info.OffXp, nxp);
            d.Points = np;
            Log("[完成] " + d.Name + " 技能点已设为 " + np + " 点");
        }


        void ShowPopup(string msg)
        {
            SetUi(() => MessageBox.Show(this, msg, "ONI 修改器", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        }

        void Spawn()
        {
            if (!EnsureReady()) return;
            Elem el = _curElem;
            if (el == null) { Log("[取消] 请先选择元素"); return; }
            float mass = (float)_numMass.Value;
            float tempC = (float)_numTemp.Value;
            float tempK = tempC + 273.15f;
            if (mass <= 0f) { Log("[取消] 质量必须大于 0"); return; }
            RunBg("单格刷资源", () =>
            {
                RefreshWorld();
                long pl = Mem.ReadU64(_info.PlayerField);
                if (pl == 0) { Log("[失败] 无法读取鼠标位置"); return; }
                float x = Mem.ReadFloat(pl + _info.OffMouse);
                float y = Mem.ReadFloat(pl + _info.OffMouse + 4);
                if (float.IsNaN(x) || float.IsNaN(y)) { Log("[失败] 读取鼠标坐标无效"); return; }
                int width = _info.Width;
                int cell = width * (int)(y + 0.05f) + (int)x;
                if (cell < 0 || cell >= _info.CellCount) { Log("[取消] 鼠标不在地图范围内"); return; }

                string ci = _bridge.Cmd("CELLINFO " + cell);
                Match mi = Regex.Match(ci ?? "", @"idx:(-?\d+)");
                Match ms = Regex.Match(ci ?? "", @"state:(-?\d+)");
                if (!mi.Success || !ms.Success) { Log("[失败] 读取目标格失败"); return; }
                int curIdx = int.Parse(mi.Groups[1].Value);
                int state = int.Parse(ms.Groups[1].Value);
                Elem curEl = _allElems.Find(e2 => e2.Idx == curIdx);
                string curName = curEl != null ? curEl.Name : ("元素" + curIdx);
                if ((state & 3) == 3)
                {
                    ShowPopup("目标格是固体（" + curName + "），无法刷入。\n请把鼠标移到气体、液体或真空格再按 F8。");
                    Log("[取消] 目标格是固体（" + curName + "）");
                    return;
                }

                string cmd = string.Format(CultureInfo.InvariantCulture, "PUTSAFE {0} {1} {2} {3}", cell, el.Hash, mass, tempK);
                string resp = _bridge.Cmd(cmd, 1000, 4000);
                int res;
                if (ParseDone(resp, out res))
                    Log("[完成] 已刷出 " + el.Name + "（" + mass + " kg，" + tempC.ToString("0.#") + "℃）");
                else
                    Log("[失败] 刷资源：" + ErrReason(resp));
            });
        }


        void ApplySpeed(int v)
        {
            if (!EnsureReady()) return;
            RunBg("设置游戏倍速", () =>
            {
                string resp = _bridge.Cmd("SPEED " + v, 1000, 4000);
                int res;
                if (ParseDone(resp, out res))
                {
                    _speedApplied = (v != 1);
                    Log(v == 1 ? "[完成] 游戏倍速已恢复 1x" : "[完成] 游戏倍速已设为 " + v + "x");
                }
                else
                    Log("[失败] 设置倍速：" + ErrReason(resp));
            });
        }


        void RevealAll()
        {
            if (!EnsureReady()) return;
            RunBg("揭开全图迷雾", () =>
            {
                string resp = _bridge.Cmd("REVEAL", 1000, 10000);
                int spawned;
                if (ParseDone(resp, out spawned))
                {
                    if (spawned > 0)
                        Log("[完成] 已揭开迷雾并立即显影 " + spawned + " 处火山/间歇泉/遗迹对象");
                    else
                        Log("[完成] 已揭开已探索星球迷雾");
                }
                else
                    Log("[失败] 揭开迷雾：" + ErrReason(resp));
            });
        }


        void ApplyStress()
        {
            if (!EnsureReady()) return;
            int v = (int)_numStress.Value;
            RunBg("设置压力上限", () =>
            {
                string resp = _bridge.Cmd("STRESS " + v, 1000, 4000);
                int res;
                if (ParseDone(resp, out res))
                {
                    _stressActive = true;
                    SetToggleBadge(_lblStressState, true, "已开启：≤ " + v + "%");
                    Log("[完成] 压力上限已设为 " + v + "%");
                }
                else
                    Log("[失败] 压力上限：" + ErrReason(resp));
            });
        }

        void OffStress()
        {
            if (!EnsureReady()) return;
            RunBg("关闭压力上限", () =>
            {
                string resp = _bridge.Cmd("STRESS -1", 1000, 4000);
                int res;
                if (ParseDone(resp, out res))
                {
                    _stressActive = false;
                    SetToggleBadge(_lblStressState, false, "未开启");
                    Log("[完成] 压力上限已关闭");
                }
                else
                    Log("[失败] 关闭压力上限：" + ErrReason(resp));
            });
        }

        void ApplyMorale()
        {
            if (!EnsureReady()) return;
            int v = (int)_numMorale.Value;
            RunBg("设置士气加成", () =>
            {
                EnsureSkillScan();
                HashSet<long> liveM = ReadLiveIds();
                List<long> targets = CollectLiveResumes(liveM);
                if (targets.Count == 0)
                {
                    _info.SkillObjects = Mem.ScanPointer(_info.VtSkill);
                    targets = CollectLiveResumes(liveM);
                }
                int ok = 0;
                foreach (long a in targets)
                {
                    string resp = _bridge.Cmd("MORALERESUME " + a.ToString("x") + " " + v, 1000, 4000);
                    int n;
                    if (ParseDone(resp, out n)) ok++;
                }
                if (ok > 0)
                {
                    _moraleActive = true;
                    SetToggleBadge(_lblMoraleState, true, "已开启：+" + v);
                    Log("[完成] 士气加成 +" + v + " 已生效（" + ok + " 名复制人）");
                }
                else
                    Log("[失败] 未找到可应用的复制人");
            });
        }

        void OffMorale()
        {
            if (!EnsureReady()) return;
            RunBg("关闭士气加成", () =>
            {
                EnsureSkillScan();
                int ok = 0;
                HashSet<long> liveM = ReadLiveIds();
                foreach (long a in _info.SkillObjects)
                {
                    if ((ulong)Mem.ReadU64(a) != _info.VtSkill) continue;
                    long ident = Mem.ReadU64(a + _info.OffIdentity);
                    if (ident == 0) continue;
                    if (liveM.Count > 0 && !liveM.Contains(ident)) continue;
                    string resp = _bridge.Cmd("MORALERESUME " + a.ToString("x") + " -1", 1000, 4000);
                    int n;
                    if (ParseDone(resp, out n)) ok++;
                }
                _moraleActive = false;
                SetToggleBadge(_lblMoraleState, false, "未开启");
                Log("[完成] 士气加成已关闭（" + ok + " 名复制人）");
            });
        }

        void ApplyMoraleExp()
        {
            if (!EnsureReady()) return;
            int v = (int)_numMoraleExp.Value;
            RunBg("设置士气需求上限", () =>
            {
                string resp = _bridge.Cmd("MORALEEXP " + v, 1000, 4000);
                int res;
                if (ParseDone(resp, out res))
                {
                    _moraleExpActive = true;
                    SetToggleBadge(_lblMoraleExpState, true, "已开启：≤ " + v);
                    Log("[完成] 士气需求上限已设为 " + v);
                }
                else
                    Log("[失败] 士气需求上限：" + ErrReason(resp));
            });
        }

        void OffMoraleExp()
        {
            if (!EnsureReady()) return;
            RunBg("关闭士气需求上限", () =>
            {
                string resp = _bridge.Cmd("MORALEEXP -1", 1000, 4000);
                int res;
                if (ParseDone(resp, out res))
                {
                    _moraleExpActive = false;
                    SetToggleBadge(_lblMoraleExpState, false, "未开启");
                    Log("[完成] 士气需求上限已恢复默认");
                }
                else
                    Log("[失败] 关闭士气需求上限：" + ErrReason(resp));
            });
        }


        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            try { _stopWatch.Set(); } catch { }
            UnregisterHotKey(Handle, 1);
            UnregisterHotKey(Handle, 2);
            UnregisterHotKey(Handle, 3);
            UnregisterHotKey(Handle, 4);
            UnregisterHotKey(Handle, 5);
            UnregisterHotKey(Handle, 6);

            bool needSpeed = _speedApplied;
            bool needStress = _stressActive;
            bool needMorale = _moraleActive;
            bool needMoraleExp = _moraleExpActive;
            Bridge br = _bridge;
            int pid = _pid;
            IntPtr ts = _timeScale;

            if ((needSpeed || needStress || needMorale || needMoraleExp) && Bridge.IsProcessAlive(pid))
            {
                try { Hide(); } catch { }
                if (needSpeed && ts != IntPtr.Zero)
                {
                    try { Mem.WriteFloat(ts.ToInt64(), 1f); } catch { }
                }
                if (br != null)
                {
                    Thread t = new Thread(() =>
                    {
                        try
                        {
                            string cmd = string.Format(CultureInfo.InvariantCulture, "CLEANUP {0} {1} {2} {3}",
                                needSpeed ? 1 : 0, needStress ? 1 : 0, needMorale ? 1 : 0, needMoraleExp ? 1 : 0);
                            br.Cmd(cmd, 200, 300);
                        }
                        catch { }
                    });
                    t.IsBackground = true;
                    t.Start();
                    t.Join(450);
                }
            }

            Mem.Detach();
            base.OnFormClosing(e);
        }
    }
}
