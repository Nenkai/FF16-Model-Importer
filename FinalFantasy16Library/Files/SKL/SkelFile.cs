using AvaloniaToolbox.Core.IO;

using HKLib.hk2018;
using HKLib.Serialization.hk2018.Binary;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FinalFantasy16Library.Files.SKL;

public class SkelFile
{
    public hkaSkeleton m_Skeleton;

    private SkelFile() { }

    public static SkelFile Open(byte[] file)
    {
        return Open(new MemoryStream(file));
    }

    public static SkelFile Open(Stream stream)
    {
        HavokBinarySerializer _serializer = new();
        var root = (hkRootLevelContainer)_serializer.Read(stream);
        var container = (hkaAnimationContainer)root.m_namedVariants[0].m_variant;

        return new SkelFile()
        {
            m_Skeleton = container.m_skeletons[0]
        };
    }
}
