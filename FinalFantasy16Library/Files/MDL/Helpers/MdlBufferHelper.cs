using System.Numerics;

using Syroot.BinaryData;
using Syroot.BinaryData.Memory;
using FinalFantasy16Library.Utils;

namespace FinalFantasy16Library.Files.MDL.Helpers;

public class MdlBufferHelper
{
    public static List<Vertex> LoadVertices(MdlFile mdlFile, MdlMeshInfo subMesh, Span<byte> buffer)
    {
        List<Vertex> vertices = [];

        var reader = new SpanReader(buffer);

        var setIndex = subMesh.FlexVertexInfoID;
        var attributeSet = mdlFile.AttributeSets[setIndex];
        var attributes = mdlFile.Attributes.GetRange(attributeSet.Idx, attributeSet.Count);

        for (int v = 0; v < subMesh.VertexCount; v++)
        {
            Vertex vertex = new Vertex();
            vertices.Add(vertex);

            for (int aIdx = 0; aIdx < attributeSet.Count; aIdx++)
            {
                var attribute = attributes[aIdx];

                var ofs = subMesh.BufferOffsets[attribute.BufferIdx] + v * subMesh.Strides[attribute.BufferIdx] + attribute.Offset;
                reader.Position = (int)ofs;

                switch (attribute.Type)
                {
                    case MdlVertexSemantic.POSITION: vertex.Position = ReadVector3(ref reader, attribute); break;
                    case MdlVertexSemantic.TEXCOORD_10_NORMAL: vertex.Normal = ReadVector3(ref reader, attribute); break;
                    case MdlVertexSemantic.TEXCOORD_11_TANGENT: vertex.Tangent = ReadVector4(ref reader, attribute); break;
                    case MdlVertexSemantic.TEXCOORD_12_BITANGENT: vertex.Binormal = ReadVector4(ref reader, attribute); break;
                    case MdlVertexSemantic.TEXCOORD_0:
                        if (attribute.Format == EncodingFormat.FLOATx4 || attribute.Format == EncodingFormat.HALFFLOATx4)
                        {
                            var vec = ReadVector4(ref reader, attribute);
                            vertex.TexCoord0 = new Vector2(vec.X, vec.Y);
                            vertex.TexCoord1 = new Vector2(vec.Z, vec.W);
                        }
                        else
                            vertex.TexCoord0 = ReadVector2(ref reader, attribute);
                        break;
                    case MdlVertexSemantic.TEXCOORD_1:
                        if (attribute.Format == EncodingFormat.FLOATx4 || attribute.Format == EncodingFormat.HALFFLOATx4)
                        {
                            var vec = ReadVector4(ref reader, attribute);
                            vertex.TexCoord2 = new Vector2(vec.X, vec.Y);
                            vertex.TexCoord3 = new Vector2(vec.Z, vec.W);
                        }
                        else
                            vertex.TexCoord2 = ReadVector2(ref reader, attribute);
                        break;
                    case MdlVertexSemantic.BLENDINDICES_0: vertex.BoneIndices0 = ReadVector4(ref reader, attribute); break;
                    case MdlVertexSemantic.BLENDWEIGHT_0:
                        vertex.BoneWeights0 = ReadVector4(ref reader, attribute);
                        break;
                    case MdlVertexSemantic.BLENDINDICES_1:
                        vertex.BoneIndices1 = ReadVector4(ref reader, attribute);
                        break;
                    case MdlVertexSemantic.BLENDWEIGHT_1: vertex.BoneWeights1 = ReadVector4(ref reader, attribute); break;

                    case MdlVertexSemantic.COLOR_0: vertex.Color = ReadVector4(ref reader, attribute); break;

                    // Undocumented ones
                    case MdlVertexSemantic.TEXCOORD_4: vertex.UnkTexcoord4Attr = ReadVector4(ref reader, attribute); break;
                    case MdlVertexSemantic.TEXCOORD_5: vertex.UnkTexcoord5Attr = ReadVector4(ref reader, attribute); break;
                    case MdlVertexSemantic.TEXCOORD_8: vertex.UnkTexcoord8Attr = ReadVector4(ref reader, attribute); break;
                    case MdlVertexSemantic.TEXCOORD_9: vertex.UnkTexcoord9Attr = ReadVector4(ref reader, attribute); break;
                    case MdlVertexSemantic.TEXCOORD_13: vertex.UnkTexcoord9Attr = ReadVector4(ref reader, attribute); break;
                    case MdlVertexSemantic.COLOR_5: vertex.UnknownColor5Attr = ReadVector4(ref reader, attribute); break;
                    case MdlVertexSemantic.COLOR_6: vertex.UnknownColor6Attr = ReadVector4(ref reader, attribute); break;
                    
                    default:
                        throw new NotSupportedException($"Vertex Semantic {attribute.Type} not yet supported");
                }
            }
        }

        return vertices;
    }

