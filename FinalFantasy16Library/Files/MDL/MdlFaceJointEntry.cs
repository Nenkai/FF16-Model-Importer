using System.Runtime.InteropServices;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class MdlFaceJointEntry
{
    public uint Offset;
    public uint Padding;
}