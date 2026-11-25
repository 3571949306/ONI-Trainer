using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ONITrainer
{
    public class SettingsForm : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, WARN = 0xF5A623, TXT = 0xECEEF2, SUB = 0x949AA8;

        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        readonly Action _onRequestAppExit;

        CheckBox _chkAdvanced;
        CheckBox _chkEnableHotkeys;
        CheckBox _chkToast;
        CheckBox _chkBackup;
        CheckBox _chkAutoUpdate;
        Button _btnCheckUpdate;
        volatile bool _checkingUpdate;

        readonly int[] _hotkeys;
        readonly Button[] _hkButtons = new Button[6];
        int _capturingIndex = -1;

        static readonly string[] _hkNames = new string[]
        {
            "一键解锁全部科技：",
            "全部复制人 +1 技能点：",
            "全员满级（全部精通）：",
            "鼠标位置刷资源：",
            "解锁选中科技前置路径：",
            "打开单个复制人技能编辑器："
        };

        public SettingsForm(Action onRequestAppExit = null)
        {
            _onRequestAppExit = onRequestAppExit;
            _hotkeys = TrainerSettings.GetHotkeys();

            Text = "设置与关于  ·  ONI Trainer";
            ClientSize = new Size(560, 646);
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            BackColor = C(BG);
            Font = _fontReg;

            BuildUI();
            DpiHelper.ScaleLayout(this);
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void BuildUI()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = C(CARD) };
            top.Controls.Add(new Label
            {
                Text = "功能、更新与关于",
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = C(ACC),
                Location = new Point(16, 9),
                Size = new Size(280, 26)
            });


            GroupBox grpAdv = new GroupBox
            {
                Text = "01  高级功能开关",
                Location = new Point(16, 50),
                Size = new Size(528, 120),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };

            _chkAdvanced = new CheckBox
            {
                Text = "启用高级功能（高阶存档修改 + 高级种子筛选 + 地图图层）",
                ForeColor = C(TXT),
                Font = _fontBold,
                Location = new Point(14, 22),
                Size = new Size(498, 22),
                Checked = TrainerSettings.AdvancedMode
            };
            grpAdv.Controls.Add(_chkAdvanced);

            grpAdv.Controls.Add(new Label
            {
                Text = "关闭（默认）：保持简洁主界面，保留「候选种子交互式地图预览 + 间歇泉/火山/遗迹清单」。",
                ForeColor = C(SUB),
                Font = _fontReg,
                Location = new Point(18, 48),
                Size = new Size(494, 20)
            });

            grpAdv.Controls.Add(new Label
            {
                Text = "开启：解锁主界面「07 高级功能」面板（极速建造挖掘/生存状态锁/格子控温与抽真空/\r\n" +
                       "  复制人与间歇泉火山编辑）、多进程高级种子筛选及纯元素/温度图层。",
                ForeColor = C(0xBFD8CF),
                Font = _fontReg,
                Location = new Point(18, 70),
                Size = new Size(494, 42)
            });


            GroupBox grpHk = new GroupBox
            {
                Text = "02  全局快捷键",
                Location = new Point(16, 178),
                Size = new Size(528, 198),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };

            _chkEnableHotkeys = new CheckBox
            {
                Text = "启用全局快捷键",
                ForeColor = C(TXT),
                Font = _fontBold,
                Location = new Point(14, 22),
                Size = new Size(160, 22),
                Checked = TrainerSettings.EnableHotkeys
            };
            _chkEnableHotkeys.CheckedChanged += (s, e) =>
            {
                CancelCapture();
                UpdateHotkeyButtonStates();
            };
            grpHk.Controls.Add(_chkEnableHotkeys);

            grpHk.Controls.Add(new Label
            {
                Text = "点击按钮后按下新键；Delete/Backspace 禁用，Esc 取消",
                ForeColor = C(SUB),
                Font = new Font("Microsoft YaHei UI", 8.25F),
                Location = new Point(180, 24),
                Size = new Size(334, 20)
            });

            for (int i = 0; i < 6; i++)
            {
                int col = i % 2;
                int row = i / 2;
                int x = 14 + col * 256;
                int y = 50 + row * 34;

                grpHk.Controls.Add(new Label
                {
                    Text = _hkNames[i],
                    ForeColor = C(TXT),
                    Font = _fontReg,
                    Location = new Point(x, y + 5),
                    Size = new Size(156, 20)
                });

                int idx = i;
                Button b = new Button
                {
                    Location = new Point(x + 158, y),
                    Size = new Size(88, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = C(BTN),
                    ForeColor = C(TXT),
                    Font = _fontBold,
                    Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = C(BTN_HOV);
                b.Click += (s, e) => BeginCapture(idx);
                _hkButtons[i] = b;
                grpHk.Controls.Add(b);
            }

            Button btnResetHk = new Button
            {
                Text = "恢复默认 (F5 - F10)",
                Location = new Point(14, 158),
                Size = new Size(164, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(BTN),
                ForeColor = C(TXT),
                Font = _fontReg,
                Cursor = Cursors.Hand
            };
            btnResetHk.FlatAppearance.BorderSize = 0;
            btnResetHk.FlatAppearance.MouseOverBackColor = C(BTN_HOV);
            btnResetHk.Click += (s, e) =>
            {
                CancelCapture();
                for (int i = 0; i < 6; i++)
                    _hotkeys[i] = TrainerSettings.DefaultHotkeys[i];
                RefreshHotkeyButtons();
            };
            grpHk.Controls.Add(btnResetHk);


            GroupBox grpUx = new GroupBox
            {
                Text = "03  实用体验、存档保护与更新",
                Location = new Point(16, 384),
                Size = new Size(528, 120),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };

            _chkToast = new CheckBox
            {
                Text = "游戏内浮动提示（使用快捷键或一键功能时在屏幕中弹窗提醒）",
                ForeColor = C(TXT),
                Font = _fontReg,
                Location = new Point(14, 22),
                Size = new Size(500, 22),
                Checked = TrainerSettings.ShowInGameToast
            };
            grpUx.Controls.Add(_chkToast);

            _chkBackup = new CheckBox
            {
                Text = "每次加载存档首次执行修改前，自动备份当前 .sav 为 .bak",
                ForeColor = C(TXT),
                Font = _fontReg,
                Location = new Point(14, 50),
                Size = new Size(500, 22),
                Checked = TrainerSettings.AutoBackupSave
            };
            grpUx.Controls.Add(_chkBackup);

            _chkAutoUpdate = new CheckBox
            {
                Text = "启动时自动检查 GitHub 发布更新（默认关闭）",
                ForeColor = C(TXT),
                Font = _fontReg,
                Location = new Point(14, 80),
                Size = new Size(372, 22),
                Checked = TrainerSettings.AutoCheckUpdate
            };
            grpUx.Controls.Add(_chkAutoUpdate);

            _btnCheckUpdate = new Button
            {
                Text = "立即检查更新",
                Location = new Point(396, 77),
                Size = new Size(118, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(BTN),
                ForeColor = C(TXT),
                Font = _fontReg,
                Cursor = Cursors.Hand
            };
            _btnCheckUpdate.FlatAppearance.BorderSize = 0;
            _btnCheckUpdate.FlatAppearance.MouseOverBackColor = C(BTN_HOV);
            _btnCheckUpdate.Click += (s, e) => CheckUpdateManual();
            grpUx.Controls.Add(_btnCheckUpdate);


            GroupBox grpAbout = new GroupBox
            {
                Text = "04  关于",
                Location = new Point(16, 512),
                Size = new Size(528, 78),
                ForeColor = C(ACC),
                BackColor = C(BG),
                Font = _fontBold
            };

            grpAbout.Controls.Add(new Label
            {
                Text = "ONI 修改器  " + AppUpdater.GetCurrentVersionDisplay() + "  ·  独立单文件修改器（无需安装、不改游戏文件）",
                ForeColor = C(TXT),
                Font = _fontReg,
                Location = new Point(14, 22),
                Size = new Size(500, 20)
            });

            grpAbout.Controls.Add(new Label
            {
                Text = "开源仓库：",
                ForeColor = C(SUB),
                Font = _fontReg,
                Location = new Point(14, 48),
                Size = new Size(68, 20)
            });

            LinkLabel lnkRepo = new LinkLabel
            {
                Text = AppUpdater.RepoUrl,
                Location = new Point(82, 48),
                Size = new Size(432, 20),
                Font = _fontReg,
                LinkColor = C(ACC),
                ActiveLinkColor = C(ACC_HOV),
                VisitedLinkColor = C(ACC),
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            lnkRepo.LinkClicked += (s, e) => AppUpdater.OpenUrl(AppUpdater.RepoUrl);
            grpAbout.Controls.Add(lnkRepo);


            Button btnOpenLog = new Button
            {
                Text = "打开日志目录",
                Location = new Point(16, 602),
                Size = new Size(124, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(BTN),
                ForeColor = C(TXT),
                Font = _fontReg,
                Cursor = Cursors.Hand
            };
            btnOpenLog.FlatAppearance.BorderSize = 0;
            btnOpenLog.FlatAppearance.MouseOverBackColor = C(BTN_HOV);
            btnOpenLog.Click += (s, e) => OpenLogDir();

            Button btnSave = new Button
            {
                Text = "保存",
                Location = new Point(348, 602),
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
                CancelCapture();
                TrainerSettings.SaveAll(
                    _chkAdvanced.Checked,
                    _chkEnableHotkeys.Checked,
                    _hotkeys,
                    _chkToast.Checked,
                    _chkBackup.Checked,
                    _chkAutoUpdate.Checked);
                DialogResult = DialogResult.OK;
                Close();
            };

            Button btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(450, 602),
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

            Controls.Add(grpAdv);
            Controls.Add(grpHk);
            Controls.Add(grpUx);
            Controls.Add(grpAbout);
            Controls.Add(btnOpenLog);
            Controls.Add(btnSave);
            Controls.Add(btnCancel);
            Controls.Add(top);

            RefreshHotkeyButtons();
            UpdateHotkeyButtonStates();
        }

        void CheckUpdateManual()
        {
            if (_checkingUpdate) return;
            _checkingUpdate = true;
            _btnCheckUpdate.Enabled = false;
            _btnCheckUpdate.Text = "检查中…";

            Thread t = new Thread(() =>
            {
                UpdateCheckResult res = AppUpdater.CheckLatest(10000);
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed) return;
                        _checkingUpdate = false;
                        _btnCheckUpdate.Enabled = true;
                        _btnCheckUpdate.Text = "立即检查更新";

                        if (!res.Success)
                        {
                            DialogResult ans = MessageBox.Show(
                                this,
                                (res.Error ?? "检查更新失败。") + "\r\n\r\n是否打开 GitHub 发布页手动查看？",
                                "检查更新",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                            if (ans == DialogResult.Yes)
                                AppUpdater.OpenUrl(res.HtmlUrl ?? AppUpdater.ReleasesUrl);
                            return;
                        }

                        if (!res.HasUpdate)
                        {
                            MessageBox.Show(
                                this,
                                "当前已是最新版本！\r\n当前版本：" + AppUpdater.FormatVersion(res.CurrentVersion) +
                                "\r\n仓库最新：" + (res.LatestTag ?? ""),
                                "检查更新",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                            return;
                        }

                        using (UpdateDialog dlg = new UpdateDialog(res, TriggerExitForUpdate))
                        {
                            dlg.ShowDialog(this);
                            if (dlg.UpdateStarted)
                                Close();
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void TriggerExitForUpdate()
        {
            DialogResult = DialogResult.Cancel;
            if (_onRequestAppExit != null)
                _onRequestAppExit();
        }

        void BeginCapture(int idx)
        {
            if (!_chkEnableHotkeys.Checked) return;
            _capturingIndex = idx;
            RefreshHotkeyButtons();
        }

        void CancelCapture()
        {
            if (_capturingIndex >= 0)
            {
                _capturingIndex = -1;
                RefreshHotkeyButtons();
            }
        }

        void RefreshHotkeyButtons()
        {
            for (int i = 0; i < 6; i++)
            {
                Button b = _hkButtons[i];
                if (b == null) continue;
                if (i == _capturingIndex)
                {
                    b.Text = "请按键…";
                    b.BackColor = C(WARN);
                    b.ForeColor = C(0x1E1400);
                }
                else
                {
                    b.Text = TrainerSettings.FormatVkName(_hotkeys[i]);
                    b.BackColor = C(BTN);
                    b.ForeColor = _hotkeys[i] > 0 ? C(TXT) : C(SUB);
                }
            }
        }

        void UpdateHotkeyButtonStates()
        {
            bool en = _chkEnableHotkeys.Checked;
            for (int i = 0; i < 6; i++)
            {
                if (_hkButtons[i] != null) _hkButtons[i].Enabled = en;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_capturingIndex >= 0)
            {
                Keys key = keyData & Keys.KeyCode;
                if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu ||
                    key == Keys.LShiftKey || key == Keys.RShiftKey ||
                    key == Keys.LControlKey || key == Keys.RControlKey ||
                    key == Keys.LMenu || key == Keys.RMenu ||
                    key == Keys.LWin || key == Keys.RWin)
                {
                    return true;
                }
                if (key == Keys.Escape)
                {
                    CancelCapture();
                    return true;
                }
                if (key == Keys.Delete || key == Keys.Back)
                {
                    _hotkeys[_capturingIndex] = 0;
                    _capturingIndex = -1;
                    RefreshHotkeyButtons();
                    return true;
                }
                int vk = (int)key;
                if (vk > 0 && vk <= 254)
                {
                    _hotkeys[_capturingIndex] = vk;
                    _capturingIndex = -1;
                    RefreshHotkeyButtons();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void OpenLogDir()
        {
            try
            {
                string dir = Bridge.RuntimeDir;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开日志目录：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
