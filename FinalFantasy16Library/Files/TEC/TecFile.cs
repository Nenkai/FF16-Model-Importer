using System.Diagnostics;
using System.Runtime.InteropServices;

using Syroot.BinaryData;

using FinalFantasy16Library.IO;
using FinalFantasy16Library.Utils;

namespace FinalFantasy16Library.Files.TEC;

public class TecFile
{
    private TecHeader Header;
    private TecExtraHeader ExtraHeader;

    public List<UnknownSection1> Unknown1 = new List<UnknownSection1>();

    public List<ShaderProgram> ShaderPrograms = new List<ShaderProgram>();

    public List<Shader> Shaders = new List<Shader>();

    public uint[] IndexTable = new uint[0];

    public List<string> Samplers = new List<string>();
    public List<uint> SamplerFlags = new List<uint>();

    private const uint HeaderStart = 112;

    public TecFile(Stream stream)
    {
        Read(new BinaryStream(stream));
    }

    private void Read(BinaryStream reader)
    {
        Header = reader.ReadStruct<TecHeader>();
        long pos = reader.Position; //start of section where offsets are relative (always 112)

        ExtraHeader = reader.ReadStruct<TecExtraHeader>();

        reader.Position = HeaderStart + Header.Unknown1Offset; //to 64 byte struct
        Unknown1 = reader.ReadMultipleStructs<UnknownSection1>(Header.Unknown1Count);

        reader.Position = HeaderStart + Header.ShaderProgramOffset; //to program list that has index, count for shaders to use
        ShaderPrograms = reader.ReadMultipleStructs<ShaderProgram>(Header.ShaderProgramCount);

        reader.Position = HeaderStart + Header.IndexListOffset; //index to map shaders to the program
        IndexTable = reader.ReadUInt32s((int)Header.IndexListCount);

        reader.Position = HeaderStart + Header.ShaderOffset; //Shader list
        Shaders = ReadShaders(reader);

        reader.Position = HeaderStart + Header.SamlerNameOffset; //Sampler names
        Samplers = ReadStrings(reader, (int)Header.SamplerCount);

        reader.Position = HeaderStart + Header.SamlerConfigOffset; //Sampler flags
        SamplerFlags = reader.ReadUInt32s((int)Header.SamplerCount).ToList();

        int idx = 0;
        foreach (var prog in ShaderPrograms)
        {
            for (int i = 0; i < prog.Count; i++)
            {
                var shaderIdx = IndexTable[prog.Index + i];
                var shader = Shaders[(int)shaderIdx];

                if (i == 0)
                {
                    File.WriteAllBytes("shader.vert.bin", shader.Data);
                }
                if (i == 1)
                {
                    File.WriteAllBytes("shader.frag.bin", shader.Data);
                }
            }
            if (idx > 2)
                break;

            idx++;
        }
    }

    private List<Shader> ReadShaders(BinaryStream reader)
    {
        List<Shader> shaders = new List<Shader>();
        for (int i = 0; i < Header.ShaderCount; i++)
        {
            Shader shader = new Shader();
            uint dataOffset = reader.ReadUInt32();
            uint dataSize = reader.ReadUInt32();
            uint shaderDefineIndex = reader.ReadUInt32();
            shader.StageType = reader.Read1Byte(); //0 1 or 2
            shader.Unknown2 = reader.Read1Byte(); //5
            Debug.Assert(reader.ReadUInt16() == 0); //padding
            shaders.Add(shader);

            if (dataSize > 0)
            {
                using (reader.TemporarySeek())
                {
                    shader.Info = ReadShaderDefine(reader, shader, shaderDefineIndex);
                }
                using (reader.TemporarySeek(HeaderStart + Header.ShaderDataOffset + dataOffset, SeekOrigin.Begin))
                {
                    shader.Data = reader.ReadBytes((int)dataSize);
                }
            }
        }
        return shaders;
    }

