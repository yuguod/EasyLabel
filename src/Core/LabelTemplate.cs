using System;
using System.Drawing;

namespace EasyLabel.Core
{
    /// <summary>
    /// 一张标签的完整“配方”：规格 + 版式 + 自定义布局 + 一组示例内容。
    /// 保存成模版文件后可以反复打开，不需要每次重新调字体、边距和位置。
    /// </summary>
    public sealed class LabelTemplate
    {
        /// <summary>模版文件格式版本，将来字段变化时用于兼容读取。</summary>
        public const int FormatVersion = 1;

        public string Name { get; set; }

        /// <summary>可选备注，界面上作为说明显示。</summary>
        public string Note { get; set; }

        /// <summary>运行期字段：模版文件路径。不写入文件本身。</summary>
        public string FilePath { get; set; }

        // ---- 运行期字段：.zpl 文件相关，不写入 .elbl ----

        /// <summary>文件是 .zpl（而不是 EasyLabel 自己的 .elbl）。</summary>
        public bool IsZplFile { get; set; }

        /// <summary>
        /// 外部生成的 ZPL：没有内嵌模版信息，无法还原成可编辑的版式，只能直接打印。
        /// </summary>
        public bool IsPrintableOnly { get; set; }

        /// <summary>ZPL 原文。打印和导出都直接用这份内容。</summary>
        public string RawZpl { get; set; }

        public int ZplWidthDots { get; set; }
        public int ZplHeightDots { get; set; }
        public int ZplCopies { get; set; }

        public DateTime CreatedUtc { get; set; }
        public DateTime ModifiedUtc { get; set; }

        // ---- 标签规格 ----
        public string LabelName { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }

        // ---- 版式 ----
        public LayoutMode LayoutMode { get; set; }
        public BarcodeType BarcodeType { get; set; }
        public QrEcc QrEcc { get; set; }
        public TextAlign TextAlign { get; set; }
        public bool ShowBarcodeText { get; set; }
        public double MarginTopMm { get; set; }
        public double MarginBottomMm { get; set; }
        public double MarginLeftMm { get; set; }
        public double MarginRightMm { get; set; }
        public double FontSizeMm { get; set; }
        public string FontFamily { get; set; }
        public bool Bold { get; set; }
        public int BarcodeHeightPercent { get; set; }
        public int QrSizePercent { get; set; }

        /// <summary>布局编辑得到的位置/尺寸；null 表示使用自动布局。</summary>
        public LayoutPlan LayoutPlan { get; set; }

        // ---- 示例内容 ----
        public string Text { get; set; }
        public bool ShowBarcode { get; set; }
        public string BarcodeValue { get; set; }
        public bool BarcodeFromText { get; set; }
        public bool ShowQr { get; set; }
        public string QrValue { get; set; }
        public bool QrFromText { get; set; }

        public int Copies { get; set; }

        public LabelTemplate()
        {
            Name = "";
            Note = "";
            FilePath = "";
            LabelName = LabelSpec.Label40x15.Name;
            WidthMm = LabelSpec.Label40x15.WidthMm;
            HeightMm = LabelSpec.Label40x15.HeightMm;
            LayoutMode = LayoutMode.Auto;
            BarcodeType = BarcodeType.Code128;
            QrEcc = QrEcc.M;
            TextAlign = TextAlign.Center;
            ShowBarcodeText = true;
            MarginTopMm = 1.2;
            MarginBottomMm = 1.2;
            MarginLeftMm = 1.2;
            MarginRightMm = 1.2;
            FontSizeMm = 0;
            FontFamily = "Microsoft YaHei UI";
            Bold = false;
            BarcodeHeightPercent = 100;
            QrSizePercent = 100;
            Text = "";
            BarcodeValue = "";
            QrValue = "";
            RawZpl = "";
            BarcodeFromText = true;
            QrFromText = true;
            Copies = 1;
            CreatedUtc = DateTime.UtcNow;
            ModifiedUtc = CreatedUtc;
        }

