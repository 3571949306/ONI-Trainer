using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ONITrainer
{
    public class CommunitySeedForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, INPUT = 0x292C36, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, TXT = 0xECEEF2, SUB = 0x949AA8, WARN = 0xF2B138, ERR = 0xF06464;

        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        readonly WorldGenCatalog _catalog;
        readonly ClusterDef _cluster;
        readonly List<PlanetConstraint> _constraints;
        readonly bool _hasTraitConstraint;
        readonly Dictionary<int, List<PoiFilterRule>> _groupedPoiRules;
        readonly Action<SeedRecord, bool> _onApplySeed;
        readonly Func<SeedRecord> _getCurrentSeedRecord;
        readonly Func<Dictionary<int, MapPreviewData>> _getCurrentMaps;
        readonly Func<BridgePreviewState> _getBridge;
        readonly string _gameDir;
        readonly string _activeDlcCsv;
        readonly string _storyIdsCsv;
        readonly string _mixingCode;
        readonly Action _dbChangedHandler;

        Label _lblRepoInfo, _lblStatus;
        CheckBox _chkMatchCurrent;
        TextBox _txtKeyword;
        ListView _lv;
        Button _btnSync, _btnCfg, _btnImport, _btnUploadAll, _btnApplyPreview, _btnApplyOnly;
        volatile bool _syncing;
        volatile bool _closing;

        readonly List<CommunityQueryResult> _rows = new List<CommunityQueryResult>();

        public CommunitySeedForm(
            WorldGenCatalog catalog,
            ClusterDef cluster,
            List<PlanetConstraint> constraints,
            bool hasTraitConstraint,
            Dictionary<int, List<PoiFilterRule>> groupedPoiRules,
            Action<SeedRecord, bool> onApplySeed,
            Func<SeedRecord> getCurrentSeedRecord,
            Func<Dictionary<int, MapPreviewData>> getCurrentMaps,
            Func<BridgePreviewState> getBridge,
            string gameDir,
            string activeDlcCsv,
            string storyIdsCsv,
            string mixingCode)
        {
            _catalog = catalog;
            _cluster = cluster;
            _constraints = constraints;
            _hasTraitConstraint = hasTraitConstraint;
            _groupedPoiRules = groupedPoiRules;
            _onApplySeed = onApplySeed;
            _getCurrentSeedRecord = getCurrentSeedRecord;
            _getCurrentMaps = getCurrentMaps;
            _getBridge = getBridge;
            _gameDir = gameDir;
            _activeDlcCsv = activeDlcCsv;
            _storyIdsCsv = storyIdsCsv;
            _mixingCode = mixingCode;

            Text = "GitHub 社区种子快速索引库  ·  同步、检索与批量上传";
            ClientSize = new Size(900, 564);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = C(BG);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = _fontReg;

            BuildUI();
            DpiHelper.ScaleLayout(this);

            _dbChangedHandler = () => SetUi(() =>
            {
                RefreshRepoHeader();
                UpdateUploadButtonText();
            });
            CommunitySeedDb.StateChanged += _dbChangedHandler;

            RefreshRepoHeader();
            UpdateUploadButtonText();
            ReloadList();
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        Button MakeBtn(string text, int x, int y, int w, int h, int bgRgb, int fgRgb, bool bold)
        {
            Button b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                Font = bold ? _fontBold : _fontReg,
                Cursor = Cursors.Hand,
                BackColor = C(bgRgb),
                ForeColor = C(fgRgb)
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = bgRgb == ACC ? C(ACC_HOV) : (bgRgb == BTN ? C(BTN_HOV) : C(bgRgb));
            return b;
        }

        void BuildUI()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = C(CARD) };
            top.Controls.Add(new Label
            {
                Text = "社区种子快速索引库 (GitHub)",
                Font = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold),
                ForeColor = C(ACC),
                Location = new Point(16, 6),
                Size = new Size(320, 24)
            });

            _lblRepoInfo = new Label
            {
                Text = "仓库：...",
                ForeColor = C(SUB),
                Font = _fontReg,
                AutoEllipsis = true,
                Location = new Point(18, 32),
                Size = new Size(520, 20)
            };
            top.Controls.Add(_lblRepoInfo);

            _btnSync = MakeBtn("同步社区索引", 548, 12, 120, 32, ACC, 0x0E231C, true);
            _btnSync.Click += (s, e) => TriggerSync();
            top.Controls.Add(_btnSync);

            _btnCfg = MakeBtn("数据源设置…", 676, 12, 104, 32, BTN, TXT, false);
            _btnCfg.Click += (s, e) => OpenConfigDialog();
            top.Controls.Add(_btnCfg);

            _btnImport = MakeBtn("导入数据包…", 788, 12, 96, 32, BTN, TXT, false);
            _btnImport.Click += (s, e) => ImportLocalJson();
            top.Controls.Add(_btnImport);

            Panel filterBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = C(BG) };
            string cname = _cluster != null ? (_cluster.DisplayName + " [" + _cluster.CoordinatePrefix + "]") : "未选择星群";
            filterBar.Controls.Add(new Label
            {
                Text = "星群：" + cname,
                ForeColor = C(TXT),
                Font = _fontBold,
                AutoEllipsis = true,
                Location = new Point(16, 12),
                Size = new Size(242, 20)
            });

            bool hasAnyFilter = _hasTraitConstraint || (_groupedPoiRules != null && _groupedPoiRules.Count > 0);
            _chkMatchCurrent = new CheckBox
            {
                Text = "仅显示满足当前筛选器条件",
                Checked = hasAnyFilter,
                ForeColor = hasAnyFilter ? C(ACC) : C(SUB),
                Font = _fontReg,
                Location = new Point(266, 11),
                Size = new Size(206, 22),
                AutoCheck = true
            };
            _chkMatchCurrent.CheckedChanged += (s, e) => ReloadList();
            filterBar.Controls.Add(_chkMatchCurrent);

            filterBar.Controls.Add(new Label
            {
                Text = "搜索",
                ForeColor = C(SUB),
                Font = _fontReg,
                Location = new Point(480, 12),
                Size = new Size(38, 20)
            });

            _txtKeyword = new TextBox
            {
                Location = new Point(522, 9),
                Size = new Size(276, 25),
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                Font = _fontReg
            };
            _txtKeyword.TextChanged += (s, e) => ReloadList();
            filterBar.Controls.Add(_txtKeyword);

            Button btnQuery = MakeBtn("刷新", 806, 9, 78, 26, BTN, TXT, false);
            btnQuery.Click += (s, e) => ReloadList();
            filterBar.Controls.Add(btnQuery);

            _lv = new ListView
            {
                Location = new Point(16, 102),
                Size = new Size(868, 400),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                BackColor = C(CARD),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                Font = _fontReg
            };
            _lv.Columns.Add("种子坐标", 190);
            _lv.Columns.Add("行星特质（全星群）", 260);
            _lv.Columns.Add("已索引喷孔 / 火山 / 遗迹（不含预览图）", 316);
            _lv.Columns.Add("来源", 84);
            _lv.SelectedIndexChanged += (s, e) => UpdateSelectionButtons();
            _lv.DoubleClick += (s, e) => ApplySelected(true);
            Controls.Add(_lv);

            _lblStatus = new Label
            {
                Text = "本地快速索引已就绪（开始筛选时会自动秒级检索）",
                ForeColor = C(SUB),
                Font = _fontReg,
                AutoEllipsis = true,
                Location = new Point(16, 518),
                Size = new Size(338, 22)
            };
            Controls.Add(_lblStatus);

            _btnUploadAll = MakeBtn("上传全部筛选记录 (0)…", 362, 514, 176, 32, WARN, 0x1A1508, true);
            _btnUploadAll.Click += (s, e) => OpenBatchUploadDialog();
            Controls.Add(_btnUploadAll);

            _btnApplyPreview = MakeBtn("应用并预览地图", 546, 514, 128, 32, ACC, 0x0E231C, true);
            _btnApplyPreview.Enabled = false;
            _btnApplyPreview.Click += (s, e) => ApplySelected(true);
            Controls.Add(_btnApplyPreview);

            _btnApplyOnly = MakeBtn("填入筛选器", 682, 514, 104, 32, BTN, TXT, false);
            _btnApplyOnly.Enabled = false;
            _btnApplyOnly.Click += (s, e) => ApplySelected(false);
            Controls.Add(_btnApplyOnly);

            Button btnClose = MakeBtn("关闭", 794, 514, 90, 32, BTN, TXT, false);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);

            Controls.Add(filterBar);
            Controls.Add(top);
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

        void UpdateUploadButtonText()
        {
            if (_btnUploadAll == null) return;
            int pend = CommunitySeedDb.GetPendingUploadCount();
            _btnUploadAll.Text = string.Format("上传全部记录 ({0})…", pend);
        }

        void RefreshRepoHeader()
        {
            int total = CommunitySeedDb.GetCachedTotalCount();
            int pend = CommunitySeedDb.GetPendingUploadCount();
            DateTime? last = CommunitySeedDb.GetLastSyncUtc();
            string lastStr = last.HasValue ? last.Value.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) : "尚未同步";
            _lblRepoInfo.Text = string.Format("仓库：{0} · 本地总索引 {1} 条 · 待上传 {2} 条 · 同步：{3}",
                TrainerSettings.SeedDbRepo, total, pend, lastStr);
        }

        void ReloadList()
        {
            _rows.Clear();
            _lv.BeginUpdate();
            _lv.Items.Clear();

            bool matchCur = _chkMatchCurrent != null && _chkMatchCurrent.Checked;
            List<CommunityQueryResult> found = CommunitySeedDb.Query(
                _catalog,
                _cluster,
                matchCur ? _constraints : null,
                matchCur && _hasTraitConstraint,
                matchCur ? _groupedPoiRules : null,
                _txtKeyword != null ? _txtKeyword.Text : "");


            int showCount = Math.Min(found.Count, 1000);
            for (int i = 0; i < showCount; i++)
            {
                CommunityQueryResult qr = found[i];
                CommunitySeedEntry ent = qr.Entry;
                string ts = ent.TraitSummary;
                if (string.IsNullOrEmpty(ts) && qr.EvalTraits != null && _cluster != null)
                {
                    List<string> tnames = new List<string>();
                    foreach (KeyValuePair<int, List<WorldTraitDef>> kv in qr.EvalTraits)
                        if (kv.Value != null)
                            for (int k = 0; k < kv.Value.Count; k++)
                                if (!tnames.Contains(kv.Value[k].DisplayName)) tnames.Add(kv.Value[k].DisplayName);
                    ts = string.Join("、", tnames.ToArray());
                }
                ListViewItem lvi = new ListViewItem(ent.Coordinate);
                lvi.SubItems.Add(ts ?? "");
                lvi.SubItems.Add(!string.IsNullOrEmpty(ent.PoiSummary) ? ent.PoiSummary : "（仅记录特质）");
                lvi.SubItems.Add(ent.Author ?? "");
                _lv.Items.Add(lvi);
                _rows.Add(qr);
            }
            _lv.EndUpdate();
            UpdateSelectionButtons();

            int clusterCached = _cluster != null ? CommunitySeedDb.LoadClusterSeeds(_cluster.CoordinatePrefix).Count : 0;
            _lblStatus.Text = clusterCached == 0
                ? "当前星群暂无索引数据：开始筛选会自动记录全部已筛种子，也可点击上方同步社区库。"
                : string.Format("当前星群已索引 {0} 条 · 匹配 {1} 条（主界面点「开始后台筛选」会直接毫秒级命中）", clusterCached, found.Count);
            _lblStatus.ForeColor = clusterCached == 0 ? C(WARN) : C(ACC);
        }

        void UpdateSelectionButtons()
        {
            bool sel = _lv.SelectedIndices.Count > 0 && _lv.SelectedIndices[0] >= 0 && _lv.SelectedIndices[0] < _rows.Count;
            _btnApplyPreview.Enabled = sel;
            _btnApplyOnly.Enabled = sel;
        }

        void TriggerSync()
        {
            if (_syncing) return;
            _syncing = true;
            _btnSync.Enabled = false;
            _lblStatus.Text = "正在连接 GitHub 社区仓库增量同步索引分片…";
            _lblStatus.ForeColor = C(WARN);

            Thread t = new Thread(() =>
            {
                CommunitySyncResult res = CommunitySeedDb.SyncNow(false, msg => SetUi(() =>
                {
                    _lblStatus.Text = msg;
                    _lblStatus.ForeColor = C(WARN);
                }));
                SetUi(() =>
                {
                    _syncing = false;
                    _btnSync.Enabled = true;
                    RefreshRepoHeader();
                    if (res.Success)
                    {
                        ReloadList();
                        _lblStatus.Text = string.Format("同步成功（{0}）· 更新 {1} 个分片 · 远端共 {2} 条种子",
                            res.UsedSource, res.UpdatedClusters, res.TotalSeeds);
                        _lblStatus.ForeColor = C(ACC);
                    }
                    else
                    {
                        _lblStatus.Text = "⚠ 同步失败：" + res.Error;
                        _lblStatus.ForeColor = C(ERR);
                    }
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void OpenConfigDialog()
        {
            using (SeedDbConfigDialog dlg = new SeedDbConfigDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshRepoHeader();
                    _lblStatus.Text = "数据源配置已保存。";
                    _lblStatus.ForeColor = C(ACC);
                }
            }
        }

        void ImportLocalJson()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "社区种子索引数据包 (*.json;*.txt)|*.json;*.txt|所有文件 (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                string err;
                int n = CommunitySeedDb.ImportLocalJsonFile(ofd.FileName, out err);
                if (n <= 0)
                {
                    MessageBox.Show(this, "导入失败：" + (err ?? "未知格式"), "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                RefreshRepoHeader();
                ReloadList();
                _lblStatus.Text = string.Format("已成功导入合并 {0} 条种子索引记录！", n);
                _lblStatus.ForeColor = C(ACC);
            }
        }

        void OpenBatchUploadDialog()
        {
            using (BatchUploadSeedsDialog dlg = new BatchUploadSeedsDialog())
            {
                dlg.ShowDialog(this);
                RefreshRepoHeader();
                UpdateUploadButtonText();
                ReloadList();
            }
        }

        void ApplySelected(bool openPreview)
        {
            if (_lv.SelectedIndices.Count == 0) return;
            int idx = _lv.SelectedIndices[0];
            if (idx < 0 || idx >= _rows.Count) return;

            CommunitySeedEntry ent = _rows[idx].Entry;
            SeedRecord rec = new SeedRecord
            {
                Seed = ent.Seed,
                Coordinate = ent.Coordinate,
                ClusterPath = !string.IsNullOrEmpty(ent.ClusterPath) ? ent.ClusterPath : (_cluster != null ? _cluster.FilePath : ""),
                ClusterName = !string.IsNullOrEmpty(ent.ClusterName) ? ent.ClusterName : (_cluster != null ? _cluster.DisplayName : ""),
                StoryCode = ent.StoryCode,
                MixingCode = ent.MixingCode,
                TraitSummary = ent.TraitSummary,
                PoiSummary = ent.PoiSummary,
                Note = ent.Note
            };
            if (_onApplySeed != null) _onApplySeed(rec, openPreview);
            if (openPreview) Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            if (_dbChangedHandler != null)
                CommunitySeedDb.StateChanged -= _dbChangedHandler;
            base.OnFormClosing(e);
        }
    }

    public class SeedDbConfigDialog : Form
    {
        const int BG = 0x16171C, INPUT = 0x292C36, ACC = 0x00D68F, BTN = 0x323642, TXT = 0xECEEF2, SUB = 0x949AA8;
        TextBox _txtRepo, _txtBranch, _txtMirror, _txtToken;
        CheckBox _chkAuto;

        public SeedDbConfigDialog()
        {
            Text = "社区种子索引库设置";
            ClientSize = new Size(500, 314);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = C(BG);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(Lbl("GitHub 仓库 (owner/repo)", 18, 16, 180, TXT));
            _txtRepo = Box(TrainerSettings.SeedDbRepo, 18, 38, 296);
            Controls.Add(_txtRepo);

            Controls.Add(Lbl("分支名 (Branch)", 326, 16, 150, TXT));
            _txtBranch = Box(TrainerSettings.SeedDbBranch, 326, 38, 154);
            Controls.Add(_txtBranch);

            Controls.Add(Lbl("自定义镜像 / 加速前缀（留空默认 Raw + jsDelivr 自动回退）", 18, 76, 462, TXT));
            _txtMirror = Box(TrainerSettings.SeedDbMirror, 18, 98, 462);
            Controls.Add(_txtMirror);
            Controls.Add(Lbl("示例：https://ghfast.top/  或包含 {repo}/{branch}/{path} 模板", 18, 126, 462, SUB));

            _chkAuto = new CheckBox
            {
                Text = "每次打开筛选器时自动后台增量拉取社区索引（间隔 ≥ 6 小时）",
                Checked = TrainerSettings.SeedDbAutoPull,
                ForeColor = C(TXT),
                Location = new Point(18, 156),
                Size = new Size(462, 22),
                AutoCheck = true
            };
            Controls.Add(_chkAuto);

            Controls.Add(Lbl("GitHub PAT Token（可选，用于客户端一键直传全部筛选记录；拉取无需填写）", 18, 188, 462, TXT));
            _txtToken = Box(TrainerSettings.SeedDbGitHubToken, 18, 210, 462);
            _txtToken.UseSystemPasswordChar = true;
            Controls.Add(_txtToken);

            Button ok = new Button
            {
                Text = "保存",
                Location = new Point(298, 264),
                Size = new Size(88, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(ACC),
                ForeColor = C(0x0E231C),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += (s, e) =>
            {
                string r = _txtRepo.Text.Trim();
                if (!CommunitySeedDb.IsValidRepoSlug(r))
                {
                    MessageBox.Show(this, "仓库地址必须是 owner/repo 格式，例如 3571949306/ONI-Seed-DB。",
                        "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                TrainerSettings.SetSeedDbConfig(r, _txtBranch.Text.Trim(), _txtMirror.Text.Trim(), _chkAuto.Checked, _txtToken.Text.Trim());
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            Button cancel = new Button
            {
                Text = "取消",
                Location = new Point(394, 264),
                Size = new Size(86, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(BTN),
                ForeColor = C(TXT)
            };
            cancel.FlatAppearance.BorderSize = 0;
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            DpiHelper.ScaleLayout(this);
        }

        static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }
        static Label Lbl(string t, int x, int y, int w, int rgb)
        {
            return new Label { Text = t, Location = new Point(x, y), Size = new Size(w, 20), ForeColor = C(rgb) };
        }
        static TextBox Box(string val, int x, int y, int w)
        {
            return new TextBox
            {
                Text = val ?? "",
                Location = new Point(x, y),
                Size = new Size(w, 24),
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle
            };
        }
    }


    public class BatchUploadSeedsDialog : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, INPUT = 0x292C36, ACC = 0x00D68F, BTN = 0x323642;
        const int TXT = 0xECEEF2, SUB = 0x949AA8, WARN = 0xF2B138, ERR = 0xF06464;

        TextBox _txtAuthor, _txtToken, _txtSummary;
        Label _lblStatus;
        Button _btnApiUpload, _btnWebUpload, _btnExportFile, _btnClearDone;
        List<CommunitySeedEntry> _pending = new List<CommunitySeedEntry>();
        volatile bool _uploading;
        volatile bool _closing;

        public BatchUploadSeedsDialog()
        {
            Text = "批量上传已记录的筛选种子  ·  GitHub 社区快速索引库";
            ClientSize = new Size(680, 446);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = C(BG);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUI();
            DpiHelper.ScaleLayout(this);
            ReloadPendingData();
        }

        static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        static Button MakeBtn(string t, int x, int y, int w, int h, int bg, int fg, bool bold)
        {
            Button b = new Button
            {
                Text = t,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9F, bold ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand,
                BackColor = C(bg),
                ForeColor = C(fg)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        void BuildUI()
        {
            Controls.Add(new Label
            {
                Text = "自动记录说明：每次筛选时，所有经过检测的种子编号、星球特质、喷孔/火山/遗迹坐标与产量均已自动记录（不含预览图）。",
                Location = new Point(16, 12),
                Size = new Size(648, 36),
                ForeColor = C(SUB)
            });

            Controls.Add(new Label { Text = "贡献者署名", Location = new Point(16, 56), Size = new Size(76, 20), ForeColor = C(TXT) });
            _txtAuthor = new TextBox
            {
                Text = string.IsNullOrEmpty(Environment.UserName) ? "Player" : Environment.UserName,
                Location = new Point(96, 53),
                Size = new Size(140, 24),
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_txtAuthor);

            Controls.Add(new Label { Text = "GitHub Token (直传用)", Location = new Point(250, 56), Size = new Size(142, 20), ForeColor = C(TXT) });
            _txtToken = new TextBox
            {
                Text = TrainerSettings.SeedDbGitHubToken ?? "",
                Location = new Point(396, 53),
                Size = new Size(268, 24),
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = true
            };
            Controls.Add(_txtToken);

            _txtSummary = new TextBox
            {
                Location = new Point(16, 88),
                Size = new Size(648, 260),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = C(CARD),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9F)
            };
            Controls.Add(_txtSummary);

            _lblStatus = new Label
            {
                Text = "准备就绪",
                Location = new Point(16, 358),
                Size = new Size(648, 22),
                ForeColor = C(ACC),
                AutoEllipsis = true
            };
            Controls.Add(_lblStatus);

            _btnApiUpload = MakeBtn("一键全部直传 GitHub", 16, 392, 168, 34, ACC, 0x0E231C, true);
            _btnApiUpload.Click += (s, e) => UploadAllViaApi();
            Controls.Add(_btnApiUpload);

            _btnWebUpload = MakeBtn("网页批量提交 (免Token)", 192, 392, 166, 34, BTN, TXT, true);
            _btnWebUpload.Click += (s, e) => UploadAllViaBrowser();
            Controls.Add(_btnWebUpload);

            _btnExportFile = MakeBtn("导出数据包…", 366, 392, 108, 34, BTN, TXT, false);
            _btnExportFile.Click += (s, e) => ExportPackageFile();
            Controls.Add(_btnExportFile);

            _btnClearDone = MakeBtn("清空待传", 482, 392, 88, 34, BTN, WARN, false);
            _btnClearDone.Click += (s, e) => ClearPendingQueue();
            Controls.Add(_btnClearDone);

            Button btnClose = MakeBtn("关闭", 578, 392, 86, 34, BTN, TXT, false);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
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

        void ReloadPendingData()
        {
            _pending = CommunitySeedDb.LoadAllPendingSeeds();
            Dictionary<string, int> byCluster = CommunitySeedDb.GetPendingCountsByCluster();
            bool hasPending = _pending.Count > 0;

            _btnApiUpload.Enabled = hasPending && !_uploading;
            _btnWebUpload.Enabled = hasPending && !_uploading;
            _btnExportFile.Enabled = hasPending && !_uploading;
            _btnClearDone.Enabled = hasPending && !_uploading;

            if (!hasPending)
            {
                _txtSummary.Text = "当前没有待上传的筛选记录。\r\n\r\n只需在筛选器中点击「开始后台筛选」，修改器会自动记录筛选过程中生成的全部种子编号、星球特质与间歇泉/火山数据（不含地形预览图）。";
                _lblStatus.Text = "待上传记录：0 条（本地索引库共 " + CommunitySeedDb.GetCachedTotalCount() + " 条）";
                _lblStatus.ForeColor = C(SUB);
                return;
            }

            string gzPayload = CommunitySeedDb.BuildCompressedBatchPayload(_pending);
            double kb = Encoding.UTF8.GetByteCount(gzPayload) / 1024.0;

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "待上传种子总数：{0} 条  |  涉及星群：{1} 个  |  GZip 压缩后体积：{2:0.1} KB\r\n",
                _pending.Count, byCluster.Count, kb);
            sb.AppendLine("------------------------------------------------------------------");
            foreach (KeyValuePair<string, int> kv in byCluster)
                sb.AppendFormat("  · 星群 [{0}]：{1} 条种子记录\r\n", kv.Key, kv.Value);
            sb.AppendLine("------------------------------------------------------------------");
            sb.AppendLine("最近记录预览（前 25 条，不含预览图）：");
            int previewMax = Math.Min(25, _pending.Count);
            for (int i = 0; i < previewMax; i++)
            {
                CommunitySeedEntry e = _pending[_pending.Count - 1 - i];
                string poiNote = !string.IsNullOrEmpty(e.PoiSummary) ? e.PoiSummary : e.TraitSummary;
                if (poiNote != null && poiNote.Length > 58) poiNote = poiNote.Substring(0, 58) + "…";
                sb.AppendFormat("  #{0}  [{1}]  {2}\r\n", e.Seed, e.Coordinate, poiNote ?? "");
            }
            if (_pending.Count > previewMax)
                sb.AppendFormat("  … 以及其余 {0} 条已记录种子（点击下方上传将一次性全部提交）\r\n", _pending.Count - previewMax);

            _txtSummary.Text = sb.ToString();
            _lblStatus.Text = string.Format("共 {0} 条待上传种子记录，点击下方按钮即可全部上传到仓库 {1}",
                _pending.Count, TrainerSettings.SeedDbRepo);
            _lblStatus.ForeColor = C(ACC);
        }

        void UploadAllViaApi()
        {
            if (_uploading || _pending.Count == 0) return;
            string token = _txtToken.Text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                MessageBox.Show(this,
                    "直传需要填写 GitHub Personal Access Token（具备 public_repo 或 issues 权限）。\r\n" +
                    "若不想配置 Token，请点击旁边的「网页批量提交 (免Token)」按钮即可一键打包提交！",
                    "未填写 Token", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!string.Equals(token, TrainerSettings.SeedDbGitHubToken, StringComparison.Ordinal))
            {
                TrainerSettings.SetSeedDbConfig(
                    TrainerSettings.SeedDbRepo, TrainerSettings.SeedDbBranch,
                    TrainerSettings.SeedDbMirror, TrainerSettings.SeedDbAutoPull, token);
            }

            string repo = TrainerSettings.SeedDbRepo;
            string author = _txtAuthor.Text.Trim();
            _uploading = true;
            _btnApiUpload.Enabled = false;
            _btnWebUpload.Enabled = false;
            _lblStatus.Text = "正在打包并批量上传全部已记录种子…";
            _lblStatus.ForeColor = C(WARN);

            Thread t = new Thread(() =>
            {
                int uploaded;
                List<string> urls;
                string err;
                bool ok = CommunitySeedDb.UploadAllPendingViaGitHubApi(
                    repo, token, author,
                    msg => SetUi(() => { _lblStatus.Text = msg; _lblStatus.ForeColor = C(WARN); }),
                    out uploaded, out urls, out err);

                SetUi(() =>
                {
                    _uploading = false;
                    ReloadPendingData();
                    if (ok)
                    {
                        _lblStatus.Text = string.Format("✓ 已成功上传全部 {0} 条种子记录！仓库机器人将自动合并更新索引库。", uploaded);
                        _lblStatus.ForeColor = C(ACC);
                        MessageBox.Show(this,
                            string.Format("成功上传全部 {0} 条种子记录（共 {1} 个压缩分包）！\r\n仓库 GitHub Actions 机器人会自动校验合并至索引库。",
                                uploaded, urls.Count),
                            "批量上传完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        _lblStatus.Text = "⚠ 上传中断（已完成 " + uploaded + " 条）：" + (err ?? "网络错误");
                        _lblStatus.ForeColor = C(ERR);
                    }
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void UploadAllViaBrowser()
        {
            if (_pending.Count == 0) return;
            string author = _txtAuthor.Text.Trim();
            for (int i = 0; i < _pending.Count; i++)
                _pending[i].Author = string.IsNullOrEmpty(author) ? "Player" : author;

            string clipPayload;
            bool needPaste;
            string url = CommunitySeedDb.BuildBatchBrowserIssueUrl(TrainerSettings.SeedDbRepo, _pending, out clipPayload, out needPaste);
            try { Clipboard.SetText(clipPayload); } catch { }
            try { Process.Start(url); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开浏览器：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string tip = needPaste
                ? string.Format("全部 {0} 条种子的 GZip 压缩索引包已自动复制到剪贴板！\r\n请在打开的 GitHub Issue 页面按 Ctrl+V 粘贴并点击 Submit new issue。\r\n\r\n提交完成后是否立即清空本地「待上传」队列？（本地快速索引仍会完整保留）", _pending.Count)
                : string.Format("已在浏览器打开预填好全部 {0} 条种子的 GitHub Issue 页面，直接点击 Submit new issue 即可完成上传！\r\n\r\n是否将这批记录标记为已上传并清空待传队列？（本地快速索引仍会完整保留）", _pending.Count);

            if (MessageBox.Show(this, tip, "网页批量上传", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                CommunitySeedDb.ClearAllPendingAfterManualUpload();
                ReloadPendingData();
                _lblStatus.Text = "✓ 已标记为已上传，待传队列已清空（本地快速索引完整保留）。";
                _lblStatus.ForeColor = C(ACC);
            }
        }

        void ExportPackageFile()
        {
            if (_pending.Count == 0) return;
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "社区种子批量索引 JSON (*.json)|*.json";
                sfd.FileName = string.Format(CultureInfo.InvariantCulture, "oni_seeds_batch_{0}_{1}.json",
                    _pending.Count, DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture));
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                string author = _txtAuthor.Text.Trim();
                for (int i = 0; i < _pending.Count; i++)
                    _pending[i].Author = string.IsNullOrEmpty(author) ? "Player" : author;
                string json = CommunitySeedDb.BuildBatchSubmissionJson(_pending);
                File.WriteAllText(sfd.FileName, json, new UTF8Encoding(false));
                _lblStatus.Text = "已导出全部 " + _pending.Count + " 条记录至：" + Path.GetFileName(sfd.FileName);
                _lblStatus.ForeColor = C(ACC);
            }
        }

        void ClearPendingQueue()
        {
            if (_pending.Count == 0) return;
            if (MessageBox.Show(this,
                string.Format("确定清空当前 {0} 条「待上传」队列吗？\r\n（注：本机已记录的快速筛选索引不会被删除，仍可本地秒级命中）", _pending.Count),
                "确认清空待传队列", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            CommunitySeedDb.ClearAllPendingAfterManualUpload();
            ReloadPendingData();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            base.OnFormClosing(e);
        }
    }
}
