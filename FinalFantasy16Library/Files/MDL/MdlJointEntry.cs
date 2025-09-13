using System.Numerics;
using System.Runtime.InteropServices;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class JointEntry
{
    public uint NameOffset;
    public Vector3 WorldPosition;
}