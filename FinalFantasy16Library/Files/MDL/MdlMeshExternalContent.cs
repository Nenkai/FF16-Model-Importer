using System.Runtime.InteropServices;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class MeshSpecsHeader
{
    public uint ModelExternalContentSize;
    public ushort SubmeshCount;
    public ushort OptionCount;
    public ushort DrawPartCount;
    public ushort MaterialCount;
    public ushort JointCount;
    public ushort Count4;
    public ushort Entries6Count;
    public ushort LODModelCount;
    public byte Unknown3a;
    public byte FaceJointCount;

    public ushort MuscleJointCount;

    public byte UnkJointParamCount;
    public byte AdditionalPartCount;
    public byte Unknown6;
    public byte VFXEntryCount;

    public uint ExtraSectionSize;
    public uint FlexVertexCount;
    public uint StringTableSize; //string pool size at the end 
    public uint FormatFlags;
    public uint Unknown10;

    public float Unknown11;
    public float Unknown12;

    public uint UnknownBuffer1DecompressedSize;
    public uint UnknownBuffer2DecompressedSize;
}
