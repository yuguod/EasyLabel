namespace EasyLabel.Core
{
    public enum BarcodeType
    {
        Code128 = 0,
        Code39 = 1,
        Ean13 = 2
    }

    public enum QrEcc
    {
        L = 0,
        M = 1,
        Q = 2,
        H = 3
    }

    public enum MediaType
    {
        Gap = 0,
        Continuous = 1
    }

    public enum LayoutMode
    {
        Auto = 0,
        TextOnly = 1,
        BarcodeOnly = 2,
        QrOnly = 3,
        TextBarcode = 4,
        TextQr = 5,
        BarcodeQr = 6,
        All = 7
    }

    public enum TextAlign
    {
        Left = 0,
        Center = 1,
        Right = 2
    }
}
