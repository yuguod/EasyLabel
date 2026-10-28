using System;

namespace EasyLabel.Core
{
    /// <summary>一张标签的内容。条码/二维码可以手动填写，也可以回退到文本。</summary>
    public sealed class LabelContent
    {
        public string Text { get; set; }
        public bool ShowBarcode { get; set; }
        public BarcodeType BarcodeType { get; set; }
        public string BarcodeValue { get; set; }
        public bool UseTextForBarcode { get; set; }
        public bool ShowQr { get; set; }
        public string QrValue { get; set; }
        public bool UseTextForQr { get; set; }

        public LabelContent()
        {
            Text = "";
            BarcodeType = BarcodeType.Code128;
            BarcodeValue = "";
            QrValue = "";
            UseTextForBarcode = true;
            UseTextForQr = true;
        }

        public string EffectiveText
        {
            get { return (Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim(); }
        }

        public string EffectiveBarcode
        {
            get
            {
                if (!ShowBarcode) return "";
                string v = (BarcodeValue ?? "").Trim();
                if (v.Length == 0 && UseTextForBarcode) v = EffectiveText;
                return v;
            }
        }

        public string EffectiveQr
        {
            get
            {
                if (!ShowQr) return "";
                string v = (QrValue ?? "").Trim();
                if (v.Length == 0 && UseTextForQr) v = EffectiveText;
                return v;
            }
        }

        public bool HasText
        {
            get { return EffectiveText.Length > 0; }
        }

        public bool HasBarcode
        {
            get { return EffectiveBarcode.Length > 0; }
        }

        public bool HasQr
        {
            get { return EffectiveQr.Length > 0; }
        }

        public LabelContent Clone()
        {
            return (LabelContent)MemberwiseClone();
        }
    }

    public sealed class ValidationResult
    {
        public bool IsValid { get { return Errors.Count == 0; } }
        public System.Collections.Generic.List<string> Errors { get; private set; }
        public System.Collections.Generic.List<string> Warnings { get; private set; }

        public ValidationResult()
        {
            Errors = new System.Collections.Generic.List<string>();
            Warnings = new System.Collections.Generic.List<string>();
        }

        public string ErrorText
        {
            get { return string.Join("；", Errors.ToArray()); }
        }

        public string WarningText
        {
            get { return string.Join("；", Warnings.ToArray()); }
        }
    }

    public sealed class LabelRenderException : Exception
    {
        public System.Collections.Generic.IList<string> Errors { get; private set; }

        public LabelRenderException(System.Collections.Generic.IList<string> errors)
            : base(errors != null && errors.Count > 0 ? string.Join("；", System.Linq.Enumerable.ToArray(errors)) : "标签渲染失败")
        {
            Errors = errors ?? new System.Collections.Generic.List<string>();
        }
    }
}
