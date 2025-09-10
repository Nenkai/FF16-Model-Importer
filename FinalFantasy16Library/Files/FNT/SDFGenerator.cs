using SkiaSharp;

using static FinalFantasy16Library.Files.FNT.FontGenerator;

namespace FinalFantasy16Library.Files.FNT;

public class SDFGenerator
{
    private const double INF = 1e20;

    private int Radius = 6;
    private double Cutoff = 0.2f;
    private int Buffer = 2;

    private double[] f;
    private double[] z;
    private ushort[] v;
    private double[] gridOuter;
    private double[] gridInner;

    public SDFGenerator(float fontSize = 36)
    {
        var size = (int)fontSize + Buffer * 4;
        f = new double[size];
        z = new double[size + 1];
        v = new ushort[size];
        gridOuter = new double[size * size];
        gridInner = new double[size * size];
    }

    public GlyphEntry Draw(SKFont font, char c)
    {
        SKRect bounds = new SKRect();
        var charWidth = font.MeasureText(c.ToString(), out bounds);

        int padding = 5;

        var glyphWidth = (int)bounds.Width + padding * 2;
        var glyphHeight = (int)(bounds.Height + padding * 2);
        var offsetY = bounds.Top < 0 ? bounds.Top : 0;

        byte[] rgba = new byte[glyphWidth * glyphHeight * 4];

        if (glyphWidth == 0) return new GlyphEntry() { Data = new byte[0] };

        SKPaint paint = new SKPaint
        {
            Typeface = font.Typeface,
            TextSize = font.Size,
            IsAntialias = true,
            Color = new SKColor(128, 128, 128),
        };

        SKImageInfo info = new SKImageInfo(glyphWidth, glyphHeight);
        using (SKSurface surface = SKSurface.Create(info))
        {

            float x = padding - bounds.Left; // Align the character to the left
            float y = padding - bounds.Top;  // Align the character to the top

            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawText(c.ToString(), x, y, font, paint);

            using (SKImage image = surface.Snapshot())
            {
                rgba = SKHelper.GetImagePixelData(image);
            }
        }

        int width = glyphWidth + 2 * Buffer;
        int height = glyphHeight + 2 * Buffer;

        int len = width * height;
        byte[] data = new byte[len];

        // Initialize grids outside the glyph range to alpha 0
        Array.Fill(gridOuter, INF, 0, len);
        Array.Fill(gridInner, 0, 0, len);

        for (int y = 0; y < glyphHeight; y++)
        {
            for (int x = 0; x < glyphWidth; x++)
            {
                float a = rgba[4 * (y * glyphWidth + x) + 3] / 255f; // alpha value
                if (a == 0) continue; // empty

                var j = (y + Buffer) * width + x + Buffer;

                if (a == 1)
                { // fully drawn pixels
                    gridOuter[j] = 0;
                    gridInner[j] = INF;
                }
                else
                { // aliased pixels
                    var d = 0.5 - a;
                    gridOuter[j] = d > 0 ? d * d : 0;
                    gridInner[j] = d < 0 ? d * d : 0;
                }
            }
        }

        EDT(gridOuter, 0, 0, width, height, width, f, v, z);
        EDT(gridInner, Buffer, Buffer, glyphWidth, glyphHeight, width, f, v, z);

        for (int i = 0; i < len; i++)
        {
            var d = Math.Sqrt(gridOuter[i]) - Math.Sqrt(gridInner[i]);
            data[i] = (byte)Math.Max(0, Math.Min(255, 255 - 255 * (d / Radius + Cutoff)));
            data[i] = (byte)(data[i] * 0.8);
        }

        //red to rgba
        byte[] rgba_output = new byte[width * height * 4];
        int idx = 0;
        for (int i = 0; i < width * height; i++)
        {
            rgba_output[idx + 0] = data[i];
            rgba_output[idx + 1] = data[i];
            rgba_output[idx + 2] = data[i];
            rgba_output[idx + 3] = 255;

            idx += 4;
        }

        return new GlyphEntry()
        {
            Width = width,
            Height = height,
            Data = rgba_output,
        };
    }

    // Euclidean squared distance transform (2D)
    private void EDT(double[] grid, int x0, int y0, int width, int height, int gridSize, double[] f, ushort[] v, double[] z)
    {
        for (int x = x0; x < x0 + width; x++)
        {
            EDT1D(grid, y0 * gridSize + x, gridSize, height, f, v, z);
        }

        for (int y = y0; y < y0 + height; y++)
        {
            EDT1D(grid, y * gridSize + x0, 1, width, f, v, z);
        }
    }

    // 1D squared distance transform
    private void EDT1D(double[] grid, int offset, int stride, int length, double[] f, ushort[] v, double[] z)
    {
        v[0] = 0;
        z[0] = -INF;
        z[1] = INF;
        f[0] = grid[offset];

        int k = 0;
        double s;
        for (int q = 1; q < length; q++)
        {
            f[q] = grid[offset + q * stride];
            int q2 = q * q;
            do
            {
                int r = v[k];
                s = (double)(f[q] - f[r] + q2 - r * (double)r) / (q - r) / 2;
            } while (s <= z[k] && --k > -1);

            k++;
            v[k] = (ushort)q;
            z[k] = s;
            z[k + 1] = INF;
        }

        k = 0;
        for (int q = 0; q < length; q++)
        {
            while (z[k + 1] < q) k++;
            int r = v[k];
            int qr = q - r;
            grid[offset + q * stride] = f[r] + qr * (double)qr;
        }
    }
}
