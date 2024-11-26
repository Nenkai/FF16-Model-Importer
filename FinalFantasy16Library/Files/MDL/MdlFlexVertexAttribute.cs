using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinalFantasy16Library.Files.MDL;

public struct MdlFlexVertexAttribute
{
    public byte BufferIdx;
    public byte Offset;
    public EncodingFormat Format;
    public MdlVertexSemantic Type;

    public MdlFlexVertexAttribute() { }

    public MdlFlexVertexAttribute(byte buffer, byte offset, MdlVertexSemantic type, EncodingFormat format)
    {
        BufferIdx = buffer;
        Offset = offset;
        Format = format;
        Type = type;
    }

    public readonly int Size
    {
        get
        {
            switch (Format)
            {
                case EncodingFormat.ENCODING_UINT8x4:
                case EncodingFormat.ENCODING_UNORM8x4:
                case EncodingFormat.ENCODING_HALFFLOATx2:
                    return 4;
                case EncodingFormat.ENCODING_FLOATx4:
                    return 16;
                case EncodingFormat.ENCODING_FLOATx3:
                    return 12;
                case EncodingFormat.ENCODING_HALFFLOATx4:
                case EncodingFormat.ENCODING_FLOATx2:
                    return 8;
                default:
                    throw new Exception($"{Format} not supported!");
            }
        }
    }

    public override readonly string ToString() => $"{Type}_{Format}_{Offset}_{BufferIdx}";
}

public enum EncodingFormat : byte
{
    ENCODING_SNORM16x2 = 18,   // = 37 = DXGI_FORMAT_R16G16_SNORM
    ENCODING_SNORM16x4 = 20,   // = 13 = DXGI_FORMAT_R16G16B16A16_SNORM
    ENCODING_FLOAT = 33,       // = 41 = DXGI_FORMAT_R32_FLOAT
    ENCODING_FLOATx2 = 34,     // = 16 = DXGI_FORMAT_R32G32_FLOAT
    ENCODING_FLOATx3 = 35,     // = 6  = DXGI_FORMAT_R32G32B32A32_FLOAT
    ENCODING_FLOATx4 = 36,     // = 2  = DXGI_FORMAT_R32G32B32A32_FLOAT
    ENCODING_HALFFLOATx2 = 50, // = 34 = DXGI_FORMAT_R16G16_FLOAT
    ENCODING_HALFFLOATx4 = 52, // = 10 = DXGI_FORMAT_R16G16B16A16_FLOAT
    ENCODING_UNORM8x4 = 68,    // = 28 = DXGI_FORMAT_R8G8_B8G8_UNORM
    ENCODING_SINT16x2 = 82,    // = 38 = DXGI_FORMAT_R16G16_SINT
    ENCODING_SINT16x4 = 84,    // = 14 = DXGI_FORMAT_R16G16B16A16_SINT
    ENCODING_UINT8x4 = 116,    // = 30 = DXGI_FORMAT_R8G8B8A8_UINT
    ENCODING_UINT16x2 = 130,   // = 36 = DXGI_FORMAT_R16G16_UINT
    ENCODING_UINT16x4 = 132,   // = 12 = DXGI_FORMAT_R16G16B16A16_UINT
    ENCODING_SINT32 = 145,     // = 43 = DXGI_FORMAT_R32_SINT 
    ENCODING_SINT32x2 = 146,   // = 18 = DXGI_FORMAT_R32G32_SINT
    ENCODING_SINT32x3 = 147,   // = 8  = DXGI_FORMAT_R32G32B32_SINT
    ENCODING_SINT32x4 = 148,   // = 4  = DXGI_FORMAT_R32G32B32A32_SINT  
    ENCODING_UINT32 = 161,     // = 42 = DXGI_FORMAT_R32_UINT
    ENCODING_UINT32x2 = 162,   // = 17 = DXGI_FORMAT_R32G32_UINT
    ENCODING_UINT32x3 = 163,   // = 7  = DXGI_FORMAT_R32G32B32_UINT
    ENCODING_UINT32x4 = 163,   // = 3  = DXGI_FORMAT_R32G32B32A32_UINT
    ENCODING_UNORM16x2 = 178,  // = 35 = DXGI_FORMAT_R16G16_UNORM
    ENCODING_UNORM16x4 = 180,  // = 11 = DXGI_FORMAT_R16G16B16A16_UNORM
}

public enum MdlVertexSemantic : byte
{
    Position = 0,
    BoneWeights0 = 1,
    BoneIndices0 = 2,
    Color0 = 3,

    Color5 = 8,
    Color6 = 9,

    TexCoord0 = 11,
    TexCoord1 = 12,
    Normals = 21,
    Tangents = 22,
    Binormal = 23,

    TexCoord13 = 24,

    BoneWeights1 = 28,
    BoneIndices1 = 29,
}
