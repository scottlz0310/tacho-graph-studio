using System.Runtime.InteropServices.WindowsRuntime;

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TachoGraphStudio.App.Stage;

public sealed class WriteableBitmapImageSourceFactory : IImageSourceFactory
{
    public ImageSource? Create(byte[] premultipliedBgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(premultipliedBgra);

        WriteableBitmap bitmap = new(width, height);
        using (Stream pixelStream = bitmap.PixelBuffer.AsStream())
        {
            pixelStream.Write(premultipliedBgra);
        }

        bitmap.Invalidate();
        return bitmap;
    }
}
