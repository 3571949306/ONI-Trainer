using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ONITrainer
{
    public sealed class SeedHistoryForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, GOLD = 0xF5B942, ERR = 0xF25F5C, TXT = 0xECEEF2, SUB = 0x949AA8;

        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        readonly Action<SeedRecord, bool> _onApply;

        RadioButton _rbHistory, _rbFav;
        ListView _lv;
        Label _lblCount;
        Button _btnPreview, _btnApply, _btnFav, _btnNote, _btnCopy, _btnDel;
        bool _showingFavorites = true;

        public SeedHistoryForm(Action<SeedRecord, bool> onApply)
        {
            _onApply = onApply;

            Text = "种子历史与收藏夹  ·  ONI Seed Vault";
            ClientSize = new Size(920, 520);
            MinimumSize = new Size(760, 420);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = C(BG);
            ForeColor = C(TXT);
            Font = _fontReg;

            BuildUI();
            DpiHelper.ScaleLayout(this);
            WireResize();
            SeedStore.StoreChanged += OnStoreChanged;
            RefreshList();
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void BuildUI()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = C(CARD) };
            top.Controls.Add(new Label
            {
                Text = "候选种子历史 & 持久化收藏夹",
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = C(ACC),
                Location = new Point(14, 10),
                Size = new Size(310, 26)
            });

            _rbFav = new RadioButton
            {
                Text = "我的收藏（持久保存）",
                Location = new Point(340, 12),
                Size = new Size(168, 22),
                ForeColor = C(GOLD),
                Font = _fontBold,
                Checked = true
            };
            _rbFav.CheckedChanged += (s, e) => { if (_rbFav.Checked) { _showingFavorites = true; RefreshList(); } };

            _rbHistory = new RadioButton
            {
                Text = "本次会话历史",
                Location = new Point(518, 12),
                Size = new Size(132, 22),
                ForeColor = C(TXT),
                Font = _fontReg
            };
            _rbHistory.CheckedChanged += (s, e) => { if (_rbHistory.Checked) { _showingFavorites = false; RefreshList(); } };

            _lblCount = new Label
            {
                Text = "0 条记录",
                Location = new Point(664, 14),
                Size = new Size(236, 20),
                ForeColor = C(SUB),
                TextAlign = ContentAlignment.MiddleRight
            };

            top.Controls.Add(_rbFav);
            top.Controls.Add(_rbHistory);
            top.Controls.Add(_lblCount);

            _lv = new ListView
            {
                Location = new Point(12, 56),
                Size = new Size(896, 408),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                BackColor = C(CARD),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle
            };
            _lv.Columns.Add("坐标", 236);
            _lv.Columns.Add("星群", 136);
            _lv.Columns.Add("特质 / 间歇泉摘要", 272);
            _lv.Columns.Add("备注", 128);
            _lv.Columns.Add("记录时间", 118);
            _lv.SelectedIndexChanged += (s, e) => UpdateButtons();
            _lv.DoubleClick += (s, e) => InvokeSelected(true);

            Panel bot = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = C(BG) };
            _btnPreview = MakeBtn("载入并预览地图", 12, 8, 134, ACC, 0x0E231C, true);
            _btnPreview.Click += (s, e) => InvokeSelected(true);

            _btnApply = MakeBtn("应用到主窗口", 154, 8, 114, BTN, TXT, false);
            _btnApply.Click += (s, e) => InvokeSelected(false);

            _btnFav = MakeBtn("★ 收藏/取消收藏", 276, 8, 128, BTN, GOLD, false);
            _btnFav.Click += (s, e) => ToggleFavSelected();

            _btnNote = MakeBtn("编辑备注…", 412, 8, 96, BTN, TXT, false);
            _btnNote.Click += (s, e) => EditNoteSelected();

            _btnCopy = MakeBtn("复制坐标", 516, 8, 90, BTN, TXT, false);
            _btnCopy.Click += (s, e) => CopySelected();

            _btnDel = MakeBtn("删除收藏", 614, 8, 90, BTN, ERR, false);
            _btnDel.Click += (s, e) => DeleteSelected();

            Button btnClose = MakeBtn("关闭", 814, 8, 94, BTN, TXT, false);
            btnClose.Click += (s, e) => Close();

            bot.Controls.Add(_btnPreview);
            bot.Controls.Add(_btnApply);
            bot.Controls.Add(_btnFav);
            bot.Controls.Add(_btnNote);
            bot.Controls.Add(_btnCopy);
            bot.Controls.Add(_btnDel);
            bot.Controls.Add(btnClose);

            Controls.Add(_lv);
            Controls.Add(top);
            Controls.Add(bot);
        }

        Button MakeBtn(string text, int x, int y, int w, int bg, int fg, bool bold)
        {
            Button b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(bg),
                ForeColor = C(fg),
                Font = bold ? _fontBold : _fontReg,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            if (bg == ACC) b.FlatAppearance.MouseOverBackColor = C(ACC_HOV);
            else b.FlatAppearance.MouseOverBackColor = C(BTN_HOV);
            return b;
        }

        void WireResize()
        {
            Resize += (s, e) =>
            {
                if (_lv != null)
                {
                    _lv.Size = new Size(Math.Max(200, ClientSize.Width - 24), Math.Max(160, ClientSize.Height - 108));
                    _lblCount.Location = new Point(Math.Max(340, ClientSize.Width - _lblCount.Width - 16), _lblCount.Top);
                }
            };
        }

        void OnStoreChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                if (InvokeRequired) BeginInvoke((Action)RefreshList);
                else RefreshList();
            }
            catch { }
        }

        void RefreshList()
        {
            List<SeedRecord> items = _showingFavorites ? SeedStore.GetFavorites() : SeedStore.GetHistory();
            _lv.BeginUpdate();
            _lv.Items.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                SeedRecord r = items[i];
                string summary = r.TraitSummary ?? "";
                if (!string.IsNullOrEmpty(r.PoiSummary))
                    summary = string.IsNullOrEmpty(summary) ? r.PoiSummary : (summary + " | " + r.PoiSummary);
                ListViewItem lvi = new ListViewItem(r.Coordinate);
                lvi.SubItems.Add(r.ClusterName);
                lvi.SubItems.Add(summary);
                lvi.SubItems.Add(r.Note);
                lvi.SubItems.Add(r.Timestamp);
                lvi.Tag = r;
                _lv.Items.Add(lvi);
            }
            _lv.EndUpdate();
            _lblCount.Text = items.Count + (_showingFavorites ? " 条收藏" : " 条历史");
            UpdateButtons();
        }

        SeedRecord SelectedRecord()
        {
            if (_lv.SelectedItems.Count == 0) return null;
            return _lv.SelectedItems[0].Tag as SeedRecord;
        }

        void UpdateButtons()
        {
            SeedRecord r = SelectedRecord();
            bool has = r != null;
            _btnPreview.Enabled = has;
            _btnApply.Enabled = has;
            _btnFav.Enabled = has;
            _btnNote.Enabled = has;
            _btnCopy.Enabled = has;
            _btnDel.Enabled = has && _showingFavorites;
            if (has)
            {
                bool isFav = SeedStore.IsFavorite(r.Coordinate, r.Seed);
                _btnFav.Text = isFav ? "☆ 取消收藏" : "★ 加入收藏";
            }
        }

        void InvokeSelected(bool openPreview)
        {
            SeedRecord r = SelectedRecord();
            if (r == null) return;
            if (_onApply != null) _onApply(r.Clone(), openPreview);
        }

        void ToggleFavSelected()
        {
            SeedRecord r = SelectedRecord();
            if (r == null) return;
            SeedStore.ToggleFavorite(r);
        }

        void CopySelected()
        {
            SeedRecord r = SelectedRecord();
            if (r == null || string.IsNullOrEmpty(r.Coordinate)) return;
            try { Clipboard.SetText(r.Coordinate); } catch { }
        }

        void DeleteSelected()
        {
            SeedRecord r = SelectedRecord();
            if (r == null || !_showingFavorites) return;
            SeedStore.RemoveFavorite(r.Coordinate, r.Seed);
        }

        void EditNoteSelected()
        {
            SeedRecord r = SelectedRecord();
            if (r == null) return;
            using (Form dlg = new Form())
            {
                dlg.Text = "编辑种子备注";
                dlg.ClientSize = new Size(420, 128);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = C(BG);
                dlg.ForeColor = C(TXT);
                dlg.Font = _fontReg;

                TextBox tb = new TextBox
                {
                    Location = new Point(14, 18),
                    Size = new Size(392, 24),
                    BackColor = C(CARD),
                    ForeColor = C(TXT),
                    Text = r.Note ?? ""
                };
                Button ok = MakeBtn("保存", 218, 76, 90, ACC, 0x0E231C, true);
                ok.DialogResult = DialogResult.OK;
                Button cancel = MakeBtn("取消", 316, 76, 90, BTN, TXT, false);
                cancel.DialogResult = DialogResult.Cancel;
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                dlg.Controls.Add(tb);
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);
                DpiHelper.ScaleLayout(dlg);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    SeedStore.UpdateNote(r.Coordinate, r.Seed, tb.Text.Trim());
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SeedStore.StoreChanged -= OnStoreChanged;
            base.OnFormClosed(e);
        }
    }
}
