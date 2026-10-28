using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using EasyLabel.Core;
using EasyLabel.UI;

namespace EasyLabel
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                // -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
                if (!SetProcessDpiAwarenessContext(new IntPtr(-4)))
                    SetProcessDPIAware();
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }

            int startPage = 0;
            bool demo = false;
            bool importDemo = false;
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (arg != null && arg.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))
                    {
                        int p;
                        if (int.TryParse(arg.Substring(7), out p)) startPage = p;
                    }
                    if (string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase)) demo = true;
                    if (string.Equals(arg, "--import-demo", StringComparison.OrdinalIgnoreCase)) importDemo = true;
                }
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Initialize(new Size(1280, 720));
            Application.ThreadException += (s, e) =>
            {
                Logger.Error("未处理的界面异常", e.Exception);
                MessageBox.Show("发生错误：\r\n" + e.Exception.Message + "\r\n\r\n详细信息已写入日志。",
                    "EasyLabel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Logger.Error("未处理的异常", ex);
            };
            try
            {
                Application.Run(new MainForm(startPage, demo, importDemo));
            }
            catch (Exception ex)
            {
                Logger.Error("程序启动失败", ex);
                MessageBox.Show("程序启动失败：\r\n" + ex.Message, "EasyLabel",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (string.Equals(name, "zxing", StringComparison.OrdinalIgnoreCase))
                return LoadResource("EasyLabel.zxing.dll");
            if (string.Equals(name, "ExcelDataReader", StringComparison.OrdinalIgnoreCase))
                return LoadResource("EasyLabel.ExcelDataReader.dll");
            return null;
        }

        private static Assembly LoadResource(string name)
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    var bytes = new byte[stream.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = stream.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return Assembly.Load(bytes);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
