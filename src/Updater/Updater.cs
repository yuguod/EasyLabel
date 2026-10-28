using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace EasyLabel.Updater
{
    /// <summary>
    /// 在线更新的“落地”程序：等待主程序退出 → 用解包目录覆盖安装目录 → 重新启动主程序。
    /// 单独存在的原因：正在运行的 exe 无法被自身覆盖，必须由另一个进程在退出后替换。
    /// 参数：
    ///   --pid     主程序进程号（等它退出）
    ///   --src     解包后的新版本目录
    ///   --dst     安装目录
    ///   --exe     主程序完整路径
    ///   --restart 1 表示替换完成后启动主程序
    /// </summary>
    internal static class Program
    {
        private static readonly string LogPath =
            Path.Combine(Path.GetTempPath(), "EasyLabel-update.log");

        [STAThread]
        private static int Main(string[] args)
        {
            string source = "";
            string target = "";
            string exe = "";
            int pid = -1;
            bool restart = true;

            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                string value = i + 1 < args.Length ? args[i + 1] : "";
                switch (key)
                {
                    case "--pid": int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pid); i++; break;
                    case "--src": source = value.Trim('"'); i++; break;
                    case "--dst": target = value.Trim('"'); i++; break;
                    case "--exe": exe = value.Trim('"'); i++; break;
                    case "--restart": restart = value != "0"; i++; break;
                }
            }

            Log("更新程序启动 pid=" + pid + " src=" + source + " dst=" + target);

            if (source.Length == 0 || target.Length == 0)
            {
                Log("参数不完整，退出");
                return 2;
            }
            if (exe.Length == 0) exe = Path.Combine(target, "EasyLabel.exe");

            WaitForExit(pid);

            string error;
            if (!CopyTree(source, target, out error))
            {
                Log("替换失败：" + error);
                MessageBox.Show(
                    "更新未能完成：\r\n" + error + "\r\n\r\n程序目录：" + target +
                    "\r\n可关闭程序后重新运行安装包，或手动解压更新包覆盖。",
                    "EasyLabel 更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 3;
            }

            Log("替换完成");

            // 新版本目录已无用，尽力清理（占用时忽略，下次启动会再清）。
            try { Directory.Delete(source, true); } catch { }
            try
            {
                string parent = Path.GetDirectoryName(source);
                if (!string.IsNullOrEmpty(parent) && Directory.GetFileSystemEntries(parent).Length == 0)
                    Directory.Delete(parent);
            }
            catch { }

            if (restart && File.Exists(exe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = target, UseShellExecute = true });
                    Log("已启动新版本");
                }
                catch (Exception ex)
                {
                    Log("启动新版本失败：" + ex.Message);
                    MessageBox.Show("更新完成，但自动启动失败，请手动打开：\r\n" + exe,
                        "EasyLabel 更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            return 0;
        }

        private static void WaitForExit(int pid)
        {
            if (pid <= 0) return;
            try
            {
                Process process = Process.GetProcessById(pid);
                if (!process.WaitForExit(120000))
                {
                    Log("等待主程序退出超时，继续尝试替换");
                }
                else
                {
                    Log("主程序已退出");
                }
            }
            catch (ArgumentException)
            {
                // 进程已经不在了，直接继续。
                Log("主程序进程不存在，直接替换");
            }
            catch (Exception ex)
            {
                Log("等待主程序退出出错：" + ex.Message);
            }

            // 文件句柄释放有延迟，稍等一下再覆盖，避免“文件正被使用”。
            Thread.Sleep(600);
        }

        private static bool CopyTree(string source, string target, out string error)
        {
            error = null;
            string self = Path.GetFullPath(Application.ExecutablePath);

            try
            {
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                    if (relative.Length == 0) continue;

                    // 用户数据（便携模式的 data 目录）与便携标记一律不动。
                    if (relative.StartsWith("data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.Equals(relative, "portable.flag", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string destination = Path.Combine(target, relative);
                    string folder = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                    if (string.Equals(Path.GetFullPath(destination), self, StringComparison.OrdinalIgnoreCase))
                    {
                        // 更新程序自己正在运行：先改名，再写入新文件，最后删掉旧文件。
                        string backup = destination + ".old";
                        try
                        {
                            if (File.Exists(backup)) File.Delete(backup);
                            File.Move(destination, backup);
                            Log("已备份更新程序自身：" + backup);
                        }
                        catch (Exception ex)
                        {
                            Log("备份更新程序自身失败（跳过该文件）：" + ex.Message);
                            continue;
                        }
                    }

                    try
                    {
                        CopyWithRetry(file, destination);
                    }
                    catch (Exception ex)
                    {
                        // 主程序被占用才算致命；文档、示例之类的文件失败不影响使用。
                        if (string.Equals(relative, "EasyLabel.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            error = "无法覆盖 EasyLabel.exe：" + ex.Message;
                            return false;
                        }
                        Log("跳过文件 " + relative + "：" + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (!File.Exists(Path.Combine(target, "EasyLabel.exe")))
            {
                error = "替换后没有找到 EasyLabel.exe。";
                return false;
            }
            return true;
        }

        private static void CopyWithRetry(string source, string destination)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    File.Copy(source, destination, true);
                    return;
                }
                catch (IOException)
                {
                    if (attempt == 3) throw;
                    Thread.Sleep(400 * attempt);
                }
            }
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                    "  " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }
    }
}
