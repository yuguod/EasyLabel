using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Drawing;

namespace EasyLabel.Core
{
    /// <summary>
    /// 模版库：一个目录下的 *.elbl（UTF-8 XML，含全部版式）与 *.zpl（原生 ZPL 指令）。
    /// 默认目录是 AppSettings.DataDirectory\templates，便携模式下随程序目录走。
    /// </summary>
    public sealed class TemplateStore
    {
        public const string Extension = ".elbl";

        /// <summary>原生 ZPL 文件。既能直发打印机，也带内嵌信息可再编辑。</summary>
        public const string ZplExtension = ZplTemplateFile.Extension;

        private const string RootName = "EasyLabelTemplate";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly string _directory;

        public TemplateStore(string directory)
        {
            _directory = string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(AppSettings.DataDirectory, "templates")
                : directory;
        }

        public static TemplateStore Default
        {
            get { return new TemplateStore(Path.Combine(AppSettings.DataDirectory, "templates")); }
        }

        public string DirectoryPath
        {
            get { return _directory; }
        }

        public string PathFor(string name)
        {
            return PathFor(name, false);
        }

        public string PathFor(string name, bool zpl)
        {
            return Path.Combine(_directory, SanitizeFileName(name) + (zpl ? ZplExtension : Extension));
        }

        public bool Exists(string name)
        {
            return Exists(name, false);
        }

        public bool Exists(string name, bool zpl)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return File.Exists(PathFor(name, zpl));
        }

        /// <summary>文件名里不能出现的字符统一换成下划线；模版显示名保持原样。</summary>
        public static string SanitizeFileName(string name)
        {
            string value = (name ?? "").Trim();
            if (value.Length == 0) return "未命名模版";

            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            value = value.Replace('\\', '_').Replace('/', '_').Trim();
            value = value.TrimEnd('.', ' ');
            if (value.Length == 0) return "未命名模版";
            if (value.Length > 80)
            {
                value = value.Substring(0, 80).Trim();
                if (value.Length == 0) return "未命名模版";
            }
            return value;
        }

