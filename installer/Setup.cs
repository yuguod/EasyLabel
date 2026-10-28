using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            bool autoStart = HasFlag(args, "--autostart");
            string dir = ArgValue(args, "/D") ?? ArgValue(args, "--dir");
            bool desktop = ArgFlag(args, "--desktop", true);
            bool launch = ArgFlag(args, "--launch", true);

            CleanupTempCopies();

            if (wipe)
            {
                WipeDirectory(dir, ArgInt(args, "--pid"), ArgValue(args, "--data"));
                return;
            }

            if (uninstall)
            {
                if (silent)
                {
                    if (Uninstaller.IsRunning()) Uninstaller.KillRunning();
                    Uninstaller.Run(dir, true, false);
                    return;
                }
                Application.Run(new UninstallForm(dir));
                return;
            }

            if (silent)
            {
                string target = string.IsNullOrEmpty(dir) ? Installer.DefaultDirectory : dir;
                Environment.ExitCode = Installer.Run(target, true, desktop, null);
                return;
            }

            Ui.Scale = Ui.DetectScale(SetupForm.DesignWidth, SetupForm.DesignHeight);
            Application.Run(new SetupForm(dir, desktop, launch, autoStart));
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

        /// <summary>读取 --flag 1 / --flag 0，缺省时用 fallback。</summary>
        internal static bool ArgFlag(string[] args, string key, bool fallback)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string value = null;
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    value = args[i + 1];
                else if (args[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    value = args[i].Substring(key.Length + 1);
                if (value != null)
                {
                    value = value.Trim('"').Trim();
                    return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                }
            }
            return fallback;
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

        /// <summary>目标目录需要提权才能写入，且当前进程没有管理员权限。</summary>
        internal static bool NeedsElevation(string dir)
        {
            if (IsElevated()) return false;
            return !IsWritable(dir);
        }

        /// <summary>以管理员身份重启自己，并带上用户已选的选项。</summary>
        internal static bool RelaunchElevated(string dir, bool desktop, bool launch, bool silent)
        {
            try
            {
                string arguments = "/elevated --autostart /D=\"" + dir + "\"" +
                                   " --desktop " + (desktop ? "1" : "0") +
                                   " --launch " + (launch ? "1" : "0");
                if (silent) arguments = "/S " + arguments;
                var info = new ProcessStartInfo(Application.ExecutablePath, arguments)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath)
                };
                Process.Start(info);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>返回 0 成功，其他为失败。report 参数为百分比与状态文字。</summary>
        internal static int Run(string dir, bool silent, bool desktopShortcut, Action<int, string> report)
        {
            try
            {
                dir = Path.GetFullPath(dir.TrimEnd('\\'));
            }
            catch
            {
                Fail(silent, "安装目录无效：" + dir);
                return 2;
            }

            if (!IsWritable(dir) && !IsElevated())
            {
                // 静默安装也要能提权，否则 Program Files 场景下会静默失败。
                if (RelaunchElevated(dir, desktopShortcut, false, silent)) return 0;
                Fail(silent, "没有权限写入 " + dir + "，请换一个目录（如默认的当前用户目录），或右键选择“以管理员身份运行”。");
                return 3;
            }

            bool machineWide = IsMachineWide(dir) && IsElevated();
            string version = SetupInfo.Version;

            try
            {
                Report(report, 3, "正在准备安装目录…");
                Directory.CreateDirectory(dir);

                // 1. 展开内嵌的安装包
                byte[] payload = ReadPayload();
                if (payload == null)
                {
                    Fail(silent, "安装包损坏：缺少内嵌文件。请重新下载安装程序。");
                    return 4;
                }

                using (var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read))
                {
                    int total = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                        if (!string.IsNullOrEmpty(entry.Name)) total++;

                    int done = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue; // 目录项
                        string target = Path.Combine(dir, entry.FullName.Replace('/', '\\'));
                        string parent = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                        entry.ExtractToFile(target, true);
                        done++;
                        Report(report, 5 + (total == 0 ? 60 : done * 60 / total), "正在安装文件…");
                    }
                }

                // 2. 安装版不写 portable.flag：设置与模版存 %APPDATA%\EasyLabel
                string flag = Path.Combine(dir, "portable.flag");
                if (File.Exists(flag))
                {
                    try { File.Delete(flag); } catch { }
                }

                // 3. 卸载程序 = 安装程序自身的一份副本
                Report(report, 70, "正在写入卸载程序…");
                string uninstaller = Path.Combine(dir, "Uninstall.exe");
                try
                {
                    File.Copy(Application.ExecutablePath, uninstaller, true);
                }
                catch (Exception ex)
                {
                    Fail(silent, "无法写入卸载程序：" + ex.Message);
                    return 5;
                }

                // 4. 快捷方式
                Report(report, 82, "正在创建快捷方式…");
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
                Report(report, 93, "正在注册卸载信息…");
                WriteUninstallEntry(dir, uninstaller, version, machineWide);

                Report(report, 100, "安装完成");
                return 0;
            }
            catch (Exception ex)
            {
                Fail(silent, "安装失败：\r\n" + ex.Message);
                return 6;
            }
        }

        private static byte[] ReadPayload()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            {
                if (stream == null) return null;
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
        }

        private static void Report(Action<int, string> report, int percent, string text)
        {
            if (report != null) report(percent, text);
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

        internal static void Fail(bool silent, string message)
        {
            if (silent) return;
            MessageBox.Show(message, SetupInfo.ProductName + " 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal static class Uninstaller
    {
        internal static readonly string UninstallKey =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Program.UninstallKeyName;

        internal static string ResolveDirectory(string dir)
        {
            if (!string.IsNullOrEmpty(dir)) return dir;
            string found = ReadInstallLocation(Registry.CurrentUser) ?? ReadInstallLocation(Registry.LocalMachine);
            return string.IsNullOrEmpty(found) ? Installer.DefaultDirectory : found;
        }

        internal static bool IsRunning()
        {
            try { return Process.GetProcessesByName("EasyLabel").Length > 0; }
            catch { return false; }
        }

        internal static void KillRunning()
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

        /// <summary>执行卸载。deleteData 为 true 时连同设置与模版一起删除。</summary>
        internal static void Run(string dir, bool silent, bool deleteData)
        {
            string target = ResolveDirectory(dir);

            // 1. 快捷方式
            RemoveShortcuts();

            // 2. 注册表
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); }
            catch { }
            if (Installer.IsElevated())
            {
                try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); }
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

            string dataDir = deleteData ? Installer.DataDirectory : "";
            try
            {
                string tempCopy = Path.Combine(Path.GetTempPath(),
                    "EasyLabel-setup-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Application.ExecutablePath, tempCopy, true);
                var psi = new ProcessStartInfo(tempCopy)
                {
                    UseShellExecute = false,
                    Arguments = "--wipe --dir \"" + target + "\" --pid " + Process.GetCurrentProcess().Id +
                                (deleteData ? " --data \"" + dataDir + "\"" : "")
                };
                Process.Start(psi);
                return; // 由临时副本完成删除并退出
            }
            catch { }

            try { Directory.Delete(target, true); } catch { }
        }

        private static string ReadInstallLocation(RegistryKey root)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(UninstallKey))
                {
                    if (key == null) return null;
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

    // ------------------------------------------------------------------ 界面基础设施

    /// <summary>
    /// 安装界面的绘制与缩放工具。
    /// 关键点：进程声明了 dpiAware（见 setup.manifest），字号 pt 会随系统 DPI 自动放大，
    /// 所以像素尺寸必须用 S() 同比例放大；窗体固定 AutoScaleMode.None，
    /// 否则 WinForms 会按 DPI 再缩放一遍，文字就会撑破固定布局（旧版 150% 下重叠正是这个原因）。
    /// </summary>
    internal static class Ui
    {
        internal static float Scale = 1f;

        internal static readonly Color Background = Color.FromArgb(245, 247, 251);
        internal static readonly Color Card = Color.White;
        internal static readonly Color Border = Color.FromArgb(226, 230, 236);
        internal static readonly Color Text = Color.FromArgb(31, 41, 55);
        internal static readonly Color Muted = Color.FromArgb(107, 114, 128);
        internal static readonly Color Accent = Color.FromArgb(37, 99, 235);
        internal static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
        internal static readonly Color AccentSoft = Color.FromArgb(240, 245, 255);
        internal static readonly Color Success = Color.FromArgb(22, 163, 74);
        internal static readonly Color Error = Color.FromArgb(220, 38, 38);
        internal static readonly Color HeaderSubText = Color.FromArgb(219, 234, 254);

        private const string FontName = "Microsoft YaHei UI";

        internal static int S(double value)
        {
            return (int)Math.Round(value * Scale);
        }

        internal static Font F(float points, FontStyle style)
        {
            return new Font(FontName, points, style, GraphicsUnit.Point);
        }

        /// <summary>按屏幕 DPI 求缩放，并保证设计尺寸能放进工作区。</summary>
        internal static float DetectScale(float designWidth, float designHeight)
        {
            float scale = 1f;
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    scale = g.DpiX / 96f;
                }
            }
            catch { }
            if (scale < 1f) scale = 1f;
            if (scale > 3f) scale = 3f;

            try
            {
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                float fit = Math.Min((work.Width - 40f) / designWidth, (work.Height - 60f) / designHeight);
                if (fit < scale) scale = Math.Max(0.85f, fit);
            }
            catch { }
            return scale;
        }

        internal static GraphicsPath RoundPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (d >= bounds.Width || d >= bounds.Height)
            {
                path.AddEllipse(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static FlatButton Primary(string text)
        {
            return new FlatButton
            {
                Text = text,
                Fill = Accent,
                FillHover = AccentDark,
                BorderColor = Color.Empty,
                ForeColor = Color.White,
                Font = F(9.5f, FontStyle.Regular),
                Size = new Size(S(124), S(40))
            };
        }

        internal static FlatButton Secondary(string text)
        {
            return new FlatButton
            {
                Text = text,
                Fill = Color.White,
                FillHover = Color.FromArgb(243, 246, 251),
                BorderColor = Border,
                ForeColor = Text,
                Font = F(9.5f, FontStyle.Regular),
                Size = new Size(S(96), S(40))
            };
        }

        internal static Label Label(string text, float points, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = F(points, FontStyle.Regular),
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        internal static CheckBox Check(string text, bool value)
        {
            return new CheckBox
            {
                Text = text,
                Checked = value,
                AutoSize = true,
                Font = F(9f, FontStyle.Regular),
                ForeColor = Text,
                BackColor = Color.Transparent,
                UseVisualStyleBackColor = false,
                Margin = new Padding(0)
            };
        }
    }

    /// <summary>圆角按钮。默认的 FlatStyle 画不出圆角，重绘一遍更省事。</summary>
    internal sealed class FlatButton : Button
    {
        internal Color Fill = Ui.Accent;
        internal Color FillHover = Ui.AccentDark;
        internal Color BorderColor = Color.Empty;
        private bool _hover;

        internal FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Card);

            var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            Color fill = !Enabled ? Color.FromArgb(229, 233, 240) : (_hover ? FillHover : Fill);

            using (GraphicsPath path = Ui.RoundPath(bounds, Ui.S(8)))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (BorderColor != Color.Empty)
                {
                    using (var pen = new Pen(BorderColor)) g.DrawPath(pen, path);
                }
            }

            Color fore = !Enabled ? Ui.Muted : ForeColor;
            TextRenderer.DrawText(g, Text, Font, bounds, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues)
            {
                var inner = Rectangle.Inflate(bounds, -Ui.S(4), -Ui.S(4));
                ControlPaint.DrawFocusRectangle(g, inner, fore, fill);
            }
        }
    }

    /// <summary>进度条：系统自带的绿色块与界面不搭，这里自绘一条圆角轨道。</summary>
    internal sealed class ProgressBarEx : Control
    {
        private int _value;

        internal ProgressBarEx()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Card;
        }

        internal int Value
        {
            get { return _value; }
            set
            {
                int next = Math.Max(0, Math.Min(100, value));
                if (next == _value) return;
                _value = next;
                Invalidate();
            }
        }

        internal Color FillColor = Ui.Accent;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Card);

            var track = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            int radius = Math.Max(1, track.Height / 2);
            using (GraphicsPath path = Ui.RoundPath(track, radius))
            using (var brush = new SolidBrush(Color.FromArgb(230, 234, 242)))
            {
                g.FillPath(brush, path);
            }

            if (_value <= 0) return;
            int width = Math.Max(track.Height, Width * _value / 100);
            var fill = new Rectangle(0, 0, Math.Min(width, Width) - 1, track.Height);
            using (GraphicsPath path = Ui.RoundPath(fill, radius))
            using (var brush = new SolidBrush(FillColor))
            {
                g.FillPath(brush, path);
            }
        }
    }

    /// <summary>顶部品牌区：渐变底 + 应用图标 + 标题。</summary>
    internal sealed class HeaderPanel : Panel
    {
        private readonly Icon _icon;
        private readonly string _title;
        private readonly string _subtitle;

        internal HeaderPanel(string title, string subtitle)
        {
            _title = title;
            _subtitle = subtitle;
            try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            BackColor = Ui.Accent;
            Height = Ui.S(100);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var full = new Rectangle(0, 0, Width, Height);
            using (var brush = new LinearGradientBrush(full, Ui.Accent, Ui.AccentDark, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, full);
            }

            // 右侧浅色装饰圆，避免大块纯色显得单调
            using (var soft = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
            {
                int size = Ui.S(190);
                g.FillEllipse(soft, Width - size / 2, -size / 3, size, size);
            }

            int boxSize = Ui.S(46);
            var box = new Rectangle(Ui.S(28), (Height - boxSize) / 2, boxSize, boxSize);
            using (GraphicsPath path = Ui.RoundPath(box, Ui.S(11)))
            using (var brush = new SolidBrush(Color.White))
            {
                g.FillPath(brush, path);
            }
            if (_icon != null)
            {
                var target = Rectangle.Inflate(box, -Ui.S(6), -Ui.S(6));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_icon.ToBitmap(), target);
            }

            int textLeft = box.Right + Ui.S(16);
            var titleRect = new Rectangle(textLeft, Ui.S(22), Width - textLeft - Ui.S(120), Ui.S(32));
            TextRenderer.DrawText(g, _title, Ui.F(15f, FontStyle.Bold), titleRect,
                Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var subRect = new Rectangle(textLeft, Ui.S(54), Width - textLeft - Ui.S(120), Ui.S(24));
            TextRenderer.DrawText(g, _subtitle, Ui.F(9f, FontStyle.Regular), subRect, Ui.HeaderSubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>带圆角与浅色底的信息条。</summary>
    internal sealed class InfoPanel : Panel
    {
        private readonly string _text;

        internal InfoPanel(string text)
        {
            _text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Background;
            Dock = DockStyle.Fill;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Background);

            var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = Ui.RoundPath(bounds, Ui.S(8)))
            {
                using (var brush = new SolidBrush(Ui.AccentSoft)) g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(219, 232, 254))) g.DrawPath(pen, path);
            }

            var text = new Rectangle(bounds.X + Ui.S(14), bounds.Y + Ui.S(10),
                bounds.Width - Ui.S(28), bounds.Height - Ui.S(20));
            TextRenderer.DrawText(g, _text, Ui.F(8.6f, FontStyle.Regular), text, Color.FromArgb(59, 74, 99),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    // ------------------------------------------------------------------ 安装界面

    internal sealed class SetupForm : Form
    {
        internal const int DesignWidth = 620;
        internal const int DesignHeight = 476;

        private readonly TextBox _dirBox;
        private readonly CheckBox _desktop;
        private readonly CheckBox _launch;
        private readonly ProgressBarEx _bar;
        private readonly Label _status;
        private readonly FlatButton _install;
        private readonly FlatButton _cancel;
        private bool _working;
        private bool _finished;
        private bool _launchAfter;
        private bool _elevating;

        internal SetupForm(string initialDir, bool desktop, bool launch, bool autoStart)
        {
            Text = SetupInfo.ProductName + " 安装程序";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ui.Background;
            Font = Ui.F(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.S(DesignWidth), Ui.S(DesignHeight));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var header = new HeaderPanel(SetupInfo.ProductName,
                "安装程序 · " + (Installer.IsElevated() ? "管理员安装" : "当前用户安装"));

            var version = Ui.Label("v" + SetupInfo.Version, 9.5f, Ui.HeaderSubText);
            version.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            version.BackColor = Color.Transparent;
            header.Controls.Add(version);
            header.Resize += (s, e) =>
            {
                version.Left = header.Width - version.Width - Ui.S(28);
                version.Top = Ui.S(32);
            };

            // ---------------- 底部按钮区
            var footer = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(80), BackColor = Ui.Card };
            var footerLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Ui.Border };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Ui.Card,
                Padding = new Padding(0, Ui.S(20), Ui.S(24), 0)
            };
            _install = Ui.Primary("安装");
            _install.Margin = new Padding(Ui.S(12), 0, 0, 0);
            _install.Click += (s, e) => OnPrimaryClick();
            _cancel = Ui.Secondary("取消");
            _cancel.Click += (s, e) => Close();
            buttons.Controls.Add(_install);
            buttons.Controls.Add(_cancel);
            footer.Controls.Add(buttons);
            footer.Controls.Add(footerLine);

            // ---------------- 表单主体
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Ui.Background,
                Padding = new Padding(Ui.S(28), Ui.S(20), Ui.S(28), Ui.S(6)),
                ColumnCount = 1,
                RowCount = 9
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(26)));  // 0 标题
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(38)));  // 1 目录
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(32)));  // 2 桌面快捷方式
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(32)));  // 3 安装后启动
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(84)));  // 4 提示
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));       // 5 弹性空白
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(26)));  // 6 状态
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(18)));  // 7 进度条
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(6)));

            var caption = Ui.Label("安装位置", 9f, Ui.Text);
            caption.Margin = new Padding(Ui.S(2), 0, 0, Ui.S(6));
            caption.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            body.Controls.Add(caption, 0, 0);

            var dirRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = new Padding(0),
                BackColor = Ui.Background
            };
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(96)));
            _dirBox = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Font = Ui.F(9.5f, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle,
                Text = string.IsNullOrEmpty(initialDir) ? Installer.DefaultDirectory : initialDir,
                Margin = new Padding(0, 0, Ui.S(10), 0)
            };
            var browse = Ui.Secondary("浏览…");
            browse.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            browse.Size = new Size(Ui.S(88), Ui.S(32));
            browse.Font = Ui.F(9f, FontStyle.Regular);
            browse.Margin = new Padding(0);
            browse.Click += (s, e) => Browse();
            dirRow.Controls.Add(_dirBox, 0, 0);
            dirRow.Controls.Add(browse, 1, 0);
            body.Controls.Add(dirRow, 0, 1);

            _desktop = Ui.Check("创建桌面快捷方式", desktop);
            _desktop.Margin = new Padding(Ui.S(2), S(6), 0, 0);
            _desktop.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            body.Controls.Add(_desktop, 0, 2);

            _launch = Ui.Check("安装完成后启动 EasyLabel", launch);
            _launch.Margin = new Padding(Ui.S(2), 0, 0, S(2));
            _launch.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            body.Controls.Add(_launch, 0, 3);

            var info = new InfoPanel(
                "安装到当前用户目录不需要管理员权限；设置与模版保存在 " + Installer.DataDirectory + "，卸载时默认保留。\r\n" +
                "如果把目录改到 Program Files，安装程序会自动请求管理员权限并改为“所有用户”安装。");
            info.Margin = new Padding(0, Ui.S(12), 0, 0);
            body.Controls.Add(info, 0, 4);

            _status = new Label
            {
                Text = "准备就绪",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = Ui.F(8.8f, FontStyle.Regular),
                ForeColor = Ui.Muted,
                BackColor = Color.Transparent,
                Margin = new Padding(Ui.S(2), 0, 0, 0)
            };
            body.Controls.Add(_status, 0, 6);

            _bar = new ProgressBarEx
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = Ui.S(8),
                Margin = new Padding(0, 0, 0, Ui.S(4))
            };
            body.Controls.Add(_bar, 0, 7);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            AcceptButton = _install;
            CancelButton = _cancel;

            FormClosing += (s, e) =>
            {
                if (_working) e.Cancel = true;
            };
            Shown += (s, e) =>
            {
                if (autoStart) BeginInvoke(new Action(Install));
                else _install.Focus();
            };
        }

        private static int S(int value)
        {
            return Ui.S(value);
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "选择 EasyLabel 安装目录",
                SelectedPath = _dirBox.Text,
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) _dirBox.Text = dialog.SelectedPath;
            }
        }

        private void OnPrimaryClick()
        {
            if (_finished) Close();
            else Install();
        }

        private void SetStatus(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        private void Install()
        {
            if (_working || _finished) return;

            string dir = (_dirBox.Text ?? "").Trim();
            if (dir.Length == 0)
            {
                SetStatus("请填写安装目录。", Ui.Error);
                _dirBox.Focus();
                return;
            }

            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "EasyLabel.exe")) && Uninstaller.IsRunning())
            {
                SetStatus("该目录里的 EasyLabel 正在运行，请先关闭程序再安装。", Ui.Error);
                return;
            }

            // 目标目录需要管理员权限：换一个提权实例继续，本窗口直接退出，避免误报“安装完成”。
            if (Installer.NeedsElevation(dir))
            {
                _elevating = true;
                if (Installer.RelaunchElevated(dir, _desktop.Checked, _launch.Checked, false))
                {
                    Close();
                }
                else
                {
                    _elevating = false;
                    SetStatus("没有得到管理员权限，安装已取消。", Ui.Error);
                    MessageBox.Show(this,
                        "写入 " + dir + " 需要管理员权限，但提权请求被拒绝。\r\n" +
                        "可以改用默认的当前用户目录，或右键安装程序选择“以管理员身份运行”。",
                        "安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            _working = true;
            _launchAfter = _launch.Checked;
            _install.Enabled = false;
            _cancel.Enabled = false;
            _dirBox.Enabled = false;
            _desktop.Enabled = false;
            _launch.Enabled = false;
            _bar.FillColor = Ui.Accent;
            _bar.Value = 0;
            SetStatus("正在安装…", Ui.Text);
            UseWaitCursor = true;

            int code = Installer.Run(dir, false, _desktop.Checked, OnProgress);

            UseWaitCursor = false;
            _working = false;

            if (code != 0)
            {
                _install.Enabled = true;
                _cancel.Enabled = true;
                _dirBox.Enabled = true;
                _desktop.Enabled = true;
                _launch.Enabled = true;
                _bar.Value = 0;
                SetStatus("安装未完成，请查看提示后重试。", Ui.Error);
                return;
            }

            _finished = true;
            _bar.FillColor = Ui.Success;
            _bar.Value = 100;
            _bar.Refresh();
            SetStatus("安装完成 · 设置与模版保存在 " + Installer.DataDirectory, Ui.Success);
            _install.Text = "完成";
            _install.Enabled = true;
            _cancel.Visible = false;
            _install.Focus();
        }

        private void OnProgress(int percent, string text)
        {
            if (IsDisposed) return;
            _bar.Value = percent;
            SetStatus(text + (percent > 0 ? "（" + percent + "%）" : ""), Ui.Text);
            Application.DoEvents();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_elevating || _working) return;
            if (_finished && _launchAfter) Launch();
        }

        private void Launch()
        {
            try
            {
                string dir = Path.GetFullPath(_dirBox.Text.Trim());
                Process.Start(new ProcessStartInfo(Path.Combine(dir, "EasyLabel.exe")) { WorkingDirectory = dir });
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ 卸载界面

    internal sealed class UninstallForm : Form
    {
        internal const int DesignWidth = 540;
        internal const int DesignHeight = 420;

        private readonly string _dir;
        private readonly CheckBox _deleteData;
        private readonly Label _status;
        private readonly FlatButton _remove;
        private readonly FlatButton _cancel;
        private bool _working;

        internal UninstallForm(string dir)
        {
            _dir = Uninstaller.ResolveDirectory(dir);

            Text = "卸载 " + SetupInfo.ProductName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ui.Background;
            Font = Ui.F(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            Ui.Scale = Ui.DetectScale(DesignWidth, DesignHeight);
            ClientSize = new Size(Ui.S(DesignWidth), Ui.S(DesignHeight));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var header = new HeaderPanel("卸载 " + SetupInfo.ProductName, "会移除程序文件与快捷方式");

            var footer = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(80), BackColor = Ui.Card };
            var footerLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Ui.Border };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Ui.Card,
                Padding = new Padding(0, Ui.S(20), Ui.S(24), 0)
            };
            _remove = Ui.Primary("卸载");
            _remove.Fill = Ui.Error;
            _remove.FillHover = Color.FromArgb(185, 28, 28);
            _remove.Margin = new Padding(Ui.S(12), 0, 0, 0);
            _remove.Click += (s, e) => Remove();
            _cancel = Ui.Secondary("取消");
            _cancel.Click += (s, e) => Close();
            buttons.Controls.Add(_remove);
            buttons.Controls.Add(_cancel);
            footer.Controls.Add(buttons);
            footer.Controls.Add(footerLine);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Ui.Background,
                Padding = new Padding(Ui.S(28), Ui.S(22), Ui.S(28), Ui.S(8)),
                ColumnCount = 1,
                RowCount = 6
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(24)));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(40)));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(32)));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(86)));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(24)));

            var caption = Ui.Label("将从这个目录移除程序", 9f, Ui.Text);
            caption.Margin = new Padding(Ui.S(2), 0, 0, Ui.S(4));
            caption.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            body.Controls.Add(caption, 0, 0);

            var path = new Label
            {
                Text = _dir,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = Ui.F(9f, FontStyle.Regular),
                ForeColor = Ui.Muted,
                BackColor = Ui.Card,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Padding = new Padding(Ui.S(12), 0, Ui.S(12), 0),
                Margin = new Padding(0)
            };
            path.Paint += (s, e) =>
            {
                using (var pen = new Pen(Ui.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, path.Width - 1, path.Height - 1);
            };
            body.Controls.Add(path, 0, 1);

            _deleteData = Ui.Check("同时删除设置与模版（" + Installer.DataDirectory + "）", false);
            _deleteData.Margin = new Padding(Ui.S(2), Ui.S(8), 0, 0);
            _deleteData.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            body.Controls.Add(_deleteData, 0, 2);

            var info = new InfoPanel(
                "卸载会删除程序文件、开始菜单与桌面快捷方式，并从“应用和功能”里移除条目。\r\n" +
                "不勾选上面一项时，打印机设置、标签模版与历史日志都会保留。");
            info.Margin = new Padding(0, Ui.S(12), 0, 0);
            body.Controls.Add(info, 0, 3);

            _status = new Label
            {
                Text = "",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = Ui.F(8.8f, FontStyle.Regular),
                ForeColor = Ui.Muted,
                BackColor = Color.Transparent,
                Margin = new Padding(Ui.S(2), 0, 0, 0)
            };
            body.Controls.Add(_status, 0, 5);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            AcceptButton = _remove;
            CancelButton = _cancel;

            FormClosing += (s, e) =>
            {
                if (_working) e.Cancel = true;
            };
        }

        private void Remove()
        {
            if (Uninstaller.IsRunning())
            {
                DialogResult answer = MessageBox.Show(this,
                    "EasyLabel 正在运行，卸载前需要先关闭它。\r\n\r\n是否现在关闭并继续卸载？",
                    "卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
                Uninstaller.KillRunning();
            }

            _working = true;
            _remove.Enabled = false;
            _cancel.Enabled = false;
            _deleteData.Enabled = false;
            _status.ForeColor = Ui.Text;
            _status.Text = "正在卸载…";
            UseWaitCursor = true;
            Application.DoEvents();

            try
            {
                Uninstaller.Run(_dir, false, _deleteData.Checked);
            }
            catch (Exception ex)
            {
                _working = false;
                UseWaitCursor = false;
                _remove.Enabled = true;
                _cancel.Enabled = true;
                _deleteData.Enabled = true;
                _status.ForeColor = Ui.Error;
                _status.Text = "卸载未完成：" + ex.Message;
                return;
            }

            UseWaitCursor = false;
            _working = false;
            Close();
        }
    }
}
