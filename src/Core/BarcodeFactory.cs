using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ZXing;
using ZXing.Common;
using ZXing.OneD;
using ZXing.QrCode.Internal;

namespace EasyLabel.Core
{
    public sealed class EncodedBarcode
    {
        public string Value { get; set; }
        public string DisplayValue { get; set; }
        public BarcodeFormat Format { get; set; }
        public BitMatrix Matrix { get; set; }
    }

    public sealed class EncodedQr
    {
        public string Value { get; set; }
        public ByteMatrix Matrix { get; set; }
        public int Version { get; set; }
        public QrEcc Ecc { get; set; }
    }

    public static class BarcodeFactory
    {
        public static string TypeDisplayName(BarcodeType type)
        {
            switch (type)
            {
                case BarcodeType.Code39: return "Code 39";
                case BarcodeType.Ean13: return "EAN-13";
                default: return "Code 128";
            }
        }

        public static string ValidateBarcode(string value, BarcodeType type)
        {
            if (string.IsNullOrEmpty(value)) return "一维码内容为空";
            try
            {
                EncodeBarcode(value, type);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>轻量校验，不生成条码矩阵，适合批量列表实时标状态。</summary>
        public static string ValidateBarcodeSyntax(string value, BarcodeType type)
        {
            if (string.IsNullOrEmpty(value)) return "一维码内容为空";
            if (type == BarcodeType.Ean13)
            {
                try { NormalizeEan13(value); return null; }
                catch (Exception ex) { return ex.Message; }
            }
            if (type == BarcodeType.Code39)
            {
                string upper = value.ToUpperInvariant();
                foreach (char c in upper)
                {
                    if (!IsCode39Char(c))
                        return "Code 39 不支持字符 '" + c + "'";
                }
                return null;
            }
            foreach (char c in value)
            {
                if (c < 32 || c > 126)
                    return "Code 128 不支持中文或全角字符";
            }
            if (value.Length > 120) return "Code 128 内容过长（最多 120 个字符）";
            return null;
        }

        public static EncodedBarcode EncodeBarcode(string value, BarcodeType type)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("一维码内容为空");
            if (type == BarcodeType.Ean13)
            {
                string digits = NormalizeEan13(value);
                var hints = new Dictionary<EncodeHintType, object>();
                hints[EncodeHintType.MARGIN] = 10;
                var matrix = new EAN13Writer().encode(digits, BarcodeFormat.EAN_13, 0, 1, hints);
                return new EncodedBarcode
                {
                    Value = digits,
                    DisplayValue = digits,
                    Format = BarcodeFormat.EAN_13,
                    Matrix = matrix
                };
            }

            if (type == BarcodeType.Code39)
            {
                string upper = value.ToUpperInvariant();
                foreach (char c in upper)
                {
                    if (!IsCode39Char(c))
                        throw new ArgumentException("Code 39 不支持字符 '" + c + "'，仅支持 A-Z、0-9 和 - . 空格 $ / + %");
                }
                var hints = new Dictionary<EncodeHintType, object>();
                hints[EncodeHintType.MARGIN] = 10;
                var matrix = new Code39Writer().encode(upper, BarcodeFormat.CODE_39, 0, 1, hints);
                return new EncodedBarcode
                {
                    Value = upper,
                    DisplayValue = upper,
                    Format = BarcodeFormat.CODE_39,
                    Matrix = matrix
                };
            }

            foreach (char c in value)
            {
                if (c < 32 || c > 126)
                    throw new ArgumentException("Code 128 仅支持英文、数字和常用 ASCII 符号，不支持中文或全角字符");
            }
            if (value.Length > 120) throw new ArgumentException("Code 128 内容过长（最多 120 个字符）");

            var barcodeHints = new Dictionary<EncodeHintType, object>();
            barcodeHints[EncodeHintType.MARGIN] = 10;
            var code128 = new Code128Writer().encode(value, BarcodeFormat.CODE_128, 0, 1, barcodeHints);
            return new EncodedBarcode
            {
                Value = value,
                DisplayValue = value,
                Format = BarcodeFormat.CODE_128,
                Matrix = code128
            };
        }

        public static EncodedQr EncodeQr(string value, QrEcc ecc)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("二维码内容为空");
            var hints = new Dictionary<EncodeHintType, object>();
            hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
            var level = ToErrorCorrectionLevel(ecc);
            QRCode code = ZXing.QrCode.Internal.Encoder.encode(value, level, hints);
            int version = (code.Matrix.Width - 17) / 4;
            return new EncodedQr
            {
                Value = value,
                Matrix = code.Matrix,
                Version = version,
                Ecc = ecc
            };
        }

        public static ErrorCorrectionLevel ToErrorCorrectionLevel(QrEcc ecc)
        {
            switch (ecc)
            {
                case QrEcc.L: return ErrorCorrectionLevel.L;
                case QrEcc.Q: return ErrorCorrectionLevel.Q;
                case QrEcc.H: return ErrorCorrectionLevel.H;
                default: return ErrorCorrectionLevel.M;
            }
        }

        public static string EccDisplayName(QrEcc ecc)
        {
            switch (ecc)
            {
                case QrEcc.L: return "L（约 7%）";
                case QrEcc.Q: return "Q（约 25%）";
                case QrEcc.H: return "H（约 30%）";
                default: return "M（约 15%）";
            }
        }

        public static string NormalizeEan13(string value)
        {
            string digits = new string((value ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length == 12)
            {
                digits += EanCheckDigit(digits);
                return digits;
            }
            if (digits.Length == 13)
            {
                string check = EanCheckDigit(digits.Substring(0, 12));
                if (digits.Substring(12, 1) != check)
                    throw new ArgumentException("EAN-13 校验位不正确，应为 " + check);
                return digits;
            }
            throw new ArgumentException("EAN-13 需要 12 位（自动补校验位）或 13 位数字");
        }

        public static string EanCheckDigit(string first12)
        {
            if (first12 == null || first12.Length != 12)
                throw new ArgumentException("EAN-13 前 12 位无效");
            int sum = 0;
            for (int i = 0; i < 12; i++)
            {
                int d;
                if (!int.TryParse(first12.Substring(i, 1), NumberStyles.None, CultureInfo.InvariantCulture, out d))
                    throw new ArgumentException("EAN-13 只能包含数字");
                sum += d * ((i % 2 == 0) ? 1 : 3);
            }
            int check = (10 - (sum % 10)) % 10;
            return check.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsCode39Char(char c)
        {
            if (c >= 'A' && c <= 'Z') return true;
            if (c >= '0' && c <= '9') return true;
            return c == '-' || c == '.' || c == ' ' || c == '$' || c == '/' || c == '+' || c == '%';
        }
    }
}
