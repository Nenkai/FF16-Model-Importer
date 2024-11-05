using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Toolbox.Core;

namespace CafeLibrary.ff16
{
    public class MdlBufferHelper
    { 
        public static List<Vertex> LoadVertices(MdlFile mdlFile, MdlFile.MeshInfo subMesh, byte[] buffer)
        {
            List<Vertex> vertices = new List<Vertex>();

            using (var reader = new BinaryReader(new MemoryStream(buffer)))
            {
                var setIndex = subMesh.AttributeSetIdx;
                var attributeSet = mdlFile.AttributeSets[setIndex];

                for (int v = 0; v < subMesh.VertexCount; v++)
                {
                    Vertex vertex = new Vertex();
                    vertices.Add(vertex);

                    for (int aIdx = 0; aIdx < attributeSet.Count; aIdx++)
                    {
                        var attribute = mdlFile.Attributes[attributeSet.Idx + aIdx];

                        var ofs = subMesh.BufferOffsets[attribute.BufferIdx] + v * subMesh.Strides[attribute.BufferIdx] + attribute.Offset;
                        reader.BaseStream.Seek(ofs, SeekOrigin.Begin);

                        switch (attribute.Type)
                        {
                            case MdlFile.AttributeType.Position: vertex.Position = ReadVector3(reader, attribute); break;
                            case MdlFile.AttributeType.Normals: vertex.Normal = ReadVector3(reader, attribute);  break;
                            case MdlFile.AttributeType.Tangents: vertex.Tangent = ReadVector4(reader, attribute); break;
                            case MdlFile.AttributeType.Binormal: vertex.Binormal = ReadVector4(reader, attribute); break;
                            case MdlFile.AttributeType.TexCoord01: vertex.TexCoord01 = ReadVector4(reader, attribute); break;
                            case MdlFile.AttributeType.TexCoord23:  vertex.TexCoord23 = ReadVector4(reader, attribute); break;
                            case MdlFile.AttributeType.BoneIndices0: vertex.BoneIndices0 = ReadVector4(reader, attribute); break;
                            case MdlFile.AttributeType.BoneWeights0: vertex.BoneWeights0 = ReadVector4(reader, attribute); break;
                            case MdlFile.AttributeType.BoneIndices1: vertex.BoneIndices1 = ReadVector4(reader, attribute); break;
                            case MdlFile.AttributeType.BoneWeights1: vertex.BoneWeights1 = ReadVector4(reader, attribute); break;
                            case MdlFile.AttributeType.UnknownAttr8: vertex.UnknownAttr8 = ReadVector4(reader, attribute); break;
                            case MdlFile.AttributeType.UnknownAttr9: vertex.UnknownAttr9 = ReadVector4(reader, attribute); break;
                            case MdlFile.AttributeType.Color0: vertex.Color = ReadVector4(reader, attribute); break;
                            default:
                                break;
                        }
                    }
                }
            }

            return vertices;
        }

        public static List<int> LoadIndices(MdlFile.MeshInfo subMesh, byte[] indexBuffer)
        {
            List<int> indices = new List<int>();
            using (var reader = new BinaryReader(new MemoryStream(indexBuffer)))
            {
                reader.BaseStream.Seek(subMesh.IndexOffset * 2, SeekOrigin.Begin);
                for (int i = 0; i < subMesh.IndexCount; i++)
                    indices.Add(reader.ReadUInt16());
            }
            return indices;
        }

        public static void WriteIndicesBuffer(MdlFile.MeshInfo subMesh,
    List<ushort> indices, BinaryWriter writer)
        {
            subMesh.IndexOffset = (uint)writer.BaseStream.Position / 2;
            subMesh.IndexCount = (uint)indices.Count;

            foreach (var index in indices)
                writer.Write(index);
        }

