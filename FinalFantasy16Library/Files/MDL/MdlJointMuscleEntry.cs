using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using static FinalFantasy16Library.Files.MDL.MdlFile;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class MdlJointMuscleEntry
{
    public uint NameOffset;
    public float Unknown1;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public ushort[] IndicesSet1;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public ushort[] IndicesSet2;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WeightsSet1;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WeightsSet2;

    public Vector3 Unknown2;
    public Vector3 Unknown3;
}