    public static List<int> LoadIndices(MdlMeshInfo subMesh, Span<byte> indexBuffer)
    {
        List<int> indices = [];
        SpanReader reader = new SpanReader(indexBuffer);

        reader.Position = (int)subMesh.FaceIndicesOffset * 2;
        for (int i = 0; i < subMesh.FaceIndexCount; i++)
            indices.Add(reader.ReadUInt16());

        return indices;
    }

    public static void WriteIndicesBuffer(MdlMeshInfo subMesh, List<ushort> indices, BinaryStream writer)
    {
        subMesh.FaceIndicesOffset = (uint)writer.BaseStream.Position / 2;
        subMesh.FaceIndexCount = (uint)indices.Count;

        foreach (var index in indices)
            writer.Write(index);
    }

    public static void WriteVertexBuffer(MdlFile mdlFile,
        MdlMeshInfo mesh, List<Vertex> vertices, BinaryStream writer)
    {
        mesh.VertexCount = (ushort)vertices.Count;

        var setIndex = mesh.FlexVertexInfoID;
        var attributeSet = mdlFile.AttributeSets[setIndex];

        //Compute stride via attribute sizes
        Array.Clear(mesh.Strides);
        for (int aIdx = 0; aIdx < attributeSet.Count; aIdx++)
        {
            var attribute = mdlFile.Attributes[attributeSet.Idx + aIdx];
            mesh.Strides[attribute.BufferIdx] += (byte)attribute.Size;
        }

        for (int bufferIdx = 0; bufferIdx < mesh.UsedBufferCount; bufferIdx++)
        {
            mesh.BufferOffsets[bufferIdx] = (uint)writer.BaseStream.Position;
            for (int v = 0; v < vertices.Count; v++)
            {
                for (int aIdx = 0; aIdx < attributeSet.Count; aIdx++)
                {
                    MdlFlexVertexAttribute attribute = mdlFile.Attributes[attributeSet.Idx + aIdx];
                    if (attribute.BufferIdx != bufferIdx)
                        continue;

                    var ofs = mesh.BufferOffsets[bufferIdx] + v * mesh.Strides[attribute.BufferIdx] + attribute.Offset;
                    writer.BaseStream.Seek(ofs, SeekOrigin.Begin);

                    // NOTE: The game has a BITANGENT component, but GLTF doesn't.
                    // We merely skip it.
                    switch (attribute.Type)
                    {
                        case MdlVertexSemantic.POSITION: WriteVector(writer, vertices[v].Position, attribute); break;
                        case MdlVertexSemantic.TEXCOORD_10_NORMAL: WriteVector(writer, vertices[v].Normal, attribute); break;
                        case MdlVertexSemantic.TEXCOORD_11_TANGENT: WriteVector(writer, vertices[v].Tangent, attribute); break;
                        case MdlVertexSemantic.TEXCOORD_12_BITANGENT: WriteVector(writer, vertices[v].Binormal, attribute); break;

                        case MdlVertexSemantic.TEXCOORD_0:
                            if (attribute.Format == EncodingFormat.FLOATx4 || attribute.Format == EncodingFormat.HALFFLOATx4)
                            {
                                Vector2? uv1 = vertices[v].TexCoord0;
                                Vector2? uv2 = vertices[v].TexCoord1;
                                WriteVector(writer, new Vector4(uv1.Value.X, uv1.Value.Y, uv2.Value.X, uv2.Value.Y), attribute);
                            }
                            else if (attribute.Format == EncodingFormat.FLOATx2)
                                WriteVector(writer, vertices[v].TexCoord0, attribute);
                            else
                                throw new NotSupportedException($"MDL: Unsupported TexCoord0 format type {attribute.Format}");
                            break;

                        case MdlVertexSemantic.TEXCOORD_1:
                            if (attribute.Format == EncodingFormat.FLOATx4 || attribute.Format == EncodingFormat.HALFFLOATx4)
                            {
                                Vector2? uv3 = vertices[v].TexCoord2;
                                Vector2? uv4 = vertices[v].TexCoord3;
                                WriteVector(writer, new Vector4(uv3.Value.X, uv3.Value.Y, uv4.Value.X, uv4.Value.Y), attribute);
                            }
                            else if (attribute.Format == EncodingFormat.FLOATx2)
                                WriteVector(writer, vertices[v].TexCoord2, attribute);
                            else
                                throw new NotSupportedException($"MDL: Unsupported TexCoord1 format type {attribute.Format}");
                            break;

                        case MdlVertexSemantic.BLENDINDICES_0: WriteVector(writer, vertices[v].BoneIndices0, attribute); break;
                        case MdlVertexSemantic.BLENDWEIGHT_0: WriteVector(writer, vertices[v].BoneWeights0, attribute); break;
                        case MdlVertexSemantic.BLENDINDICES_1:
                            if (vertices[v].BoneIndices1 is not null)
                            {
                                WriteVector(writer, vertices[v].BoneIndices1, attribute);
                            }
                            else if (vertices[v].BoneIndices0 is not null)
                            {
                                // If BLENDINDICES0 is empty, the game simply repeats the first byte to the bytes of BLENDINDICES1.
                                var bones0 = vertices[v].BoneIndices0!.Value;
                                WriteVector(writer, new Vector4(bones0.X), attribute);
                            }
                            else
                                WriteVector(writer, vertices[v].BoneIndices1, attribute);

                            break;
                        case MdlVertexSemantic.BLENDWEIGHT_1: WriteVector(writer, vertices[v].BoneWeights1, attribute); break;
                        case MdlVertexSemantic.COLOR_5: WriteVector(writer, vertices[v].UnknownColor5Attr, attribute); break;
                        case MdlVertexSemantic.COLOR_6: WriteVector(writer, vertices[v].UnknownColor6Attr, attribute); break;
                        case MdlVertexSemantic.TEXCOORD_4: WriteVector(writer, vertices[v].UnkTexcoord4Attr, attribute); break;
                        case MdlVertexSemantic.TEXCOORD_5: WriteVector(writer, vertices[v].UnkTexcoord5Attr, attribute); break;
                        case MdlVertexSemantic.TEXCOORD_8: WriteVector(writer, vertices[v].UnkTexcoord8Attr, attribute); break;
                        case MdlVertexSemantic.TEXCOORD_9: WriteVector(writer, vertices[v].UnkTexcoord9Attr, attribute); break;
                        case MdlVertexSemantic.TEXCOORD_13: WriteVector(writer, vertices[v].UnkTexcoord13Attr, attribute); break;
                        case MdlVertexSemantic.COLOR_0: WriteVector(writer, vertices[v].Color, attribute); break;
                        default:
                            throw new Exception($"Attribute {attribute.Type} not supported!");
                    }
                }
            }
        }
    }