        /// <summary>列出全部模版（.elbl 与 .zpl），按最近修改时间倒序；读不了的文件跳过，不影响其他模版。</summary>
        public List<LabelTemplate> List()
        {
            var result = new List<LabelTemplate>();
            try
            {
                if (!Directory.Exists(_directory)) return result;
                foreach (string path in EnumerateFiles())
                {
                    try
                    {
                        result.Add(Load(path));
                    }
                    catch (Exception ex)
                    {
                        Logger.Info("跳过无法读取的模版：" + path + "（" + ex.Message + "）");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("枚举模版目录失败：" + _directory, ex);
            }
            result.Sort(delegate(LabelTemplate a, LabelTemplate b)
            {
                int byTime = b.ModifiedUtc.CompareTo(a.ModifiedUtc);
                if (byTime != 0) return byTime;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        private IEnumerable<string> EnumerateFiles()
        {
            foreach (string path in Directory.GetFiles(_directory, "*" + Extension)) yield return path;
            foreach (string path in Directory.GetFiles(_directory, "*" + ZplExtension)) yield return path;
        }

        public LabelTemplate Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidDataException("模版文件不存在：" + path);

            if (string.Equals(Path.GetExtension(path), ZplExtension, StringComparison.OrdinalIgnoreCase))
                return LoadZpl(path);

            try
            {
                XDocument doc = XDocument.Load(path);
                return FromXml(doc, path);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("模版文件已损坏或不是 EasyLabel 模版：" + Path.GetFileName(path), ex);
            }
        }

        /// <summary>
        /// 读取 .zpl：带内嵌模版信息的还原成完整模版；外部 ZPL 只能当“直发文件”（IsPrintableOnly）。
        /// </summary>
        public LabelTemplate LoadZpl(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidDataException("ZPL 文件不存在：" + path);

            string text;
            try
            {
                text = ZplTemplateFile.ReadText(path);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("无法读取 ZPL 文件：" + Path.GetFileName(path), ex);
            }

            if (!ZplTemplateFile.LooksLikeZpl(text))
                throw new InvalidDataException("不是 ZPL 文件（找不到 ^XA）：" + Path.GetFileName(path));

            DateTime written = File.GetLastWriteTimeUtc(path);
            LabelTemplate template;
            if (ZplTemplateFile.TryReadTemplate(text, out template))
            {
                template.FilePath = path;
                template.IsZplFile = true;
                template.IsPrintableOnly = false;
                template.RawZpl = text;
                template.ZplCopies = ZplTemplateFile.Inspect(text).Copies;
                if (string.IsNullOrWhiteSpace(template.Name))
                    template.Name = Path.GetFileNameWithoutExtension(path);
                if (template.ModifiedUtc == default(DateTime) || template.ModifiedUtc.Year < 2000)
                    template.ModifiedUtc = written;
                template.Normalize();
                return template;
            }

            ZplSummary summary = ZplTemplateFile.Inspect(text);
            var raw = new LabelTemplate
            {
                Name = Path.GetFileNameWithoutExtension(path),
                FilePath = path,
                IsZplFile = true,
                IsPrintableOnly = true,
                RawZpl = text,
                ZplWidthDots = summary.WidthDots,
                ZplHeightDots = summary.HeightDots,
                ZplCopies = summary.Copies,
                CreatedUtc = written,
                ModifiedUtc = written
            };
            // 点数换算成毫米只用于界面上的大致显示（按 203 dpi 估），不参与渲染。
            if (summary.WidthDots > 0) raw.WidthMm = Math.Round(summary.WidthDots * 25.4 / 203.0, 1);
            if (summary.HeightDots > 0) raw.HeightMm = Math.Round(summary.HeightDots * 25.4 / 203.0, 1);
            raw.Normalize();
            return raw;
        }

        /// <summary>保存到模版库；同名文件直接覆盖。返回实际文件路径。</summary>
        public string Save(LabelTemplate template)
        {
            if (template == null) throw new ArgumentNullException("template");
            template.Name = (template.Name ?? "").Trim();
            if (template.Name.Length == 0) template.Name = "未命名模版";
            template.Normalize();
            if (template.CreatedUtc == default(DateTime)) template.CreatedUtc = DateTime.UtcNow;
            template.ModifiedUtc = DateTime.UtcNow;

            Directory.CreateDirectory(_directory);
            string path = PathFor(template.Name);
            WriteAtomically(path, ToXml(template));
            template.FilePath = path;
            template.IsZplFile = false;
            template.IsPrintableOnly = false;
            template.RawZpl = "";
            Logger.Info("保存模版：" + template.Name + " -> " + path);
            return path;
        }

        /// <summary>
        /// 保存成 .zpl 模版：正文是可直接发给打印机的 ZPL，同时把模版信息以注释形式写在里面，
        /// 之后还能被 EasyLabel 打开继续编辑。返回实际文件路径。
        /// </summary>
        public string SaveZpl(LabelTemplate template, string zplText)
        {
            if (template == null) throw new ArgumentNullException("template");
            if (string.IsNullOrEmpty(zplText)) throw new ArgumentException("ZPL 内容为空", "zplText");

            template.Name = (template.Name ?? "").Trim();
            if (template.Name.Length == 0) template.Name = "未命名模版";
            template.Normalize();
            if (template.CreatedUtc == default(DateTime)) template.CreatedUtc = DateTime.UtcNow;
            template.ModifiedUtc = DateTime.UtcNow;

            string content = ZplTemplateFile.Embed(template, zplText);
            Directory.CreateDirectory(_directory);
            string path = PathFor(template.Name, true);
            WriteTextAtomically(path, content);
            template.FilePath = path;
            template.IsZplFile = true;
            template.IsPrintableOnly = false;
            template.RawZpl = content;
            template.ZplCopies = ZplTemplateFile.Inspect(content).Copies;
            template.ZplWidthDots = ZplTemplateFile.Inspect(content).WidthDots;
            template.ZplHeightDots = ZplTemplateFile.Inspect(content).HeightDots;
            Logger.Info("保存 ZPL 模版：" + template.Name + " -> " + path);
            return path;
        }

        public void Delete(LabelTemplate template)
        {
            if (template == null) return;
            Delete(template.FilePath);
        }

        public void Delete(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            File.Delete(path);
            Logger.Info("删除模版：" + path);
        }

        /// <summary>改名相当于“换一个文件”，旧文件在同名覆盖后清掉。</summary>
        public string Rename(LabelTemplate template, string newName)
        {
            if (template == null) throw new ArgumentNullException("template");
            string oldPath = template.FilePath;
            string trimmed = (newName ?? "").Trim();
            if (trimmed.Length == 0) throw new ArgumentException("模版名称不能为空", "newName");

            bool zpl = template.IsZplFile;
            string newPath = PathFor(trimmed, zpl);
            if (!string.IsNullOrEmpty(oldPath) &&
                string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            {
                // 只是大小写或首尾空格变化，直接原地保存。
                template.Name = trimmed;
                return zpl ? SaveZpl(template, template.RawZpl) : Save(template);
            }

            template.Name = trimmed;
            string saved;
            if (zpl)
            {
                // 纯 ZPL 直接照抄原文；带内嵌信息的重新写一遍，免得文件里的旧名字留着。
                string content = template.IsPrintableOnly || string.IsNullOrEmpty(template.RawZpl)
                    ? template.RawZpl
                    : ZplTemplateFile.Embed(template, template.RawZpl);
                Directory.CreateDirectory(_directory);
                WriteTextAtomically(newPath, content ?? "");
                template.FilePath = newPath;
                template.RawZpl = content ?? "";
                saved = newPath;
            }
            else
            {
                saved = Save(template);
            }

            if (!string.IsNullOrEmpty(oldPath) && File.Exists(oldPath) &&
                !string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(saved), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(oldPath); }
                catch (Exception ex) { Logger.Error("删除旧模版文件失败：" + oldPath, ex); }
            }
            return saved;
        }

        /// <summary>从外部文件导入到模版库；重名时自动加序号，不覆盖已有模版。.zpl 保持原扩展名。</summary>
        public LabelTemplate Import(string path)
        {
            if (string.Equals(Path.GetExtension(path), ZplExtension, StringComparison.OrdinalIgnoreCase))
            {
                // 先读一遍做校验：不是 ZPL 或读不了的文件不进库。
                LabelTemplate source = LoadZpl(path);
                string name = UniqueName(source.Name, true);
                Directory.CreateDirectory(_directory);
                string target = PathFor(name, true);
                if (source.IsPrintableOnly || string.IsNullOrEmpty(source.RawZpl))
                {
                    File.Copy(path, target, true);
                }
                else
                {
                    source.Name = name;
                    WriteTextAtomically(target, ZplTemplateFile.Embed(source, source.RawZpl));
                }
                return Load(target);
            }

            LabelTemplate template = Load(path);
            if (Exists(template.Name)) template.Name = UniqueName(template.Name);
            Save(template);
            return template;
        }

        public void Export(LabelTemplate template, string path)
        {
            if (template == null) throw new ArgumentNullException("template");
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("导出路径不能为空", "path");
            if (template.IsZplFile)
            {
                string text = template.RawZpl;
                if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(template.FilePath) && File.Exists(template.FilePath))
                    text = ZplTemplateFile.ReadText(template.FilePath);
                WriteTextAtomically(path, text ?? "");
                Logger.Info("导出 ZPL 模版：" + template.Name + " -> " + path);
                return;
            }
            template.Normalize();
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            WriteAtomically(path, ToXml(template));
            Logger.Info("导出模版：" + template.Name + " -> " + path);
        }

        public string UniqueName(string name)
        {
            return UniqueName(name, false);
        }

        public string UniqueName(string name, bool zpl)
        {
            string baseName = (name ?? "").Trim();
            if (baseName.Length == 0) baseName = zpl ? "ZPL 文件" : "模版";
            if (!Exists(baseName, zpl)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + " (" + i + ")";
                if (!Exists(candidate, zpl)) return candidate;
            }
            return baseName + " (" + DateTime.Now.ToString("HHmmss", Inv) + ")";
        }

        // ---------------- 序列化 ----------------

        /// <summary>把模版序列化成 XML 文本；.zpl 的内嵌信息复用同一套字段，避免两处格式不一致。</summary>
        public static string Serialize(LabelTemplate template)
        {
            if (template == null) throw new ArgumentNullException("template");
            template.Normalize();
            return ToXml(template).ToString();
        }

        public static LabelTemplate Deserialize(string xml, string path)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            XDocument doc = XDocument.Parse(xml, LoadOptions.None);
            return FromXml(doc, path ?? "");
        }

        private static XDocument ToXml(LabelTemplate t)
        {
            var root = new XElement(RootName,
                new XAttribute("version", LabelTemplate.FormatVersion),
                new XElement("Name", t.Name ?? ""),
                new XElement("Note", t.Note ?? ""),
                new XElement("CreatedUtc", FormatDate(t.CreatedUtc)),
                new XElement("ModifiedUtc", FormatDate(t.ModifiedUtc)),
                new XElement("Label",
                    new XAttribute("name", t.LabelName ?? ""),
                    new XAttribute("widthMm", Num(t.WidthMm)),
                    new XAttribute("heightMm", Num(t.HeightMm))),
                new XElement("Layout",
                    new XAttribute("mode", (int)t.LayoutMode),
                    new XAttribute("barcodeType", (int)t.BarcodeType),
                    new XAttribute("qrEcc", (int)t.QrEcc),
                    new XAttribute("textAlign", (int)t.TextAlign),
                    new XAttribute("showBarcodeText", t.ShowBarcodeText),
                    new XAttribute("marginTopMm", Num(t.MarginTopMm)),
                    new XAttribute("marginBottomMm", Num(t.MarginBottomMm)),
                    new XAttribute("marginLeftMm", Num(t.MarginLeftMm)),
                    new XAttribute("marginRightMm", Num(t.MarginRightMm)),
                    new XAttribute("fontFamily", t.FontFamily ?? ""),
                    new XAttribute("fontSizeMm", Num(t.FontSizeMm)),
                    new XAttribute("bold", t.Bold),
                    new XAttribute("barcodeHeightPercent", t.BarcodeHeightPercent),
                    new XAttribute("qrSizePercent", t.QrSizePercent)),
                PlanToXml(t.LayoutPlan),
                new XElement("Content",
                    new XAttribute("showBarcode", t.ShowBarcode),
                    new XAttribute("barcodeFromText", t.BarcodeFromText),
                    new XAttribute("showQr", t.ShowQr),
                    new XAttribute("qrFromText", t.QrFromText),
                    new XAttribute("copies", t.Copies),
                    // 正文用元素承载，属性值里的换行会被 XML 规范化成空格。
                    new XElement("Text", t.Text ?? ""),
                    new XElement("Barcode", t.BarcodeValue ?? ""),
                    new XElement("Qr", t.QrValue ?? "")));
            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        private static XElement PlanToXml(LayoutPlan plan)
        {
            if (plan == null) return new XElement("Plan", new XAttribute("enabled", false));
            return new XElement("Plan",
                new XAttribute("enabled", true),
                new XAttribute("barHeightRatio", Num(plan.BarHeightRatio)),
                new XAttribute("hriGapMm", Num(plan.HriGapMm)),
                RectToXml("Text", plan.Text),
                RectToXml("Barcode", plan.Barcode),
                RectToXml("Qr", plan.Qr));
        }

        private static XElement RectToXml(string name, RectangleF rect)
        {
            return new XElement(name,
                new XAttribute("x", Num(rect.X)),
                new XAttribute("y", Num(rect.Y)),
                new XAttribute("width", Num(rect.Width)),
                new XAttribute("height", Num(rect.Height)));
        }

        private static LabelTemplate FromXml(XDocument doc, string path)
        {
            XElement root = doc.Root;
            if (root == null || root.Name.LocalName != RootName)
                throw new InvalidDataException("不是 EasyLabel 模版文件：" + Path.GetFileName(path));

            var t = new LabelTemplate();
            t.FilePath = path;
            t.Name = (string)root.Element("Name") ?? "";
            t.Note = (string)root.Element("Note") ?? "";
            t.CreatedUtc = ParseDate((string)root.Element("CreatedUtc"), DateTime.UtcNow);
            t.ModifiedUtc = ParseDate((string)root.Element("ModifiedUtc"), t.CreatedUtc);

            XElement label = root.Element("Label");
            if (label != null)
            {
                t.LabelName = (string)label.Attribute("name") ?? "";
                t.WidthMm = ParseDouble((string)label.Attribute("widthMm"), 40);
                t.HeightMm = ParseDouble((string)label.Attribute("heightMm"), 15);
            }

            XElement layout = root.Element("Layout");
            if (layout != null)
            {
                t.LayoutMode = (LayoutMode)ParseInt((string)layout.Attribute("mode"), 0);
                t.BarcodeType = (BarcodeType)ParseInt((string)layout.Attribute("barcodeType"), 0);
                t.QrEcc = (QrEcc)ParseInt((string)layout.Attribute("qrEcc"), 1);
                t.TextAlign = (TextAlign)ParseInt((string)layout.Attribute("textAlign"), 1);
                t.ShowBarcodeText = ParseBool((string)layout.Attribute("showBarcodeText"), true);
                t.MarginTopMm = ParseDouble((string)layout.Attribute("marginTopMm"), 1.2);
                t.MarginBottomMm = ParseDouble((string)layout.Attribute("marginBottomMm"), 1.2);
                t.MarginLeftMm = ParseDouble((string)layout.Attribute("marginLeftMm"), 1.2);
                t.MarginRightMm = ParseDouble((string)layout.Attribute("marginRightMm"), 1.2);
                t.FontFamily = (string)layout.Attribute("fontFamily") ?? "Microsoft YaHei UI";
                t.FontSizeMm = ParseDouble((string)layout.Attribute("fontSizeMm"), 0);
                t.Bold = ParseBool((string)layout.Attribute("bold"), false);
                t.BarcodeHeightPercent = ParseInt((string)layout.Attribute("barcodeHeightPercent"), 100);
                t.QrSizePercent = ParseInt((string)layout.Attribute("qrSizePercent"), 100);
            }

            XElement plan = root.Element("Plan");
            if (plan != null && ParseBool((string)plan.Attribute("enabled"), false))
            {
                t.LayoutPlan = new LayoutPlan
                {
                    Text = ParseRect(plan.Element("Text")),
                    Barcode = ParseRect(plan.Element("Barcode")),
                    Qr = ParseRect(plan.Element("Qr")),
                    BarHeightRatio = (float)ParseDouble((string)plan.Attribute("barHeightRatio"), 0),
                    HriGapMm = (float)ParseDouble((string)plan.Attribute("hriGapMm"), LayoutPlan.DefaultHriGapMm)
                };
            }

            XElement content = root.Element("Content");
            if (content != null)
            {
                t.ShowBarcode = ParseBool((string)content.Attribute("showBarcode"), false);
                t.BarcodeFromText = ParseBool((string)content.Attribute("barcodeFromText"), true);
                t.ShowQr = ParseBool((string)content.Attribute("showQr"), false);
                t.QrFromText = ParseBool((string)content.Attribute("qrFromText"), true);
                t.Copies = ParseInt((string)content.Attribute("copies"), 1);
                t.Text = (string)content.Element("Text") ?? "";
                t.BarcodeValue = (string)content.Element("Barcode") ?? "";
                t.QrValue = (string)content.Element("Qr") ?? "";
            }

            if (string.IsNullOrWhiteSpace(t.Name))
                t.Name = Path.GetFileNameWithoutExtension(path);
            t.Normalize();
            return t;
        }

        private static RectangleF ParseRect(XElement element)
        {
            if (element == null) return RectangleF.Empty;
            float x = (float)ParseDouble((string)element.Attribute("x"), 0);
            float y = (float)ParseDouble((string)element.Attribute("y"), 0);
            float w = (float)ParseDouble((string)element.Attribute("width"), 0);
            float h = (float)ParseDouble((string)element.Attribute("height"), 0);
            if (w <= 0 || h <= 0) return RectangleF.Empty;
            return new RectangleF(x, y, w, h);
        }

        private static void WriteAtomically(string path, XDocument doc)
        {
            string temp = path + ".tmp";
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  "
            };
            using (XmlWriter writer = XmlWriter.Create(temp, settings))
            {
                doc.Save(writer);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static void WriteTextAtomically(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text ?? "", new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static string Num(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
            return value.ToString("0.####", Inv);
        }

        private static string Num(float value)
        {
            return Num((double)value);
        }

        private static string FormatDate(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
            return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv);
        }

        private static DateTime ParseDate(string value, DateTime fallback)
        {
            DateTime parsed;
            if (!string.IsNullOrWhiteSpace(value) &&
                DateTime.TryParse(value, Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                return parsed;
            return fallback;
        }

        private static double ParseDouble(string value, double fallback)
        {
            double parsed;
            if (!string.IsNullOrWhiteSpace(value) &&
                double.TryParse(value, NumberStyles.Float, Inv, out parsed) &&
                !double.IsNaN(parsed) && !double.IsInfinity(parsed))
                return parsed;
            return fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return !string.IsNullOrWhiteSpace(value) &&
                   int.TryParse(value, NumberStyles.Integer, Inv, out parsed) ? parsed : fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            return !string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out parsed) ? parsed : fallback;
        }
    }
}
