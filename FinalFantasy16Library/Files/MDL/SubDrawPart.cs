using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using static FinalFantasy16Library.Files.MDL.MdlFile;

namespace FinalFantasy16Library.Files.MDL;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class SubDrawPart
{
    public uint IndexStart;
    public uint IndexCount;
    public uint Unknown; //type? 0, 1, 2
    public uint Unknown2; //0

    public uint Unknown3; //0
    public uint Unknown4; //0
    public uint Unknown5; //0
    public uint Unknown6; //0
}