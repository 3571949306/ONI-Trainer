using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace ONITrainer
{
    public class MapPreviewForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, INPUT = 0x292C36, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, TXT = 0xECEEF2, SUB = 0x949AA8, WARN = 0xF2B138, ERR = 0xF06464;

        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        readonly ClusterDef _cluster;
        readonly int _seed;
        readonly string _coordinate;
        readonly string _activeDlcCsv;
        readonly string _storyArg;
        readonly string _mixingCode;
        readonly Func<BridgePreviewState> _getBridge;
        readonly List<WorldPlacementDef> _orderedPlacements;
        readonly Dictionary<int, MapPreviewData> _cache = new Dictionary<int, MapPreviewData>();

        Panel _topBar, _advBar, _rightPanel, _statusBar, _overlayPanel, _pnlDetail;
        Label _lblCoord, _lblMeta, _lblPoiHeader, _lblPoiSummary, _lblPoiDetail, _lblHover, _lblOverlay;
        ComboBox _cboPlanet, _cboRenderMode;
        CheckBox[] _catChecks;
        readonly string[] _catKeys = { "metal_volcano", "volcano", "water_geyser", "gas_geyser", "oil", "story", "poi" };
        readonly string[] _catNames = { "金属火山", "火山", "水源/液泉", "气体喷口", "油井", "故事特质", "遗迹/传送" };
        Button _btnRetry, _btnExportPng, _btnOverview;
        ListBox _lstPois;
        MapCanvas _canvas;
        ClusterOverviewForm _overviewForm;

        MapPreviewData _curData;
        Bitmap _curBitmap;
        MapRenderMode _renderMode = MapRenderMode.BiomeTerrain;
        bool _suppressEvents;
        volatile bool _closing;
        int _loadGenSeq;
        int _targetWorldIndex;
        Action _advChangedHandler;

        public MapPreviewForm(
            ClusterDef cluster,
            int seed,
            string coordinate,
            string activeDlcCsv,
            Func<BridgePreviewState> getBridge,
            Dictionary<int, MapPreviewData> initialCache,
            int initialWorldIndex)
            : this(cluster, seed, coordinate, activeDlcCsv, "", "", getBridge, initialCache, initialWorldIndex)
        {
        }

        public MapPreviewForm(
            ClusterDef cluster,
            int seed,
            string coordinate,
            string activeDlcCsv,
            string storyArg,
            string mixingCode,
            Func<BridgePreviewState> getBridge,
            Dictionary<int, MapPreviewData> initialCache,
            int initialWorldIndex)
        {
            _cluster = cluster;
            _seed = seed;
            _coordinate = coordinate ?? "";
            _activeDlcCsv = activeDlcCsv ?? "";
            _storyArg = storyArg ?? "";
            _mixingCode = mixingCode ?? "";
            _getBridge = getBridge;
            _orderedPlacements = cluster != null ? cluster.GetOrderedPlacements() : new List<WorldPlacementDef>();
            _targetWorldIndex = initialWorldIndex;

            if (initialCache != null)
            {
                foreach (KeyValuePair<int, MapPreviewData> kv in initialCache)
                    if (kv.Value != null) _cache[kv.Key] = kv.Value;
            }

            Text = string.Format("星图与地貌预览  ·  {0}  ·  {1}",
                cluster != null ? cluster.DisplayName : "", _coordinate);
            ClientSize = new Size(1080, 680);
            MinimumSize = new Size(880, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = C(BG);
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = _fontReg;

            BuildUI();
            DpiHelper.ScaleLayout(this);
            float scale = DpiHelper.GetDpiScale();
            if (scale > 1.01f)
                _lstPois.ItemHeight = (int)Math.Round(44 * scale);

            _advChangedHandler = OnGlobalAdvancedModeChanged;
            TrainerSettings.AdvancedModeChanged += _advChangedHandler;
            ApplyAdvancedModeVisibility();

            PopulatePlanetSelector();
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void BuildUI()
        {

            _topBar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = C(CARD) };

            _lblCoord = new Label
            {
                Text = _coordinate,
                ForeColor = C(ACC),
                Font = new Font("Consolas", 9.5F, FontStyle.Bold),
                Location = new Point(12, 13),
                Size = new Size(196, 22),
                AutoEllipsis = true
            };
            _topBar.Controls.Add(_lblCoord);

            _topBar.Controls.Add(new Label
            {
                Text = "小行星",
                ForeColor = C(TXT),
                Location = new Point(212, 14),
                Size = new Size(44, 20)
            });

            _cboPlanet = new ComboBox
            {
                Location = new Point(258, 11),
                Size = new Size(172, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat,
                Font = _fontReg
            };
            _cboPlanet.SelectedIndexChanged += (s, e) => OnPlanetChanged();
            _topBar.Controls.Add(_cboPlanet);

            Button btnZoomIn = B("+", 436, 10, 28, 27, false);
            btnZoomIn.Click += (s, e) => _canvas.StepZoom(1.3f);
            Button btnZoomOut = B("-", 468, 10, 28, 27, false);
            btnZoomOut.Click += (s, e) => _canvas.StepZoom(1f / 1.3f);
            Button btnFit = B("适应窗口", 500, 10, 68, 27, false);
            btnFit.Click += (s, e) => _canvas.ResetFit();
            Button btnReload = B("重新生成", 572, 10, 70, 27, false);
            btnReload.Click += (s, e) => ReloadCurrentPlanet(true);
            _btnOverview = B("整星群总览", 646, 10, 92, 27, true);
            _btnOverview.Click += (s, e) => OpenClusterOverview();

            _topBar.Controls.Add(btnZoomIn);
            _topBar.Controls.Add(btnZoomOut);
            _topBar.Controls.Add(btnFit);
            _topBar.Controls.Add(btnReload);
            _topBar.Controls.Add(_btnOverview);

            _lblMeta = new Label
            {
                Text = "准备加载…",
                ForeColor = C(SUB),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(744, 13),
                Size = new Size(322, 22),
                AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _topBar.Controls.Add(_lblMeta);


            _advBar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = C(0x1B1D24), Visible = false };
            _advBar.Controls.Add(new Label
            {
                Text = "高级图层",
                ForeColor = C(ACC),
                Font = _fontBold,
                Location = new Point(14, 9),
                Size = new Size(64, 20)
            });

            _cboRenderMode = new ComboBox
            {
                Location = new Point(80, 6),
                Size = new Size(116, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat,
                Font = _fontReg
            };
            _cboRenderMode.Items.AddRange(new object[] { "生态地形", "纯元素色", "温度热力图" });
            _cboRenderMode.SelectedIndex = 0;
            _cboRenderMode.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                _renderMode = (MapRenderMode)_cboRenderMode.SelectedIndex;
                RebuildMapBitmap();
            };
            _advBar.Controls.Add(_cboRenderMode);

            _catChecks = new CheckBox[_catKeys.Length];
            int cx = 208;
            int[] cwidths = { 82, 56, 86, 82, 56, 82, 82 };
            for (int i = 0; i < _catKeys.Length; i++)
            {
                CheckBox cb = new CheckBox
                {
                    Text = _catNames[i],
                    ForeColor = C(TXT),
                    Location = new Point(cx, 8),
                    Size = new Size(cwidths[i], 22),
                    Checked = true
                };
                cb.CheckedChanged += (s, e) => RefreshFilteredPois();
                _catChecks[i] = cb;
                _advBar.Controls.Add(cb);
                cx += cwidths[i] + 4;
            }

            _btnExportPng = B("导出 PNG…", 974, 5, 92, 26, true);
            _btnExportPng.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnExportPng.Click += (s, e) => ExportPng();
            _advBar.Controls.Add(_btnExportPng);


            _statusBar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = C(CARD) };
            _lblHover = new Label
            {
                Text = "鼠标滚轮缩放 · 左键按住拖拽平移 · 点击标记或右侧列表项可高亮定位",
                ForeColor = C(SUB),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 12, 0)
            };
            _statusBar.Controls.Add(_lblHover);


            _rightPanel = new Panel { Dock = DockStyle.Right, Width = 336, BackColor = C(0x1B1D24) };

            Panel poiHdr = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = C(CARD) };
            _lblPoiHeader = new Label
            {
                Text = "间歇泉 / 火山 / 遗迹清单",
                ForeColor = C(ACC),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                Location = new Point(12, 6),
                Size = new Size(312, 22)
            };
            _lblPoiSummary = new Label
            {
                Text = "—",
                ForeColor = C(SUB),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                Location = new Point(12, 28),
                Size = new Size(312, 20),
                AutoEllipsis = true
            };
            poiHdr.Controls.Add(_lblPoiHeader);
            poiHdr.Controls.Add(_lblPoiSummary);

            _pnlDetail = new Panel { Dock = DockStyle.Bottom, Height = 66, BackColor = C(CARD), Padding = new Padding(10, 6, 10, 6) };
            _lblPoiDetail = new Label
            {
                Text = "点击任意间歇泉/火山/遗迹查看详细产出与周期…",
                ForeColor = C(TXT),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                Dock = DockStyle.Fill
            };
            _pnlDetail.Controls.Add(_lblPoiDetail);

            _lstPois = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = C(0x181A20),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 44
            };
            _lstPois.DrawItem += DrawPoiItem;
            _lstPois.SelectedIndexChanged += (s, e) =>
            {
                MapPoiItem poi = _lstPois.SelectedItem as MapPoiItem;
                UpdateSelectedPoiDetail(poi);
                if (_suppressEvents) return;
                _canvas.FocusPoi(poi);
            };

            _rightPanel.Controls.Add(_lstPois);
            _rightPanel.Controls.Add(_pnlDetail);
            _rightPanel.Controls.Add(poiHdr);


            Panel centerWrap = new Panel { Dock = DockStyle.Fill, BackColor = C(0x0E1015) };
            _canvas = new MapCanvas { Dock = DockStyle.Fill };
            _canvas.HoveredCellChanged += OnCanvasHoverChanged;
            _canvas.PoiClicked += OnCanvasPoiClicked;

            _overlayPanel = new Panel
            {
                Size = new Size(460, 148),
                BackColor = C(CARD),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            _lblOverlay = new Label
            {
                Text = "正在生成地图预览…",
                ForeColor = C(TXT),
                Font = _fontBold,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(18, 16),
                Size = new Size(424, 78)
            };
            _btnRetry = B("重试生成", 180, 102, 100, 30, true);
            _btnRetry.Visible = false;
            _btnRetry.Click += (s, e) => ReloadCurrentPlanet(true);
            _overlayPanel.Controls.Add(_lblOverlay);
            _overlayPanel.Controls.Add(_btnRetry);

            centerWrap.Controls.Add(_overlayPanel);
            centerWrap.Controls.Add(_canvas);
            centerWrap.Resize += (s, e) => CenterOverlay(centerWrap);

            Controls.Add(centerWrap);
            Controls.Add(_rightPanel);
            Controls.Add(_statusBar);
            Controls.Add(_advBar);
            Controls.Add(_topBar);
        }

        void CenterOverlay(Panel wrap)
        {
            if (_overlayPanel == null || wrap == null) return;
            _overlayPanel.Location = new Point(
                Math.Max(8, (wrap.ClientSize.Width - _overlayPanel.Width) / 2),
                Math.Max(8, (wrap.ClientSize.Height - _overlayPanel.Height) / 2));
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

        void OnGlobalAdvancedModeChanged()
        {
            if (_closing || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (_closing || IsDisposed) return;
                    ApplyAdvancedModeVisibility();
                }));
            }
            catch { }
        }

        void ApplyAdvancedModeVisibility()
        {
            bool adv = TrainerSettings.AdvancedMode;
            _advBar.Visible = adv;
            if (!adv)
            {
                _suppressEvents = true;
                if (_cboRenderMode.SelectedIndex != 0) _cboRenderMode.SelectedIndex = 0;
                for (int i = 0; i < _catChecks.Length; i++) _catChecks[i].Checked = true;
                _suppressEvents = false;
                if (_renderMode != MapRenderMode.BiomeTerrain)
                {
                    _renderMode = MapRenderMode.BiomeTerrain;
                    RebuildMapBitmap();
                }
                RefreshFilteredPois();
            }
        }

        sealed class PlanetChoice
        {
            public WorldPlacementDef Placement;
            public override string ToString()
            {
                string nm = Placement.World != null ? Placement.World.DisplayName : Placement.WorldId;
                return string.Format("[{0}] {1}", Placement.GroupLabel(), nm);
            }
        }

        void PopulatePlanetSelector()
        {
            _suppressEvents = true;
            _cboPlanet.Items.Clear();
            int selIdx = 0;
            for (int i = 0; i < _orderedPlacements.Count; i++)
            {
                WorldPlacementDef wp = _orderedPlacements[i];
                _cboPlanet.Items.Add(new PlanetChoice { Placement = wp });
                if (wp.PlacementIndex == _targetWorldIndex)
                    selIdx = i;
            }
            if (_cboPlanet.Items.Count > 0) _cboPlanet.SelectedIndex = selIdx;
            _suppressEvents = false;

            OnPlanetChanged();
        }

        void OnPlanetChanged()
        {
            if (_suppressEvents) return;
            ReloadCurrentPlanet(false);
        }

        void ReloadCurrentPlanet(bool forceRegenerate)
        {
            PlanetChoice pc = _cboPlanet.SelectedItem as PlanetChoice;
            if (pc == null || _cluster == null) return;
            int worldIndex = pc.Placement.PlacementIndex;

            if (!forceRegenerate)
            {
                MapPreviewData cached;
                lock (_cache) { _cache.TryGetValue(worldIndex, out cached); }
                if (cached != null)
                {
                    ShowPreviewData(cached);
                    return;
                }
            }

            int mySeq = Interlocked.Increment(ref _loadGenSeq);
            ShowOverlay("正在生成「" + (pc.Placement.World != null ? pc.Placement.World.DisplayName : pc.Placement.WorldId) + "」地图，请稍候…", false, false);
            _lblMeta.Text = "生成中…";
            _lblMeta.ForeColor = C(WARN);

            Thread t = new Thread(() =>
            {
                BridgePreviewState state = _getBridge != null ? _getBridge() : null;
                if (state == null || state.Bridge == null)
                {
                    SetUi(() =>
                    {
                        if (mySeq != _loadGenSeq) return;
                        ShowOverlay("请先启动《缺氧》游戏并停留在「主菜单」（无需进入存档）\r\n修改器会自动连接后即可点击「重试生成」。", true, true);
                        _lblMeta.Text = "未连接游戏";
                        _lblMeta.ForeColor = C(WARN);
                    });
                    return;
                }
                if (state.InSave)
                {
                    SetUi(() =>
                    {
                        if (mySeq != _loadGenSeq) return;
                        ShowOverlay("当前游戏已进入殖民地存档。\r\n为避免影响存档数据，请先在游戏内「返回主菜单」，再点击「重试生成」。", true, true);
                        _lblMeta.Text = "需返回主菜单";
                        _lblMeta.ForeColor = C(WARN);
                    });
                    return;
                }

                string err;
                MapPreviewData data = state.Bridge.GenerateMapPreview(
                    _cluster.FilePath, _seed, worldIndex, _activeDlcCsv, _storyArg, _mixingCode, out err);

                SetUi(() =>
                {
                    if (mySeq != _loadGenSeq) return;
                    if (data == null)
                    {
                        ShowOverlay(err ?? "地图生成失败", true, true);
                        _lblMeta.Text = "生成失败";
                        _lblMeta.ForeColor = C(ERR);
                        return;
                    }
                    lock (_cache) { _cache[worldIndex] = data; }
                    ShowPreviewData(data);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void OpenClusterOverview()
        {
            if (_overviewForm != null && !_overviewForm.IsDisposed)
            {
                _overviewForm.Activate();
                return;
            }
            _overviewForm = new ClusterOverviewForm(
                _cluster, _seed, _coordinate, _activeDlcCsv, _storyArg, _mixingCode,
                _getBridge, _cache, SelectPlanetIndex);
            _overviewForm.Show(this);
        }

        public void SelectPlanetIndex(int worldIndex)
        {
            for (int i = 0; i < _cboPlanet.Items.Count; i++)
            {
                PlanetChoice pc = _cboPlanet.Items[i] as PlanetChoice;
                if (pc != null && pc.Placement.PlacementIndex == worldIndex)
                {
                    _cboPlanet.SelectedIndex = i;
                    break;
                }
            }
        }

        void UpdateSelectedPoiDetail(MapPoiItem poi)
        {
            if (_lblPoiDetail == null) return;
            if (poi == null)
                _lblPoiDetail.Text = "点击任意间歇泉/火山/遗迹查看详细产出与周期…";
            else
                _lblPoiDetail.Text = poi.FormatDetailText();
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

        void ShowOverlay(string msg, bool showRetry, bool isError)
        {
            if (_overlayPanel == null) return;
            _lblOverlay.Text = msg;
            _lblOverlay.ForeColor = isError ? C(WARN) : C(TXT);
            _btnRetry.Visible = showRetry;
            CenterOverlay(_overlayPanel.Parent as Panel);
            _overlayPanel.Visible = true;
            _overlayPanel.BringToFront();
        }

        void HideOverlay()
        {
            if (_overlayPanel != null) _overlayPanel.Visible = false;
        }

        void ShowPreviewData(MapPreviewData data)
        {
            HideOverlay();
            _curData = data;
            string traits = string.IsNullOrEmpty(data.TraitsSummary) ? "无特质" : data.TraitsSummary;
            _lblMeta.Text = string.Format(CultureInfo.InvariantCulture, "{0}×{1} · 耗时 {2:0.00}s · {3}",
                data.Width, data.Height, data.ElapsedMs / 1000.0, traits);
            _lblMeta.ForeColor = C(ACC);
            RebuildMapBitmap();
            RefreshFilteredPois();
            UpdateSelectedPoiDetail(null);
        }

        void RebuildMapBitmap()
        {
            if (_curData == null) return;
            Bitmap next = _curData.RenderBitmap(_renderMode);
            Bitmap old = _curBitmap;
            _curBitmap = next;
            _canvas.SetData(_curData, _curBitmap);
            if (old != null)
                try { old.Dispose(); } catch { }
        }

        HashSet<string> ActiveCategoryFilter()
        {
            HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal);
            allowed.Add("start");
            if (!TrainerSettings.AdvancedMode)
            {
                for (int i = 0; i < _catKeys.Length; i++) allowed.Add(_catKeys[i]);
                return allowed;
            }
            for (int i = 0; i < _catKeys.Length; i++)
                if (_catChecks != null && i < _catChecks.Length && _catChecks[i].Checked)
                    allowed.Add(_catKeys[i]);
            return allowed;
        }

        void RefreshFilteredPois()
        {
            if (_curData == null) return;
            HashSet<string> allowed = ActiveCategoryFilter();
            List<MapPoiItem> filtered = new List<MapPoiItem>();
            int mv = 0, vol = 0, wat = 0, gas = 0, oil = 0, st = 0, poi = 0;

            for (int i = 0; i < _curData.Pois.Count; i++)
            {
                MapPoiItem item = _curData.Pois[i];
                if (item.Category == "metal_volcano") mv++;
                else if (item.Category == "volcano") vol++;
                else if (item.Category == "water_geyser") wat++;
                else if (item.Category == "gas_geyser") gas++;
                else if (item.Category == "oil") oil++;
                else if (item.Category == "story") st++;
                else if (item.Category == "poi") poi++;

                if (allowed.Contains(item.Category))
                    filtered.Add(item);
            }

            _lblPoiSummary.Text = string.Format("金属火山 {0} · 火山 {1} · 水泉 {2} · 气泉 {3} · 油井 {4} · 故事/遗迹 {5}",
                mv, vol, wat, gas, oil, st + poi);

            _suppressEvents = true;
            _lstPois.BeginUpdate();
            _lstPois.Items.Clear();
            for (int i = 0; i < filtered.Count; i++)
                _lstPois.Items.Add(filtered[i]);
            _lstPois.EndUpdate();
            _suppressEvents = false;

            _canvas.SetVisiblePois(filtered);
        }

        public static Color CategoryColor(string cat)
        {
            if (cat == "start") return Color.FromArgb(0x00, 0xD6, 0x8F);
            if (cat == "metal_volcano") return Color.FromArgb(0xFF, 0x5A, 0x5F);
            if (cat == "volcano") return Color.FromArgb(0xFF, 0x94, 0x36);
            if (cat == "water_geyser") return Color.FromArgb(0x3D, 0xB8, 0xFF);
            if (cat == "gas_geyser") return Color.FromArgb(0xBA, 0x84, 0xFF);
            if (cat == "oil") return Color.FromArgb(0xF0, 0xC4, 0x2D);
            if (cat == "story") return Color.FromArgb(0xFF, 0x6B, 0xB5);
            return Color.FromArgb(0x78, 0xD8, 0xC4);
        }

        static Color GradeColor(byte grade)
        {
            if (grade == 1) return Color.FromArgb(0xF5, 0xB9, 0x42);
            if (grade == 2) return Color.FromArgb(0x00, 0xD6, 0x8F);
            if (grade == 3) return Color.FromArgb(0x4A, 0xAE, 0xFF);
            return Color.FromArgb(0x8C, 0x92, 0xA2);
        }

        void DrawPoiItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _lstPois.Items.Count) return;
            MapPoiItem item = (MapPoiItem)_lstPois.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color bg = selected ? C(0x2B3748) : ((e.Index & 1) == 0 ? C(0x1A1C23) : C(0x1F212A));
            using (SolidBrush brBg = new SolidBrush(bg))
                e.Graphics.FillRectangle(brBg, e.Bounds);

            Color dotCol = CategoryColor(item.Category);
            float scale = DpiHelper.GetDpiScale();
            int pad = 8;
            int dotSz = Math.Max(8, (int)Math.Round(10 * scale));
            int dotY = e.Bounds.Top + (e.Bounds.Height / 4) - (dotSz / 2) + 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brDot = new SolidBrush(dotCol))
                e.Graphics.FillEllipse(brDot, e.Bounds.Left + pad, dotY, dotSz, dotSz);

            int textX = e.Bounds.Left + pad + dotSz + 8;
            int halfH = e.Bounds.Height / 2;
            int badgeW = 0;
            string grade = item.GradeText();
            if (!string.IsNullOrEmpty(grade))
            {
                badgeW = (int)Math.Round(42 * scale);
                Rectangle badgeRect = new Rectangle(e.Bounds.Right - badgeW - 6, e.Bounds.Top + 4, badgeW, halfH - 5);
                Color gcol = GradeColor(item.Grade);
                using (SolidBrush brBadge = new SolidBrush(Color.FromArgb(44, gcol)))
                using (Pen penBadge = new Pen(gcol))
                {
                    e.Graphics.FillRectangle(brBadge, badgeRect);
                    e.Graphics.DrawRectangle(penBadge, badgeRect);
                }
                TextRenderer.DrawText(e.Graphics, grade + "级", _fontBold, badgeRect,
                    gcol, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                badgeW += 10;
            }

            string title = item.DisplayName + (item.IsRandomGeyser ? " [随机]" : "");
            TextRenderer.DrawText(e.Graphics, title, _fontBold,
                new Rectangle(textX, e.Bounds.Top + 2, e.Bounds.Width - (textX - e.Bounds.Left) - 4 - badgeW, halfH),
                dotCol, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            string sub;
            if (item.HasGeyserStats)
            {
                sub = string.Format(CultureInfo.InvariantCulture, "均产 {0} · {1:0.#}℃ · 距 {2} 格",
                    item.FormatYieldShort(), item.OutputTempC, item.DistFromStart);
            }
            else if (item.Category == "start")
            {
                sub = string.Format("({0}, {1})  ·  初始出生点", item.X, item.Y);
            }
            else
            {
                sub = string.Format("({0}, {1})  ·  距打印舱 {2} 格", item.X, item.Y, item.DistFromStart);
            }
            TextRenderer.DrawText(e.Graphics, sub, _fontReg,
                new Rectangle(textX, e.Bounds.Top + halfH - 1, e.Bounds.Width - (textX - e.Bounds.Left) - 4, halfH),
                C(SUB), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        void OnCanvasPoiClicked(MapPoiItem poi)
        {
            if (poi == null) return;
            UpdateSelectedPoiDetail(poi);
            _suppressEvents = true;
            for (int i = 0; i < _lstPois.Items.Count; i++)
            {
                if (_lstPois.Items[i] == poi)
                {
                    _lstPois.SelectedIndex = i;
                    break;
                }
            }
            _suppressEvents = false;
        }

        void OnCanvasHoverChanged(int x, int y, MapPoiItem nearPoi)
        {
            if (_curData == null || x < 0 || y < 0 || x >= _curData.Width || y >= _curData.Height)
            {
                _lblHover.Text = "鼠标滚轮缩放 · 左键按住拖拽平移 · 点击标记或右侧列表项可高亮定位";
                _lblHover.ForeColor = C(SUB);
                return;
            }
            int idx = y * _curData.Width + x;
            string zoneName = MapPreviewData.GetZoneName(_curData.ZoneType[idx]);
            ushort eidx = _curData.ElemIdx[idx];
            string elemName = (eidx < _curData.Elements.Length && !string.IsNullOrEmpty(_curData.Elements[eidx].Name))
                ? _curData.Elements[eidx].Name : "未知";
            double tempC = _curData.TempDeciC[idx] / 10.0;
            string info = string.Format(CultureInfo.InvariantCulture,
                "坐标 ({0}, {1})   |   {2}   |   {3}  {4:0.0}℃",
                x, y, zoneName, elemName, tempC);
            if (nearPoi != null)
            {
                string extra = nearPoi.HasGeyserStats
                    ? string.Format(CultureInfo.InvariantCulture, " [{0} {1}]", nearPoi.GradeText(), nearPoi.FormatYieldShort())
                    : "";
                info += string.Format("   |   ★ {0}{1}（距 {2} 格）", nearPoi.DisplayName, extra, nearPoi.DistFromStart);
                _lblHover.ForeColor = C(ACC);
            }
            else
            {
                _lblHover.ForeColor = C(TXT);
            }
            _lblHover.Text = info;
        }

        void ExportPng()
        {
            if (_curData == null || _curBitmap == null) return;
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG 图片 (*.png)|*.png";
                sfd.FileName = string.Format("{0}_{1}_w{2}.png", _cluster.CoordinatePrefix, _seed, _curData.WorldIndex);
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int scale = 3;
                    using (Bitmap outBmp = new Bitmap(_curData.Width * scale, _curData.Height * scale, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(outBmp))
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(_curBitmap, new Rectangle(0, 0, outBmp.Width, outBmp.Height));
                        _canvas.DrawOverlayTo(g, 0f, 0f, scale, _curData.Width, _curData.Height);
                        outBmp.Save(sfd.FileName, ImageFormat.Png);
                    }
                    _lblHover.Text = "已导出高分辨率地图到：" + sfd.FileName;
                    _lblHover.ForeColor = C(ACC);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出 PNG 失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            Interlocked.Increment(ref _loadGenSeq);
            if (_overviewForm != null && !_overviewForm.IsDisposed)
                try { _overviewForm.Close(); } catch { }
            if (_advChangedHandler != null)
                TrainerSettings.AdvancedModeChanged -= _advChangedHandler;
            if (_curBitmap != null)
                try { _curBitmap.Dispose(); } catch { }
            base.OnFormClosing(e);
        }

        sealed class MapCanvas : Control
        {
            MapPreviewData _data;
            Bitmap _bmp;
            List<MapPoiItem> _pois = new List<MapPoiItem>();
            MapPoiItem _selectedPoi;
            MapPoiItem _hoverPoi;

            float _zoom = 1f;
            float _panX;
            float _panY;
            bool _fittedOnce;
            bool _dragging;
            Point _dragStart;
            float _dragStartPanX, _dragStartPanY;
            bool _movedDuringDrag;

            readonly Font _lblFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold);

            public event Action<int, int, MapPoiItem> HoveredCellChanged;
            public event Action<MapPoiItem> PoiClicked;

            public MapCanvas()
            {
                DoubleBuffered = true;
                BackColor = Color.FromArgb(0x0E, 0x10, 0x15);
                Cursor = Cursors.Hand;
            }

            public void SetData(MapPreviewData data, Bitmap bmp)
            {
                bool sizeChanged = (_data == null || data == null || _data.Width != data.Width || _data.Height != data.Height);
                _data = data;
                _bmp = bmp;
                if (sizeChanged) _selectedPoi = null;
                if (!_fittedOnce || sizeChanged)
                {
                    ResetFit();
                    _fittedOnce = true;
                }
                else
                {
                    Invalidate();
                }
            }

            public void SetVisiblePois(List<MapPoiItem> pois)
            {
                _pois = pois ?? new List<MapPoiItem>();
                if (_selectedPoi != null && !_pois.Contains(_selectedPoi))
                    _selectedPoi = null;
                Invalidate();
            }

            public void ResetFit()
            {
                if (_data == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
                float zx = (ClientSize.Width - 24f) / _data.Width;
                float zy = (ClientSize.Height - 24f) / _data.Height;
                _zoom = Math.Max(0.4f, Math.Min(10f, Math.Min(zx, zy)));
                _panX = (ClientSize.Width - _data.Width * _zoom) * 0.5f;
                _panY = (ClientSize.Height - _data.Height * _zoom) * 0.5f;
                Invalidate();
            }

            public void StepZoom(float factor)
            {
                ZoomAt(ClientSize.Width * 0.5f, ClientSize.Height * 0.5f, factor);
            }

            void ZoomAt(float cx, float cy, float factor)
            {
                if (_data == null) return;
                float oldZ = _zoom;
                float newZ = Math.Max(0.4f, Math.Min(14f, oldZ * factor));
                if (Math.Abs(newZ - oldZ) < 0.001f) return;
                float wx = (cx - _panX) / oldZ;
                float wy = (cy - _panY) / oldZ;
                _zoom = newZ;
                _panX = cx - wx * newZ;
                _panY = cy - wy * newZ;
                Invalidate();
            }

            public void FocusPoi(MapPoiItem poi)
            {
                _selectedPoi = poi;
                if (_data != null && poi != null && ClientSize.Width > 0 && ClientSize.Height > 0)
                {
                    if (_zoom < 2.2f) _zoom = 2.6f;
                    float pyImg = (_data.Height - 1 - poi.Y) + 0.5f;
                    float pxImg = poi.X + 0.5f;
                    _panX = ClientSize.Width * 0.5f - pxImg * _zoom;
                    _panY = ClientSize.Height * 0.5f - pyImg * _zoom;
                }
                Invalidate();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (!_fittedOnce && _data != null) ResetFit();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                float factor = e.Delta > 0 ? 1.2f : (1f / 1.2f);
                ZoomAt(e.X, e.Y, factor);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left)
                {
                    _dragging = true;
                    _movedDuringDrag = false;
                    _dragStart = e.Location;
                    _dragStartPanX = _panX;
                    _dragStartPanY = _panY;
                    Capture = true;
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (_dragging)
                {
                    int dx = e.X - _dragStart.X;
                    int dy = e.Y - _dragStart.Y;
                    if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3) _movedDuringDrag = true;
                    _panX = _dragStartPanX + dx;
                    _panY = _dragStartPanY + dy;
                    Invalidate();
                    return;
                }
                UpdateHover(e.X, e.Y);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (_dragging)
                {
                    _dragging = false;
                    Capture = false;
                    if (!_movedDuringDrag)
                    {
                        MapPoiItem hit = HitTestPoi(e.X, e.Y);
                        if (hit != null)
                        {
                            _selectedPoi = hit;
                            Invalidate();
                            Action<MapPoiItem> cb = PoiClicked;
                            if (cb != null) cb(hit);
                        }
                    }
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hoverPoi = null;
                Action<int, int, MapPoiItem> cb = HoveredCellChanged;
                if (cb != null) cb(-1, -1, null);
                Invalidate();
            }

            void UpdateHover(int mx, int my)
            {
                if (_data == null) return;
                int gx = (int)Math.Floor((mx - _panX) / _zoom);
                int imgY = (int)Math.Floor((my - _panY) / _zoom);
                int gy = _data.Height - 1 - imgY;

                MapPoiItem hit = HitTestPoi(mx, my);
                if (hit != _hoverPoi)
                {
                    _hoverPoi = hit;
                    Invalidate();
                }

                Action<int, int, MapPoiItem> cb = HoveredCellChanged;
                if (cb != null) cb(gx, gy, hit);
            }

            MapPoiItem HitTestPoi(int mx, int my)
            {
                if (_data == null || _pois == null) return null;
                MapPoiItem best = null;
                float bestDistSq = 18f * 18f;
                for (int i = 0; i < _pois.Count; i++)
                {
                    MapPoiItem p = _pois[i];
                    float sx = _panX + (p.X + 0.5f) * _zoom;
                    float sy = _panY + (_data.Height - 1 - p.Y + 0.5f) * _zoom;
                    float d2 = (mx - sx) * (mx - sx) + (my - sy) * (my - sy);
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        best = p;
                    }
                }
                return best;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (_bmp == null || _data == null) return;

                Graphics g = e.Graphics;
                g.InterpolationMode = _zoom >= 1.5f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                float dw = _data.Width * _zoom;
                float dh = _data.Height * _zoom;
                g.DrawImage(_bmp, _panX, _panY, dw, dh);

                using (Pen borderPen = new Pen(Color.FromArgb(90, 0xEC, 0xEE, 0xF2), 1f))
                    g.DrawRectangle(borderPen, _panX, _panY, dw, dh);

                DrawOverlayTo(g, _panX, _panY, _zoom, _data.Width, _data.Height);
            }

            public void DrawOverlayTo(Graphics g, float offX, float offY, float scale, int mapW, int mapH)
            {
                if (_pois == null || _pois.Count == 0) return;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Default;

                List<RectangleF> placedLabels = new List<RectangleF>();

                for (int i = 0; i < _pois.Count; i++)
                {
                    MapPoiItem p = _pois[i];
                    float sx = offX + (p.X + 0.5f) * scale;
                    float sy = offY + (mapH - 1 - p.Y + 0.5f) * scale;
                    Color col = CategoryColor(p.Category);
                    bool isFocus = (p == _selectedPoi || p == _hoverPoi);
                    float r = isFocus ? 8f : (p.Category == "start" ? 7.5f : 5.5f);

                    if (isFocus)
                    {
                        using (Pen halo = new Pen(Color.White, 2f))
                            g.DrawEllipse(halo, sx - r - 4f, sy - r - 4f, (r + 4f) * 2f, (r + 4f) * 2f);
                    }

                    using (SolidBrush br = new SolidBrush(col))
                    using (Pen outline = new Pen(Color.FromArgb(220, 14, 16, 22), 1.6f))
                    {
                        if (p.Category == "metal_volcano" || p.Category == "volcano")
                        {
                            PointF[] tri =
                            {
                                new PointF(sx, sy - r - 1f),
                                new PointF(sx - r, sy + r * 0.85f),
                                new PointF(sx + r, sy + r * 0.85f)
                            };
                            g.FillPolygon(br, tri);
                            g.DrawPolygon(outline, tri);
                        }
                        else if (p.Category == "oil")
                        {
                            g.FillRectangle(br, sx - r, sy - r, r * 2f, r * 2f);
                            g.DrawRectangle(outline, sx - r, sy - r, r * 2f, r * 2f);
                        }
                        else if (p.Category == "story" || p.Category == "poi" || p.Category == "start")
                        {
                            PointF[] dia =
                            {
                                new PointF(sx, sy - r - 1f),
                                new PointF(sx + r + 1f, sy),
                                new PointF(sx, sy + r + 1f),
                                new PointF(sx - r - 1f, sy)
                            };
                            g.FillPolygon(br, dia);
                            g.DrawPolygon(outline, dia);
                        }
                        else
                        {
                            g.FillEllipse(br, sx - r, sy - r, r * 2f, r * 2f);
                            g.DrawEllipse(outline, sx - r, sy - r, r * 2f, r * 2f);
                        }
                    }

                    bool major = p.Category != "poi" || scale >= 2.2f || isFocus;
                    if (!major) continue;

                    string lbl = p.DisplayName;
                    SizeF sz = g.MeasureString(lbl, _lblFont);
                    RectangleF box = new RectangleF(sx + r + 4f, sy - sz.Height * 0.5f, sz.Width + 6f, sz.Height + 2f);
                    if (!isFocus && OverlapsAny(placedLabels, box)) continue;
                    placedLabels.Add(box);

                    using (SolidBrush pillBg = new SolidBrush(Color.FromArgb(isFocus ? 235 : 185, 0x16, 0x17, 0x1C)))
                    using (SolidBrush pillFg = new SolidBrush(isFocus ? Color.White : col))
                    using (Pen pillBd = new Pen(col, isFocus ? 1.3f : 0.8f))
                    {
                        g.FillRectangle(pillBg, box);
                        g.DrawRectangle(pillBd, box.X, box.Y, box.Width, box.Height);
                        g.DrawString(lbl, _lblFont, pillFg, box.X + 3f, box.Y + 1f);
                    }
                }
            }

            static bool OverlapsAny(List<RectangleF> list, RectangleF r)
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].IntersectsWith(r)) return true;
                return false;
            }
        }
    }
}
