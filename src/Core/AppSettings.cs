using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace EasyLabel.Core
{
    public sealed class AppSettings
    {
        public string PrinterName { get; set; }
        public bool UseNetwork { get; set; }
        public string NetworkHost { get; set; }
        public int NetworkPort { get; set; }
        public int Dpi { get; set; }
        public int Darkness { get; set; }
        public int Speed { get; set; }
        public MediaType MediaType { get; set; }
        public string DefaultLabel { get; set; }
        public BarcodeType DefaultBarcodeType { get; set; }
        public QrEcc DefaultQrEcc { get; set; }
        public bool ShowBarcodeText { get; set; }
        public double DefaultFontSizeMm { get; set; }
        public string DefaultFontFamily { get; set; }
        public bool DefaultBold { get; set; }
        public int BarcodeHeightPercent { get; set; }
        public int QrSizePercent { get; set; }
        public double MarginTopMm { get; set; }
        public double MarginBottomMm { get; set; }
        public double MarginLeftMm { get; set; }
        public double MarginRightMm { get; set; }
        public int SettingsVersion { get; set; }
        public TextAlign TextAlign { get; set; }
        public string LastFolder { get; set; }
        public string LastImportFolder { get; set; }
        public bool AutoCheckUpdate { get; set; }
        public string UpdateFeedUrl { get; set; }
        public string LastUpdateCheck { get; set; }
        public string LastNotifiedVersion { get; set; }

        public AppSettings()
        {
            PrinterName = "";
            UseNetwork = false;
            NetworkHost = "192.168.1.100";
            NetworkPort = 9100;
            Dpi = 600;
            Darkness = 0;
            Speed = 4;
            MediaType = MediaType.Gap;
            DefaultLabel = LabelSpec.Label40x15.Name;
            DefaultBarcodeType = BarcodeType.Code128;
            DefaultQrEcc = QrEcc.M;
            ShowBarcodeText = true;
            DefaultFontSizeMm = 0;
            DefaultFontFamily = "Microsoft YaHei UI";
            DefaultBold = false;
            BarcodeHeightPercent = 100;
            QrSizePercent = 100;
            MarginTopMm = 1.2;
            MarginBottomMm = 1.2;
            MarginLeftMm = 1.2;
            MarginRightMm = 1.2;
            SettingsVersion = 2;
            TextAlign = TextAlign.Center;
            LastFolder = "";
            LastImportFolder = "";
            AutoCheckUpdate = true;
            UpdateFeedUrl = "";
            LastUpdateCheck = "";
            LastNotifiedVersion = "";
        }

        public static bool Portable
        {
            get
            {
                try
                {
                    return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portable.flag"));
                }
                catch { return false; }
            }
        }

        public static string DataDirectory
        {
            get
            {
                if (Portable)
                {
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                }
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrWhiteSpace(appData)) appData = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(appData, "EasyLabel");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(DataDirectory, "settings.xml"); }
        }

        /// <summary>
        /// 旧版（更名前）的设置目录名。升级后新目录里没有 settings.xml 时，
        /// 从这里复制一份过去，避免用户重装后打印队列、DPI、版式全部要重设。
        /// </summary>
        private static readonly string[] LegacyFolderNames = { "ZebraLabel" };

        public static void MigrateLegacySettings()
        {
            try
            {
                if (Portable) return;
                if (File.Exists(SettingsPath)) return;

                foreach (string name in LegacyFolderNames)
                {
                    string legacyDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name);
                    string legacyFile = Path.Combine(legacyDir, "settings.xml");
                    if (!File.Exists(legacyFile)) continue;

                    Directory.CreateDirectory(DataDirectory);
                    File.Copy(legacyFile, SettingsPath, false);
                    Logger.Info("已从旧版本目录迁移设置：" + legacyFile);
                    return;
                }
            }
            catch
            {
                // 迁移失败就按默认设置启动，不能挡住程序运行。
            }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                MigrateLegacySettings();
                string path = SettingsPath;
                if (!File.Exists(path)) return settings;
                XDocument doc = XDocument.Load(path);
                XElement root = doc.Root;
                if (root == null) return settings;

                settings.PrinterName = Get(root, "PrinterName", settings.PrinterName);
                settings.UseNetwork = GetBool(root, "UseNetwork", settings.UseNetwork);
                settings.NetworkHost = Get(root, "NetworkHost", settings.NetworkHost);
                settings.NetworkPort = GetInt(root, "NetworkPort", settings.NetworkPort);
                settings.Dpi = GetInt(root, "Dpi", settings.Dpi);
                settings.Darkness = GetInt(root, "Darkness", settings.Darkness);
                settings.Speed = GetInt(root, "Speed", settings.Speed);
                settings.MediaType = (MediaType)GetInt(root, "MediaType", (int)settings.MediaType);
                settings.DefaultLabel = Get(root, "DefaultLabel", settings.DefaultLabel);
                settings.DefaultBarcodeType = (BarcodeType)GetInt(root, "DefaultBarcodeType", (int)settings.DefaultBarcodeType);
                settings.DefaultQrEcc = (QrEcc)GetInt(root, "DefaultQrEcc", (int)settings.DefaultQrEcc);
                settings.ShowBarcodeText = GetBool(root, "ShowBarcodeText", settings.ShowBarcodeText);
                settings.DefaultFontSizeMm = GetDouble(root, "DefaultFontSizeMm", settings.DefaultFontSizeMm);
                settings.DefaultFontFamily = Get(root, "DefaultFontFamily", settings.DefaultFontFamily);
                settings.DefaultBold = GetBool(root, "DefaultBold", settings.DefaultBold);
                settings.BarcodeHeightPercent = GetInt(root, "BarcodeHeightPercent", settings.BarcodeHeightPercent);
                settings.QrSizePercent = GetInt(root, "QrSizePercent", settings.QrSizePercent);
                double legacyMargin = GetDouble(root, "MarginMm", settings.MarginLeftMm);
                settings.MarginTopMm = GetDouble(root, "MarginTopMm", legacyMargin);
                settings.MarginBottomMm = GetDouble(root, "MarginBottomMm", legacyMargin);
                settings.MarginLeftMm = GetDouble(root, "MarginLeftMm", legacyMargin);
                settings.MarginRightMm = GetDouble(root, "MarginRightMm", legacyMargin);
                settings.SettingsVersion = GetInt(root, "SettingsVersion", 0);
                settings.TextAlign = (TextAlign)GetInt(root, "TextAlign", (int)settings.TextAlign);
                settings.LastFolder = Get(root, "LastFolder", settings.LastFolder);
                settings.LastImportFolder = Get(root, "LastImportFolder", settings.LastImportFolder);
                settings.AutoCheckUpdate = GetBool(root, "AutoCheckUpdate", settings.AutoCheckUpdate);
                settings.UpdateFeedUrl = Get(root, "UpdateFeedUrl", settings.UpdateFeedUrl);
                settings.LastUpdateCheck = Get(root, "LastUpdateCheck", settings.LastUpdateCheck);
                settings.LastNotifiedVersion = Get(root, "LastNotifiedVersion", settings.LastNotifiedVersion);

                // v1.1 起默认 600 dpi 机型；旧设置文件只迁移一次，统一到 600。
                if (settings.SettingsVersion < 2)
                {
                    settings.Dpi = 600;
                    settings.SettingsVersion = 2;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取设置失败，使用默认设置", ex);
            }
            settings.Normalize();
            return settings;
        }

        public void Save()
        {
            try
            {
                Normalize();
                Directory.CreateDirectory(DataDirectory);
                var root = new XElement("EasyLabelSettings",
                    new XElement("PrinterName", Safe(PrinterName)),
                    new XElement("UseNetwork", UseNetwork),
                    new XElement("NetworkHost", Safe(NetworkHost)),
                    new XElement("NetworkPort", NetworkPort),
                    new XElement("Dpi", Dpi),
                    new XElement("Darkness", Darkness),
                    new XElement("Speed", Speed),
                    new XElement("MediaType", (int)MediaType),
                    new XElement("DefaultLabel", Safe(DefaultLabel)),
                    new XElement("DefaultBarcodeType", (int)DefaultBarcodeType),
                    new XElement("DefaultQrEcc", (int)DefaultQrEcc),
                    new XElement("ShowBarcodeText", ShowBarcodeText),
                    new XElement("DefaultFontSizeMm", DefaultFontSizeMm.ToString("0.##", CultureInfo.InvariantCulture)),
                    new XElement("DefaultFontFamily", Safe(DefaultFontFamily)),
                    new XElement("DefaultBold", DefaultBold),
                    new XElement("BarcodeHeightPercent", BarcodeHeightPercent),
                    new XElement("QrSizePercent", QrSizePercent),
                    new XElement("MarginTopMm", MarginTopMm.ToString("0.##", CultureInfo.InvariantCulture)),
                    new XElement("MarginBottomMm", MarginBottomMm.ToString("0.##", CultureInfo.InvariantCulture)),
                    new XElement("MarginLeftMm", MarginLeftMm.ToString("0.##", CultureInfo.InvariantCulture)),
                    new XElement("MarginRightMm", MarginRightMm.ToString("0.##", CultureInfo.InvariantCulture)),
                    new XElement("SettingsVersion", 2),
                    new XElement("TextAlign", (int)TextAlign),
                    new XElement("LastFolder", Safe(LastFolder)),
                    new XElement("LastImportFolder", Safe(LastImportFolder)),
                    new XElement("AutoCheckUpdate", AutoCheckUpdate),
                    new XElement("UpdateFeedUrl", Safe(UpdateFeedUrl)),
                    new XElement("LastUpdateCheck", Safe(LastUpdateCheck)),
                    new XElement("LastNotifiedVersion", Safe(LastNotifiedVersion))
                );
                new XDocument(root).Save(SettingsPath);
            }
            catch (Exception ex)
            {
                Logger.Error("保存设置失败", ex);
            }
        }

        public void Normalize()
        {
            if (Dpi != 203 && Dpi != 300 && Dpi != 600) Dpi = 600;
            if (Darkness < -30) Darkness = -30;
            if (Darkness > 30) Darkness = 30;
            if (Speed < 2) Speed = 2;
            if (Speed > 14) Speed = 14;
            if (NetworkPort <= 0 || NetworkPort > 65535) NetworkPort = 9100;
            if (DefaultFontSizeMm < 0) DefaultFontSizeMm = 0;
            if (DefaultFontSizeMm > 0 && DefaultFontSizeMm < 1.2) DefaultFontSizeMm = 1.2;
            if (DefaultFontSizeMm > 10.0) DefaultFontSizeMm = 10.0;
            if (string.IsNullOrWhiteSpace(DefaultFontFamily)) DefaultFontFamily = "Microsoft YaHei UI";
            if (BarcodeHeightPercent < 70) BarcodeHeightPercent = 70;
            if (BarcodeHeightPercent > 130) BarcodeHeightPercent = 130;
            if (QrSizePercent < 70) QrSizePercent = 70;
            if (QrSizePercent > 130) QrSizePercent = 130;
            MarginTopMm = ClampMargin(MarginTopMm);
            MarginBottomMm = ClampMargin(MarginBottomMm);
            MarginLeftMm = ClampMargin(MarginLeftMm);
            MarginRightMm = ClampMargin(MarginRightMm);
            SettingsVersion = 2;
            if (string.IsNullOrWhiteSpace(DefaultLabel)) DefaultLabel = LabelSpec.Label40x15.Name;
            if (NetworkHost == null) NetworkHost = "";
            if (PrinterName == null) PrinterName = "";
            if (LastFolder == null) LastFolder = "";
            if (LastImportFolder == null) LastImportFolder = "";
            if (UpdateFeedUrl == null) UpdateFeedUrl = "";
            if (LastUpdateCheck == null) LastUpdateCheck = "";
            if (LastNotifiedVersion == null) LastNotifiedVersion = "";
        }

        public LabelSpec GetLabelSpec()
        {
            return LabelSpec.FromName(DefaultLabel);
        }

        public PrintOptions CreatePrintOptions()
        {
            return new PrintOptions
            {
                Dpi = Dpi,
                Darkness = Darkness,
                Speed = Speed,
                MediaType = MediaType
            };
        }

        public RenderOptions CreateRenderOptions()
        {
            return new RenderOptions
            {
                Dpi = Dpi,
                LayoutMode = LayoutMode.Auto,
                BarcodeType = DefaultBarcodeType,
                QrEcc = DefaultQrEcc,
                ShowBarcodeText = ShowBarcodeText,
                TextAlign = TextAlign,
                MarginTopMm = MarginTopMm,
                MarginBottomMm = MarginBottomMm,
                MarginLeftMm = MarginLeftMm,
                MarginRightMm = MarginRightMm,
                FontSizeMm = DefaultFontSizeMm,
                FontFamily = DefaultFontFamily,
                Bold = DefaultBold,
                BarcodeHeightPercent = BarcodeHeightPercent,
                QrSizePercent = QrSizePercent
            };
        }

        private static double ClampMargin(double value)
        {
            if (value < 0) return 0;
            if (value > 20) return 20;
            return value;
        }

        private static string Safe(string value)
        {
            return value ?? "";
        }

        private static string Get(XElement root, string name, string fallback)
        {
            XElement e = root.Element(name);
            return e == null ? fallback : (e.Value ?? fallback);
        }

        private static int GetInt(XElement root, string name, int fallback)
        {
            XElement e = root.Element(name);
            int value;
            return e != null && int.TryParse(e.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static bool GetBool(XElement root, string name, bool fallback)
        {
            XElement e = root.Element(name);
            bool value;
            return e != null && bool.TryParse(e.Value, out value) ? value : fallback;
        }

        private static double GetDouble(XElement root, string name, double fallback)
        {
            XElement e = root.Element(name);
            double value;
            return e != null && double.TryParse(e.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
    }
}
