using AvaloniaToolbox.Core.IO;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FinalFantasy16Library.Files.MTL
{
    public class MtlFile
    {
        public uint HeaderFlags;

        public string ShaderPath;

        public List<TexturePath> TexturePaths = new List<TexturePath>();
        public List<TextureConstant> TextureConstants = new List<TextureConstant>();
        public List<TextureBindInfo> TextureBindInfos = new List<TextureBindInfo>();

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
            Read(new FileReader(stream));
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
            using (var writer = new FileWriter(stream))
                Write(writer);
        }

        private void Read(FileReader reader)
        {
            reader.ReadSignature("MTL ");
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
            ParamFlag = reader.ReadByte(); //editing this affects params some way
            Unknown1 = reader.ReadByte();
            uint dataSectionSize = reader.ReadUInt32();
            ushort numConstantTextures = reader.ReadUInt16();
            ushort numExternalShaders = reader.ReadUInt16();
            Unknown2 = reader.ReadUInt16();
            byte padding = reader.ReadByte();
            Unknown3 = reader.ReadByte();
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
                byte slot = reader.ReadByte();
                byte type = reader.ReadByte(); //0 = path, 1 == constant

                TextureBindInfos.Add(new TextureBindInfo() { Slot = slot, Type = type, });
            }
        }

        private void Write(FileWriter writer)
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

            writer.WriteSignature("MTL ");
            writer.Write(HeaderFlags);
            writer.Write(0); //size later
            writer.Write(0);

            writer.Write((ushort)TexturePaths.Count);
            writer.Write(ParamFlag);
            writer.Write(Unknown1);
            writer.Write(Align(ParamData.Length * 4 + TextureBindInfos.Count * 2, 16)); //Param section size
            writer.Write((ushort)TextureConstants.Count);
            writer.Write((ushort)(string.IsNullOrEmpty(ShaderPath) ? 0 : 1));
            writer.Write(Unknown2);
            writer.Write((byte)0);
            writer.Write(Unknown3);
            writer.Write((ushort)(ParamData.Length * 4));
            writer.Write((ushort)TextureBindInfos.Count);

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
                    writer.Write((Half)TextureConstants[i].Value);
                    writer.Align(4);
                }
                else if (TextureConstants[i].Value is float)
                {
                    writer.Write((Half)(float)TextureConstants[i].Value);
                    writer.Align(4);
                }
                else
                {
                    writer.Write(((Rgba)TextureConstants[i].Value).R);
                    writer.Write(((Rgba)TextureConstants[i].Value).G);
                    writer.Write(((Rgba)TextureConstants[i].Value).B);
                    writer.Write(((Rgba)TextureConstants[i].Value).A);
                }
            }
            writer.AlignBytes(16);
            writer.Write(ParamData);

            for (int i = 0; i < TextureBindInfos.Count; i++)
            {
                writer.Write(TextureBindInfos[i].Slot);
                writer.Write(TextureBindInfos[i].Type);
            }

            writer.AlignBytes(16);
            writer.Write(stringPool);

            writer.AlignBytes(16);

            writer.WriteSectionSizeU32(8, writer.BaseStream.Length - 32);
        }

        private string ReadString(FileReader reader, uint stringTablePos)
        {
            uint offset = reader.ReadUInt32();

            using (reader.TemporarySeek(stringTablePos + offset, SeekOrigin.Begin))
            {
                return reader.ReadStringZeroTerminated();
            }
        }

        private string ReadStringUshort(FileReader reader, uint stringTablePos)
        {
            ushort offset = reader.ReadUInt16();

            using (reader.TemporarySeek(stringTablePos + offset, SeekOrigin.Begin))
            {
                return reader.ReadStringZeroTerminated();
            }
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
}
