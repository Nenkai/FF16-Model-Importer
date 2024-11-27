using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Runtime.InteropServices;
using System.Text;
using Toolbox.Core;
using CommunityToolkit.HighPerformance.Buffers;
using System.Numerics;
using AvaloniaToolbox.Core.IO;

using FinalFantasy16Library.Files.MDL.Helpers;
using FinalFantasy16Library.Files.MDL.Convert;

namespace FinalFantasy16Library.Files.MDL;

public class MdlFile
{
    public uint MainHeaderSize => Version >= 20 ? 0xA8u : 0x98u;

    /// <summary>
    /// Version.
    /// </summary>
    public byte Version;

    public byte MainFlags;

    public byte ModelType;

    public byte UnkFlags_0x16;

    /// <summary>
    /// Attribute sets which determine what attributes to use from the attribute list.
    /// </summary>
    public List<MdlFlexVertexInfo> AttributeSets = [];

    /// <summary>
    /// Attribute that determine the layout of vertices in a vertex buffer.
    /// </summary>
    public List<MdlFlexVertexAttribute> Attributes = [];

    /// <summary>
    /// The min and max bounding box of the entire model.
    /// </summary>
    public float[] BoundingBox = new float[8];

    /// <summary>
    /// Unknown values
    /// </summary>
    public byte[] UnknownEntries;

    /// <summary>
    /// A list of material files that are externally referenced. 
    /// </summary>
    public List<string> MaterialFileNames = [];

    /// <summary>
    /// A list of vertex buffers used to store compressed vertex buffer data.
    /// </summary>
    public ModelBuffer[] vBuffers;

    /// <summary>
    /// A list of index buffers used to store compressed index buffer data.
    /// </summary>
    public ModelBuffer[] idxBuffers;

    /// <summary>
    /// Buffer with an unknown purpose.
    /// </summary>
    public ModelBuffer UnknownBuffer1 = new ModelBuffer();

    /// <summary>
    /// Buffer with an unknown purpose.
    /// </summary>
    public ModelBuffer UnknownBuffer2 = new ModelBuffer();

    /// <summary>
    /// The model spec header
    /// </summary>
    public MeshSpecsHeader SpecsHeader;

    /// <summary>
    /// A list of level of detail meshes
    /// </summary>
    public List<MdlLODModelInfo> LODModels = [];

    /// <summary>
    /// A list of meshes used by the LODs.
    /// </summary>
    public List<MdlMeshInfo> MeshInfos = [];

    /// <summary>
    /// A list of sub draw calls for drawing less faces.
    /// </summary>
    public List<SubDrawPart> SubDrawCalls = [];

    /// <summary>
    /// A list of muscle joints for muscle calculations.
    /// </summary>
    public List<MdlJointMuscleEntry> JointMuscles = [];

    /// <summary>
    /// A list of joint faces with an unknown purpose.
    /// </summary>
    public List<MdlUnkJointParam> JointFacesEntries = [];

    /// <summary>
    /// A list of joints that have a joint name and position.
    /// </summary>
    public List<JointEntry> Joints = [];

    /// <summary>
    /// A list of bounding boxes used to attach to joints for culling.
    /// </summary>
    private List<MdlJointBounding> JointBoundings = [];

    public float[] JointMaxBounds = [float.MinValue, float.MinValue, float.MinValue,
                                     float.MaxValue, float.MaxValue, float.MaxValue];

    /// <summary>
    /// A list of joint names.
    /// These joints are all rigged and referenced to vertex data.
    /// </summary>
    public List<string> JointNames = [];

    /// <summary>
    /// A list of joint face names.
    /// </summary>
    public List<string> JointFaceNames = [];

    /// <summary>
    /// A list of joint muscle names.
    /// </summary>
    public List<string> JointMuscleNames = [];

    /// <summary>
    /// A list of material names. These always match the amount of material files.
    /// </summary>
    public List<string> MaterialNames = [];

