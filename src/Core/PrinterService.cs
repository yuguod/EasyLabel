using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.ComponentModel;

namespace EasyLabel.Core
{
    public static class PrinterService
    {
        public static List<string> GetInstalledPrinters()
        {
            var list = new List<string>();
            try
            {
                foreach (string name in PrinterSettings.InstalledPrinters)
                {
                    if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("枚举打印机失败", ex);
            }
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            return list;
        }

        public static string GetDefaultPrinter()
        {
            try { return new PrinterSettings().PrinterName; }
            catch { return ""; }
        }

        public static string GuessDpi(string printerName)
        {
            if (string.IsNullOrEmpty(printerName)) return "";
            string n = printerName.ToLowerInvariant();
            if (n.Contains("600dpi") || n.Contains("600 dpi")) return "600";
            if (n.Contains("300dpi") || n.Contains("300 dpi")) return "300";
            if (n.Contains("203dpi") || n.Contains("203 dpi")) return "203";
            return "";
        }

        public static bool IsZebraLike(string printerName)
        {
            if (string.IsNullOrEmpty(printerName)) return false;
            string n = printerName.ToLowerInvariant();
            return n.Contains("zebra") || n.Contains("zt610") || n.Contains("zdesigner");
        }

        public static void Send(byte[] data, string jobName, AppSettings settings)
        {
            if (data == null || data.Length == 0) throw new ArgumentException("打印数据为空");
            if (settings == null) throw new ArgumentNullException("settings");

            if (settings.UseNetwork)
            {
                SendNetwork(data, settings.NetworkHost, settings.NetworkPort);
            }
            else
            {
                string printer = (settings.PrinterName ?? "").Trim();
                if (printer.Length == 0) throw new InvalidOperationException("未选择打印机队列，请到“打印机设置”中选择");
                RawPrinter.Send(printer, jobName, data);
            }
        }

        private static void SendNetwork(byte[] data, string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("未填写打印机 IP 地址");
            if (port <= 0 || port > 65535) throw new InvalidOperationException("打印机端口无效");
            using (var client = new TcpClient())
            {
                client.NoDelay = true;
                IAsyncResult ar = client.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(6)))
                {
                    try { client.Close(); } catch { }
                    throw new InvalidOperationException("连接打印机超时：" + host + ":" + port);
                }
                client.EndConnect(ar);
                client.SendTimeout = NetworkWriteTimeoutMs;
                using (NetworkStream stream = client.GetStream())
                {
                    // 只设连接超时不够：慢链路写阻塞时会把整个打印流程挂死，
                    // 所以补上写超时，并分块写以便超时能及时触发。
                    stream.WriteTimeout = NetworkWriteTimeoutMs;
                    try
                    {
                        int offset = 0;
                        while (offset < data.Length)
                        {
                            int count = Math.Min(NetworkChunkSize, data.Length - offset);
                            stream.Write(data, offset, count);
                            offset += count;
                        }
                        stream.Flush();
                    }
                    catch (IOException ex)
                    {
                        throw new InvalidOperationException(
                            "向打印机写入失败（可能网络中断或打印缓冲区已满）：" + ex.Message, ex);
                    }
                }
            }
        }

        private const int NetworkWriteTimeoutMs = 30000;
        private const int NetworkChunkSize = 64 * 1024;
    }

    internal static class RawPrinter
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DOC_INFO_1
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDocName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDatatype;
        }

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(string printerName, out IntPtr hPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int StartDocPrinter(IntPtr hPrinter, int level, ref DOC_INFO_1 docInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, [In] byte[] pBytes, int dwCount, out int dwWritten);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool SetJob(IntPtr hPrinter, int jobId, int level, IntPtr pJob, int command);

        private const int JobControlCancel = 3;

        public static void Send(string printerName, string jobName, byte[] data)
        {
            IntPtr hPrinter = IntPtr.Zero;
            bool docStarted = false;
            bool pageStarted = false;
            bool failed = false;
            int jobId = 0;
            try
            {
                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开打印机：" + printerName);

                var doc = new DOC_INFO_1
                {
                    pDocName = string.IsNullOrEmpty(jobName) ? "EasyLabel" : jobName,
                    pOutputFile = null,
                    pDatatype = "RAW"
                };
                jobId = StartDocPrinter(hPrinter, 1, ref doc);
                if (jobId == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "创建打印作业失败");
                docStarted = true;

                if (!StartPagePrinter(hPrinter))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "开始打印页失败");
                pageStarted = true;

                int written;
                if (!WritePrinter(hPrinter, data, data.Length, out written) || written != data.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "写入打印机失败（已写入 " + written + " 字节）");

                if (!EndPagePrinter(hPrinter))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "结束打印页失败");
                pageStarted = false;
                if (!EndDocPrinter(hPrinter))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "结束打印作业失败");
                docStarted = false;
            }
            catch
            {
                failed = true;
                throw;
            }
            finally
            {
                // 只写了一半时不能再 EndDocPrinter 把残缺作业提交上去，否则会吐出半张残标。
                if (failed && jobId != 0 && hPrinter != IntPtr.Zero)
                {
                    try { SetJob(hPrinter, jobId, 0, IntPtr.Zero, JobControlCancel); } catch { }
                }
                if (pageStarted) { try { EndPagePrinter(hPrinter); } catch { } }
                if (docStarted) { try { EndDocPrinter(hPrinter); } catch { } }
                if (hPrinter != IntPtr.Zero) { try { ClosePrinter(hPrinter); } catch { } }
            }
        }
    }
}