    #region Read Vectors


    static Vector2 ReadVector2(ref SpanReader reader, MdlFlexVertexAttribute attr)
    {
        if (attr.Format == EncodingFormat.FLOATx2)
        {
            return new Vector2(
                 reader.ReadSingle(),
                 reader.ReadSingle());
        }
        else if (attr.Format == EncodingFormat.HALFFLOATx2)
        {
            return new Vector2(
                 (float)reader.ReadHalf(),
                 (float)reader.ReadHalf());
        }
        else
            throw new Exception($"Unsupported format {attr.Format}");
    }

    static Vector3 ReadVector3(ref SpanReader reader, MdlFlexVertexAttribute attr)
    {
        if (attr.Format == EncodingFormat.HALFFLOATx4)
        {
            var v = new Vector3(
                 (float)reader.ReadHalf(),
                 (float)reader.ReadHalf(),
                 (float)reader.ReadHalf());
            reader.ReadUInt16();
            return v;
        }
        else if (attr.Format == EncodingFormat.FLOATx3)
        {
            return new Vector3(
             reader.ReadSingle(),
             reader.ReadSingle(),
             reader.ReadSingle());
        }
        else
            throw new Exception($"Unsupported format {attr.Format}");
    }

    static Vector4 ReadVector4(ref SpanReader reader, MdlFlexVertexAttribute attr)
    {
        if (attr.Format == EncodingFormat.HALFFLOATx4)
        {
            var v = new Vector4(
                 (float)reader.ReadHalf(),
                 (float)reader.ReadHalf(),
                 (float)reader.ReadHalf(),
                 (float)reader.ReadHalf());
            return v;
        }
        else if (attr.Format == EncodingFormat.FLOATx2)
        {
            return new Vector4(reader.ReadSingle(), reader.ReadSingle(), 0, 0);
        }
        else if (attr.Format == EncodingFormat.HALFFLOATx2)
        {
            return new Vector4((float)reader.ReadHalf(), (float)reader.ReadHalf(), 0, 0);
        }
        else if (attr.Format == EncodingFormat.FLOATx4)
        {
            return new Vector4(
                 reader.ReadSingle(),
                 reader.ReadSingle(),
                 reader.ReadSingle(),
                 reader.ReadSingle());
        }
        else if (attr.Format == EncodingFormat.UNORM8x4)
        {
            return new Vector4(
                 (float)reader.ReadByte() / byte.MaxValue,
                 (float)reader.ReadByte() / byte.MaxValue,
                 (float)reader.ReadByte() / byte.MaxValue,
                 (float)reader.ReadByte() / byte.MaxValue);
        }
        else if (attr.Format == EncodingFormat.UINT8x4)
        {
            return new Vector4(
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte());
        }
        else if (attr.Format == EncodingFormat.UINT16x4)
        {
            return new Vector4(
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16());
        }
        else
            throw new Exception($"Unsupported format {attr.Format}");
    }

