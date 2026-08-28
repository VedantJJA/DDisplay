using System.Drawing;
using System.Drawing.Imaging;

namespace DDisplay.Core.Capture;

/// <summary>
/// Represents a dirty screen tile update.
/// </summary>
public sealed class TilePatch
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string ImageBase64 { get; set; } = string.Empty;
}

public enum FrameClassification
{
    Static,
    CursorOnly,
    Patch,
    Full,
}

/// <summary>
/// Snaps dirty rectangles to a fixed tile grid and extracts compressed tile patches for low-overhead partial screen updates.
/// </summary>
public static class TilePatchCompressor
{
    public const int TileSize = 64;
    public const double SmallChangeThreshold = 0.05; // 5% of screen area

    public static (int X, int Y, int Width, int Height) SnapToTileGrid(int left, int top, int right, int bottom, int screenWidth, int screenHeight)
    {
        int x0 = (left / TileSize) * TileSize;
        int y0 = (top / TileSize) * TileSize;
        int x1 = Math.Min(screenWidth, ((right + TileSize - 1) / TileSize) * TileSize);
        int y1 = Math.Min(screenHeight, ((bottom + TileSize - 1) / TileSize) * TileSize);

        return (x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    public static List<(int X, int Y, int Width, int Height)> MergeAndSnapRectangles(
        IEnumerable<(int Left, int Top, int Right, int Bottom)> dirtyRects,
        int screenWidth,
        int screenHeight)
    {
        var tiles = new HashSet<(int TileX, int TileY)>();

        foreach (var r in dirtyRects)
        {
            int startTileX = r.Left / TileSize;
            int endTileX = (r.Right + TileSize - 1) / TileSize;
            int startTileY = r.Top / TileSize;
            int endTileY = (r.Bottom + TileSize - 1) / TileSize;

            for (int ty = startTileY; ty < endTileY; ty++)
            {
                for (int tx = startTileX; tx < endTileX; tx++)
                {
                    tiles.Add((tx, ty));
                }
            }
        }

        var result = new List<(int X, int Y, int Width, int Height)>();
        foreach (var (tx, ty) in tiles)
        {
            int x = tx * TileSize;
            int y = ty * TileSize;
            int w = Math.Min(TileSize, screenWidth - x);
            int h = Math.Min(TileSize, screenHeight - y);

            if (w > 0 && h > 0)
            {
                result.Add((x, y, w, h));
            }
        }

        return result;
    }

    public static double CalculateChangeRatio(IEnumerable<(int Left, int Top, int Right, int Bottom)> dirtyRects, int screenWidth, int screenHeight)
    {
        long totalArea = (long)screenWidth * screenHeight;
        if (totalArea <= 0) return 0;

        long dirtyArea = 0;
        foreach (var r in dirtyRects)
        {
            long w = Math.Max(0, r.Right - r.Left);
            long h = Math.Max(0, r.Bottom - r.Top);
            dirtyArea += w * h;
        }

        return (double)dirtyArea / totalArea;
    }

    public static unsafe TilePatch? ExtractTilePatch(
        byte[] bgraFrame,
        int screenWidth,
        int screenHeight,
        int tileX,
        int tileY,
        int tileW,
        int tileH,
        int quality = 70)
    {
        if (tileX + tileW > screenWidth || tileY + tileH > screenHeight) return null;

        using var bmp = new Bitmap(tileW, tileH, PixelFormat.Format32bppArgb);
        var bmpData = bmp.LockBits(new Rectangle(0, 0, tileW, tileH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            fixed (byte* pSrc = bgraFrame)
            {
                byte* pDst = (byte*)bmpData.Scan0;

                for (int row = 0; row < tileH; row++)
                {
                    int srcOffset = ((tileY + row) * screenWidth + tileX) * 4;
                    int dstOffset = row * bmpData.Stride;

                    Buffer.MemoryCopy(pSrc + srcOffset, pDst + dstOffset, tileW * 4, tileW * 4);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }

        using var ms = new MemoryStream();
        var encoder = GetEncoder(ImageFormat.Jpeg);
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);

        if (encoder != null)
        {
            bmp.Save(ms, encoder, encoderParams);
        }
        else
        {
            bmp.Save(ms, ImageFormat.Jpeg);
        }

        return new TilePatch
        {
            X = tileX,
            Y = tileY,
            Width = tileW,
            Height = tileH,
            ImageBase64 = Convert.ToBase64String(ms.ToArray()),
        };
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        return codecs.FirstOrDefault(c => c.FormatID == format.Guid);
    }
}
