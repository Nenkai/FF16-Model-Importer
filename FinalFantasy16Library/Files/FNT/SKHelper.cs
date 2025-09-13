using SkiaSharp;

namespace FinalFantasy16Library.Files.FNT;

public class SKHelper
{
    public static SKBitmap CreateBitmap(byte[] rgba, int width, int height)
    {
        // Create an SKBitmap with the specified width and height
        SKBitmap bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        // Lock the bitmap's pixel buffer
        using (var pixmap = bitmap.PeekPixels())
        {
            // Copy the RGBA byte array into the bitmap's pixel buffer
            System.Runtime.InteropServices.Marshal.Copy(rgba, 0, pixmap.GetPixels(), rgba.Length);
        }

        return bitmap;
    }

    public static byte[] GetImagePixelData(SKImage image)
    {
        using (SKPixmap pixmap = image.PeekPixels())
        {
            int size = pixmap.Width * pixmap.Height * pixmap.BytesPerPixel;
            byte[] pixelData = new byte[size];

            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixelData, System.Runtime.InteropServices.GCHandleType.Pinned);
            nint ptr = handle.AddrOfPinnedObject();

            SKImageInfo info = new SKImageInfo(pixmap.Width, pixmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            pixmap.ReadPixels(info, ptr, pixmap.RowBytes, 0, 0);

            handle.Free();

            return pixelData;
        }
    }
}
