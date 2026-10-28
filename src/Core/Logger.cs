using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace EasyLabel.Core
{
    public static class Logger
    {
        private static readonly object Sync = new object();

        public static string LogDirectory
        {
            get { return Path.Combine(AppSettings.DataDirectory, "logs"); }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message, ex);
        }

        public static void Error(string message)
        {
            Write("ERROR", message, null);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    string file = Path.Combine(LogDirectory, "app-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                      .Append(" [").Append(level).Append("] ")
                      .Append(message ?? "");
                    if (ex != null)
                    {
                        sb.AppendLine();
                        sb.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
                        if (!string.IsNullOrEmpty(ex.StackTrace)) sb.AppendLine().Append(ex.StackTrace);
                    }
                    sb.AppendLine();
                    File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败不能影响打印。
            }
        }
    }
}
