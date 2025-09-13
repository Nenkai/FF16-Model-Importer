using HKLib.hk2018;
using HKLib.Serialization.hk2018.Binary;

namespace FinalFantasy16Library.Files.ANMB;

public class AnmbFile
{   
    public hkaAnimation m_Animation;
    public hkaAnimationBinding m_Binding;

    private AnmbFile() { }

    public static AnmbFile Open(byte[] file)
    {
        return Open(new MemoryStream(file));
    }

    public static AnmbFile Open(Stream stream)
    {
        HavokBinarySerializer _serializer = new();
        var root = (hkRootLevelContainer)_serializer.Read(stream);
        var container = (hkaAnimationContainer)root.m_namedVariants[0].m_variant;

        return new AnmbFile()
        {
            m_Animation = container.m_animations[0],
            m_Binding = container.m_bindings[0]
        };
    }
}
