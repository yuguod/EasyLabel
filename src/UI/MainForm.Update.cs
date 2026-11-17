using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    public sealed partial class MainForm
    {
        private Button _versionLink;
        private bool _updateDialogOpen;
        private string _availableVersion;

        /// <summary>已下载并解包、等待安装的目录（非空时版本号处提示“点击安装”）。</summary>
        private string _pendingPackageRoot;
        private bool _downloadingUpdate;
        private int _downloadPercent = -1;

        // ---------------------------------------------------------------- 入口

        /// <summary>版本号点击：有已下载的包就直接装，否则打开更新窗口。</summary>
        private void VersionLinkClicked()
        {
            if (!string.IsNullOrEmpty(_pendingPackageRoot))
            {
                string message = "新版本 v" + _availableVersion + " 的更新包已下载。\r\n\r\n" +
                                 "现在安装并重启吗？\r\n" +
                                 "选择“否”会保留更新包，下次启动时再提醒安装。";
                if (MessageBox.Show(this, message, "安装更新", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                {
                    ApplyPendingUpdate();
                }
                return;
            }
            OpenUpdateDialog();
        }

        /// <summary>打开在线更新窗口；已下载的包会直接复用，不再重复下载。</summary>
        private void OpenUpdateDialog()
        {
            if (_updateDialogOpen) return;
            _updateDialogOpen = true;
            try
            {
                using (var dialog = new UpdateDialog(_settings, true, _pendingPackageRoot))
                {
                    dialog.ShowDialog(this);
                    if (dialog.Applied)
                    {
                        try
                        {
                            ReadSettingsFromUi();
                            _settings.Save();
                        }
                        catch { }
                        Logger.Info("已下载更新 " + (_availableVersion ?? "") + "，退出程序以便替换文件");
                        Application.Exit();
                        return;
                    }
                }

                // 对话框里可能下载了包或清掉了记录，回到主界面同步一次。
                string version = _settings.PendingUpdateVersion;
                string root;
                if (!string.IsNullOrEmpty(version) && UpdateService.TryGetStaged(version, out root))
                {
                    _pendingPackageRoot = root;
                    _availableVersion = version;
                }
                else
                {
                    _pendingPackageRoot = null;
                }
                UpdateVersionLink();
            }
            finally
            {
                _updateDialogOpen = false;
            }
        }

        // ---------------------------------------------------------------- 启动：先把上次下载好的装上

        /// <summary>
        /// 启动时检查上次是否留下了已下载的更新包：有就提示安装（用户选“否”则本次跳过，下次启动继续提醒）。
        /// 同时清掉过期 / 失效的记录，避免版本号一直挂着“可安装”却点不动。
        /// </summary>
        private void RestorePendingUpdate()
        {
            string version = _settings.PendingUpdateVersion;
            string root = null;

            if (!string.IsNullOrEmpty(version) && UpdateService.TryGetStaged(version, out root))
            {
                if (UpdateService.CompareVersions(version, AppInfo.Version) <= 0)
                {
                    // 已经是这个版本（或更高）了，说明更新装好了，把残留清掉。
                    ClearPendingUpdate(true);
                    UpdateService.PruneStaged(AppInfo.Version);
                    return;
                }

                _pendingPackageRoot = root;
                _availableVersion = version;
                UpdateVersionLink();
                SetStatus("新版本 v" + version + " 已下载，点击左下角版本号安装", false, true);
                Logger.Info("发现上次下载的更新包 v" + version + "，等待安装");

                string message = "已下载新版本 v" + version + "（当前 v" + AppInfo.Version + "）。\r\n\r\n" +
                                 "现在安装并重启吗？\r\n选择“否”继续使用当前版本，下次启动时再提醒。";
                if (MessageBox.Show(this, message, "安装更新", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                {
                    ApplyPendingUpdate();
                }
                return;
            }

            if (!string.IsNullOrEmpty(version))
            {
                Logger.Info("待安装的更新包已不存在（" + (root ?? "") + "），清除记录");
                ClearPendingUpdate();
            }
            UpdateService.PruneStaged(AppInfo.Version);
        }

        /// <summary>用已下载的包替换程序文件并退出，交给更新程序重启。</summary>
        private void ApplyPendingUpdate()
        {
            if (string.IsNullOrEmpty(_pendingPackageRoot)) return;

            string version = _settings.PendingUpdateVersion;
            string error;
            Logger.Info("安装已下载的更新 " + version + "：" + _pendingPackageRoot);
            if (UpdateService.ApplyAndRestart(_pendingPackageRoot, out error))
            {
                ClearPendingUpdate();
                try
                {
                    ReadSettingsFromUi();
                    _settings.Save();
                }
                catch { }
                Application.Exit();
                return;
            }

            // 替换失败时保留更新包：下次启动还会再提醒，也可手动重试。
            Logger.Error("安装已下载的更新失败：" + error);
            MessageBox.Show(this, "安装更新失败：\r\n\r\n" + error + "\r\n\r\n更新包会保留，下次启动可以再试。",
                "安装更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ClearPendingUpdate(bool deleteFiles = false)
        {
            string root = _pendingPackageRoot;
            _pendingPackageRoot = null;
            _settings.PendingUpdateVersion = "";
            _settings.PendingUpdateRoot = "";
            try { _settings.Save(); } catch { }

            if (!deleteFiles || string.IsNullOrEmpty(root)) return;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }

        // ---------------------------------------------------------------- 后台检查 + 自动下载

        /// <summary>
        /// 启动后静默检查更新：24 小时内只查一次。
        /// 发现新版本后自动在后台下载、解压好，只在左下角版本号处提示安装，不弹窗打断操作。
        /// </summary>
        private void StartBackgroundUpdateCheck()
        {
            if (!_settings.AutoCheckUpdate) return;
            if (!IsUpdateCheckDue()) return;

            string feed = UpdateService.ResolveFeedUrl(_settings.UpdateFeedUrl);
            Task.Run(() => UpdateService.Check(feed)).ContinueWith(task =>
            {
                UpdateCheckResult result = task.IsFaulted || task.IsCanceled ? null : task.Result;
                try
                {
                    if (IsDisposed) return;
                    BeginInvoke(new Action<UpdateCheckResult>(OnBackgroundUpdateChecked), result);
                }
                catch { }
            });
        }

        private void OnBackgroundUpdateChecked(UpdateCheckResult result)
        {
            _settings.LastUpdateCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            try { _settings.Save(); } catch { }

            if (result == null || !result.Success)
            {
                if (result != null) Logger.Info("后台检查更新失败：" + result.Error);
                return;
            }
            if (!result.Info.IsNewer())
            {
                Logger.Info("后台检查更新：已是最新版本 " + result.Info.Version);
                if (!string.IsNullOrEmpty(_settings.PendingUpdateVersion)) ClearPendingUpdate(true);
                UpdateService.PruneStaged(AppInfo.Version);
                return;
            }

            _availableVersion = result.Info.Version;
            Logger.Info("发现新版本 " + _availableVersion + "（当前 " + AppInfo.Version + "）");

            string root;
            if (UpdateService.TryGetStaged(_availableVersion, out root))
            {
                // 上次已经下载好（例如用户选了“稍后”），直接提示安装。
                _pendingPackageRoot = root;
                _settings.PendingUpdateVersion = _availableVersion;
                _settings.PendingUpdateRoot = root;
                try { _settings.Save(); } catch { }
                UpdateVersionLink();
                SetStatus("新版本 v" + _availableVersion + " 已下载，点击左下角版本号安装", false, false);
                return;
            }

            if (!_settings.AutoDownloadUpdate)
            {
                UpdateVersionLink();
                SetStatus("发现新版本 v" + _availableVersion + "，点击左下角版本号查看并更新", false, true);
                return;
            }

            StartBackgroundDownload(result.Info);
        }

        private void StartBackgroundDownload(UpdateInfo info)
        {
            if (_downloadingUpdate) return;
            _downloadingUpdate = true;
            _downloadPercent = -1;
            UpdateVersionLink();
            SetStatus("正在后台下载新版本 " + info.DisplayVersion() + "…", false, false);
            Logger.Info("开始后台下载 " + info.Version + "：" + info.DownloadUrl);

            Task.Run(() => UpdateService.Stage(info, (percent, text) => OnDownloadProgress(percent), CancellationToken.None))
                .ContinueWith(task =>
                {
                    UpdatePackageResult staged = task.IsFaulted || task.IsCanceled ? null : task.Result;
                    if (task.IsFaulted && task.Exception != null) Logger.Error("后台下载更新异常", task.Exception);
                    try
                    {
                        if (IsDisposed) return;
                        BeginInvoke(new Action<UpdateInfo, UpdatePackageResult>(OnBackgroundDownloadFinished), info, staged);
                    }
                    catch { }
                });
        }

        private void OnDownloadProgress(int percent)
        {
            if (percent == _downloadPercent) return;
            _downloadPercent = percent;
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (!_downloadingUpdate) return;
                    UpdateVersionLink();
                    SetStatus("正在后台下载新版本 v" + _availableVersion +
                              (percent >= 0 ? "（" + percent + "%）…" : "…"), false, false);
                }));
            }
            catch { }
        }

        private void OnBackgroundDownloadFinished(UpdateInfo info, UpdatePackageResult result)
        {
            _downloadingUpdate = false;
            _downloadPercent = -1;

            if (result == null || !result.Success)
            {
                string error = result == null ? "未知错误" : result.Error;
                Logger.Info("后台下载更新失败：" + error);
                UpdateVersionLink();
                SetStatus("新版本 v" + info.Version + " 下载失败，点击左下角版本号可重试", false, true);
                return;
            }

            _pendingPackageRoot = result.Root;
            _availableVersion = info.Version;
            _settings.PendingUpdateVersion = info.Version;
            _settings.PendingUpdateRoot = result.Root;
            _settings.LastNotifiedVersion = info.Version;
            try { _settings.Save(); } catch { }

            UpdateVersionLink();
            SetStatus("新版本 v" + info.Version + " 已下载，点击左下角版本号安装（下次启动也会提醒）", false, false);
            Logger.Info("后台已准备好新版本 " + info.Version + "：" + result.Root);
        }

        private bool IsUpdateCheckDue()
        {
            string last = _settings.LastUpdateCheck;
            if (string.IsNullOrWhiteSpace(last)) return true;
            DateTime parsed;
            if (!DateTime.TryParseExact(last.Trim(), new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return true;
            return (DateTime.Now - parsed).TotalHours >= 24;
        }

        // ---------------------------------------------------------------- 版本号

        private void UpdateVersionLink()
        {
            if (_versionLink == null) return;

            string current = "v" + AppInfo.Version;
            if (string.IsNullOrEmpty(_availableVersion))
            {
                SetVersionLinkText(current + " · " + EditionText(), Theme.Muted, false);
                return;
            }

            if (!string.IsNullOrEmpty(_pendingPackageRoot))
            {
                SetVersionLinkText(current + " → v" + _availableVersion + " 点击安装", Theme.Success, true);
                return;
            }

            if (_downloadingUpdate)
            {
                SetVersionLinkText(current + " → v" + _availableVersion +
                                   (_downloadPercent >= 0 ? " " + _downloadPercent + "%" : " 下载中"),
                    Theme.Accent, false);
                return;
            }

            SetVersionLinkText(current + " → v" + _availableVersion + " 可更新", Theme.Accent, false);
        }

        private void SetVersionLinkText(string text, Color color, bool bold)
        {
            _versionLink.Text = text;
            _versionLink.ForeColor = color;
            _versionLink.Font = bold ? Theme.Ui(8.2f, FontStyle.Bold) : Theme.Ui(8.2f);
        }

        /// <summary>“绿色版”指程序目录有 portable.flag；“安装版”由安装程序写入，数据存 %APPDATA%。</summary>
        private static string EditionText()
        {
            return AppSettings.Portable ? "绿色版" : "安装版";
        }
    }
}
