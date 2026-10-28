using System;
using System.Globalization;
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

        /// <summary>打开在线更新窗口；用户确认更新后保存设置并退出，让更新程序接管文件替换。</summary>
        private void OpenUpdateDialog()
        {
            if (_updateDialogOpen) return;
            _updateDialogOpen = true;
            try
            {
                using (var dialog = new UpdateDialog(_settings, true))
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
                    }
                }
            }
            finally
            {
                _updateDialogOpen = false;
            }
        }

        /// <summary>
        /// 启动后静默检查更新：24 小时内只查一次，且只在发现新版本时打扰用户。
        /// 检查失败不提示，避免离线环境每次启动都弹窗。
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
            _settings.Save();

            if (result == null || !result.Success)
            {
                if (result != null) Logger.Info("后台检查更新失败：" + result.Error);
                return;
            }
            if (!result.Info.IsNewer())
            {
                Logger.Info("后台检查更新：已是最新版本 " + result.Info.Version);
                return;
            }

            _availableVersion = result.Info.Version;
            UpdateVersionLink();
            SetStatus("发现新版本 " + result.Info.DisplayVersion() + "，点击左下角版本号可查看并更新", false, true);
            Logger.Info("发现新版本 " + result.Info.Version + "（当前 " + AppInfo.Version + "）");

            if (string.Equals(_settings.LastNotifiedVersion, result.Info.Version, StringComparison.OrdinalIgnoreCase))
                return;

            _settings.LastNotifiedVersion = result.Info.Version;
            _settings.Save();

            string message = "发现新版本 " + result.Info.DisplayVersion() + "（当前 v" + AppInfo.Version + "）。\r\n\r\n" +
                             "现在查看更新内容吗？更新只替换程序文件，打印机设置与模版不受影响。";
            if (MessageBox.Show(this, message, "发现新版本", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                OpenUpdateDialog();
            }
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

        private void UpdateVersionLink()
        {
            if (_versionLink == null) return;
            _versionLink.Text = string.IsNullOrEmpty(_availableVersion)
                ? "v" + AppInfo.Version + " · " + EditionText()
                : "v" + AppInfo.Version + " → " + _availableVersion + " 可更新";
            _versionLink.ForeColor = string.IsNullOrEmpty(_availableVersion) ? Theme.Muted : Theme.Accent;
        }

        /// <summary>“绿色版”指程序目录有 portable.flag；“安装版”由安装程序写入，数据存 %APPDATA%。</summary>
        private static string EditionText()
        {
            return AppSettings.Portable ? "绿色版" : "安装版";
        }
    }
}