    #endregion

    #region Write Vectors

    static void WriteVector(BinaryStream writer, Vector3? value, MdlFlexVertexAttribute attr)
    {
        if (value is null)
        {
            if (attr.Type == MdlVertexSemantic.TEXCOORD_10_NORMAL)
                value = new Vector3(0.5f, 0.5f, 0.5f);
            else
                value = Vector3.Zero;
        }

        if (attr.Format == EncodingFormat.HALFFLOATx4)
        {
            writer.WriteHalf((Half)value.Value.X);
            writer.WriteHalf((Half)value.Value.Y);
            writer.WriteHalf((Half)value.Value.Z);
            writer.Write((ushort)0);
        }
        else if (attr.Format == EncodingFormat.FLOATx3)
        {
            writer.WriteSingle(value.Value.X);
            writer.WriteSingle(value.Value.Y);
            writer.WriteSingle(value.Value.Z);
        }
        else
            throw new Exception($"Unsupported format {attr.Format}");
    }

    static void WriteVector(BinaryStream writer, Vector4? value, MdlFlexVertexAttribute attr)
    {
        if (value is null)
        {
            if (attr.Type == MdlVertexSemantic.COLOR_0)
                value = Vector4.One;
            else
                value = Vector4.Zero;
        }

        if (attr.Format == EncodingFormat.HALFFLOATx4)
        {
            writer.WriteHalf((Half)value.Value.X);
            writer.WriteHalf((Half)value.Value.Y);
            writer.WriteHalf((Half)value.Value.Z);
            writer.WriteHalf((Half)value.Value.W);
        }
        else if (attr.Format == EncodingFormat.FLOATx2)
        {
            writer.WriteSingle(value.Value.X);
            writer.WriteSingle(value.Value.Y);
        }
        else if (attr.Format == EncodingFormat.HALFFLOATx2)
        {
            writer.WriteHalf((Half)value.Value.X);
            writer.WriteHalf((Half)value.Value.Y);
        }
        else if (attr.Format == EncodingFormat.FLOATx4)
        {
            writer.WriteSingle(value.Value.X);
            writer.WriteSingle(value.Value.Y);
            writer.WriteSingle(value.Value.Z);
            writer.WriteSingle(value.Value.W);
        }
        else if (attr.Format == EncodingFormat.UNORM8x4)
        {
            writer.WriteByte((byte)(value.Value.X * byte.MaxValue));
            writer.WriteByte((byte)(value.Value.Y * byte.MaxValue));
            writer.WriteByte((byte)(value.Value.Z * byte.MaxValue));
            writer.WriteByte((byte)(value.Value.W * byte.MaxValue));
        }
        else if (attr.Format == EncodingFormat.UINT8x4)
        {
            writer.WriteByte((byte)value.Value.X);
            writer.WriteByte((byte)value.Value.Y);
            writer.WriteByte((byte)value.Value.Z);
            writer.WriteByte((byte)value.Value.W);
        }
        else
            throw new Exception($"Unsupported format {attr.Format}");
    }

