using QRCoder;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WifiDownload
{
    public partial class QRWindow : Window
    {
        public QRWindow(string url)
        {
            InitializeComponent();
            TxtUrl.Text = url;
            GenerateQr(url);
        }

        private void GenerateQr(string text)
        {
            try
            {
                using var qr = new QRCodeGenerator();
                using var data = qr.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
                using var code = new QRCode(data);
                using Bitmap bmp = code.GetGraphic(20);
                ImgQr.Source = BitmapToImageSource(bmp);
            }
            catch { }
        }

        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            return img;
        }
    }
}