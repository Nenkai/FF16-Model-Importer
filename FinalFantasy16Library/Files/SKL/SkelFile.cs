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

namespace CafeLibrary.ff16
{
    public class SkelFile
    {
        public hkaSkeleton m_Skeleton;

        public SkelFile(Stream stream)
        {
            HavokBinarySerializer _serializer = new();
            var root = (hkRootLevelContainer)_serializer.Read(stream);
            var container = (hkaAnimationContainer)root.m_namedVariants[0].m_variant;
            m_Skeleton = container.m_skeletons[0];
        }
    }
}