    static void WriteVector(BinaryStream writer, Vector2? value, MdlFlexVertexAttribute attr)
    {
        value ??= Vector2.Zero;

        if (attr.Format == EncodingFormat.FLOATx2)
        {
            writer.WriteSingle(value.Value.X);
            writer.WriteSingle(value.Value.Y);
        }
        else if (attr.Format == EncodingFormat.HALFFLOATx2)
        {
            writer.WriteHalf((Half)value.Value.X);
            writer.WriteHalf((Half)value.Value.Y);
        }
        else
            throw new Exception($"Unsupported format {attr.Format}");
    }

    #endregion

    public class Vertex
    {
        public Vector3 Position { get; set; }
        public Vector2? TexCoord0 { get; set; }
        public Vector2? TexCoord1 { get; set; }
        public Vector2? TexCoord2 { get; set; }
        public Vector2? TexCoord3 { get; set; }
        public Vector4? BoneIndices0 { get; set; }
        public Vector4? BoneIndices1 { get; set; }
        public Vector4? BoneWeights0 { get; set; }
        public Vector4? BoneWeights1 { get; set; }

        public Vector3? Normal { get; set; }

        public Vector4? Color { get; set; }

        public Vector4? Tangent { get; set; }
        public Vector4? Binormal { get; set; }

        // c1001/f0103 (clive's face) pretty much has all these.
        public Vector4? UnkTexcoord4Attr { get; set; }
        public Vector4? UnkTexcoord5Attr { get; set; }
        public Vector4? UnkTexcoord8Attr { get; set; }
        public Vector4? UnkTexcoord9Attr { get; set; }
        public Vector4? UnknownColor5Attr { get; set; }
        public Vector4? UnknownColor6Attr { get; set; }
        public Vector4? UnkTexcoord13Attr { get; set; }

