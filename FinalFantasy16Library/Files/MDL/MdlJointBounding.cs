using System.Numerics;
using System.Runtime.InteropServices;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class MdlJointBounding
{
    public Vector3 BoundingMin;
    public Vector3 BoundingMax;
    public float Unknown1;
    public float Unknown2;
}