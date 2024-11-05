using AvaloniaToolbox.Core.IO;
using FinalFantasy16;
using SharpGen.Runtime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace FF16Tool
{
    public class FontFile
    {
        public const int CHAR_TABLE_COUNT = 65536;

        public uint Flags;
        public float[] Params;

        public ushort Unknown;

        public ushort[] CharTable = new ushort[CHAR_TABLE_COUNT];   

        public string TextureFileName;
        public string KerningFileName;

        public ushort Unknown2;
        public float Width;
        public float Height;
        public ushort Unknown3;

        public List<FontGlyph> FontGlyphs = new List<FontGlyph>();

        public FontFile() { }

        public FontFile(string path)
        {
            Read(new FileReader(path));
        }

        public void Save(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) {
                Write(new FileWriter(fs));
            }
        }

        private void Read(FileReader reader)
        {
            reader.ReadSignature("FNT ");
            Flags = reader.ReadUInt32(); //flags?
            uint stringPoolSize = reader.ReadUInt32();
            uint fontDataSize = reader.ReadUInt32();
            Unknown = reader.ReadUInt16(); //?
            ushort numCharacterInfo = reader.ReadUInt16();
            ushort numCharSpacings = reader.ReadUInt16();
            reader.ReadUInt16();

            Params = reader.ReadSingles(5);
            Unknown2 = reader.ReadUInt16();
            Width = reader.ReadUInt16() / 4;
            Height = reader.ReadUInt16() / 4;
            Unknown3 = reader.ReadUInt16();

            reader.SeekBegin(64);
            TextureFileName = reader.ReadStringZeroTerminated();
            KerningFileName  = reader.ReadStringZeroTerminated();

            reader.SeekBegin(64 + stringPoolSize);
            //a big table of character indices that index the glyph list by character code
            CharTable = reader.ReadUInt16s(CHAR_TABLE_COUNT); //always 65536

            FontGlyph[] glyphs = new FontGlyph[numCharacterInfo];
            for (int i = 0; i < numCharacterInfo; i++)
            {
                glyphs[i] = new FontGlyph();
                glyphs[i].Character = (char)CharTable.ToList().FindIndex(x => x == i);

                ushort unk1 = reader.ReadUInt16(); //0
                glyphs[i].SpacingCount = reader.ReadUInt16(); //counter to char space section after
                glyphs[i].SpacingIndex = reader.ReadUInt32(); //index to char space section after

                glyphs[i].OffsetX = reader.ReadSingle(); 
                glyphs[i].OffsetY = reader.ReadSingle(); 
                glyphs[i].CharacterWidth = reader.ReadSingle(); //advances X
                float zero = reader.ReadSingle(); //0

                glyphs[i].BitmapPosX = reader.ReadUInt16() / 4f;
                glyphs[i].BitmapPosY = reader.ReadUInt16() / 4f;
                glyphs[i].BitmapWidth = reader.ReadUInt16() / 4f;
                glyphs[i].BitmapHeight = reader.ReadUInt16() / 4f;
            }

            Debug.Assert(reader.Position == 64 + stringPoolSize + CHAR_TABLE_COUNT * 2 + numCharacterInfo * 32);

            FontSpacing[] spacings = new FontSpacing[numCharSpacings];
            for (int i = 0; i < numCharSpacings; i++)
            {
                spacings[i] = new FontSpacing()
                {
                    Char1 = reader.ReadUInt16(),
                    Char2 = reader.ReadUInt16(),
                    //offset or spacing between characters?
                    OffsetX = reader.ReadSingle(),
                    OffsetY = reader.ReadSingle(),
                };
            }

            for (int i = 0; i < numCharacterInfo; i++)
            {
                for (int j = 0; j < glyphs[i].SpacingCount; j++)
                    glyphs[i].FontSpacing.Add(spacings[glyphs[i].SpacingIndex + j]);
            }

            FontGlyphs.AddRange(glyphs);

            File.WriteAllText("test.xml", ToXml());
        }

        public void Write(FileWriter writer)
        {
            foreach (var g in this.FontGlyphs)
                g.FontSpacing.Clear();

            writer.Write(Encoding.ASCII.GetBytes("FNT "));
            writer.Write(Flags);
            writer.Write(0); //string pool size later
            writer.Write(0); //data size later

            writer.Write((ushort)Unknown);
            writer.Write((ushort)this.FontGlyphs.Count);
            writer.Write((ushort)this.FontGlyphs.Sum(x => x.FontSpacing.Count));
            writer.Write((ushort)0); //padding

            writer.Write(Params);
            writer.Write((ushort)Unknown2);
            writer.Write((ushort)(Width * 4));
            writer.Write((ushort)(Height * 4));
            writer.Write((ushort)Unknown3);

            writer.SeekBegin(64);

            var str_pos = writer.Position;

            writer.Write(Encoding.UTF8.GetBytes(TextureFileName));
            writer.Write((byte)0);
            writer.Write(Encoding.UTF8.GetBytes(KerningFileName));
            writer.Write((byte)0);

            writer.Align(16);
            var stringPoolSize = writer.Position - str_pos;
            writer.WriteSectionSizeU32(8, stringPoolSize);

            var data_pos = writer.Position;

            writer.Write(CharTable);

            int fontSpaceIdx = 0;
            foreach (var g in this.FontGlyphs)
            {
                writer.Write((ushort)0);
                writer.Write((ushort)g.FontSpacing.Count);
                writer.Write(g.FontSpacing.Count > 0 ? fontSpaceIdx : 0);
                writer.Write(g.OffsetX);
                writer.Write(g.OffsetY);
                writer.Write(g.CharacterWidth);
                writer.Write(0);

                writer.Write((ushort)(g.BitmapPosX * 4));
                writer.Write((ushort)(g.BitmapPosY * 4));
                writer.Write((ushort)(g.BitmapWidth * 4));
                writer.Write((ushort)(g.BitmapHeight * 4));

                fontSpaceIdx += g.FontSpacing.Count;
            }
            foreach (var g in this.FontGlyphs)
            {
                foreach (var s in g.FontSpacing)
                {
                    writer.Write((ushort)s.Char1);
                    writer.Write((ushort)s.Char2);
                    writer.Write(s.OffsetX);
                    writer.Write(s.OffsetY);
                }
            }

            var dataSize = writer.Position - data_pos;
            writer.WriteSectionSizeU32(12, dataSize);
        }

        public void Export(TexFile texFile)
        {
            var image = texFile.Textures[0].GetImage();
            for (int i = 0; i < this.FontGlyphs.Count; i++)
            {
                var g = this.FontGlyphs[i];
                if (g.BitmapWidth == 0)
                    continue;

                var clone = image.Clone();
                clone.Mutate(x => x.Crop(new Rectangle(
                    (int)g.BitmapPosX, (int)g.BitmapPosY, (int)g.BitmapWidth, (int)g.BitmapHeight)));
                clone.SaveAsPng($"Images\\{(int)g.Character}.png");
            }
        }

        public string ToXml()
        {
            using (var writer = new System.IO.StringWriter())
            {
                var serializer = new XmlSerializer(typeof(FontFile));
                serializer.Serialize(writer, this);
                writer.Flush();
                return writer.ToString();
            }
        }

        public class FontGlyph
        {
            public char Character;

            public float CharacterWidth;

            public float OffsetX;
            public float OffsetY;

            public float BitmapPosX;
            public float BitmapPosY;
            public float BitmapWidth;
            public float BitmapHeight;


            public uint SpacingIndex;
            public uint SpacingCount;

            public List<FontSpacing> FontSpacing = new List<FontSpacing>();
        }

        public class FontSpacing
        {
            public ushort Char1;
            public ushort Char2;
            public float OffsetX;
            public float OffsetY;
        }
    }
}