        public static void WriteVertexBuffer(MdlFile mdlFile,
            MdlFile.MeshInfo mesh, List<Vertex> vertices, BinaryWriter writer)
        {
            mesh.VertexCount = (ushort)vertices.Count;

            var setIndex = mesh.AttributeSetIdx;
            var attributeSet = mdlFile.AttributeSets[setIndex];

            //Compute stride via attribute sizes
            Array.Clear(mesh.Strides);
            for (int aIdx = 0; aIdx < attributeSet.Count; aIdx++)
            {
                var attribute = mdlFile.Attributes[attributeSet.Idx + aIdx];
                mesh.Strides[attribute.BufferIdx] += (byte)attribute.Size;
            }

            for (int bufferIdx = 0; bufferIdx < mesh.BufferCount; bufferIdx++)
            {
                mesh.BufferOffsets[bufferIdx] = (uint)writer.BaseStream.Position;
                for (int v = 0; v < vertices.Count; v++)
                {
                    for (int aIdx = 0; aIdx < attributeSet.Count; aIdx++)
                    {
                        var attribute = mdlFile.Attributes[attributeSet.Idx + aIdx];
                        if (attribute.BufferIdx != bufferIdx)
                            continue;

                        var ofs = mesh.BufferOffsets[bufferIdx] + v * mesh.Strides[attribute.BufferIdx] + attribute.Offset;
                        writer.BaseStream.Seek(ofs, SeekOrigin.Begin);

                        switch (attribute.Type)
                        {
                            case MdlFile.AttributeType.Position: WriteVector(writer, vertices[v].Position, attribute); break;
                            case MdlFile.AttributeType.Normals: WriteVector(writer, vertices[v].Normal, attribute); break;
                            case MdlFile.AttributeType.Tangents: WriteVector(writer, vertices[v].Tangent, attribute); break;
                            case MdlFile.AttributeType.Binormal: WriteVector(writer, vertices[v].Binormal, attribute); break;
                            case MdlFile.AttributeType.TexCoord01: WriteVector(writer, vertices[v].TexCoord01, attribute); break;
                            case MdlFile.AttributeType.TexCoord23: WriteVector(writer, vertices[v].TexCoord23, attribute); break;
                            case MdlFile.AttributeType.BoneIndices0: WriteVector(writer, vertices[v].BoneIndices0, attribute); break;
                            case MdlFile.AttributeType.BoneWeights0: WriteVector(writer, vertices[v].BoneWeights0, attribute); break;
                            case MdlFile.AttributeType.BoneIndices1: WriteVector(writer, vertices[v].BoneIndices1, attribute); break;
                            case MdlFile.AttributeType.BoneWeights1: WriteVector(writer, vertices[v].BoneWeights1, attribute); break;
                            case MdlFile.AttributeType.UnknownAttr8: WriteVector(writer, vertices[v].UnknownAttr8, attribute); break;
                            case MdlFile.AttributeType.UnknownAttr9: WriteVector(writer, vertices[v].UnknownAttr9, attribute); break;
                            case MdlFile.AttributeType.UnknownAttr24: WriteVector(writer, vertices[v].UnknownAttr24, attribute); break;
                            case MdlFile.AttributeType.Color0: WriteVector(writer, vertices[v].Color, attribute); break;
                            default:
                                throw new Exception($"Attribute {attribute.Type} not supported!");
                        }
                    }
                }
            }
        }

        #region Read Vectors


        static Vector2 ReadVector2(BinaryReader reader, MdlFile.VertexAttribute attr)
        {
            if (attr.Format == MdlFile.AttributeFormat.Vec2f)
            {
                return new Vector2(
                     reader.ReadSingle(),
                     reader.ReadSingle());
            }
            else if (attr.Format == MdlFile.AttributeFormat.Vec2HalfFloat)
            {
                return new Vector2(
                     (float)reader.ReadHalf(),
                     (float)reader.ReadHalf());
            }
            else
                throw new Exception($"Unsupported format {attr.Format}");
        }

        static Vector3 ReadVector3(BinaryReader reader, MdlFile.VertexAttribute attr)
        {
            if (attr.Format == MdlFile.AttributeFormat.Vec4HalfFloat)
            {
                var v = new Vector3(
                     (float)reader.ReadHalf(),
                     (float)reader.ReadHalf(),
                     (float)reader.ReadHalf());
                reader.ReadUInt16();
                return v;
            }
            else if (attr.Format == MdlFile.AttributeFormat.Vec3f)
            {
                return new Vector3(
                 reader.ReadSingle(),
                 reader.ReadSingle(),
                 reader.ReadSingle());
            }
            else
                throw new Exception($"Unsupported format {attr.Format}");
        }

