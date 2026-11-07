using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using EasyLabel.Core;
using ZXing;

internal static class TestRunner
{
    private static int _passed;
    private static int _failed;
    private static string _outDir;

    private static void Main(string[] args)
    {
        _outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "out");
        Directory.CreateDirectory(_outDir);

        // 测试期间不要动用户的字体目录。
        FontLibrary.ResetForTests();
        FontLibrary.FontDirectory = Path.Combine(_outDir, "fonts-ambient");

        TestSpecs();
        TestDpiGuess();
        TestNewSizes();
        TestBarcodeRoundTrip();
        TestQrRoundTrip();
        TestRenderAndDecode40x15();
        TestRenderAndDecode30x25();
        TestRender30x20_600();
        TestTextOnly();
        TestHighResolution();
        TestMarginsAndFont();
        TestBold();
        TestLayoutPlan();
        TestLayoutPlanRoundTrip();
        TestMarginsWithLayoutPlan();
        TestPrintOffsetAndNegativeMargins();
        TestLegacyLayoutMigration();
        TestLayoutHelpers();
        TestZplCopies();
        TestTemplates();
        TestZplTemplates();
        TestImportPrint();
        TestExcelImport();
        TestCsvImport();
        TestZpl();
        TestTestPage();
        TestValidation();
        TestFontSize();
        TestTextAlign();
        TestFontLibrary();
        TestUpdates();
        TestLabelary();

