using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using static FinalFantasy16Library.Files.MDL.MdlFile;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class MdlLODModelInfo
{
    public ushort MeshIndex;
    public ushort MeshCount;
    public uint Unknown0;
    public uint Unknown1;
    public uint TriCount;
    public uint vBufferOffset;
    public uint idxBufferOffset;
    public uint DecompVertexBuffSize;
    public uint DecompIdxBuffSize;
    public uint DecompIdxBuffSizeMultiplied6;
    public uint VertexCount;

    public uint Unknown3;
    public uint Unknown4;
    public uint Unknown5;
    public uint Unknown6;
    public uint Unknown7;
    public uint Unknown8;
}