        static Vector4 ReadVector4(BinaryReader reader, MdlFile.VertexAttribute attr)
        {
            if (attr.Format == MdlFile.AttributeFormat.Vec4HalfFloat)
            {
                var v = new Vector4(
                     (float)reader.ReadHalf(),
                     (float)reader.ReadHalf(),
                     (float)reader.ReadHalf(),
                     (float)reader.ReadHalf());
                return v;
            }
            else if (attr.Format == MdlFile.AttributeFormat.Vec2f)
            {
                return new Vector4(reader.ReadSingle(), reader.ReadSingle(), 0, 0);
            }
            else if (attr.Format == MdlFile.AttributeFormat.Vec2HalfFloat)
            {
                return new Vector4((float)reader.ReadHalf(), (float)reader.ReadHalf(), 0, 0);
            }
            else if (attr.Format == MdlFile.AttributeFormat.Vec4f)
            {
                return new Vector4(
                     reader.ReadSingle(),
                     reader.ReadSingle(),
                     reader.ReadSingle(),
                     reader.ReadSingle());
            }
            else if (attr.Format == MdlFile.AttributeFormat.UNorm8)
            {
                return new Vector4(
                     (float)reader.ReadByte() / byte.MaxValue,
                     (float)reader.ReadByte() / byte.MaxValue,
                     (float)reader.ReadByte() / byte.MaxValue,
                     (float)reader.ReadByte() / byte.MaxValue);
            }
            else if (attr.Format == MdlFile.AttributeFormat.Byte4)
            {
                return new Vector4(
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte());
            }
            else
                throw new Exception($"Unsupported format {attr.Format}");
        }

        #endregion

        #region Write Vectors

        static void WriteVector(BinaryWriter writer, Vector3 value, MdlFile.VertexAttribute attr)
        {
            if (attr.Format == MdlFile.AttributeFormat.Vec4HalfFloat)
            {
                writer.Write((Half)value.X);
                writer.Write((Half)value.Y);
                writer.Write((Half)value.Z);
                writer.Write((ushort)0);
            }
            else if (attr.Format == MdlFile.AttributeFormat.Vec3f)
            {
                writer.Write(value.X);
                writer.Write(value.Y);
                writer.Write(value.Z);
            }
            else
                throw new Exception($"Unsupported format {attr.Format}");
        }

        static void WriteVector(BinaryWriter writer, Vector4 value, MdlFile.VertexAttribute attr)
        {
            if (attr.Format == MdlFile.AttributeFormat.Vec4HalfFloat)
            {
                writer.Write((Half)value.X);
                writer.Write((Half)value.Y);
                writer.Write((Half)value.Z);
                writer.Write((Half)value.W);
            }
            else if (attr.Format == MdlFile.AttributeFormat.Vec2f)
            {
                writer.Write(value.X);
                writer.Write(value.Y);
            }
            else if (attr.Format == MdlFile.AttributeFormat.Vec2HalfFloat)
            {
                writer.Write((Half)value.X);
                writer.Write((Half)value.Y);
            }
            else if (attr.Format == MdlFile.AttributeFormat.Vec4f)
            {
                writer.Write(value.X);
                writer.Write(value.Y);
                writer.Write(value.Z);
                writer.Write(value.W);
            }
            else if (attr.Format == MdlFile.AttributeFormat.UNorm8)
            {
                writer.Write((byte)(value.X * byte.MaxValue));
                writer.Write((byte)(value.Y * byte.MaxValue));
                writer.Write((byte)(value.Z * byte.MaxValue));
                writer.Write((byte)(value.W * byte.MaxValue));
            }
            else if (attr.Format == MdlFile.AttributeFormat.Byte4)
            {
                writer.Write((byte)value.X);
                writer.Write((byte)value.Y);
                writer.Write((byte)value.Z);
                writer.Write((byte)value.W);
            }
            else
                throw new Exception($"Unsupported format {attr.Format}");
        }

        #endregion

