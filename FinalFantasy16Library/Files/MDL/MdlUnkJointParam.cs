using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using static FinalFantasy16Library.Files.MDL.MdlFile;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class MdlUnkJointParam
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
    public float[] a;

    public ushort b;
    public ushort c;
}