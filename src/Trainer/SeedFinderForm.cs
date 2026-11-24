using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace ONITrainer
{
    public class SeedFinderForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, INPUT = 0x292C36, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, TXT = 0xECEEF2, SUB = 0x949AA8, WARN = 0xF2B138, ERR = 0xF06464;

        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        string _gameDir;
        List<DlcEntry> _dlcs;
        WorldGenCatalog _catalog;
        ClusterDef _curCluster;
        List<WorldPlacementDef> _orderedPlacements = new List<WorldPlacementDef>();
        readonly Dictionary<int, PlanetConstraint> _constraints = new Dictionary<int, PlanetConstraint>();

        bool _suppressEvents;
        volatile bool _closing;
        SearchControl _searchCtrl;
        Thread _searchThread;
        SeedSearchResult _lastResult;

        Label _lblGameDir, _lblPlanetInfo, _lblRuleHint, _lblSearchStatus, _lblSeedOnly;
        CheckBox[] _dlcChecks;
        ComboBox _cboStyle, _cboCluster;
        TextBox _txtTemplateCoord, _txtCoord, _txtPreview;
        ListBox _lstPlanets;
        CheckedListBox _clbTraits;
        CheckBox _chkNoTraits;
        Button _btnExclude, _btnClearPlanet, _btnClearAll, _btnSearch, _btnStop, _btnNext, _btnCopyCoord, _btnCopySeed;

        public SeedFinderForm(string initialGameDir, List<DlcEntry> initialDlcs)
        {
            _gameDir = initialGameDir;
            _dlcs = initialDlcs;

            Text = "开局种子 / 星球特质筛选器  ·  Oxygen Not Included";
            ClientSize = new Size(920, 622);
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = C(BG);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = _fontReg;

            BuildUI();
            DpiHelper.ScaleLayout(this);
            float scale = DpiHelper.GetDpiScale();
            if (scale > 1.01f)
            {
                _lstPlanets.ItemHeight = (int)Math.Round(24 * scale);
                _clbTraits.ColumnWidth = (int)Math.Round(208 * scale);
            }
            InitializeCatalog();
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void BuildUI()
        {

            Panel top = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = C(CARD) };
            top.Controls.Add(new Label
            {
                Text = "开局种子筛选器",
                Font = new Font("Microsoft YaHei UI", 14.5F, FontStyle.Bold),
                ForeColor = C(ACC),
                Location = new Point(16, 4),
                Size = new Size(190, 26)
            });
            top.Controls.Add(Lbl("按已安装 DLC 与游戏官方规则模拟开局，后台自动寻找符合全部星球特质的坐标", 18, 30, 560, SUB));

            Button btnBrowse = B("选择游戏目录…", 796, 11, 108, 30, false);
            btnBrowse.Click += (s, e) => BrowseGameDir();
            top.Controls.Add(btnBrowse);

            _lblGameDir = new Label
            {
                Text = "检测游戏目录中…",
                ForeColor = C(SUB),
                Font = _fontReg,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(580, 15),
                Size = new Size(208, 22)
            };
            top.Controls.Add(_lblGameDir);

            Panel body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };


            GroupBox gCfg = Card("开局环境设置（自动识别本机 DLC，可手动调整）", 16, 8, 888, 92);

            gCfg.Controls.Add(Lbl("已启用 DLC", 14, 24, 80, TXT));
            string[] dlcNames = { "眼冒金星！", "寒霜行星包", "仿生增幅包", "史前行星包", "汪洋行星包" };
            int[] dlcWidths = { 96, 98, 98, 98, 98 };
            _dlcChecks = new CheckBox[5];
            int dx = 98;
            for (int i = 0; i < 5; i++)
            {
                CheckBox cb = new CheckBox
                {
                    Text = dlcNames[i],
                    ForeColor = C(TXT),
                    Font = _fontReg,
                    Location = new Point(dx, 22),
                    Size = new Size(dlcWidths[i], 22),
                    AutoCheck = true
                };
                int capIdx = i;
                cb.CheckedChanged += (s, e) => OnDlcToggleChanged(capIdx);
                _dlcChecks[i] = cb;
                gCfg.Controls.Add(cb);
                dx += dlcWidths[i] + 4;
            }
            gCfg.Controls.Add(Lbl("修改 DLC 开关将实时更新可选星群", 614, 24, 260, SUB));

            gCfg.Controls.Add(Lbl("小行星风格", 14, 58, 76, TXT));
            _cboStyle = new ComboBox
            {
                Location = new Point(92, 55),
                Size = new Size(192, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat,
                Font = _fontReg
            };
            _cboStyle.SelectedIndexChanged += (s, e) => OnStyleChanged();
            gCfg.Controls.Add(_cboStyle);

            gCfg.Controls.Add(Lbl("选择星群", 296, 58, 62, TXT));
            _cboCluster = new ComboBox
            {
                Location = new Point(360, 55),
                Size = new Size(236, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat,
                Font = _fontReg
            };
            _cboCluster.SelectedIndexChanged += (s, e) => OnClusterChanged();
            gCfg.Controls.Add(_cboCluster);

            gCfg.Controls.Add(Lbl("保留坐标后缀", 608, 58, 88, TXT));
            _txtTemplateCoord = new TextBox
            {
                Location = new Point(698, 56),
                Size = new Size(176, 24),
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                Font = _fontReg
            };
            gCfg.Controls.Add(_txtTemplateCoord);


            GroupBox gPlanets = Card("星群行星（点击选择要定制的行星）", 16, 108, 396, 282);
            _lstPlanets = new ListBox
            {
                Location = new Point(12, 24),
                Size = new Size(372, 246),
                BackColor = C(CARD),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24,
                Font = _fontReg
            };
            _lstPlanets.DrawItem += DrawPlanetItem;
            _lstPlanets.SelectedIndexChanged += (s, e) => RefreshTraitEditor();
            gPlanets.Controls.Add(_lstPlanets);


            GroupBox gTraits = Card("行星特质定制（必选特质）", 424, 108, 480, 282);
            _lblPlanetInfo = new Label
            {
                Text = "请先在左侧选择一颗行星",
                ForeColor = C(ACC),
                Font = _fontBold,
                Location = new Point(14, 24),
                Size = new Size(452, 22)
            };
            gTraits.Controls.Add(_lblPlanetInfo);

            _chkNoTraits = new CheckBox
            {
                Text = "要求该行星无特质",
                ForeColor = C(TXT),
                Font = _fontReg,
                Location = new Point(14, 50),
                Size = new Size(156, 22)
            };
            _chkNoTraits.CheckedChanged += (s, e) => OnNoTraitsChecked();
            gTraits.Controls.Add(_chkNoTraits);

            _btnExclude = B("排除特质(0)…", 184, 47, 118, 26, false);
            _btnExclude.Click += (s, e) => EditExcludedTraits();
            _btnClearPlanet = B("清空本星", 310, 47, 76, 26, false);
            _btnClearPlanet.Click += (s, e) => ClearSelectedPlanet();
            _btnClearAll = B("清空全部", 392, 47, 74, 26, false);
            _btnClearAll.Click += (s, e) => ClearAllPlanets();
            gTraits.Controls.Add(_btnExclude);
            gTraits.Controls.Add(_btnClearPlanet);
            gTraits.Controls.Add(_btnClearAll);

            _clbTraits = new CheckedListBox
            {
                Location = new Point(14, 79),
                Size = new Size(452, 168),
                BackColor = C(CARD),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true,
                MultiColumn = true,
                ColumnWidth = 208,
                IntegralHeight = false,
                Font = _fontReg
            };
            _clbTraits.ItemCheck += OnTraitItemCheck;
            gTraits.Controls.Add(_clbTraits);

            _lblRuleHint = Lbl("勾选希望刷出的特质（仅展示当前行星规则允许的特质）", 14, 253, 452, SUB);
            gTraits.Controls.Add(_lblRuleHint);


            GroupBox gRes = Card("筛选控制与结果坐标（粘贴进游戏「坐标」框即可应用）", 16, 398, 888, 162);

            _btnSearch = B("开始后台筛选", 14, 24, 132, 30, true);
            _btnSearch.Click += (s, e) => StartSearch(false);
            _btnStop = B("停止", 154, 24, 66, 30, false);
            _btnStop.Enabled = false;
            _btnStop.Click += (s, e) => StopSearch();
            _btnNext = B("换下一个种子", 228, 24, 112, 30, false);
            _btnNext.Enabled = false;
            _btnNext.Click += (s, e) => StartSearch(true);

            _lblSearchStatus = Lbl("选择至少一项特质后点击「开始后台筛选」；留空后缀默认输出 -0-0-0（可在「保留坐标后缀」贴入游戏现坐标）", 350, 28, 526, SUB);
            gRes.Controls.Add(_btnSearch);
            gRes.Controls.Add(_btnStop);
            gRes.Controls.Add(_btnNext);
            gRes.Controls.Add(_lblSearchStatus);

            gRes.Controls.Add(Lbl("目的地坐标", 14, 64, 78, TXT));
            _txtCoord = new TextBox
            {
                Location = new Point(96, 61),
                Size = new Size(370, 26),
                ReadOnly = true,
                BackColor = C(INPUT),
                ForeColor = C(ACC),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10F, FontStyle.Bold)
            };
            gRes.Controls.Add(_txtCoord);

            _btnCopyCoord = B("复制完整坐标", 474, 60, 114, 28, true);
            _btnCopyCoord.Enabled = false;
            _btnCopyCoord.Click += (s, e) => CopyCoord(true);
            _btnCopySeed = B("复制纯种子", 596, 60, 96, 28, false);
            _btnCopySeed.Enabled = false;
            _btnCopySeed.Click += (s, e) => CopyCoord(false);
            _lblSeedOnly = Lbl("纯种子：—", 704, 64, 172, SUB);
            gRes.Controls.Add(_btnCopyCoord);
            gRes.Controls.Add(_btnCopySeed);
            gRes.Controls.Add(_lblSeedOnly);

            _txtPreview = new TextBox
            {
                Location = new Point(14, 94),
                Size = new Size(860, 58),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = C(CARD),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                Font = _fontReg
            };
            gRes.Controls.Add(_txtPreview);

            body.Controls.Add(gCfg);
            body.Controls.Add(gPlanets);
            body.Controls.Add(gTraits);
            body.Controls.Add(gRes);

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

        void InitializeCatalog()
        {
            if (string.IsNullOrEmpty(_gameDir) || !GameInstallationDetector.IsValidGameDir(_gameDir))
                _gameDir = GameInstallationDetector.DetectGameDir();

            if (string.IsNullOrEmpty(_gameDir) || !GameInstallationDetector.IsValidGameDir(_gameDir))
            {
                _lblGameDir.Text = "未找到游戏目录，请手动选择";
                _lblGameDir.ForeColor = C(WARN);
                _lblSearchStatus.Text = "请点击右上角「选择游戏目录…」定位《缺氧》安装目录（含 OxygenNotIncluded_Data）。";
                _lblSearchStatus.ForeColor = C(WARN);
                return;
            }

            if (_dlcs == null)
                _dlcs = GameInstallationDetector.DetectInstalledDlcs(_gameDir);

            _lblGameDir.Text = "已识别 Steam 游戏与 DLC";
            _lblGameDir.ForeColor = C(ACC);

            _suppressEvents = true;
            for (int i = 0; i < _dlcChecks.Length && i < _dlcs.Count; i++)
            {
                _dlcChecks[i].Checked = _dlcs[i].Enabled;
                _dlcChecks[i].ForeColor = _dlcs[i].Installed ? C(TXT) : C(SUB);
                _dlcChecks[i].Text = _dlcs[i].DisplayName + (_dlcs[i].Installed ? "" : "(未装)");
            }
            _suppressEvents = false;

            ReloadCatalog(null, null);
        }

        void BrowseGameDir()
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "请选择《缺氧》安装根目录（内含 OxygenNotIncluded.exe 与 OxygenNotIncluded_Data）";
                if (!string.IsNullOrEmpty(_gameDir)) fbd.SelectedPath = _gameDir;
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                if (!GameInstallationDetector.IsValidGameDir(fbd.SelectedPath))
                {
                    MessageBox.Show(this, "所选目录未包含 OxygenNotIncluded_Data\\StreamingAssets\\worldgen，请重新选择。",
                        "目录无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _gameDir = fbd.SelectedPath;
                _dlcs = GameInstallationDetector.DetectInstalledDlcs(_gameDir);
                InitializeCatalog();
            }
        }

        void OnDlcToggleChanged(int idx)
        {
            if (_suppressEvents || _dlcs == null || idx < 0 || idx >= _dlcs.Count) return;
            StopSearch();
            _dlcs[idx].Enabled = _dlcChecks[idx].Checked;
            string prevStyle = SelectedStyleKey();
            string prevCluster = _curCluster != null ? _curCluster.FilePath : null;
            ReloadCatalog(prevStyle, prevCluster);
        }

        void ReloadCatalog(string preferStyle, string preferClusterPath)
        {
            if (!GameInstallationDetector.IsValidGameDir(_gameDir)) return;
            HashSet<string> active = new HashSet<string>(StringComparer.Ordinal);
            active.Add("");
            if (_dlcs != null)
                for (int i = 0; i < _dlcs.Count; i++)
                    if (_dlcs[i].Enabled) active.Add(_dlcs[i].Id);

            _catalog = WorldGenCatalog.Load(_gameDir, active);

            _suppressEvents = true;
            _cboStyle.Items.Clear();
            List<KeyValuePair<string, string>> styles = _catalog.GetStyleOptions();
            int selStyle = 0;
            for (int i = 0; i < styles.Count; i++)
            {
                _cboStyle.Items.Add(new StyleItem { Key = styles[i].Key, Title = styles[i].Value });
                if (preferStyle != null && string.Equals(styles[i].Key, preferStyle, StringComparison.OrdinalIgnoreCase))
                    selStyle = i;
            }
            if (_cboStyle.Items.Count > 0) _cboStyle.SelectedIndex = selStyle;
            _suppressEvents = false;

            PopulateClusters(preferClusterPath);
        }

        sealed class StyleItem
        {
            public string Key;
            public string Title;
            public override string ToString() { return Title; }
        }

        string SelectedStyleKey()
        {
            StyleItem item = _cboStyle.SelectedItem as StyleItem;
            return item != null ? item.Key : null;
        }

        void OnStyleChanged()
        {
            if (_suppressEvents) return;
            StopSearch();
            PopulateClusters(null);
        }

        void PopulateClusters(string preferClusterPath)
        {
            if (_catalog == null) return;
            string styleKey = SelectedStyleKey();
            List<ClusterDef> list = styleKey != null ? _catalog.GetClustersForStyle(styleKey) : new List<ClusterDef>();

            _suppressEvents = true;
            _cboCluster.Items.Clear();
            int sel = 0;
            for (int i = 0; i < list.Count; i++)
            {
                _cboCluster.Items.Add(list[i]);
                if (preferClusterPath != null && string.Equals(list[i].FilePath, preferClusterPath, StringComparison.OrdinalIgnoreCase))
                    sel = i;
            }
            if (_cboCluster.Items.Count > 0) _cboCluster.SelectedIndex = sel;
            _suppressEvents = false;

            OnClusterChanged();
        }

        void OnClusterChanged()
        {
            if (_suppressEvents) return;
            StopSearch();
            _curCluster = _cboCluster.SelectedItem as ClusterDef;
            _constraints.Clear();
            _lastResult = null;
            _txtCoord.Clear();
            _txtPreview.Clear();
            _lblSeedOnly.Text = "纯种子：—";
            _btnNext.Enabled = false;
            _btnCopyCoord.Enabled = false;
            _btnCopySeed.Enabled = false;

            _orderedPlacements.Clear();
            if (_curCluster != null)
            {
                _orderedPlacements = _curCluster.GetOrderedPlacements();
                for (int i = 0; i < _orderedPlacements.Count; i++)
                {
                    int pidx = _orderedPlacements[i].PlacementIndex;
                    _constraints[pidx] = new PlanetConstraint { PlacementIndex = pidx };
                }
            }
            RefreshPlanetList(0);
            RefreshTraitEditor();
        }

        void RefreshPlanetList(int keepIndex)
        {
            int idx = keepIndex >= 0 ? keepIndex : _lstPlanets.SelectedIndex;
            _lstPlanets.BeginUpdate();
            _lstPlanets.Items.Clear();
            for (int i = 0; i < _orderedPlacements.Count; i++)
                _lstPlanets.Items.Add(_orderedPlacements[i]);
            _lstPlanets.EndUpdate();
            if (_lstPlanets.Items.Count > 0)
            {
                if (idx < 0 || idx >= _lstPlanets.Items.Count) idx = 0;
                _lstPlanets.SelectedIndex = idx;
            }
        }

        void DrawPlanetItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _lstPlanets.Items.Count) return;
            WorldPlacementDef wp = (WorldPlacementDef)_lstPlanets.Items[e.Index];
            WorldTraitLegalInfo legal = _catalog.GetLegalTraits(wp.World);
            PlanetConstraint pc;
            if (!_constraints.TryGetValue(wp.PlacementIndex, out pc))
                pc = new PlanetConstraint { PlacementIndex = wp.PlacementIndex };

            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color bg = selected ? C(0x2F3849) : C(CARD);
            using (SolidBrush brBg = new SolidBrush(bg))
                e.Graphics.FillRectangle(brBg, e.Bounds);

            int reqCnt = PopCount(pc.RequiredMask);
            int forbCnt = PopCount(pc.ForbiddenMask);
            bool hasCond = reqCnt > 0 || forbCnt > 0 || pc.RequireNoTraits;

            string rangeTxt = legal.IsFixedNone ? "固定无特质"
                : (legal.MinTraits == legal.MaxTraits ? legal.MinTraits + "特质" : legal.MinTraits + "~" + legal.MaxTraits + "特质");

            string condTxt;
            if (legal.IsFixedNone) condTxt = "不可更改";
            else if (pc.RequireNoTraits) condTxt = "要求无特质";
            else if (reqCnt > 0 || forbCnt > 0)
            {
                List<string> parts = new List<string>();
                if (reqCnt > 0) parts.Add("选" + reqCnt + "项");
                if (forbCnt > 0) parts.Add("排" + forbCnt + "项");
                condTxt = string.Join("/", parts.ToArray());
            }
            else condTxt = "不限";

            string line = string.Format("[{0}] {1} ({2})  ·  {3}",
                wp.GroupLabel(), wp.World != null ? wp.World.DisplayName : wp.WorldId, rangeTxt, condTxt);

            Color fg = legal.IsFixedNone ? C(SUB)
                : (hasCond ? C(ACC) : (string.Equals(wp.LocationType, "StartWorld", StringComparison.OrdinalIgnoreCase) ? C(TXT) : C(0xD2D6E0)));

            TextRenderer.DrawText(e.Graphics, line, hasCond ? _fontBold : _fontReg,
                new Rectangle(e.Bounds.Left + 6, e.Bounds.Top + 2, e.Bounds.Width - 10, e.Bounds.Height - 4),
                fg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        static int PopCount(ulong v)
        {
            int c = 0;
            while (v != 0) { v &= (v - 1); c++; }
            return c;
        }

        WorldPlacementDef CurrentPlacement()
        {
            return _lstPlanets.SelectedItem as WorldPlacementDef;
        }

        void RefreshTraitEditor()
        {
            WorldPlacementDef wp = CurrentPlacement();
            _suppressEvents = true;
            _clbTraits.Items.Clear();

            if (wp == null || wp.World == null || _catalog == null)
            {
                _lblPlanetInfo.Text = "请在左侧选择一颗行星";
                _lblPlanetInfo.ForeColor = C(SUB);
                _chkNoTraits.Checked = false;
                _chkNoTraits.Enabled = false;
                _clbTraits.Enabled = false;
                _btnExclude.Enabled = false;
                _btnExclude.Text = "排除特质(0)…";
                _btnClearPlanet.Enabled = false;
                _suppressEvents = false;
                return;
            }

            WorldTraitLegalInfo legal = _catalog.GetLegalTraits(wp.World);
            PlanetConstraint pc = _constraints[wp.PlacementIndex];

            if (legal.IsFixedNone)
            {
                _lblPlanetInfo.Text = wp.World.DisplayName + "  ·  游戏规则固定无特质，无法更改";
                _lblPlanetInfo.ForeColor = C(SUB);
                _chkNoTraits.Checked = false;
                _chkNoTraits.Enabled = false;
                _clbTraits.Enabled = false;
                _btnExclude.Enabled = false;
                _btnExclude.Text = "排除特质(0)…";
                _btnClearPlanet.Enabled = false;
                _lblRuleHint.Text = "该行星设置了 disableWorldTraits，任何种子下都恒为「无特质」。";
                _lblRuleHint.ForeColor = C(SUB);
                _suppressEvents = false;
                return;
            }

            string range = legal.MinTraits == legal.MaxTraits
                ? ("固定生成 " + legal.MinTraits + " 个特质")
                : ("可生成 " + legal.MinTraits + "~" + legal.MaxTraits + " 个特质");
            int reqCnt = PopCount(pc.RequiredMask);
            _lblPlanetInfo.Text = string.Format("{0}  ·  {1}  ·  已选 {2} 个必选",
                wp.World.DisplayName, range, reqCnt);
            _lblPlanetInfo.ForeColor = C(ACC);

            bool canBeEmpty = (legal.MinTraits == 0 && legal.FixedTraits.Count == 0);
            _chkNoTraits.Enabled = canBeEmpty;
            _chkNoTraits.Checked = canBeEmpty && pc.RequireNoTraits;

            _clbTraits.Enabled = !_chkNoTraits.Checked;
            _btnExclude.Enabled = !_chkNoTraits.Checked;
            _btnExclude.Text = "排除特质(" + PopCount(pc.ForbiddenMask) + ")…";
            _btnClearPlanet.Enabled = true;


            for (int i = 0; i < legal.FixedTraits.Count; i++)
            {
                WorldTraitDef ft = legal.FixedTraits[i];
                if (!legal.CandidateTraits.Contains(ft))
                {
                    int idx = _clbTraits.Items.Add(ft);
                    _clbTraits.SetItemChecked(idx, (pc.RequiredMask & (1UL << ft.Index)) != 0);
                }
            }
            for (int i = 0; i < legal.CandidateTraits.Count; i++)
            {
                WorldTraitDef ct = legal.CandidateTraits[i];
                int idx = _clbTraits.Items.Add(ct);
                _clbTraits.SetItemChecked(idx, (pc.RequiredMask & (1UL << ct.Index)) != 0);
            }

            UpdateRuleHint(wp, pc, legal);
            _suppressEvents = false;
        }

        void UpdateRuleHint(WorldPlacementDef wp, PlanetConstraint pc, WorldTraitLegalInfo legal)
        {
            string err;
            List<PlanetConstraint> one = new List<PlanetConstraint>(1);
            one.Add(pc);
            if ((pc.RequiredMask != 0 || pc.ForbiddenMask != 0 || pc.RequireNoTraits) &&
                !SeedSearcher.ValidateConstraints(_catalog, _curCluster, one, out err))
            {
                _lblRuleHint.Text = "⚠ " + err;
                _lblRuleHint.ForeColor = C(WARN);
                return;
            }
            if (legal.FixedTraits.Count > 0)
            {
                List<string> fn = new List<string>();
                for (int i = 0; i < legal.FixedTraits.Count; i++) fn.Add(legal.FixedTraits[i].DisplayName);
                _lblRuleHint.Text = "提示：该星群自带固定必出特质：" + string.Join("、", fn.ToArray());
                _lblRuleHint.ForeColor = C(SUB);
                return;
            }
            _lblRuleHint.Text = "仅显示当前行星规则允许生成的特质；数量上限 " + legal.MaxTraits + " 个";
            _lblRuleHint.ForeColor = C(SUB);
        }

        void OnNoTraitsChecked()
        {
            if (_suppressEvents) return;
            WorldPlacementDef wp = CurrentPlacement();
            if (wp == null) return;
            PlanetConstraint pc = _constraints[wp.PlacementIndex];
            pc.RequireNoTraits = _chkNoTraits.Checked;
            if (pc.RequireNoTraits)
            {
                pc.RequiredMask = 0;
                pc.ForbiddenMask = 0;
            }
            RefreshTraitEditor();
            _lstPlanets.Invalidate();
        }

        void OnTraitItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressEvents) return;
            WorldPlacementDef wp = CurrentPlacement();
            if (wp == null || wp.World == null) return;
            WorldTraitDef trait = _clbTraits.Items[e.Index] as WorldTraitDef;
            if (trait == null) return;

            PlanetConstraint pc = _constraints[wp.PlacementIndex];
            ulong bit = 1UL << trait.Index;
            ulong nextReq = pc.RequiredMask;
            if (e.NewValue == CheckState.Checked) nextReq |= bit;
            else nextReq &= ~bit;

            WorldTraitLegalInfo legal = _catalog.GetLegalTraits(wp.World);
            if (e.NewValue == CheckState.Checked)
            {
                if (PopCount(nextReq) > legal.MaxTraits)
                {
                    e.NewValue = CheckState.Unchecked;
                    _lblRuleHint.Text = "⚠ " + wp.World.DisplayName + " 最多只能生成 " + legal.MaxTraits + " 个特质，请先取消一项。";
                    _lblRuleHint.ForeColor = C(WARN);
                    return;
                }
                if ((pc.ForbiddenMask & bit) != 0)
                    pc.ForbiddenMask &= ~bit;
            }

            pc.RequiredMask = nextReq;
            pc.RequireNoTraits = false;


            BeginInvoke((Action)(() =>
            {
                if (_closing || IsDisposed) return;
                int reqCnt = PopCount(pc.RequiredMask);
                string range = legal.MinTraits == legal.MaxTraits
                    ? ("固定生成 " + legal.MinTraits + " 个特质")
                    : ("可生成 " + legal.MinTraits + "~" + legal.MaxTraits + " 个特质");
                _lblPlanetInfo.Text = string.Format("{0}  ·  {1}  ·  已选 {2} 个必选", wp.World.DisplayName, range, reqCnt);
                _btnExclude.Text = "排除特质(" + PopCount(pc.ForbiddenMask) + ")…";
                UpdateRuleHint(wp, pc, legal);
                _lstPlanets.Invalidate();
            }));
        }

        void EditExcludedTraits()
        {
            WorldPlacementDef wp = CurrentPlacement();
            if (wp == null || wp.World == null || _catalog == null) return;
            WorldTraitLegalInfo legal = _catalog.GetLegalTraits(wp.World);
            if (legal.IsFixedNone || legal.CandidateTraits.Count == 0) return;

            PlanetConstraint pc = _constraints[wp.PlacementIndex];
            using (ExcludeTraitsDialog dlg = new ExcludeTraitsDialog(wp.World.DisplayName, legal.CandidateTraits, pc.RequiredMask, pc.ForbiddenMask))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    pc.ForbiddenMask = dlg.ForbiddenMask;
                    RefreshTraitEditor();
                    _lstPlanets.Invalidate();
                }
            }
        }

        void ClearSelectedPlanet()
        {
            WorldPlacementDef wp = CurrentPlacement();
            if (wp == null) return;
            PlanetConstraint pc = _constraints[wp.PlacementIndex];
            pc.RequiredMask = 0;
            pc.ForbiddenMask = 0;
            pc.RequireNoTraits = false;
            RefreshTraitEditor();
            _lstPlanets.Invalidate();
        }

        void ClearAllPlanets()
        {
            foreach (PlanetConstraint pc in _constraints.Values)
            {
                pc.RequiredMask = 0;
                pc.ForbiddenMask = 0;
                pc.RequireNoTraits = false;
            }
            RefreshTraitEditor();
            _lstPlanets.Invalidate();
        }

        void StartSearch(bool findNext)
        {
            if (_catalog == null || _curCluster == null) return;
            if (_searchThread != null && _searchThread.IsAlive) return;

            List<PlanetConstraint> list = new List<PlanetConstraint>(_constraints.Values);
            string err;
            if (!SeedSearcher.ValidateConstraints(_catalog, _curCluster, list, out err))
            {
                _lblSearchStatus.Text = "⚠ " + err;
                _lblSearchStatus.ForeColor = C(WARN);
                MessageBox.Show(this, err, "条件无法同时满足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int startSeed;
            if (findNext && _lastResult != null)
            {
                startSeed = _lastResult.Seed + 1;
                if (startSeed > SeedSearcher.MaxSearchSeed) startSeed = 1;
            }
            else
            {
                startSeed = new Random(Environment.TickCount).Next(1, 1800000000);
            }

            string tplCoord = _txtTemplateCoord.Text;
            ClusterDef cluster = _curCluster;
            WorldGenCatalog catalog = _catalog;
            SearchControl ctrl = new SearchControl();
            _searchCtrl = ctrl;

            _btnSearch.Enabled = false;
            _btnNext.Enabled = false;
            _btnStop.Enabled = true;
            _lblSearchStatus.Text = "正在后台多线程筛选种子，请稍候…";
            _lblSearchStatus.ForeColor = C(WARN);

            Thread t = new Thread(() =>
            {
                SeedSearchResult res = null;
                try
                {
                    res = SeedSearcher.SearchSync(catalog, cluster, list, startSeed, tplCoord, ctrl, (tested, elapsedMs) =>
                    {
                        double rate = elapsedMs > 1.0 ? (tested / (elapsedMs / 1000.0)) : 0;
                        string msg = string.Format("后台筛选中… 已检测 {0:N0} 个种子（约 {1:N0} 个/秒）", tested, rate);
                        SetUi(() =>
                        {
                            if (_searchCtrl == ctrl && !ctrl.Stop)
                            {
                                _lblSearchStatus.Text = msg;
                                _lblSearchStatus.ForeColor = C(WARN);
                            }
                        });
                    });
                }
                catch { }

                SetUi(() =>
                {
                    if (_searchCtrl != ctrl) return;
                    _btnSearch.Enabled = true;
                    _btnStop.Enabled = false;
                    if (ctrl.Stop)
                    {
                        _lblSearchStatus.Text = "已停止搜索（已检测 " + ctrl.Tested.ToString("N0") + " 个种子）";
                        _lblSearchStatus.ForeColor = C(SUB);
                        return;
                    }
                    if (res == null)
                    {
                        _lblSearchStatus.Text = "未在完整种子空间内找到满足全部条件的组合，请放宽部分条件。";
                        _lblSearchStatus.ForeColor = C(ERR);
                        return;
                    }

                    _lastResult = res;
                    _btnNext.Enabled = cluster.FixedCoordinate <= 0;
                    _btnCopyCoord.Enabled = true;
                    _btnCopySeed.Enabled = true;

                    _txtCoord.Text = res.Coordinate;
                    _lblSeedOnly.Text = "纯种子：" + res.Seed.ToString(CultureInfo.InvariantCulture);
                    _lblSearchStatus.Text = string.Format("已找到符合条件的世界种子！耗时 {0:0.00} 秒（检测 {1:N0} 个种子）",
                        res.ElapsedMs / 1000.0, res.SeedsTested);
                    _lblSearchStatus.ForeColor = C(ACC);
                    _txtPreview.Text = BuildPreviewText(cluster, res);
                });
            });
            t.IsBackground = true;
            _searchThread = t;
            t.Start();
        }

        string BuildPreviewText(ClusterDef cluster, SeedSearchResult res)
        {
            List<WorldPlacementDef> ordered = cluster.GetOrderedPlacements();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < ordered.Count; i++)
            {
                WorldPlacementDef wp = ordered[i];
                List<WorldTraitDef> ts;
                if (!res.PlanetTraits.TryGetValue(wp.PlacementIndex, out ts) || ts.Count == 0)
                {
                    sb.AppendFormat("[{0}] {1}：无特质", wp.GroupLabel(), wp.World != null ? wp.World.DisplayName : wp.WorldId);
                }
                else
                {
                    string[] names = new string[ts.Count];
                    for (int k = 0; k < ts.Count; k++) names[k] = ts[k].DisplayName;
                    sb.AppendFormat("[{0}] {1}：{2}", wp.GroupLabel(), wp.World != null ? wp.World.DisplayName : wp.WorldId, string.Join("、", names));
                }
                if (i < ordered.Count - 1) sb.Append("    |    ");
                if (i == 0 || (i < ordered.Count - 1 && wp.LocationType != ordered[i + 1].LocationType))
                    sb.Append("\r\n");
            }
            return sb.ToString();
        }

        void StopSearch()
        {
            if (_searchCtrl != null) _searchCtrl.Stop = true;
        }

        void CopyCoord(bool full)
        {
            if (_lastResult == null) return;
            string text = full
                ? SeedSearcher.FormatCoordinate(_curCluster != null ? _curCluster.CoordinatePrefix : "", _lastResult.Seed, _txtTemplateCoord.Text)
                : _lastResult.Seed.ToString(CultureInfo.InvariantCulture);
            try
            {
                if (full) _txtCoord.Text = text;
                Clipboard.SetText(text);
                _lblSearchStatus.Text = (full ? "已复制完整坐标到剪贴板：" : "已复制纯种子到剪贴板：") + text;
                _lblSearchStatus.ForeColor = C(ACC);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            StopSearch();
            if (_searchThread != null && _searchThread.IsAlive)
                _searchThread.Join(250);
            base.OnFormClosing(e);
        }
    }

    internal sealed class ExcludeTraitsDialog : Form
    {
        public ulong ForbiddenMask { get; private set; }
        readonly CheckedListBox _clb;

        public ExcludeTraitsDialog(string worldName, List<WorldTraitDef> candidates, ulong requiredMask, ulong forbiddenMask)
        {
            ForbiddenMask = forbiddenMask;
            Text = "排除不想要的特质  ·  " + worldName;
            ClientSize = new Size(420, 360);
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(255, 0x16, 0x17, 0x1C);
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(new Label
            {
                Text = "勾选「绝不希望出现」的特质（已被设为必选的特质不可同时排除）：",
                ForeColor = Color.FromArgb(255, 0x94, 0x9A, 0xA8),
                Location = new Point(14, 12),
                Size = new Size(392, 22)
            });

            _clb = new CheckedListBox
            {
                Location = new Point(14, 40),
                Size = new Size(392, 260),
                BackColor = Color.FromArgb(255, 0x20, 0x22, 0x2A),
                ForeColor = Color.FromArgb(255, 0xEC, 0xEE, 0xF2),
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true,
                MultiColumn = true,
                ColumnWidth = 184,
                IntegralHeight = false
            };

            for (int i = 0; i < candidates.Count; i++)
            {
                WorldTraitDef t = candidates[i];
                if ((requiredMask & (1UL << t.Index)) != 0) continue;
                int idx = _clb.Items.Add(t);
                if ((forbiddenMask & (1UL << t.Index)) != 0) _clb.SetItemChecked(idx, true);
            }
            Controls.Add(_clb);

            Button ok = new Button
            {
                Text = "确定",
                Location = new Point(226, 314),
                Size = new Size(86, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(255, 0x00, 0xD6, 0x8F),
                ForeColor = Color.FromArgb(255, 0x0E, 0x23, 0x1C),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += (s, e) =>
            {
                ulong m = 0;
                for (int i = 0; i < _clb.CheckedItems.Count; i++)
                {
                    WorldTraitDef t = _clb.CheckedItems[i] as WorldTraitDef;
                    if (t != null) m |= (1UL << t.Index);
                }
                ForbiddenMask = m;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            Button cancel = new Button
            {
                Text = "取消",
                Location = new Point(320, 314),
                Size = new Size(86, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(255, 0x32, 0x36, 0x42),
                ForeColor = Color.FromArgb(255, 0xEC, 0xEE, 0xF2)
            };
            cancel.FlatAppearance.BorderSize = 0;
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            DpiHelper.ScaleLayout(this);
            float scale = DpiHelper.GetDpiScale();
            if (scale > 1.01f) _clb.ColumnWidth = (int)Math.Round(184 * scale);
        }
    }
}