        public class Vertex
        {
            public Vector3 Position { get; set; }
            public Vector4 TexCoord01 { get; set; }
            public Vector4 TexCoord23 { get; set; }
            public Vector4 BoneIndices0 { get; set; }
            public Vector4 BoneIndices1 { get; set; }
            public Vector4 BoneWeights0 { get; set; }
            public Vector4 BoneWeights1 { get; set; }

            public Vector3 Normal { get; set; } = new Vector3(0.5f, 0.5f, 0.5f);

            public Vector4 Color { get; set; } = Vector4.One;

            public Vector4 Tangent { get; set; }
            public Vector4 Binormal { get; set; }

            public Vector4 UnknownAttr8 { get; set; }
            public Vector4 UnknownAttr9 { get; set; }
            public Vector4 UnknownAttr24 { get; set; }

            public List<int> GetBoneIndices()
            {
                var boneCount = (byte)Math.Floor(BoneWeights0.X * 8 + 0.1);

                List<int> boneIndices = new List<int>();
                if (boneCount > 0) boneIndices.Add((int)BoneIndices0.X);
                if (boneCount > 1) boneIndices.Add((int)BoneIndices0.Y);
                if (boneCount > 2) boneIndices.Add((int)BoneIndices0.Z);
                if (boneCount > 3) boneIndices.Add((int)BoneIndices0.W);

                if (boneCount > 4) boneIndices.Add((int)BoneIndices1.X);
                if (boneCount > 5) boneIndices.Add((int)BoneIndices1.Y);
                if (boneCount > 6) boneIndices.Add((int)BoneIndices1.Z);
                if (boneCount > 7) boneIndices.Add((int)BoneIndices1.W);
                return boneIndices;
            }

            public List<float> GetBoneWeights()
            {
                var boneCount = (byte)Math.Floor(BoneWeights0.X * 8 + 0.1);

                var sum = BoneWeights0.Y + BoneWeights0.Z + BoneWeights0.W +
                          BoneWeights1.X + BoneWeights1.Y + BoneWeights1.Z + BoneWeights1.W;

                List<float> boneWeights = new List<float>();
                if (boneCount > 0) boneWeights.Add(1.0f - sum);
                if (boneCount > 1) boneWeights.Add(BoneWeights0.Y);
                if (boneCount > 2) boneWeights.Add(BoneWeights0.Z);
                if (boneCount > 3) boneWeights.Add(BoneWeights0.W);

                if (boneCount > 4) boneWeights.Add(BoneWeights1.X);
                if (boneCount > 5) boneWeights.Add(BoneWeights1.Y);
                if (boneCount > 6) boneWeights.Add(BoneWeights1.Z);
                if (boneCount > 7) boneWeights.Add(BoneWeights1.W);
                return boneWeights;
            }

            public void SetBoneIndicesWeights(List<int> boneIndices, List<float> weights)
            {
                var boneCount = ((byte)boneIndices.Count * 32) / 255f;
                BoneWeights0 = new Vector4(boneCount,
                    weights.Count > 0 ? weights[0] : 0f,
                    weights.Count > 1 ? weights[1] : 0f,
                    weights.Count > 2 ? weights[2] : 0f);
                BoneWeights1 = new Vector4(
                    weights.Count > 3 ? weights[3] : 0f,
                    weights.Count > 4 ? weights[4] : 0f,
                    weights.Count > 5 ? weights[5] : 0f,
                    weights.Count > 6 ? weights[6] : 0f);

                BoneIndices0 = new Vector4(
                    boneIndices.Count > 0 ? boneIndices[0] : 0,
                    boneIndices.Count > 1 ? boneIndices[1] : 0,
                    boneIndices.Count > 2 ? boneIndices[2] : 0,
                    boneIndices.Count > 3 ? boneIndices[3] : 0);
                BoneIndices1 = new Vector4(
                    boneIndices.Count > 4 ? boneIndices[4] : 0,
                    boneIndices.Count > 5 ? boneIndices[5] : 0,
                    boneIndices.Count > 6 ? boneIndices[6] : 0,
                    boneIndices.Count > 7 ? boneIndices[7] : 0);
            }
        }
    }
}