    private ShaderInfo ReadShaderDefine(BinaryStream reader, Shader shader, uint index)
    {
        ShaderInfo info = new ShaderInfo();

        reader.Position = HeaderStart + Header.ShaderDefineOffset + index * 4;
        ushort shaderInfoIndex = reader.ReadUInt16();
        ushort symbolIndex = reader.ReadUInt16();

        //Shader symbol info
        reader.Position = HeaderStart + Header.ShaderInfoOffset + shaderInfoIndex * 56;
        info.Header = reader.ReadStruct<ShaderInfoHeader>();

        reader.Position = HeaderStart + Header.ShaderSymbolsOffset + symbolIndex * 4;
        info.UniformBlocks = ReadSymbols(reader, info.Header.BlockCount);
        info.Uniforms = ReadSymbols(reader, info.Header.UniformCount);
        info.Samplers = ReadSymbols(reader, info.Header.SamplerCount);

        return info;
    }

    private List<Symbol> ReadSymbols(BinaryStream reader, int count)
    {
        List<Symbol> symbols = new List<Symbol>();
        for (int i = 0; i < count; i++)
        {
            Symbol symbol = new Symbol();
            symbol.Index = reader.Read1Byte();
            symbol.Kind = reader.Read1Byte();
            ushort nameOffset = reader.ReadUInt16();
            symbols.Add(symbol);

            using (reader.TemporarySeek(HeaderStart + Header.StringTableOffset + nameOffset, SeekOrigin.Begin))
            {
                symbol.Name = reader.ReadString(StringCoding.ZeroTerminated);
            }
        }
        return symbols;
    }

    private List<string> ReadStrings(BinaryStream reader, int count)
    {
        ushort[] offsets = reader.ReadUInt16s(count);

        List<string> strings = new List<string>();
        for (int i = 0; i < offsets.Length; i++)
        {
            reader.Position = HeaderStart + Header.StringTableOffset + offsets[i];
            strings.Add(reader.ReadString(StringCoding.ZeroTerminated));
        }
        return strings;
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class TecHeader
    {
        public Magic Magic = "TEC ";
        public uint Flags;
        public uint Size;
        public uint Padding;

        public uint Unknown1Offset;
        public uint Unknown1Count;

        public uint ShaderProgramOffset;
        public uint ShaderProgramCount;

        public uint ShaderEmptyOffset; //to empty 8 byte structure. Reserved for runtime pointers?
        public uint ShaderEmptyCount;

        public uint IndexListOffset;
        public uint IndexListCount;

        public uint ShaderOffset;
        public uint ShaderCount;

        public uint ShaderDefineOffset;
        public uint ShaderDefineCount;

        public uint ShaderDataOffset;
        public uint ShaderDataSize; //total shader data size

        public uint StringTableOffset;
        public uint StringTableSize;

        public uint ShaderInfoOffset; //has symbol info on shader
        public uint ShaderSymbolsOffset; //symbols used by shader info

        public uint SamplerCount;
        public uint SamlerNameOffset;
        public uint SamlerConfigOffset;
        public uint UnkOffset; //same as shader def offset

        public uint Padding1;
        public uint Padding2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class TecExtraHeader
    {
        public uint Unknown1; //0
        public uint FlagCount; //flag list in indices section.
        public uint Unknown2; //0
        public uint Unknown3; //0
        public uint Unknown4; //0
        public uint Unknown1Count; //Unknown1Count from tex header
        public uint Unknown5; //0
        public uint ShaderProgramCount; //0
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class UnknownSection1
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Unknown;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class ShaderProgram
    {
        public uint Index; //To index list
        public uint Count; //Of indices in index list
    }

    public class Shader
    {
        public byte StageType; //or stage type
        public byte Unknown2; //Binary type? Always DXIL / 2

        public byte[] Data;

        public ShaderInfo Info;
    }

    public class Symbol
    {
        public byte Index;
        public byte Kind;
        public string Name;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class ShaderInfoHeader
    {
        public byte Unknown1;
        public byte Unknown2;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Unknown3;

        public byte Unknown4;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
        public byte[] Unknown5;

        public byte Unknown6;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7 + 24)]
        public byte[] Unknown7;

        public byte BlockCount;
        public byte UniformCount;
        public byte SamplerCount;
        public byte Unknown8;

        public uint Unknown9; //padding?
    }

    public class ShaderInfo
    {
        public List<Symbol> UniformBlocks = new List<Symbol>();
        public List<Symbol> Uniforms = new List<Symbol>();
        public List<Symbol> Samplers = new List<Symbol>();

        public ShaderInfoHeader Header;
    }
}
