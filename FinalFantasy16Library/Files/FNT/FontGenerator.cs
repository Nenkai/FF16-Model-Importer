using AvaloniaToolbox.Core;
using FF16Tool;
using FinalFantasy16;
using FinalFantasyConvertTool.FNT;
using RectpackSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Syroot.BinaryData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinalFantasyConvertTool
{
    public class FontGenerator
    {
        public static void Generate(FontFile fontFile, TexFile texFile, string ttfFile, Settings settings)
        {
            settings.PrepareCharacterSettings();

            if (!settings.KeepOriginalCharacters)
                fontFile.FontGlyphs.Clear();

            var typeface = SKTypeface.FromFile(ttfFile);
            SKFont font = new SKFont(typeface, settings.FontSize);

            SDFGenerator sdf_drawer = new SDFGenerator(settings.FontSize + 8);

            SKPaint paint = new SKPaint
            {
                Typeface = typeface,
                TextSize = settings.FontSize,
                IsAntialias = true,
                Color = SKColors.White,
            };

            SKRect bounds = new SKRect();

            float maxAscent = 0;

            List<GlyphEntry> importedGlyphs = new List<GlyphEntry>();

            if (settings.KeepOriginalCharacters)
            {
                var img = texFile.Textures[0].GetImage();
                foreach (var g in fontFile.FontGlyphs)
                {
                    if (g.BitmapWidth == 0)
                    {
                        importedGlyphs.Add(new GlyphEntry()
                        {
                            Width = 0,
                            Height = 0,
                            Data = new byte[0],
                            AdvanceX = g.CharacterWidth,
                            OffsetX = g.OffsetX,
                            OffsetY = g.OffsetY,
                            Character = g.Character,
                        });
                        continue;
                    }

                    var clone = img.Clone();
                    clone.Mutate(x => x.Crop(new Rectangle(
                        (int)g.BitmapPosX,
                        (int)g.BitmapPosY,
                        (int)g.BitmapWidth, 
                        (int)g.BitmapHeight)));

                  //  clone.SaveAsPng(Path.Combine("c", $"{(int)g.Character}.png"));
                    
                    var rgba = clone.GetSourceInBytes();
                    var w = clone.Width;
                    var h = clone.Height;

                    importedGlyphs.Add(new GlyphEntry()
                    {
                        X = g.BitmapPosX,
                        Y = g.BitmapPosY,
                        Width = w, Height = h,
                        Data = rgba,
                        AdvanceX = g.CharacterWidth,
                        OffsetX = g.OffsetX,
                        OffsetY = g.OffsetY,
                        Character = g.Character,
                    });

                    clone.Dispose();
                }
            }

            foreach (var c in settings.CharactersImport)
            {
                if (!font.Typeface.ContainsGlyph(c))
                    continue;

                var charWidth = font.MeasureText(c.ToString(), out bounds);
                //ascent calculation for imported characters
                maxAscent = Math.Max(maxAscent, -bounds.Top);
            }

            int spacing = 25;

            foreach (var c in settings.CharactersImport)
            {
                if (!font.Typeface.ContainsGlyph(c))
                    continue;

                var glyph = sdf_drawer.Draw(font, c);
                if (glyph.Data.Length != glyph.Width * glyph.Height * 4 || glyph.Data.Length == 0)
                    continue;

                var charWidth = font.MeasureText(c.ToString(), out bounds);
                var advanceX = font.GetGlyphWidths(c.ToString()).FirstOrDefault();
                var offsets = font.GetGlyphPositions(c.ToString());

                float offsetX = bounds.Left + offsets[0].X;
                float offsetY = bounds.Top;

                if (bounds.Left < 0)
                    offsetX = bounds.Left + offsets[0].X - (spacing / 2);

                if (bounds.Top > 0)
                {
                    offsetY += (spacing / 2);
                }

                //Characters with bounding and offset info
                glyph.Character = c;
                glyph.OffsetX = offsetX - 22;
                glyph.OffsetY = offsetY - 22;
                if (advanceX  != 0)
                    glyph.AdvanceX = advanceX + (spacing / 2);
                else
                    glyph.AdvanceX = 1;

                importedGlyphs.Add(glyph);
            }

            var image = PackGlyphs(ref importedGlyphs);
            texFile.Textures[0].Replace(image);

            fontFile.Width = image.Width;
            fontFile.Height = image.Height;

            //Finalize font params
            ushort[] character_indices = new ushort[FontFile.CHAR_TABLE_COUNT];

            fontFile.FontGlyphs.Clear();
            fontFile.FontGlyphs.Add(new FontFile.FontGlyph()
            {
                OffsetX = -21.33333f,
                OffsetY = -21.33333f,
                CharacterWidth = 26.5625f,
            });

            foreach (var g in importedGlyphs.OrderBy(x => x.Character))
            {
                //set index
                character_indices[g.Character] = (ushort)fontFile.FontGlyphs.Count;

                Console.WriteLine($"{g.Character} {g.X}x{g.Y} {g.Width}x{g.Height} {g.OffsetX}x{g.OffsetY} {g.AdvanceX}");

                fontFile.FontGlyphs.Add(new FontFile.FontGlyph()
                {
                    BitmapWidth = g.Width,
                    BitmapHeight = g.Height,
                    BitmapPosX = g.X,
                    BitmapPosY = g.Y,
                    OffsetX = g.OffsetX,
                    OffsetY = g.OffsetY,
                    CharacterWidth = g.AdvanceX,
                    Character = g.Character,
                    FontSpacing = new List<FontFile.FontSpacing>(),
                });
            }
            fontFile.CharTable = character_indices;
        }

        //pack compute
        public static Image<Rgba32> PackGlyphs(ref List<GlyphEntry> glyphs)
        {
            glyphs = glyphs.OrderByDescending(x => x.Width).ThenByDescending(x => x.Height).ToList();

            PackingRectangle[] rectangles = new PackingRectangle[glyphs.Count];
            for (int i = 0; i < glyphs.Count; i++)
            {
                if ((uint)glyphs[i].Width == 0)
                {
                    rectangles[i] = new PackingRectangle(
                        (uint)glyphs[i].X,
                        (uint)glyphs[i].Y,
                      1,
                      1, i);
                }
                else
                {
                    rectangles[i] = new PackingRectangle(
                      (uint)glyphs[i].X,
                      (uint)glyphs[i].Y,
                      (uint)glyphs[i].Width,
                      (uint)glyphs[i].Height, i);
                }
            }

            RectanglePacker.Pack(rectangles, out PackingRectangle bounds,
                PackingHints.UnusualSizes |
                PackingHints.FindBest | 
                PackingHints.TryByArea |
                PackingHints.MostlySquared);

            uint Align(uint value, uint alignment)
            {
                return (value + (alignment - 1)) & ~(alignment - 1);
            }

            // Create output image
           // Image<Rgba32> outputImage = new Image<Rgba32>((int)1164, (int)1244, new Rgba32(0, 0, 0, 255));
             Image<Rgba32> outputImage = new Image<Rgba32>((int)bounds.Width, (int)bounds.Height, new Rgba32(0, 0, 0, 255));
            for (int i = 0; i < glyphs.Count; i++)
            {
                var id = rectangles[i].Id;

                if (!(glyphs[id].Character <= 'a' || glyphs[id].Character >= 'z' ||
                     glyphs[id].Character <= 'A' || glyphs[id].Character >= 'Z'))
                    continue;

                int x = (int)rectangles[i].X;
                int y = (int)rectangles[i].Y;
                int w = (int)rectangles[i].Width;
                int h = (int)rectangles[i].Height;

                if (glyphs[id].Data.Length == 0)
                    continue;

                glyphs[id].X = x;
                glyphs[id].Y = y;
                glyphs[id].Width = w;
                glyphs[id].Height = h;

                Image<Rgba32> glyphImage = Image.LoadPixelData<Rgba32>(
                    glyphs[id].Data, 
                    (int)glyphs[id].Width, 
                    (int)glyphs[id].Height);

                outputImage.Mutate(ctx => ctx.DrawImage(glyphImage, new Point(x, y), 1f));

                glyphImage.Dispose();
            }
            return outputImage;
        }

        public class GlyphEntry
        {
            public char Character { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float OffsetX { get; set; }  
            public float OffsetY { get; set; }
            public float AdvanceX { get; set; }

            public byte[] Data;
        }

        public class Settings
        {
            public bool KeepOriginalCharacters = true;
            public int FontSize = 48;

            public string CharactersImport = "";

            public void PrepareCharacterSettings()
            {
                CharactersImport = "";
              //  for (int i = 32; i < 255; i++)
               //     CharactersImport += (char)i;
                //Thai
                for (int i = 0x0E00; i < 0x0E7F; i++)
                    CharactersImport += (char)i;
            }
        }
    }
}