        Console.WriteLine();
        Console.WriteLine("PASS=" + _passed + " FAIL=" + _failed);
        Environment.ExitCode = _failed == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string name, string detail)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine("[PASS] " + name + (string.IsNullOrEmpty(detail) ? "" : "  " + detail));
        }
        else
        {
            _failed++;
            Console.WriteLine("[FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : "  " + detail));
        }
    }

    private static void Skip(string name, string detail)
    {
        Console.WriteLine("[SKIP] " + name + (string.IsNullOrEmpty(detail) ? "" : "  " + detail));
    }

    private static void TestSpecs()
    {
        Check(LabelSpec.Label40x15.WidthDots(203) == 320, "40x15 width @203", LabelSpec.Label40x15.WidthDots(203).ToString());
        Check(LabelSpec.Label40x15.HeightDots(203) == 120, "40x15 height @203", LabelSpec.Label40x15.HeightDots(203).ToString());
        Check(LabelSpec.Label30x25.WidthDots(203) == 240, "30x25 width @203", LabelSpec.Label30x25.WidthDots(203).ToString());
        Check(LabelSpec.Label30x25.HeightDots(203) == 200, "30x25 height @203", LabelSpec.Label30x25.HeightDots(203).ToString());
        Check(LabelSpec.Label40x15.WidthDots(300) == 472, "40x15 width @300", LabelSpec.Label40x15.WidthDots(300).ToString());
        Check(LabelSpec.Label40x15.HeightDots(600) == 354, "40x15 height @600", LabelSpec.Label40x15.HeightDots(600).ToString());
    }

    private static void TestDpiGuess()
    {
        Check(PrinterService.GuessDpi("ZDesigner ZT610-203dpi ZPL") == "203", "guess 203", "");
        Check(PrinterService.GuessDpi("ZDesigner ZT610-300dpi ZPL") == "300", "guess 300", "");
        Check(PrinterService.GuessDpi("ZDesigner ZT610-600dpi ZPL") == "600", "guess 600", "");
        Check(PrinterService.GuessDpi("Zebra ZT610") == "", "guess unknown", "");
    }

    private static void TestNewSizes()
    {
        Check(LabelSpec.Label30x20.WidthDots(600) == 709, "30x20 width @600", LabelSpec.Label30x20.WidthDots(600).ToString());
        Check(LabelSpec.Label30x20.HeightDots(600) == 472, "30x20 height @600", LabelSpec.Label30x20.HeightDots(600).ToString());
        LabelSpec custom = LabelSpec.FromName("35×22 mm");
        Check(Math.Abs(custom.WidthMm - 35) < 0.001 && Math.Abs(custom.HeightMm - 22) < 0.001,
            "custom size parse", custom.Name);
        Check(LabelSpec.FromName("30×20 mm") == LabelSpec.Label30x20, "static 30x20 parse", "");
        Check(LabelSpec.All.Count == 3, "label size list", LabelSpec.All.Count.ToString());
        Check(new AppSettings().Dpi == 600, "default dpi 600", new AppSettings().Dpi.ToString());
    }

    private static void TestRender30x20_600()
    {
        var content = new LabelContent
        {
            Text = "产品C",
            ShowBarcode = true,
            BarcodeType = BarcodeType.Code128,
            BarcodeValue = "C-2026-003",
            ShowQr = true,
            QrValue = "https://example.com/C-003",
            UseTextForQr = false
        };
        RenderCase("30x20_all_600", LabelSpec.Label30x20, content,
            new RenderOptions { Dpi = 600, LayoutMode = LayoutMode.All, QrEcc = QrEcc.M }, true, true);
    }

    private static void TestMarginsAndFont()
    {
        var content = new LabelContent { Text = "边距字体测试" };
        var options = new RenderOptions
        {
            Dpi = 600,
            LayoutMode = LayoutMode.TextOnly,
            MarginTopMm = 4,
            MarginBottomMm = 2,
            MarginLeftMm = 3,
            MarginRightMm = 5,
            FontFamily = "SimHei",
            FontSizeMm = 5
        };
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            LabelLayout layout = LabelLayout.Build(LabelSpec.Label40x15, content, options, g);
            Check(layout.IsValid, "margins layout valid", string.Join("；", layout.Errors.ToArray()));
            Check(Math.Abs(layout.TextRect.Left - options.MmToPx(3)) < 1.5, "left margin", layout.TextRect.Left.ToString("0.0"));
            Check(Math.Abs(layout.TextRect.Top - options.MmToPx(4)) < 1.5, "top margin", layout.TextRect.Top.ToString("0.0"));
            Check(Math.Abs(layout.TextRect.Width - options.MmToPx(32)) < 1.5, "margin width", layout.TextRect.Width.ToString("0.0"));
            Check(Math.Abs(layout.TextRect.Height - options.MmToPx(9)) < 1.5, "margin height", layout.TextRect.Height.ToString("0.0"));
        }
        using (RenderedLabel rendered = LabelRenderer.Render(LabelSpec.Label40x15, content, options))
        {
            Check(rendered.WidthDots == 945 && rendered.HeightDots == 354, "600dpi custom font render", rendered.WidthDots + "x" + rendered.HeightDots);
            rendered.Bitmap.Save(Path.Combine(_outDir, "40x15_font_simhei_600.png"), ImageFormat.Png);
        }
    }

    private static void TestBarcodeRoundTrip()
    {
        foreach (BarcodeType type in new[] { BarcodeType.Code128, BarcodeType.Code39, BarcodeType.Ean13 })
        {
            string value = type == BarcodeType.Ean13 ? "690123456789" : (type == BarcodeType.Code39 ? "ABC-123" : "A-2026-001");
            EncodedBarcode code = BarcodeFactory.EncodeBarcode(value, type);
            Check(code.Matrix.Width > 20, type + " encoded", "width=" + code.Matrix.Width);
        }
    }

    private static void TestQrRoundTrip()
    {
        EncodedQr qr = BarcodeFactory.EncodeQr("https://example.com/测试", QrEcc.M);
        Check(qr.Matrix.Width >= 21, "QR encoded", "v" + qr.Version + " width=" + qr.Matrix.Width);
        using (var bmp = RenderQr(qr, 6))
        {
            string decoded;
            Check(TryDecode(bmp, BarcodeFormat.QR_CODE, out decoded), "QR decoded", decoded);
        }
    }

    private static void TestRenderAndDecode40x15()
    {
        var content = new LabelContent
        {
            Text = "产品A",
            ShowBarcode = true,
            BarcodeType = BarcodeType.Code128,
            BarcodeValue = "A-2026-001",
            ShowQr = true,
            QrValue = "https://example.com/A-001",
            UseTextForQr = false
        };
        var options = new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.All, QrEcc = QrEcc.M };
        RenderCase("40x15_all_203", LabelSpec.Label40x15, content, options, true, true);
    }

    private static void TestRenderAndDecode30x25()
    {
        var content = new LabelContent
        {
            Text = "产品B",
            ShowBarcode = true,
            BarcodeType = BarcodeType.Code128,
            BarcodeValue = "20260001",
            ShowQr = true,
            QrValue = "https://example.com/B-001",
            UseTextForQr = false
        };
        var options = new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.All, QrEcc = QrEcc.M };
        RenderCase("30x25_all_203", LabelSpec.Label30x25, content, options, true, true);
    }

    private static void TestTextOnly()
    {
        var content = new LabelContent { Text = "中文标签测试\n第二行文字\nZebra ZT610" };
        var options = new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.TextOnly };
        RenderCase("40x15_text_203", LabelSpec.Label40x15, content, options, false, false);
    }

    private static void TestHighResolution()
    {
        var content = new LabelContent
        {
            Text = "产品A",
            ShowBarcode = true,
            BarcodeType = BarcodeType.Code128,
            BarcodeValue = "A-2026-001",
            ShowQr = true,
            QrValue = "https://example.com/A-001",
            UseTextForQr = false
        };
        RenderCase("40x15_all_300", LabelSpec.Label40x15, content,
            new RenderOptions { Dpi = 300, LayoutMode = LayoutMode.All, QrEcc = QrEcc.M }, true, true);
        RenderCase("30x25_all_300", LabelSpec.Label30x25,
            new LabelContent
            {
                Text = "产品B",
                ShowBarcode = true,
                BarcodeType = BarcodeType.Code128,
                BarcodeValue = "20260001",
                ShowQr = true,
                QrValue = "https://example.com/B-001",
                UseTextForQr = false
            },
            new RenderOptions { Dpi = 300, LayoutMode = LayoutMode.All, QrEcc = QrEcc.M }, true, true);
    }

    private static void RenderCase(string name, LabelSpec spec, LabelContent content, RenderOptions options,
        bool expectBarcode, bool expectQr)
    {
        try
        {
            using (RenderedLabel rendered = LabelRenderer.Render(spec, content, options))
            {
                string path = Path.Combine(_outDir, name + ".png");
                rendered.Bitmap.Save(path, ImageFormat.Png);
                Console.WriteLine("       " + path + "  " + rendered.WidthDots + "x" + rendered.HeightDots +
                                  "  " + rendered.Info);
                foreach (string w in rendered.Warnings) Console.WriteLine("       warning: " + w);
                Check(rendered.WidthDots == spec.WidthDots(options.Dpi) && rendered.HeightDots == spec.HeightDots(options.Dpi),
                    name + " size", rendered.WidthDots + "x" + rendered.HeightDots);
                Check(rendered.PackedBits.Length == rendered.Stride * rendered.HeightDots, name + " packed", rendered.PackedBits.Length.ToString());

                if (expectBarcode)
                {
                    string decoded;
                    bool ok = TryDecode(rendered.Bitmap, BarcodeFormat.CODE_128, out decoded);
                    Check(ok, name + " barcode decode", decoded);
                }
                if (expectQr)
                {
                    string decoded;
                    bool ok = TryDecode(rendered.Bitmap, BarcodeFormat.QR_CODE, out decoded);
                    Check(ok, name + " qr decode", decoded);
                }
            }
        }
        catch (Exception ex)
        {
            Check(false, name + " render", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static bool TryDecode(Bitmap bmp, BarcodeFormat format, out string text)
    {
        text = "";
        var reader = new BarcodeReader();
        reader.AutoRotate = true;
        reader.Options.TryHarder = true;
        reader.Options.PossibleFormats = new List<BarcodeFormat> { format };
        Result result = reader.Decode(bmp);
        if (result == null) return false;
        text = result.Text;
        return true;
    }

    private static Bitmap RenderQr(EncodedQr qr, int scale)
    {
        int total = qr.Matrix.Width + 8;
        var bmp = new Bitmap(total * scale, total * scale);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using (var brush = new SolidBrush(Color.Black))
            {
                for (int y = 0; y < qr.Matrix.Height; y++)
                {
                    for (int x = 0; x < qr.Matrix.Width; x++)
                    {
                        if (qr.Matrix[x, y] == 1)
                            g.FillRectangle(brush, (x + 4) * scale, (y + 4) * scale, scale, scale);
                    }
                }
            }
        }
        return bmp;
    }

    private static void TestBold()
    {
        var content = new LabelContent { Text = "加粗测试 ABC" };
        var regular = new RenderOptions
        {
            Dpi = 600,
            LayoutMode = LayoutMode.TextOnly,
            FontFamily = "Microsoft YaHei UI",
            FontSizeMm = 5,
            Bold = false
        };
        var bold = regular.Clone();
        bold.Bold = true;

        int regularDark, boldDark;
        using (RenderedLabel r = LabelRenderer.Render(LabelSpec.Label40x15, content, regular))
        {
            r.Bitmap.Save(Path.Combine(_outDir, "bold_regular.png"), ImageFormat.Png);
            regularDark = CountDark(r.Bitmap);
        }
        using (RenderedLabel b = LabelRenderer.Render(LabelSpec.Label40x15, content, bold))
        {
            b.Bitmap.Save(Path.Combine(_outDir, "bold_bold.png"), ImageFormat.Png);
            boldDark = CountDark(b.Bitmap);
        }
        Check(boldDark > regularDark * 1.03, "bold adds ink", "regular=" + regularDark + " bold=" + boldDark);
        Check(new BatchOptions { Bold = true }.CreateRenderOptions().Bold, "batch bold copied", "");
        Check(!new AppSettings().DefaultBold, "default bold off", "");
    }

    private static int CountDark(Bitmap bitmap)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                if (c.R < 128 && c.G < 128 && c.B < 128) count++;
            }
        }
        return count;
    }

    private static void TestLayoutPlan()
    {
        var content = new LabelContent
        {
            Text = "A",
            ShowBarcode = true,
            BarcodeType = BarcodeType.Code128,
            BarcodeValue = "A-2026-001",
            ShowQr = true,
            QrValue = "https://example.com/A"
        };
        // 这套断言只看“安全区内坐标怎么落到点阵上”，所以边距清零，让安全区等于整张标签。
        var options = new RenderOptions
        {
            Dpi = 600,
            LayoutMode = LayoutMode.All,
            MarginTopMm = 0,
            MarginBottomMm = 0,
            MarginLeftMm = 0,
            MarginRightMm = 0
        };
        LayoutPlan auto;
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            LabelLayout layout = LabelLayout.Build(LabelSpec.Label40x15, content, options, g);
            Check(layout.IsValid, "layout plan auto valid", string.Join("；", layout.Errors.ToArray()));
            auto = LayoutPlan.FromLayout(layout);
        }

        var custom = auto.Clone();
        custom.Text = new RectangleF(1.0f, 1.0f, 15.0f, 4.0f);
        custom.Barcode = new RectangleF(1.0f, 6.0f, 20.0f, 7.0f);
        custom.Qr = new RectangleF(24.0f, 6.0f, 8.0f, 8.0f);
        options.LayoutPlan = custom;
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            LabelLayout layout = LabelLayout.Build(LabelSpec.Label40x15, content, options, g);
            Check(layout.IsValid, "layout plan custom valid", string.Join("；", layout.Errors.ToArray()));
            Check(Math.Abs(layout.TextRect.Left - options.MmToPx(1)) < 1.5, "layout plan text x", layout.TextRect.Left.ToString("0.0"));
            Check(Math.Abs(layout.BarcodeRect.Top - options.MmToPx(6)) < 1.5, "layout plan barcode y", layout.BarcodeRect.Top.ToString("0.0"));
            Check(Math.Abs(layout.QrRect.Width - options.MmToPx(8)) < 1.5, "layout plan qr size", layout.QrRect.Width.ToString("0.0"));
        }
        using (RenderedLabel rendered = LabelRenderer.Render(LabelSpec.Label40x15, content, options))
        {
            rendered.Bitmap.Save(Path.Combine(_outDir, "layout_plan_custom.png"), ImageFormat.Png);
            Check(rendered.WidthDots == 945 && rendered.HeightDots == 354, "layout plan render", rendered.WidthDots + "x" + rendered.HeightDots);
        }
        Check(new BatchOptions { LayoutPlan = custom.Clone() }.CreateRenderOptions().LayoutPlan != null, "batch layout copied", "");
    }

    /// <summary>
    /// 进出一趟布局编辑器必须完全不变形：FromLayout → ApplyLayoutPlan 幂等。
    /// 旧实现把条码区固定按 74% 拆条/明文，且完全忽略“条码高度 %”，打开不动都会偏。
    /// </summary>
    private static void TestLayoutPlanRoundTrip()
    {
        var content = new LabelContent
        {
            Text = "产品A",
            ShowBarcode = true,
            BarcodeType = BarcodeType.Code128,
            BarcodeValue = "A-2026-001",
            ShowQr = true,
            QrValue = "https://example.com/A"
        };

        foreach (int barPercent in new[] { 100, 85, 120 })
        {
            // 百分比在自定义布局下也必须继续生效：往返后仍与自动布局一致即证明没被吞掉。
            foreach (int qrPercent in new[] { 100, 80, 120 })
                RoundTripCase(content, barPercent, qrPercent, barPercent + "/" + qrPercent);
        }

        // 明文关闭时也不应因为百分比而重复缩放。
        var noHri = new LabelContent
        {
            Text = "产品A",
            ShowBarcode = true,
            BarcodeValue = "A-2026-001"
        };
        RectangleF barOnly;
        LayoutPlan plan2;
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            var options = new RenderOptions
            {
                Dpi = 600,
                LayoutMode = LayoutMode.TextBarcode,
                ShowBarcodeText = false,
                BarcodeHeightPercent = 90
            };
            LabelLayout layout = LabelLayout.Build(LabelSpec.Label40x15, noHri, options, g);
            Check(layout.IsValid, "roundtrip nohri valid", string.Join("；", layout.Errors.ToArray()));
            barOnly = layout.BarcodeRect;
            plan2 = LayoutPlan.FromLayout(layout);
        }
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            var options = new RenderOptions
            {
                Dpi = 600,
                LayoutMode = LayoutMode.TextBarcode,
                ShowBarcodeText = false,
                BarcodeHeightPercent = 90,
                LayoutPlan = plan2
            };
            LabelLayout layout = LabelLayout.Build(LabelSpec.Label40x15, noHri, options, g);
            Check(SameRect(barOnly, layout.BarcodeRect, 1.5f), "roundtrip nohri bar",
                Fmt(barOnly) + " -> " + Fmt(layout.BarcodeRect));
        }
    }

    private static void RoundTripCase(LabelContent content, int barPercent, int qrPercent, string tag)
    {
        RectangleF bar1, hri1, qr1;
        LayoutPlan plan;
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            var options = new RenderOptions
            {
                Dpi = 600,
                LayoutMode = LayoutMode.All,
                BarcodeHeightPercent = barPercent,
                QrSizePercent = qrPercent
            };
            LabelLayout layout = LabelLayout.Build(LabelSpec.Label40x15, content, options, g);
            Check(layout.IsValid, "roundtrip auto valid " + tag, string.Join("；", layout.Errors.ToArray()));
            bar1 = layout.BarcodeRect;
            hri1 = layout.HriRect;
            qr1 = layout.QrRect;
            plan = LayoutPlan.FromLayout(layout);
        }

        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            var options = new RenderOptions
            {
                Dpi = 600,
                LayoutMode = LayoutMode.All,
                BarcodeHeightPercent = barPercent,
                QrSizePercent = qrPercent,
                LayoutPlan = plan
            };
            LabelLayout layout = LabelLayout.Build(LabelSpec.Label40x15, content, options, g);
            Check(layout.IsValid, "roundtrip plan valid " + tag, string.Join("；", layout.Errors.ToArray()));
            Check(SameRect(bar1, layout.BarcodeRect, 1.5f), "roundtrip bar " + tag,
                Fmt(bar1) + " -> " + Fmt(layout.BarcodeRect));
            Check(SameRect(hri1, layout.HriRect, 1.5f), "roundtrip hri " + tag,
                Fmt(hri1) + " -> " + Fmt(layout.HriRect));
            Check(SameRect(qr1, layout.QrRect, 1.5f), "roundtrip qr " + tag,
                Fmt(qr1) + " -> " + Fmt(layout.QrRect));
        }
    }

    /// <summary>边距在自定义布局下同样生效：改边距后墨迹必须跟着移动。</summary>
    private static void TestMarginsWithLayoutPlan()
    {
        LabelSpec spec = LabelSpec.CreateCustom(55, 33);
        var content = new LabelContent { Text = "边距生效", ShowBarcode = false, ShowQr = false };

        int autoSmall, autoLarge, planSmall, planLarge, planClamped, unused;

        using (RenderedLabel r = LabelRenderer.Render(spec, content, MarginOptions(2, null)))
            InkBounds(r.Bitmap, out autoSmall, out unused);
        using (RenderedLabel r = LabelRenderer.Render(spec, content, MarginOptions(12, null)))
            InkBounds(r.Bitmap, out autoLarge, out unused);
        Check(autoLarge - autoSmall >= 50, "auto margin shifts ink",
            autoSmall + " -> " + autoLarge);

        LayoutPlan plan;
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            RenderOptions options = MarginOptions(2, null);
            LabelLayout layout = LabelLayout.Build(spec, content, options, g);
            Check(layout.IsValid, "margin plan layout valid", string.Join("；", layout.Errors.ToArray()));
            Check(Math.Abs(layout.TextRect.Left - options.MmToPx(2)) < 1.5, "auto text starts at margin",
                layout.TextRect.Left.ToString("0.0"));
            plan = LayoutPlan.FromLayout(layout);
            Check(Math.Abs(plan.Text.X) < 0.05 && Math.Abs(plan.Text.Y) < 0.05,
                "plan uses safe-box origin", Fmt(plan.Text));
            Check(Math.Abs(plan.Text.Width - 51) < 0.2 && Math.Abs(plan.Text.Height - 29) < 0.2,
                "plan size excludes margins", Fmt(plan.Text));
        }

        using (RenderedLabel r = LabelRenderer.Render(spec, content, MarginOptions(2, plan)))
            InkBounds(r.Bitmap, out planSmall, out unused);
        using (RenderedLabel r = LabelRenderer.Render(spec, content, MarginOptions(12, plan)))
            InkBounds(r.Bitmap, out planLarge, out unused);

        Check(Math.Abs(planSmall - autoSmall) <= 2, "plan same margin matches auto",
            planSmall + " vs " + autoSmall);
        // 关键：同一份布局，只改边距，输出必须变。
        Check(planLarge - planSmall >= 50, "plan follows margin change",
            planSmall + " -> " + planLarge);
        Check(Math.Abs(planLarge - autoLarge) <= 2, "plan new margin matches auto",
            planLarge + " vs " + autoLarge);

        // 布局比安全区大时会被夹进安全区，而不是溢出到边距里。
        var tooBig = new LayoutPlan { Text = new RectangleF(0, 0, 60, 40) };
        using (RenderedLabel r = LabelRenderer.Render(spec, content, MarginOptions(12, tooBig)))
            InkBounds(r.Bitmap, out planClamped, out unused);
        Check(planClamped >= (int)Math.Round(12.0 * 8) - 2, "oversized plan clamped into safe box",
            planClamped + " dots");

        using (RenderedLabel r = LabelRenderer.Render(spec, content, MarginOptions(2, null)))
            r.Bitmap.Save(Path.Combine(_outDir, "margin-plan-2mm.png"), ImageFormat.Png);
        using (RenderedLabel r = LabelRenderer.Render(spec, content, MarginOptions(12, plan)))
            r.Bitmap.Save(Path.Combine(_outDir, "margin-plan-12mm.png"), ImageFormat.Png);
    }

    private static RenderOptions MarginOptions(double margin, LayoutPlan plan)
    {
        return new RenderOptions
        {
            Dpi = 203,
            LayoutMode = LayoutMode.TextOnly,
            TextAlign = TextAlign.Left,
            FontFamily = "SimHei",
            FontSizeMm = 4,
            MarginTopMm = margin,
            MarginBottomMm = margin,
            MarginLeftMm = margin,
            MarginRightMm = margin,
            LayoutPlan = plan
        };
    }

    /// <summary>打印偏移、负边距、越界布局：三条“手动补偿”路径都要真的动到墨迹。</summary>
    private static void TestPrintOffsetAndNegativeMargins()
    {
        LabelSpec spec = LabelSpec.CreateCustom(55, 33);
        var content = new LabelContent { Text = "AB" };

        // ---- 打印偏移补偿：整幅平移，X/Y 各按点阵走 ----
        RenderOptions plain = OffsetTestOptions(2, null, TextAlign.Center);
        RenderOptions moved = OffsetTestOptions(2, null, TextAlign.Center);
        moved.OffsetXMm = 3;
        moved.OffsetYMm = 2;

        Rectangle at0, at3;
        using (RenderedLabel r = LabelRenderer.Render(spec, content, plain)) at0 = InkRect(r.Bitmap);
        using (RenderedLabel r = LabelRenderer.Render(spec, content, moved)) at3 = InkRect(r.Bitmap);
        Check(at0.Width > 0 && at3.Width > 0, "offset ink present", at0 + " / " + at3);
        Check(Math.Abs((at3.X - at0.X) - 3 * 8) <= 2, "offset x shifts ink", at0.X + " -> " + at3.X);
        Check(Math.Abs((at3.Y - at0.Y) - 2 * 8) <= 2, "offset y shifts ink", at0.Y + " -> " + at3.Y);

        RenderOptions bigOffset = new RenderOptions { OffsetXMm = 25, OffsetYMm = -25 };
        bigOffset.Normalize();
        Check(bigOffset.OffsetXMm == 20 && bigOffset.OffsetYMm == -20, "offset clamped",
            bigOffset.OffsetXMm + " / " + bigOffset.OffsetYMm);

        // ---- 负边距：纯平移，安全区尺寸不变 ----
        RenderOptions zero = OffsetTestOptions(0, null, TextAlign.Center);
        RenderOptions negative = OffsetTestOptions(0, null, TextAlign.Center);
        negative.MarginLeftMm = -3;
        negative.MarginTopMm = -2;
        negative.Normalize();

        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            LabelLayout l0 = LabelLayout.Build(spec, content, zero, g);
            LabelLayout ln = LabelLayout.Build(spec, content, negative, g);
            Check(ln.IsValid, "negative margin layout valid", string.Join("；", ln.Errors.ToArray()));
            Check(Math.Abs(ln.TextRect.Width - l0.TextRect.Width) < 0.6f, "negative margin keeps width",
                l0.TextRect.Width.ToString("0.0") + " vs " + ln.TextRect.Width.ToString("0.0"));
            Check(Math.Abs(ln.TextRect.Left + 3 * 8) < 1.6f, "negative left margin shifts origin",
                ln.TextRect.Left.ToString("0.0"));
            Check(Math.Abs(ln.TextRect.Top + 2 * 8) < 1.6f, "negative top margin shifts origin",
                ln.TextRect.Top.ToString("0.0"));
        }

        Rectangle negInk;
        using (RenderedLabel r = LabelRenderer.Render(spec, content, negative)) negInk = InkRect(r.Bitmap);
        Check(Math.Abs((at0.X - negInk.X) - 3 * 8) <= 2, "negative margin shifts ink", at0.X + " -> " + negInk.X);

        RenderOptions clamped = new RenderOptions
        {
            MarginLeftMm = -30,
            MarginTopMm = 30,
            MarginBottomMm = -5,
            MarginRightMm = 40
        };
        clamped.Normalize();
        Check(clamped.MarginLeftMm == -20 && clamped.MarginTopMm == 20 &&
              clamped.MarginBottomMm == 0 && clamped.MarginRightMm == 20,
            "margin clamps", clamped.MarginLeftMm + "/" + clamped.MarginTopMm + "/" +
                             clamped.MarginBottomMm + "/" + clamped.MarginRightMm);

        // ---- 越界布局：元素可以放到标签外，不再被夹回安全区 ----
        var outside = new LayoutPlan { Text = new RectangleF(-6, -2, 20, 10) };
        RenderOptions withPlan = OffsetTestOptions(5, outside, TextAlign.Left);
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            LabelLayout lp = LabelLayout.Build(spec, content, withPlan, g);
            Check(lp.IsValid, "out-of-bounds plan valid", string.Join("；", lp.Errors.ToArray()));
            Check(lp.TextRect.Left < -1f, "out-of-bounds plan not clamped", lp.TextRect.Left.ToString("0.0"));
            Check(Math.Abs(lp.TextRect.Left - (5 - 6) * 8) < 2f, "out-of-bounds plan applied",
                lp.TextRect.Left.ToString("0.0"));
        }
        using (RenderedLabel r = LabelRenderer.Render(spec, content, withPlan))
        {
            Rectangle ink = InkRect(r.Bitmap);
            Check(ink.Width > 0 && ink.X == 0, "out-of-bounds content clipped at edge", ink.ToString());
            r.Bitmap.Save(Path.Combine(_outDir, "out-of-bounds-plan.png"), ImageFormat.Png);
        }

        // ---- 允许越界的范围：标签四边各 8 mm ----
        RenderOptions allowOptions = OffsetTestOptions(1.2, null, TextAlign.Left);
        RectangleF allow = LayoutPlan.AllowBox(spec, allowOptions);
        Check(Math.Abs(allow.X + 9.2f) < 0.05f && Math.Abs(allow.Width - 71f) < 0.05f,
            "allow box covers overflow", Fmt(allow));

        // ---- 设置 → 渲染参数 ----
        var settings = new AppSettings { PrintOffsetXMm = -1.5, PrintOffsetYMm = 2.5 };
        RenderOptions fromSettings = settings.CreateRenderOptions();
        Check(fromSettings.OffsetXMm == -1.5 && fromSettings.OffsetYMm == 2.5,
            "settings offset into render options",
            fromSettings.OffsetXMm + " / " + fromSettings.OffsetYMm);
    }

    private static RenderOptions OffsetTestOptions(double margin, LayoutPlan plan, TextAlign align)
    {
        return new RenderOptions
        {
            Dpi = 203,
            LayoutMode = LayoutMode.TextOnly,
            TextAlign = align,
            FontFamily = "SimHei",
            FontSizeMm = 4,
            MarginTopMm = margin,
            MarginBottomMm = margin,
            MarginLeftMm = margin,
            MarginRightMm = margin,
            LayoutPlan = plan
        };
    }

    private static Rectangle InkRect(Bitmap bmp)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                Color c = bmp.GetPixel(x, y);
                if (c.R > 128 || c.G > 128 || c.B > 128) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0) return Rectangle.Empty;
        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    /// <summary>旧版模版里的布局是绝对坐标，读取时要按当时的边距平移过来。</summary>
    private static void TestLegacyLayoutMigration()
    {
        string dir = Path.Combine(Path.GetTempPath(), "EasyLabel-legacy-" + Guid.NewGuid().ToString("N"));
        string path = null;
        try
        {
            var store = new TemplateStore(dir);
            var options = new RenderOptions
            {
                Dpi = 203,
                LayoutMode = LayoutMode.TextBarcode,
                MarginLeftMm = 3,
                MarginTopMm = 2,
                MarginRightMm = 1,
                MarginBottomMm = 1,
                LayoutPlan = new LayoutPlan
                {
                    Text = new RectangleF(5, 4, 20, 6),
                    Barcode = new RectangleF(5, 11, 30, 8),
                    BarHeightRatio = 0.7f
                }
            };
            var content = new LabelContent { Text = "旧模版", ShowBarcode = true, BarcodeValue = "A-1", UseTextForBarcode = false };
            LabelTemplate template = LabelTemplate.Capture("旧模版", LabelSpec.CreateCustom(55, 33), options, content, 1);
            path = store.Save(template);

            string saved = File.ReadAllText(path);
            Check(saved.IndexOf("space=\"margins\"", StringComparison.Ordinal) >= 0, "plan writes margin space", "");

            // 去掉坐标系标记，模拟 v1.8 及更早保存的模版。
            File.WriteAllText(path, saved.Replace(" space=\"margins\"", ""));

            LabelTemplate loaded = store.Load(path);
            Check(loaded.LayoutPlan != null, "legacy plan loaded", loaded.LayoutPlan == null ? "null" : Fmt(loaded.LayoutPlan.Text));
            if (loaded.LayoutPlan != null)
            {
                Check(Math.Abs(loaded.LayoutPlan.Text.X - 2) < 0.01 && Math.Abs(loaded.LayoutPlan.Text.Y - 2) < 0.01,
                    "legacy plan shifted by margin", Fmt(loaded.LayoutPlan.Text));
                Check(Math.Abs(loaded.LayoutPlan.Barcode.X - 2) < 0.01 && Math.Abs(loaded.LayoutPlan.Barcode.Y - 9) < 0.01,
                    "legacy barcode shifted by margin", Fmt(loaded.LayoutPlan.Barcode));
            }
        }
        catch (Exception ex)
        {
            Check(false, "legacy layout migration", ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>对齐 / 吸附 / 安全区计算。</summary>
    private static void TestLayoutHelpers()
    {
        var area = new RectangleF(0, 0, 30, 12);
        var rect = new RectangleF(1, 2, 10, 4);
        Check(Math.Abs(LayoutPlan.AlignRect(rect, area, LayoutAnchor.Left).X - 0) < 0.01, "align left", "");
        Check(Math.Abs(LayoutPlan.AlignRect(rect, area, LayoutAnchor.CenterHorizontal).X - 10) < 0.01,
            "align center h", LayoutPlan.AlignRect(rect, area, LayoutAnchor.CenterHorizontal).X.ToString("0.0"));
        Check(Math.Abs(LayoutPlan.AlignRect(rect, area, LayoutAnchor.Right).X - 20) < 0.01, "align right",
            LayoutPlan.AlignRect(rect, area, LayoutAnchor.Right).X.ToString("0.0"));
        Check(Math.Abs(LayoutPlan.AlignRect(rect, area, LayoutAnchor.Top).Y - 0) < 0.01, "align top", "");
        Check(Math.Abs(LayoutPlan.AlignRect(rect, area, LayoutAnchor.CenterVertical).Y - 4) < 0.01,
            "align center v", LayoutPlan.AlignRect(rect, area, LayoutAnchor.CenterVertical).Y.ToString("0.0"));
        Check(Math.Abs(LayoutPlan.AlignRect(rect, area, LayoutAnchor.Bottom).Y - 8) < 0.01, "align bottom",
            LayoutPlan.AlignRect(rect, area, LayoutAnchor.Bottom).Y.ToString("0.0"));

        Check(Math.Abs(LayoutPlan.Snap(1.24f, 0.5f) - 1.0f) < 0.01, "snap down to grid", "");
        Check(Math.Abs(LayoutPlan.Snap(1.26f, 0.5f) - 1.5f) < 0.01, "snap up to grid", "");
        Check(Math.Abs(LayoutPlan.Snap(3.3f, 1f) - 3f) < 0.01, "snap 1mm grid", "");
        Check(Math.Abs(LayoutPlan.Snap(3.3f, 0f) - 3.3f) < 0.001, "snap disabled keeps value", "");

        RectangleF box = LayoutPlan.ContentBox(LabelSpec.Label40x15, 2, 3, 4, 1);
        Check(Math.Abs(box.Width - 34) < 0.01 && Math.Abs(box.Height - 11) < 0.01,
            "content box excludes margins", box.Width + "x" + box.Height);
        RectangleF noRoom = LayoutPlan.ContentBox(LabelSpec.Label40x15, 20, 20, 20, 20);
        Check(noRoom.Width >= 2 && noRoom.Height >= 2, "content box keeps minimum", noRoom.Width + "x" + noRoom.Height);
    }

    private static void TestZplCopies()    {
        var content = new LabelContent { Text = "ZPL COPIES", ShowBarcode = false, ShowQr = false };
        var options = new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.TextOnly };
        using (RenderedLabel rendered = LabelRenderer.Render(LabelSpec.Label40x15, content, options))
        {
            string one = ZplBuilder.BuildLabel(LabelSpec.Label40x15, rendered, new PrintOptions(), "copies", 1);
            string multi = ZplBuilder.BuildLabel(LabelSpec.Label40x15, rendered, new PrintOptions(), "copies", 5);

            Check(!one.Contains("^PQ"), "no pq for 1 copy", "");
            Check(CountOf(multi, "^PQ5\r\n") == 1, "pq once for 5 copies", "");
            Check(CountOf(multi, "^XA") == 1, "pq keeps single format", "");
            Check(multi.Length == one.Length + 6, "pq only adds command", (multi.Length - one.Length).ToString());

            string wrapped = ZplBuilder.Wrap(new[] { one, one }, 3);
            Check(CountOf(wrapped, "^PQ3\r\n") == 2, "wrap pq per label", "");
            Check(CountOf(wrapped, "^XA\r\n") == 2, "wrap keeps two formats", "");
            Check(ZplBuilder.Wrap(new[] { one }, 1) == one, "wrap 1 copy unchanged", "");
            File.WriteAllText(Path.Combine(_outDir, "sample-copies.zpl"), wrapped, System.Text.Encoding.ASCII);
        }
    }

    private static void TestTemplates()
    {
        string dir = Path.Combine(Path.GetTempPath(), "EasyLabel-template-" + Guid.NewGuid().ToString("N"));
        string exportPath = Path.Combine(Path.GetTempPath(), "EasyLabel-export-" + Guid.NewGuid().ToString("N") + TemplateStore.Extension);
        try
        {
            var store = new TemplateStore(dir);
            Check(store.List().Count == 0, "template store empty", dir);

            LabelSpec spec = LabelSpec.CreateCustom(55, 33);
            var content = new LabelContent
            {
                Text = "第一行\r\n第二行 中文",
                ShowBarcode = true,
                BarcodeType = BarcodeType.Code39,
                BarcodeValue = "AB-123",
                UseTextForBarcode = false,
                ShowQr = true,
                QrValue = "https://example.com/中文",
                UseTextForQr = false
            };
            var options = new RenderOptions
            {
                Dpi = 300,
                LayoutMode = LayoutMode.All,
                BarcodeType = BarcodeType.Code39,
                QrEcc = QrEcc.Q,
                TextAlign = TextAlign.Right,
                ShowBarcodeText = false,
                MarginTopMm = 2.5,
                MarginBottomMm = 0.8,
                MarginLeftMm = 3.1,
                MarginRightMm = 1.4,
                FontSizeMm = 2.5,
                FontFamily = "SimHei",
                Bold = true,
                BarcodeHeightPercent = 115,
                QrSizePercent = 90,
                LayoutPlan = new LayoutPlan
                {
                    Text = new RectangleF(2f, 3f, 20f, 10f),
                    Barcode = new RectangleF(2f, 14f, 30f, 12f),
                    Qr = new RectangleF(40f, 2f, 12f, 12f),
                    BarHeightRatio = 0.68f,
                    HriGapMm = 0.35f
                }
            };

            LabelTemplate template = LabelTemplate.Capture("产品甲 · 55×33", spec, options, content, 3);
            string path = store.Save(template);
            Check(File.Exists(path), "template saved", path);
            Check(path.EndsWith(TemplateStore.Extension), "template extension", Path.GetFileName(path));
            Check(store.List().Count == 1, "template listed", store.List().Count.ToString());

            LabelTemplate loaded = store.Load(path);
            Check(loaded.Name == "产品甲 · 55×33", "template name roundtrip", loaded.Name);
            Check(Math.Abs(loaded.WidthMm - 55) < 0.001 && Math.Abs(loaded.HeightMm - 33) < 0.001,
                "template size roundtrip", loaded.WidthMm + "x" + loaded.HeightMm);
            Check(loaded.LayoutMode == LayoutMode.All && loaded.BarcodeType == BarcodeType.Code39 &&
                  loaded.QrEcc == QrEcc.Q && loaded.TextAlign == TextAlign.Right,
                "template layout roundtrip", loaded.LayoutModeName);
            Check(!loaded.ShowBarcodeText && loaded.Bold && Math.Abs(loaded.FontSizeMm - 2.5) < 0.001 &&
                  loaded.FontFamily == "SimHei", "template font roundtrip", loaded.FontFamily);
            Check(Math.Abs(loaded.MarginLeftMm - 3.1) < 0.001 && Math.Abs(loaded.MarginBottomMm - 0.8) < 0.001 &&
                  Math.Abs(loaded.MarginRightMm - 1.4) < 0.001, "template margin roundtrip",
                loaded.MarginLeftMm + "/" + loaded.MarginBottomMm);
            Check(loaded.BarcodeHeightPercent == 115 && loaded.QrSizePercent == 90, "template scale roundtrip", "");
            Check(loaded.Text.Replace("\r\n", "\n") == "第一行\n第二行 中文", "template multiline text roundtrip",
                loaded.Text.Replace("\n", "\\n"));
            Check(loaded.BarcodeValue == "AB-123" && loaded.QrValue == "https://example.com/中文",
                "template content roundtrip", loaded.BarcodeValue);
            Check(loaded.ShowBarcode && loaded.ShowQr && !loaded.BarcodeFromText && !loaded.QrFromText,
                "template content flags roundtrip", "");
            Check(loaded.Copies == 3, "template copies roundtrip", loaded.Copies.ToString());
            Check(loaded.LayoutPlan != null &&
                  Math.Abs(loaded.LayoutPlan.Text.X - 2) < 0.01 &&
                  Math.Abs(loaded.LayoutPlan.Qr.Width - 12) < 0.01 &&
                  Math.Abs(loaded.LayoutPlan.BarHeightRatio - 0.68f) < 0.001f &&
                  Math.Abs(loaded.LayoutPlan.HriGapMm - 0.35f) < 0.001f,
                "template custom layout roundtrip", Fmt(loaded.LayoutPlan == null ? RectangleF.Empty : loaded.LayoutPlan.Text));

            LabelSpec loadedSpec = loaded.GetSpec();
            Check(Math.Abs(loadedSpec.WidthMm - 55) < 0.001 && Math.Abs(loadedSpec.HeightMm - 33) < 0.001,
                "template getspec custom size", loadedSpec.Name);

            RenderOptions restored = loaded.CreateRenderOptions(600);
            Check(restored.Dpi == 600 && restored.FontFamily == "SimHei" && restored.BarcodeHeightPercent == 115,
                "template render options", restored.Dpi + " dpi");
            Check(restored.LayoutPlan != null && Math.Abs(restored.LayoutPlan.Barcode.Y - 14) < 0.01,
                "template render options keep layout", "");

            LabelContent restoredContent = loaded.CreateContent();
            Check(restoredContent.ShowQr && !restoredContent.UseTextForQr && restoredContent.HasBarcode,
                "template content object", "");

            try
            {
                using (RenderedLabel rendered = LabelRenderer.Render(loaded.GetSpec(), restoredContent, restored))
                {
                    Check(rendered.WidthDots == loaded.GetSpec().WidthDots(600),
                        "template renders at spec width", rendered.WidthDots + " dots");
                }
            }
            catch (Exception ex)
            {
                Check(false, "template renders", ex.Message);
            }

            // 同名保存是覆盖，不应出现第二份
            loaded.Note = "第二次保存";
            store.Save(loaded);
            Check(store.List().Count == 1, "template overwrite keeps one file", store.List().Count.ToString());

            // 文件名非法字符
            LabelTemplate weird = loaded.Clone();
            weird.Name = "A/B:C*D?";
            string weirdPath = store.Save(weird);
            string weirdFile = Path.GetFileName(weirdPath);
            Check(File.Exists(weirdPath), "template weird name saved", weirdFile);
            Check(weirdFile.IndexOfAny(new[] { '/', '\\', ':', '*', '?' }) < 0, "template filename sanitized", weirdFile);
            Check(store.Load(weirdPath).Name == "A/B:C*D?", "template display name kept", store.Load(weirdPath).Name);
            Check(store.List().Count == 2, "template store has two", store.List().Count.ToString());

            // 重命名：换文件，不新增条目
            LabelTemplate renameTarget = store.Load(weirdPath);
            string renamedPath = store.Rename(renameTarget, "改名之后");
            Check(File.Exists(renamedPath) && !File.Exists(weirdPath), "template rename moves file", Path.GetFileName(renamedPath));
            Check(store.List().Count == 2, "template rename keeps count", store.List().Count.ToString());
            Check(store.Load(renamedPath).Name == "改名之后", "template rename name", store.Load(renamedPath).Name);

            // 删除
            store.Delete(renamedPath);
            Check(store.List().Count == 1 && !File.Exists(renamedPath), "template delete", store.List().Count.ToString());

            // 损坏文件不影响列表，直接读取则报错
            string broken = Path.Combine(dir, "损坏" + TemplateStore.Extension);
            File.WriteAllText(broken, "这不是 XML");
            Check(store.List().Count == 1, "template list skips broken", store.List().Count.ToString());
            bool threw = false;
            try { store.Load(broken); }
            catch (InvalidDataException) { threw = true; }
            catch (Exception) { threw = true; }
            Check(threw, "template load broken throws", "");

            // 导出 / 导入：重名自动加序号
            store.Export(store.List()[0], exportPath);
            Check(File.Exists(exportPath), "template exported", exportPath);
            LabelTemplate imported = store.Import(exportPath);
            Check(imported.Name != "产品甲 · 55×33" && store.List().Count == 2,
                "template import unique name", imported.Name);

            // 已知规格应还原成内置实例，下拉框才能选中
            var known = new LabelTemplate { LabelName = LabelSpec.Label40x15.Name, WidthMm = 40, HeightMm = 15 };
            Check(ReferenceEquals(known.GetSpec(), LabelSpec.Label40x15), "template known spec instance", known.GetSpec().Name);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            try { if (File.Exists(exportPath)) File.Delete(exportPath); } catch { }
        }
    }

    /// <summary>.zpl 模版：内嵌信息可还原、外部 ZPL 只能直发、导入导出改名。</summary>
    private static void TestZplTemplates()
    {
        string dir = Path.Combine(Path.GetTempPath(), "EasyLabel-zpl-" + Guid.NewGuid().ToString("N"));
        string externalPath = Path.Combine(Path.GetTempPath(), "EasyLabel-ext-" + Guid.NewGuid().ToString("N") +
                                           ZplTemplateFile.Extension);
        string exportPath = Path.Combine(Path.GetTempPath(), "EasyLabel-zpl-export-" + Guid.NewGuid().ToString("N") +
                                         ZplTemplateFile.Extension);
        try
        {
            var store = new TemplateStore(dir);
            LabelSpec spec = LabelSpec.CreateCustom(50, 30);
            var content = new LabelContent
            {
                Text = "ZPL 往返",
                ShowBarcode = true,
                BarcodeType = BarcodeType.Code128,
                BarcodeValue = "Z-2026-001",
                UseTextForBarcode = false,
                ShowQr = true,
                QrValue = "https://example.com/zpl",
                UseTextForQr = false
            };
            var options = new RenderOptions
            {
                Dpi = 300,
                LayoutMode = LayoutMode.All,
                BarcodeType = BarcodeType.Code128,
                QrEcc = QrEcc.Q,
                TextAlign = TextAlign.Left,
                MarginLeftMm = 2.4,
                MarginTopMm = 1.8,
                FontSizeMm = 2.5,
                Bold = true,
                BarcodeHeightPercent = 110,
                QrSizePercent = 95
            };
            LabelTemplate template = LabelTemplate.Capture("ZPL 往返 · 50×30", spec, options, content, 4);
            template.Normalize();

            using (RenderedLabel rendered = LabelRenderer.Render(spec, content, options))
            {
                string zpl = ZplBuilder.BuildLabel(spec, rendered, new PrintOptions(), "EasyLabel 50x30", 4);
                Check(zpl.StartsWith("^XA", StringComparison.Ordinal), "zpl body starts with ^XA", "");
                Check(zpl.IndexOf("EasyLabel-Template", StringComparison.Ordinal) < 0,
                    "zpl body has no metadata", "");

                string embedded = ZplTemplateFile.Embed(template, zpl);
                Check(embedded.IndexOf("^FX EasyLabel-Template v1", StringComparison.Ordinal) >= 0,
                    "zpl metadata marker", "");
                Check(CountOf(embedded, "^XA") == 1, "zpl metadata keeps one format", "");
                Check(embedded.IndexOf("^GFA,", StringComparison.Ordinal) >= 0, "zpl keeps graphic", "");
                Check(ZplTemplateFile.LooksLikeZpl(embedded), "zpl still printable", "");
                Check(ZplTemplateFile.StripMetadata(embedded).IndexOf("^PW", StringComparison.Ordinal) >= 0,
                    "zpl strip keeps commands", "");

                LabelTemplate parsed;
                bool read = ZplTemplateFile.TryReadTemplate(embedded, out parsed);
                Check(read && parsed != null, "zpl metadata readable", "");
                if (parsed != null)
                {
                    Check(parsed.Name == "ZPL 往返 · 50×30", "zpl metadata name", parsed.Name);
                    Check(Math.Abs(parsed.WidthMm - 50) < 0.001 && Math.Abs(parsed.HeightMm - 30) < 0.001,
                        "zpl metadata size", parsed.WidthMm + "x" + parsed.HeightMm);
                    Check(parsed.LayoutMode == LayoutMode.All && parsed.BarcodeType == BarcodeType.Code128 &&
                          parsed.QrEcc == QrEcc.Q && parsed.TextAlign == TextAlign.Left,
                        "zpl metadata layout", parsed.LayoutModeName);
                    Check(parsed.Bold && Math.Abs(parsed.FontSizeMm - 2.5) < 0.001 &&
                          Math.Abs(parsed.MarginLeftMm - 2.4) < 0.001,
                        "zpl metadata font/margin", parsed.FontFamily);
                    Check(parsed.BarcodeHeightPercent == 110 && parsed.QrSizePercent == 95,
                        "zpl metadata scale", "");
                    Check(parsed.Copies == 4 && parsed.BarcodeValue == "Z-2026-001" &&
                          parsed.QrValue == "https://example.com/zpl",
                        "zpl metadata content", parsed.BarcodeValue);
                    Check(parsed.Text == "ZPL 往返", "zpl metadata text", parsed.Text);
                }

                ZplSummary summary = ZplTemplateFile.Inspect(embedded);
                Check(summary.WidthDots == spec.WidthDots(300) && summary.HeightDots == spec.HeightDots(300),
                    "zpl inspect size", summary.SizeText);
                Check(summary.Copies == 4 && summary.HasGraphic && summary.HasEmbeddedTemplate,
                    "zpl inspect flags", summary.ByteText);

                // 保存成 .zpl 模版：列表能读到，且是可编辑模版而不是纯指令
                string path = store.SaveZpl(template, zpl);
                Check(path.EndsWith(TemplateStore.ZplExtension, StringComparison.OrdinalIgnoreCase),
                    "zpl template extension", Path.GetFileName(path));
                Check(File.Exists(path), "zpl template saved", path);
                Check(store.List().Count == 1, "zpl template listed", store.List().Count.ToString());

                LabelTemplate loaded = store.Load(path);
                Check(loaded.IsZplFile && !loaded.IsPrintableOnly, "zpl template editable", "");
                Check(loaded.Name == "ZPL 往返 · 50×30", "zpl template name", loaded.Name);
                Check(Math.Abs(loaded.WidthMm - 50) < 0.001 && loaded.Copies == 4,
                    "zpl template fields", loaded.SizeText);
                Check(loaded.LayoutPlan == null && loaded.Text == "ZPL 往返", "zpl template content", "");
                Check(!string.IsNullOrEmpty(loaded.RawZpl) && loaded.RawZpl.StartsWith("^XA", StringComparison.Ordinal),
                    "zpl template keeps raw", "");

                // 覆盖保存不应该产生第二份
                store.SaveZpl(loaded, zpl);
                Check(store.List().Count == 1, "zpl template overwrite", store.List().Count.ToString());

                // 改名后文件里的名字也要跟着变，否则下次加载显示旧名
                LabelTemplate renameTarget = store.Load(path);
                string renamed = store.Rename(renameTarget, "改名后的 ZPL");
                Check(renamed.EndsWith(TemplateStore.ZplExtension, StringComparison.OrdinalIgnoreCase) &&
                      !File.Exists(path), "zpl rename moves file", Path.GetFileName(renamed));
                Check(store.Load(renamed).Name == "改名后的 ZPL", "zpl rename updates metadata",
                    store.Load(renamed).Name);
                Check(store.List().Count == 1, "zpl rename keeps count", store.List().Count.ToString());

                // 导出：内容仍是完整 ZPL
                store.Export(store.Load(renamed), exportPath);
                Check(File.Exists(exportPath), "zpl export exists", exportPath);
                Check(ZplTemplateFile.LooksLikeZpl(ZplTemplateFile.ReadText(exportPath)),
                    "zpl export printable", "");
            }

            // 外部 ZPL：没有内嵌信息，只能直发
            File.WriteAllText(externalPath, "^XA\r\n^PW406\r\n^LL203\r\n^FO0,0^GB406,203,2^FS\r\n^XZ\r\n",
                System.Text.Encoding.ASCII);
            LabelTemplate raw = store.Load(externalPath);
            Check(raw.IsZplFile && raw.IsPrintableOnly, "external zpl is printable only", raw.FileTypeName);
            Check(raw.ZplWidthDots == 406 && raw.ZplHeightDots == 203 && raw.ZplCopies == 1,
                "external zpl inspect", raw.SizeText);
            Check(raw.Name == Path.GetFileNameWithoutExtension(externalPath), "external zpl name", raw.Name);
            LabelTemplate unknown;
            Check(!ZplTemplateFile.TryReadTemplate(raw.RawZpl, out unknown), "external zpl no metadata", "");

            // 导入外部 .zpl：进库后仍是 .zpl
            LabelTemplate imported = store.Import(externalPath);
            Check(imported.IsZplFile && imported.IsPrintableOnly, "external zpl imported", imported.Name);
            Check(store.List().Count == 2, "zpl store has two", store.List().Count.ToString());

            // 不是 ZPL 的文件：列表跳过，直接读取报错
            string bad = Path.Combine(dir, "坏文件" + TemplateStore.ZplExtension);
            File.WriteAllText(bad, "这不是 ZPL");
            Check(store.List().Count == 2, "zpl list skips broken", store.List().Count.ToString());
            bool threw = false;
            try { store.Load(bad); }
            catch (InvalidDataException) { threw = true; }
            catch (Exception) { threw = true; }
            Check(threw, "zpl load broken throws", "");

            // .elbl 与 .zpl 互不干扰：同名不同扩展名可以共存
            LabelTemplate plain = template.Clone();
            plain.Name = "同名共存";
            store.Save(plain);
            LabelTemplate zplSame = template.Clone();
            zplSame.Name = "同名共存";
            using (RenderedLabel rendered = LabelRenderer.Render(spec, content, options))
            {
                store.SaveZpl(zplSame, ZplBuilder.BuildLabel(spec, rendered, new PrintOptions(), "EasyLabel", 1));
            }
            Check(File.Exists(store.PathFor("同名共存", false)) && File.Exists(store.PathFor("同名共存", true)),
                "elbl and zpl coexist", "");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            try { if (File.Exists(externalPath)) File.Delete(externalPath); } catch { }
            try { if (File.Exists(exportPath)) File.Delete(exportPath); } catch { }
        }
    }

    private static bool SameRect(RectangleF a, RectangleF b, float tolerance)    {
        return Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance &&
               Math.Abs(a.Right - b.Right) <= tolerance && Math.Abs(a.Bottom - b.Bottom) <= tolerance;
    }

    private static string Fmt(RectangleF r)
    {
        return "(" + r.Left.ToString("0.0") + "," + r.Top.ToString("0.0") + "," +
               r.Width.ToString("0.0") + "x" + r.Height.ToString("0.0") + ")";
    }

    private static int CountOf(string text, string token)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)) return 0;
        int count = 0, index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    /// <summary>
    /// “导入并打印”链路：份数改写（覆盖已有 ^PQ、多段作业、带参数）/ 从任意路径读 .elbl 与 .zpl。
    /// 界面部分（预览、按钮）由人工验证，这里只覆盖能被断言的核心行为。
    /// </summary>
    private static void TestImportPrint()
    {
        string dir = Path.Combine(Path.GetTempPath(), "EasyLabel-import-" + Guid.NewGuid().ToString("N"));
        string elblPath = Path.Combine(Path.GetTempPath(), "EasyLabel-import-" + Guid.NewGuid().ToString("N") + TemplateStore.Extension);
        string zplPath = Path.Combine(Path.GetTempPath(), "EasyLabel-import-" + Guid.NewGuid().ToString("N") + TemplateStore.ZplExtension);
        try
        {
            var content = new LabelContent { Text = "IMPORT", ShowBarcode = false, ShowQr = false };
            var options = new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.TextOnly };
            using (RenderedLabel rendered = LabelRenderer.Render(LabelSpec.Label40x15, content, options))
            {
                string single = ZplBuilder.BuildLabel(LabelSpec.Label40x15, rendered, new PrintOptions(), "import", 1);
                string three = ZplBuilder.OverrideCopies(single, 3);
                Check(CountOf(three, "^PQ3\r\n") == 1, "override inserts pq", CountOf(three, "^PQ3").ToString());
                Check(CountOf(three, "^XA") == 1 && CountOf(three, "^XZ") == 1, "override keeps one format", "");
                Check(three.Length == single.Length + 6, "override only adds command", (three.Length - single.Length).ToString());

                string five = ZplBuilder.BuildLabel(LabelSpec.Label40x15, rendered, new PrintOptions(), "import", 5);
                string two = ZplBuilder.OverrideCopies(five, 2);
                Check(CountOf(two, "^PQ") == 1 && two.IndexOf("^PQ2\r\n", StringComparison.Ordinal) >= 0,
                    "override replaces pq", two.IndexOf("^PQ2\r\n", StringComparison.Ordinal).ToString());
                Check(two.IndexOf("^PQ5", StringComparison.Ordinal) < 0, "override drops old pq", "");

                string multi = single + single;
                Check(CountOf(ZplBuilder.OverrideCopies(multi, 4), "^PQ4\r\n") == 2, "override per format", "");
            }

            // 带参数的 ^PQ（^PQ3,0,1,Y）只改份数，后面的参数必须留着。
            string withArgs = ZplBuilder.OverrideCopies("^XA\r\n^PQ3,0,1,Y\r\n^XZ\r\n", 7);
            Check(withArgs.IndexOf("^PQ7,0,1,Y", StringComparison.Ordinal) >= 0, "override keeps pq params", withArgs.Replace("\r\n", "|"));

            // ---- 从任意路径读 .elbl：导入打印就是拿外部路径直接 Load ----
            var store = new TemplateStore(dir);
            LabelSpec spec = LabelSpec.CreateCustom(50, 25);
            var elblContent = new LabelContent
            {
                Text = "导入测试",
                ShowBarcode = true,
                BarcodeType = BarcodeType.Code128,
                BarcodeValue = "IMP-1",
                UseTextForBarcode = false
            };
            var elblOptions = new RenderOptions
            {
                Dpi = 203,
                LayoutMode = LayoutMode.TextBarcode,
                BarcodeType = BarcodeType.Code128,
                FontSizeMm = 2.6,
                TextAlign = TextAlign.Center
            };
            LabelTemplate template = LabelTemplate.Capture("导入测试", spec, elblOptions, elblContent, 2);
            string savedPath = store.Save(template);
            File.Copy(savedPath, elblPath, true);

            LabelTemplate loadedElbl = new TemplateStore(Path.Combine(dir, "elsewhere")).Load(elblPath);
            Check(loadedElbl != null && !loadedElbl.IsPrintableOnly, "import elbl loads template", elblPath);
            Check(loadedElbl.Text == "导入测试", "import elbl text", loadedElbl.Text);
            Check(loadedElbl.Copies == 2, "import elbl copies", loadedElbl.Copies.ToString());
            Check(Math.Abs(loadedElbl.GetSpec().WidthMm - 50) < 0.001 && Math.Abs(loadedElbl.GetSpec().HeightMm - 25) < 0.001,
                "import elbl size", loadedElbl.GetSpec().WidthMm + "x" + loadedElbl.GetSpec().HeightMm);

            // ---- 从任意路径读外部 .zpl：只能直发，份数按 ^PQ 识别 ----
            string rawZpl;
            using (RenderedLabel rendered = LabelRenderer.Render(spec, elblContent, elblOptions))
            {
                rawZpl = ZplBuilder.BuildLabel(spec, rendered, new PrintOptions(), "external", 3);
            }
            File.WriteAllText(zplPath, rawZpl, System.Text.Encoding.ASCII);

            LabelTemplate loadedZpl = new TemplateStore(Path.Combine(dir, "elsewhere2")).Load(zplPath);
            Check(loadedZpl.IsPrintableOnly, "import zpl printable only", "");
            Check(loadedZpl.ZplCopies == 3, "import zpl copies", loadedZpl.ZplCopies.ToString());
            Check(loadedZpl.RawZpl == rawZpl, "import zpl keeps raw text", "");
            Check(ZplTemplateFile.Inspect(loadedZpl.RawZpl).WidthDots > 0, "import zpl size readable", "");

            // 打印前会剥掉内嵌注释再改份数，这两步叠起来不能破坏指令。
            string printReady = ZplBuilder.OverrideCopies(ZplTemplateFile.StripMetadata(loadedZpl.RawZpl), 1);
            Check(printReady.IndexOf("^GFA,", StringComparison.Ordinal) >= 0 && CountOf(printReady, "^XA") == 1,
                "import print zpl intact", "");
            Check(printReady.IndexOf("^PQ3", StringComparison.Ordinal) < 0 && CountOf(printReady, "^PQ1") == 1,
                "import print zpl copies", CountOf(printReady, "^PQ").ToString());
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            try { if (File.Exists(elblPath)) File.Delete(elblPath); } catch { }
            try { if (File.Exists(zplPath)) File.Delete(zplPath); } catch { }
        }
    }

    private static void TestExcelImport()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples", "sample-data.xlsx");
        if (!File.Exists(path))
        {
            Check(false, "excel sample exists", path);
            return;
        }
        try
        {
            WorkbookData wb = ExcelImporter.LoadWorkbook(path);
            Check(wb.Sheets.Count > 0, "excel sheets", wb.Sheets.Count.ToString());
            SheetData sheet = wb.Sheets[0];
            Check(sheet.Rows.Count == 4, "excel rows", sheet.Rows.Count.ToString());
            Check(sheet.Rows[1][0] == "产品A", "excel chinese", sheet.Rows[1][0]);
            int textCol, barcodeCol, qrCol;
            ExcelImporter.SuggestMapping(sheet, true, out textCol, out barcodeCol, out qrCol);
            Check(textCol == 0 && barcodeCol == 1 && qrCol == 2, "excel mapping", textCol + "," + barcodeCol + "," + qrCol);
        }
        catch (Exception ex)
        {
            Check(false, "excel import", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void TestCsvImport()
    {
        string path = Path.Combine(_outDir, "sample.csv");
        File.WriteAllText(path, "文本,条码,二维码\r\n产品C,C-001,https://example.com/C\r\n", new System.Text.UTF8Encoding(true));
        try
        {
            WorkbookData wb = ExcelImporter.LoadWorkbook(path);
            SheetData sheet = wb.Sheets[0];
            Check(sheet.Rows.Count == 2 && sheet.Rows[1][1] == "C-001", "csv import", sheet.Rows.Count.ToString());
        }
        catch (Exception ex)
        {
            Check(false, "csv import", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void TestZpl()
    {
        var content = new LabelContent { Text = "ZPL TEST", ShowBarcode = false, ShowQr = false };
        var options = new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.TextOnly };
        using (RenderedLabel rendered = LabelRenderer.Render(LabelSpec.Label40x15, content, options))
        {
            string zpl = ZplBuilder.BuildLabel(LabelSpec.Label40x15, rendered, new PrintOptions(), "test");
            Check(zpl.StartsWith("^XA\r\n") && zpl.EndsWith("^XZ\r\n"), "zpl wrapper", "");
            Check(zpl.Contains("^PW320\r\n") && zpl.Contains("^LL120\r\n"), "zpl dimensions", "");
            Check(zpl.Contains("^GFA,4800,4800,40,"), "zpl gfa header", "");
            Check(zpl.Length > 9600, "zpl hex length", zpl.Length.ToString());
            File.WriteAllText(Path.Combine(_outDir, "sample-40x15.zpl"), zpl, System.Text.Encoding.ASCII);
        }
    }

    private static void TestTestPage()
    {
        try
        {
            using (RenderedLabel rendered = LabelRenderer.RenderTestPage(LabelSpec.Label40x15,
                new RenderOptions { Dpi = 203 }, "Test Printer"))
            {
                rendered.Bitmap.Save(Path.Combine(_outDir, "test-page-40x15.png"), ImageFormat.Png);
                Check(rendered.WidthDots == 320 && rendered.HeightDots == 120, "test page 40x15", rendered.WidthDots + "x" + rendered.HeightDots);
            }
            using (RenderedLabel rendered = LabelRenderer.RenderTestPage(LabelSpec.Label30x25,
                new RenderOptions { Dpi = 203 }, "Test Printer"))
            {
                rendered.Bitmap.Save(Path.Combine(_outDir, "test-page-30x25.png"), ImageFormat.Png);
                Check(rendered.WidthDots == 240 && rendered.HeightDots == 200, "test page 30x25", rendered.WidthDots + "x" + rendered.HeightDots);
            }
        }
        catch (Exception ex)
        {
            Check(false, "test page render", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void TestValidation()
    {
        var longBarcode = new LabelContent
        {
            Text = "x",
            ShowBarcode = true,
            BarcodeType = BarcodeType.Code128,
            BarcodeValue = new string('9', 80)
        };
        var options = new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.TextBarcode };
        ValidationResult barcodeResult = LabelRenderer.Validate(LabelSpec.Label40x15, longBarcode, options);
        Check(!barcodeResult.IsValid, "long barcode rejected", barcodeResult.ErrorText);

        var longText = new LabelContent
        {
            Text = new string('中', 500)
        };
        ValidationResult textResult = LabelRenderer.Validate(LabelSpec.Label40x15, longText,
            new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.TextOnly });
        Check(!textResult.IsValid, "long text rejected", textResult.ErrorText);

        var invalidCode = LabelContentForBarcode("中文");
        ValidationResult codeResult = LabelRenderer.Validate(LabelSpec.Label40x15, invalidCode,
            new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.BarcodeOnly });
        Check(!codeResult.IsValid, "chinese in 1D rejected", codeResult.ErrorText);
    }

    private static LabelContent LabelContentForBarcode(string value)
    {
        return new LabelContent
        {
            ShowBarcode = true,
            BarcodeType = BarcodeType.Code128,
            BarcodeValue = value
        };
    }

    private static void TestFontSize()
    {
        var content = new LabelContent { Text = "测试" };
        using (RenderedLabel auto = LabelRenderer.Render(LabelSpec.Label40x15, content,
            new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.TextOnly }))
        {
            Check(auto.Info.Contains("6.5 mm"), "auto text size", auto.Info);
        }
        using (RenderedLabel large = LabelRenderer.Render(LabelSpec.Label40x15, content,
            new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.TextOnly, FontSizeMm = 7.0 }))
        {
            Check(large.Info.Contains("7.0 mm"), "explicit 7mm text", large.Info);
            Check(large.Warnings.Count == 0, "7mm no warning", string.Join("；", large.Warnings.ToArray()));
        }

        var barcodeContent = new LabelContent
        {
            Text = "产品A",
            ShowBarcode = true,
            BarcodeValue = "A-2026-001"
        };
        ValidationResult tooLarge = LabelRenderer.Validate(LabelSpec.Label40x15, barcodeContent,
            new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.TextBarcode, FontSizeMm = 6.0 });
        Check(tooLarge.Warnings.Any(w => w.Contains("自动缩小")), "font capped warning", tooLarge.WarningText);
    }

    private static void TestUpdates()
    {
        Check(UpdateService.CompareVersions("1.6.0", "1.5.1") > 0, "更新：1.6.0 比 1.5.1 新", "");
        Check(UpdateService.CompareVersions("v1.6.0", "1.6.0") == 0, "更新：v 前缀不影响比较", "");
        Check(UpdateService.CompareVersions("1.6", "1.6.0") == 0, "更新：1.6 与 1.6.0 等价", "");
        Check(UpdateService.CompareVersions("1.10.0", "1.9.9") > 0, "更新：按数字段比较而非字符串", "");
        Check(UpdateService.CompareVersions("1.6.0-beta", "1.6.0") < 0, "更新：预发布版低于正式版", "");
        Check(UpdateService.CompareVersions("1.5.1", "1.6.0") < 0, "更新：旧版本判定为不更新", "");

        const string manifest = @"{
  ""version"": ""1.6.0"",
  ""releaseDate"": ""2026-10-28"",
  ""url"": ""https://example.com/EasyLabel-1.6.0-update.zip"",
  ""sha256"": ""ABCDEF0123"",
  ""size"": 12345678,
  ""notes"": ""第一行\n第二行""
}";
        string error;
        UpdateInfo info = UpdateService.ParseManifest(manifest, out error);
        Check(info != null, "更新：解析自建 update.json", error ?? "");
        Check(info != null && info.Version == "1.6.0", "更新：清单版本号", info == null ? "" : info.Version);
        Check(info != null && info.Size == 12345678, "更新：清单包大小", info == null ? "" : info.Size.ToString());
        Check(info != null && info.Sha256 == "ABCDEF0123", "更新：清单校验值", info == null ? "" : info.Sha256);
        Check(info != null && info.Notes.Contains("第二行"), "更新：清单更新说明", "");

        const string github = @"{
  ""tag_name"": ""v2.0.0"",
  ""body"": ""GitHub 发布说明"",
  ""published_at"": ""2026-12-01T08:00:00Z"",
  ""assets"": [
    { ""name"": ""EasyLabel-2.0.0-win64.zip"", ""browser_download_url"": ""https://example.com/portable.zip"", ""size"": 100 },
    { ""name"": ""EasyLabel-2.0.0-update.zip"", ""browser_download_url"": ""https://example.com/update.zip"", ""size"": 90 }
  ]
}";
        UpdateInfo release = UpdateService.ParseManifest(github, out error);
        Check(release != null && release.Version == "2.0.0", "更新：解析 GitHub Releases",
            release == null ? error : release.Version);
        Check(release != null && release.DownloadUrl.EndsWith("update.zip"),
            "更新：GitHub 优先选 update 包", release == null ? "" : release.DownloadUrl);

        UpdateInfo broken = UpdateService.ParseManifest("not a json", out error);
        Check(broken == null && !string.IsNullOrEmpty(error), "更新：非法清单被拒绝", error);
        UpdateInfo empty = UpdateService.ParseManifest("{\"notes\":\"no version\"}", out error);
        Check(empty == null && !string.IsNullOrEmpty(error), "更新：缺版本号的清单被拒绝", error);

        Check(UpdateService.ResolveFeedUrl("") == UpdateService.DefaultFeedUrl, "更新：空地址回落到默认清单", "");
        Check(UpdateService.ResolveFeedUrl(" http://intranet/update.json ") == "http://intranet/update.json",
            "更新：自定义地址去空格", "");

        // 默认地址直接指向 GitHub 的 Release（不再依赖仓库里的 update.json）
        Check(UpdateService.DefaultFeedUrl.EndsWith("/releases/latest"),
            "更新：默认地址为 GitHub Release", UpdateService.DefaultFeedUrl);

        // 粘贴 GitHub 页面地址时自动换算成 API
        Check(UpdateService.ResolveFeedUrl("https://github.com/yuguod/EasyLabel") ==
              "https://api.github.com/repos/yuguod/EasyLabel/releases/latest",
            "更新：仓库主页换算为最新 Release", UpdateService.ResolveFeedUrl("https://github.com/yuguod/EasyLabel"));
        Check(UpdateService.ResolveFeedUrl("https://github.com/yuguod/EasyLabel/releases") ==
              "https://api.github.com/repos/yuguod/EasyLabel/releases/latest",
            "更新：发布页换算为最新 Release", UpdateService.ResolveFeedUrl("https://github.com/yuguod/EasyLabel/releases"));
        Check(UpdateService.ResolveFeedUrl("https://github.com/yuguod/EasyLabel/releases/tag/v1.6.0") ==
              "https://api.github.com/repos/yuguod/EasyLabel/releases/tags/v1.6.0",
            "更新：指定 Release 页面换算为对应版本", UpdateService.ResolveFeedUrl("https://github.com/yuguod/EasyLabel/releases/tag/v1.6.0"));
        Check(UpdateService.ResolveFeedUrl("github.com/yuguod/EasyLabel") ==
              "https://api.github.com/repos/yuguod/EasyLabel/releases/latest",
            "更新：省略协议的地址也能识别", UpdateService.ResolveFeedUrl("github.com/yuguod/EasyLabel"));
        Check(UpdateService.ResolveFeedUrl("https://api.github.com/repos/yuguod/EasyLabel/releases/latest") ==
              "https://api.github.com/repos/yuguod/EasyLabel/releases/latest",
            "更新：API 地址原样使用", "");
        Check(UpdateService.ResolveFeedUrl("https://intranet.local/feed.json") == "https://intranet.local/feed.json",
            "更新：内网清单原样使用", "");

        // 附件自带 digest 时直接作为校验值，大小取附件大小
        const string digestRelease = @"{
  ""tag_name"": ""v3.0.0"",
  ""body"": ""没有哈希的说明"",
  ""assets"": [
    { ""name"": ""EasyLabel-3.0.0-update.zip"", ""browser_download_url"": ""https://example.com/u.zip"",
      ""size"": 4242,
      ""digest"": ""sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"" }
  ]
}";
        UpdateInfo digest = UpdateService.ParseManifest(digestRelease, out error);
        Check(digest != null && digest.Size == 4242, "更新：GitHub 附件大小", digest == null ? "" : digest.Size.ToString());
        Check(digest != null && digest.Sha256.Length == 64, "更新：GitHub 附件 digest 作为校验值",
            digest == null ? "" : digest.Sha256);

        // 没有 digest 时从发布说明里取 SHA-256（publish.ps1 会把哈希写进正文）
        const string bodyHash = @"{ ""tag_name"": ""v3.1.0"",
  ""body"": ""说明\n\n`EasyLabel-3.1.0-update.zip` 的 SHA-256：`bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb`"",
  ""assets"": [ { ""name"": ""EasyLabel-3.1.0-update.zip"", ""browser_download_url"": ""https://example.com/u.zip"", ""size"": 10 } ] }";
        UpdateInfo fromBody = UpdateService.ParseManifest(bodyHash, out error);
        Check(fromBody != null && fromBody.Sha256 == new string('b', 64),
            "更新：发布说明里的 SHA-256 被采用", fromBody == null ? error : fromBody.Sha256);
    }

    private static void TestLabelary()
    {        TestLabelaryCase("labelary-40x15-203", LabelSpec.Label40x15, 203, "产品A", "A-2026-001", "https://example.com/A-001");
        TestLabelaryCase("labelary-30x25-203", LabelSpec.Label30x25, 203, "产品B", "20260001", "https://example.com/B-001");
        TestLabelaryCase("labelary-40x15-300", LabelSpec.Label40x15, 300, "产品A", "A-2026-001", "https://example.com/A-001");
        TestLabelaryCase("labelary-30x25-300", LabelSpec.Label30x25, 300, "产品B", "20260001", "https://example.com/B-001");
        TestLabelaryCase("labelary-40x15-600", LabelSpec.Label40x15, 600, "产品A", "A-2026-001", "https://example.com/A-001");
        TestLabelaryCase("labelary-30x20-600", LabelSpec.Label30x20, 600, "产品C", "C-2026-003", "https://example.com/C-003");
    }

    private static void TestLabelaryCase(string name, LabelSpec spec, int dpi, string text, string barcode, string qr)
    {
        try
        {
            var content = new LabelContent
            {
                Text = text,
                ShowBarcode = true,
                BarcodeType = BarcodeType.Code128,
                BarcodeValue = barcode,
                ShowQr = true,
                QrValue = qr,
                UseTextForQr = false
            };
            var options = new RenderOptions { Dpi = dpi, LayoutMode = LayoutMode.All, QrEcc = QrEcc.M };
            using (RenderedLabel rendered = LabelRenderer.Render(spec, content, options))
            {
                string zpl = ZplBuilder.BuildLabel(spec, rendered, new PrintOptions { Dpi = dpi }, name);
                File.WriteAllText(Path.Combine(_outDir, name + ".zpl"), zpl, System.Text.Encoding.ASCII);
                int dpmm = dpi == 203 ? 8 : (dpi == 300 ? 12 : 24);
                string url = "http://api.labelary.com/v1/printers/" + dpmm + "dpmm/labels/" +
                             (spec.WidthMm / 25.4).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "x" +
                             (spec.HeightMm / 25.4).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "/0/";
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.Accept = "image/png";
                request.Timeout = 30000;
                byte[] body = System.Text.Encoding.ASCII.GetBytes(zpl);
                request.ContentLength = body.Length;
                using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    byte[] png = ms.ToArray();
                    File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), png);
                    using (var img = Image.FromStream(new MemoryStream(png)))
                    {
                        int toleranceW = Math.Max(2, (int)(rendered.WidthDots * 0.02));
                        int toleranceH = Math.Max(2, (int)(rendered.HeightDots * 0.02));
                        Check(Math.Abs(img.Width - rendered.WidthDots) <= toleranceW &&
                              Math.Abs(img.Height - rendered.HeightDots) <= toleranceH,
                            "labelary " + name, img.Width + "x" + img.Height + " expected " + rendered.WidthDots + "x" + rendered.HeightDots);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Skip("labelary " + name, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>左/中/右三种对齐，文字墨迹的水平位置应依次右移。</summary>
    private static void TestTextAlign()
    {
        var content = new LabelContent { Text = "AB" };
        TextAlign[] aligns = { TextAlign.Left, TextAlign.Center, TextAlign.Right };
        string[] names = { "left", "center", "right" };
        int[] minX = new int[3];
        int[] maxX = new int[3];

        for (int i = 0; i < aligns.Length; i++)
        {
            var options = new RenderOptions
            {
                Dpi = 203,
                LayoutMode = LayoutMode.TextOnly,
                FontFamily = "Arial",
                FontSizeMm = 4,
                TextAlign = aligns[i]
            };
            using (RenderedLabel rendered = LabelRenderer.Render(LabelSpec.Label40x15, content, options))
            {
                rendered.Bitmap.Save(Path.Combine(_outDir, "align_" + names[i] + ".png"), ImageFormat.Png);
                InkBounds(rendered.Bitmap, out minX[i], out maxX[i]);
            }
        }

        Check(minX[0] < minX[1], "align left starts before center", minX[0] + " < " + minX[1]);
        Check(minX[1] < minX[2], "align center starts before right", minX[1] + " < " + minX[2]);
        Check(maxX[0] < maxX[1], "align left ends before center", maxX[0] + " < " + maxX[1]);
        Check(maxX[1] < maxX[2], "align right ends last", maxX[1] + " < " + maxX[2]);
        // 居中时左右留白应大致相等（半个字宽内）
        using (RenderedLabel rendered = LabelRenderer.Render(LabelSpec.Label40x15, content,
            new RenderOptions { Dpi = 203, LayoutMode = LayoutMode.TextOnly, FontFamily = "Arial", FontSizeMm = 4, TextAlign = TextAlign.Center }))
        {
            int left, right;
            InkBounds(rendered.Bitmap, out left, out right);
            int gapLeft = left;
            int gapRight = rendered.WidthDots - 1 - right;
            Check(Math.Abs(gapLeft - gapRight) <= 6, "align center is balanced", gapLeft + " vs " + gapRight);
        }
    }

    private static void InkBounds(Bitmap bmp, out int minX, out int maxX)
    {
        minX = int.MaxValue;
        maxX = -1;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                Color c = bmp.GetPixel(x, y);
                if (c.R > 128 || c.G > 128 || c.B > 128) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }
        }
        if (maxX < 0) { minX = 0; maxX = 0; }
    }

    /// <summary>字体库：导入 → 列表/枚举/建字体 → 删除，全程在临时目录里。</summary>
    private static void TestFontLibrary()
    {
        string source = null;
        foreach (string candidate in new[] { "arial.ttf", "segoeui.ttf", "simhei.ttf", "calibri.ttf" })
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), candidate);
            if (File.Exists(path)) { source = path; break; }
        }
        if (source == null)
        {
            Skip("font library import", "系统字体目录里找不到可用的 ttf");
            return;
        }

        string tempDir = Path.Combine(_outDir, "fonts-test");
        try
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
        catch { }
        Directory.CreateDirectory(tempDir);

        FontLibrary.ResetForTests();
        FontLibrary.FontDirectory = tempDir;
        try
        {
            Check(FontLibrary.GetCustomFonts().Count == 0, "font library starts empty", "");

            CustomFontInfo info = FontLibrary.Import(source);
            Check(info != null && !string.IsNullOrEmpty(info.FamilyName), "font import returns family", info == null ? "" : info.DisplayName);
            Check(File.Exists(info.FilePath), "font file copied", info.FilePath);
            Check(FontLibrary.GetCustomFonts().Count == 1, "font library counts one", FontLibrary.GetCustomFonts().Count.ToString());
            Check(ContainsFamily(FontLibrary.GetFamilies(), info.FamilyName), "font family listed", info.FamilyName);

            using (Font font = FontLibrary.CreateFont(info.FamilyName, 48, false))
            {
                Check(font != null, "font created from library", font == null ? "" : font.Name);
            }

            // 用导入字体真正渲染一张，确认布局可用
            var options = new RenderOptions
            {
                Dpi = 203,
                LayoutMode = LayoutMode.TextOnly,
                FontFamily = info.FamilyName,
                FontSizeMm = 4
            };
            using (RenderedLabel rendered = LabelRenderer.Render(LabelSpec.Label40x15,
                new LabelContent { Text = "Font Test 字体" }, options))
            {
                Check(rendered.WidthDots > 0 && rendered.HeightDots > 0, "render with imported font",
                    rendered.WidthDots + "x" + rendered.HeightDots);
            }

            // 同名再导入应自动改名，不覆盖原文件
            CustomFontInfo second = FontLibrary.Import(source);
            Check(second.FileName != info.FileName, "duplicate import renamed", second.FileName);
            Check(FontLibrary.GetCustomFonts().Count == 2, "font library counts two", FontLibrary.GetCustomFonts().Count.ToString());

            Check(FontLibrary.Remove(info), "font file deleted", info.FilePath);
            Check(!File.Exists(info.FilePath), "font file gone from disk", info.FilePath);
            Check(FontLibrary.GetCustomFonts().Count == 1, "font library counts one after delete",
                FontLibrary.GetCustomFonts().Count.ToString());

            FontLibrary.Remove(second);
            Check(FontLibrary.GetCustomFonts().Count == 0, "font library empty again", "");
        }
        catch (Exception ex)
        {
            Check(false, "font library exception", ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            FontLibrary.ResetForTests();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static bool ContainsFamily(IList<string> families, string family)
    {
        if (families == null || string.IsNullOrEmpty(family)) return false;
        foreach (string item in families)
        {
            if (string.Equals(item, family, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
