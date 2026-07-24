using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace PowerTray
{
    public class PowerPlan
    {
        public Guid Guid { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
        public override string ToString() => IsActive ? "● " + Name : Name;
    }

    public class MainForm : Form
    {
        private ListBox listBoxPlans;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool closeToTray = true;

        public MainForm()
        {
            // ---------- 加载自定义图标（重点修改部分） ----------
            // 获取程序运行目录（即 exe 所在的文件夹）
            string exePath = Application.StartupPath;
            string iconFilePath = Path.Combine(exePath, "app.ico");

            Icon customIcon;
            if (File.Exists(iconFilePath))
            {
                // 如果找到了 app.ico 文件，就加载它
                customIcon = new Icon(iconFilePath);
            }
            else
            {
                // 如果没找到（比如你忘了复制文件），就降级使用系统盾牌图标，避免程序报错崩溃
                customIcon = SystemIcons.Shield;
            }

            // 设置窗口左上角图标
            this.Icon = customIcon;

            this.Text = "电源计划切换器";
            this.Width = 320;
            this.Height = 280;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;

            // 强制置顶一瞬间，避免窗口跑到后面
            this.TopMost = true;
            this.Shown += (s, e) => { this.TopMost = false; };

            // 列表
            listBoxPlans = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 10F),
                IntegralHeight = false
            };
            listBoxPlans.SelectedIndexChanged += ListBoxPlans_SelectedIndexChanged;
            this.Controls.Add(listBoxPlans);

            // 托盘图标
            trayIcon = new NotifyIcon
            {
                Icon = customIcon,   // 这里使用刚才加载好的自定义图标
                Text = "电源计划切换器",
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => ShowWindow();

            // 托盘右键菜单
            trayMenu = new ContextMenuStrip();
            trayIcon.ContextMenuStrip = trayMenu;

            // 事件绑定
            this.Resize += MainForm_Resize;
            this.FormClosing += MainForm_FormClosing;

            // 加载数据
            RefreshAll();
        }

        private void RefreshAll()
        {
            RefreshList();
            RefreshTrayMenu();
        }

        private void RefreshList()
        {
            var plans = GetPowerPlans();
            listBoxPlans.Items.Clear();
            foreach (var plan in plans)
                listBoxPlans.Items.Add(plan);
        }

        private void RefreshTrayMenu()
        {
            trayMenu.Items.Clear();
            var plans = GetPowerPlans();
            foreach (var plan in plans)
            {
                var item = new ToolStripMenuItem(plan.ToString());
                item.Tag = plan.Guid;
                item.Click += OnTrayItemClick;
                trayMenu.Items.Add(item);
            }
            trayMenu.Items.Add(new ToolStripSeparator());
            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += (s, e) => ExitApplication();
            trayMenu.Items.Add(exitItem);
        }

        // ---------- 获取活动 GUID ----------
        private Guid GetActivePlanGuid()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "-getactivescheme",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    var match = Regex.Match(output, @"([0-9a-fA-F\-]{36})");
                    if (match.Success)
                        return Guid.Parse(match.Groups[1].Value);
                }
            }
            catch { }
            return Guid.Empty;
        }

        private List<PowerPlan> GetPowerPlans()
        {
            var list = new List<PowerPlan>();
            Guid activeGuid = GetActivePlanGuid();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "-list",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var match = Regex.Match(line, @"([0-9a-fA-F\-]{36})\s+\((.+?)\)\s*(?:\[.*\])?");
                        if (match.Success)
                        {
                            var guid = Guid.Parse(match.Groups[1].Value);
                            var name = match.Groups[2].Value.Trim();
                            bool isActive = (guid == activeGuid);
                            list.Add(new PowerPlan { Guid = guid, Name = name, IsActive = isActive });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("获取电源计划失败: " + ex.Message);
            }
            return list;
        }

        private bool SetActivePlan(Guid guid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = $"-setactive {guid}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)?.WaitForExit();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------- 列表点击 ----------
        private void ListBoxPlans_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBoxPlans.SelectedItem is PowerPlan plan)
            {
                if (plan.IsActive)
                {
                    listBoxPlans.SelectedIndex = -1;
                    return;
                }
                if (SetActivePlan(plan.Guid))
                {
                    Thread.Sleep(100);   // 等待系统更新
                    RefreshAll();
                    trayIcon.ShowBalloonTip(1500, "电源计划", $"已切换到：{plan.Name}", ToolTipIcon.Info);
                }
                else
                {
                    MessageBox.Show("切换失败，请以管理员身份运行！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                listBoxPlans.SelectedIndex = -1;
            }
        }

        // ---------- 托盘菜单点击 ----------
        private void OnTrayItemClick(object sender, EventArgs e)
        {
            var item = (ToolStripMenuItem)sender;
            var guid = (Guid)item.Tag;
            var plans = GetPowerPlans();
            var plan = plans.Find(p => p.Guid == guid);
            if (plan != null && plan.IsActive)
            {
                trayIcon.ShowBalloonTip(1000, "提示", "已是当前计划", ToolTipIcon.Info);
                return;
            }
            if (SetActivePlan(guid))
            {
                Thread.Sleep(100);
                RefreshAll();
                trayIcon.ShowBalloonTip(1500, "电源计划", $"已切换到：{plan?.Name ?? "未知"}", ToolTipIcon.Info);
            }
            else
            {
                trayIcon.ShowBalloonTip(2000, "错误", "切换失败，请以管理员身份运行", ToolTipIcon.Error);
            }
        }

        // ---------- 窗口行为 ----------
        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
                HideWindow();
        }

        private void HideWindow()
        {
            this.Hide();
            this.ShowInTaskbar = false;
            trayIcon.Visible = true;
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            trayIcon.Visible = true;
            this.Activate();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (closeToTray)
                {
                    e.Cancel = true;
                    HideWindow();
                    trayIcon.ShowBalloonTip(2000, "电源计划", "已最小化到托盘，右键图标可切换。", ToolTipIcon.Info);
                }
                else
                {
                    ExitApplication();
                }
            }
        }

        private void ExitApplication()
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }

        public void SetCloseToTray(bool value) => closeToTray = value;
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                File.WriteAllText("error.log", ex.ToString());
                MessageBox.Show($"程序启动失败，错误详情已写入 error.log 文件\n{ex.Message}", "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}