    /// <summary>
    /// A list of parts with an unknown purpose.
    /// </summary>
    public List<string> Options = [];

    /// <summary>
    /// A list of parts with an unknown purpose.
    /// </summary>
    public List<string> AdditionalParts = [];

    /// <summary>
    /// A list of parts with an unknown purpose.
    /// </summary>
    public List<string> VFXEntries = [];

    //Extra section at the end with an unknown purpose
    private byte[] ExtraSection = [];

    //MCEX section used to store embedded data like collision.
    private byte[] McexSection = [];

    //Extra section at the end with an unknown purpose
    private byte[] ExtraSection2 = [];

    // Add field to store data for bones not in base MDL file
    private List<GeneratedJointData> _generatedJoints;

    // Add generated data to existing joint collections
    public void SetGeneratedJoints(IEnumerable<GeneratedJointData> joints)
    {
        _generatedJoints = new List<GeneratedJointData>(joints);

        foreach (var genJoint in _generatedJoints.OrderBy(j => j.Index))
        {
            Joints.Add(genJoint.Joint);
            JointBoundings.Add(genJoint.Bounding);
            JointNames.Add(genJoint.Name);
        }
    }



    public MdlFile(Stream stream)
    {
        Read(new FileReader(stream));
    }

    public void Save(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        Save(fs);
    }

    public void Save(Stream stream)
    {
        Write(new FileWriter(stream));
    }

    #region Reading
    private void Read(FileReader reader)
    {
        reader.ReadSignature("MDL ");
        Version = reader.ReadByte();

        if (Version != 28)
            Console.WriteLine($"WARN: Only MDL version 28 is supported. Version: {Version}");

        MainFlags = reader.ReadByte();
        if (MainFlags != 1)
            Console.WriteLine($"WARN: Model flag 1 is missing.");

        ModelType = reader.ReadByte();
        reader.ReadByte();

        uint section1Size = reader.ReadUInt32();
        uint section2Size = reader.ReadUInt32();
        ushort materialNamesCount = reader.ReadUInt16();
        ushort flexVertAttributeCount = reader.ReadUInt16();
        byte flexVertInfoCount = reader.ReadByte();
        byte lodCount = reader.ReadByte();
        UnkFlags_0x16 = reader.ReadByte();
        reader.ReadByte();
        uint[] vBuffersOffsets = reader.ReadUInt32s(8);
        uint[] idxBuffersOffsets = reader.ReadUInt32s(8);
        uint[] vBuffersSizes = reader.ReadUInt32s(8);
        uint[] idxBuffersSizes = reader.ReadUInt32s(8);
        uint unkBuffer1Offset = reader.ReadUInt32();
        uint unkBuffer1Size = reader.ReadUInt32();
        uint unkBuffer1Offset2 = reader.ReadUInt32();
        uint unkBuffer2Size = reader.ReadUInt32();

        AttributeSets = reader.ReadMultipleStructs<MdlFlexVertexInfo>(flexVertInfoCount);
        Attributes = reader.ReadMultipleStructs<MdlFlexVertexAttribute>(flexVertAttributeCount);
        BoundingBox = reader.ReadSingles(8);
        var namePointers = reader.ReadMultipleStructs<NamePointer>(materialNamesCount);

        if ((UnkFlags_0x16 & 2) != 0)
            UnknownEntries = reader.ReadBytes(0x10);

        long nameTableStart = reader.Position;
        for (int i = 0; i < materialNamesCount; i++)
        {
            reader.SeekBegin(nameTableStart + (int)namePointers[i].Offset);
            MaterialFileNames.Add(reader.ReadStringZeroTerminated());
        }

        reader.SeekBegin(MainHeaderSize + section1Size);
        ReadModelData(reader);

        vBuffers = ReadBuffers(reader, vBuffersOffsets, vBuffersSizes);
        idxBuffers = ReadBuffers(reader, idxBuffersOffsets, idxBuffersSizes);

        if (unkBuffer1Size > 0)
        {
            reader.SeekBegin(MainHeaderSize + unkBuffer1Offset);
            UnknownBuffer1 = new ModelBuffer()
            {
                Data = reader.ReadBytes((int)unkBuffer1Size),
            };
        }

        if (unkBuffer2Size > 0)
        {
            reader.SeekBegin(MainHeaderSize + unkBuffer1Offset2);
            UnknownBuffer2 = new ModelBuffer()
            {
                Data = reader.ReadBytes((int)unkBuffer2Size),
            };
        }
    }

