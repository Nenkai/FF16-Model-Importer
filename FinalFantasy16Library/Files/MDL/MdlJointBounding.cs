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
public class MdlJointBounding
{
    public Vector3 BoundingMin;
    public Vector3 BoundingMax;
    public float Unknown1;
    public float Unknown2;
}