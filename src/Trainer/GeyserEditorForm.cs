using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace ONITrainer
{
    public sealed class GeyserEditorForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, INPUT = 0x292C36, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, ERR = 0xF25F5C, WARN = 0xF2B138, TXT = 0xECEEF2, SUB = 0x949AA8;


        sealed class FlatSlider : Control
        {
            int _min;
            int _max = 1000;
            int _val = 500;
            bool _dragging;
            bool _hover;

            public event EventHandler ValueChanged;

            public FlatSlider()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
            }

            public int Minimum
            {
                get { return _min; }
                set { _min = value; if (_val < _min) Value = _min; Invalidate(); }
            }

            public int Maximum
            {
                get { return _max; }
                set { _max = Math.Max(_min + 1, value); if (_val > _max) Value = _max; Invalidate(); }
            }

            public int Value
            {
                get { return _val; }
                set
                {
                    int clamped = Math.Max(_min, Math.Min(_max, value));
                    if (_val == clamped) return;
                    _val = clamped;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }

            void SetFromMouseX(int x)
            {
                int pad = Math.Max(7, Height / 3);
                int w = Math.Max(1, Width - pad * 2);
                double t = (double)(x - pad) / w;
                if (t < 0.0) t = 0.0;
                else if (t > 1.0) t = 1.0;
                Value = _min + (int)Math.Round(t * (_max - _min));
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left)
                {
                    Focus();
                    _dragging = true;
                    Capture = true;
                    SetFromMouseX(e.X);
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (_dragging) SetFromMouseX(e.X);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (_dragging)
                {
                    _dragging = false;
                    Capture = false;
                }
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                _hover = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hover = false;
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                int step = e.Delta > 0 ? 10 : -10;
                Value = _val + step;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush brBg = new SolidBrush(BackColor))
                    g.FillRectangle(brBg, ClientRectangle);

                int pad = Math.Max(7, Height / 3);
                int trackW = Math.Max(4, Width - pad * 2);
                int trackH = Math.Max(4, Height / 5);
                int trackY = (Height - trackH) / 2;

                double frac = (_max > _min) ? (double)(_val - _min) / (_max - _min) : 0.0;
                int thumbX = pad + (int)Math.Round(frac * trackW);
                int thumbR = Math.Max(5, Height / 3 - 1);

                using (SolidBrush brRail = new SolidBrush(Color.FromArgb(0x2E, 0x32, 0x3E)))
                    g.FillRectangle(brRail, pad, trackY, trackW, trackH);

                int fillW = Math.Max(0, thumbX - pad);
                if (fillW > 0)
                {
                    using (SolidBrush brFill = new SolidBrush(Color.FromArgb(0x00, 0xD6, 0x8F)))
                        g.FillRectangle(brFill, pad, trackY, fillW, trackH);
                }

                Color thumbCol = (_dragging || _hover)
                    ? Color.FromArgb(0x1C, 0xE3, 0xA0)
                    : Color.FromArgb(0x00, 0xD6, 0x8F);
                Rectangle thumbRect = new Rectangle(thumbX - thumbR, Height / 2 - thumbR, thumbR * 2, thumbR * 2);
                using (SolidBrush brThumb = new SolidBrush(thumbCol))
                using (Pen penThumb = new Pen(Color.FromArgb(0xEC, 0xEE, 0xF2), 1.2f))
                {
                    g.FillEllipse(brThumb, thumbRect);
                    g.DrawEllipse(penThumb, thumbRect);
                }
            }
        }

        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        readonly Font _fontSmall = new Font("Microsoft YaHei UI", 8.25F);
        readonly Bridge _bridge;
        readonly Action<string> _log;

        sealed class GeyserEntry
        {
            public int InstId;
            public int WorldId;
            public string WorldName;
            public int X;
            public int Y;
            public string PrefabId;
            public string RawTypeId;
            public string Category;
            public string DisplayName;
            public bool Studied;
            public float AvgGps;
            public float EmitGps;
            public float TempC;
            public float EruptSec;
            public float IdleSec;
            public float ActiveCycles;
            public float DormantCycles;
            public byte Grade;
            public float Percentile;
            public float RateRoll;
            public float IterLenRoll;
            public float IterPctRoll;
            public float YearLenRoll;
            public float YearPctRoll;
            public float MinAvgGps;
            public float MaxAvgGps;
            public float MinRateKgCycle = 1000f;
            public float MaxRateKgCycle = 2000f;
            public float MinYearPct = 0.4f;
            public float MaxYearPct = 0.8f;
            public float MinIterPct = 0.1f;
            public float MaxIterPct = 0.9f;
            public float MinIterLenSec = 60f;
            public float MaxIterLenSec = 1140f;
            public float MinYearLenSec = 15000f;
            public float MaxYearLenSec = 135000f;
        }

        sealed class CatalogTypeEntry
        {
            public string PrefabId;
            public string RawTypeId;
            public string Category;
            public string DisplayName;
            public float TempC;
            public float MinAvgGps;
            public float MaxAvgGps;

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "[{0}] {1} ({2:0.#}℃ · 均产 {3:0}~{4:0} g/s)",
                    PoiFilterCatalogEntry.CategoryLabel(Category), DisplayName, TempC, MinAvgGps, MaxAvgGps);
            }
        }

        readonly List<GeyserEntry> _allGeysers = new List<GeyserEntry>();
        readonly List<CatalogTypeEntry> _catalog = new List<CatalogTypeEntry>();
        readonly FlatSlider[] _sliders = new FlatSlider[5];
        readonly NumericUpDown[] _nums = new NumericUpDown[5];
        readonly Label[] _lblPhys = new Label[5];

        ComboBox _cboWorld, _cboCategory, _cboCatalog;
        ListBox _lstGeysers;
        Label _lblStatus, _lblSelectedTitle, _lblStatsDetail, _lblLivePreview;
        CheckBox _chkMarkStudied;
        Button _btnRefresh, _btnFocus, _btnStudyAll, _btnApplyStats, _btnReplaceType;

        bool _suppressEvents;
        volatile bool _busy;

        public GeyserEditorForm(Bridge bridge, Action<string> log)
        {
            _bridge = bridge;
            _log = log;

            Text = "高级工具 · 存档内间歇泉 / 火山编辑器";
            ClientSize = new Size(968, 652);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = C(BG);
            ForeColor = C(TXT);
            Font = _fontReg;

            BuildUI();
            DpiHelper.ScaleLayout(this);
            float scale = DpiHelper.GetDpiScale();
            if (scale > 1.01f && _lstGeysers != null)
                _lstGeysers.ItemHeight = (int)Math.Round(46 * scale);
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }


        static float OniResample(float t, float min, float max)
        {
            if (t <= 0f) return min;
            if (t >= 1f) return max;
            const float c = 0.002472623f;
            double u = c + (double)t * (1.0 - 2.0 * c);
            double norm = (-Math.Log(1.0 / u - 1.0) + 6.0) / 12.0;
            if (norm < 0.0) norm = 0.0;
            else if (norm > 1.0) norm = 1.0;
            return (float)(min + (max - min) * norm);
        }

        void BuildUI()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = C(CARD) };
            top.Controls.Add(new Label
            {
                Text = "小行星",
                Location = new Point(12, 14),
                Size = new Size(46, 20),
                ForeColor = C(TXT),
                Font = _fontBold
            });
            _cboWorld = new ComboBox
            {
                Location = new Point(60, 11),
                Size = new Size(148, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat
            };
            _cboWorld.SelectedIndexChanged += (s, e) => RefreshFilteredList(0);
            top.Controls.Add(_cboWorld);

            top.Controls.Add(new Label
            {
                Text = "类别",
                Location = new Point(218, 14),
                Size = new Size(36, 20),
                ForeColor = C(TXT),
                Font = _fontBold
            });
            _cboCategory = new ComboBox
            {
                Location = new Point(256, 11),
                Size = new Size(116, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat
            };
            _cboCategory.Items.AddRange(new object[] { "全部类别", "金属火山", "火山", "水泉/液泉", "气体喷口" });
            _cboCategory.SelectedIndex = 0;
            _cboCategory.SelectedIndexChanged += (s, e) => RefreshFilteredList(0);
            top.Controls.Add(_cboCategory);

            _btnRefresh = MakeBtn("刷新列表", 382, 10, 76, 28, BTN, TXT, false);
            _btnRefresh.Click += (s, e) => LoadData(0);
            _btnFocus = MakeBtn("定位镜头", 464, 10, 78, 28, BTN, TXT, false);
            _btnFocus.Click += (s, e) => FocusSelected();
            _btnStudyAll = MakeBtn("一键全部分析", 548, 10, 106, 28, BTN, TXT, false);
            _btnStudyAll.Click += (s, e) => StudyAll();
            top.Controls.Add(_btnRefresh);
            top.Controls.Add(_btnFocus);
            top.Controls.Add(_btnStudyAll);

            _lblStatus = new Label
            {
                Text = "读取存档喷孔中…",
                Location = new Point(662, 14),
                Size = new Size(290, 20),
                ForeColor = C(ACC),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };
            top.Controls.Add(_lblStatus);


            GroupBox gList = new GroupBox
            {
                Text = "存档内已发现 / 已生成间歇泉与火山（点击编辑）",
                Location = new Point(14, 56),
                Size = new Size(406, 582),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };
            _lstGeysers = new ListBox
            {
                Location = new Point(10, 24),
                Size = new Size(386, 546),
                BackColor = C(CARD),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 46,
                Font = _fontReg
            };
            _lstGeysers.DrawItem += DrawGeyserItem;
            _lstGeysers.SelectedIndexChanged += (s, e) => ShowSelectedGeyser();
            gList.Controls.Add(_lstGeysers);


            GroupBox gStats = new GroupBox
            {
                Text = "当前喷孔实时属性与官方边界范围",
                Location = new Point(432, 56),
                Size = new Size(522, 116),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };
            _lblSelectedTitle = new Label
            {
                Text = "请在左侧选择一座间歇泉或火山",
                Location = new Point(12, 22),
                Size = new Size(498, 22),
                ForeColor = C(ACC),
                Font = _fontBold,
                AutoEllipsis = true
            };
            _lblStatsDetail = new Label
            {
                Text = "—",
                Location = new Point(12, 46),
                Size = new Size(498, 62),
                ForeColor = C(TXT),
                Font = _fontReg
            };
            gStats.Controls.Add(_lblSelectedTitle);
            gStats.Controls.Add(_lblStatsDetail);


            GroupBox gEdit = new GroupBox
            {
                Text = "产率与喷发周期参数修改（边界 0% ~ 100% 对应官方理论下限 ~ 上限，永久写入存档）",
                Location = new Point(432, 178),
                Size = new Size(522, 338),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };

            gEdit.Controls.Add(new Label
            {
                Text = "快捷预设：",
                Location = new Point(12, 29),
                Size = new Size(68, 20),
                ForeColor = C(SUB),
                Font = _fontReg
            });
            Button bMax = MakeBtn("满产 (100%)", 80, 24, 98, 26, ACC, 0x0E231C, true);
            bMax.Click += (s, e) => ApplyPresetRolls(100.0f, 100.0f, 100.0f);
            Button bS = MakeBtn("S 级 (95%)", 184, 24, 84, 26, BTN, TXT, false);
            bS.Click += (s, e) => ApplyPresetRolls(95.0f, 95.0f, 80.0f);
            Button bMid = MakeBtn("中位 (50%)", 274, 24, 84, 26, BTN, TXT, false);
            bMid.Click += (s, e) => ApplyAllRolls(50.0f);
            Button bMin = MakeBtn("下限 (0%)", 364, 24, 78, 26, BTN, TXT, false);
            bMin.Click += (s, e) => ApplyPresetRolls(0.0f, 0.0f, 10.0f);
            Button bRnd = MakeBtn("随机", 448, 24, 62, 26, BTN, TXT, false);
            bRnd.Click += (s, e) => ApplyRandomRolls();
            gEdit.Controls.Add(bMax);
            gEdit.Controls.Add(bS);
            gEdit.Controls.Add(bMid);
            gEdit.Controls.Add(bMin);
            gEdit.Controls.Add(bRnd);

            string[] paramNames =
            {
                "活跃期产率 (rateRoll)",
                "活跃期占比 (yearPct)",
                "单次喷发占比 (iterPct)",
                "单次周期长 (iterLen)",
                "大周期总长 (yearLen)"
            };
            for (int i = 0; i < 5; i++)
            {
                int y = 62 + i * 41;
                gEdit.Controls.Add(new Label
                {
                    Text = paramNames[i],
                    Location = new Point(12, y + 3),
                    Size = new Size(132, 20),
                    ForeColor = C(TXT),
                    Font = _fontReg
                });

                int cap = i;
                FlatSlider sl = new FlatSlider
                {
                    Location = new Point(148, y + 1),
                    Size = new Size(122, 22),
                    BackColor = C(BG),
                    Minimum = 0,
                    Maximum = 1000,
                    Value = 500
                };
                sl.ValueChanged += (s, e) =>
                {
                    if (_suppressEvents) return;
                    _suppressEvents = true;
                    _nums[cap].Value = (decimal)sl.Value / 10M;
                    _suppressEvents = false;
                    UpdateLivePreviewLabels();
                };
                _sliders[i] = sl;
                gEdit.Controls.Add(sl);

                NumericUpDown num = new NumericUpDown
                {
                    Location = new Point(276, y),
                    Size = new Size(62, 24),
                    Minimum = 0.0M,
                    Maximum = 100.0M,
                    DecimalPlaces = 1,
                    Increment = 5.0M,
                    Value = 50.0M,
                    BackColor = C(INPUT),
                    ForeColor = C(TXT),
                    Font = _fontReg
                };
                num.ValueChanged += (s, e) =>
                {
                    if (_suppressEvents) return;
                    _suppressEvents = true;
                    int iv = Math.Max(0, Math.Min(1000, (int)Math.Round(num.Value * 10M)));
                    _sliders[cap].Value = iv;
                    _suppressEvents = false;
                    UpdateLivePreviewLabels();
                };
                _nums[i] = num;
                gEdit.Controls.Add(num);

                Label lblP = new Label
                {
                    Text = "%",
                    Location = new Point(342, y + 3),
                    Size = new Size(172, 20),
                    ForeColor = C(SUB),
                    Font = _fontSmall,
                    AutoEllipsis = true
                };
                _lblPhys[i] = lblP;
                gEdit.Controls.Add(lblP);
            }

            _lblLivePreview = new Label
            {
                Text = "修改后预计：—",
                Location = new Point(12, 269),
                Size = new Size(498, 20),
                ForeColor = C(WARN),
                Font = _fontSmall,
                AutoEllipsis = true
            };
            gEdit.Controls.Add(_lblLivePreview);

            _chkMarkStudied = new CheckBox
            {
                AutoSize = false,
                Text = "应用时同时标记为已分析",
                Location = new Point(12, 299),
                Size = new Size(204, 24),
                ForeColor = C(TXT),
                Font = _fontReg,
                Checked = true
            };
            _btnApplyStats = MakeBtn("应用产率与周期修改", 288, 296, 222, 30, ACC, 0x0E231C, true);
            _btnApplyStats.Click += (s, e) => ApplyGeyserStats();
            gEdit.Controls.Add(_chkMarkStudied);
            gEdit.Controls.Add(_btnApplyStats);


            GroupBox gType = new GroupBox
            {
                Text = "替换喷孔 / 火山种类（保留中子物质底座与上方产率设置）",
                Location = new Point(432, 522),
                Size = new Size(522, 116),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };
            gType.Controls.Add(new Label
            {
                Text = "目标种类",
                Location = new Point(12, 27),
                Size = new Size(64, 20),
                ForeColor = C(TXT),
                Font = _fontReg
            });
            _cboCatalog = new ComboBox
            {
                Location = new Point(80, 23),
                Size = new Size(430, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat,
                Font = _fontReg
            };
            gType.Controls.Add(_cboCatalog);

            gType.Controls.Add(new Label
            {
                Text = "提示：替换会销毁当前喷孔并在原坐标重建目标种类（不破坏下方中子物质）。",
                Location = new Point(12, 54),
                Size = new Size(498, 20),
                ForeColor = C(SUB),
                Font = _fontSmall,
                AutoEllipsis = true
            });

            _btnReplaceType = MakeBtn("替换为选中种类…", 288, 79, 222, 28, WARN, 0x1A1508, true);
            _btnReplaceType.Click += (s, e) => ReplaceGeyserType();
            gType.Controls.Add(_btnReplaceType);

            Controls.Add(gList);
            Controls.Add(gStats);
            Controls.Add(gEdit);
            Controls.Add(gType);
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
            b.FlatAppearance.MouseOverBackColor = C(bg == ACC ? ACC_HOV : (bg == BTN ? BTN_HOV : bg));
            return b;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LoadData(0);
        }

        void SetUi(Action act)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { try { BeginInvoke(act); } catch { } return; }
            act();
        }

        void SetBusy(bool busy, string statusText, int statusColor)
        {
            _busy = busy;
            _btnRefresh.Enabled = !busy;
            _btnFocus.Enabled = !busy;
            _btnStudyAll.Enabled = !busy;
            _btnApplyStats.Enabled = !busy;
            _btnReplaceType.Enabled = !busy;
            if (statusText != null)
            {
                _lblStatus.Text = statusText;
                _lblStatus.ForeColor = C(statusColor);
            }
        }

        void LoadData(int selectInstId)
        {
            if (_busy) return;
            SetBusy(true, "读取存档喷孔中…", ACC);
            Thread t = new Thread(() =>
            {
                string errList, errCat;
                string respList = _bridge.RunHelperCmd("GEYSERLIST", out errList, 8000);
                string respCat = _bridge.RunHelperCmd("GEYSERCATALOG", out errCat, 8000);
                List<GeyserEntry> parsedList = ParseGeyserList(respList);
                List<CatalogTypeEntry> parsedCat = ParseCatalog(respCat);

                SetUi(() =>
                {
                    if (respList == null)
                    {
                        SetBusy(false, "读取失败：" + (errList ?? "无响应"), ERR);
                        return;
                    }
                    _allGeysers.Clear();
                    _allGeysers.AddRange(parsedList);
                    if (parsedCat.Count > 0)
                    {
                        _catalog.Clear();
                        _catalog.AddRange(parsedCat);
                        _cboCatalog.Items.Clear();
                        for (int i = 0; i < _catalog.Count; i++)
                            _cboCatalog.Items.Add(_catalog[i]);
                        if (_cboCatalog.Items.Count > 0) _cboCatalog.SelectedIndex = 0;
                    }

                    PopulateWorldCombo();
                    RefreshFilteredList(selectInstId);
                    SetBusy(false, string.Format("已加载 {0} 处间歇泉/火山", _allGeysers.Count), ACC);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void PopulateWorldCombo()
        {
            string prev = _cboWorld.SelectedItem as string;
            _suppressEvents = true;
            _cboWorld.Items.Clear();
            _cboWorld.Items.Add("全部小行星");
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _allGeysers.Count; i++)
            {
                string wn = _allGeysers[i].WorldName;
                if (!string.IsNullOrEmpty(wn) && seen.Add(wn))
                    _cboWorld.Items.Add(wn);
            }
            int sel = 0;
            if (!string.IsNullOrEmpty(prev))
            {
                int idx = _cboWorld.Items.IndexOf(prev);
                if (idx >= 0) sel = idx;
            }
            _cboWorld.SelectedIndex = sel;
            _suppressEvents = false;
        }

        void RefreshFilteredList(int preferInstId)
        {
            if (_suppressEvents) return;
            GeyserEntry curSel = _lstGeysers.SelectedItem as GeyserEntry;
            int targetId = preferInstId != 0 ? preferInstId : (curSel != null ? curSel.InstId : 0);

            string worldFilter = _cboWorld.SelectedIndex > 0 ? (_cboWorld.SelectedItem as string) : null;
            string catFilter = null;
            switch (_cboCategory.SelectedIndex)
            {
                case 1: catFilter = "metal_volcano"; break;
                case 2: catFilter = "volcano"; break;
                case 3: catFilter = "water_geyser"; break;
                case 4: catFilter = "gas_geyser"; break;
            }

            _suppressEvents = true;
            _lstGeysers.BeginUpdate();
            _lstGeysers.Items.Clear();
            int selIdx = -1;
            for (int i = 0; i < _allGeysers.Count; i++)
            {
                GeyserEntry g = _allGeysers[i];
                if (worldFilter != null && !string.Equals(g.WorldName, worldFilter, StringComparison.Ordinal)) continue;
                if (catFilter != null && !string.Equals(g.Category, catFilter, StringComparison.OrdinalIgnoreCase)) continue;
                int idx = _lstGeysers.Items.Add(g);
                if (g.InstId == targetId) selIdx = idx;
            }
            if (selIdx < 0 && _lstGeysers.Items.Count > 0) selIdx = 0;
            _lstGeysers.SelectedIndex = selIdx;
            _lstGeysers.EndUpdate();
            _suppressEvents = false;
            ShowSelectedGeyser();
        }

        void ShowSelectedGeyser()
        {
            GeyserEntry g = _lstGeysers.SelectedItem as GeyserEntry;
            if (g == null)
            {
                _lblSelectedTitle.Text = "请在左侧选择一座间歇泉或火山";
                _lblStatsDetail.Text = "—";
                _lblLivePreview.Text = "修改后预计：—";
                for (int i = 0; i < 5; i++) _lblPhys[i].Text = "%";
                return;
            }
            string grd = g.Grade == 1 ? "S级" : (g.Grade == 2 ? "A级" : (g.Grade == 3 ? "B级" : "C级"));
            _lblSelectedTitle.Text = string.Format("{0}  [{1}]  ·  {2} ({3}, {4})",
                g.DisplayName, g.Studied ? "已分析" : "未分析", g.WorldName, g.X, g.Y);
            _lblStatsDetail.Text = string.Format(CultureInfo.InvariantCulture,
                "均产 {0:0.##} g/s（{1} {2:0}%） · 喷发速率 {3:0.##} g/s · 固定温度 {4:0.#}℃\r\n" +
                "喷发 {5:0.#}s / 间歇 {6:0.#}s  |  活跃 {7:0.#}周期 / 休眠 {8:0.#}周期\r\n" +
                "该种类官方均产边界：{9:0.#} ~ {10:0.#} g/s（产率 0% ~ 100%）",
                g.AvgGps, grd, g.Percentile, g.EmitGps, g.TempC,
                g.EruptSec, g.IdleSec, g.ActiveCycles, g.DormantCycles,
                g.MinAvgGps, g.MaxAvgGps);


            SetSliderPct(0, g.RateRoll * 100f);
            SetSliderPct(1, g.YearPctRoll * 100f);
            SetSliderPct(2, g.IterPctRoll * 100f);
            SetSliderPct(3, g.IterLenRoll * 100f);
            SetSliderPct(4, g.YearLenRoll * 100f);
            UpdateLivePreviewLabels();

            for (int i = 0; i < _catalog.Count; i++)
            {
                if (string.Equals(_catalog[i].PrefabId, g.PrefabId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_catalog[i].RawTypeId, g.RawTypeId, StringComparison.OrdinalIgnoreCase))
                {
                    _cboCatalog.SelectedIndex = i;
                    break;
                }
            }
        }

        void UpdateLivePreviewLabels()
        {
            GeyserEntry g = _lstGeysers.SelectedItem as GeyserEntry;
            if (g == null) return;

            float rRate = (float)_nums[0].Value / 100f;
            float rYearPct = (float)_nums[1].Value / 100f;
            float rIterPct = (float)_nums[2].Value / 100f;
            float rIterLen = (float)_nums[3].Value / 100f;
            float rYearLen = (float)_nums[4].Value / 100f;

            float scaledRateKgCyc = OniResample(rRate, g.MinRateKgCycle, g.MaxRateKgCycle);
            float scaledYearPct = OniResample(rYearPct, g.MinYearPct, g.MaxYearPct);
            float scaledIterPct = OniResample(rIterPct, g.MinIterPct, g.MaxIterPct);
            float scaledIterLen = OniResample(rIterLen, g.MinIterLenSec, g.MaxIterLenSec);
            float scaledYearLen = OniResample(rYearLen, g.MinYearLenSec, g.MaxYearLenSec);

            _lblPhys[0].Text = string.Format(CultureInfo.InvariantCulture, "% → {0:0.#} kg/周 [{1:0}~{2:0}]",
                scaledRateKgCyc, g.MinRateKgCycle, g.MaxRateKgCycle);
            _lblPhys[1].Text = string.Format(CultureInfo.InvariantCulture, "% → 活跃 {0:0.#}% [{1:0}~{2:0}%]",
                scaledYearPct * 100f, g.MinYearPct * 100f, g.MaxYearPct * 100f);
            _lblPhys[2].Text = string.Format(CultureInfo.InvariantCulture, "% → 喷发 {0:0.#}% [{1:0}~{2:0}%]",
                scaledIterPct * 100f, g.MinIterPct * 100f, g.MaxIterPct * 100f);
            _lblPhys[3].Text = string.Format(CultureInfo.InvariantCulture, "% → {0:0}s [{1:0}~{2:0}s]",
                scaledIterLen, g.MinIterLenSec, g.MaxIterLenSec);
            _lblPhys[4].Text = string.Format(CultureInfo.InvariantCulture, "% → {0:0.#}周 [{1:0}~{2:0}周]",
                scaledYearLen / 600f, g.MinYearLenSec / 600f, g.MaxYearLenSec / 600f);

            float onSec = scaledIterLen * scaledIterPct;
            float offSec = Math.Max(0f, scaledIterLen - onSec);
            float emitGps = onSec > 0.001f ? ((scaledRateKgCyc / 600f) * scaledIterLen / onSec * 1000f) : 0f;
            float avgGps = (scaledRateKgCyc / 600f) * scaledYearPct * 1000f;
            float actCyc = (scaledYearLen * scaledYearPct) / 600f;
            float dormCyc = Math.Max(0f, (scaledYearLen / 600f) - actCyc);

            _lblLivePreview.Text = string.Format(CultureInfo.InvariantCulture,
                "修改后预计：均产 {0:0.##} g/s · 喷发 {1:0.##} g/s · 喷{2:0}s/歇{3:0}s · 活{4:0.#}周/眠{5:0.#}周",
                avgGps, emitGps, onSec, offSec, actCyc, dormCyc);
        }

        void SetSliderPct(int idx, float pct)
        {
            if (float.IsNaN(pct) || float.IsInfinity(pct)) pct = 50f;
            int iv = Math.Max(0, Math.Min(1000, (int)Math.Round(pct * 10f)));
            _suppressEvents = true;
            _sliders[idx].Value = iv;
            _nums[idx].Value = (decimal)iv / 10M;
            _suppressEvents = false;
        }

        void ApplyPresetRolls(float ratePct, float yearPct, float iterPct)
        {
            SetSliderPct(0, ratePct);
            SetSliderPct(1, yearPct);
            SetSliderPct(2, iterPct);
            UpdateLivePreviewLabels();
        }

        void ApplyAllRolls(float pct)
        {
            for (int i = 0; i < 5; i++) SetSliderPct(i, pct);
            UpdateLivePreviewLabels();
        }

        void ApplyRandomRolls()
        {
            Random r = new Random(Environment.TickCount);
            for (int i = 0; i < 5; i++)
                SetSliderPct(i, (float)r.Next(1001) / 10f);
            UpdateLivePreviewLabels();
        }

        void ApplyGeyserStats()
        {
            GeyserEntry g = _lstGeysers.SelectedItem as GeyserEntry;
            if (g == null || _busy) return;

            float rRate = (float)_nums[0].Value / 100f;
            float rYearPct = (float)_nums[1].Value / 100f;
            float rIterPct = (float)_nums[2].Value / 100f;
            float rIterLen = (float)_nums[3].Value / 100f;
            float rYearLen = (float)_nums[4].Value / 100f;
            int markStudied = _chkMarkStudied.Checked ? 1 : 0;

            string cmd = string.Format(CultureInfo.InvariantCulture,
                "GEYSERSET {0} {1:0.#####} {2:0.#####} {3:0.#####} {4:0.#####} {5:0.#####} {6}",
                g.InstId, rRate, rIterLen, rIterPct, rYearLen, rYearPct, markStudied);

            int targetId = g.InstId;
            SetBusy(true, "正在写入喷孔参数…", WARN);
            Thread t = new Thread(() =>
            {
                string err;
                string res = _bridge.RunHelperCmd(cmd, out err, 5000);
                SetUi(() =>
                {
                    if (res == null)
                    {
                        SetBusy(false, "修改失败：" + (err ?? "无响应"), ERR);
                        return;
                    }
                    if (_log != null) _log(string.Format("[完成] 修改喷孔 {0} 产率周期 ({1})", g.DisplayName, res));
                    SetBusy(false, "参数已实时写入游戏！", ACC);
                    LoadData(targetId);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void ReplaceGeyserType()
        {
            GeyserEntry g = _lstGeysers.SelectedItem as GeyserEntry;
            CatalogTypeEntry target = _cboCatalog.SelectedItem as CatalogTypeEntry;
            if (g == null || target == null || _busy) return;

            DialogResult dr = MessageBox.Show(this,
                string.Format("确定将位于「{0} ({1}, {2})」的「{3}」替换为「{4}」吗？\r\n" +
                              "替换后将使用当前右侧 5 项产率/周期参数。",
                    g.WorldName, g.X, g.Y, g.DisplayName, target.DisplayName),
                "确认替换喷孔种类", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;

            float rRate = (float)_nums[0].Value / 100f;
            float rYearPct = (float)_nums[1].Value / 100f;
            float rIterPct = (float)_nums[2].Value / 100f;
            float rIterLen = (float)_nums[3].Value / 100f;
            float rYearLen = (float)_nums[4].Value / 100f;
            int keepStudied = _chkMarkStudied.Checked ? 1 : 0;

            string cmd = string.Format(CultureInfo.InvariantCulture,
                "GEYSERTYPE {0} {1} {2:0.#####} {3:0.#####} {4:0.#####} {5:0.#####} {6:0.#####} {7}",
                g.InstId, target.PrefabId, rRate, rIterLen, rIterPct, rYearLen, rYearPct, keepStudied);

            SetBusy(true, "正在重建目标喷孔…", WARN);
            Thread t = new Thread(() =>
            {
                string err;
                string res = _bridge.RunHelperCmd(cmd, out err, 6000);
                SetUi(() =>
                {
                    if (res == null)
                    {
                        SetBusy(false, "替换失败：" + (err ?? "无响应"), ERR);
                        return;
                    }
                    int newId = 0;
                    if (res.StartsWith("replaced:", StringComparison.Ordinal))
                    {
                        string[] ps = res.Substring(9).Split('|');
                        if (ps.Length > 0) int.TryParse(ps[0], out newId);
                    }
                    if (_log != null) _log(string.Format("[完成] 喷孔替换 {0} -> {1} ({2})", g.DisplayName, target.DisplayName, res));
                    SetBusy(false, "替换完成！", ACC);
                    LoadData(newId);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void FocusSelected()
        {
            GeyserEntry g = _lstGeysers.SelectedItem as GeyserEntry;
            if (g == null || _busy) return;
            Thread t = new Thread(() =>
            {
                string err;
                string res = _bridge.RunHelperCmd("GEYSERFOCUS " + g.InstId, out err, 4000);
                SetUi(() =>
                {
                    if (res == null) _lblStatus.Text = "定位失败：" + (err ?? "无响应");
                    else _lblStatus.Text = "已定位镜头：" + g.DisplayName;
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void StudyAll()
        {
            if (_busy) return;
            SetBusy(true, "分析全部间歇泉中…", WARN);
            Thread t = new Thread(() =>
            {
                string err;
                string res = _bridge.RunHelperCmd("STUDYALL", out err, 5000);
                SetUi(() =>
                {
                    if (res == null)
                    {
                        SetBusy(false, "分析失败：" + (err ?? "无响应"), ERR);
                        return;
                    }
                    if (_log != null) _log("[完成] 一键分析全部间歇泉：" + res);
                    SetBusy(false, "全部间歇泉已标记为已分析", ACC);
                    LoadData(0);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void DrawGeyserItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _lstGeysers.Items.Count) return;
            GeyserEntry item = (GeyserEntry)_lstGeysers.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color bg = selected ? C(0x2B3748) : ((e.Index & 1) == 0 ? C(0x1A1C23) : C(0x1F212A));
            using (SolidBrush brBg = new SolidBrush(bg))
                e.Graphics.FillRectangle(brBg, e.Bounds);

            Color catCol = MapPreviewForm.CategoryColor(item.Category);
            float scale = DpiHelper.GetDpiScale();
            int pad = 8;
            int dotSz = Math.Max(8, (int)Math.Round(10 * scale));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brDot = new SolidBrush(catCol))
                e.Graphics.FillEllipse(brDot, e.Bounds.Left + pad, e.Bounds.Top + (e.Bounds.Height / 4) - (dotSz / 2) + 2, dotSz, dotSz);

            int textX = e.Bounds.Left + pad + dotSz + 8;
            int halfH = e.Bounds.Height / 2;
            int badgeW = (int)Math.Round(42 * scale);
            string grade = item.Grade == 1 ? "S" : (item.Grade == 2 ? "A" : (item.Grade == 3 ? "B" : "C"));
            Color gcol = item.Grade == 1 ? Color.FromArgb(0xF5, 0xB9, 0x42)
                : (item.Grade == 2 ? Color.FromArgb(0x00, 0xD6, 0x8F)
                : (item.Grade == 3 ? Color.FromArgb(0x4A, 0xAE, 0xFF) : Color.FromArgb(0x8C, 0x92, 0xA2)));

            Rectangle badgeRect = new Rectangle(e.Bounds.Right - badgeW - 6, e.Bounds.Top + 4, badgeW, halfH - 4);
            using (SolidBrush brBadge = new SolidBrush(Color.FromArgb(44, gcol)))
            using (Pen penBadge = new Pen(gcol))
            {
                e.Graphics.FillRectangle(brBadge, badgeRect);
                e.Graphics.DrawRectangle(penBadge, badgeRect);
            }
            TextRenderer.DrawText(e.Graphics, grade + "级", _fontBold, badgeRect, gcol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            string title = item.DisplayName + (item.Studied ? "" : " [未分析]");
            TextRenderer.DrawText(e.Graphics, title, _fontBold,
                new Rectangle(textX, e.Bounds.Top + 2, e.Bounds.Width - (textX - e.Bounds.Left) - badgeW - 14, halfH),
                catCol, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            string avgStr = item.AvgGps >= 1000f
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.##}kg/s", item.AvgGps / 1000f)
                : string.Format(CultureInfo.InvariantCulture, "{0:0.#}g/s", item.AvgGps);
            string sub = string.Format(CultureInfo.InvariantCulture, "{0} ({1},{2}) · 均产 {3} · {4:0.#}℃",
                item.WorldName, item.X, item.Y, avgStr, item.TempC);
            TextRenderer.DrawText(e.Graphics, sub, _fontReg,
                new Rectangle(textX, e.Bounds.Top + halfH - 1, e.Bounds.Width - (textX - e.Bounds.Left) - 6, halfH),
                C(SUB), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        static List<GeyserEntry> ParseGeyserList(string resp)
        {
            List<GeyserEntry> list = new List<GeyserEntry>();
            if (string.IsNullOrEmpty(resp)) return list;
            string[] lines = resp.Replace("\r\n", "\n").Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                string[] c = lines[i].Split('\t');
                if (c.Length < 26) continue;
                GeyserEntry g = new GeyserEntry();
                int.TryParse(c[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out g.InstId);
                int.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out g.WorldId);
                g.WorldName = c[2];
                int.TryParse(c[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out g.X);
                int.TryParse(c[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out g.Y);
                g.PrefabId = c[5];
                g.RawTypeId = c[6];
                g.Category = c[7];
                g.DisplayName = c[8];
                g.Studied = c[9] == "1";
                float.TryParse(c[10], NumberStyles.Float, CultureInfo.InvariantCulture, out g.AvgGps);
                float.TryParse(c[11], NumberStyles.Float, CultureInfo.InvariantCulture, out g.EmitGps);
                float.TryParse(c[12], NumberStyles.Float, CultureInfo.InvariantCulture, out g.TempC);
                float.TryParse(c[13], NumberStyles.Float, CultureInfo.InvariantCulture, out g.EruptSec);
                float.TryParse(c[14], NumberStyles.Float, CultureInfo.InvariantCulture, out g.IdleSec);
                float.TryParse(c[15], NumberStyles.Float, CultureInfo.InvariantCulture, out g.ActiveCycles);
                float.TryParse(c[16], NumberStyles.Float, CultureInfo.InvariantCulture, out g.DormantCycles);
                byte.TryParse(c[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out g.Grade);
                float.TryParse(c[18], NumberStyles.Float, CultureInfo.InvariantCulture, out g.Percentile);
                float.TryParse(c[19], NumberStyles.Float, CultureInfo.InvariantCulture, out g.RateRoll);
                float.TryParse(c[20], NumberStyles.Float, CultureInfo.InvariantCulture, out g.IterLenRoll);
                float.TryParse(c[21], NumberStyles.Float, CultureInfo.InvariantCulture, out g.IterPctRoll);
                float.TryParse(c[22], NumberStyles.Float, CultureInfo.InvariantCulture, out g.YearLenRoll);
                float.TryParse(c[23], NumberStyles.Float, CultureInfo.InvariantCulture, out g.YearPctRoll);
                float.TryParse(c[24], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MinAvgGps);
                float.TryParse(c[25], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MaxAvgGps);
                if (c.Length >= 36)
                {
                    float.TryParse(c[26], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MinRateKgCycle);
                    float.TryParse(c[27], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MaxRateKgCycle);
                    float.TryParse(c[28], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MinYearPct);
                    float.TryParse(c[29], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MaxYearPct);
                    float.TryParse(c[30], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MinIterPct);
                    float.TryParse(c[31], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MaxIterPct);
                    float.TryParse(c[32], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MinIterLenSec);
                    float.TryParse(c[33], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MaxIterLenSec);
                    float.TryParse(c[34], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MinYearLenSec);
                    float.TryParse(c[35], NumberStyles.Float, CultureInfo.InvariantCulture, out g.MaxYearLenSec);
                }
                list.Add(g);
            }
            return list;
        }

        static List<CatalogTypeEntry> ParseCatalog(string resp)
        {
            List<CatalogTypeEntry> list = new List<CatalogTypeEntry>();
            if (string.IsNullOrEmpty(resp)) return list;
            string[] lines = resp.Replace("\r\n", "\n").Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                string[] c = lines[i].Split('\t');
                if (c.Length < 7) continue;
                CatalogTypeEntry e = new CatalogTypeEntry
                {
                    PrefabId = c[0],
                    RawTypeId = c[1],
                    Category = c[2],
                    DisplayName = c[3]
                };
                float.TryParse(c[4], NumberStyles.Float, CultureInfo.InvariantCulture, out e.TempC);
                float.TryParse(c[5], NumberStyles.Float, CultureInfo.InvariantCulture, out e.MinAvgGps);
                float.TryParse(c[6], NumberStyles.Float, CultureInfo.InvariantCulture, out e.MaxAvgGps);
                list.Add(e);
            }
            return list;
        }
    }
}
