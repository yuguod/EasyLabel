using System;

namespace EasyLabel.Core
{
    /// <summary>渲染参数。所有尺寸在内部按打印机点阵计算。</summary>
    public sealed class RenderOptions
    {
        public int Dpi { get; set; }
        public LayoutMode LayoutMode { get; set; }
        public BarcodeType BarcodeType { get; set; }
        public QrEcc QrEcc { get; set; }
        public bool ShowBarcodeText { get; set; }
        public TextAlign TextAlign { get; set; }
        public double MarginTopMm { get; set; }
        public double MarginBottomMm { get; set; }
        public double MarginLeftMm { get; set; }
        public double MarginRightMm { get; set; }

        /// <summary>
        /// 打印偏移补偿（mm）：正数把整幅内容往右/往下挪。
        /// 用来抵消打印机机械起始位置、非打印区或标签在纸上的贴偏——
        /// 这类偏移每台机器固定，跟版式无关，所以在渲染最后一刻整体平移。
        /// </summary>
        public double OffsetXMm { get; set; }
        public double OffsetYMm { get; set; }
        public double FontSizeMm { get; set; }
        public string FontFamily { get; set; }
        public bool Bold { get; set; }
        public LayoutPlan LayoutPlan { get; set; }
        public int BarcodeHeightPercent { get; set; }
        public int QrSizePercent { get; set; }

        public RenderOptions()
        {
            Dpi = 600;
            LayoutMode = LayoutMode.Auto;
            BarcodeType = BarcodeType.Code128;
            QrEcc = QrEcc.M;
            ShowBarcodeText = true;
            TextAlign = TextAlign.Center;
            MarginTopMm = 1.2;
            MarginBottomMm = 1.2;
            MarginLeftMm = 1.2;
            MarginRightMm = 1.2;
            FontSizeMm = 0;
            FontFamily = "Microsoft YaHei UI";
            Bold = false;
            LayoutPlan = null;
            BarcodeHeightPercent = 100;
            QrSizePercent = 100;
        }

        public RenderOptions Clone()
        {
            return (RenderOptions)MemberwiseClone();
        }

        public void Normalize()
        {
            if (Dpi != 203 && Dpi != 300 && Dpi != 600) Dpi = 600;
            // 上边距和左边距允许负数：负数等于把内容整体往左/往上挪（超出部分被裁掉），
            // 这是除了“打印偏移补偿”之外的手动补偿手段。右边距/下边距保持 0 起，
            // 因为负数会让自动布局越算越大，没有实际用处。
            MarginTopMm = ClampMargin(MarginTopMm, MarginMinMm);
            MarginLeftMm = ClampMargin(MarginLeftMm, MarginMinMm);
            MarginBottomMm = ClampMargin(MarginBottomMm, 0);
            MarginRightMm = ClampMargin(MarginRightMm, 0);
            OffsetXMm = ClampMargin(OffsetXMm, MarginMinMm);
            OffsetYMm = ClampMargin(OffsetYMm, MarginMinMm);
            if (FontSizeMm < 0) FontSizeMm = 0;
            if (FontSizeMm > 0 && FontSizeMm < 1.2) FontSizeMm = 1.2;
            if (FontSizeMm > 10.0) FontSizeMm = 10.0;
            if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = "Microsoft YaHei UI";
            if (BarcodeHeightPercent < 70) BarcodeHeightPercent = 70;
            if (BarcodeHeightPercent > 130) BarcodeHeightPercent = 130;
            if (QrSizePercent < 70) QrSizePercent = 70;
            if (QrSizePercent > 130) QrSizePercent = 130;
        }

        /// <summary>边距/偏移允许的最小值（mm）。</summary>
        public const double MarginMinMm = -20;

        /// <summary>边距/偏移允许的最大值（mm）。</summary>
        public const double MarginMaxMm = 20;

        private static double ClampMargin(double value, double min)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (value < min) return min;
            if (value > MarginMaxMm) return MarginMaxMm;
            return value;
        }

        public bool HasOffset
        {
            get { return Math.Abs(OffsetXMm) > 0.004 || Math.Abs(OffsetYMm) > 0.004; }
        }

        public string OffsetText
        {
            get
            {
                if (!HasOffset) return "";
                return "打印偏移 X " + OffsetXMm.ToString("0.0") + " / Y " + OffsetYMm.ToString("0.0") + " mm";
            }
        }

        public double DotsPerMm
        {
            get { return Dpi / 25.4; }
        }

        public float MmToPx(double mm)
        {
            return (float)(mm * DotsPerMm);
        }

        public int MmToDots(double mm)
        {
            return (int)Math.Round(mm * DotsPerMm, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>打印参数（与版式无关）。</summary>
    public sealed class PrintOptions
    {
        public int Dpi { get; set; }
        public int Darkness { get; set; }
        public int Speed { get; set; }
        public MediaType MediaType { get; set; }

        public PrintOptions()
        {
            Dpi = 600;
            Darkness = 0;
            Speed = 4;
            MediaType = MediaType.Gap;
        }

        public void Normalize()
        {
            if (Dpi != 203 && Dpi != 300 && Dpi != 600) Dpi = 600;
            if (Darkness < -30) Darkness = -30;
            if (Darkness > 30) Darkness = 30;
            if (Speed < 2) Speed = 2;
            if (Speed > 14) Speed = 14;
        }

        public PrintOptions Clone()
        {
            return (PrintOptions)MemberwiseClone();
        }
    }
}
