using System.IO;
using System.Windows.Media.Imaging;
using LibmpvIptvClient.Diagnostics;
using QRCoder;

namespace LibmpvIptvClient.Services.WebRemote
{
    /// <summary>
    /// Renders the Web Remote access URL into a QR code image for the settings window.
    /// </summary>
    public static class WebRemoteQrCode
    {
        /// <summary>
        /// Creates a frozen QR code bitmap for the given text, or null when the text is empty or generation fails.
        /// </summary>
        public static BitmapSource? Create(string? text, int pixelsPerModule = 6)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(text!, QRCodeGenerator.ECCLevel.M);
                var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(png);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                Logger.Debug($"[WebRemote] QR code generation failed: {ex.Message}");
                return null;
            }
        }
    }
}
