using System;
using System.ComponentModel;
using System.Text;

namespace EasyLabel.Core
{
    public sealed class BatchOptions
    {
        public bool BarcodeEnabled { get; set; }
        public bool QrEnabled { get; set; }
        public bool UseTextFallback { get; set; }
        public BarcodeType BarcodeType { get; set; }
        public QrEcc QrEcc { get; set; }
        public bool ShowBarcodeText { get; set; }
        public LayoutMode LayoutMode { get; set; }
        public TextAlign TextAlign { get; set; }
        public double MarginTopMm { get; set; }
        public double MarginBottomMm { get; set; }
        public double MarginLeftMm { get; set; }
        public double MarginRightMm { get; set; }
        public double FontSizeMm { get; set; }
        public string FontFamily { get; set; }
        public bool Bold { get; set; }
        public LayoutPlan LayoutPlan { get; set; }
        public int BarcodeHeightPercent { get; set; }
        public int QrSizePercent { get; set; }
        public int Dpi { get; set; }

        public BatchOptions()
        {
            BarcodeEnabled = true;
            QrEnabled = false;
            UseTextFallback = true;
            BarcodeType = BarcodeType.Code128;
            QrEcc = QrEcc.M;
            ShowBarcodeText = true;
            LayoutMode = LayoutMode.Auto;
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
            Dpi = 600;
        }

        public RenderOptions CreateRenderOptions()
        {
            return new RenderOptions
            {
                Dpi = Dpi,
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
                FontFamily = FontFamily,
                Bold = Bold,
                LayoutPlan = LayoutPlan,
                BarcodeHeightPercent = BarcodeHeightPercent,
                QrSizePercent = QrSizePercent
            };
        }
    }

    public sealed class BatchRow : INotifyPropertyChanged
    {
        private bool _selected;
        private string _text;
        private string _barcode;
        private string _qr;
        private string _status;
        private string _error;
        private bool _isValid;

        public int RowNumber { get; set; }

        public bool Selected
        {
            get { return _selected; }
            set { if (_selected != value) { _selected = value; Raise("Selected"); } }
        }

        public string Text
        {
            get { return _text; }
            set { if (_text != value) { _text = value; Raise("Text"); } }
        }

        public string Barcode
        {
            get { return _barcode; }
            set { if (_barcode != value) { _barcode = value; Raise("Barcode"); } }
        }

        public string Qr
        {
            get { return _qr; }
            set { if (_qr != value) { _qr = value; Raise("Qr"); } }
        }

        public string Status
        {
            get { return _status; }
            set { if (_status != value) { _status = value; Raise("Status"); } }
        }

        public string Error
        {
            get { return _error; }
            set { if (_error != value) { _error = value; Raise("Error"); } }
        }

        public bool IsValid
        {
            get { return _isValid; }
            set { if (_isValid != value) { _isValid = value; Raise("IsValid"); } }
        }

        public BatchRow()
        {
            _selected = true;
            _text = "";
            _barcode = "";
            _qr = "";
            _status = "待打印";
            _error = "";
            _isValid = true;
        }

        public string EffectiveBarcode(BatchOptions options)
        {
            string v = (Barcode ?? "").Trim();
            if (v.Length == 0 && options.UseTextFallback) v = (Text ?? "").Trim();
            return v;
        }

        public string EffectiveQr(BatchOptions options)
        {
            string v = (Qr ?? "").Trim();
            if (v.Length == 0 && options.UseTextFallback) v = (Text ?? "").Trim();
            return v;
        }

        public LabelContent ToContent(BatchOptions options)
        {
            string barcode = EffectiveBarcode(options);
            string qr = EffectiveQr(options);
            return new LabelContent
            {
                Text = Text ?? "",
                ShowBarcode = options.BarcodeEnabled && barcode.Length > 0,
                BarcodeType = options.BarcodeType,
                BarcodeValue = barcode,
                UseTextForBarcode = false,
                ShowQr = options.QrEnabled && qr.Length > 0,
                QrValue = qr,
                UseTextForQr = false
            };
        }

        public void ValidateBasic(BatchOptions options)
        {
            var errors = new StringBuilder();
            string text = (Text ?? "").Trim();
            string barcode = EffectiveBarcode(options);
            string qr = EffectiveQr(options);

            if (text.Length == 0 && barcode.Length == 0 && qr.Length == 0)
                errors.Append("没有可打印内容");

            if (options.BarcodeEnabled && barcode.Length > 0)
            {
                string err = BarcodeFactory.ValidateBarcodeSyntax(barcode, options.BarcodeType);
                if (err != null) errors.Append(err);
            }

            if (options.QrEnabled && qr.Length > 0)
            {
                if (Encoding.UTF8.GetByteCount(qr) > 1200)
                    errors.Append("二维码内容过长（超过 1200 字节）");
            }

            if (errors.Length > 0)
            {
                IsValid = false;
                Error = errors.ToString();
                Status = "有误";
            }
            else
            {
                IsValid = true;
                Error = "";
                Status = "待打印";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
