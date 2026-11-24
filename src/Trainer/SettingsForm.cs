using System;
using System.Drawing;
using System.Windows.Forms;

namespace ONITrainer
{
    public class SettingsForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, TXT = 0xECEEF2, SUB = 0x949AA8;

        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        CheckBox _chkAdvanced;

        public SettingsForm()
        {
            Text = "修改器设置  ·  ONI Trainer";
            ClientSize = new Size(496, 266);
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = C(BG);
            Font = _fontReg;

            BuildUI();
            DpiHelper.ScaleLayout(this);
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void BuildUI()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = C(CARD) };
            top.Controls.Add(new Label
            {
                Text = "功能与界面设置",
                Font = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold),
                ForeColor = C(ACC),
                Location = new Point(16, 10),
                Size = new Size(220, 26)
            });

            GroupBox grp = new GroupBox
            {
                Text = "高级功能开关",
                Location = new Point(16, 56),
                Size = new Size(464, 154),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };

            _chkAdvanced = new CheckBox
            {
                Text = "启用高级功能（高级种子筛选与地图图层）",
                ForeColor = C(TXT),
                Font = _fontBold,
                Location = new Point(16, 28),
                Size = new Size(432, 24),
                Checked = TrainerSettings.AdvancedMode
            };
            grp.Controls.Add(_chkAdvanced);

            grp.Controls.Add(new Label
            {
                Text = "关闭（默认）：保持上一版简洁界面，并在「开局种子筛选器」中保留\r\n" +
                       "  「候选种子交互式地图预览 + 间歇泉 / 火山 / 遗迹清单」。",
                ForeColor = C(SUB),
                Font = _fontReg,
                Location = new Point(20, 58),
                Size = new Size(428, 38)
            });

            grp.Controls.Add(new Label
            {
                Text = "开启：解锁按「间歇泉 / 火山 / 油井 / 遗迹」种类与数量二次批量筛选，\r\n" +
                       "  并在地图预览中开放纯元素视图、温度热力图、分类过滤与导出 PNG。",
                ForeColor = C(0xBFD8CF),
                Font = _fontReg,
                Location = new Point(20, 102),
                Size = new Size(428, 38)
            });

            Button btnSave = new Button
            {
                Text = "保存",
                Location = new Point(284, 222),
                Size = new Size(94, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(ACC),
                ForeColor = C(0x0E231C),
                Font = _fontBold,
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.FlatAppearance.MouseOverBackColor = C(ACC_HOV);
            btnSave.Click += (s, e) =>
            {
                TrainerSettings.SetAdvancedMode(_chkAdvanced.Checked);
                DialogResult = DialogResult.OK;
                Close();
            };

            Button btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(386, 222),
                Size = new Size(94, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(BTN),
                ForeColor = C(TXT),
                Font = _fontBold,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.FlatAppearance.MouseOverBackColor = C(BTN_HOV);
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            Controls.Add(grp);
            Controls.Add(btnSave);
            Controls.Add(btnCancel);
            Controls.Add(top);
        }
    }
}
