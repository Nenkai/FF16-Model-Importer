using System.Numerics;

namespace FinalFantasy16Library.Files.MDL.Convert;

public class GeneratedJointData
{
    public string Name { get; set; }
    public int Index { get; set; }
    public JointEntry Joint { get; set; }
    public MdlJointBounding Bounding { get; set; }

    public GeneratedJointData(string name, int index, JointEntry joint, MdlJointBounding bounding)
    {
        Name = name;
        Index = index;
        Joint = joint;
        Bounding = bounding;
    }
}