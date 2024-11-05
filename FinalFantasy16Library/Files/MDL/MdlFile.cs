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
using CafeLibrary.Formats.FF16.Shared;

namespace CafeLibrary.ff16
{
    public class MdlFile
    {
        /// <summary>
        /// Header flags with an unknown purpose.
        /// </summary>
        public uint Flags;

        /// <summary>
        /// Attribute sets which determine what attributes to use from the attribute list.
        /// </summary>
        public List<VertexAttributeSet> AttributeSets = new List<VertexAttributeSet>();

        /// <summary>
        /// Attribute that determine the layout of vertices in a vertex buffer.
        /// </summary>
        public List<VertexAttribute> Attributes = new List<VertexAttribute>();

        /// <summary>
        /// The min and max bounding box of the entire model.
        /// </summary>
        public float[] BoundingBox = new float[8];

        /// <summary>
        /// Unknown values
        /// </summary>
        public ulong[] UnknownEntries;

        /// <summary>
        /// A list of material files that are externally referenced. 
        /// </summary>
        public List<string> MaterialFileNames = new List<string>();

        /// <summary>
        /// A list of vertex buffers used to store compressed vertex buffer data.
        /// </summary>
        public Buffer[] vBuffers;

        /// <summary>
        /// A list of index buffers used to store compressed index buffer data.
        /// </summary>
        public Buffer[] idxBuffers;

        /// <summary>
        /// Buffer with an unknown purpose.
        /// </summary>
        public Buffer UnknownBuffer1 = new Buffer();

        /// <summary>
        /// Buffer with an unknown purpose.
        /// </summary>
        public Buffer UnknownBuffer2 = new Buffer();

        /// <summary>
        /// The model spec header
        /// </summary>
        public MeshSpecsHeader SpecsHeader;

        /// <summary>
        /// A list of level of detail meshes
        /// </summary>
        public List<LODModelInfo> LODModels = new List<LODModelInfo>();

        /// <summary>
        /// A list of meshes used by the LODs.
        /// </summary>
        public List<MeshInfo> MeshInfos = new List<MeshInfo>();

        /// <summary>
        /// A list of sub draw calls for drawing less faces.
        /// </summary>
        public List<SubDrawCall> SubDrawCalls = new List<SubDrawCall>();

        /// <summary>
        /// A list of muscle joints for muscle calculations.
        /// </summary>
        public List<JointMuscleEntry> JointMuscles = new List<JointMuscleEntry>();

        /// <summary>
        /// A list of joint faces with an unknown purpose.
        /// </summary>
        public List<JointFaceEntry> JointFacesEntries = new List<JointFaceEntry>();

        /// <summary>
        /// A list of joints that have a joint name and position.
        /// </summary>
        public List<JointEntry> Joints = new List<JointEntry>();

        /// <summary>
        /// A list of bounding boxes used to attach to joints for culling.
        /// </summary>
        private List<JointBounding> JointBoundings = new List<JointBounding>();

        /// <summary>
        /// A list of joint names.
        /// These joints are all rigged and referenced to vertex data.
        /// </summary>
        public List<string> JointNames = new List<string>();

        /// <summary>
        /// A list of joint face names.
        /// </summary>
        public List<string> JointFaceNames = new List<string>();

        /// <summary>
        /// A list of joint muscle names.
        /// </summary>
        public List<string> JointMuscleNames = new List<string>();

        /// <summary>
        /// A list of material names. These always match the amount of material files.
        /// </summary>
        public List<string> MaterialNames = new List<string>();

        /// <summary>
        /// A list of parts with an unknown purpose.
        /// </summary>
        public List<string> Part1Names = new List<string>();

        /// <summary>
        /// A list of parts with an unknown purpose.
        /// </summary>
        public List<string> Part2Names = new List<string>();

        /// <summary>
        /// A list of parts with an unknown purpose.
        /// </summary>
        public List<string> Part3Names = new List<string>();

        //Extra section at the end with an unknown purpose
        private byte[] ExtraSection = new byte[0];
        //MCEX section used to store embedded data like collision.
        private byte[] McexSection = new byte[0];
        //An unknown value
        private byte Unknown1;

        public MdlFile(Stream stream)
        {
            Read(new FileReader(stream));
        }

        public void Save(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                Save(fs);
            }
        }

        public void Save(Stream stream)
        {
            Write(new FileWriter(stream));
        }

