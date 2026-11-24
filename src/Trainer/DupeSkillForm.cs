using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ONITrainer
{
    public class DupeSkillForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, ACC = 0x00D68F, TXT = 0xECEEF2, SUB = 0x949AA8;
        readonly List<DupeInfo> _all;
        readonly Action<DupeInfo, int> _apply;
        TextBox _search;
        ListBox _list;
        NumericUpDown _amount;

        public DupeSkillForm(List<DupeInfo> dupes, Action<DupeInfo, int> apply)
        {
            _all = dupes;
            _apply = apply;
            Text = "指定复制人技能点";
            Size = new Size(500, 460);
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
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void Build()
        {
            Controls.Add(new Label { Text = "搜索名字", ForeColor = C(TXT), Location = new Point(20, 20), Size = new Size(70, 24) });
            _search = new TextBox { Location = new Point(92, 18), Size = new Size(370, 26), BackColor = C(CARD), ForeColor = C(TXT) };
            _search.TextChanged += (s, e) => RefreshList();
            Controls.Add(_search);

            _list = new ListBox
            {
                Location = new Point(20, 60), Size = new Size(442, 240), BackColor = C(CARD), ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false
            };
            Controls.Add(_list);

            Controls.Add(new Label { Text = "技能点数量", ForeColor = C(TXT), Location = new Point(20, 320), Size = new Size(90, 24) });
            _amount = new NumericUpDown
            {
                Location = new Point(112, 318), Size = new Size(80, 26), Minimum = 1, Maximum = 100, Value = 1,
                BackColor = C(CARD), ForeColor = C(TXT)
            };
            Controls.Add(_amount);

            Button apply = new Button
            {
                Text = "添加技能点", Location = new Point(220, 314), Size = new Size(120, 34), FlatStyle = FlatStyle.Flat,
                BackColor = C(ACC), ForeColor = C(0x10241E), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            apply.FlatAppearance.BorderSize = 0;
            apply.Click += (s, e) => Apply();
            Controls.Add(apply);

            Button close = new Button
            {
                Text = "关闭", Location = new Point(350, 314), Size = new Size(110, 34), FlatStyle = FlatStyle.Flat,
                BackColor = C(0x35353D), ForeColor = C(TXT)
            };
            close.FlatAppearance.BorderSize = 0;
            close.Click += (s, e) => Close();
            Controls.Add(close);

            Controls.Add(new Label
            {
                Text = "支持普通复制人和仿生人；默认 +1，可调 1–100；每人上限 50 点。",
                ForeColor = C(SUB), Location = new Point(20, 370), Size = new Size(440, 22)
            });
        }

        void RefreshList()
        {
            string q = _search.Text.Trim();
            string sel = _list.SelectedItem != null ? ((DupeInfo)_list.SelectedItem).Name : null;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (DupeInfo d in _all)
                if (q.Length == 0 || d.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    _list.Items.Add(d);
            _list.EndUpdate();
            if (_list.Items.Count > 0)
            {
                int idx = 0;
                if (sel != null)
                    for (int i = 0; i < _list.Items.Count; i++)
                        if (((DupeInfo)_list.Items[i]).Name == sel) { idx = i; break; }
                _list.SelectedIndex = idx;
            }
        }

        void Apply()
        {
            DupeInfo d = _list.SelectedItem as DupeInfo;
            if (d == null)
            {
                MessageBox.Show(this, "请先选择一个复制人。", "ONI 修改器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _apply(d, (int)_amount.Value);
            RefreshList();
        }
    }
}
