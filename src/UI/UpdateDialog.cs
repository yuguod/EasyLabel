using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    /// <summary>
    /// 检查更新对话框：拉取发布清单 → 显示更新说明 → 下载、校验、替换并重启。
    /// </summary>
    public sealed class UpdateDialog : Form
    {
        private const int DesignWidth = 640;
        private const int DesignHeight = 560;

        private readonly AppSettings _settings;
        private readonly bool _autoCheck;
        private Label _status;
        private Label _detail;
        private RichTextBox _notes;
        private Button _install;
        private Button _recheck;
        private CheckBox _autoCheckBox;
        private UpdateInfo _info;
        private bool _busy;

        /// <summary>后台已经下载好的解包目录（按版本区分）；为空表示需要现场下载。</summary>
        private string _stagedRoot;

        /// <summary>true 表示已经启动更新程序，调用方应保存设置并退出主程序。</summary>
        public bool Applied { get; private set; }

        public UpdateDialog(AppSettings settings, bool autoCheck, string stagedRoot = null)
        {
            _settings = settings ?? new AppSettings();
            _autoCheck = autoCheck;
            _stagedRoot = stagedRoot;

            Text = "检查更新";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Background;
            ClientSize = new Size(DesignWidth, DesignHeight);
            Font = Theme.Ui(9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Theme.ApplyScale(this, new Size(DesignWidth, DesignHeight));

            Shown += (s, e) =>
            {
                if (_autoCheck) StartCheck();
                else
                {
                    SetStatus("点击“检查更新”获取最新版本信息。", Theme.Muted);
                    SetDetail("当前版本 v" + AppInfo.Version + "（" + EditionText() + "）");
                }
            };
        }

        private void BuildUi()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.White };
            header.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
                using (var brush = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(brush, 0, 0, 4, header.Height);
            };
            var title = Theme.Label("软件更新", 16f, Theme.Text, FontStyle.Bold);
            title.Location = new Point(24, 14);
            var subtitle = Theme.Label(AppInfo.ProductName + " · 当前版本 v" + AppInfo.Version + "（" + EditionText() + "）",
                8.8f, Theme.Muted);
            subtitle.Location = new Point(26, 46);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

            var statusCard = new Panel { Location = new Point(24, 14), Size = new Size(592, 88), BackColor = Color.White };
            statusCard.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, statusCard.Width - 1, statusCard.Height - 1);
            };
            _status = Theme.Label("尚未检查更新。", 10f, Theme.Text, FontStyle.Bold);
            _status.Location = new Point(16, 14);
            _detail = Theme.Label("", 8.6f, Theme.Muted);
            _detail.AutoSize = false;
            _detail.Location = new Point(16, 44);
            _detail.Size = new Size(560, 34);
            statusCard.Controls.Add(_status);
            statusCard.Controls.Add(_detail);

            var notesTitle = Theme.Label("更新内容", 9.2f, Theme.Muted, FontStyle.Bold);
            notesTitle.Location = new Point(26, 114);

            _notes = new RichTextBox
            {
                Location = new Point(24, 138),
                Size = new Size(592, 194),
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = Theme.Text,
                Font = Theme.Ui(9.2f),
                DetectUrls = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Text = ""
            };
            _notes.LinkClicked += (s, e) => OpenUrl(e.LinkText);

            var hint = Theme.Label(
                "更新只替换程序文件，打印机设置、模版和历史日志都保留。\r\n" +
                "启动时会自动检查并下载新版本，下载好在左下角版本号处提示安装；程序会在替换完成后自动重启。",
                8.4f, Theme.Muted);
            hint.AutoSize = false;
            hint.Location = new Point(26, 340);
            hint.Size = new Size(590, 40);

            _autoCheckBox = UiKit.Check("启动时自动检查更新", _settings.AutoCheckUpdate);
            _autoCheckBox.Location = new Point(24, 388);
            _autoCheckBox.CheckedChanged += (s, e) =>
            {
                _settings.AutoCheckUpdate = _autoCheckBox.Checked;
                _settings.Save();
            };

            _install = UiKit.PrimaryButton("立即更新");
            _install.Size = new Size(132, 38);
            _install.Location = new Point(272, 424);
            _install.Enabled = false;
            _install.Click += async (s, e) => await InstallAsync();

            _recheck = UiKit.SecondaryButton("检查更新");
            _recheck.Size = new Size(112, 38);
            _recheck.Location = new Point(412, 424);
            _recheck.Click += (s, e) => StartCheck();

            var close = UiKit.SecondaryButton("关闭");
            close.Size = new Size(88, 38);
            close.Location = new Point(532, 424);
            close.Click += (s, e) => Close();

            body.Controls.Add(statusCard);
            body.Controls.Add(notesTitle);
            body.Controls.Add(_notes);
            body.Controls.Add(hint);
            body.Controls.Add(_autoCheckBox);
            body.Controls.Add(_install);
            body.Controls.Add(_recheck);
            body.Controls.Add(close);

            Controls.Add(body);
            Controls.Add(header);
            CancelButton = close;
        }

        private string EditionText()
        {
            return AppSettings.Portable ? "绿色版" : "安装版";
        }

        private void SetStatus(string text, Color color)
        {
            _status.Text = text ?? "";
            _status.ForeColor = color;
        }

        private void SetDetail(string text)
        {
            _detail.Text = text ?? "";
        }

        // ---------------------------------------------------------------- 检查

        private async void StartCheck()
        {
            if (_busy) return;
            _busy = true;
            _install.Enabled = false;
            SetStatus("正在检查更新…", Theme.Text);
            SetDetail("更新地址：" + UpdateService.ResolveFeedUrl(_settings.UpdateFeedUrl));
            _notes.Text = "";

            string feed = UpdateService.ResolveFeedUrl(_settings.UpdateFeedUrl);
            UpdateCheckResult check = await Task.Run(() => UpdateService.Check(_settings.UpdateFeedUrl));

            _busy = false;
            _settings.LastUpdateCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            _settings.Save();

            if (!check.Success)
            {
                _info = null;
                SetStatus("检查更新失败", Theme.Error);
                SetDetail(check.Error ?? "");
                _notes.Text = "检查更新只读取版本信息，不上传任何数据。\r\n\r\n" +
                              "默认地址是项目发布页（" + UpdateService.ReleasesUrl + "）。\r\n" +
                              "也可以在“打印机设置 → 软件更新”里填仓库主页、" +
                              "Release 页面，或内网自建的 update.json 地址。";
                return;
            }

            UpdateInfo info = check.Info;
            _info = info;
            if (check.UsedFallback)
            {
                SetDetail("更新地址：" + feed + "（不可用，已改用备用清单）");
            }
            if (info.IsNewer())
            {
                bool staged = IsStagedFor(info.Version);
                SetStatus("发现新版本 " + info.DisplayVersion() + (staged ? "（更新包已下载）" : ""), Theme.Success);
                string detail = "当前版本 v" + AppInfo.Version + "　→　最新版本 " + info.DisplayVersion();
                if (!string.IsNullOrEmpty(info.ReleaseDate))
                    detail += "　·　" + TrimDate(info.ReleaseDate);
                if (info.Size > 0)
                    detail += "　·　" + (info.Size / 1024.0 / 1024.0).ToString("0.0") + " MB";
                if (staged) detail += "　·　已下载，可直接安装";
                SetDetail(detail);
                _notes.Text = string.IsNullOrEmpty(info.Notes) ? "该版本未提供更新说明。" : Normalize(info.Notes);
                _install.Enabled = true;
                _install.Text = staged ? "安装并重启" : "更新到 " + info.DisplayVersion();
            }
            else
            {
                SetStatus("当前已是最新版本", Theme.Success);
                SetDetail("当前版本 v" + AppInfo.Version + "　·　服务器版本 " + info.DisplayVersion() +
                          (string.IsNullOrEmpty(info.ReleaseDate) ? "" : "　·　" + TrimDate(info.ReleaseDate)));
                _notes.Text = string.IsNullOrEmpty(info.Notes)
                    ? "没有可用的更新。"
                    : Normalize(info.Notes);
            }
        }

        private static string TrimDate(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Length >= 10 && text[4] == '-') return text.Substring(0, 10);
            return text;
        }

        private static string Normalize(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n").Trim();
        }

        // ---------------------------------------------------------------- 安装

        /// <summary>这个版本的更新包是不是已经准备好了（后台下载过，或上次安装失败留下的）。</summary>
        private bool IsStagedFor(string version)
        {
            if (string.IsNullOrEmpty(_stagedRoot) || string.IsNullOrEmpty(version)) return false;
            try
            {
                if (!File.Exists(Path.Combine(_stagedRoot, "EasyLabel.exe"))) return false;
                string name = Path.GetFileName(_stagedRoot.TrimEnd(Path.DirectorySeparatorChar));
                return string.Equals(name, "ready-" + version, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private async Task InstallAsync()
        {
            if (_info == null || _busy) return;
            if (!_info.IsNewer()) return;

            bool staged = IsStagedFor(_info.Version);
            string message = staged
                ? "更新包已下载：" + _info.DisplayVersion() + "。\r\n\r\n" +
                  "· 安装时程序会自动关闭，完成后重新启动；\r\n" +
                  "· 打印机设置与模版不会受影响。\r\n\r\n现在安装吗？"
                : "即将下载并安装 " + _info.DisplayVersion() + "。\r\n\r\n" +
                  "· 下载完成后会校验文件完整性；\r\n" +
                  "· 替换文件时程序会自动关闭，完成后重新启动；\r\n" +
                  "· 打印机设置与模版不会受影响。\r\n\r\n是否继续？";
            if (MessageBox.Show(this, message, "安装更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1) != DialogResult.Yes) return;

            _busy = true;
            _install.Enabled = false;
            _recheck.Enabled = false;

            string package = null;
            string failure = null;
            bool canceled = false;

            if (staged)
            {
                package = _stagedRoot;
                SetStatus("正在安装 " + _info.DisplayVersion() + "…", Theme.Text);
            }
            else
            {
                var progress = new ProgressForm("正在更新 EasyLabel", 100);
                progress.Show(this);
                try
                {
                    var token = progress.Token;
                    package = await Task.Run(() =>
                    {
                        UpdatePackageResult result = UpdateService.Stage(
                            _info, (percent, text) => progress.Report(percent, text), token);
                        if (!result.Success)
                        {
                            failure = result.Error;
                            canceled = result.Canceled;
                            return null;
                        }
                        return result.Root;
                    });
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }
                catch (Exception ex)
                {
                    Logger.Error("在线更新失败", ex);
                    failure = ex.Message;
                }
                finally
                {
                    try { progress.Finish(); } catch { }
                }
            }

            _busy = false;
            _recheck.Enabled = true;

            if (package == null)
            {
                if (canceled)
                {
                    SetStatus("已取消更新", Theme.Warning);
                    _install.Enabled = true;
                    return;
                }
                SetStatus("更新失败", Theme.Error);
                SetDetail(failure ?? "未知错误。");
                _install.Enabled = true;
                MessageBox.Show(this, "更新失败：\r\n\r\n" + (failure ?? "未知错误"), "安装更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string applyError;
            if (UpdateService.ApplyAndRestart(package, out applyError))
            {
                _stagedRoot = null;
                _settings.PendingUpdateVersion = "";
                _settings.PendingUpdateRoot = "";
                try { _settings.Save(); } catch { }
                Applied = true;
                SetStatus("更新已就绪，程序即将重启…", Theme.Success);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            // 替换失败：保留更新包，下次启动或点“重试安装”还能用。
            _stagedRoot = package;
            _settings.PendingUpdateVersion = _info.Version;
            _settings.PendingUpdateRoot = package;
            try { _settings.Save(); } catch { }
            SetStatus("更新失败", Theme.Error);
            SetDetail(applyError ?? "无法替换程序文件。");
            _install.Text = "重试安装";
            _install.Enabled = true;
            MessageBox.Show(this, "更新失败：\r\n\r\n" + (applyError ?? "无法替换程序文件。"),
                "安装更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OpenUrl(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