        public List<int> GetBoneIndices()
        {
            if (BoneWeights0 is null)
                return [];

            // This is how the shader handles it
            //var boneCount = Math.Min((uint)(BoneWeights0.Value.X * 8 + 0.1), 4);
            var boneCount = (uint)(BoneWeights0.Value.X * 8 + 0.1);

            List<int> boneIndices = [];
            if (boneCount >= 1) boneIndices.Add((int)BoneIndices0.Value.X);
            if (boneCount >= 2) boneIndices.Add((int)BoneIndices0.Value.Y);
            if (boneCount >= 3) boneIndices.Add((int)BoneIndices0.Value.Z);
            if (boneCount >= 4) boneIndices.Add((int)BoneIndices0.Value.W);

            if (BoneWeights1 is not null)
            {
                if (boneCount >= 5) boneIndices.Add((int)BoneIndices1.Value.X);
                if (boneCount >= 6) boneIndices.Add((int)BoneIndices1.Value.Y);
                if (boneCount >= 7) boneIndices.Add((int)BoneIndices1.Value.Z);
                if (boneCount >= 8) boneIndices.Add((int)BoneIndices1.Value.W);
            }

            return boneIndices;
        }

        public List<float> GetBoneWeights()
        {
            if (BoneWeights0 is null)
                return [];  

            // This is how the shader handles it
            // var boneCount = Math.Min((uint)(BoneWeights0.Value.X * 8 + 0.1), 4);
            var boneCount = (uint)(BoneWeights0.Value.X * 8 + 0.1);
            var sum = BoneWeights0.Value.Y + BoneWeights0.Value.Z + BoneWeights0.Value.W;
            if (BoneWeights1 is not null)
                sum += BoneWeights1.Value.X + BoneWeights1.Value.Y + BoneWeights1.Value.Z + BoneWeights1.Value.W;

            List<float> boneWeights = [];
            if (boneCount > 0) boneWeights.Add(1.0f - sum);
            if (boneCount > 1) boneWeights.Add(BoneWeights0.Value.Y);
            if (boneCount > 2) boneWeights.Add(BoneWeights0.Value.Z);
            if (boneCount > 3) boneWeights.Add(BoneWeights0.Value.W);

            if (BoneWeights1 is not null)
            {
                if (boneCount > 4) boneWeights.Add(BoneWeights1.Value.X);
                if (boneCount > 5) boneWeights.Add(BoneWeights1.Value.Y);
                if (boneCount > 6) boneWeights.Add(BoneWeights1.Value.Z);
                if (boneCount > 7) boneWeights.Add(BoneWeights1.Value.W);
            }
            return boneWeights;
        }

        public void SetBoneIndicesWeights(List<int> boneIndices, List<float> weights)
        {
            float boneCount = Math.Min((byte)boneIndices.Count * 32 / 255f, 1.0f);

            BoneWeights0 = new Vector4(boneCount,
                weights.Count > 1 ? weights[1] : 0f,
                weights.Count > 2 ? weights[2] : 0f,
                weights.Count > 3 ? weights[3] : 0f);

            if (weights.Count > 4)
            {
                BoneWeights1 = new Vector4(
                    weights.Count > 4 ? weights[4] : 0f,
                    weights.Count > 5 ? weights[5] : 0f,
                    weights.Count > 6 ? weights[6] : 0f,
                    weights.Count > 7 ? weights[7] : 0f);
            }

            // unused bone indices are set to the first's index
            BoneIndices0 = new Vector4(
                boneIndices.Count > 0 ? boneIndices[0] : boneIndices[0],
                boneIndices.Count > 1 ? boneIndices[1] : boneIndices[0],
                boneIndices.Count > 2 ? boneIndices[2] : boneIndices[0],
                boneIndices.Count > 3 ? boneIndices[3] : boneIndices[0]);

            if (boneIndices.Count > 4)
            {
                BoneIndices1 = new Vector4(
                    boneIndices.Count > 4 ? boneIndices[4] : boneIndices[0],
                    boneIndices.Count > 5 ? boneIndices[5] : boneIndices[0],
                    boneIndices.Count > 6 ? boneIndices[6] : boneIndices[0],
                    boneIndices.Count > 7 ? boneIndices[7] : boneIndices[0]);
            }
        }
    }
}