        private void Read(FileReader reader)
        {
            reader.ReadSignature("MDL ");
            Flags = reader.ReadUInt32();
            uint section1Size = reader.ReadUInt32();
            uint section2Size = reader.ReadUInt32();
            ushort materialNamesCount = reader.ReadUInt16();
            ushort attributesCount = reader.ReadUInt16();
            byte attributeSetCount = reader.ReadByte();
            byte lodCount = reader.ReadByte();
            byte unkCount = reader.ReadByte();
            byte unk2 = reader.ReadByte();
            uint[] vBuffersOffsets = reader.ReadUInt32s(8);
            uint[] idxBuffersOffsets = reader.ReadUInt32s(8);
            uint[] vBuffersSizes = reader.ReadUInt32s(8);
            uint[] idxBuffersSizes = reader.ReadUInt32s(8);
            uint unkBuffer1Offset = reader.ReadUInt32();
            uint unkBuffer1Size = reader.ReadUInt32();
            uint unkBuffer1Offset2 = reader.ReadUInt32();
            uint unkBuffer2Size = reader.ReadUInt32();

            AttributeSets = reader.ReadMultipleStructs<VertexAttributeSet>(attributeSetCount);
            Attributes = reader.ReadMultipleStructs<VertexAttribute>(attributesCount);
            BoundingBox = reader.ReadSingles(8);
            var namePointers = reader.ReadMultipleStructs<NamePointer>(materialNamesCount);
            UnknownEntries = reader.ReadUInt64s(unkCount);

            long nameTableStart = reader.Position;
            for (int i = 0; i < materialNamesCount; i++)
            {
                reader.SeekBegin(nameTableStart + (int)namePointers[i].Offset);
                MaterialFileNames.Add(reader.ReadStringZeroTerminated());
            }

            reader.SeekBegin(0xA8 + section1Size);
            ReadModelData(reader);

            vBuffers = ReadBuffers(reader, vBuffersOffsets, vBuffersSizes);
            idxBuffers = ReadBuffers(reader, idxBuffersOffsets, idxBuffersSizes);

            if (unkBuffer1Size > 0)
            {
                reader.SeekBegin(0xA8 + unkBuffer1Offset);
                UnknownBuffer1 = new Buffer() {
                    Data = reader.ReadBytes((int)unkBuffer1Size),
                };
            }
            if (unkBuffer2Size > 0)
            {
                reader.SeekBegin(0xA8 + unkBuffer1Offset2);
                UnknownBuffer2 = new Buffer() {
                    Data = reader.ReadBytes((int)unkBuffer2Size),
                };
            }
        }

        private long _ofsMeshInfoSavedPos; //for adjusting buffer offsets

        private void Write(FileWriter writer)
        {
            uint[] vBuffersSizes = new uint[8];
            uint[] idxBuffersSizes = new uint[8];

            for (int i = 0; i < this.vBuffers.Length; i++)
                if(vBuffers[i] != null)
                    vBuffersSizes[i] = (uint)vBuffers[i]?.Data?.Length;

            for (int i = 0; i < this.idxBuffers.Length; i++)
                if (idxBuffers[i] != null)
                    idxBuffersSizes[i] = (uint)idxBuffers[i]?.Data?.Length;

            writer.Write(Encoding.ASCII.GetBytes("MDL "));
            writer.Write(this.Flags);
            writer.Write(0); //mat size later
            writer.Write(0); //mesh spec size later
            writer.Write((ushort)this.MaterialFileNames.Count);
            writer.Write((ushort)this.Attributes.Count);
            writer.Write((byte)this.AttributeSets.Count);
            writer.Write((byte)this.LODModels.Count); //LOD count
            writer.Write((byte)UnknownEntries.Length);
            writer.Write((byte)0);

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
            WriteNameOffsets(writer, MaterialFileNames, ref nameOfs);
            writer.Write(UnknownEntries);
            writer.WriteStrings(MaterialFileNames);
            writer.Align(16);
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
                writer.WriteUint32Offset(ofsVbufferPos + (i * 4), start_section1);
                //mesh header offset
                writer.WriteUint32Offset(_ofsMeshInfoSavedPos + (i * 64) + 16, start_section1);
                writer.Write(vBuffers[i].Data);

                //mdl header offset
                writer.WriteUint32Offset(ofsIbufferPos + (i * 4), start_section1);
                //mesh header offset
                writer.WriteUint32Offset(_ofsMeshInfoSavedPos + (i * 64) + 20, start_section1);
                writer.Write(idxBuffers[i].Data);
            }

