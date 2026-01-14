using System.Text;

using Syroot.BinaryData;

using FinalFantasy16Library.Files.MDL.Helpers;
using FinalFantasy16Library.Files.MDL.Convert;
using FinalFantasy16Library.Utils;

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
    public List<MdlJointMuscleEntry> MuscleJoints = [];

    /// <summary>
    /// A list of joint faces with an unknown purpose.
    /// </summary>
    public List<MdlUnkJointParam> FaceJoints = [];

    /// <summary>
    /// A list of joints that have a joint name and position.
    /// </summary>
    public List<JointEntry> Joints = [];

    /// <summary>
    /// A list of parts with an unknown purpose.
    /// </summary>
    public List<MdlVFXEntry> VFXEntries = [];

    /// <summary>
    /// A list of parts with an unknown purpose.
    /// </summary>
    public List<MdlUnk6Entry> Entries6 = [];

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
    public List<string> FaceJointNames = [];

    /// <summary>
    /// A list of joint muscle names.
    /// </summary>
    public List<string> MuscleJointNames = [];

    /// <summary>
    /// A list of material names. These always match the amount of material files.
    /// </summary>
    public List<string> MaterialNames = [];

    /// <summary>
    /// A list of parts with an unknown purpose.
    /// </summary>
    public List<string> OptionNames = [];

    /// <summary>
    /// A list of parts with an unknown purpose.
    /// </summary>
    public List<string> AdditionalPartsNames = [];

    public List<string> VFXEntriesNames = [];

    //Extra section at the end with an unknown purpose
    private byte[] ExtraData7 = [];

    //MCEX section used to store embedded data like collision.
    private byte[] ExternalContentSection = [];

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
        Read(new BinaryStream(stream));
    }

    public void Save(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        Save(fs);
    }

    public void Save(Stream stream)
    {
        Write(new BinaryStream(stream));
    }

    #region Reading
    private void Read(BinaryStream reader)
    {
        reader.ReadSignature("MDL "u8);
        Version = reader.Read1Byte();

        if (Version != 28)
            Console.WriteLine($"WARN: Only MDL version 28 is supported. Version: {Version}");

        MainFlags = reader.Read1Byte();
        if (MainFlags != 1)
            Console.WriteLine($"WARN: Model flag 1 is missing.");

        ModelType = reader.Read1Byte();
        reader.ReadByte();

        uint section1Size = reader.ReadUInt32();
        uint section2Size = reader.ReadUInt32();
        ushort materialNamesCount = reader.ReadUInt16();
        ushort flexVertAttributeCount = reader.ReadUInt16();
        byte flexVertInfoCount = reader.Read1Byte();
        byte lodCount = reader.Read1Byte();
        UnkFlags_0x16 = reader.Read1Byte();
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
            reader.Position = nameTableStart + (int)namePointers[i].Offset;
            MaterialFileNames.Add(reader.ReadString(StringCoding.ZeroTerminated));
        }

        reader.Position = MainHeaderSize + section1Size;
        ReadModelData(reader);

        vBuffers = ReadBuffers(reader, vBuffersOffsets, vBuffersSizes);
        idxBuffers = ReadBuffers(reader, idxBuffersOffsets, idxBuffersSizes);

        if (unkBuffer1Size > 0)
        {
            reader.Position = MainHeaderSize + unkBuffer1Offset;
            UnknownBuffer1 = new ModelBuffer()
            {
                Data = reader.ReadBytes((int)unkBuffer1Size),
            };
        }

        if (unkBuffer2Size > 0)
        {
            reader.Position = MainHeaderSize + unkBuffer1Offset2;
            UnknownBuffer2 = new ModelBuffer()
            {
                Data = reader.ReadBytes((int)unkBuffer2Size),
            };
        }
    }

    private ModelBuffer[] ReadBuffers(BinaryStream reader, uint[] offsets, uint[] sizes)
    {
        ModelBuffer[] buffers = new ModelBuffer[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            if (sizes[i] == 0)
                continue;

            reader.Position = offsets[i] + MainHeaderSize;
            buffers[i] = new ModelBuffer()
            {
                Data = reader.ReadBytes((int)sizes[i]),
            };
        }
        return buffers;
    }

    private void ReadModelData(BinaryStream reader)
    {
        SpecsHeader = reader.ReadStruct<MeshSpecsHeader>();
        LODModels = reader.ReadMultipleStructs<MdlLODModelInfo>(SpecsHeader.LODModelCount);
        MeshInfos = reader.ReadMultipleStructs<MdlMeshInfo>(SpecsHeader.SubmeshCount);
        SubDrawCalls = reader.ReadMultipleStructs<SubDrawPart>(SpecsHeader.DrawPartCount);
        Joints = reader.ReadMultipleStructs<JointEntry>(SpecsHeader.JointCount);
        var MaterialNamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.MaterialCount);
        var JointFaceNamePointers = reader.ReadMultipleStructs<MdlFaceJointEntry>(SpecsHeader.FaceJointCount);
        MuscleJoints = reader.ReadMultipleStructs<MdlJointMuscleEntry>(SpecsHeader.MuscleJointCount);
        FaceJoints = reader.ReadMultipleStructs<MdlUnkJointParam>(SpecsHeader.UnkJointParamCount);
        var AdditionalPartNamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.AdditionalPartCount);
        var OptionNamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.OptionCount);
        VFXEntries = reader.ReadMultipleStructs<MdlVFXEntry>(SpecsHeader.VFXEntryCount);
        Entries6 = reader.ReadMultipleStructs<MdlUnk6Entry>(SpecsHeader.Entries6Count);

        ExtraData7 = reader.ReadBytes((int)SpecsHeader.ExtraSectionSize); //40 bytes when used
        reader.Align(0x04);

        long mcexPosition = reader.Position;
        ExternalContentSection = reader.ReadBytes((int)SpecsHeader.ModelExternalContentSize);
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

            string str = reader.ReadString(StringCoding.ZeroTerminated);
            if (!str.All(char.IsAscii))
                throw new Exception($"Material name '{str}' must be a valid ASCII with no special characters.");

            MaterialNames.Add(str);
        }

        for (int i = 0; i < SpecsHeader.JointCount; i++)
        {
            reader.Position = strTableOffset + Joints[i].NameOffset;

            string str = reader.ReadString(StringCoding.ZeroTerminated);
            if (!str.All(char.IsAscii))
                throw new Exception($"Joint name '{str}' must be a valid ASCII with no special characters.");

            JointNames.Add(str);
        }

        for (int i = 0; i < SpecsHeader.FaceJointCount; i++)
        {
            reader.Position = strTableOffset + JointFaceNamePointers[i].Offset;

            string str = reader.ReadString(StringCoding.ZeroTerminated);
            if (!str.All(char.IsAscii))
                throw new Exception($"Joint face name '{str}' must be a valid ASCII with no special characters.");

            FaceJointNames.Add(str);
        }

        for (int i = 0; i < SpecsHeader.MuscleJointCount; i++)
        {
            reader.Position = strTableOffset + MuscleJoints[i].NameOffset;

            string str = reader.ReadString(StringCoding.ZeroTerminated);
            if (!str.All(char.IsAscii))
                throw new Exception($"Joint muscle name '{str}' must be a valid ASCII with no special characters.");

            MuscleJointNames.Add(str);
        }

        for (int i = 0; i < SpecsHeader.AdditionalPartCount; i++)
        {
            reader.Position = strTableOffset + AdditionalPartNamePointers[i].Offset;

            string str = reader.ReadString(StringCoding.ZeroTerminated);
            if (!str.All(char.IsAscii))
                throw new Exception($"Additional part '{str}' must be a valid ASCII with no special characters.");

            AdditionalPartsNames.Add(str);
        }

        for (int i = 0; i < SpecsHeader.OptionCount; i++)
        {
            reader.Position = strTableOffset + OptionNamePointers[i].Offset;

            string str = reader.ReadString(StringCoding.ZeroTerminated);
            if (!str.All(char.IsAscii))
                throw new Exception($"Option name '{str}' must be a valid ASCII with no special characters.");

            OptionNames.Add(str);
        }

        for (int i = 0; i < SpecsHeader.VFXEntryCount; i++)
        {
            var entry = new MdlVFXEntry();
            reader.Position = strTableOffset + VFXEntries[i].NameOffset;

            string str = reader.ReadString(StringCoding.ZeroTerminated);
            if (!str.All(char.IsAscii))
                throw new Exception($"VFX Entry '{str}' must be a valid ASCII with no special characters.");

            VFXEntriesNames.Add(str);
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

    private void Write(BinaryStream writer)
    {
        uint[] vBuffersSizes = new uint[8];
        uint[] idxBuffersSizes = new uint[8];

        for (int i = 0; i < vBuffers.Length; i++)
            if (vBuffers[i] != null)
                vBuffersSizes[i] = (uint)vBuffers[i].Data.Length;

        for (int i = 0; i < idxBuffers.Length; i++)
            if (idxBuffers[i] != null)
                idxBuffersSizes[i] = (uint)idxBuffers[i].Data.Length;

        writer.Write("MDL "u8);
        writer.WriteByte(Version);
        writer.WriteByte(MainFlags);
        writer.WriteByte(ModelType);
        writer.WriteByte(0);
        writer.WriteUInt32(0); //mat size later
        writer.WriteUInt32(0); //mesh spec size later
        writer.WriteUInt16((ushort)MaterialFileNames.Count);
        writer.WriteUInt16((ushort)Attributes.Count);
        writer.WriteByte((byte)AttributeSets.Count);
        writer.WriteByte((byte)LODModels.Count); //LOD count
        writer.WriteByte(UnkFlags_0x16);
        writer.WriteByte(0);

        //vertex buffer offsets
        long ofsVbufferPos = writer.Position;
        writer.WriteUInt32s(new uint[8]); //vertex buffer offsets saved later
        long ofsIbufferPos = writer.Position;
        writer.WriteUInt32s(new uint[8]); //index buffer offsets saved later
        writer.WriteUInt32s(vBuffersSizes);
        writer.WriteUInt32s(idxBuffersSizes);

        long ofsUnknownBuffers = writer.Position;

        writer.WriteUInt32(0); //unknown buffer 1 offset
        writer.WriteUInt32((uint)UnknownBuffer1.Data.Length);
        writer.WriteUInt32(0); //unknown buffer 2 offset
        writer.WriteUInt32((uint)UnknownBuffer2.Data.Length);

        long start_section1 = writer.Position;

        writer.WriteMultiStruct(AttributeSets);
        writer.WriteMultiStruct(Attributes);
        writer.Write(BoundingBox);

        // Write material file name table
        long matFileNamesTableOffset = writer.Position;
        var materialFileNamesStrTable = new OptimizedStringTable() { IsRelativeOffsets = true };
        foreach (var materialFileName in MaterialFileNames)
            materialFileNamesStrTable.AddString(materialFileName);
        writer.Position += (0x10 * MaterialFileNames.Count); // Skip for now
        if ((UnkFlags_0x16 & 2) != 0)
            writer.Write(UnknownEntries);
        materialFileNamesStrTable.SaveStream(writer);
        writer.Align(0x08);

        // String table written, write the structures now
        long endTableOffset = writer.Position;
        writer.Position = matFileNamesTableOffset;
        for (int i = 0; i < MaterialFileNames.Count; i++)
        {
            writer.WriteUInt32((uint)materialFileNamesStrTable.GetStringOffset(MaterialFileNames[i]));
            writer.Position += 0x0C;
        }
        writer.Position = endTableOffset;

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

    private void WriteMeshData(BinaryStream writer)
    {
        long baseHeaderOffset = writer.Position;

        //Prepare spec header
        SpecsHeader.LODModelCount = (ushort)LODModels.Count;
        SpecsHeader.SubmeshCount = (ushort)MeshInfos.Count;
        SpecsHeader.JointCount = (ushort)Joints.Count;
        SpecsHeader.MuscleJointCount = (ushort)MuscleJoints.Count;
        SpecsHeader.FaceJointCount = (byte)FaceJointNames.Count;
        SpecsHeader.DrawPartCount = (ushort)SubDrawCalls.Count;
        SpecsHeader.UnkJointParamCount = (byte)FaceJoints.Count;
        SpecsHeader.MaterialCount = (byte)MaterialNames.Count;
        SpecsHeader.FlexVertexCount = (byte)Attributes.Count;
        SpecsHeader.OptionCount = (byte)OptionNames.Count;
        SpecsHeader.AdditionalPartCount = (byte)AdditionalPartsNames.Count;
        SpecsHeader.VFXEntryCount = (byte)VFXEntries.Count;
        SpecsHeader.Entries6Count = (ushort)Entries6.Count;

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

        var stringTable = new OptimizedStringTable() { IsRelativeOffsets = true };
        foreach (var str in MaterialNames) // Yes these come before joints despite joints's struct being first
            stringTable.AddString(str);
        foreach (var str in JointNames)
            stringTable.AddString(str);
        foreach (var str in FaceJointNames)
            stringTable.AddString(str);
        foreach (var str in MuscleJointNames)
            stringTable.AddString(str);
        foreach (var str in AdditionalPartsNames)
            stringTable.AddString(str);
        foreach (var str in OptionNames)
            stringTable.AddString(str);
        foreach (var str in VFXEntriesNames)
            stringTable.AddString(str);

        // At this point we can start writing the contents, except structs that have strings
        writer.Position = baseHeaderOffset + 0x40; // Skip header for now
        _ofsMeshInfoSavedPos = writer.Position;
        writer.WriteMultiStruct(LODModels);
        writer.WriteMultiStruct(MeshInfos);
        writer.WriteMultiStruct(SubDrawCalls);

        long jointsOffset = writer.Position;
        writer.Position += (Joints.Count * 0x10); // Write later
        writer.Position += (MaterialNames.Count * 0x10); // Write later
        writer.Position += (FaceJointNames.Count * 0x08); // Write later
        writer.Position += (MuscleJoints.Count * 0x50); // Write later
        writer.Position += (FaceJoints.Count * 0x20); // Write later
        writer.Position += (AdditionalPartsNames.Count * 0x10); // Write later
        writer.Position += (OptionNames.Count * 0x10); // Write later
        writer.Position += (VFXEntries.Count * 0x10); // Write later
        writer.Position += (Entries6.Count * 0x20); // Write later
        writer.Write(ExtraData7);
        writer.Align(0x04);

        long modelExternalContentOffset = writer.Position;
        writer.Write(ExternalContentSection);
        writer.Position = modelExternalContentOffset + (int)AlignValue((uint)ExternalContentSection.Length, 0x10);

        if (SpecsHeader.JointCount > 0)
            writer.WriteMultiStruct(JointBoundings);

        if (SpecsHeader.JointCount > 0)
        {
            long basePos = writer.Position;
            writer.Write(JointMaxBounds);
            writer.Position = basePos + AlignValue(sizeof(float) * 6, 0x10);
        }

        long stringTableOffset = writer.Position;
        stringTable.SaveStream(writer);
        SpecsHeader.StringTableSize = (uint)(writer.Position - stringTableOffset);
        writer.Align(0x10, grow: true);
        if ((UnkFlags_0x16 & 1) != 0)
        {
            writer.Write(ExtraSection2);
            writer.Align(0x10, grow: true);
        }
        long bottomOffset = writer.Position;

        // Bottom.
        // Write structures we haven't written yet (mainly because they have strings)
        writer.Position = jointsOffset;
        for (int i = 0; i < JointNames.Count; i++)
            Joints[i].NameOffset = (uint)stringTable.GetStringOffset(JointNames[i]);
        writer.WriteMultiStruct(Joints);

        for (int i = 0; i < MaterialNames.Count; i++)
        {
            writer.WriteUInt32((uint)stringTable.GetStringOffset(MaterialNames[i]));
            writer.Position += 0x0C;
        }

        for (int i = 0; i < FaceJointNames.Count; i++)
        {
            writer.WriteUInt32((uint)stringTable.GetStringOffset(FaceJointNames[i]));
            writer.WriteUInt32(0);
        }

        for (int i = 0; i < MuscleJoints.Count; i++)
            MuscleJoints[i].NameOffset = (uint)stringTable.GetStringOffset(MuscleJointNames[i]);
        writer.WriteMultiStruct(MuscleJoints);

        writer.WriteMultiStruct(FaceJoints);

        for (int i = 0; i < AdditionalPartsNames.Count; i++)
        {
            writer.WriteUInt32((uint)stringTable.GetStringOffset(AdditionalPartsNames[i]));
            writer.Position += 0x0C;
        }

        for (int i = 0; i < OptionNames.Count; i++)
        {
            writer.WriteUInt32((uint)stringTable.GetStringOffset(OptionNames[i]));
            writer.Position += 0x0C;
        }

        for (int i = 0; i < VFXEntriesNames.Count; i++)
            VFXEntries[i].NameOffset = (uint)stringTable.GetStringOffset(VFXEntriesNames[i]);
        writer.WriteMultiStruct(VFXEntries);
        writer.WriteMultiStruct(Entries6);

        writer.Position = baseHeaderOffset;
        writer.WriteStruct(SpecsHeader);
        writer.Position = bottomOffset;
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
