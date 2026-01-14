using System.Text;

using Syroot.BinaryData;

using FinalFantasy16Library.IO;
using FinalFantasy16Library.Utils;

namespace FinalFantasy16Library.Files.MTL;

public class MtlFile
{
    public uint HeaderFlags;

    public string ShaderPath;

    public List<TexturePath> TexturePaths = [];
    public List<TextureConstant> TextureConstants = [];
    public List<TextureBindInfo> TextureBindInfos = [];

    public float[] ParamData;

    public byte ParamFlag;
    public byte Unknown1;
    public ushort Unknown2;
    public byte Unknown3;

    public string Path;

    public MtlFile() { }

    public MtlFile(Stream stream, string path)
    {
        Path = path;
        Read(new BinaryStream(stream));
    }

    public void Save(string path)
    {
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            Save(fs);
        }
    }

    public void Save(Stream stream)
    {
        using (var writer = new BinaryStream(stream))
            Write(writer);
    }

    private void Read(BinaryStream reader)
    {
        reader.ReadSignature("MTL "u8);
        HeaderFlags = reader.ReadUInt32();

        if (HeaderFlags == 65285)
        {
            //todo, data just has raw params. rarely used
            {
                uint size = reader.ReadUInt32();
                uint paramSectionSize1 = reader.ReadUInt32();
                reader.ReadUInt16();
                reader.ReadUInt16();
                uint paramSectionSize2 = reader.ReadUInt32();
                reader.ReadUInt32();
                reader.ReadUInt32();
                reader.ReadBytes((int)paramSectionSize1);
                reader.ReadBytes((int)paramSectionSize2);
            }
            return;
        }

        reader.ReadUInt32(); //section size at 0x20
        uint endSectionSize = reader.ReadUInt32(); //0 or 16

        ushort numTexturePaths = reader.ReadUInt16();
        ParamFlag = reader.Read1Byte(); //editing this affects params some way
        Unknown1 = reader.Read1Byte();
        uint dataSectionSize = reader.ReadUInt32();
        ushort numConstantTextures = reader.ReadUInt16();
        ushort numExternalShaders = reader.ReadUInt16();
        Unknown2 = reader.ReadUInt16();
        byte padding = reader.Read1Byte();
        Unknown3 = reader.Read1Byte();
        ushort paramSize = reader.ReadUInt16();
        ushort numTotalTextures = reader.ReadUInt16();

        int Align(int pos, int alignment)
        {

            var amount = (-pos % alignment + alignment) % alignment;
            return pos + amount;
        }

        uint string_table_pos = (uint)(Align((int)(reader.Position + 4 +
            numTexturePaths * 8 + numConstantTextures * 8), 16) + (int)dataSectionSize);

        //shader
        ShaderPath = ReadString(reader, string_table_pos);

        //path is empty
        if (numExternalShaders == 0)
            ShaderPath = "";

        Console.WriteLine($"ShaderPath {ShaderPath}");

        for (int i = 0; i < numTexturePaths; i++)
        {
            string path = ReadString(reader, string_table_pos);
            string name = ReadString(reader, string_table_pos);

            TexturePaths.Add(new TexturePath() { Name = name, Path = path, });
        }
        for (int i = 0; i < numConstantTextures; i++)
        {
            var type = (ConstantType)reader.ReadUInt16();
            string name = ReadStringUshort(reader, string_table_pos);
            //color or single scalar
            object value = type == ConstantType.HalfFloat ? reader.ReadHalf() : new Rgba(reader.ReadBytes(4));
            TextureConstants.Add(new TextureConstant() { Type = type, Name = name, Value = value, });

            if (type == ConstantType.HalfFloat)
                reader.Align(4);
        }
        reader.Align(16);

        ParamData = reader.ReadSingles(paramSize / 4);

        for (int i = 0; i < numTotalTextures; i++)
        {
            byte slot = reader.Read1Byte();
            byte type = reader.Read1Byte(); //0 = path, 1 == constant

            TextureBindInfos.Add(new TextureBindInfo() { Slot = slot, Type = type, });
        }
    }

    private void Write(BinaryStream writer)
    {
        int Align(int pos, int alignment)
        {
            var amount = (-pos % alignment + alignment) % alignment;
            return pos + amount;
        }

        //string pool
        Dictionary<string, long> strings = new Dictionary<string, long>();

        var mem = new MemoryStream();
        using (var strWriter = new BinaryWriter(mem))
        {
            void AddString(string val)
            {
                if (strings.ContainsKey(val) || string.IsNullOrEmpty(val))
                    return;

                strings.Add(val, strWriter.BaseStream.Position);
                strWriter.Write(Encoding.UTF8.GetBytes(val));
                strWriter.Write((byte)0);
            }

            AddString(ShaderPath);
            for (int i = 0; i < TextureBindInfos.Count; i++)
            {
                if (TextureBindInfos[i].Type == 0)
                {
                    var texturePath = TexturePaths[TextureBindInfos[i].Slot];

                    AddString(texturePath.Name);
                    AddString(texturePath.Path);
                }
                else
                {
                    var textureConst = TextureConstants[TextureBindInfos[i].Slot];
                    AddString(textureConst.Name);
                }
            }
        }
        var stringPool = mem.ToArray();

        void WriteStringOffset(string val)
        {
            writer.Write(val != null && strings.ContainsKey(val) ? (uint)strings[val] : 0u);
        }

        writer.Write("MTL "u8);
        writer.WriteUInt32(HeaderFlags);
        writer.WriteUInt32(0); //size later
        writer.WriteUInt32(0);

        writer.WriteUInt16((ushort)TexturePaths.Count);
        writer.WriteByte(ParamFlag);
        writer.WriteByte(Unknown1);
        writer.WriteInt32(Align(ParamData.Length * 4 + TextureBindInfos.Count * 2, 16)); //Param section size
        writer.WriteUInt16((ushort)TextureConstants.Count);
        writer.WriteUInt16((ushort)(string.IsNullOrEmpty(ShaderPath) ? 0 : 1));
        writer.WriteUInt16(Unknown2);
        writer.WriteByte((byte)0);
        writer.WriteByte(Unknown3);
        writer.WriteUInt16((ushort)(ParamData.Length * 4));
        writer.WriteUInt16((ushort)TextureBindInfos.Count);

        WriteStringOffset(ShaderPath);

        for (int i = 0; i < TexturePaths.Count; i++)
        {
            WriteStringOffset(TexturePaths[i].Path);
            WriteStringOffset(TexturePaths[i].Name);
        }
        for (int i = 0; i < TextureConstants.Count; i++)
        {
            writer.Write((ushort)TextureConstants[i].Type);
            writer.Write(TextureConstants[i].Name != null &&
                strings.ContainsKey(TextureConstants[i].Name) ?
                    (ushort)strings[TextureConstants[i].Name] : (ushort)0);

            if (TextureConstants[i].Value is Half)
            {
                writer.WriteHalf((Half)TextureConstants[i].Value);
                writer.Align(4);
            }
            else if (TextureConstants[i].Value is float)
            {
                writer.WriteHalf((Half)(float)TextureConstants[i].Value);
                writer.Align(4);
            }
            else
            {
                writer.WriteByte(((Rgba)TextureConstants[i].Value).R);
                writer.WriteByte(((Rgba)TextureConstants[i].Value).G);
                writer.WriteByte(((Rgba)TextureConstants[i].Value).B);
                writer.WriteByte(((Rgba)TextureConstants[i].Value).A);
            }
        }
        writer.Align(0x10);
        writer.Write(ParamData);

        for (int i = 0; i < TextureBindInfos.Count; i++)
        {
            writer.WriteByte(TextureBindInfos[i].Slot);
            writer.WriteByte(TextureBindInfos[i].Type);
        }

        writer.Align(0x10);
        writer.Write(stringPool);

        writer.Align(0x10);

        writer.WriteSectionSizeU32(8, writer.BaseStream.Length - 32);
    }

    private string ReadString(BinaryStream reader, uint stringTablePos)
    {
        uint offset = reader.ReadUInt32();

        using (reader.TemporarySeek(stringTablePos + offset, SeekOrigin.Begin))
            return reader.ReadString(StringCoding.ZeroTerminated);
    }

    private string ReadStringUshort(BinaryStream reader, uint stringTablePos)
    {
        ushort offset = reader.ReadUInt16();

        using (reader.TemporarySeek(stringTablePos + offset, SeekOrigin.Begin))
            return reader.ReadString(StringCoding.ZeroTerminated);
    }

    public class TexturePath
    {
        public string Path;
        public string Name;
    }

    public class TextureConstant
    {
        public ConstantType Type;
        public object Value;
        public string Name;
    }

    public class Rgba
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public Rgba() { }

        public Rgba(byte[] rgba)
        {
            R = rgba[0];
            G = rgba[1];
            B = rgba[2];
            A = rgba[3];
        }
    }

    public class TextureBindInfo
    {
        public byte Slot;
        public byte Type;
    }

    public enum ConstantType
    {
        Rgba = 0,
        HalfFloat = 1,
    }
}