            //2 unknown buffers
            writer.WriteUint32Offset(ofsUnknownBuffers, start_section1);
            writer.Write(UnknownBuffer1.Data);

            writer.WriteUint32Offset(ofsUnknownBuffers + 8, start_section1);
            writer.Write(UnknownBuffer2.Data);
        }

        private void WriteMeshData(FileWriter writer)
        {
            //Prepare spec header
            SpecsHeader.LODModelCount = (ushort)this.LODModels.Count;
            SpecsHeader.MeshCount = (ushort)this.MeshInfos.Count;
            SpecsHeader.JointCount = (uint)this.Joints.Count;
            SpecsHeader.JointMuscleCount = (ushort)this.JointMuscles.Count;
            SpecsHeader.JointFaceCount = (byte)this.JointFaceNames.Count;
            SpecsHeader.SubDrawCallCount = (ushort)this.SubDrawCalls.Count;
            SpecsHeader.Entry3aCount = (byte)this.JointFacesEntries.Count;
            SpecsHeader.MaterialCount = (byte)this.MaterialNames.Count;
            SpecsHeader.AttributesCount = (byte)this.Attributes.Count;
            SpecsHeader.Part1Count = (byte)this.Part1Names.Count;
            SpecsHeader.Part2Count = (byte)this.Part2Names.Count;
            SpecsHeader.Part3Count = (byte)this.Part3Names.Count;

            SpecsHeader.StringPoolLength = (uint)MaterialNames.Sum(x => x.Length + 1) +
                                           (uint)JointNames.Sum(x => x.Length + 1) +
                                           (uint)JointFaceNames.Sum(x => x.Length + 1) +
                                           (uint)JointMuscleNames.Sum(x => x.Length + 1) +
                                           (uint)Part1Names.Sum(x => x.Length + 1) +
                                           (uint)Part2Names.Sum(x => x.Length + 1) +
                                           (uint)Part3Names.Sum(x => x.Length + 1);

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

            foreach (var mesh in LODModels)
            {
                //get sub meshes
                List<MeshInfo> subMeshes = new List<MeshInfo>();
                for (int i = 0; i < mesh.MeshCount; i++)
                    subMeshes.Add(MeshInfos[mesh.MeshIndex + i]);

                //Auto set vertex and index counters
                mesh.VertexCount = (uint)subMeshes.Sum(x => x.VertexCount);
                mesh.TriCount = (uint)subMeshes.Sum(x => x.IndexCount) / 3;
            }

            writer.WriteStruct(SpecsHeader);

            _ofsMeshInfoSavedPos = writer.Position;
            writer.WriteMultiStruct(LODModels);
            writer.WriteMultiStruct(MeshInfos);
            writer.WriteMultiStruct(SubDrawCalls);
            writer.WriteMultiStruct(Joints);
            WriteNameOffsets(writer, this.MaterialNames, ref nameOfs);

            writer.Write(jointFaceNameOffsets);
            writer.WriteMultiStruct(JointMuscles);
            writer.WriteMultiStruct(JointFacesEntries);

            nameOfs += (uint)JointNames.Sum(x => x.Length + 1);
            nameOfs += (uint)JointFaceNames.Sum(x => x.Length + 1);
            nameOfs += (uint)JointMuscleNames.Sum(x => x.Length + 1);

            WriteNameOffsets(writer, this.Part2Names, ref nameOfs);
            WriteNameOffsets(writer, this.Part1Names, ref nameOfs);
            WriteNameOffsets(writer, this.Part3Names, ref nameOfs);
            writer.Write(ExtraSection);
            writer.Write(McexSection);
            writer.Align(8);

            if (SpecsHeader.JointCount > 0)
                writer.WriteMultiStruct(JointBoundings);

            writer.WriteStrings(MaterialNames);
            writer.WriteStrings(JointNames);
            writer.WriteStrings(JointFaceNames);
            writer.WriteStrings(JointMuscleNames);
            writer.WriteStrings(Part2Names);
            writer.WriteStrings(Part1Names);
            writer.WriteStrings(Part3Names);
        }

        private void WriteNameOffsets(FileWriter writer, List<string> strings, ref uint offsetStart)
        {
            foreach (var name in strings)
            {
                writer.Write((ulong)offsetStart);
                writer.Write((ulong)0);
                offsetStart += (uint)name.Length + 1;
            }
        }

        private Buffer[] ReadBuffers(FileReader reader, uint[] offsets, uint[] sizes)
        {
            Buffer[] buffers = new Buffer[sizes.Length];
            for (int i = 0; i < sizes.Length; i++)
            {
                if (sizes[i] == 0)
                    continue;

                reader.SeekBegin(offsets[i] + 0xa8);
                buffers[i] = new Buffer()
                {
                    Data = reader.ReadBytes((int)sizes[i]),
                };
            }
            return buffers;
        }

        private void ReadModelData(FileReader reader)
        {
            SpecsHeader = reader.ReadStruct<MeshSpecsHeader>();
            LODModels = reader.ReadMultipleStructs<LODModelInfo>(SpecsHeader.LODModelCount);
            MeshInfos = reader.ReadMultipleStructs<MeshInfo>(SpecsHeader.MeshCount);

            SubDrawCalls = reader.ReadMultipleStructs<SubDrawCall>(SpecsHeader.SubDrawCallCount);
            Joints = reader.ReadMultipleStructs<JointEntry>(SpecsHeader.JointCount);
            var MaterialNamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.MaterialCount);
            var JointFaceNamePointers = reader.ReadUInt64s(SpecsHeader.JointFaceCount);
            JointMuscles = reader.ReadMultipleStructs<JointMuscleEntry>(SpecsHeader.JointMuscleCount);
            JointFacesEntries = reader.ReadMultipleStructs<JointFaceEntry>(SpecsHeader.Entry3aCount);
            var Part2NamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.Part2Count);
            var Part1NamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.Part1Count);
            var Part3NamePointers = reader.ReadMultipleStructs<NamePointer>(SpecsHeader.Part3Count);
            ExtraSection = reader.ReadBytes((int)SpecsHeader.ExtraSectionSize); //40 bytes when used

            McexSection = reader.ReadBytes((int)SpecsHeader.McexSize + 8);
            reader.Align(8);

            if (SpecsHeader.JointCount > 0)
                JointBoundings = reader.ReadMultipleStructs<JointBounding>(SpecsHeader.JointCount + 1);

            MaterialNames = ReadStrings(reader, SpecsHeader.MaterialCount);
            JointNames = ReadStrings(reader, SpecsHeader.JointCount);
            JointFaceNames = ReadStrings(reader, SpecsHeader.JointFaceCount);
            JointMuscleNames = ReadStrings(reader, SpecsHeader.JointMuscleCount);
            Part2Names = ReadStrings(reader, SpecsHeader.Part2Count);
            Part1Names = ReadStrings(reader, SpecsHeader.Part1Count);
            Part3Names = ReadStrings(reader, SpecsHeader.Part3Count);


            Console.WriteLine($"{Part1Names.Count} {Part2Names.Count} {Part3Names.Count} {SpecsHeader.McexSize}");

            for (int i = 0; i < Part1Names.Count; i++)
            {
               // Console.WriteLine($"{i} {Part1Names[i]}");
            }
            for (int i = 0; i < Part2Names.Count; i++)
            {
               // Console.WriteLine($"{i} {Part2Names[i]}");
            }
        }

        private List<string> ReadStrings(FileReader reader, uint count)
        {
            string[] strings = new string[count];
            for (int i = 0; i < count; i++)
                strings[i] = reader.ReadStringZeroTerminated();
            return strings.ToList();
        }

        public class Buffer
        {
            public byte[] Data = new byte[0];

            public byte[] GetDecompressedData(uint decompressedSize)
            {
                if (this.Data.Length != decompressedSize)
                {
                    byte[] decompressed = new byte[decompressedSize];
                    GDeflate.Decompress(this.Data, decompressed);
                    return decompressed;
                }
                else
                    return Data;
            }

            public void CompressedData(byte[] decompressedData)
            {
                if (decompressedData.Length == 0)
                    throw new Exception($"Buffer given is empty!");

                long sizeCompressed = GDeflate.CompressionSize((uint)decompressedData.Length);

                MemoryOwner<byte> compressedBuffer = MemoryOwner<byte>.Allocate((int)sizeCompressed);
                GDeflate.Compress(decompressedData, compressedBuffer.Span);
                this.Data = compressedBuffer.Span.ToArray();
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class MeshSpecsHeader
        {
            public uint McexSize;
            public ushort MeshCount;
            public ushort Part1Count;
            public ushort SubDrawCallCount;
            public ushort MaterialCount;
            public uint JointCount;

            public ushort Unknown1;
            public ushort LODModelCount;

            public byte Unknown3a;
            public byte JointFaceCount;

            public ushort JointMuscleCount;

            public byte Entry3aCount;
            public byte Part2Count;
            public byte Unknown6;
            public byte Part3Count;

            public uint ExtraSectionSize;
            public uint AttributesCount;
            public uint StringPoolLength; //string pool size at the end 
            public uint FormatFlags;
            public uint Unknown10;

            public float Unknown11;
            public float Unknown12;

            public uint UnknownBuffer1DecompressedSize;
            public uint UnknownBuffer2DecompressedSize;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class LODModelInfo
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

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class MeshInfo
        {
            public uint IndexCount;
            public uint IndexOffset;
            public ushort VertexCount;
            public ushort MaterialID;
            public ushort SubDrawCallIndex;
            public ushort SubDrawCallCount; //sub draw call that ranges 0 -> 3. Not always used, but reduces draw usage after first level

            public byte AttributeSetIdx;
            public byte ColorSetFlag;
            public byte BoneSetFlag; 
            public byte TexCoordSetFlag;

            public uint Flag2;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public uint[] Unknowns2 = new uint[6];

            public uint Unknown3;

            public byte Unknown4;
            public byte Unknown5;
            public byte Unknown6;
            public byte BufferCount = 2;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public uint[] BufferOffsets = new uint[8];

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Strides = new byte[8];
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class JointEntry
        {
            public uint NameOffset;
            public Vector3Struct WorldPosition;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SubDrawCall
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

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class JointBounding
        {
            public Vector3Struct BoundingMin;
            public Vector3Struct BoundingMax;
            public float Unknown1;
            public float Unknown2;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class JointMuscleEntry
        {
            public uint NameOffset;
            public float Unknown1;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public ushort[] IndicesSet1;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public ushort[] IndicesSet2;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public float[] WeightsSet1;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public float[] WeightsSet2;

            public Vector3Struct Unknown2;
            public Vector3Struct Unknown3;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class JointFaceEntry
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public float[] a;

            public ushort b;
            public ushort c;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Vector3Struct
        {
            public float X;
            public float Y;
            public float Z;
        }

        public struct VertexAttributeSet
        {
            public ushort Idx; //Attribute idx
            public ushort Count; //Attribute count
        }

        public struct VertexAttribute
        {
            public byte BufferIdx;
            public byte Offset;
            public AttributeFormat Format;
            public AttributeType Type;

            public VertexAttribute() { }

            public VertexAttribute(byte buffer, byte offset, AttributeType type, AttributeFormat format)
            {
                this.BufferIdx = buffer;
                this.Offset = offset;
                this.Format = format;
                this.Type = type;
            }

            public int Size
            {
                get
                {
                    switch (this.Format)
                    {
                        case AttributeFormat.Byte4:
                        case AttributeFormat.UNorm8:
                        case AttributeFormat.Vec2HalfFloat:
                            return 4;
                        case AttributeFormat.Vec4f:
                            return 16;
                        case AttributeFormat.Vec3f:
                            return 12;
                        case AttributeFormat.Vec4HalfFloat:
                        case AttributeFormat.Vec2f:
                            return 8;
                        default:
                            throw new Exception($"{this.Format} not supported!");
                    }
                }
            }

            public override string ToString()
            {
                return $"{this.Type}_{this.Format}_{this.Offset}_{BufferIdx}";
            }
        }

        public enum AttributeFormat : byte
        {
            Vec2f = 34,
            Vec3f = 35,
            Vec4f = 36,

            Vec2HalfFloat = 50,
            Vec4HalfFloat = 52,
            UNorm8 = 68,
            Byte4 = 116,
        }

        public enum AttributeType : byte
        {
            Position = 0,
            BoneWeights0 = 1,
            BoneIndices0 = 2,
            Color0 = 3,

            UnknownAttr8 = 8,
            UnknownAttr9 = 9,

            TexCoord01 = 11,
            TexCoord23 = 12,
            Normals = 21,
            Tangents = 22,
            Binormal = 23,

            UnknownAttr24 = 24,

            BoneWeights1 = 28,
            BoneIndices1 = 29,
        }

        public struct NamePointer
        {
            public ulong Offset;
            public ulong Padding;
        }
    }
}
