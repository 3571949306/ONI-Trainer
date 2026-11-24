using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace ONITrainer
{
    public sealed class ClusterOverviewForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, TXT = 0xECEEF2, SUB = 0x949AA8, WARN = 0xF2B138;

        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        readonly ClusterDef _cluster;
        readonly int _seed;
        readonly string _activeDlcCsv;
        readonly string _storyArg;
        readonly string _mixingCode;
        readonly Func<BridgePreviewState> _getBridge;
        readonly List<WorldPlacementDef> _orderedPlacements;
        readonly Dictionary<int, MapPreviewData> _sharedCache;
        readonly Action<int> _onSelectPlanet;

        ListView _lv;
        Label _lblStatus;
        ProgressBar _pb;
        Button _btnRescan, _btnStop;

        volatile bool _closing;
        volatile bool _stopReq;
        Thread _scanThread;

        public ClusterOverviewForm(
            ClusterDef cluster,
            int seed,
            string coordinate,
            string activeDlcCsv,
            string storyArg,
            string mixingCode,
            Func<BridgePreviewState> getBridge,
            Dictionary<int, MapPreviewData> sharedCache,
            Action<int> onSelectPlanet)
        {
            _cluster = cluster;
            _seed = seed;
            _activeDlcCsv = activeDlcCsv ?? "";
            _storyArg = storyArg ?? "";
            _mixingCode = mixingCode ?? "";
            _getBridge = getBridge;
            _sharedCache = sharedCache ?? new Dictionary<int, MapPreviewData>();
            _onSelectPlanet = onSelectPlanet;
            _orderedPlacements = cluster != null ? cluster.GetOrderedPlacements() : new List<WorldPlacementDef>();

            Text = string.Format("整星群总览  ·  {0}  ·  {1}",
                cluster != null ? cluster.DisplayName : "", coordinate ?? "");
            ClientSize = new Size(1000, 560);
            MinimumSize = new Size(800, 440);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = C(BG);
            ForeColor = C(TXT);
            Font = _fontReg;

            BuildUI();
            DpiHelper.ScaleLayout(this);
            WireResize();
            PopulateInitialRows();
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void BuildUI()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = C(CARD) };
            _lblStatus = new Label
            {
                Text = "准备扫描星群内全部小行星…（双击任意行立即跳转地图预览）",
                Location = new Point(14, 7),
                Size = new Size(640, 20),
                ForeColor = C(ACC),
                Font = _fontBold
            };
            _pb = new ProgressBar
            {
                Location = new Point(14, 31),
                Size = new Size(640, 14),
                Minimum = 0,
                Maximum = Math.Max(1, _orderedPlacements.Count)
            };

            _btnRescan = MakeBtn("开始/继续扫描", 672, 12, 120, 30, ACC, 0x0E231C, true);
            _btnRescan.Click += (s, e) => StartScan();

            _btnStop = MakeBtn("停止", 798, 12, 76, 30, BTN, TXT, false);
            _btnStop.Enabled = false;
            _btnStop.Click += (s, e) => { _stopReq = true; };

            Button btnClose = MakeBtn("关闭", 882, 12, 102, 30, BTN, TXT, false);
            btnClose.Click += (s, e) => Close();

            top.Controls.Add(_lblStatus);
            top.Controls.Add(_pb);
            top.Controls.Add(_btnRescan);
            top.Controls.Add(_btnStop);
            top.Controls.Add(btnClose);

            _lv = new ListView
            {
                Location = new Point(12, 64),
                Size = new Size(976, 484),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                BackColor = C(CARD),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle
            };
            _lv.Columns.Add("#", 38);
            _lv.Columns.Add("小行星", 150);
            _lv.Columns.Add("归属", 82);
            _lv.Columns.Add("状态", 78);
            _lv.Columns.Add("尺寸", 72);
            _lv.Columns.Add("金属火山", 68);
            _lv.Columns.Add("火山", 52);
            _lv.Columns.Add("水/气/油", 76);
            _lv.Columns.Add("遗迹/故事", 76);
            _lv.Columns.Add("重点特质 / S-A 级喷孔亮点", 272);
            _lv.DoubleClick += (s, e) => JumpToSelected();

            Controls.Add(_lv);
            Controls.Add(top);
        }

        Button MakeBtn(string text, int x, int y, int w, int h, int bg, int fg, bool bold)
        {
            Button b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(bg),
                ForeColor = C(fg),
                Font = bold ? _fontBold : _fontReg,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = C(bg == ACC ? ACC_HOV : BTN_HOV);
            return b;
        }

        void WireResize()
        {
            Resize += (s, e) =>
            {
                if (_lv != null)
                    _lv.Size = new Size(Math.Max(200, ClientSize.Width - 24), Math.Max(160, ClientSize.Height - 76));
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartScan();
        }

        void PopulateInitialRows()
        {
            _lv.BeginUpdate();
            _lv.Items.Clear();
            for (int i = 0; i < _orderedPlacements.Count; i++)
            {
                WorldPlacementDef wp = _orderedPlacements[i];
                string wname = wp.World != null ? wp.World.DisplayName : wp.WorldId;
                ListViewItem lvi = new ListViewItem((i + 1).ToString());
                lvi.SubItems.Add(wname);
                lvi.SubItems.Add(wp.GroupLabel());
                lvi.SubItems.Add("待扫描");
                lvi.SubItems.Add("—");
                lvi.SubItems.Add("—");
                lvi.SubItems.Add("—");
                lvi.SubItems.Add("—");
                lvi.SubItems.Add("—");
                lvi.SubItems.Add("—");
                lvi.Tag = wp.PlacementIndex;
                _lv.Items.Add(lvi);

                MapPreviewData cached;
                lock (_sharedCache)
                {
                    _sharedCache.TryGetValue(wp.PlacementIndex, out cached);
                }
                if (cached != null)
                    UpdateRowFromData(i, cached);
            }
            _lv.EndUpdate();
        }

        void UpdateRowFromData(int rowIndex, MapPreviewData d)
        {
            if (rowIndex < 0 || rowIndex >= _lv.Items.Count || d == null) return;
            int mv = 0, vol = 0, wat = 0, gas = 0, oil = 0, poi = 0;
            List<string> highlights = new List<string>();
            for (int i = 0; i < d.Pois.Count; i++)
            {
                MapPoiItem p = d.Pois[i];
                if (p.Category == "metal_volcano") mv++;
                else if (p.Category == "volcano") vol++;
                else if (p.Category == "water_geyser") wat++;
                else if (p.Category == "gas_geyser") gas++;
                else if (p.Category == "oil") oil++;
                else if (p.Category == "story" || p.Category == "poi") poi++;

                if (p.HasGeyserStats && (p.Grade == 1 || p.Grade == 2) && highlights.Count < 3)
                    highlights.Add(string.Format("[{0}]{1} {2}", p.GradeText(), p.DisplayName, p.FormatYieldShort()));
            }

            string hl = string.IsNullOrEmpty(d.TraitsSummary) ? "" : d.TraitsSummary;
            if (highlights.Count > 0)
                hl = string.IsNullOrEmpty(hl) ? string.Join(" · ", highlights.ToArray()) : (hl + " | " + string.Join(" · ", highlights.ToArray()));

            ListViewItem lvi = _lv.Items[rowIndex];
            lvi.SubItems[3].Text = "完成";
            lvi.SubItems[4].Text = d.Width + "×" + d.Height;
            lvi.SubItems[5].Text = mv.ToString();
            lvi.SubItems[6].Text = vol.ToString();
            lvi.SubItems[7].Text = string.Format("{0}/{1}/{2}", wat, gas, oil);
            lvi.SubItems[8].Text = poi.ToString();
            lvi.SubItems[9].Text = hl;
        }

        void StartScan()
        {
            if (_scanThread != null && _scanThread.IsAlive) return;
            _stopReq = false;
            _btnRescan.Enabled = false;
            _btnStop.Enabled = true;

            _scanThread = new Thread(ScanWorker);
            _scanThread.IsBackground = true;
            _scanThread.Start();
        }

        void ScanWorker()
        {
            int done = 0;
            int total = _orderedPlacements.Count;
            string errStop = null;

            for (int i = 0; i < total; i++)
            {
                if (_closing || _stopReq) break;
                int rowIdx = i;
                WorldPlacementDef wp = _orderedPlacements[i];

                MapPreviewData data;
                lock (_sharedCache)
                {
                    _sharedCache.TryGetValue(wp.PlacementIndex, out data);
                }

                if (data == null)
                {
                    SetUi(() =>
                    {
                        if (rowIdx < _lv.Items.Count) _lv.Items[rowIdx].SubItems[3].Text = "生成中…";
                        _lblStatus.Text = string.Format("正在扫描 ({0}/{1})：{2}…", i + 1, total, wp.World != null ? wp.World.DisplayName : wp.WorldId);
                        _lblStatus.ForeColor = C(WARN);
                    });

                    BridgePreviewState st = _getBridge != null ? _getBridge() : null;
                    if (st == null || st.Bridge == null)
                    {
                        errStop = "未连接游戏，扫描已暂停。";
                        break;
                    }
                    if (st.InSave)
                    {
                        errStop = "检测到已进入存档，需返回主菜单后继续扫描。";
                        break;
                    }

                    string err;
                    data = st.Bridge.GenerateMapPreview(
                        _cluster.FilePath, _seed, wp.PlacementIndex, _activeDlcCsv, _storyArg, _mixingCode, out err);
                    if (data == null)
                    {
                        SetUi(() =>
                        {
                            if (rowIdx < _lv.Items.Count) _lv.Items[rowIdx].SubItems[3].Text = "失败";
                        });
                        errStop = err ?? "生成失败";
                        continue;
                    }
                    lock (_sharedCache)
                    {
                        _sharedCache[wp.PlacementIndex] = data;
                    }
                }

                done++;
                MapPreviewData captured = data;
                int doneSnap = done;
                SetUi(() =>
                {
                    UpdateRowFromData(rowIdx, captured);
                    _pb.Value = Math.Min(_pb.Maximum, doneSnap);
                });
            }

            SetUi(() =>
            {
                _btnRescan.Enabled = true;
                _btnStop.Enabled = false;
                if (_stopReq)
                {
                    _lblStatus.Text = string.Format("扫描已停止（已完成 {0}/{1}）— 双击任意行查看地图", done, total);
                    _lblStatus.ForeColor = C(SUB);
                }
                else if (!string.IsNullOrEmpty(errStop))
                {
                    _lblStatus.Text = errStop + string.Format("（已完成 {0}/{1}）", done, total);
                    _lblStatus.ForeColor = C(WARN);
                }
                else
                {
                    _lblStatus.Text = string.Format(CultureInfo.InvariantCulture,
                        "全星群扫描完成（共 {0} 颗小行星）— 双击任意行立即跳转地图预览", total);
                    _lblStatus.ForeColor = C(ACC);
                }
            });
        }

        void JumpToSelected()
        {
            if (_lv.SelectedItems.Count == 0) return;
            object tag = _lv.SelectedItems[0].Tag;
            if (tag is int && _onSelectPlanet != null)
            {
                _onSelectPlanet((int)tag);
                if (Owner != null) Owner.Activate();
            }
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            _stopReq = true;
            base.OnFormClosing(e);
        }
    }
}