        /// <summary>把当前界面状态打包成模版。</summary>
        public static LabelTemplate Capture(string name, LabelSpec spec, RenderOptions options,
            LabelContent content, int copies)
        {
            var template = new LabelTemplate();
            template.Name = name ?? "";
            if (spec != null)
            {
                template.LabelName = spec.Name;
                template.WidthMm = spec.WidthMm;
                template.HeightMm = spec.HeightMm;
            }

            if (options != null)
            {
                template.LayoutMode = options.LayoutMode;
                template.BarcodeType = options.BarcodeType;
                template.QrEcc = options.QrEcc;
                template.TextAlign = options.TextAlign;
                template.ShowBarcodeText = options.ShowBarcodeText;
                template.MarginTopMm = options.MarginTopMm;
                template.MarginBottomMm = options.MarginBottomMm;
                template.MarginLeftMm = options.MarginLeftMm;
                template.MarginRightMm = options.MarginRightMm;
                template.FontSizeMm = options.FontSizeMm;
                template.FontFamily = options.FontFamily;
                template.Bold = options.Bold;
                template.BarcodeHeightPercent = options.BarcodeHeightPercent;
                template.QrSizePercent = options.QrSizePercent;
                template.LayoutPlan = options.LayoutPlan == null ? null : options.LayoutPlan.Clone();
            }

            if (content != null)
            {
                template.Text = content.Text ?? "";
                template.ShowBarcode = content.ShowBarcode;
                template.BarcodeValue = content.BarcodeValue ?? "";
                template.BarcodeFromText = content.UseTextForBarcode;
                template.ShowQr = content.ShowQr;
                template.QrValue = content.QrValue ?? "";
                template.QrFromText = content.UseTextForQr;
            }

            template.Copies = copies;
            template.Normalize();
            return template;
        }

        /// <summary>取出标签规格；未知尺寸按自定义规格还原。</summary>
        public LabelSpec GetSpec()
        {
            double w = WidthMm, h = HeightMm;
            if (w < 5 || w > 300) w = 40;
            if (h < 5 || h > 300) h = 15;
            foreach (LabelSpec known in LabelSpec.All)
            {
                if (Math.Abs(known.WidthMm - w) < 0.001 && Math.Abs(known.HeightMm - h) < 0.001)
                    return known;
            }
            string name = string.IsNullOrWhiteSpace(LabelName) ? LabelSpec.FormatName(w, h) : LabelName;
            // 名字里带了尺寸就是自定义规格；否则按尺寸重新生成，避免下拉框显示成别的规格。
            if (LabelSpec.FromName(name).WidthMm != w || LabelSpec.FromName(name).HeightMm != h)
                name = LabelSpec.FormatName(w, h);
            return new LabelSpec(name, w, h);
        }

        /// <summary>按指定 DPI 生成渲染参数（版式与模版一致，内容另取）。</summary>
        public RenderOptions CreateRenderOptions(int dpi)
        {
            var options = new RenderOptions
            {
                Dpi = dpi,
                LayoutMode = LayoutMode,
                BarcodeType = BarcodeType,
                QrEcc = QrEcc,
                ShowBarcodeText = ShowBarcodeText,
                TextAlign = TextAlign,
                MarginTopMm = MarginTopMm,
                MarginBottomMm = MarginBottomMm,
                MarginLeftMm = MarginLeftMm,
                MarginRightMm = MarginRightMm,
                FontSizeMm = FontSizeMm,
                FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Microsoft YaHei UI" : FontFamily,
                Bold = Bold,
                LayoutPlan = LayoutPlan == null ? null : LayoutPlan.Clone(),
                BarcodeHeightPercent = BarcodeHeightPercent,
                QrSizePercent = QrSizePercent
            };
            options.Normalize();
            return options;
        }

        public LabelContent CreateContent()
        {
            return new LabelContent
            {
                Text = Text ?? "",
                ShowBarcode = ShowBarcode,
                BarcodeType = BarcodeType,
                BarcodeValue = BarcodeValue ?? "",
                UseTextForBarcode = BarcodeFromText,
                ShowQr = ShowQr,
                QrValue = QrValue ?? "",
                UseTextForQr = QrFromText
            };
        }

