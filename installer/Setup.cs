using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace EasyLabel.Setup
{
    /// <summary>
    /// EasyLabel 安装程序。安装包（payload.zip）以资源形式内嵌，单文件分发。
    /// 默认按当前用户安装到 %LOCALAPPDATA%\Programs\EasyLabel，不需要管理员权限；
    /// 手工选到 Program Files 时会自动请求提权，并改为“所有用户”安装。
    /// 参数：/S 静默安装，/uninstall 卸载，/D="目录" 指定安装目录。
    /// </summary>
    internal static class Program
    {
        internal const string UninstallKeyName = "EasyLabel";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = HasFlag(args, "/S") || HasFlag(args, "--silent");
            bool uninstall = HasFlag(args, "/uninstall") || HasFlag(args, "--uninstall");
            bool wipe = HasFlag(args, "--wipe");
            string dir = ArgValue(args, "/D") ?? ArgValue(args, "--dir");

            CleanupTempCopies();

            if (wipe)
            {
                WipeDirectory(dir, ArgInt(args, "--pid"), ArgValue(args, "--data"));
                return;
            }

            if (uninstall)
            {
                Uninstaller.Run(silent, dir);
                return;
            }

            if (silent)
            {
                string target = string.IsNullOrEmpty(dir) ? Installer.DefaultDirectory : dir;
                int code = Installer.Run(target, true, false, false, null);
                Environment.ExitCode = code;
                return;
            }

            Application.Run(new SetupForm(dir));
        }

        internal static bool HasFlag(string[] args, string flag)
        {
            foreach (string arg in args)
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return args[i + 1].Trim('"');
                if (args[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(key.Length + 1).Trim('"');
            }
            return null;
        }

        private static int ArgInt(string[] args, string key)
        {
            int value;
            return int.TryParse(ArgValue(args, key), out value) ? value : -1;
        }

        /// <summary>清理上次卸载/安装留下的临时副本（不删除正在运行的那个）。</summary>
        private static void CleanupTempCopies()
        {
            try
            {
                string self = Path.GetFullPath(Application.ExecutablePath);
                foreach (string file in Directory.GetFiles(Path.GetTempPath(), "EasyLabel-setup-*.exe"))
                {
                    if (string.Equals(Path.GetFullPath(file), self, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        /// <summary>等父进程退出后删除整个安装目录（卸载的最后一步，由临时副本执行）。</summary>
        private static void WipeDirectory(string dir, int pid, string dataDir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (pid > 0)
            {
                try
                {
                    Process parent = Process.GetProcessById(pid);
                    parent.WaitForExit(60000);
                }
                catch { }
                System.Threading.Thread.Sleep(500);
            }

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    break;
                }
                catch
                {
                    System.Threading.Thread.Sleep(400 * attempt);
                }
            }

            if (!string.IsNullOrEmpty(dataDir))
            {
                try { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); } catch { }
            }

            try { File.Delete(Application.ExecutablePath); } catch { }
        }
    }

    internal static class Installer
    {
        internal const string PayloadResource = "payload.zip";

        internal static string DefaultDirectory
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "Programs", "EasyLabel");
            }
        }

        internal static string DataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyLabel"); }
        }

        internal static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        internal static bool IsMachineWide(string dir)
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string full = Path.GetFullPath(dir);
                return (!string.IsNullOrEmpty(programFiles) && full.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) ||
                       (!string.IsNullOrEmpty(programFilesX86) && full.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>返回 0 成功，其他为失败。</summary>
        internal static int Run(string dir, bool silent, bool desktopShortcut, bool launchAfter, IWin32Window owner)
        {
            try
            {
                dir = Path.GetFullPath(dir.TrimEnd('\\'));
            }
            catch
            {
                Fail(owner, "安装目录无效：" + dir);
                return 2;
            }

            if (!IsWritable(dir) && !IsElevated())
            {
                // 目标目录需要管理员权限：以提升权限重新启动自己。
                try
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                        "/elevated /D=\"" + dir + "\"")
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                    return 0;
                }
                catch (Exception ex)
                {
                    Fail(owner, "没有权限写入 " + dir + "，请换一个目录（如默认的当前用户目录）。\r\n\r\n" + ex.Message);
                    return 3;
                }
            }

            bool machineWide = IsMachineWide(dir) && IsElevated();
            string version = SetupInfo.Version;

            try
            {
                Directory.CreateDirectory(dir);

                // 1. 展开内嵌的安装包
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
                {
                    if (stream == null)
                    {
                        Fail(owner, "安装包损坏：缺少内嵌文件。请重新下载安装程序。");
                        return 4;
                    }
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue; // 目录项
                            string target = Path.Combine(dir, entry.FullName.Replace('/', '\\'));
                            string parent = Path.GetDirectoryName(target);
                            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                            entry.ExtractToFile(target, true);
                        }
                    }
                }

                // 2. 安装版不写 portable.flag：设置与模版存 %APPDATA%\EasyLabel
                string flag = Path.Combine(dir, "portable.flag");
                if (File.Exists(flag))
                {
                    try { File.Delete(flag); } catch { }
                }

                // 3. 卸载程序 = 安装程序自身的一份副本
                string uninstaller = Path.Combine(dir, "Uninstall.exe");
                try
                {
                    File.Copy(Application.ExecutablePath, uninstaller, true);
                }
                catch (Exception ex)
                {
                    Fail(owner, "无法写入卸载程序：" + ex.Message);
                    return 5;
                }

                // 4. 快捷方式
                string startMenu = machineWide
                    ? Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
                    : Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string group = Path.Combine(startMenu, "EasyLabel");
                Directory.CreateDirectory(group);
                Shortcut.Create(Path.Combine(group, "EasyLabel 标签轻松打.lnk"),
                    Path.Combine(dir, "EasyLabel.exe"), dir, "EasyLabel 标签轻松打", Path.Combine(dir, "EasyLabel.exe"));
                Shortcut.Create(Path.Combine(group, "卸载 EasyLabel.lnk"),
                    uninstaller, dir, "卸载 EasyLabel 标签轻松打", uninstaller);

                if (desktopShortcut)
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    try
                    {
                        Shortcut.Create(Path.Combine(desktop, "EasyLabel 标签轻松打.lnk"),
                            Path.Combine(dir, "EasyLabel.exe"), dir, "EasyLabel 标签轻松打",
                            Path.Combine(dir, "EasyLabel.exe"));
                    }
                    catch { }
                }

                // 5. 注册卸载信息，让“应用和功能”里能看到并卸载
                WriteUninstallEntry(dir, uninstaller, version, machineWide);

                if (launchAfter && !silent)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(Path.Combine(dir, "EasyLabel.exe")) { WorkingDirectory = dir });
                    }
                    catch { }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Fail(owner, "安装失败：\r\n" + ex.Message);
                return 6;
            }
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                Directory.CreateDirectory(dir);
                using (var stream = File.Create(probe)) { }
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        private static void WriteUninstallEntry(string dir, string uninstaller, string version, bool machineWide)
        {
            RegistryKey root = machineWide ? Registry.LocalMachine : Registry.CurrentUser;
            using (RegistryKey key = root.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Program.UninstallKeyName))
            {
                if (key == null) return;
                key.SetValue("DisplayName", SetupInfo.ProductName);
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", SetupInfo.Publisher);
                key.SetValue("InstallLocation", dir);
                key.SetValue("DisplayIcon", Path.Combine(dir, "EasyLabel.exe") + ",0");
                key.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /S");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("URLInfoAbout", SetupInfo.ProjectUrl);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", EstimateSizeKb(dir), RegistryValueKind.DWord);
            }
        }

        internal static int EstimateSizeKb(string dir)
        {
            try
            {
                long total = 0;
                foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }
                return (int)Math.Max(1, total / 1024);
            }
            catch { return 1; }
        }

        internal static void Fail(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message, "EasyLabel 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal static class Uninstaller
    {
        internal static void Run(bool silent, string dir)
        {
            string target = dir;
            bool machineWide = false;
            if (string.IsNullOrEmpty(target))
            {
                target = ReadInstallLocation(Registry.CurrentUser, out machineWide) ??
                         ReadInstallLocation(Registry.LocalMachine, out machineWide);
            }
            if (string.IsNullOrEmpty(target))
            {
                target = Installer.DefaultDirectory;
            }

            bool keepData = true;
            if (!silent)
            {
                DialogResult answer = MessageBox.Show(
                    "确定卸载 " + SetupInfo.ProductName + " 吗？\r\n\r\n" +
                    "· “是”：删除程序，保留设置与模版（" + Installer.DataDirectory + "）\r\n" +
                    "· “否”：删除程序，同时删除设置与模版\r\n" +
                    "· “取消”：不做任何改动",
                    "卸载 EasyLabel", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                keepData = answer == DialogResult.Yes;

                if (File.Exists(Path.Combine(target, "EasyLabel.exe")) && IsRunning())
                {
                    DialogResult close = MessageBox.Show(
                        "EasyLabel 正在运行。请先关闭程序再卸载。\r\n\r\n是否现在关闭并继续？",
                        "卸载 EasyLabel", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (close != DialogResult.Yes) return;
                    KillRunning();
                }
            }
            else
            {
                KillRunning();
            }

            // 1. 快捷方式
            RemoveShortcuts();

            // 2. 注册表
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Program.UninstallKeyName, false); }
            catch { }
            if (Installer.IsElevated())
            {
                try { Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Program.UninstallKeyName, false); }
                catch { }
            }

            // 3. 程序文件：先删目录内容，剩下的自己交给临时副本来删
            try
            {
                string self = Path.GetFullPath(Application.ExecutablePath);
                foreach (string file in Directory.GetFiles(target, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFullPath(file), self, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(file); } catch { }
                }
                foreach (string folder in Directory.GetDirectories(target, "*", SearchOption.AllDirectories))
                {
                    try { Directory.Delete(folder, false); } catch { }
                }
            }
            catch { }

            string dataDir = keepData ? "" : Installer.DataDirectory;
            try
            {
                string tempCopy = Path.Combine(Path.GetTempPath(),
                    "EasyLabel-setup-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Application.ExecutablePath, tempCopy, true);
                var psi = new ProcessStartInfo(tempCopy)
                {
                    UseShellExecute = false,
                    Arguments = "--wipe --dir \"" + target + "\" --pid " + Process.GetCurrentProcess().Id +
                                (keepData ? "" : " --data \"" + dataDir + "\"")
                };
                Process.Start(psi);
                return; // 由临时副本完成删除并退出
            }
            catch { }

            try { Directory.Delete(target, true); } catch { }
        }

        private static string ReadInstallLocation(RegistryKey root, out bool machineWide)
        {
            machineWide = false;
            try
            {
                using (RegistryKey key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Program.UninstallKeyName))
                {
                    if (key == null) return null;
                    machineWide = ReferenceEquals(root, Registry.LocalMachine);
                    string dir = Convert.ToString(key.GetValue("InstallLocation"));
                    return string.IsNullOrWhiteSpace(dir) ? null : dir;
                }
            }
            catch { return null; }
        }

        private static void RemoveShortcuts()
        {
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            };
            foreach (string root in roots)
            {
                string group = Path.Combine(root, "EasyLabel");
                try { if (Directory.Exists(group)) Directory.Delete(group, true); } catch { }
            }
            try
            {
                string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "EasyLabel 标签轻松打.lnk");
                if (File.Exists(desktop)) File.Delete(desktop);
            }
            catch { }
        }

        private static bool IsRunning()
        {
            try { return Process.GetProcessesByName("EasyLabel").Length > 0; }
            catch { return false; }
        }

        private static void KillRunning()
        {
            foreach (Process process in Process.GetProcessesByName("EasyLabel"))
            {
                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(4000)) process.Kill();
                }
                catch { }
            }
        }
    }

    internal static class Shortcut
    {
        /// <summary>通过 WScript.Shell 生成 .lnk，避免依赖 COM 互操作程序集。</summary>
        internal static void Create(string linkPath, string targetPath, string workingDirectory, string description, string iconPath)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { linkPath });
                if (shortcut == null) return;

                Type linkType = shortcut.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                if (!string.IsNullOrEmpty(iconPath))
                    linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut,
                        new object[] { iconPath + ",0" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, new object[0]);
            }
            catch
            {
                // 快捷方式失败不影响安装本身。
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox _dirBox;
        private readonly CheckBox _desktop;
        private readonly CheckBox _launch;
        private readonly ProgressBar _bar;
        private readonly Label _status;
        private readonly Button _install;
        private readonly Button _cancel;
        private bool _working;

        internal SetupForm(string initialDir)
        {
            Text = SetupInfo.ProductName + " 安装程序";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            ClientSize = new Size(560, 372);
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label
            {
                Text = SetupInfo.ProductName,
                AutoSize = true,
                Location = new Point(24, 18),
                Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55)
            };
            var subtitle = new Label
            {
                Text = "版本 " + SetupInfo.Version + " · 安装程序 · " + (Installer.IsElevated() ? "管理员" : "当前用户"),
                AutoSize = true,
                Location = new Point(26, 54),
                ForeColor = Color.FromArgb(107, 114, 128)
            };
            var line = new Panel { Location = new Point(0, 84), Size = new Size(560, 1), BackColor = Color.FromArgb(226, 230, 236) };

            var dirLabel = new Label { Text = "安装目录", AutoSize = true, Location = new Point(24, 106), ForeColor = Color.FromArgb(107, 114, 128) };
            _dirBox = new TextBox
            {
                Location = new Point(98, 102),
                Size = new Size(346, 26),
                Text = string.IsNullOrEmpty(initialDir) ? Installer.DefaultDirectory : initialDir
            };
            var browse = new Button
            {
                Text = "浏览…",
                Location = new Point(452, 101),
                Size = new Size(84, 27),
                FlatStyle = FlatStyle.System
            };
            browse.Click += (s, e) => Browse();

            _desktop = new CheckBox
            {
                Text = "创建桌面快捷方式",
                Location = new Point(98, 142),
                AutoSize = true,
                Checked = true
            };
            _launch = new CheckBox
            {
                Text = "安装完成后启动 EasyLabel",
                Location = new Point(98, 168),
                AutoSize = true,
                Checked = true
            };

            var hint = new Label
            {
                Text = "安装到当前用户目录不需要管理员权限；设置与模版保存在 " + Installer.DataDirectory + "，卸载时默认保留。\r\n" +
                       "如果填 Program Files，安装程序会自动请求管理员权限并改为“所有用户”安装。",
                Location = new Point(24, 204),
                Size = new Size(512, 46),
                ForeColor = Color.FromArgb(107, 114, 128)
            };

            _bar = new ProgressBar
            {
                Location = new Point(24, 262),
                Size = new Size(512, 14),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Visible = false
            };
            _status = new Label
            {
                Text = "",
                Location = new Point(24, 284),
                Size = new Size(512, 24),
                ForeColor = Color.FromArgb(107, 114, 128)
            };

            _install = new Button
            {
                Text = "安装",
                Location = new Point(348, 320),
                Size = new Size(92, 34),
                FlatStyle = FlatStyle.System
            };
            _install.Click += (s, e) => Install();
            _cancel = new Button
            {
                Text = "取消",
                Location = new Point(446, 320),
                Size = new Size(90, 34),
                FlatStyle = FlatStyle.System
            };
            _cancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[]
            {
                title, subtitle, line, dirLabel, _dirBox, browse, _desktop, _launch, hint,
                _bar, _status, _install, _cancel
            });
            AcceptButton = _install;
            CancelButton = _cancel;

            FormClosing += (s, e) =>
            {
                if (_working) e.Cancel = true;
            };
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择 EasyLabel 安装目录", SelectedPath = _dirBox.Text })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) _dirBox.Text = dialog.SelectedPath;
            }
        }

        private void Install()
        {
            string dir = (_dirBox.Text ?? "").Trim();
            if (dir.Length == 0)
            {
                MessageBox.Show(this, "请填写安装目录。", "安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "EasyLabel.exe")) && IsRunning())
            {
                MessageBox.Show(this, "该目录里的 EasyLabel 正在运行，请先关闭程序再安装。", "安装",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _working = true;
            _install.Enabled = false;
            _cancel.Enabled = false;
            _dirBox.Enabled = false;
            _bar.Visible = true;
            _status.Text = "正在安装…";
            UseWaitCursor = true;
            Application.DoEvents();

            int code = Installer.Run(dir, false, _desktop.Checked, _launch.Checked, this);

            _working = false;
            UseWaitCursor = false;
            _bar.Visible = false;

            if (code != 0)
            {
                _install.Enabled = true;
                _cancel.Enabled = true;
                _dirBox.Enabled = true;
                _status.Text = "安装未完成。";
                return;
            }

            _status.Text = "安装完成。";
            Close();
        }

        private static bool IsRunning()
        {
            try { return Process.GetProcessesByName("EasyLabel").Length > 0; }
            catch { return false; }
        }
    }
}
