using System.Runtime.InteropServices;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class MdlUnkJointParam
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
    public float[] a;

    public ushort b;
    public ushort c;
}