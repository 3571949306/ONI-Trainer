using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ONITrainer
{
    public sealed class DupeEditorForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, INPUT = 0x292C36, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, ERR = 0xF25F5C, TXT = 0xECEEF2, SUB = 0x949AA8;

        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        readonly Bridge _bridge;
        readonly Action<string> _log;

        static readonly string[,] _attrDefs = new string[,]
        {
            { "Construction", "建造" },
            { "Digging", "挖掘" },
            { "Machinery", "机械" },
            { "Athletics", "运动" },
            { "Learning", "科学" },
            { "Cooking", "烹饪" },
            { "Caring", "医疗" },
            { "Strength", "力量" },
            { "Art", "艺术" },
            { "Botanist", "农业" },
            { "Ranching", "畜牧" }
        };

        sealed class TraitEntry
        {
            public string Id;
            public string Name;
            public bool Positive;
            public override string ToString()
            {
                return (Positive ? "[+] " : "[-] ") + Name + " (" + Id + ")";
            }
        }

        sealed class DupeRecord
        {
            public int InstId;
            public string Name;
            public string Model;
            public Dictionary<string, int> Attrs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public List<TraitEntry> Traits = new List<TraitEntry>();
            public override string ToString()
            {
                bool bio = !string.IsNullOrEmpty(Model) && Model.IndexOf("Bionic", StringComparison.OrdinalIgnoreCase) >= 0;
                return (string.IsNullOrEmpty(Name) ? ("复制人#" + InstId) : Name) + (bio ? " [仿生]" : "");
            }
        }

        readonly List<DupeRecord> _dupes = new List<DupeRecord>();
        readonly Dictionary<string, NumericUpDown> _numMap = new Dictionary<string, NumericUpDown>(StringComparer.OrdinalIgnoreCase);
        ComboBox _cboDupe;
        ListBox _lstTraits;
        Label _lblStatus;
        Button _btnApplyAttr, _btnMaxAttr, _btnRemoveTrait, _btnRefresh;

        public DupeEditorForm(Bridge bridge, Action<string> log)
        {
            _bridge = bridge;
            _log = log;

            Text = "高级工具 · 复制人属性与特质编辑器";
            ClientSize = new Size(736, 514);
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = C(BG);
            ForeColor = C(TXT);
            Font = _fontReg;

            BuildUI();
            DpiHelper.ScaleLayout(this);
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void BuildUI()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = C(CARD) };
            top.Controls.Add(new Label
            {
                Text = "当前复制人",
                Location = new Point(14, 14),
                Size = new Size(78, 20),
                ForeColor = C(TXT),
                Font = _fontBold
            });

            _cboDupe = new ComboBox
            {
                Location = new Point(96, 11),
                Size = new Size(256, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                FlatStyle = FlatStyle.Flat
            };
            _cboDupe.SelectedIndexChanged += (s, e) => ShowSelectedDupe();
            top.Controls.Add(_cboDupe);

            _btnRefresh = MakeBtn("刷新列表", 364, 10, 90, 28, BTN, TXT, false);
            _btnRefresh.Click += (s, e) => LoadDupes();
            top.Controls.Add(_btnRefresh);

            _lblStatus = new Label
            {
                Text = "读取中…",
                Location = new Point(464, 14),
                Size = new Size(256, 20),
                ForeColor = C(ACC),
                TextAlign = ContentAlignment.MiddleRight
            };
            top.Controls.Add(_lblStatus);

            GroupBox gAttr = new GroupBox
            {
                Text = "工作属性等级（0 ~ 99）",
                Location = new Point(16, 58),
                Size = new Size(372, 396),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };

            int n = _attrDefs.GetLength(0);
            for (int i = 0; i < n; i++)
            {
                int col = i % 2;
                int row = i / 2;
                int x = 14 + col * 176;
                int y = 30 + row * 48;

                gAttr.Controls.Add(new Label
                {
                    Text = _attrDefs[i, 1],
                    Location = new Point(x, y + 4),
                    Size = new Size(56, 22),
                    ForeColor = C(TXT),
                    Font = _fontReg
                });

                NumericUpDown num = new NumericUpDown
                {
                    Location = new Point(x + 60, y),
                    Size = new Size(92, 26),
                    Minimum = 0,
                    Maximum = 99,
                    Value = 0,
                    BackColor = C(INPUT),
                    ForeColor = C(TXT),
                    Font = _fontReg
                };
                _numMap[_attrDefs[i, 0]] = num;
                gAttr.Controls.Add(num);
            }

            _btnApplyAttr = MakeBtn("应用全部属性", 14, 348, 166, 32, ACC, 0x0E231C, true);
            _btnApplyAttr.Click += (s, e) => ApplyAttributes();
            _btnMaxAttr = MakeBtn("一键全部设为 20 级", 190, 348, 166, 32, BTN, TXT, false);
            _btnMaxAttr.Click += (s, e) =>
            {
                foreach (NumericUpDown num in _numMap.Values) num.Value = 20;
            };
            gAttr.Controls.Add(_btnApplyAttr);
            gAttr.Controls.Add(_btnMaxAttr);

            GroupBox gTrait = new GroupBox
            {
                Text = "拥有特质（红色为负面特质，可安全移除）",
                Location = new Point(398, 58),
                Size = new Size(322, 396),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };

            _lstTraits = new ListBox
            {
                Location = new Point(14, 28),
                Size = new Size(294, 308),
                BackColor = C(CARD),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24,
                Font = _fontReg
            };
            _lstTraits.DrawItem += DrawTraitItem;
            gTrait.Controls.Add(_lstTraits);

            _btnRemoveTrait = MakeBtn("移除选中特质", 14, 348, 166, 32, BTN, ERR, true);
            _btnRemoveTrait.Click += (s, e) => RemoveSelectedTrait();
            gTrait.Controls.Add(_btnRemoveTrait);

            Button btnClose = MakeBtn("关闭", 626, 468, 94, 32, BTN, TXT, false);
            btnClose.Click += (s, e) => Close();

            Controls.Add(gAttr);
            Controls.Add(gTrait);
            Controls.Add(btnClose);
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

        void DrawTraitItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _lstTraits.Items.Count) return;
            TraitEntry te = _lstTraits.Items[e.Index] as TraitEntry;
            if (te == null) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush brBg = new SolidBrush(sel ? C(0x2B3748) : C(CARD)))
                e.Graphics.FillRectangle(brBg, e.Bounds);

            Color fg = te.Positive ? C(ACC) : C(ERR);
            TextRenderer.DrawText(e.Graphics, te.ToString(), _fontReg,
                new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 10, e.Bounds.Height),
                fg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            float scale = DpiHelper.GetDpiScale();
            if (scale > 1.01f) _lstTraits.ItemHeight = (int)Math.Round(24 * scale);
            LoadDupes();
        }

        void LoadDupes()
        {
            int prevInstId = (_cboDupe.SelectedItem as DupeRecord) != null ? ((DupeRecord)_cboDupe.SelectedItem).InstId : 0;
            _dupes.Clear();
            _cboDupe.Items.Clear();

            string err = null;
            string resp = _bridge != null ? _bridge.RunHelperCmd("DUPELIST", out err) : null;
            if (string.IsNullOrEmpty(resp))
            {
                _lblStatus.Text = "读取失败：" + (err ?? "无响应");
                _lblStatus.ForeColor = C(ERR);
                return;
            }

            string[] lines = resp.Split('\n');
            int selIdx = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                string[] cols = line.Split('|');
                if (cols.Length < 4) continue;
                int instId;
                if (!int.TryParse(cols[0], out instId) || instId == 0) continue;

                DupeRecord dr = new DupeRecord
                {
                    InstId = instId,
                    Name = cols[1],
                    Model = cols[2]
                };

                string[] attrs = cols[3].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int a = 0; a < attrs.Length; a++)
                {
                    int c = attrs[a].IndexOf(':');
                    if (c <= 0) continue;
                    int lvl;
                    if (int.TryParse(attrs[a].Substring(c + 1), out lvl))
                        dr.Attrs[attrs[a].Substring(0, c)] = lvl;
                }

                if (cols.Length >= 5 && !string.IsNullOrEmpty(cols[4]))
                {
                    string[] trs = cols[4].Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int t = 0; t < trs.Length; t++)
                    {
                        string[] tp = trs[t].Split(':');
                        if (tp.Length < 2) continue;
                        dr.Traits.Add(new TraitEntry
                        {
                            Id = tp[0],
                            Name = tp[1],
                            Positive = tp.Length > 2 && tp[2] == "1"
                        });
                    }
                }

                if (instId == prevInstId) selIdx = _dupes.Count;
                _dupes.Add(dr);
                _cboDupe.Items.Add(dr);
            }

            if (_cboDupe.Items.Count > 0)
            {
                _cboDupe.SelectedIndex = selIdx;
                _lblStatus.Text = "共 " + _dupes.Count + " 名复制人";
                _lblStatus.ForeColor = C(ACC);
            }
            else
            {
                _lblStatus.Text = "当前存档无存活复制人";
                _lblStatus.ForeColor = C(SUB);
            }
        }

        void ShowSelectedDupe()
        {
            DupeRecord dr = _cboDupe.SelectedItem as DupeRecord;
            _lstTraits.Items.Clear();
            if (dr == null) return;
            foreach (KeyValuePair<string, NumericUpDown> kv in _numMap)
            {
                int lvl;
                dr.Attrs.TryGetValue(kv.Key, out lvl);
                if (lvl < 0) lvl = 0;
                if (lvl > 99) lvl = 99;
                kv.Value.Value = lvl;
            }
            for (int i = 0; i < dr.Traits.Count; i++)
                _lstTraits.Items.Add(dr.Traits[i]);
        }

        void ApplyAttributes()
        {
            DupeRecord dr = _cboDupe.SelectedItem as DupeRecord;
            if (dr == null || _bridge == null) return;

            int applied = 0;
            string lastErr = null;
            foreach (KeyValuePair<string, NumericUpDown> kv in _numMap)
            {
                int lvl = (int)kv.Value.Value;
                int old;
                dr.Attrs.TryGetValue(kv.Key, out old);
                if (old == lvl) continue;
                string err;
                string res = _bridge.RunHelperCmd(
                    string.Format(CultureInfo.InvariantCulture, "SETATTR {0} {1} {2}", dr.InstId, kv.Key, lvl),
                    out err);
                if (res != null)
                {
                    dr.Attrs[kv.Key] = lvl;
                    applied++;
                }
                else
                {
                    lastErr = err;
                }
            }

            if (lastErr != null)
            {
                _lblStatus.Text = "部分失败：" + lastErr;
                _lblStatus.ForeColor = C(ERR);
            }
            else
            {
                _lblStatus.Text = applied > 0 ? ("已应用 " + applied + " 项属性变更") : "属性未变化";
                _lblStatus.ForeColor = C(ACC);
                if (applied > 0 && _log != null)
                    _log("[高级] 已修改「" + dr.Name + "」的 " + applied + " 项属性等级");
            }
        }

        void RemoveSelectedTrait()
        {
            DupeRecord dr = _cboDupe.SelectedItem as DupeRecord;
            TraitEntry te = _lstTraits.SelectedItem as TraitEntry;
            if (dr == null || te == null || _bridge == null) return;

            string err;
            string res = _bridge.RunHelperCmd(
                string.Format(CultureInfo.InvariantCulture, "REMOVETRAIT {0} {1}", dr.InstId, te.Id),
                out err);
            if (res != null)
            {
                _lblStatus.Text = "已移除特质：" + te.Name;
                _lblStatus.ForeColor = C(ACC);
                if (_log != null) _log("[高级] 已移除「" + dr.Name + "」的特质：" + te.Name);
                LoadDupes();
            }
            else
            {
                _lblStatus.Text = "移除失败：" + (err ?? "未知错误");
                _lblStatus.ForeColor = C(ERR);
            }
        }
    }
}
