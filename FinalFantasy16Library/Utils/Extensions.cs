using Syroot.BinaryData.Memory;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace FinalFantasy16Library.Utils;

public static class Extensions
{
    public static Half ReadHalf(this ref SpanReader sr)
    {
        byte[] bytes = sr.ReadBytes(2);
        return sr.Endian == Syroot.BinaryData.Core.Endian.Little ? BinaryPrimitives.ReadHalfLittleEndian(bytes) : BinaryPrimitives.ReadHalfBigEndian(bytes);
    }
}
