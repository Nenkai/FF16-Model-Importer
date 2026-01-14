using System.Diagnostics;
using System.Text;
using System.Xml.Serialization;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

using Syroot.BinaryData;

using FinalFantasy16Library.Files.TEX;
using FinalFantasy16Library.Utils;

namespace FinalFantasy16Library.Files.FNT;

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

    public List<FontGlyph> FontGlyphs = [];

    public FontFile() { }

    public FontFile(string path)
    {
        using var fs = File.OpenRead(path);
        Read(new BinaryStream(fs));
    }

    public void Save(string path)
    {
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            Write(new BinaryStream(fs));
        }
    }

    private void Read(BinaryStream reader)
    {
        reader.ReadSignature("FNT "u8);
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

        reader.Position = 0x40;
        TextureFileName = reader.ReadString(StringCoding.ZeroTerminated);
        KerningFileName = reader.ReadString(StringCoding.ZeroTerminated);

        reader.Position = 0x40 + stringPoolSize;
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

        Debug.Assert(reader.Position == 0x40 + stringPoolSize + CHAR_TABLE_COUNT * 2 + numCharacterInfo * 32);

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

    public void Write(BinaryStream writer)
    {
        foreach (var g in FontGlyphs)
            g.FontSpacing.Clear();

        writer.Write("FNT "u8);
        writer.WriteUInt32(Flags);
        writer.WriteUInt32(0); //string pool size later
        writer.WriteUInt32(0); //data size later

        writer.WriteUInt16(Unknown);
        writer.WriteUInt16((ushort)FontGlyphs.Count);
        writer.WriteUInt16((ushort)FontGlyphs.Sum(x => x.FontSpacing.Count));
        writer.WriteUInt16((ushort)0); //padding

        writer.WriteSingles(Params);
        writer.WriteUInt16(Unknown2);
        writer.WriteUInt16((ushort)(Width * 4));
        writer.WriteUInt16((ushort)(Height * 4));
        writer.WriteUInt16(Unknown3);

        writer.Position = 0x40;

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
        foreach (var g in FontGlyphs)
        {
            writer.WriteUInt16(0);
            writer.WriteUInt16((ushort)g.FontSpacing.Count);
            writer.WriteInt32(g.FontSpacing.Count > 0 ? fontSpaceIdx : 0);
            writer.WriteSingle(g.OffsetX);
            writer.WriteSingle(g.OffsetY);
            writer.WriteSingle(g.CharacterWidth);
            writer.WriteUInt32(0);

            writer.WriteUInt16((ushort)(g.BitmapPosX * 4));
            writer.WriteUInt16((ushort)(g.BitmapPosY * 4));
            writer.WriteUInt16((ushort)(g.BitmapWidth * 4));
            writer.WriteUInt16((ushort)(g.BitmapHeight * 4));

            fontSpaceIdx += g.FontSpacing.Count;
        }
        foreach (var g in FontGlyphs)
        {
            foreach (var s in g.FontSpacing)
            {
                writer.WriteUInt16(s.Char1);
                writer.WriteUInt16(s.Char2);
                writer.WriteSingle(s.OffsetX);
                writer.WriteSingle(s.OffsetY);
            }
        }

        var dataSize = writer.Position - data_pos;
        writer.WriteSectionSizeU32(12, dataSize);
    }

    public void Export(TexFile texFile)
    {
        var image = texFile.Textures[0].GetImage();
        for (int i = 0; i < FontGlyphs.Count; i++)
        {
            var g = FontGlyphs[i];
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
        using (var writer = new StringWriter())
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
