using HKLib.hk2018;
using HKLib.Serialization.hk2018.Binary;

namespace FinalFantasy16Library.Files.SKL;

public class SklFile
{
    public hkaSkeleton m_Skeleton;

    private SklFile() { }

    public static SklFile Open(byte[] file)
    {
        return Open(new MemoryStream(file));
    }

    public static SklFile Open(Stream stream)
    {
        HavokBinarySerializer _serializer = new();
        var root = (hkRootLevelContainer)_serializer.Read(stream);
        var container = (hkaAnimationContainer)root.m_namedVariants[0].m_variant;

        return new SklFile()
        {
            m_Skeleton = container.m_skeletons[0]
        };
    }
}