    private ModelBuffer[] ReadBuffers(FileReader reader, uint[] offsets, uint[] sizes)
    {
        ModelBuffer[] buffers = new ModelBuffer[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            if (sizes[i] == 0)
                continue;

            reader.SeekBegin(offsets[i] + MainHeaderSize);
            buffers[i] = new ModelBuffer()
            {
                Data = reader.ReadBytes((int)sizes[i]),
            };
        }
        return buffers;
    }

    private void ReadModelData(FileReader reader)
    {
        SpecsHeader = reader.ReadStruct<MeshSpecsHeader>();
        LODModels = reader.ReadMultipleStructs<MdlLODModelInfo>(SpecsHeader.LODModelCount);
        MeshInfos = reader.ReadMultipleStructs<MdlMeshInfo>(SpecsHeader.SubmeshCount);
        SubDrawCalls = reader.ReadMultipleStructs<SubDrawPart>(SpecsHeader.DrawPartCount);
        Joints = reader.ReadMultipleStructs<JointEntry>(SpecsHeader.JointCount);
        var MaterialNamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.MaterialCount);
        var JointFaceNamePointers = reader.ReadMultipleStructs<MdlFaceJointEntry>(SpecsHeader.FaceJointCount);
        JointMuscles = reader.ReadMultipleStructs<MdlJointMuscleEntry>(SpecsHeader.MuscleJointCount);
        JointFacesEntries = reader.ReadMultipleStructs<MdlUnkJointParam>(SpecsHeader.UnkJointParamCount);
        var AdditionalPartNamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.AdditionalPartCount);
        var OptionNamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.OptionCount);
        var VFXEntryNamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.VFXEntryCount);

        ExtraSection = reader.ReadBytes((int)SpecsHeader.ExtraSectionSize); //40 bytes when used
        reader.Align(0x04);

        long mcexPosition = reader.Position;
        McexSection = reader.ReadBytes((int)SpecsHeader.ModelExternalContentSize);
        reader.Position = mcexPosition + (int)AlignValue(SpecsHeader.ModelExternalContentSize, 0x10);

        if (SpecsHeader.JointCount > 0)
            JointBoundings = reader.ReadMultipleStructs<MdlJointBounding>(SpecsHeader.JointCount);

        if (SpecsHeader.JointCount > 0)
        {
            long basePos = reader.Position;
            JointMaxBounds = reader.ReadSingles(6);

            reader.Position = basePos + AlignValue(sizeof(float) * 6, 0x10);
        }

        long strTableOffset = reader.Position;
        for (int i = 0; i < SpecsHeader.MaterialCount; i++)
        {
            reader.Position = strTableOffset + MaterialNamePointers[i].Offset;

            string str = reader.ReadStringZeroTerminated();
            if (!str.All(char.IsAscii))
                throw new Exception($"Material name '{str}' must be a valid ASCII with no special characters.");

            MaterialNames.Add(str);
        }

        for (int i = 0; i < SpecsHeader.JointCount; i++)
        {
            reader.Position = strTableOffset + Joints[i].NameOffset;

            string str = reader.ReadStringZeroTerminated();
            if (!str.All(char.IsAscii))
                throw new Exception($"Joint name '{str}' must be a valid ASCII with no special characters.");

            JointNames.Add(str);
        }

        for (int i = 0; i < SpecsHeader.FaceJointCount; i++)
        {
            reader.Position = strTableOffset + JointFaceNamePointers[i].Offset;

            string str = reader.ReadStringZeroTerminated();
            if (!str.All(char.IsAscii))
                throw new Exception($"Joint face name '{str}' must be a valid ASCII with no special characters.");

            JointFaceNames.Add(str);
        }

        for (int i = 0; i < SpecsHeader.MuscleJointCount; i++)
        {
            reader.Position = strTableOffset + JointMuscles[i].NameOffset;

            string str = reader.ReadStringZeroTerminated();
            if (!str.All(char.IsAscii))
                throw new Exception($"Joint muscle name '{str}' must be a valid ASCII with no special characters.");

            JointMuscleNames.Add(str);
        }

        for (int i = 0; i < SpecsHeader.AdditionalPartCount; i++)
        {
            reader.Position = strTableOffset + AdditionalPartNamePointers[i].Offset;

            string str = reader.ReadStringZeroTerminated();
            if (!str.All(char.IsAscii))
                throw new Exception($"Additional part '{str}' must be a valid ASCII with no special characters.");

            AdditionalParts.Add(str);
        }

        for (int i = 0; i < SpecsHeader.OptionCount; i++)
        {
            reader.Position = strTableOffset + OptionNamePointers[i].Offset;

            string str = reader.ReadStringZeroTerminated();
            if (!str.All(char.IsAscii))
                throw new Exception($"Option name '{str}' must be a valid ASCII with no special characters.");

            Options.Add(str);
        }

        for (int i = 0; i < SpecsHeader.VFXEntryCount; i++)
        {
            reader.Position = strTableOffset + VFXEntryNamePointers[i].Offset;

            string str = reader.ReadStringZeroTerminated();
            if (!str.All(char.IsAscii))
                throw new Exception($"VFX Entry '{str}' must be a valid ASCII with no special characters.");

            VFXEntries.Add(str);
        }

        reader.Position = strTableOffset + SpecsHeader.StringTableSize;
        reader.Align(0x10);

        if ((UnkFlags_0x16 & 1) != 0)
        {
            ExtraSection2 = reader.ReadBytes(0x18);
            reader.Align(0x10);
        }
    }

    #endregion

    #region Writing
    private long _ofsMeshInfoSavedPos; //for adjusting buffer offsets

    private void Write(FileWriter writer)
    {
        uint[] vBuffersSizes = new uint[8];
        uint[] idxBuffersSizes = new uint[8];

        for (int i = 0; i < vBuffers.Length; i++)
            if (vBuffers[i] != null)
                vBuffersSizes[i] = (uint)vBuffers[i].Data.Length;

        for (int i = 0; i < idxBuffers.Length; i++)
            if (idxBuffers[i] != null)
                idxBuffersSizes[i] = (uint)idxBuffers[i].Data.Length;

        writer.Write(Encoding.ASCII.GetBytes("MDL "));
        writer.Write(Version);
        writer.Write(MainFlags);
        writer.Write(ModelType);
        writer.Write(0); //mat size later
        writer.Write(0); //mesh spec size later
        writer.Write((ushort)MaterialFileNames.Count);
        writer.Write((ushort)Attributes.Count);
        writer.Write((byte)AttributeSets.Count);
        writer.Write((byte)LODModels.Count); //LOD count
        writer.Write(UnkFlags_0x16);

        //vertex buffer offsets
        long ofsVbufferPos = writer.Position;
        writer.Write(new uint[8]); //vertex buffer offsets saved later
        long ofsIbufferPos = writer.Position;
        writer.Write(new uint[8]); //index buffer offsets saved later
        writer.Write(vBuffersSizes);
        writer.Write(idxBuffersSizes);

        long ofsUnknownBuffers = writer.Position;

        writer.Write((uint)0); //unknown buffer 1 offset
        writer.Write((uint)UnknownBuffer1.Data.Length);
        writer.Write((uint)0); //unknown buffer 2 offset
        writer.Write((uint)UnknownBuffer2.Data.Length);

        long start_section1 = writer.Position;

        writer.WriteMultiStruct(AttributeSets);
        writer.WriteMultiStruct(Attributes);
        writer.Write(BoundingBox);

        uint nameOfs = 0;
        WriteNameStructs(writer, MaterialFileNames, ref nameOfs);

        if ((UnkFlags_0x16 & 2) != 0)
            writer.Write(UnknownEntries);

        writer.WriteStrings(MaterialFileNames);
        writer.Align(0x10);

        //size
        writer.WriteSectionSizeU32(8, writer.Position - start_section1);

        long start_mesh_section = writer.Position;

        WriteMeshData(writer);

        writer.Align(16);
        //size
        writer.WriteSectionSizeU32(12, writer.Position - start_mesh_section);

        //buffer data last
        for (int i = 0; i < vBuffers.Length; i++)
        {
            if (vBuffers[i] == null)
                continue;

            //mdl header offset
            writer.WriteUint32Offset(ofsVbufferPos + i * 4, start_section1);
            //mesh header offset
            writer.WriteUint32Offset(_ofsMeshInfoSavedPos + i * 64 + 16, start_section1);
            writer.Write(vBuffers[i].Data.Span);

            //mdl header offset
            writer.WriteUint32Offset(ofsIbufferPos + i * 4, start_section1);
            //mesh header offset
            writer.WriteUint32Offset(_ofsMeshInfoSavedPos + i * 64 + 20, start_section1);
            writer.Write(idxBuffers[i].Data.Span);
        }

        //2 unknown buffers
        writer.WriteUint32Offset(ofsUnknownBuffers, start_section1);
        writer.Write(UnknownBuffer1.Data.Span);

        writer.WriteUint32Offset(ofsUnknownBuffers + 8, start_section1);
        writer.Write(UnknownBuffer2.Data.Span);
    }

    private void WriteMeshData(FileWriter writer)
    {
        //Prepare spec header
        SpecsHeader.LODModelCount = (ushort)LODModels.Count;
        SpecsHeader.SubmeshCount = (ushort)MeshInfos.Count;
        SpecsHeader.JointCount = (uint)Joints.Count;
        SpecsHeader.MuscleJointCount = (ushort)JointMuscles.Count;
        SpecsHeader.FaceJointCount = (byte)JointFaceNames.Count;
        SpecsHeader.DrawPartCount = (ushort)SubDrawCalls.Count;
        SpecsHeader.UnkJointParamCount = (byte)JointFacesEntries.Count;
        SpecsHeader.MaterialCount = (byte)MaterialNames.Count;
        SpecsHeader.FlexVertexCount = (byte)Attributes.Count;
        SpecsHeader.OptionCount = (byte)Options.Count;
        SpecsHeader.AdditionalPartCount = (byte)AdditionalParts.Count;
        SpecsHeader.VFXEntryCount = (byte)VFXEntries.Count;

        SpecsHeader.StringTableSize = (uint)MaterialNames.Sum(x => Encoding.ASCII.GetByteCount(x) + 1) +
                                       (uint)JointNames.Sum(x => Encoding.ASCII.GetByteCount(x) + 1) +
                                       (uint)JointFaceNames.Sum(x => Encoding.ASCII.GetByteCount(x) + 1) +
                                       (uint)JointMuscleNames.Sum(x => Encoding.ASCII.GetByteCount(x) + 1) +
                                       (uint)Options.Sum(x => Encoding.ASCII.GetByteCount(x) + 1) +
                                       (uint)AdditionalParts.Sum(x => Encoding.ASCII.GetByteCount(x) + 1) +
                                       (uint)VFXEntries.Sum(x => Encoding.ASCII.GetByteCount(x) + 1);

        uint nameOfs = 0;

        //name offset setup for joints (after material names)
        uint jointNameOfs = (uint)MaterialNames.Sum(x => x.Length + 1);
        for (int i = 0; i < Joints.Count; i++)
        {
            Joints[i].NameOffset = jointNameOfs;
            jointNameOfs += (uint)JointNames[i].Length + 1;
        }
        //Joint face names
        ulong[] jointFaceNameOffsets = new ulong[JointFaceNames.Count];
        for (int i = 0; i < JointFaceNames.Count; i++)
        {
            jointFaceNameOffsets[i] = jointNameOfs;
            jointNameOfs += (uint)JointFaceNames[i].Length + 1;
        }
        //Joint muscle names
        for (int i = 0; i < JointMuscles.Count; i++)
        {
            JointMuscles[i].NameOffset = jointNameOfs;
            jointNameOfs += (uint)JointMuscleNames[i].Length + 1;
        }

        foreach (MdlLODModelInfo mesh in LODModels)
        {
            //get sub meshes
            List<MdlMeshInfo> subMeshes = [];
            for (int i = 0; i < mesh.MeshCount; i++)
                subMeshes.Add(MeshInfos[mesh.MeshIndex + i]);

            //Auto set vertex and index counters
            mesh.VertexCount = (uint)subMeshes.Sum(x => x.VertexCount);
            mesh.TriCount = (uint)subMeshes.Sum(x => x.FaceIndexCount) / 3;
        }

        writer.WriteStruct(SpecsHeader);

        _ofsMeshInfoSavedPos = writer.Position;
        writer.WriteMultiStruct(LODModels);
        writer.WriteMultiStruct(MeshInfos);
        writer.WriteMultiStruct(SubDrawCalls);
        writer.WriteMultiStruct(Joints);
        WriteNameStructs(writer, MaterialNames, ref nameOfs);

        writer.Write(jointFaceNameOffsets);
        writer.WriteMultiStruct(JointMuscles);
        writer.WriteMultiStruct(JointFacesEntries);

        nameOfs += (uint)JointNames.Sum(x => Encoding.ASCII.GetByteCount(x) + 1);
        nameOfs += (uint)JointFaceNames.Sum(x => Encoding.ASCII.GetByteCount(x) + 1);
        nameOfs += (uint)JointMuscleNames.Sum(x => Encoding.ASCII.GetByteCount(x) + 1);

        WriteNameStructs(writer, AdditionalParts, ref nameOfs);
        WriteNameStructs(writer, Options, ref nameOfs);
        WriteNameStructs(writer, VFXEntries, ref nameOfs);

        writer.Write(ExtraSection);
        writer.Align(0x04);

        long mcexPosition = writer.Position;
        writer.Write(McexSection);
        writer.Position = mcexPosition + (int)AlignValue((uint)McexSection.Length, 0x10);

        if (SpecsHeader.JointCount > 0)
            writer.WriteMultiStruct(JointBoundings);

        if (SpecsHeader.JointCount > 0)
        {
            long basePos = writer.Position;
            writer.Write(JointMaxBounds);
            writer.Position = basePos + AlignValue(sizeof(float) * 6, 0x10);
        }

        // TODO: String table
        writer.WriteStrings(MaterialNames);
        writer.WriteStrings(JointNames);
        writer.WriteStrings(JointFaceNames);
        writer.WriteStrings(JointMuscleNames);
        writer.WriteStrings(AdditionalParts);
        writer.WriteStrings(Options);
        writer.WriteStrings(VFXEntries);
        writer.Align(0x10);

        if ((UnkFlags_0x16 & 1) != 0)
        {
            writer.Write(ExtraSection2);
            writer.Align(0x10);
        }
    }

    private void WriteNameStructs(FileWriter writer, List<string> strings, ref uint offsetStart)
    {
        foreach (var name in strings)
        {
            writer.Write((ulong)offsetStart);
            writer.Write((ulong)0);
            offsetStart += (uint)name.Length + 1;
        }
    }
    #endregion

    public struct NamePointer
    {
        public uint Offset;
        public uint Padding;
        public ulong Padding2;
    }

    private static uint AlignValue(uint x, uint alignment)
    {
        uint mask = ~(alignment - 1);
        return (x + (alignment - 1)) & mask;
    }
}
