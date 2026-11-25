using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
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
        readonly string _coordinate;
        readonly string _activeDlcCsv;
        readonly string _storyArg;
        readonly string _mixingCode;
        readonly string _gameDir;
        readonly string _storyIdsCsv;
        readonly Func<BridgePreviewState> _getBridge;
        readonly List<WorldPlacementDef> _orderedPlacements;
        readonly Dictionary<int, MapPreviewData> _sharedCache;
        readonly Action<int> _onSelectPlanet;

        ListView _lv;
        Label _lblStatus;
        ProgressBar _pb;
        Button _btnRescan, _btnExportAll, _btnStop;

        volatile bool _closing;
        volatile bool _stopReq;
        volatile bool _exportingAll;
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
            : this(cluster, seed, coordinate, activeDlcCsv, storyArg, mixingCode, "", "", getBridge, sharedCache, onSelectPlanet)
        {
        }

        public ClusterOverviewForm(
            ClusterDef cluster,
            int seed,
            string coordinate,
            string activeDlcCsv,
            string storyArg,
            string mixingCode,
            string gameDir,
            string storyIdsCsv,
            Func<BridgePreviewState> getBridge,
            Dictionary<int, MapPreviewData> sharedCache,
            Action<int> onSelectPlanet)
        {
            _cluster = cluster;
            _seed = seed;
            _coordinate = coordinate ?? "";
            _activeDlcCsv = activeDlcCsv ?? "";
            _storyArg = storyArg ?? "";
            _mixingCode = mixingCode ?? "";
            _gameDir = string.IsNullOrEmpty(gameDir) ? (GameInstallationDetector.DetectGameDir() ?? "") : gameDir;
            _storyIdsCsv = storyIdsCsv ?? "";
            _getBridge = getBridge;
            _sharedCache = sharedCache ?? new Dictionary<int, MapPreviewData>();
            _onSelectPlanet = onSelectPlanet;
            _orderedPlacements = cluster != null ? cluster.GetOrderedPlacements() : new List<WorldPlacementDef>();

            Text = string.Format("整星群总览  ·  {0}  ·  {1}",
                cluster != null ? cluster.DisplayName : "", _coordinate);
            ClientSize = new Size(1000, 560);
            MinimumSize = new Size(860, 440);
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
                Size = new Size(540, 20),
                ForeColor = C(ACC),
                Font = _fontBold,
                AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _pb = new ProgressBar
            {
                Location = new Point(14, 31),
                Size = new Size(540, 14),
                Minimum = 0,
                Maximum = Math.Max(1, _orderedPlacements.Count),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _btnRescan = MakeBtn("开始/继续扫描", 568, 12, 112, 30, ACC, 0x0E231C, true);
            _btnRescan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRescan.Click += (s, e) => StartScan();

            _btnExportAll = MakeBtn("导出全星群大图", 686, 12, 124, 30, BTN, TXT, true);
            _btnExportAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnExportAll.Click += (s, e) => ExportAllCombinedPng();

            _btnStop = MakeBtn("停止", 816, 12, 76, 30, BTN, TXT, false);
            _btnStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStop.Enabled = false;
            _btnStop.Click += (s, e) => { _stopReq = true; };

            Button btnClose = MakeBtn("关闭", 898, 12, 88, 30, BTN, TXT, false);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Click += (s, e) => Close();

            top.Controls.Add(_lblStatus);
            top.Controls.Add(_pb);
            top.Controls.Add(_btnRescan);
            top.Controls.Add(_btnExportAll);
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
            if (_exportingAll) return;
            if (_scanThread != null && _scanThread.IsAlive) return;
            _stopReq = false;
            _btnRescan.Enabled = false;
            if (_btnExportAll != null) _btnExportAll.Enabled = false;
            _btnStop.Enabled = true;

            _scanThread = new Thread(ScanWorker);
            _scanThread.IsBackground = true;
            _scanThread.Start();
        }

        MapPreviewData GeneratePlanetSafe(int worldIndex, out string err)
        {
            BridgePreviewState st = _getBridge != null ? _getBridge() : null;
            if (st != null && st.Bridge != null && !st.InSave)
            {
                return st.Bridge.GenerateMapPreview(
                    _cluster.FilePath, _seed, worldIndex, _activeDlcCsv, _storyArg, _mixingCode, out err);
            }
            string hStory = string.IsNullOrEmpty(_storyIdsCsv) ? _storyArg : _storyIdsCsv;
            return HeadlessPreviewHost.GeneratePreview(
                _gameDir, _cluster.FilePath, _seed, worldIndex, _activeDlcCsv, hStory, _mixingCode, false, st, out err);
        }

        void ExportAllCombinedPng()
        {
            if (_exportingAll) return;
            if (_scanThread != null && _scanThread.IsAlive)
            {
                MessageBox.Show(this, "正在扫描星群中，请等待扫描完成或先点击「停止」后再导出。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_orderedPlacements.Count == 0)
            {
                MessageBox.Show(this, "当前星群没有可导出的小行星。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int missing = 0;
            int cachedCount = 0;
            lock (_sharedCache)
            {
                for (int i = 0; i < _orderedPlacements.Count; i++)
                {
                    MapPreviewData existing;
                    if (_sharedCache.TryGetValue(_orderedPlacements[i].PlacementIndex, out existing) && existing != null) cachedCount++;
                    else missing++;
                }
            }

            BridgePreviewState initialState = _getBridge != null ? _getBridge() : null;
            bool useMainMenuBridge = (initialState != null && initialState.Bridge != null && !initialState.InSave);
            bool hasHeadless = string.IsNullOrEmpty(_mixingCode) &&
                !string.IsNullOrEmpty(_gameDir) &&
                GameInstallationDetector.IsValidGameDir(_gameDir);
            if (missing > 0 && !useMainMenuBridge && !hasHeadless)
            {
                if (cachedCount == 0)
                {
                    MessageBox.Show(this, "当前未定位到《缺氧》游戏目录且尚无缓存，请先设置有效游戏目录或启动游戏再导出全部地图。", "无法生成地图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DialogResult dr = MessageBox.Show(this,
                    string.Format("当前无法生成其余 {0} 颗小行星，是否仅将已完成的 {1} 颗小行星合并导出为大图？", missing, cachedCount),
                    "导出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG 高清大图 (*.png)|*.png";
                sfd.FileName = string.Format("{0}_{1}_ALL.png",
                    _cluster != null ? _cluster.CoordinatePrefix : "ONI", _seed);
                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                string outPath = sfd.FileName;
                _exportingAll = true;
                _stopReq = false;
                _btnRescan.Enabled = false;
                if (_btnExportAll != null) _btnExportAll.Enabled = false;
                _btnStop.Enabled = true;

                Thread t = new Thread(() =>
                {
                    int done = 0;
                    int failed = 0;
                    int total = _orderedPlacements.Count;
                    string lastErr = null;
                    try
                    {
                        for (int i = 0; i < total; i++)
                        {
                            if (_closing || _stopReq) break;
                            int rowIdx = i;
                            WorldPlacementDef wp = _orderedPlacements[i];
                            MapPreviewData data;
                            lock (_sharedCache) { _sharedCache.TryGetValue(wp.PlacementIndex, out data); }

                            if (data == null)
                            {
                                SetUi(() =>
                                {
                                    if (rowIdx < _lv.Items.Count) _lv.Items[rowIdx].SubItems[3].Text = "生成中…";
                                    _lblStatus.Text = string.Format("导出全星群大图 ({0}/{1})：正在生成 {2}…",
                                        i + 1, total, wp.World != null ? wp.World.DisplayName : wp.WorldId);
                                    _lblStatus.ForeColor = C(WARN);
                                });

                                string err = null;
                                for (int attempt = 0; attempt < 3; attempt++)
                                {
                                    if (_closing || _stopReq) break;
                                    data = GeneratePlanetSafe(wp.PlacementIndex, out err);
                                    if (data != null) break;
                                    if (err == null || err.IndexOf("busy", StringComparison.OrdinalIgnoreCase) < 0) break;
                                    Thread.Sleep(450);
                                }
                                if (data == null)
                                {
                                    failed++;
                                    if (!string.IsNullOrEmpty(err)) lastErr = err;
                                    SetUi(() => { if (rowIdx < _lv.Items.Count) _lv.Items[rowIdx].SubItems[3].Text = "失败"; });
                                    continue;
                                }
                                lock (_sharedCache) { _sharedCache[wp.PlacementIndex] = data; }
                                if (SeedStore.IsFavorite(_coordinate, _seed))
                                    SeedStore.SaveSingleFavoritePreview(_coordinate, _seed, data);
                            }

                            done++;
                            MapPreviewData cap = data;
                            int doneSnap = done;
                            SetUi(() =>
                            {
                                UpdateRowFromData(rowIdx, cap);
                                _pb.Value = Math.Min(_pb.Maximum, doneSnap);
                            });
                        }

                        if (_closing || _stopReq)
                        {
                            SetUi(() =>
                            {
                                _exportingAll = false;
                                _btnRescan.Enabled = true;
                                if (_btnExportAll != null) _btnExportAll.Enabled = true;
                                _btnStop.Enabled = false;
                                _lblStatus.Text = "已取消导出全星群大图";
                                _lblStatus.ForeColor = C(SUB);
                            });
                            return;
                        }

                        Dictionary<int, MapPreviewData> snap = new Dictionary<int, MapPreviewData>();
                        lock (_sharedCache)
                        {
                            foreach (KeyValuePair<int, MapPreviewData> kv in _sharedCache)
                                if (kv.Value != null) snap[kv.Key] = kv.Value;
                        }

                        if (snap.Count == 0)
                        {
                            SetUi(() =>
                            {
                                _exportingAll = false;
                                _btnRescan.Enabled = true;
                                if (_btnExportAll != null) _btnExportAll.Enabled = true;
                                _btnStop.Enabled = false;
                                _lblStatus.Text = "全星群扫描失败，未能生成任何地图";
                                _lblStatus.ForeColor = C(WARN);
                                MessageBox.Show(this,
                                    "未能成功生成任何小行星地图，无法导出大图：\r\n" + (lastErr ?? "请确认游戏已连接。"),
                                    "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            });
                            return;
                        }

                        SetUi(() =>
                        {
                            _lblStatus.Text = "正在合成全星群高清大图，请稍候…";
                            _lblStatus.ForeColor = C(WARN);
                        });

                        int imgW, imgH;
                        using (Bitmap big = MapPreviewForm.RenderCombinedClusterBitmap(
                            _cluster, _coordinate, _orderedPlacements, snap, MapRenderMode.BiomeTerrain, null))
                        {
                            imgW = big.Width;
                            imgH = big.Height;
                            big.Save(outPath, ImageFormat.Png);
                        }

                        SetUi(() =>
                        {
                            _exportingAll = false;
                            _btnRescan.Enabled = true;
                            if (_btnExportAll != null) _btnExportAll.Enabled = true;
                            _btnStop.Enabled = false;
                            string warnNote = failed > 0 ? string.Format("（{0} 颗未成功生成）", failed) : "";
                            _lblStatus.Text = string.Format("已导出全星群大图 ({0}×{1})：{2}{3}", imgW, imgH, outPath, warnNote);
                            _lblStatus.ForeColor = C(ACC);
                            DialogResult openRes = MessageBox.Show(this,
                                string.Format(
                                    "全星群高清大图已导出完成！\r\n包含 {0}/{1} 颗小行星{2}，分辨率 {3}×{4}。\r\n保存位置：{5}\r\n\r\n是否立即打开文件所在文件夹？",
                                    snap.Count, total, warnNote, imgW, imgH, outPath),
                                "导出全星群完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                            if (openRes == DialogResult.Yes)
                            {
                                try { Process.Start("explorer.exe", "/select,\"" + outPath + "\""); } catch { }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        SetUi(() =>
                        {
                            _exportingAll = false;
                            _btnRescan.Enabled = true;
                            if (_btnExportAll != null) _btnExportAll.Enabled = true;
                            _btnStop.Enabled = false;
                            _lblStatus.Text = "导出全星群大图失败：" + ex.Message;
                            _lblStatus.ForeColor = C(WARN);
                            MessageBox.Show(this, "导出全星群大图失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        });
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
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

                    string err;
                    data = GeneratePlanetSafe(wp.PlacementIndex, out err);
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
                if (_btnExportAll != null) _btnExportAll.Enabled = true;
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
