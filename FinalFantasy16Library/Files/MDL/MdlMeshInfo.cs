using System.Runtime.InteropServices;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class MdlMeshInfo
{
    public uint FaceIndexCount;
    public uint FaceIndicesOffset;
    public ushort VertexCount;
    public ushort MaterialID;
    public ushort DrawPartStartIndex;
    public ushort DrawPartCount; //sub draw call that ranges 0 -> 3. Not always used, but reduces draw usage after first level
    public ushort FlexVertexInfoID;
    public MdlMeshBoneSetFlags BoneSetFlag;
    public MdlMeshTexCoordFlags TexCoordSetFlag;
    public uint Flag2;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public uint[] Unknowns2 = new uint[6];
    public uint Unknown3;
    public byte Unknown4;
    public byte Unknown5;
    public byte Unknown6;
    public byte UsedBufferCount = 2;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public uint[] BufferOffsets = new uint[8];

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] Strides = new byte[8];
}

[Flags]
public enum MdlMeshTexCoordFlags : byte
{
    USE_UV0 = 1 << 0,
    USE_UV1 = 1 << 1,
    USE_UV2 = 1 << 2,
    USE_UV3 = 1 << 3,
}

[Flags]
public enum MdlMeshBoneSetFlags : byte
{
    USE_BONESET1 = 1 << 0,
    USE_BONESET0 = 1 << 1,
}