using System.Runtime.InteropServices;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class MdlVFXEntry
{
    public uint NameOffset;
    public uint UnkIndex;
    public uint UnkCount;
    public uint Padding0x0C;
}