using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
        readonly Func<BridgePreviewState> _getBridge;
        readonly Action _openSettings;
        WorldGenCatalog _catalog;
        ClusterDef _curCluster;
        List<WorldPlacementDef> _orderedPlacements = new List<WorldPlacementDef>();
        readonly Dictionary<int, PlanetConstraint> _constraints = new Dictionary<int, PlanetConstraint>();

        bool _suppressEvents;
        volatile bool _closing;
        SearchControl _searchCtrl;
        Thread _searchThread;
        SeedSearchResult _lastResult;

        int _baseClientHeight;
        int _advClientHeight;
        Action _advChangedHandler;

        int _previewCacheSeed;
        readonly Dictionary<int, MapPreviewData> _previewCache = new Dictionary<int, MapPreviewData>();
        MapPreviewForm _mapPreviewForm;

        sealed class BoundPoiRule
        {
            public int PlacementIndex;
            public string PlanetName;
            public PoiFilterRule Rule;

            public override string ToString()
            {
                return string.Format("[{0}] {1}", PlanetName, Rule.ToString());
            }
        }
        readonly List<BoundPoiRule> _poiRules = new List<BoundPoiRule>();

        Label _lblGameDir, _lblPlanetInfo, _lblRuleHint, _lblSearchStatus, _lblSeedOnly;
        CheckBox[] _dlcChecks;
        ComboBox _cboStyle, _cboCluster;
        TextBox _txtTemplateCoord, _txtCoord, _txtPreview;
        ListBox _lstPlanets;
        CheckedListBox _clbTraits;
        CheckBox _chkNoTraits;
        Button _btnExclude, _btnClearPlanet, _btnClearAll, _btnSearch, _btnStop, _btnNext, _btnCopyCoord, _btnCopySeed, _btnPreviewMap;
        Button _btnSyncCfg, _btnFav, _btnHistory, _btnCommunity, _btnSubmitSeed;
        string _otherCode = "", _storyCode = "", _mixingCode = "", _storyIdsCsv = "";
        bool _templateAutoSynced;
        int _lastIndexMatchedSeed;
        Action _seedDbStateHandler;
        SeedHistoryForm _historyForm;
        CommunitySeedForm _communityForm;

        GroupBox _gPoiFilter;
        ComboBox _cboPoiPlanet, _cboPoiType, _cboPoiOp, _cboPoiGrade, _cboWorkers;
        NumericUpDown _numPoiCount, _numPoiDist, _numPoiYield;
        ListBox _lstPoiRules;
        Button _btnAddPoi, _btnClearPoi;
        bool _poiMutexUpdating;

        public SeedFinderForm(string initialGameDir, List<DlcEntry> initialDlcs)
            : this(initialGameDir, initialDlcs, null, null)
        {
        }

        public SeedFinderForm(
            string initialGameDir,
            List<DlcEntry> initialDlcs,
            Func<BridgePreviewState> getBridge,
            Action openSettings)
        {
            _gameDir = initialGameDir;
            _dlcs = initialDlcs;
            _getBridge = getBridge;
            _openSettings = openSettings;

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
            _baseClientHeight = ClientSize.Height;
            _advClientHeight = (int)Math.Round(812 * scale);
            if (scale > 1.01f)
            {
                _lstPlanets.ItemHeight = (int)Math.Round(24 * scale);
                _clbTraits.ColumnWidth = (int)Math.Round(208 * scale);
            }

            _advChangedHandler = OnGlobalAdvancedModeChanged;
            TrainerSettings.AdvancedModeChanged += _advChangedHandler;
            _seedDbStateHandler = () => SetUi(RefreshUploadPendingBadge);
            CommunitySeedDb.StateChanged += _seedDbStateHandler;
            ApplyAdvancedModeLayout();

            InitializeCatalog();
            RefreshUploadPendingBadge();
            TriggerCommunityAutoPull();
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
            top.Controls.Add(Lbl("按本机 DLC 与游戏规则模拟开局，后台自动筛选星球特质", 18, 30, 352, SUB));

            _btnCommunity = B("社区库", 552, 11, 66, 30, false);
            _btnCommunity.Click += (s, e) => OpenCommunityDbDialog();
            top.Controls.Add(_btnCommunity);

            _btnSyncCfg = B("读取游戏设置", 624, 11, 88, 30, false);
            _btnSyncCfg.Click += (s, e) => SyncGameConfig();
            top.Controls.Add(_btnSyncCfg);

            Button btnBrowse = B("选择游戏目录…", 718, 11, 108, 30, false);
            btnBrowse.Click += (s, e) => BrowseGameDir();
            top.Controls.Add(btnBrowse);

            Button btnSettings = B("设置", 832, 11, 72, 30, false);
            btnSettings.Click += (s, e) => OpenSettingsDialog();
            top.Controls.Add(btnSettings);

            _lblGameDir = new Label
            {
                Text = "检测游戏目录中…",
                ForeColor = C(SUB),
                Font = _fontReg,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                Location = new Point(376, 14),
                Size = new Size(170, 22)
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
            _txtTemplateCoord.TextChanged += (s, e) => { if (!_suppressEvents) _templateAutoSynced = false; };
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
                Size = new Size(452, 156),
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
            _clbTraits.SelectedIndexChanged += (s, e) => OnTraitSelectedChanged();
            gTraits.Controls.Add(_clbTraits);

            _lblRuleHint = new Label
            {
                Text = "勾选希望刷出的特质（仅展示当前行星规则允许的特质）",
                ForeColor = C(SUB),
                Font = _fontReg,
                Location = new Point(14, 240),
                Size = new Size(452, 36),
                AutoEllipsis = true
            };
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
            _lblSearchStatus.AutoEllipsis = true;
            gRes.Controls.Add(_btnSearch);
            gRes.Controls.Add(_btnStop);
            gRes.Controls.Add(_btnNext);
            gRes.Controls.Add(_lblSearchStatus);

            gRes.Controls.Add(Lbl("目的地坐标", 14, 64, 72, TXT));
            _txtCoord = new TextBox
            {
                Location = new Point(88, 61),
                Size = new Size(186, 26),
                ReadOnly = false,
                BackColor = C(INPUT),
                ForeColor = C(ACC),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10F, FontStyle.Bold)
            };
            _txtCoord.TextChanged += (s, e) => OnCoordTextChanged();
            gRes.Controls.Add(_txtCoord);

            _btnCopyCoord = B("复制坐标", 278, 60, 68, 28, false);
            _btnCopyCoord.Enabled = false;
            _btnCopyCoord.Click += (s, e) => CopyCoord(true);
            _btnCopySeed = B("复制种子", 350, 60, 68, 28, false);
            _btnCopySeed.Enabled = false;
            _btnCopySeed.Click += (s, e) => CopyCoord(false);
            _btnPreviewMap = B("预览地图", 422, 60, 72, 28, true);
            _btnPreviewMap.Enabled = false;
            _btnPreviewMap.Click += (s, e) => OpenMapPreview();
            _btnFav = B("★ 收藏", 498, 60, 64, 28, false);
            _btnFav.Enabled = false;
            _btnFav.Click += (s, e) => ToggleFavoriteCurrent();
            _btnHistory = B("历史/收藏", 566, 60, 74, 28, false);
            _btnHistory.Click += (s, e) => OpenHistoryDialog();
            _btnSubmitSeed = B("上传记录(0)", 644, 60, 104, 28, false);
            _btnSubmitSeed.Enabled = true;
            _btnSubmitSeed.Click += (s, e) => OpenSubmitSeedDialog();
            _lblSeedOnly = Lbl("纯种子：—", 752, 64, 128, SUB);
            _lblSeedOnly.AutoEllipsis = true;
            gRes.Controls.Add(_btnCopyCoord);
            gRes.Controls.Add(_btnCopySeed);
            gRes.Controls.Add(_btnPreviewMap);
            gRes.Controls.Add(_btnFav);
            gRes.Controls.Add(_btnHistory);
            gRes.Controls.Add(_btnSubmitSeed);
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


            _gPoiFilter = Card("高级功能：按间歇泉 / 火山 / 遗迹筛选（支持离线多进程极速筛选）", 16, 568, 888, 184);
            _gPoiFilter.Visible = false;

            _gPoiFilter.Controls.Add(Lbl("目标行星", 12, 27, 62, TXT));
            _cboPoiPlanet = new ComboBox
            {
                Location = new Point(76, 24),
                Size = new Size(184, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat,
                Font = _fontReg
            };
            _gPoiFilter.Controls.Add(_cboPoiPlanet);

            _gPoiFilter.Controls.Add(Lbl("泉/火山类型", 270, 27, 80, TXT));
            _cboPoiType = new ComboBox
            {
                Location = new Point(352, 24),
                Size = new Size(268, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat,
                Font = _fontReg
            };
            List<PoiFilterCatalogEntry> poiCat = MapPreviewData.GetFilterCatalog();
            for (int i = 0; i < poiCat.Count; i++) _cboPoiType.Items.Add(poiCat[i]);
            if (_cboPoiType.Items.Count > 0) _cboPoiType.SelectedIndex = 0;
            _gPoiFilter.Controls.Add(_cboPoiType);

            _gPoiFilter.Controls.Add(Lbl("数量", 630, 27, 36, TXT));
            _cboPoiOp = new ComboBox
            {
                Location = new Point(668, 24),
                Size = new Size(82, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat,
                Font = _fontReg
            };
            _cboPoiOp.Items.AddRange(new object[] { "至少 ≥", "至多 ≤" });
            _cboPoiOp.SelectedIndex = 0;
            _gPoiFilter.Controls.Add(_cboPoiOp);

            _numPoiCount = new NumericUpDown
            {
                Location = new Point(756, 24),
                Size = new Size(54, 26),
                Minimum = 0,
                Maximum = 9,
                Value = 1,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                Font = _fontReg
            };
            _gPoiFilter.Controls.Add(_numPoiCount);
            _gPoiFilter.Controls.Add(Lbl("个", 814, 27, 24, SUB));

            _gPoiFilter.Controls.Add(Lbl("距起点 ≤", 12, 59, 60, TXT));
            _numPoiDist = new NumericUpDown
            {
                Location = new Point(74, 56),
                Size = new Size(58, 26),
                Minimum = 0,
                Maximum = 500,
                Value = 0,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                Font = _fontReg
            };
            _gPoiFilter.Controls.Add(_numPoiDist);
            _gPoiFilter.Controls.Add(Lbl("格", 135, 59, 20, SUB));

            _gPoiFilter.Controls.Add(Lbl("均产 ≥", 162, 59, 48, TXT));
            _numPoiYield = new NumericUpDown
            {
                Location = new Point(212, 56),
                Size = new Size(68, 26),
                Minimum = 0,
                Maximum = 10000,
                Increment = 100,
                Value = 0,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                Font = _fontReg
            };
            _numPoiYield.ValueChanged += (s, e) => OnPoiYieldChanged();
            _gPoiFilter.Controls.Add(_numPoiYield);
            _gPoiFilter.Controls.Add(Lbl("g/s", 284, 59, 28, SUB));

            _gPoiFilter.Controls.Add(Lbl("评级", 322, 59, 34, TXT));
            _cboPoiGrade = new ComboBox
            {
                Location = new Point(358, 56),
                Size = new Size(134, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat,
                Font = _fontReg
            };
            _cboPoiGrade.Items.AddRange(new object[] { "不限", "仅 S 级 (≥85%)", "A 级以上 (≥65%)", "B 级以上 (≥35%)" });
            _cboPoiGrade.SelectedIndex = 0;
            _cboPoiGrade.SelectedIndexChanged += (s, e) => OnPoiGradeChanged();
            _gPoiFilter.Controls.Add(_cboPoiGrade);
            _gPoiFilter.Controls.Add(Lbl("（距填0不限；均产与评级二选一）", 498, 59, 232, SUB));

            _btnAddPoi = B("添加条件", 738, 55, 76, 26, true);
            _btnAddPoi.Click += (s, e) => AddPoiRule();
            _btnClearPoi = B("清空", 820, 55, 54, 26, false);
            _btnClearPoi.Click += (s, e) => ClearPoiRules();
            _gPoiFilter.Controls.Add(_btnAddPoi);
            _gPoiFilter.Controls.Add(_btnClearPoi);

            _lstPoiRules = new ListBox
            {
                Location = new Point(14, 88),
                Size = new Size(860, 58),
                BackColor = C(CARD),
                ForeColor = C(ACC),
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                Font = _fontReg
            };
            _lstPoiRules.MouseDoubleClick += (s, e) => RemoveSelectedPoiRule();
            _gPoiFilter.Controls.Add(_lstPoiRules);

            _gPoiFilter.Controls.Add(Lbl("评级：S ≥85%(前15%) · A 65~84% · B 35~64% · C <35%；双击条件可删。", 14, 154, 584, SUB));
            _gPoiFilter.Controls.Add(Lbl("并行进程", 604, 154, 58, TXT));
            _cboWorkers = new ComboBox
            {
                Location = new Point(664, 150),
                Size = new Size(210, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat,
                Font = _fontReg
            };
            PopulateWorkersCombo();
            _gPoiFilter.Controls.Add(_cboWorkers);

            body.Controls.Add(gCfg);
            body.Controls.Add(gPlanets);
            body.Controls.Add(gTraits);
            body.Controls.Add(gRes);
            body.Controls.Add(_gPoiFilter);

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
            if (string.IsNullOrEmpty(_txtCoord.Text.Trim()) && !string.IsNullOrEmpty(TrainerSettings.LastSeedCoord))
            {
                _suppressEvents = true;
                _txtCoord.Text = TrainerSettings.LastSeedCoord;
                _suppressEvents = false;
                OnCoordTextChanged();
            }
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
            _searchCtrl = null;
            _lastResult = null;
            _constraints.Clear();
            _poiRules.Clear();
            _previewCache.Clear();
            _previewCacheSeed = 0;

            _curCluster = _cboCluster.SelectedItem as ClusterDef;
            bool keepCoord = !string.IsNullOrEmpty(_txtCoord.Text.Trim());
            if (!keepCoord)
            {
                _txtPreview.Clear();
                _lblSeedOnly.Text = "纯种子：—";
                _btnNext.Enabled = false;
                _btnCopyCoord.Enabled = false;
                _btnCopySeed.Enabled = false;
                _btnPreviewMap.Enabled = (_curCluster != null && _curCluster.FixedCoordinate > 0);
            }

            _orderedPlacements.Clear();
            if (_curCluster != null)
            {
                _orderedPlacements = _curCluster.GetOrderedPlacements();
                for (int i = 0; i < _orderedPlacements.Count; i++)
                {
                    int pidx = _orderedPlacements[i].PlacementIndex;
                    _constraints[pidx] = new PlanetConstraint { PlacementIndex = pidx };
                }
                if (_curCluster.FixedCoordinate > 0 && !keepCoord)
                {
                    _suppressEvents = true;
                    _txtCoord.Text = SeedSearcher.FormatCoordinate(_curCluster.CoordinatePrefix, _curCluster.FixedCoordinate, _txtTemplateCoord.Text);
                    _suppressEvents = false;
                    _lblSeedOnly.Text = "纯种子：" + _curCluster.FixedCoordinate.ToString(CultureInfo.InvariantCulture);
                    _btnCopyCoord.Enabled = true;
                    _btnCopySeed.Enabled = true;
                }
            }
            if (keepCoord) OnCoordTextChanged();
            RefreshPoiPlanetCombo();
            RefreshPoiRulesList();
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

        void OnTraitSelectedChanged()
        {
            if (_suppressEvents) return;
            WorldPlacementDef wp = CurrentPlacement();
            if (wp == null || wp.World == null || _catalog == null) return;
            WorldTraitLegalInfo legal = _catalog.GetLegalTraits(wp.World);
            PlanetConstraint pc = _constraints[wp.PlacementIndex];
            UpdateRuleHint(wp, pc, legal);
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
            WorldTraitDef sel = _clbTraits.SelectedItem as WorldTraitDef;
            if (sel != null)
            {
                string desc = !string.IsNullOrEmpty(sel.Description) ? sel.Description : "（游戏未提供该特质的说明文本）";
                _lblRuleHint.Text = "【" + sel.DisplayName + "】" + desc;
                _lblRuleHint.ForeColor = C(0xBFD8CF);
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
            _lblRuleHint.Text = "仅显示当前行星规则允许生成的特质；数量上限 " + legal.MaxTraits + " 个（点击特质查看效果说明）";
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
            bool hasTraitConstraint = false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].RequiredMask != 0 || list[i].ForbiddenMask != 0 || list[i].RequireNoTraits)
                { hasTraitConstraint = true; break; }

            bool usePoiFilter = TrainerSettings.AdvancedMode && _poiRules.Count > 0;
            if (hasTraitConstraint || !usePoiFilter)
            {
                string err;
                if (!SeedSearcher.ValidateConstraints(_catalog, _curCluster, list, out err))
                {
                    _lblSearchStatus.Text = "⚠ " + err;
                    _lblSearchStatus.ForeColor = C(WARN);
                    MessageBox.Show(this, err, "条件无法同时满足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            string tplCoord = _txtTemplateCoord.Text;
            ClusterDef cluster = _curCluster;
            WorldGenCatalog catalog = _catalog;
            string dlcCsv = GetActiveDlcCsv();
            List<BoundPoiRule> poiRulesSnap = new List<BoundPoiRule>(_poiRules);


            if (!findNext) _lastIndexMatchedSeed = 0;
            bool tryIndexNow = !findNext || _lastIndexMatchedSeed > 0;
            if (tryIndexNow)
            {
                List<KeyValuePair<int, List<PoiFilterRule>>> idxGroups = null;
                if (usePoiFilter)
                {
                    Dictionary<int, List<PoiFilterRule>> grpMap = new Dictionary<int, List<PoiFilterRule>>();
                    for (int i = 0; i < poiRulesSnap.Count; i++)
                    {
                        BoundPoiRule b = poiRulesSnap[i];
                        List<PoiFilterRule> rl;
                        if (!grpMap.TryGetValue(b.PlacementIndex, out rl))
                        {
                            rl = new List<PoiFilterRule>();
                            grpMap[b.PlacementIndex] = rl;
                        }
                        rl.Add(b.Rule);
                    }
                    idxGroups = new List<KeyValuePair<int, List<PoiFilterRule>>>(grpMap);
                }

                Stopwatch idxSw = Stopwatch.StartNew();
                SeedSearchResult idxMatched;
                Dictionary<int, MapPreviewData> idxMaps;
                int idxTotalCount;
                int minExcl = findNext ? _lastIndexMatchedSeed : 0;
                if (CommunitySeedDb.TryFastIndexMatch(
                    catalog, cluster, list, hasTraitConstraint, idxGroups,
                    dlcCsv, _mixingCode, tplCoord, minExcl,
                    out idxMatched, out idxMaps, out idxTotalCount))
                {
                    idxSw.Stop();
                    idxMatched.ElapsedMs = idxSw.Elapsed.TotalMilliseconds;
                    _lastIndexMatchedSeed = idxMatched.Seed;
                    SearchControl instantCtrl = new SearchControl { Tested = idxMatched.SeedsTested };
                    _searchCtrl = instantCtrl;
                    FinishSearchOnUi(
                        cluster, instantCtrl, idxMatched, idxMaps, null,
                        string.Format("⚡ 社区/本地索引极速命中（库内共匹配 {0} 个），", idxTotalCount));
                    return;
                }

                _lastIndexMatchedSeed = 0;
            }

            BridgePreviewState st0 = _getBridge != null ? _getBridge() : null;
            bool hasCustomMixing = !string.IsNullOrEmpty(_mixingCode) && !string.Equals(_mixingCode.Trim(), "0", StringComparison.Ordinal);
            bool canHeadless = !string.IsNullOrEmpty(_gameDir) && GameInstallationDetector.IsValidGameDir(_gameDir);
            int workersCfg = TrainerSettings.ParallelWorkers;

            if (usePoiFilter)
            {
                if (hasCustomMixing)
                {
                    if (st0 == null || st0.Bridge == null || st0.InSave)
                    {
                        string msg = string.Format(
                            "检测到已同步自定义 DLC 混搭设置（{0}）；为防止闪退，混搭模式需停留在《缺氧》主菜单再筛选，或返回主菜单关闭混搭后使用多进程离线筛选。",
                            _mixingCode);
                        _lblSearchStatus.Text = "⚠ " + msg;
                        _lblSearchStatus.ForeColor = C(WARN);
                        MessageBox.Show(this, msg, "需要主菜单连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else if (workersCfg == 0)
                {
                    if (st0 == null || st0.Bridge == null || st0.InSave)
                    {
                        string msg = "当前并行进程设置为「0：仅用游戏主菜单引擎」，请启动《缺氧》并停留在主菜单，或将「并行进程」切换为「自动推荐」以离线筛选。";
                        _lblSearchStatus.Text = "⚠ " + msg;
                        _lblSearchStatus.ForeColor = C(WARN);
                        MessageBox.Show(this, msg, "需要主菜单连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else if (!canHeadless && (st0 == null || st0.Bridge == null || st0.InSave))
                {
                    string msg = "社区索引库中暂未命中该组合，需本地引擎继续筛选：请先点击右上角「选择游戏目录…」定位《缺氧》安装目录，或启动游戏停留在主菜单。";
                    _lblSearchStatus.Text = "⚠ " + msg;
                    _lblSearchStatus.ForeColor = C(WARN);
                    MessageBox.Show(this, msg, "需要游戏目录或主菜单连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
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

            SearchControl ctrl = new SearchControl();
            _searchCtrl = ctrl;

            _btnSearch.Enabled = false;
            _btnNext.Enabled = false;
            _btnStop.Enabled = true;
            _lblSearchStatus.Text = usePoiFilter
                ? ((!hasCustomMixing && canHeadless && workersCfg != 0)
                    ? string.Format("正在启动 {0} 核并行筛选…", cluster.FixedCoordinate > 0 ? 1 : TrainerSettings.EffectiveWorkerCount)
                    : "高级筛选启动中… 先匹配特质再进行验证")
                : "正在后台多线程筛选种子，请稍候…";
            _lblSearchStatus.ForeColor = C(WARN);

            Thread t = new Thread(() =>
            {
                if (!usePoiFilter)
                {
                    RunTraitOnlySearch(catalog, cluster, list, startSeed, tplCoord, ctrl);
                    return;
                }
                RunAdvancedPoiSearch(catalog, cluster, list, hasTraitConstraint, startSeed, tplCoord, dlcCsv, poiRulesSnap, ctrl);
            });
            t.IsBackground = true;
            _searchThread = t;
            t.Start();
        }

        void RunTraitOnlySearch(
            WorldGenCatalog catalog,
            ClusterDef cluster,
            List<PlanetConstraint> list,
            int startSeed,
            string tplCoord,
            SearchControl ctrl)
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
                if (res != null && !ctrl.Stop)
                {
                    CommunitySeedDb.RecordTraitMatchSeed(res, cluster, GetActiveDlcCsv(), _storyCode, _mixingCode, catalog, true);
                }
            }
            catch { }

            FinishSearchOnUi(cluster, ctrl, res, null, null);
        }

        static bool IsBadSeedPreviewError(string err)
        {
            if (string.IsNullOrEmpty(err)) return false;
            return err.IndexOf("gen-offline:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   err.IndexOf("render-offline:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void RunAdvancedPoiSearch(
            WorldGenCatalog catalog,
            ClusterDef cluster,
            List<PlanetConstraint> list,
            bool hasTraitConstraint,
            int startSeed,
            string tplCoord,
            string dlcCsv,
            List<BoundPoiRule> poiRulesSnap,
            SearchControl ctrl)
        {
            Stopwatch sw = Stopwatch.StartNew();
            long totalTraitTested = 0;
            int mapsTested = 0;
            int badSeedsSkipped = 0;
            int consecutiveGenFails = 0;
            int curSeed = startSeed;
            string abortErr = null;
            SeedSearchResult matched = null;
            Dictionary<int, MapPreviewData> matchedMaps = new Dictionary<int, MapPreviewData>();


            Dictionary<int, List<PoiFilterRule>> grouped = new Dictionary<int, List<PoiFilterRule>>();
            for (int i = 0; i < poiRulesSnap.Count; i++)
            {
                BoundPoiRule b = poiRulesSnap[i];
                List<PoiFilterRule> rl;
                if (!grouped.TryGetValue(b.PlacementIndex, out rl))
                {
                    rl = new List<PoiFilterRule>();
                    grouped[b.PlacementIndex] = rl;
                }
                rl.Add(b.Rule);
            }

            List<KeyValuePair<int, List<PoiFilterRule>>> orderedGroups =
                new List<KeyValuePair<int, List<PoiFilterRule>>>(grouped);
            orderedGroups.Sort((a, b) =>
            {
                int c = b.Value.Count.CompareTo(a.Value.Count);
                if (c != 0) return c;
                int aStart = (a.Key == cluster.StartWorldIndex) ? 0 : 1;
                int bStart = (b.Key == cluster.StartWorldIndex) ? 0 : 1;
                c = aStart.CompareTo(bStart);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });

            string sArg = string.IsNullOrEmpty(_storyCode) ? "" : ("CODE:" + _storyCode);
            string storyCodeSnap = _storyCode ?? "";
            string hStoryIds = _storyIdsCsv ?? "";
            string mixingSnap = _mixingCode ?? "";
            bool hasCustomMixing = !string.IsNullOrEmpty(mixingSnap) && !string.Equals(mixingSnap.Trim(), "0", StringComparison.Ordinal);
            bool canHeadless = !string.IsNullOrEmpty(_gameDir) && GameInstallationDetector.IsValidGameDir(_gameDir);
            int workersCfg = TrainerSettings.ParallelWorkers;
            BridgePreviewState stInit = _getBridge != null ? _getBridge() : null;
            bool useWorkerPool = !hasCustomMixing && canHeadless && (workersCfg != 0 || (stInit != null && stInit.InSave));

            List<int> partialIndexPassSeeds;
            HashSet<int> skipIndexedPrimarySeeds;
            CommunitySeedDb.CollectIndexAcceleration(
                catalog, cluster, list, hasTraitConstraint, orderedGroups, dlcCsv, mixingSnap, 1,
                out partialIndexPassSeeds, out skipIndexedPrimarySeeds);

            Action<string> onRecordBatchFile = (recFile) =>
            {
                CommunitySeedDb.RecordScreenedBatchFile(recFile, cluster, dlcCsv, storyCodeSnap, mixingSnap, tplCoord, catalog);
            };
            Action<MapPreviewData> onRecordMap = (md) =>
            {
                CommunitySeedDb.RecordScreenedMap(md, cluster, dlcCsv, storyCodeSnap, mixingSnap, tplCoord, catalog, false);
            };

            CandidateSeedProducer producer = null;
            WorldGenWorkerPool wpool = null;
            try
            {
                if (hasTraitConstraint && cluster.FixedCoordinate <= 0)
                    producer = new CandidateSeedProducer(catalog, cluster, list, startSeed, ctrl);

                if (useWorkerPool)
                {
                    int nWorkers = cluster.FixedCoordinate > 0 ? 1 : Math.Max(1, TrainerSettings.EffectiveWorkerCount);
                    string catPath = WorldGenWorkerPool.PrepareCatalogFile(stInit);
                    WorldGenWorkerPool poolTmp = new WorldGenWorkerPool(_gameDir, dlcCsv, nWorkers, catPath);
                    string pInitErr;
                    if (!poolTmp.Initialize(out pInitErr))
                    {
                        poolTmp.Dispose();
                        if (stInit != null && stInit.Bridge != null && !stInit.InSave)
                            useWorkerPool = false;
                        else
                            abortErr = pInitErr ?? "多进程初始化失败";
                    }
                    else
                    {
                        wpool = poolTmp;
                    }
                }

                if (abortErr == null && useWorkerPool && wpool != null)
                {
                    int activeWorkers = wpool.ActiveWorkerCount;
                    KeyValuePair<int, List<PoiFilterRule>> primaryGroup = orderedGroups[0];
                    long lastUiTick = 0;

                    if (cluster.FixedCoordinate > 0)
                    {
                        int candidateSeed = cluster.FixedCoordinate;
                        totalTraitTested = 1;
                        bool allOk = true;
                        Dictionary<int, MapPreviewData> curMaps = new Dictionary<int, MapPreviewData>();
                        for (int gi = 0; gi < orderedGroups.Count && !ctrl.Stop; gi++)
                        {
                            KeyValuePair<int, List<PoiFilterRule>> kv = orderedGroups[gi];
                            string pErr;
                            MapPreviewData md = wpool.RunPreviewCommand(
                                cluster.FilePath, candidateSeed, kv.Key, dlcCsv, hStoryIds, false, gi, out pErr);
                            mapsTested++;
                            if (md == null)
                            {
                                abortErr = IsBadSeedPreviewError(pErr)
                                    ? (cluster.DisplayName + " 为固定种子，且无法生成该世界：" + pErr)
                                    : (pErr ?? "地图生成失败");
                                allOk = false;
                                break;
                            }
                            curMaps[kv.Key] = md;
                            onRecordMap(md);
                            if (!md.MatchesAllRules(kv.Value)) { allOk = false; break; }
                        }
                        if (allOk && !ctrl.Stop && abortErr == null)
                        {
                            matchedMaps = curMaps;
                            matched = new SeedSearchResult
                            {
                                Seed = candidateSeed,
                                Coordinate = SeedSearcher.FormatCoordinate(cluster.CoordinatePrefix, candidateSeed, tplCoord),
                                SeedsTested = totalTraitTested,
                                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                                PlanetTraits = catalog.EvaluateClusterSeed(cluster, candidateSeed)
                            };
                        }
                    }
                    else
                    {
                        int partialIdx = 0;
                        while (!ctrl.Stop && abortErr == null && matched == null)
                        {
                            int foundPrimary = 0, chkDelta = 0, skpDelta = 0;
                            if (partialIndexPassSeeds != null && partialIdx < partialIndexPassSeeds.Count)
                            {
                                foundPrimary = partialIndexPassSeeds[partialIdx++];
                            }
                            else
                            {
                                string sErr;
                                int baseCheckedBefore = mapsTested;
                                int baseSkippedBefore = badSeedsSkipped;

                                bool okPrimary = wpool.ScreenParallel(
                                    producer,
                                    hasTraitConstraint,
                                    cluster.FilePath,
                                    primaryGroup.Key,
                                    dlcCsv,
                                    hStoryIds,
                                    primaryGroup.Value,
                                    curSeed,
                                    ctrl,
                                    (tTested, curChk, curSkp, curSampleSeed) =>
                                    {
                                        long nowTick = Environment.TickCount & 0x7FFFFFFF;
                                        if (nowTick - lastUiTick < 120) return;
                                        lastUiTick = nowTick;
                                        int cumMaps = baseCheckedBefore + baseSkippedBefore + curChk + curSkp;
                                        int cumSkip = baseSkippedBefore + curSkp;
                                        SetUi(() =>
                                        {
                                            if (_searchCtrl == ctrl && !ctrl.Stop)
                                            {
                                                double sec = sw.Elapsed.TotalSeconds;
                                                string rateNote = (sec > 0.25 && cumMaps > 0)
                                                    ? string.Format(" ({0:0.0}星/s)", cumMaps / sec) : "";
                                                string skipNote = cumSkip > 0 ? string.Format(" · 跳{0}", cumSkip) : "";
                                                _lblSearchStatus.Text = string.Format(
                                                    "{0}核极速筛选 | 特质 {1:N0} · 地图 {2}{3}{4} · {5:0}s",
                                                    activeWorkers, tTested, cumMaps, rateNote, skipNote, sec);
                                                _lblSearchStatus.ForeColor = C(WARN);
                                            }
                                        });
                                    },
                                    out foundPrimary,
                                    out chkDelta,
                                    out skpDelta,
                                    out sErr,
                                    skipIndexedPrimarySeeds,
                                    onRecordBatchFile,
                                    onRecordMap);

                                totalTraitTested = Interlocked.Read(ref ctrl.Tested);
                                mapsTested += chkDelta + skpDelta;
                                badSeedsSkipped += skpDelta;

                                if (ctrl.Stop) break;
                                if (!okPrimary)
                                {
                                    abortErr = sErr ?? "未在种子空间内找到满足全部条件的世界种子。";
                                    break;
                                }

                                if (!hasTraitConstraint)
                                {
                                    curSeed = foundPrimary + 1;
                                    if (curSeed > SeedSearcher.MaxSearchSeed) curSeed = 1;
                                }
                            }

                            bool allOk = true;
                            Dictionary<int, MapPreviewData> curMaps = new Dictionary<int, MapPreviewData>();
                            for (int gi = 1; gi < orderedGroups.Count && !ctrl.Stop; gi++)
                            {
                                KeyValuePair<int, List<PoiFilterRule>> kv = orderedGroups[gi];
                                string secErr;
                                MapPreviewData secMap = wpool.RunPreviewCommand(
                                    cluster.FilePath, foundPrimary, kv.Key, dlcCsv, hStoryIds, true, gi, out secErr);
                                mapsTested++;
                                if (secMap == null)
                                {
                                    if (IsBadSeedPreviewError(secErr))
                                    {
                                        badSeedsSkipped++;
                                        consecutiveGenFails++;
                                        if (consecutiveGenFails >= 25)
                                            abortErr = "连续 25 个候选种子均因特质布局冲突无法生成地形，请减少部分互斥或高占比特质后重试。";
                                        allOk = false;
                                        break;
                                    }
                                    abortErr = secErr ?? "副星地图生成失败";
                                    allOk = false;
                                    break;
                                }
                                consecutiveGenFails = 0;
                                curMaps[kv.Key] = secMap;
                                onRecordMap(secMap);
                                if (!secMap.MatchesAllRules(kv.Value))
                                {
                                    allOk = false;
                                    break;
                                }
                            }

                            if (abortErr != null || ctrl.Stop) break;
                            if (!allOk) continue;

                            SetUi(() =>
                            {
                                if (_searchCtrl == ctrl && !ctrl.Stop)
                                {
                                    _lblSearchStatus.Text = string.Format("命中候选 #{0}，正在渲染完整预览地形…", foundPrimary);
                                    _lblSearchStatus.ForeColor = C(ACC);
                                }
                            });

                            BridgePreviewState stFull = _getBridge != null ? _getBridge() : null;
                            bool canBridgeFull = (stFull != null && stFull.Bridge != null && !stFull.InSave && !hasCustomMixing);

                            for (int gi = 0; gi < orderedGroups.Count && !ctrl.Stop; gi++)
                            {
                                KeyValuePair<int, List<PoiFilterRule>> kv = orderedGroups[gi];
                                MapPreviewData curMd;
                                if (curMaps.TryGetValue(kv.Key, out curMd) && curMd != null && !curMd.IsPoiOnly)
                                    continue;

                                string fullErr;
                                MapPreviewData fullData = canBridgeFull
                                    ? stFull.Bridge.GenerateMapPreview(cluster.FilePath, foundPrimary, kv.Key, dlcCsv, sArg, mixingSnap, out fullErr)
                                    : wpool.RunPreviewCommand(cluster.FilePath, foundPrimary, kv.Key, dlcCsv, hStoryIds, false, gi, out fullErr);
                                if (fullData == null)
                                {
                                    if (IsBadSeedPreviewError(fullErr))
                                    {
                                        badSeedsSkipped++;
                                        allOk = false;
                                        break;
                                    }
                                    abortErr = fullErr ?? "完整地图验证失败";
                                    allOk = false;
                                    break;
                                }
                                curMaps[kv.Key] = fullData;
                                onRecordMap(fullData);
                                if (!fullData.MatchesAllRules(kv.Value))
                                {
                                    allOk = false;
                                    break;
                                }
                            }

                            if (abortErr != null || ctrl.Stop) break;
                            if (allOk)
                            {
                                matchedMaps = curMaps;
                                matched = new SeedSearchResult
                                {
                                    Seed = foundPrimary,
                                    Coordinate = SeedSearcher.FormatCoordinate(cluster.CoordinatePrefix, foundPrimary, tplCoord),
                                    SeedsTested = totalTraitTested,
                                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                                    PlanetTraits = catalog.EvaluateClusterSeed(cluster, foundPrimary)
                                };
                                break;
                            }
                        }
                    }
                }
                else if (abortErr == null)
                {

                    bool batchFallback = false;
                    int[] candBuf = new int[4];
                    KeyValuePair<int, List<PoiFilterRule>> primaryGroup = orderedGroups[0];

                    while (!ctrl.Stop)
                    {
                        int batchCount = 0;
                        if (producer != null)
                        {
                            while (!ctrl.Stop)
                            {
                                batchCount = producer.TakeBatch(candBuf, candBuf.Length, 150);
                                if (batchCount > 0 || producer.IsExhausted) break;
                                long tTested = Interlocked.Read(ref ctrl.Tested);
                                SetUi(() =>
                                {
                                    if (_searchCtrl == ctrl && !ctrl.Stop)
                                    {
                                        double sec = sw.Elapsed.TotalSeconds;
                                        string rateNote = (sec > 0.25 && mapsTested > 0)
                                            ? string.Format(" ({0:0.0}星/s)", mapsTested / sec) : "";
                                        string skipNote = badSeedsSkipped > 0
                                            ? string.Format(" · 跳{0}", badSeedsSkipped) : "";
                                        _lblSearchStatus.Text = string.Format(
                                            "特质筛选中… 已检 {0:N0} · 地图 {1}{2}{3} · {4:0}s",
                                            tTested, mapsTested, rateNote, skipNote, sec);
                                        _lblSearchStatus.ForeColor = C(WARN);
                                    }
                                });
                            }
                            totalTraitTested = Interlocked.Read(ref ctrl.Tested);
                            if (ctrl.Stop) break;
                            if (batchCount == 0)
                            {
                                abortErr = "未在种子空间内找到满足特质条件的候选种子。";
                                break;
                            }
                        }
                        else
                        {
                            int nSeq = cluster.FixedCoordinate > 0 ? 1 : candBuf.Length;
                            for (int si = 0; si < nSeq; si++)
                            {
                                int s = (cluster.FixedCoordinate > 0) ? cluster.FixedCoordinate : curSeed;
                                candBuf[batchCount++] = s;
                                totalTraitTested++;
                                if (cluster.FixedCoordinate > 0) break;
                                curSeed = s + 1;
                                if (curSeed > SeedSearcher.MaxSearchSeed) curSeed = 1;
                            }
                        }

                        if (ctrl.Stop) break;

                        SetUi(() =>
                        {
                            if (_searchCtrl == ctrl && !ctrl.Stop)
                            {
                                double sec = sw.Elapsed.TotalSeconds;
                                string rateNote = (sec > 0.25 && mapsTested > 0)
                                    ? string.Format(" ({0:0.0}星/s)", mapsTested / sec) : "";
                                string skipNote = badSeedsSkipped > 0
                                    ? string.Format(" · 跳{0}", badSeedsSkipped) : "";
                                _lblSearchStatus.Text = string.Format(
                                    "游戏内筛选 #{0} | 特质 {1:N0} · 地图 {2}{3}{4} · {5:0}s",
                                    candBuf[0], totalTraitTested, mapsTested, rateNote, skipNote, sec);
                                _lblSearchStatus.ForeColor = C(WARN);
                            }
                        });

                        BridgePreviewState st = _getBridge != null ? _getBridge() : null;
                        if (st == null || st.Bridge == null || st.InSave)
                        {
                            abortErr = (st != null && st.InSave)
                                ? "已进入游戏存档，为避免闪退已停止游戏内筛选（请在下方切换「并行进程」为自动推荐）。"
                                : "游戏连接已断开，高级筛选停止。";
                            break;
                        }

                        int checkCount = 0;
                        if (!batchFallback && cluster.FixedCoordinate <= 0)
                        {
                            string bErr;
                            Bridge.BatchPoiScreenResult bres = st.Bridge.ScreenPoiBatch(
                                cluster.FilePath, primaryGroup.Key, dlcCsv, sArg, mixingSnap,
                                primaryGroup.Value, candBuf, batchCount, out bErr, onRecordBatchFile);

                            if (bres.FallbackNeeded)
                            {
                                batchFallback = true;
                            }
                            else if (bErr != null && bres.CheckedCount == 0 && bres.SkippedBadSeeds == 0)
                            {
                                abortErr = bErr;
                                break;
                            }
                            else
                            {
                                mapsTested += bres.CheckedCount + bres.SkippedBadSeeds;
                                badSeedsSkipped += bres.SkippedBadSeeds;
                                if (bres.CheckedCount > 0)
                                    consecutiveGenFails = 0;
                                else
                                {
                                    consecutiveGenFails += bres.SkippedBadSeeds;
                                    if (consecutiveGenFails >= 25)
                                    {
                                        abortErr = "连续 25 个候选种子均因特质布局冲突无法生成地形，请减少部分互斥或高占比特质后重试。";
                                        break;
                                    }
                                }
                                if (bres.MatchedSeed <= 0)
                                    continue;

                                candBuf[0] = bres.MatchedSeed;
                                checkCount = 1;
                            }
                        }

                        if (checkCount == 0) checkCount = batchCount;

                        for (int bi = 0; bi < checkCount && !ctrl.Stop && abortErr == null; bi++)
                        {
                            int candidateSeed = candBuf[bi];
                            bool allOk = true;
                            Dictionary<int, MapPreviewData> curMaps = new Dictionary<int, MapPreviewData>();

                            int startGi = (!batchFallback && checkCount == 1 && orderedGroups.Count > 1) ? 1 : 0;
                            for (int gi = startGi; gi < orderedGroups.Count; gi++)
                            {
                                KeyValuePair<int, List<PoiFilterRule>> kv = orderedGroups[gi];
                                if (ctrl.Stop) { allOk = false; break; }
                                BridgePreviewState stLoop = _getBridge != null ? _getBridge() : null;
                                if (stLoop == null || stLoop.Bridge == null || stLoop.InSave)
                                {
                                    abortErr = "已进入存档或游戏已断开，停止游戏内筛选。";
                                    allOk = false;
                                    break;
                                }
                                string err;
                                MapPreviewData mapData = stLoop.Bridge.GenerateFastPoiPreview(
                                    cluster.FilePath, candidateSeed, kv.Key, dlcCsv, sArg, mixingSnap, out err);
                                mapsTested++;
                                if (mapData == null)
                                {
                                    if (IsBadSeedPreviewError(err))
                                    {
                                        if (cluster.FixedCoordinate > 0)
                                        {
                                            abortErr = cluster.DisplayName + " 为固定种子，且游戏无法生成该世界：" + err;
                                            allOk = false;
                                            break;
                                        }
                                        badSeedsSkipped++;
                                        consecutiveGenFails++;
                                        if (consecutiveGenFails >= 25)
                                        {
                                            abortErr = "连续 25 个候选种子均因特质布局冲突无法生成地形，请减少部分互斥或高占比特质后重试。";
                                            allOk = false;
                                            break;
                                        }
                                        allOk = false;
                                        break;
                                    }
                                    abortErr = err ?? "地图生成失败";
                                    allOk = false;
                                    break;
                                }
                                consecutiveGenFails = 0;
                                curMaps[kv.Key] = mapData;
                                onRecordMap(mapData);
                                if (!mapData.MatchesAllRules(kv.Value))
                                {
                                    allOk = false;
                                    break;
                                }
                            }

                            if (abortErr != null || ctrl.Stop) break;

                            if (allOk)
                            {
                                for (int gi = 0; gi < orderedGroups.Count; gi++)
                                {
                                    if (ctrl.Stop) { allOk = false; break; }
                                    KeyValuePair<int, List<PoiFilterRule>> kv = orderedGroups[gi];
                                    MapPreviewData curMd;
                                    if (curMaps.TryGetValue(kv.Key, out curMd) && curMd != null && !curMd.IsPoiOnly)
                                        continue;
                                    BridgePreviewState stFull = _getBridge != null ? _getBridge() : null;
                                    if (stFull == null || stFull.Bridge == null || stFull.InSave)
                                    {
                                        abortErr = "已进入存档或游戏已断开，无法在主菜单生成完整预览。";
                                        allOk = false;
                                        break;
                                    }
                                    string fullErr;
                                    MapPreviewData fullData = stFull.Bridge.GenerateMapPreview(
                                        cluster.FilePath, candidateSeed, kv.Key, dlcCsv, sArg, mixingSnap, out fullErr);
                                    if (fullData == null)
                                    {
                                        if (IsBadSeedPreviewError(fullErr))
                                        {
                                            if (cluster.FixedCoordinate > 0)
                                            {
                                                abortErr = cluster.DisplayName + " 为固定种子，且游戏无法生成该世界：" + fullErr;
                                                allOk = false;
                                                break;
                                            }
                                            badSeedsSkipped++;
                                            consecutiveGenFails++;
                                            allOk = false;
                                            break;
                                        }
                                        abortErr = fullErr ?? "完整地图验证失败";
                                        allOk = false;
                                        break;
                                    }
                                    curMaps[kv.Key] = fullData;
                                    onRecordMap(fullData);
                                    if (!fullData.MatchesAllRules(kv.Value))
                                    {
                                        allOk = false;
                                        break;
                                    }
                                }
                            }

                            if (abortErr != null || ctrl.Stop) break;

                            if (allOk)
                            {
                                matchedMaps = curMaps;
                                matched = new SeedSearchResult
                                {
                                    Seed = candidateSeed,
                                    Coordinate = SeedSearcher.FormatCoordinate(cluster.CoordinatePrefix, candidateSeed, tplCoord),
                                    SeedsTested = totalTraitTested,
                                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                                    PlanetTraits = catalog.EvaluateClusterSeed(cluster, candidateSeed)
                                };
                                break;
                            }
                        }

                        if (abortErr != null || matched != null || ctrl.Stop) break;
                        if (cluster.FixedCoordinate > 0)
                        {
                            abortErr = cluster.DisplayName + " 为固定种子，无法刷出全部指定间歇泉/火山条件。";
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                abortErr = "高级筛选异常：" + ex.Message;
            }
            finally
            {
                if (wpool != null)
                {
                    try { wpool.Dispose(); } catch { }
                }
                if (producer != null)
                {
                    totalTraitTested = Interlocked.Read(ref ctrl.Tested);
                    producer.Dispose();
                }
                CommunitySeedDb.FlushPendingToDisk();
            }

            ctrl.Tested = totalTraitTested;
            string mapNote = null;
            if (abortErr == null && matched != null)
            {
                mapNote = badSeedsSkipped > 0
                    ? string.Format("地图已验 {0} 个候选（跳过 {1} 个游戏坏种子），", mapsTested, badSeedsSkipped)
                    : string.Format("地图已验 {0} 个候选，", mapsTested);
            }
            FinishSearchOnUi(cluster, ctrl, matched, matchedMaps, abortErr, mapNote);
        }

        void FinishSearchOnUi(
            ClusterDef cluster,
            SearchControl ctrl,
            SeedSearchResult res,
            Dictionary<int, MapPreviewData> cachedMaps,
            string abortErr,
            string mapNote = null)
        {
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
                if (abortErr != null)
                {
                    _lblSearchStatus.Text = "⚠ " + abortErr.Replace('\r', ' ').Replace('\n', ' ');
                    _lblSearchStatus.ForeColor = C(ERR);
                    return;
                }
                if (res == null)
                {
                    _lblSearchStatus.Text = "未在完整种子空间内找到满足全部条件的组合，请放宽部分条件。";
                    _lblSearchStatus.ForeColor = C(ERR);
                    return;
                }

                _lastResult = res;
                _previewCache.Clear();
                if (cachedMaps != null && cachedMaps.Count > 0)
                {
                    foreach (KeyValuePair<int, MapPreviewData> kv in cachedMaps)
                    {
                        if (kv.Value != null && !kv.Value.IsPoiOnly)
                        {
                            _previewCacheSeed = res.Seed;
                            _previewCache[kv.Key] = kv.Value;
                        }
                    }
                }

                _btnNext.Enabled = cluster.FixedCoordinate <= 0;
                _btnCopyCoord.Enabled = true;
                _btnCopySeed.Enabled = true;
                _btnPreviewMap.Enabled = true;
                _btnFav.Enabled = true;
                RefreshUploadPendingBadge();

                _suppressEvents = true;
                _txtCoord.Text = res.Coordinate;
                _suppressEvents = false;
                TrainerSettings.SetLastSeedCoord(res.Coordinate);
                _lblSeedOnly.Text = "纯种子：" + res.Seed.ToString(CultureInfo.InvariantCulture);
                _lblSearchStatus.Text = string.Format("已找到符合条件的世界种子！{0}耗时 {1:0.00} 秒（特质已检 {2:N0} 个）",
                    mapNote ?? "", res.ElapsedMs / 1000.0, res.SeedsTested);
                _lblSearchStatus.ForeColor = C(ACC);

                string txt = BuildPreviewText(cluster, res);
                if (cachedMaps != null && cachedMaps.Count > 0)
                    txt += AppendMapSummaryText(cachedMaps);
                _txtPreview.Text = txt;

                SeedStore.AddHistory(BuildCurrentSeedRecord(cluster, res, cachedMaps));
                UpdateFavButton();
            });
        }

        static string AppendMapSummaryText(Dictionary<int, MapPreviewData> maps)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, MapPreviewData> kv in maps)
            {
                MapPreviewData md = kv.Value;
                Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.CurrentCulture);
                for (int i = 0; i < md.Pois.Count; i++)
                {
                    MapPoiItem p = md.Pois[i];
                    if (p.Category == "start") continue;
                    int old;
                    counts.TryGetValue(p.DisplayName, out old);
                    counts[p.DisplayName] = old + 1;
                }
                List<string> items = new List<string>();
                foreach (KeyValuePair<string, int> c in counts)
                    items.Add(c.Value > 1 ? (c.Key + "×" + c.Value) : c.Key);
                sb.AppendFormat("\r\n[{0} 间歇泉/遗迹] {1}", md.WorldName, string.Join("、", items.ToArray()));
            }
            return sb.ToString();
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
            int seed;
            if (!TryGetActiveSeed(out seed)) return;
            string text = full
                ? SeedSearcher.FormatCoordinate(_curCluster != null ? _curCluster.CoordinatePrefix : "", seed, _txtTemplateCoord.Text)
                : seed.ToString(CultureInfo.InvariantCulture);
            try
            {
                if (full)
                {
                    _suppressEvents = true;
                    _txtCoord.Text = text;
                    _suppressEvents = false;
                    TrainerSettings.SetLastSeedCoord(text);
                }
                Clipboard.SetText(text);
                _lblSearchStatus.Text = (full ? "已复制完整坐标到剪贴板：" : "已复制纯种子到剪贴板：") + text;
                _lblSearchStatus.ForeColor = C(ACC);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void OpenSettingsDialog()
        {
            if (_openSettings != null)
            {
                _openSettings();
                return;
            }
            using (SettingsForm f = new SettingsForm())
                f.ShowDialog(this);
        }

        void SyncGameConfig()
        {
            BridgePreviewState st = _getBridge != null ? _getBridge() : null;
            if (st == null || st.Bridge == null)
            {
                _lblSearchStatus.Text = "⚠ 请先启动《缺氧》游戏并停留在主菜单，再点击「读取游戏设置」。";
                _lblSearchStatus.ForeColor = C(WARN);
                return;
            }
            if (st.InSave)
            {
                _lblSearchStatus.Text = "⚠ 当前处于存档中，请在主菜单新开局界面配置故事特质/混搭后读取。";
                _lblSearchStatus.ForeColor = C(WARN);
                return;
            }
            _btnSyncCfg.Enabled = false;
            _lblSearchStatus.Text = "正在读取游戏故事特质与 DLC 混搭设置…";
            _lblSearchStatus.ForeColor = C(WARN);

            Thread t = new Thread(() =>
            {
                string err;
                string resp = st.Bridge.RunMenuCmd("GETGAMECFG", out err);
                SetUi(() =>
                {
                    _btnSyncCfg.Enabled = true;
                    if (string.IsNullOrEmpty(resp))
                    {
                        _lblSearchStatus.Text = "⚠ 读取游戏设置失败：" + (err ?? "无响应");
                        _lblSearchStatus.ForeColor = C(ERR);
                        return;
                    }
                    string oc = "", sc = "", mc = "", stCsv = "";
                    string[] ps = resp.Split('|');
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (ps[i].StartsWith("otherCode=", StringComparison.Ordinal)) oc = ps[i].Substring("otherCode=".Length);
                        else if (ps[i].StartsWith("storyCode=", StringComparison.Ordinal)) sc = ps[i].Substring("storyCode=".Length);
                        else if (ps[i].StartsWith("mixingCode=", StringComparison.Ordinal)) mc = ps[i].Substring("mixingCode=".Length);
                        else if (ps[i].StartsWith("stories=", StringComparison.Ordinal)) stCsv = ps[i].Substring("stories=".Length);
                    }
                    _otherCode = oc;
                    _storyCode = sc;
                    _mixingCode = mc;
                    _storyIdsCsv = stCsv;

                    string curTpl = _txtTemplateCoord != null ? _txtTemplateCoord.Text.Trim() : "";
                    if (_txtTemplateCoord != null && (string.IsNullOrEmpty(curTpl) || _templateAutoSynced))
                    {
                        string oNorm = string.IsNullOrEmpty(oc) ? "0" : oc;
                        string sNorm = string.IsNullOrEmpty(sc) ? "0" : sc;
                        string mNorm = string.IsNullOrEmpty(mc) ? "0" : mc;
                        string nextSuffix = (oNorm == "0" && sNorm == "0" && mNorm == "0")
                            ? ""
                            : (oNorm + "-" + sNorm + "-" + mNorm);
                        _suppressEvents = true;
                        _txtTemplateCoord.Text = nextSuffix;
                        _suppressEvents = false;
                        _templateAutoSynced = true;
                    }

                    _lblSearchStatus.Text = string.Format("已同步游戏设置：难度={0} · 故事={1} · 混搭={2}（预览与高级筛选将自动应用）",
                        string.IsNullOrEmpty(oc) ? "0" : oc,
                        string.IsNullOrEmpty(sc) ? "0" : sc,
                        string.IsNullOrEmpty(mc) ? "0" : mc);
                    _lblSearchStatus.ForeColor = C(ACC);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        SeedRecord BuildCurrentSeedRecord(ClusterDef cluster, SeedSearchResult res, Dictionary<int, MapPreviewData> maps, int seedOverride = 0)
        {
            int useSeed = (res != null && res.Seed > 0) ? res.Seed : seedOverride;
            Dictionary<int, List<WorldTraitDef>> tmap = (res != null && res.PlanetTraits != null) ? res.PlanetTraits : null;
            if (tmap == null && cluster != null && _catalog != null && useSeed > 0)
                tmap = _catalog.EvaluateClusterSeed(cluster, useSeed);

            List<string> ts = new List<string>();
            if (tmap != null)
            {
                foreach (KeyValuePair<int, List<WorldTraitDef>> kv in tmap)
                {
                    if (kv.Value == null) continue;
                    for (int i = 0; i < kv.Value.Count; i++)
                        if (!ts.Contains(kv.Value[i].DisplayName)) ts.Add(kv.Value[i].DisplayName);
                }
            }
            string poiSum = "";
            if (maps != null && maps.Count > 0)
            {
                List<string> ps = new List<string>();
                foreach (MapPreviewData md in maps.Values)
                {
                    int c = 0;
                    for (int i = 0; i < md.Pois.Count; i++) if (md.Pois[i].Category != "start") c++;
                    ps.Add(md.WorldName + "(" + c + "POI)");
                }
                poiSum = string.Join(", ", ps.ToArray());
            }
            return new SeedRecord
            {
                Seed = useSeed,
                Coordinate = (res != null && !string.IsNullOrEmpty(res.Coordinate)) ? res.Coordinate : _txtCoord.Text.Trim(),
                ClusterPath = cluster != null ? cluster.FilePath : "",
                ClusterName = cluster != null ? cluster.DisplayName : "",
                StoryCode = _storyCode,
                MixingCode = _mixingCode,
                TraitSummary = string.Join("、", ts.ToArray()),
                PoiSummary = poiSum
            };
        }

        void UpdateFavButton()
        {
            if (_btnFav == null) return;
            int seed;
            if (!TryGetActiveSeed(out seed))
            {
                _btnFav.Enabled = false;
                _btnFav.Text = "★ 收藏";
                return;
            }
            _btnFav.Enabled = _curCluster != null;
            string coord = string.IsNullOrEmpty(_txtCoord.Text.Trim()) && _curCluster != null
                ? SeedSearcher.FormatCoordinate(_curCluster.CoordinatePrefix, seed, _txtTemplateCoord.Text)
                : _txtCoord.Text.Trim();
            _btnFav.Text = SeedStore.IsFavorite(coord, seed) ? "☆ 已收藏" : "★ 收藏";
        }

        void ToggleFavoriteCurrent()
        {
            int seed;
            if (!TryGetActiveSeed(out seed) || _curCluster == null) return;
            string coord = SeedSearcher.FormatCoordinate(_curCluster.CoordinatePrefix, seed,
                string.IsNullOrEmpty(_txtTemplateCoord.Text) ? _txtCoord.Text : _txtTemplateCoord.Text);

            if (SeedStore.IsFavorite(coord, seed))
            {
                SeedStore.RemoveFavorite(coord, seed);
                UpdateFavButton();
                _lblSearchStatus.Text = "已从收藏夹移除：" + coord;
                _lblSearchStatus.ForeColor = C(SUB);
                return;
            }

            Dictionary<int, MapPreviewData> mapsSnap = (_previewCacheSeed == seed && _previewCache.Count > 0)
                ? _previewCache : null;
            SeedRecord rec = BuildCurrentSeedRecord(_curCluster, _lastResult, mapsSnap, seed);
            rec.Seed = seed;
            rec.Coordinate = coord;
            SeedStore.UpsertFavorite(rec, mapsSnap);
            UpdateFavButton();
            int savedCnt = SeedStore.GetSavedPreviewCount(coord, seed);
            _lblSearchStatus.Text = savedCnt > 0
                ? string.Format("已收藏完整种子信息（含 {0} 颗星球预览）：{1}", savedCnt, coord)
                : ("已收藏完整种子信息（打开「预览地图」后会自动保存地图快照）：" + coord);
            _lblSearchStatus.ForeColor = C(ACC);
        }

        void OpenHistoryDialog()
        {
            if (_historyForm != null && !_historyForm.IsDisposed)
            {
                _historyForm.Activate();
                return;
            }
            _historyForm = new SeedHistoryForm(ApplySavedSeedRecord);
            _historyForm.Show(this);
        }

        void ApplySavedSeedRecord(SeedRecord rec, bool openPreview)
        {
            if (rec == null) return;
            if (!string.IsNullOrEmpty(rec.ClusterPath) && _cboCluster != null)
            {
                for (int i = 0; i < _cboCluster.Items.Count; i++)
                {
                    ClusterDef cd = _cboCluster.Items[i] as ClusterDef;
                    if (cd != null && string.Equals(cd.FilePath, rec.ClusterPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _cboCluster.SelectedIndex = i;
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(rec.StoryCode)) _storyCode = rec.StoryCode;
            if (!string.IsNullOrEmpty(rec.MixingCode)) _mixingCode = rec.MixingCode;

            _previewCache.Clear();
            _previewCacheSeed = 0;
            Dictionary<int, MapPreviewData> savedMaps = SeedStore.LoadFavoritePreviews(rec.Coordinate, rec.Seed);
            if (savedMaps != null && savedMaps.Count > 0)
            {
                _previewCacheSeed = rec.Seed;
                foreach (KeyValuePair<int, MapPreviewData> kv in savedMaps)
                    _previewCache[kv.Key] = kv.Value;
            }

            _suppressEvents = true;
            _txtCoord.Text = rec.Coordinate;
            _suppressEvents = false;
            OnCoordTextChanged();
            _txtPreview.Text = string.IsNullOrEmpty(rec.TraitSummary) ? rec.Coordinate : (rec.TraitSummary + "\r\n" + rec.PoiSummary);
            _lblSearchStatus.Text = savedMaps != null && savedMaps.Count > 0
                ? string.Format("已载入收藏记录（含 {0} 颗离线预览）：{1}", savedMaps.Count, rec.Coordinate)
                : ("已载入记录：" + rec.Coordinate);
            _lblSearchStatus.ForeColor = C(ACC);
            if (openPreview) OpenMapPreview();
        }

        void TriggerCommunityAutoPull()
        {
            if (_btnCommunity != null) _btnCommunity.Text = "社区库";
            CommunitySeedDb.TryAutoPullBackground((res) =>
            {
                if (res != null && res.Success && res.UpdatedClusters > 0)
                {
                    SetUi(() =>
                    {
                        if (_lblSearchStatus != null && string.IsNullOrEmpty(_txtCoord.Text))
                        {
                            _lblSearchStatus.Text = string.Format("社区种子库已自动增量同步（共 {0} 条种子）", res.TotalSeeds);
                            _lblSearchStatus.ForeColor = C(ACC);
                        }
                    });
                }
            });
        }

        void OpenCommunityDbDialog()
        {
            if (_communityForm != null && !_communityForm.IsDisposed)
            {
                _communityForm.Activate();
                return;
            }
            List<PlanetConstraint> cons = new List<PlanetConstraint>(_constraints.Values);
            bool hasTrait = false;
            for (int i = 0; i < cons.Count; i++)
            {
                if (cons[i].RequiredMask != 0 || cons[i].ForbiddenMask != 0 || cons[i].RequireNoTraits)
                {
                    hasTrait = true;
                    break;
                }
            }

            Dictionary<int, List<PoiFilterRule>> grouped = new Dictionary<int, List<PoiFilterRule>>();
            for (int i = 0; i < _poiRules.Count; i++)
            {
                BoundPoiRule br = _poiRules[i];
                List<PoiFilterRule> rl;
                if (!grouped.TryGetValue(br.PlacementIndex, out rl))
                {
                    rl = new List<PoiFilterRule>();
                    grouped[br.PlacementIndex] = rl;
                }
                rl.Add(br.Rule);
            }

            Func<SeedRecord> getCurRec = () =>
            {
                int activeSeed;
                if (_curCluster == null || !TryGetActiveSeed(out activeSeed)) return null;
                Dictionary<int, MapPreviewData> mapsSnap = (_previewCacheSeed == activeSeed && _previewCache.Count > 0)
                    ? _previewCache : null;
                SeedRecord sr = BuildCurrentSeedRecord(_curCluster, _lastResult, mapsSnap, activeSeed);
                sr.Seed = activeSeed;
                string tpl = string.IsNullOrEmpty(_txtTemplateCoord.Text) ? _txtCoord.Text : _txtTemplateCoord.Text;
                sr.Coordinate = SeedSearcher.FormatCoordinate(_curCluster.CoordinatePrefix, activeSeed, tpl);
                return sr;
            };

            Func<Dictionary<int, MapPreviewData>> getCurMaps = () =>
            {
                int activeSeed;
                if (!TryGetActiveSeed(out activeSeed)) return null;
                return (_previewCacheSeed == activeSeed && _previewCache.Count > 0) ? _previewCache : null;
            };

            _communityForm = new CommunitySeedForm(
                _catalog,
                _curCluster,
                cons,
                hasTrait,
                grouped,
                ApplySavedSeedRecord,
                getCurRec,
                getCurMaps,
                _getBridge,
                _gameDir,
                GetActiveDlcCsv(),
                _storyIdsCsv,
                _mixingCode);
            _communityForm.Show(this);
        }

        void RefreshUploadPendingBadge()
        {
            if (_btnSubmitSeed == null) return;
            int pend = CommunitySeedDb.GetPendingUploadCount();
            _btnSubmitSeed.Text = "上传记录(" + pend.ToString(CultureInfo.InvariantCulture) + ")";
            _btnSubmitSeed.Enabled = true;
            _btnSubmitSeed.ForeColor = pend > 0 ? C(WARN) : C(TXT);
        }

        void OpenSubmitSeedDialog()
        {
            CommunitySeedDb.FlushPendingToDisk();
            using (BatchUploadSeedsDialog dlg = new BatchUploadSeedsDialog())
            {
                dlg.ShowDialog(this);
            }
            RefreshUploadPendingBadge();
        }

        void OnGlobalAdvancedModeChanged()
        {
            SetUi(ApplyAdvancedModeLayout);
        }

        void ApplyAdvancedModeLayout()
        {
            if (_gPoiFilter == null) return;
            bool adv = TrainerSettings.AdvancedMode;
            _gPoiFilter.Visible = adv;
            int targetH = adv ? _advClientHeight : _baseClientHeight;
            Screen scr = Screen.FromControl(this);
            int maxClientH = Math.Max(300, scr.WorkingArea.Height - 72);
            if (targetH > maxClientH) targetH = maxClientH;

            int ncW = Width - ClientSize.Width;
            int ncH = Height - ClientSize.Height;
            Size nextFull = new Size(ClientSize.Width + ncW, targetH + ncH);
            MinimumSize = nextFull;
            MaximumSize = nextFull;
            ClientSize = new Size(ClientSize.Width, targetH);
        }

        sealed class WorkerCountChoice
        {
            public int Value;
            public string Label;
            public override string ToString() { return Label; }
        }

        void PopulateWorkersCombo()
        {
            if (_cboWorkers == null) return;
            _suppressEvents = true;
            _cboWorkers.Items.Clear();
            int cur = TrainerSettings.ParallelWorkers;
            int autoCnt = Math.Max(2, Math.Min(6, Environment.ProcessorCount / 2));
            WorkerCountChoice[] choices = new WorkerCountChoice[]
            {
                new WorkerCountChoice { Value = -1, Label = string.Format("自动推荐 ({0} 进程)", autoCnt) },
                new WorkerCountChoice { Value = 1,  Label = "1 进程（低占用）" },
                new WorkerCountChoice { Value = 2,  Label = "2 进程" },
                new WorkerCountChoice { Value = 4,  Label = "4 进程" },
                new WorkerCountChoice { Value = 6,  Label = "6 进程" },
                new WorkerCountChoice { Value = 8,  Label = "8 进程" },
                new WorkerCountChoice { Value = 12, Label = "12 进程" },
                new WorkerCountChoice { Value = 0,  Label = "0（仅用游戏内筛选）" }
            };
            int sel = 0;
            for (int i = 0; i < choices.Length; i++)
            {
                _cboWorkers.Items.Add(choices[i]);
                if (choices[i].Value == cur) sel = i;
            }
            if (_cboWorkers.Items.Count > 0) _cboWorkers.SelectedIndex = sel;
            _cboWorkers.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                WorkerCountChoice wc = _cboWorkers.SelectedItem as WorkerCountChoice;
                if (wc != null) TrainerSettings.SetParallelWorkers(wc.Value);
            };
            _suppressEvents = false;
        }

        sealed class PoiPlanetChoice
        {
            public WorldPlacementDef Placement;
            public override string ToString()
            {
                string nm = Placement.World != null ? Placement.World.DisplayName : Placement.WorldId;
                return string.Format("[{0}] {1}", Placement.GroupLabel(), nm);
            }
        }

        void RefreshPoiPlanetCombo()
        {
            if (_cboPoiPlanet == null) return;
            _cboPoiPlanet.Items.Clear();
            int startSel = 0;
            for (int i = 0; i < _orderedPlacements.Count; i++)
            {
                _cboPoiPlanet.Items.Add(new PoiPlanetChoice { Placement = _orderedPlacements[i] });
                if (_curCluster != null && _orderedPlacements[i].PlacementIndex == _curCluster.StartWorldIndex)
                    startSel = i;
            }
            if (_cboPoiPlanet.Items.Count > 0) _cboPoiPlanet.SelectedIndex = startSel;
        }

        void OnPoiYieldChanged()
        {
            if (_poiMutexUpdating) return;
            if (_numPoiYield != null && _cboPoiGrade != null && _numPoiYield.Value > 0 && _cboPoiGrade.SelectedIndex != 0)
            {
                _poiMutexUpdating = true;
                _cboPoiGrade.SelectedIndex = 0;
                _poiMutexUpdating = false;
            }
            UpdatePoiYieldGradeColors();
        }

        void OnPoiGradeChanged()
        {
            if (_poiMutexUpdating) return;
            if (_cboPoiGrade != null && _numPoiYield != null && _cboPoiGrade.SelectedIndex > 0 && _numPoiYield.Value != 0)
            {
                _poiMutexUpdating = true;
                _numPoiYield.Value = 0;
                _poiMutexUpdating = false;
            }
            UpdatePoiYieldGradeColors();
        }

        void UpdatePoiYieldGradeColors()
        {
            if (_numPoiYield == null || _cboPoiGrade == null) return;
            if (_numPoiYield.Value > 0)
            {
                _numPoiYield.ForeColor = C(ACC);
                _cboPoiGrade.ForeColor = C(SUB);
            }
            else if (_cboPoiGrade.SelectedIndex > 0)
            {
                _cboPoiGrade.ForeColor = C(ACC);
                _numPoiYield.ForeColor = C(SUB);
            }
            else
            {
                _numPoiYield.ForeColor = C(TXT);
                _cboPoiGrade.ForeColor = C(TXT);
            }
        }

        void AddPoiRule()
        {
            PoiPlanetChoice pc = _cboPoiPlanet.SelectedItem as PoiPlanetChoice;
            PoiFilterCatalogEntry entry = _cboPoiType.SelectedItem as PoiFilterCatalogEntry;
            if (pc == null || entry == null) return;

            int pidx = pc.Placement.PlacementIndex;
            int cnt = (int)_numPoiCount.Value;
            bool atMost = (_cboPoiOp != null && _cboPoiOp.SelectedIndex == 1);
            int maxDist = _numPoiDist != null ? (int)_numPoiDist.Value : 0;
            float minYield = _numPoiYield != null ? (float)_numPoiYield.Value : 0f;
            byte minGrade = (_cboPoiGrade != null && _cboPoiGrade.SelectedIndex > 0) ? (byte)_cboPoiGrade.SelectedIndex : (byte)0;
            if (minGrade > 0) minYield = 0f;

            for (int i = 0; i < _poiRules.Count; i++)
            {
                PoiFilterRule r = _poiRules[i].Rule;
                if (_poiRules[i].PlacementIndex == pidx &&
                    string.Equals(r.PrefabId, entry.PrefabId, StringComparison.OrdinalIgnoreCase) &&
                    r.CompareAtMost == atMost &&
                    r.MaxDist == maxDist &&
                    Math.Abs(r.MinYieldGps - minYield) < 0.01f &&
                    r.MinGrade == minGrade)
                {
                    r.MinCount = cnt;
                    RefreshPoiRulesList();
                    return;
                }
            }

            _poiRules.Add(new BoundPoiRule
            {
                PlacementIndex = pidx,
                PlanetName = pc.Placement.World != null ? pc.Placement.World.DisplayName : pc.Placement.WorldId,
                Rule = new PoiFilterRule
                {
                    PrefabId = entry.PrefabId,
                    Category = entry.Category,
                    DisplayName = entry.DisplayName,
                    MinCount = cnt,
                    CompareAtMost = atMost,
                    MaxDist = maxDist,
                    MinYieldGps = minYield,
                    MinGrade = minGrade
                }
            });
            RefreshPoiRulesList();
        }

        void ClearPoiRules()
        {
            _poiRules.Clear();
            RefreshPoiRulesList();
        }

        void RemoveSelectedPoiRule()
        {
            int idx = _lstPoiRules.SelectedIndex;
            if (idx < 0 || idx >= _poiRules.Count) return;
            _poiRules.RemoveAt(idx);
            RefreshPoiRulesList();
        }

        void RefreshPoiRulesList()
        {
            if (_lstPoiRules == null) return;
            _lstPoiRules.BeginUpdate();
            _lstPoiRules.Items.Clear();
            for (int i = 0; i < _poiRules.Count; i++)
                _lstPoiRules.Items.Add(_poiRules[i]);
            _lstPoiRules.EndUpdate();
        }

        void OnCoordTextChanged()
        {
            if (_suppressEvents) return;
            TrainerSettings.SetLastSeedCoord(_txtCoord.Text);
            int seed;
            bool valid = TryParseSeedInput(_txtCoord.Text, out seed);
            _btnPreviewMap.Enabled = valid && _curCluster != null;
            _btnCopyCoord.Enabled = valid && _curCluster != null;
            _btnCopySeed.Enabled = valid && _curCluster != null;
            _lblSeedOnly.Text = valid ? ("纯种子：" + seed.ToString(CultureInfo.InvariantCulture)) : "纯种子：—";
            UpdateFavButton();
        }

        bool TryGetActiveSeed(out int seed)
        {
            if (TryParseSeedInput(_txtCoord.Text, out seed)) return true;
            if (_lastResult != null && _lastResult.Seed > 0) { seed = _lastResult.Seed; return true; }
            if (_curCluster != null && _curCluster.FixedCoordinate > 0) { seed = _curCluster.FixedCoordinate; return true; }
            seed = 0;
            return false;
        }

        static bool TryParseSeedInput(string raw, out int seed)
        {
            seed = 0;
            if (string.IsNullOrEmpty(raw)) return false;
            string s = raw.Trim();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed) && seed > 0)
                return true;
            Match m = Regex.Match(s, @"^.+?-(\d+)(?:-.+)?$");
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed) && seed > 0)
                return true;
            seed = 0;
            return false;
        }

        string GetActiveDlcCsv()
        {
            List<string> ids = new List<string>();
            if (_dlcs != null)
            {
                for (int i = 0; i < _dlcs.Count; i++)
                    if (_dlcs[i].Enabled) ids.Add(_dlcs[i].Id);
            }
            if (ids.Count == 0) return "VANILLA";
            return string.Join(",", ids.ToArray());
        }

        void OpenMapPreview()
        {
            if (_curCluster == null) return;
            int seed;
            if (!TryGetActiveSeed(out seed))
            {
                MessageBox.Show(this, "请先筛选出一个种子，或在「目的地坐标」框内输入要预览的种子/坐标。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_mapPreviewForm != null && !_mapPreviewForm.IsDisposed)
                try { _mapPreviewForm.Close(); } catch { }

            string fullCoord = SeedSearcher.FormatCoordinate(_curCluster.CoordinatePrefix, seed,
                string.IsNullOrEmpty(_txtTemplateCoord.Text) ? _txtCoord.Text : _txtTemplateCoord.Text);

            Dictionary<int, MapPreviewData> cache = (_previewCacheSeed == seed && _previewCache.Count > 0)
                ? _previewCache : null;

            int initWorldIdx = _curCluster.StartWorldIndex;
            WorldPlacementDef curWp = CurrentPlacement();
            if (curWp != null) initWorldIdx = curWp.PlacementIndex;

            ClusterDef clusterSnap = _curCluster;
            Action<MapPreviewData> onPreviewReady = (md) =>
            {
                if (md == null || _closing || IsDisposed) return;
                if (_previewCacheSeed != seed)
                {
                    _previewCache.Clear();
                    _previewCacheSeed = seed;
                }
                _previewCache[md.WorldIndex] = md;
                CommunitySeedDb.RecordScreenedMap(
                    md, clusterSnap, GetActiveDlcCsv(), _storyCode, _mixingCode, _txtTemplateCoord.Text, _catalog, true);
                if (string.IsNullOrEmpty(_txtPreview.Text.Trim()) || _lastResult == null || _lastResult.Seed != seed)
                {
                    SeedRecord sr = BuildCurrentSeedRecord(clusterSnap, null, _previewCache, seed);
                    Dictionary<int, MapPreviewData> one = new Dictionary<int, MapPreviewData>();
                    one[md.WorldIndex] = md;
                    _txtPreview.Text = (string.IsNullOrEmpty(sr.TraitSummary) ? fullCoord : sr.TraitSummary) + AppendMapSummaryText(one);
                }
            };
            Func<SeedRecord> buildRec = () =>
            {
                SeedRecord r = BuildCurrentSeedRecord(clusterSnap, _lastResult, _previewCacheSeed == seed ? _previewCache : null, seed);
                r.Seed = seed;
                r.Coordinate = fullCoord;
                return r;
            };

            string sArg = string.IsNullOrEmpty(_storyCode) ? "" : ("CODE:" + _storyCode);
            _mapPreviewForm = new MapPreviewForm(
                _curCluster,
                seed,
                fullCoord,
                GetActiveDlcCsv(),
                sArg,
                _mixingCode,
                _gameDir,
                _storyIdsCsv,
                _getBridge,
                cache,
                initWorldIdx,
                onPreviewReady,
                buildRec);
            _mapPreviewForm.Show(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            if (_advChangedHandler != null)
                TrainerSettings.AdvancedModeChanged -= _advChangedHandler;
            if (_seedDbStateHandler != null)
                CommunitySeedDb.StateChanged -= _seedDbStateHandler;
            CommunitySeedDb.FlushPendingToDisk();
            if (_historyForm != null && !_historyForm.IsDisposed)
                try { _historyForm.Close(); } catch { }
            if (_communityForm != null && !_communityForm.IsDisposed)
                try { _communityForm.Close(); } catch { }
            if (_mapPreviewForm != null && !_mapPreviewForm.IsDisposed)
                try { _mapPreviewForm.Close(); } catch { }
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
