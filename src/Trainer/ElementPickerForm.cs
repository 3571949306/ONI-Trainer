using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ONITrainer
{
    public class ElementPickerForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, ACC = 0x00D68F, TXT = 0xECEEF2, SUB = 0x949AA8;
        readonly List<Elem> _all;
        public Elem Selected;
        ComboBox _cat;
        TextBox _search;
        ListBox _list;
        Label _count;

        public ElementPickerForm(List<Elem> all, Elem current)
        {
            _all = all;
            Selected = current;
            Text = "选择元素";
            Size = new Size(520, 594);
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = C(BG);
            Font = new Font("Microsoft YaHei UI", 9F);
            Build();
            DpiHelper.ScaleLayout(this);
            RefreshList();
            if (Selected != null) SelectItem(Selected);
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void Build()
        {
            Controls.Add(new Label { Text = "分类", ForeColor = C(TXT), Location = new Point(20, 22), Size = new Size(40, 24) });
            _cat = new ComboBox
            {
                Location = new Point(62, 20), Size = new Size(150, 26), DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(CARD), ForeColor = C(TXT), FlatStyle = FlatStyle.Flat
            };
            _cat.Items.AddRange(new object[] { "全部", "固体", "液体", "气体" });
            _cat.SelectedIndex = 0;
            _cat.SelectedIndexChanged += (s, e) => RefreshList();
            Controls.Add(_cat);

            Controls.Add(new Label { Text = "搜索", ForeColor = C(TXT), Location = new Point(230, 22), Size = new Size(40, 24) });
            _search = new TextBox { Location = new Point(272, 20), Size = new Size(220, 26), BackColor = C(CARD), ForeColor = C(TXT) };
            _search.TextChanged += (s, e) => RefreshList();
            Controls.Add(_search);

            _list = new ListBox
            {
                Location = new Point(20, 60), Size = new Size(472, 390), BackColor = C(CARD), ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, HorizontalScrollbar = true
            };
            _list.MouseDoubleClick += (s, e) => { if (_list.SelectedItem != null) Confirm(); };
            Controls.Add(_list);

            _count = new Label { Text = "", ForeColor = C(SUB), Location = new Point(20, 462), Size = new Size(224, 22) };
            Controls.Add(_count);

            Button ok = new Button
            {
                Text = "确定", Location = new Point(252, 456), Size = new Size(116, 32), FlatStyle = FlatStyle.Flat,
                BackColor = C(ACC), ForeColor = C(0x0E231C), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += (s, e) => Confirm();
            Controls.Add(ok);

            Button cancel = new Button
            {
                Text = "取消", Location = new Point(376, 456), Size = new Size(116, 32), FlatStyle = FlatStyle.Flat,
                BackColor = C(0x323642), ForeColor = C(TXT), Cursor = Cursors.Hand
            };
            cancel.FlatAppearance.BorderSize = 0;
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            Label hint = new Label
            {
                Text = "选择后回到主界面设置质量/温度，鼠标移到目标格按 F8 或点「在鼠标格刷出」",
                ForeColor = C(SUB), Location = new Point(20, 496), Size = new Size(472, 36)
            };
            Controls.Add(hint);
        }

        static string StateName(int s)
        {
            int b = s & 3;
            if (b == 0) return "真空";
            if (b == 1) return "气体";
            if (b == 2) return "液体";
            return "固体";
        }

        bool MatchCat(Elem el)
        {
            int b = el.State & 3;
            switch (_cat.SelectedIndex)
            {
                case 1: return b == 3;
                case 2: return b == 2;
                case 3: return b == 1;
                default: return true;
            }
        }

        void RefreshList()
        {
            string q = _search.Text.Trim();
            string selName = _list.SelectedItem != null ? ((Elem)_list.SelectedItem).Name : null;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (Elem el in _all)
            {
                if (!MatchCat(el)) continue;
                if (q.Length > 0 && el.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                _list.Items.Add(el);
            }
            _list.EndUpdate();
            _count.Text = "共 " + _list.Items.Count + " 项" + (q.Length > 0 ? "（匹配「" + q + "」）" : "");
            if (_list.Items.Count > 0)
            {
                int idx = 0;
                if (selName != null)
                    for (int i = 0; i < _list.Items.Count; i++)
                        if (((Elem)_list.Items[i]).Name == selName) { idx = i; break; }
                _list.SelectedIndex = idx;
            }
        }

        void SelectItem(Elem el)
        {

            int b = el.State & 3;
            if (b == 3) _cat.SelectedIndex = 1;
            else if (b == 2) _cat.SelectedIndex = 2;
            else if (b == 1) _cat.SelectedIndex = 3;
            else _cat.SelectedIndex = 0;
            RefreshList();
            for (int i = 0; i < _list.Items.Count; i++)
                if (((Elem)_list.Items[i]).Name == el.Name) { _list.SelectedIndex = i; return; }
        }

        void Confirm()
        {
            Elem el = _list.SelectedItem as Elem;
            if (el == null)
            {
                MessageBox.Show(this, "请先从列表中选择一个元素。", "ONI 修改器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Selected = el;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