        public LabelTemplate Clone()
        {
            var copy = (LabelTemplate)MemberwiseClone();
            copy.LayoutPlan = LayoutPlan == null ? null : LayoutPlan.Clone();
            return copy;
        }

        /// <summary>把越界字段拉回合法范围；读取文件后必须调用。</summary>
        public void Normalize()
        {
            if (Name == null) Name = "";
            if (Note == null) Note = "";
            if (FilePath == null) FilePath = "";
            if (Text == null) Text = "";
            if (BarcodeValue == null) BarcodeValue = "";
            if (QrValue == null) QrValue = "";
            if (RawZpl == null) RawZpl = "";
            if (IsPrintableOnly) IsZplFile = true;

            if (double.IsNaN(WidthMm) || WidthMm < 5) WidthMm = 40;
            if (WidthMm > 300) WidthMm = 300;
            if (double.IsNaN(HeightMm) || HeightMm < 5) HeightMm = 15;
            if (HeightMm > 300) HeightMm = 300;
            if (string.IsNullOrWhiteSpace(LabelName)) LabelName = LabelSpec.FormatName(WidthMm, HeightMm);

            if (!Enum.IsDefined(typeof(LayoutMode), LayoutMode)) LayoutMode = LayoutMode.Auto;
            if (!Enum.IsDefined(typeof(BarcodeType), BarcodeType)) BarcodeType = BarcodeType.Code128;
            if (!Enum.IsDefined(typeof(QrEcc), QrEcc)) QrEcc = QrEcc.M;
            if (!Enum.IsDefined(typeof(TextAlign), TextAlign)) TextAlign = TextAlign.Center;

            if (Copies < 1) Copies = 1;
            if (Copies > 9999) Copies = 9999;
            if (CreatedUtc == default(DateTime)) CreatedUtc = DateTime.UtcNow;
            if (ModifiedUtc == default(DateTime)) ModifiedUtc = CreatedUtc;

            // 借用 RenderOptions 的边界规则，避免两处各写一套数字。
            RenderOptions rules = CreateRenderOptions(600);
            MarginTopMm = rules.MarginTopMm;
            MarginBottomMm = rules.MarginBottomMm;
            MarginLeftMm = rules.MarginLeftMm;
            MarginRightMm = rules.MarginRightMm;
            FontSizeMm = rules.FontSizeMm;
            FontFamily = rules.FontFamily;
            BarcodeHeightPercent = rules.BarcodeHeightPercent;
            QrSizePercent = rules.QrSizePercent;

            if (LayoutPlan != null) LayoutPlan.Normalize(GetSpec());
        }

        public string LayoutModeName
        {
            get
            {
                switch (LayoutMode)
                {
                    case LayoutMode.TextOnly: return "纯文本";
                    case LayoutMode.BarcodeOnly: return "仅一维码";
                    case LayoutMode.QrOnly: return "仅二维码";
                    case LayoutMode.TextBarcode: return "文本+一维码";
                    case LayoutMode.TextQr: return "文本+二维码";
                    case LayoutMode.BarcodeQr: return "一维码+二维码";
                    case LayoutMode.All: return "全部";
                    default: return "自动";
                }
            }
        }

        public string BarcodeTypeName
        {
            get
            {
                switch (BarcodeType)
                {
                    case BarcodeType.Code39: return "Code 39";
                    case BarcodeType.Ean13: return "EAN-13";
                    default: return "Code 128";
                }
            }
        }

        /// <summary>列表里显示的类型：EasyLabel 自己的模版，还是纯 ZPL 文件。</summary>
        public string FileTypeName
        {
            get { return IsZplFile ? "ZPL" : "模版"; }
        }

        /// <summary>一句规格说明；纯 ZPL 没有版式，用点数描述。</summary>
        public string SizeText
        {
            get
            {
                if (IsPrintableOnly && ZplWidthDots > 0 && ZplHeightDots > 0)
                    return ZplWidthDots + " × " + ZplHeightDots + " 点";
                return GetSpec().Name;
            }
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Name) ? "未命名模版" : Name;
        }
    }
}