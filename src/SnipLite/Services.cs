using System.Drawing.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace SnipLite;

internal static partial class Services
{
    /// <summary>Local OCR; no screenshot is sent to a server. Failures are logged and rethrown so callers can tell them from an empty result.</summary>
    public static async Task<OcrDocument> RecognizeTextAsync(Bitmap bitmap)
    {
        try { return await SimdOcr.RecognizeAsync(bitmap).ConfigureAwait(false); }
        catch (Exception ex)
        {
            AppLog.Write("文字识别失败", ex);
            throw;
        }
    }

    public static string? DecodeQr(Bitmap bitmap)
    {
        try
        {
            var reader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true
                }
            };

            return reader.Decode(bitmap)?.Text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>PNG, or JPEG with the configured quality (the default encoder uses 75).</summary>
    public static void SaveImage(Bitmap image, string path, bool jpeg, int quality = 92)
    {
        if (!jpeg)
        {
            image.Save(path, ImageFormat.Png);
            return;
        }

        var encoder = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

        if (encoder is null)
        {
            image.Save(path, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(quality, 30, 100));
        image.Save(path, encoder, parameters);
    }
}
