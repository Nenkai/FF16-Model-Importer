using FF16Tool;
using FinalFantasy16;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinalFantasyConvertTool
{
    public class FontPreview
    {
        private FontFile FontFile;
        private TexFile TexFile;
        private Image<Rgba32> FontImage;

        public FontPreview(FontFile font, TexFile texFile) {
            FontFile = font;
            FontFile.Save("saved.fnt");

            //string texFilePath = Path.GetFileName(font.TextureFileName);
            //TexFile = new TexFile(texFilePath);
            FontImage = texFile.Textures[0].GetImage();
            FontImage = ConvertToAlphaWithWhiteRgb(FontImage);

            FontImage.SaveAsPng("Full.png");
        }

        public void CreatePreview(string text)
        {
            Image<Rgba32> fontImage = new Image<Rgba32>(2024, 100, new Rgba32(0, 0, 0, 255));

            float max = FontFile.Params[3];

            {
                var glyphv = FontFile.FontGlyphs[FontFile.CharTable['i']];
                var glyphi = FontFile.FontGlyphs[FontFile.CharTable['v']];

                var ofsY1 = glyphv.BitmapHeight + glyphv.OffsetY;
                var ofsY2 = glyphi.BitmapHeight + glyphi.OffsetY;
                var diff = ofsY2 - ofsY1;
                Console.WriteLine();
            }
            {
                var glyphv = FontFile.FontGlyphs[FontFile.CharTable['e']];
                var glyphi = FontFile.FontGlyphs[FontFile.CharTable['v']];

                var ofsY1 = MathF.Max(max, glyphv.BitmapHeight) + glyphv.OffsetY;
                var ofsY2 = MathF.Max(max, glyphi.BitmapHeight) + glyphi.OffsetY;
                var diff = ofsY2 - ofsY1;
                Console.WriteLine();
            }

            int x = 60;
            int y = 0;
            foreach (var c in text)
            {
                var glyph = FontFile.FontGlyphs[FontFile.CharTable[c]];
                if (glyph.BitmapWidth == 0)
                    continue;

                var clone = FontImage.Clone();
                clone.Mutate(f => f.Crop(new Rectangle(
                   (int)glyph.BitmapPosX,
                   (int)glyph.BitmapPosY,
                   (int)glyph.BitmapWidth,
                   (int)glyph.BitmapHeight)));

                var ofsY = fontImage.Height + (int)(Math.Ceiling(glyph.OffsetY)) - 30;
                var ofsX = (int)(Math.Ceiling(glyph.OffsetX));

                fontImage.Mutate(f => f.DrawImage(clone, new Point(
                    x + (int)ofsX, 
                    y + (int)ofsY), 1f));

                x += (int)(glyph.CharacterWidth);
            }
            fontImage.SaveAsPng("preview.png");
        }

        public static Image<Rgba32> ConvertToAlphaWithWhiteRgb(Image<Rgba32> grayscaleImage)
        {
            // Load the black and white image (grayscale)
            // Create a new image with white background and the same dimensions
            var width = grayscaleImage.Width;
            var height = grayscaleImage.Height;
            var resultImage = new Image<Rgba32>(width, height);

            // Iterate through pixels and copy grayscale as alpha channel
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Get grayscale value (L8 stores luminance)
                    byte alphaValue = grayscaleImage[x, y].R;

                    // Set RGB to white (255, 255, 255) and alpha to the grayscale value
                    resultImage[x, y] = new Rgba32(255, 255, 255, alphaValue);
                }
            }

            return resultImage;
        }
    }
}
