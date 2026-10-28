using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace EasyLabel.Core
{
    /// <summary>一条发布信息，来自 update.json 或 GitHub Releases API。</summary>
    public sealed class UpdateInfo
    {
        public string Version = "";
        public string Notes = "";
        public string DownloadUrl = "";
        public string Sha256 = "";
        public long Size = -1;
        public string ReleaseDate = "";
        public string MinVersion = "";
        public bool Mandatory;

        /// <summary>发布版本是否比当前运行的版本新。</summary>
        public bool IsNewer()
        {
            return !string.IsNullOrEmpty(Version) &&
                   UpdateService.CompareVersions(Version, AppInfo.Version) > 0;
        }

        public string DisplayVersion()
        {
            return "v" + Version;
        }
    }

    /// <summary>检查结果：Info 非空表示拿到了清单；Error 可直接展示给用户。</summary>
    public sealed class UpdateCheckResult
    {
        public UpdateInfo Info;
        public string Error;
        public bool Success { get { return Info != null; } }
    }

    /// <summary>下载结果：ZipPath 非空表示已下载并通过校验。</summary>
    public sealed class UpdateDownloadResult
    {
        public string ZipPath;
        public string Error;
        public bool Canceled;
        public bool Success { get { return !string.IsNullOrEmpty(ZipPath); } }
    }

    /// <summary>解包结果：Root 为包含 EasyLabel.exe 的目录。</summary>
    public sealed class UpdatePackageResult
    {
        public string Root;
        public string Error;
        public bool Success { get { return !string.IsNullOrEmpty(Root); } }
    }

    /// <summary>
    /// 在线更新：读取发布清单 → 下载更新包 → 校验 SHA-256 → 解包 → 交给 Updater 替换文件并重启。
    /// 清单既支持自建 update.json，也支持 GitHub Releases API（/releases/latest）的返回体。
    /// </summary>
    public static class UpdateService
    {
        /// <summary>默认发布清单地址。企业内网可在“打印机设置 → 软件更新”里改成自建地址。</summary>
        public const string DefaultFeedUrl = "https://raw.githubusercontent.com/yuguod/EasyLabel/main/update.json";

        /// <summary>更新程序文件名，随安装包/绿色包一起分发。</summary>
        public const string UpdaterFileName = "EasyLabelUpdater.exe";

        private const string UserAgent = "EasyLabel-Update";
        private const int BufferSize = 64 * 1024;

        static UpdateService()
        {
            // .NET Framework 4.8 默认已启用 TLS 1.2，这里显式打开，避免旧系统策略把它关掉。
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch { }
            try { ServicePointManager.DefaultConnectionLimit = 8; } catch { }
        }

        public static string StagingRoot
        {
            get { return Path.Combine(Path.GetTempPath(), "EasyLabel-update"); }
        }

        public static string ResolveFeedUrl(string configured)
        {
            string url = (configured ?? "").Trim();
            return url.Length == 0 ? DefaultFeedUrl : url;
        }

        // ---------------------------------------------------------------- 版本比较

        /// <summary>按数字段比较版本号，支持 1.6、1.6.0、v1.6.0、1.6.0-beta 这类写法。</summary>
        public static int CompareVersions(string left, string right)
        {
            int[] a;
            int[] b;
            bool preA;
            bool preB;
            ParseVersion(left, out a, out preA);
            ParseVersion(right, out b, out preB);
            for (int i = 0; i < 4; i++)
            {
                if (a[i] != b[i]) return a[i] > b[i] ? 1 : -1;
            }
            if (preA == preB) return 0;
            // 数字段相同时，正式版比预发布版新。
            return preA ? -1 : 1;
        }

        private static void ParseVersion(string text, out int[] parts, out bool prerelease)
        {
            parts = new int[4];
            prerelease = false;
            if (string.IsNullOrEmpty(text)) return;

            string value = text.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            string[] pieces = value.Split('.', '-', '+', ' ');
            for (int i = 0; i < pieces.Length && i < 4; i++)
            {
                string piece = pieces[i];
                int digits = 0;
                while (digits < piece.Length && char.IsDigit(piece[digits])) digits++;
                int number;
                if (digits > 0 && int.TryParse(piece.Substring(0, digits), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out number))
                {
                    parts[i] = number;
                }
                if (digits < piece.Length) prerelease = true;
            }
            if (value.IndexOf('-') > 0 || value.IndexOf('+') > 0) prerelease = true;
        }

        // ---------------------------------------------------------------- 检查更新

        /// <summary>拉取并解析发布清单。</summary>
        public static UpdateCheckResult Check(string feedUrl)
        {
            var result = new UpdateCheckResult();
            string url = ResolveFeedUrl(feedUrl);
            string json;
            try
            {
                json = HttpGet(url, 15000);
            }
            catch (WebException ex)
            {
                result.Error = DescribeWebError(ex, url);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "无法访问更新服务器：" + ex.Message;
                return result;
            }

            string error;
            UpdateInfo info = ParseManifest(json, out error);
            if (info == null)
            {
                result.Error = error;
                return result;
            }
            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                result.Error = "更新清单里没有下载地址。";
                return result;
            }
            result.Info = info;
            return result;
        }

        /// <summary>解析 JSON 清单。支持自建 update.json 与 GitHub Releases API 两种结构。</summary>
        public static UpdateInfo ParseManifest(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "更新清单为空。";
                return null;
            }

            Dictionary<string, object> map;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
                map = serializer.Deserialize<Dictionary<string, object>>(json.Trim());
            }
            catch (Exception ex)
            {
                error = "更新清单格式错误（需要 JSON）：" + ex.Message;
                return null;
            }
            if (map == null)
            {
                error = "更新清单格式错误。";
                return null;
            }

            var info = new UpdateInfo();
            if (map.ContainsKey("tag_name"))
            {
                // GitHub Releases API：/repos/{owner}/{repo}/releases/latest
                info.Version = CleanVersion(Str(map, "tag_name"));
                info.Notes = Str(map, "body");
                info.ReleaseDate = Str(map, "published_at");
                info.DownloadUrl = PickAsset(map);
                if (info.DownloadUrl.Length == 0)
                {
                    error = "GitHub 发布里没有找到更新包（.zip）。";
                    return null;
                }
            }
            else
            {
                info.Version = CleanVersion(Str(map, "version"));
                info.Notes = Str(map, "notes");
                info.ReleaseDate = Str(map, "releaseDate");
                info.DownloadUrl = Str(map, "url");
                if (info.DownloadUrl.Length == 0) info.DownloadUrl = Str(map, "downloadUrl");
                info.Sha256 = Str(map, "sha256");
                info.MinVersion = Str(map, "minVersion");
                info.Mandatory = Bool(map, "mandatory");
                object size = Value(map, "size");
                long parsed;
                if (size != null && long.TryParse(Convert.ToString(size, CultureInfo.InvariantCulture),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    info.Size = parsed;
            }

            if (info.Version.Length == 0)
            {
                error = "更新清单缺少版本号（version）。";
                return null;
            }
            return info;
        }

        private static string PickAsset(Dictionary<string, object> map)
        {
            object assetsValue = Value(map, "assets");
            // JavaScriptSerializer 对数组可能给出 object[] 或 ArrayList，按 IEnumerable 统一处理。
            var assets = assetsValue as System.Collections.IEnumerable;
            if (assets == null || assetsValue is string) return "";

            string fallback = "";
            foreach (object item in assets)
            {
                var asset = item as Dictionary<string, object>;
                if (asset == null) continue;
                string name = Str(asset, "name");
                string url = Str(asset, "browser_download_url");
                if (url.Length == 0 || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0) return url;
                if (fallback.Length == 0) fallback = url;
            }
            return fallback;
        }

        private static string CleanVersion(string value)
        {
            string text = (value ?? "").Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
            return text;
        }

        private static object Value(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) ? value : null;
        }

        private static string Str(Dictionary<string, object> map, string key)
        {
            object value = Value(map, key);
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        private static bool Bool(Dictionary<string, object> map, string key)
        {
            object value = Value(map, key);
            if (value == null) return false;
            bool flag;
            if (value is bool) return (bool)value;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out flag) && flag;
        }

        // ---------------------------------------------------------------- 下载与解包

        /// <summary>
        /// 下载更新包到临时目录并校验 SHA-256。
        /// progress 回调参数为百分比（0–100，未知总量时为 -1）与状态文字。
        /// </summary>
        public static UpdateDownloadResult Download(UpdateInfo info, Action<int, string> progress, CancellationToken token)
        {
            var result = new UpdateDownloadResult();
            string dir = Path.Combine(StagingRoot, "dl-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            string zip = Path.Combine(dir, "EasyLabel-" + info.Version + "-update.zip");
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                result.Error = "无法创建临时目录：" + ex.Message;
                return result;
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(info.DownloadUrl);
                request.UserAgent = UserAgent + "/" + AppInfo.Version;
                request.Timeout = 30000;
                request.ReadWriteTimeout = 60000;
                request.AllowAutoRedirect = true;

                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        result.Error = "服务器没有返回内容。";
                        return result;
                    }
                    long total = response.ContentLength;
                    if (total <= 0) total = info.Size;

                    using (var file = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                    {
                        var buffer = new byte[BufferSize];
                        long received = 0;
                        int read;
                        int lastPercent = -2;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            file.Write(buffer, 0, read);
                            received += read;
                            if (progress != null)
                            {
                                int percent = total > 0
                                    ? (int)Math.Min(100, received * 100 / total)
                                    : -1;
                                if (percent != lastPercent)
                                {
                                    lastPercent = percent;
                                    progress(percent,
                                        "正在下载更新包 " + SizeText(received) +
                                        (total > 0 ? " / " + SizeText(total) : "") +
                                        (percent >= 0 ? "（" + percent + "%）" : ""));
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                SafeDelete(zip);
                result.Canceled = true;
                result.Error = "已取消下载。";
                return result;
            }
            catch (WebException ex)
            {
                SafeDelete(zip);
                result.Error = DescribeWebError(ex, info.DownloadUrl);
                return result;
            }
            catch (Exception ex)
            {
                SafeDelete(zip);
                result.Error = "下载失败：" + ex.Message;
                return result;
            }

            if (!string.IsNullOrEmpty(info.Sha256))
            {
                if (progress != null) progress(100, "正在校验更新包…");
                string actual;
                try
                {
                    actual = ComputeSha256(zip);
                }
                catch (Exception ex)
                {
                    SafeDelete(zip);
                    result.Error = "无法校验更新包：" + ex.Message;
                    return result;
                }
                if (!string.Equals(actual, info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    SafeDelete(zip);
                    result.Error = "更新包校验失败，文件可能损坏或被篡改。\r\n期望：" + info.Sha256 + "\r\n实际：" + actual;
                    return result;
                }
            }

            result.ZipPath = zip;
            return result;
        }

        /// <summary>把更新包解压到临时目录，返回包含 EasyLabel.exe 的根目录。</summary>
        public static UpdatePackageResult Extract(string zipPath)
        {
            var result = new UpdatePackageResult();
            string dir = Path.Combine(StagingRoot, "pkg-" + DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture));
            try
            {
                Directory.CreateDirectory(dir);
                ZipFile.ExtractToDirectory(zipPath, dir);
            }
            catch (Exception ex)
            {
                result.Error = "解压更新包失败：" + ex.Message;
                return result;
            }

            string root = dir;
            if (!File.Exists(Path.Combine(root, "EasyLabel.exe")))
            {
                bool found = false;
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    if (File.Exists(Path.Combine(sub, "EasyLabel.exe")))
                    {
                        root = sub;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    result.Error = "更新包里没有 EasyLabel.exe，已放弃本次更新。";
                    return result;
                }
            }
            result.Root = root;
            return result;
        }

        // ---------------------------------------------------------------- 应用更新

        /// <summary>
        /// 用解包后的目录内容覆盖当前程序目录并重启。
        /// 优先交给 EasyLabelUpdater.exe（可替换正在运行的 exe）；没有更新程序时退化为“改名旧 exe 再写入”。
        /// 返回 true 表示已经安排好后继动作，调用方应尽快退出当前进程。
        /// </summary>
        public static bool ApplyAndRestart(string packageRoot, out string error)
        {
            error = null;
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string exe = Path.Combine(appDir, "EasyLabel.exe");
            string updater = Path.Combine(appDir, UpdaterFileName);

            if (File.Exists(updater))
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(updater)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = appDir,
                        Arguments = "--pid " + System.Diagnostics.Process.GetCurrentProcess().Id +
                                    " --src \"" + packageRoot + "\"" +
                                    " --dst \"" + appDir + "\"" +
                                    " --exe \"" + exe + "\"" +
                                    " --restart 1"
                    };
                    System.Diagnostics.Process.Start(psi);
                    return true;
                }
                catch (Exception ex)
                {
                    // 更新程序起不来时退回进程内替换，不能让用户卡在“更新失败”。
                    Logger.Error("启动更新程序失败，改用进程内替换", ex);
                }
            }

            return ApplyInline(packageRoot, appDir, exe, out error);
        }

        private static bool ApplyInline(string packageRoot, string appDir, string exe, out string error)
        {
            error = null;
            try
            {
                string backup = exe + ".old";
                SafeDelete(backup);
                File.Move(exe, backup);
                File.Copy(Path.Combine(packageRoot, "EasyLabel.exe"), exe, true);

                // 其余文件（文档、示例、更新程序）尽量一并覆盖，失败不影响主程序启动。
                foreach (string source in Directory.GetFiles(packageRoot, "*", SearchOption.AllDirectories))
                {
                    string relative = source.Substring(packageRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                    if (string.Equals(relative, "EasyLabel.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (relative.StartsWith("data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        string target = Path.Combine(appDir, relative);
                        string parent = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                        File.Copy(source, target, true);
                    }
                    catch { }
                }

                System.Diagnostics.Process.Start(exe);
                return true;
            }
            catch (Exception ex)
            {
                error = "替换程序文件失败：" + ex.Message + "\r\n请关闭程序后手动解压更新包覆盖，或重新运行安装程序。";
                return false;
            }
        }

        /// <summary>
        /// 启动时清理上次更新留下的 .old 备份与过期临时包。只在成功删干净时静默进行，失败忽略。
        /// </summary>
        public static void CleanupAfterUpdate()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (string file in Directory.GetFiles(appDir, "*.old"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }

            try
            {
                if (!Directory.Exists(StagingRoot)) return;
                foreach (string dir in Directory.GetDirectories(StagingRoot))
                {
                    try
                    {
                        if ((DateTime.Now - Directory.GetLastWriteTime(dir)).TotalDays < 1) continue;
                        Directory.Delete(dir, true);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------- 工具

        private static string HttpGet(string url, int timeout)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent + "/" + AppInfo.Version;
            request.Accept = "application/json";
            request.Timeout = timeout;
            request.ReadWriteTimeout = timeout;
            request.AllowAutoRedirect = true;
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static string DescribeWebError(WebException ex, string url)
        {
            var response = ex.Response as HttpWebResponse;
            if (response != null)
            {
                int code = (int)response.StatusCode;
                if (code == 404) return "更新地址不存在（404）：" + url;
                if (code == 403) return "更新服务器拒绝访问（403），可能需要代理或该地址受限。";
                return "更新服务器返回错误 " + code + "：" + response.StatusDescription;
            }
            switch (ex.Status)
            {
                case WebExceptionStatus.NameResolutionFailure:
                    return "无法解析更新服务器地址，请检查网络或代理设置。";
                case WebExceptionStatus.ConnectFailure:
                    return "无法连接更新服务器，请检查网络或代理设置。";
                case WebExceptionStatus.Timeout:
                    return "连接更新服务器超时。";
                case WebExceptionStatus.TrustFailure:
                case WebExceptionStatus.SecureChannelFailure:
                    return "更新服务器的 HTTPS 证书校验失败。";
                default:
                    return "网络错误：" + ex.Message;
            }
        }

        public static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static string SizeText(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
