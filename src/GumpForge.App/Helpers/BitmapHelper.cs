using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;

namespace GumpForge.App.Helpers;

/// <summary>
/// Converts raw RGBA8888 pixel data (from MUL decoding) to Avalonia WriteableBitmaps
/// for display in the Asset Browser and on the Canvas.
/// </summary>
public static class BitmapHelper
{
    /// <summary>
    /// Create an Avalonia WriteableBitmap from raw RGBA8888 pixel data.
    /// </summary>
    public static WriteableBitmap? CreateBitmap(byte[] pixelData, int width, int height)
    {
        if (pixelData.Length == 0 || width <= 0 || height <= 0)
            return null;

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888,
                AlphaFormat.Unpremul);

            using var buffer = bitmap.Lock();
            // Copy pixel data row by row (handle stride differences)
            int srcStride = width * 4;
            int dstStride = buffer.RowBytes;

            if (srcStride == dstStride)
            {
                Marshal.Copy(pixelData, 0, buffer.Address, Math.Min(pixelData.Length, dstStride * height));
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    int srcOffset = y * srcStride;
                    IntPtr dstOffset = buffer.Address + y * dstStride;
                    int bytesToCopy = Math.Min(srcStride, dstStride);
                    if (srcOffset + bytesToCopy <= pixelData.Length)
                        Marshal.Copy(pixelData, srcOffset, dstOffset, bytesToCopy);
                }
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create a scaled thumbnail from raw pixel data.
    /// Simple nearest-neighbor downscale for thumbnails.
    /// </summary>
    public static WriteableBitmap? CreateThumbnail(byte[] pixelData, int srcWidth, int srcHeight, int maxSize = 64)
    {
        if (pixelData.Length == 0 || srcWidth <= 0 || srcHeight <= 0)
            return null;

        // Calculate scaled dimensions maintaining aspect ratio
        double scale = Math.Min((double)maxSize / srcWidth, (double)maxSize / srcHeight);
        if (scale >= 1.0)
            return CreateBitmap(pixelData, srcWidth, srcHeight); // No downscale needed

        int dstWidth = Math.Max(1, (int)(srcWidth * scale));
        int dstHeight = Math.Max(1, (int)(srcHeight * scale));

        // Nearest-neighbor downscale
        byte[] scaledPixels = new byte[dstWidth * dstHeight * 4];
        for (int dy = 0; dy < dstHeight; dy++)
        {
            int sy = (int)(dy / scale);
            if (sy >= srcHeight) sy = srcHeight - 1;

            for (int dx = 0; dx < dstWidth; dx++)
            {
                int sx = (int)(dx / scale);
                if (sx >= srcWidth) sx = srcWidth - 1;

                int srcIdx = (sy * srcWidth + sx) * 4;
                int dstIdx = (dy * dstWidth + dx) * 4;

                if (srcIdx + 3 < pixelData.Length && dstIdx + 3 < scaledPixels.Length)
                {
                    scaledPixels[dstIdx] = pixelData[srcIdx];
                    scaledPixels[dstIdx + 1] = pixelData[srcIdx + 1];
                    scaledPixels[dstIdx + 2] = pixelData[srcIdx + 2];
                    scaledPixels[dstIdx + 3] = pixelData[srcIdx + 3];
                }
            }
        }

        return CreateBitmap(scaledPixels, dstWidth, dstHeight);
    }
}
