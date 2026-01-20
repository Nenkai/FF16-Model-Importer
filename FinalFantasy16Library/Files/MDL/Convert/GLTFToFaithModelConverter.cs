using System.Numerics;
using FinalFantasy16Library.Files.MDL.Helpers;
using FinalFantasy16Library.Utils;

using IONET;
using IONET.Core.Model;

using Syroot.BinaryData;

using static FinalFantasy16Library.Files.MDL.Helpers.MdlBufferHelper;

namespace FinalFantasy16Library.Files.MDL.Convert;

public class GLTFToFaithModelConverter
{
    private List<string> _vertexSetCompare = [];

    // For bones not in base MDL file, map bone names to their generated indices
    private Dictionary<string, int> _generatedBoneIndices = new Dictionary<string, int>();
    private int _nextBoneIndex = 0;
    private List<GeneratedJointData> _generatedJoints = new List<GeneratedJointData>();

    public IReadOnlyList<GeneratedJointData> GeneratedJoints => _generatedJoints;

    public GLTFToFaithModelConverter()
    {
        _generatedBoneIndices = new Dictionary<string, int>();
        _nextBoneIndex = 0;
    }

    private void AddNewJointEntries(MdlFile mdlFile, IOMesh iomesh, string boneName, int generatedIndex)
    {
        // Search for joint data by finding vertex weighted to bone
        var vertex = iomesh.Vertices.FirstOrDefault(v =>
            v.Envelope.Weights.Any(w => w.BoneName == boneName));

        if (vertex == null)
        {
            Console.WriteLine($"WARNING: Could not find any vertices weighted to bone {boneName}");
            return;
        }

        // Get the bone weight data
        var boneWeight = vertex.Envelope.Weights.First(w => w.BoneName == boneName);

        // Create new JointEntry 
        var jointEntry = new JointEntry()
        {
            NameOffset = 0, // Will be calculated during write
            WorldPosition = new Vector3(
                vertex.Position.X,
                vertex.Position.Y,
                vertex.Position.Z
            )
        };

        // Create new JointBounding entry
        var boundingEntry = new MdlJointBounding()
        {
            BoundingMin = new Vector3(
                vertex.Position.X - 1.0f,
                vertex.Position.Y - 1.0f,
                vertex.Position.Z - 1.0f
            ),
            BoundingMax = new Vector3(
                vertex.Position.X + 1.0f,
                vertex.Position.Y + 1.0f,
                vertex.Position.Z + 1.0f
            )
        };

        // Store the generated data with its index
        _generatedJoints.Add(new GeneratedJointData(boneName, generatedIndex, jointEntry, boundingEntry));
    }

    private int GetBoneIndex(string boneName, MdlFile mdlFile, IOMesh iomesh)
    {
        // First check if we already have a mapping
        if (_generatedBoneIndices.ContainsKey(boneName))
        {
            return _generatedBoneIndices[boneName];
        }

        // Check if bone exists in the base MDL's joint names
        int originalIndex = mdlFile.JointNames.IndexOf(boneName);
        if (originalIndex != -1)
        {
            _generatedBoneIndices[boneName] = originalIndex;
            _nextBoneIndex = Math.Max(_nextBoneIndex, originalIndex + 1);
            return originalIndex;
        }

        // Generate a new index for bone
        int newIndex = _nextBoneIndex++;
        _generatedBoneIndices[boneName] = newIndex;

        // Create and store joint data for bone
        AddNewJointEntries(mdlFile, iomesh, boneName, newIndex);

        return newIndex;
    }

    public static void ClearMeshes(MdlFile mdlFile)
    {
        mdlFile.MeshInfos.Clear();
        mdlFile.LODModels.Clear();
        mdlFile.Attributes.Clear();
        mdlFile.AttributeSets.Clear();

        mdlFile.vBuffers = [];
        mdlFile.idxBuffers = [];
    }

    public void AddLOD(MdlFile mdlFile, string path, bool clearExistingMeshes = true)
    {
        Console.WriteLine("Loading model file");

        var scene = IOManager.LoadScene(path, new ImportSettings());
        var model = scene.Models[0];

        Console.WriteLine("Setting up mdl data");

        if (clearExistingMeshes)
            ClearMeshes(mdlFile);

        Console.WriteLine($"Adding LOD {mdlFile.LODModels.Count}");

        if (mdlFile.LODModels.Count >= 8)
            throw new Exception($"Max LOD count present! (> 8)");

        List<ModelBuffer> vertexBuffers = mdlFile.vBuffers.ToList();
        List<ModelBuffer> indexBuffers = mdlFile.idxBuffers.ToList();

        //prepare vertex and index buffer which are shared between meshes in one big buffer
        var vBuffer = new MemoryStream();
        var idxBuffer = new MemoryStream();

        using (var vWriter = new BinaryStream(vBuffer))
        using (var idxWriter = new BinaryStream(idxBuffer))
        {
            MdlLODModelInfo modelInfo = new MdlLODModelInfo();
            modelInfo.MeshIndex = (ushort)mdlFile.MeshInfos.Count;

            List<MdlMeshInfo> meshes = [];

            uint index = 0;
            foreach (var iomesh in model.Meshes)
            {
                if (iomesh.Polygons.Count == 0)
                    continue;

                var iomaterial = scene.Materials.FirstOrDefault(x => x.Name == iomesh.Polygons[0].MaterialName);
                string mat = "";
                if (iomaterial != null)
                    mat = iomaterial.Label ?? iomaterial.Name;

                Console.WriteLine($"Loading mesh {index}");

                MdlMeshInfo mesh = ImportMesh(mdlFile, iomesh, mat, vWriter, idxWriter, index);
                meshes.Add(mesh);
                index++;
            }

            //Prepare LOD mesh
            modelInfo.DecompVertexBuffSize = (uint)vBuffer.Length;
            modelInfo.DecompIdxBuffSize = (uint)idxBuffer.Length;
            modelInfo.DecompIdxBuffSizeMultiplied6 = (uint)idxBuffer.Length * 6u;
            modelInfo.MeshCount = (ushort)model.Meshes.Count;
            mdlFile.LODModels.Add(modelInfo);
            mdlFile.MeshInfos.AddRange(meshes);

            Console.WriteLine("Compressing buffers");

            //Load buffers
            ModelBuffer buffer = new ModelBuffer();
            buffer.CompressedData(vBuffer.ToArray());
            vertexBuffers.Add(buffer);

            ModelBuffer bufferIdx = new ModelBuffer();
            bufferIdx.CompressedData(idxBuffer.ToArray());
            indexBuffers.Add(bufferIdx);
        }

        mdlFile.vBuffers = vertexBuffers.ToArray();
        mdlFile.idxBuffers = indexBuffers.ToArray();
    }


    public MdlMeshInfo ImportMesh(MdlFile mdlFile, IOMesh iomesh, string materialName,
        BinaryStream vWriter, BinaryStream idxWriter, uint index)
    {
        MdlMeshInfo mesh = new MdlMeshInfo();

        var max_bone_count = iomesh.Vertices.Max(x => x.Envelope.Weights.Count);

        //Channel flags
        if (iomesh.HasUVSet(0)) mesh.TexCoordSetFlag |= MdlMeshTexCoordFlags.USE_UV0;
        if (iomesh.HasUVSet(1)) mesh.TexCoordSetFlag |= MdlMeshTexCoordFlags.USE_UV1;
        if (iomesh.HasUVSet(2)) mesh.TexCoordSetFlag |= MdlMeshTexCoordFlags.USE_UV2;
        if (iomesh.HasUVSet(3)) mesh.TexCoordSetFlag |= MdlMeshTexCoordFlags.USE_UV3;

        // This flag is reponsible for allocating a buffer & SRV. It is used as some kind of key into an unordered map too
        // If we leave it to zero, the clive's face model (and presumably any other advanced model) will kind of break (beard will be all dark)
        //
        // Behavior is essentially described as such:
        // if the unordered map contains this
        //   -> allocate buffer & SRV
        // otherwise do nothing
        //
        // It's labeled as "flag" because it is clearly one. there is no apparent behavior other than each unique value
        // will have their own buffer & srv.
        //
        // Therefore, we don't know what to place here. Just use it as an id instead.
        // [OLD NOTE] -> This WILL allocate a buffer and SRV per buffer, but for modding purposes.. it'll be fine.
        // 
        // Turns out this doesn't work. There's probably more involved with the value...
        // For now just 0xFF it lol
        // Useful signature for referencing code:
        // - faith::Graphics::ModelResourceHandle::sub_7FF6B0E2051C / 48 89 5C 24 ? 48 89 74 24 ? 48 89 7C 24 ? 55 41 56 41 57 48 8B EC 48 83 EC ? 83 65 ? ? 44 8B FA
        // - CreateAndInsertUnkGraphicsUnorderedList / 48 89 5C 24 ? 55 56 57 41 54 41 55 41 56 41 57 48 83 EC ? 48 83 B9
        // - faith::Graphics::ModelResourceHandle::RemoveSubmeshBuffers / 48 89 4C 24 ? 53 55 56 57 41 54 41 55 41 56 41 57 48 83 EC ? 48 83 B9
        mesh.Flag2 = 0xFF;

        mesh.MaterialID = 0;
        if (mdlFile.MaterialNames.Contains(materialName))
            mesh.MaterialID = (ushort)mdlFile.MaterialNames.IndexOf(materialName);
        else
            Console.WriteLine($"WARNING: Non matching material {materialName}. First material will be used {mdlFile.MaterialNames[0]}");

        // Replace original missing_bones list with new list called extended_bones
        List<string> extended_bones = [];

        List<Vertex> vertices = [];
        for (int i = 0; i < iomesh.Vertices.Count; i++)
        {
            IOVertex? vtx = iomesh.Vertices[i];
            var vertex = new Vertex()
            {
                Position = vtx.Position,
                Normal = iomesh.HasNormals ? vtx.Normal : null,
                TexCoord0 = iomesh.HasUVSet(0) ? vtx.UVs[0] : null,
                TexCoord1 = iomesh.HasUVSet(1) ? vtx.UVs[1] : null,
                TexCoord2 = iomesh.HasUVSet(2) ? vtx.UVs[2] : null,
                TexCoord3 = iomesh.HasUVSet(3) ? vtx.UVs[3] : null,
                Tangent = iomesh.HasTangents ? new Vector4(vtx.Tangent, 1f) : null,
                Binormal = iomesh.HasBitangents ? new Vector4(vtx.Binormal, 1f) : null,
            };

            if (vertex.Binormal is null && vertex.Normal is not null && vertex.Tangent is not null)
            {
                vertex.Binormal = new Vector4(
                    Vector3.Normalize(Vector3.Cross(vertex.Normal.Value, new Vector3(vertex.Tangent.Value.X, vertex.Tangent.Value.Y, vertex.Tangent.Value.Z))
                                      * vertex.Tangent.Value.W),
                    1.0f
                );
            }

            foreach (var customAttr in vtx.CustomAttributes)
            {
                switch (customAttr.Key)
                {
                    case "_COLOR_0":
                        vertex.Color = (Vector4)customAttr.Value; break;
                    case "_COLOR_1":
                        vertex.UnknownColor1Attr = (Vector4)customAttr.Value; break;
                    case "_COLOR_5":
                        vertex.UnknownColor5Attr = (Vector4)customAttr.Value; break;
                    case "_COLOR_6":
                        vertex.UnknownColor6Attr = (Vector4)customAttr.Value; break;
                    case "_COLOR_7":
                        vertex.UnknownColor7Attr = (Vector4)customAttr.Value; break;
                    case "_TEXCOORD_4":
                        vertex.UnkTexcoord4Attr = (Vector4)customAttr.Value; break;
                    case "_TEXCOORD_5":
                        vertex.UnkTexcoord5Attr = (Vector4)customAttr.Value; break;
                    case "_TEXCOORD_8":
                        vertex.UnkTexcoord8Attr = (Vector4)customAttr.Value; break;
                    case "_TEXCOORD_9":
                        vertex.UnkTexcoord9Attr = (Vector4)customAttr.Value; break;
                    case "_TEXCOORD_13":
                        vertex.UnkTexcoord9Attr = (Vector4)customAttr.Value; break;
                    default:
                        Console.WriteLine($"WARNING: Unsupported vertex semantic {customAttr.Key}.");
                        break;
                }
            }

            List<int> boneIndices = [];
            List<float> boneWeights = [];

            foreach (var env in vtx.Envelope.Weights)
            {
                // Index is now generated by GetBoneIndex method
                int boneIndex = GetBoneIndex(env.BoneName, mdlFile, iomesh);

                // Track which bones are getting extended indices
                if (!mdlFile.JointNames.Contains(env.BoneName) && !extended_bones.Contains(env.BoneName))
                {
                    extended_bones.Add(env.BoneName);
                }

                boneIndices.Add(boneIndex);
                boneWeights.Add(env.Weight);
            }

            if (boneIndices.Count > 0)
                vertex.SetBoneIndicesWeights(boneIndices, boneWeights);

            if (vertex.BoneIndices0 is not null) mesh.BoneSetFlag |= MdlMeshBoneSetFlags.USE_BONESET0;
            if (vertex.BoneIndices1 is not null) mesh.BoneSetFlag |= MdlMeshBoneSetFlags.USE_BONESET1;

            vertices.Add(vertex);
        }

        // Report bones using generated indices
        if (extended_bones.Count > 0)
        {
            Console.WriteLine("Using generated indices for the following bones:");
            foreach (var bone in extended_bones)
            {
                Console.WriteLine($"  - {bone}: Index {_generatedBoneIndices[bone]}");
            }
        }

        var vertexAttributes = CreateFlexVertexAttributes(vertices);
        var key = string.Join(' ', vertexAttributes.Select(x => x.ToString()));

        //Check if the same attribute set has been used or not
        if (!_vertexSetCompare.Contains(key))
        {
            _vertexSetCompare.Add(key);

            //Add attribute set
            var vertexAttributeSet = new MdlFlexVertexInfo()
            {
                Idx = (ushort)mdlFile.Attributes.Count,
                Count = (ushort)vertexAttributes.Count,
            };

            //Add attributes
            mdlFile.Attributes.AddRange(vertexAttributes);
            mdlFile.AttributeSets.Add(vertexAttributeSet);
        }

        mesh.FlexVertexInfoID = (ushort)_vertexSetCompare.IndexOf(key);

        List<ushort> indices = [];
        foreach (var ind in iomesh.Polygons[0].Indicies)
            indices.Add((ushort)ind);

        WriteVertexBuffer(mdlFile, mesh, vertices, vWriter);
        WriteIndicesBuffer(mesh, indices, idxWriter);

        return mesh;
    }

    public static List<MdlFlexVertexAttribute> CreateFlexVertexAttributes(List<Vertex> vertices)
    {
        List<MdlFlexVertexAttribute> vertexAttributes = [];

        bool[] hasTexCoords = new bool[4];
        bool[] hasBoneWeights = new bool[2];
        bool[] hasBoneIndices = new bool[2];
        bool hasNormal = false;
        bool hasTangent = false;
        bool hasBinormal = false;
        bool hasColors = false;

        bool hasColor1Attr = false;
        bool hasColor5Attr = false;
        bool hasColor6Attr = false;
        bool hasColor7Attr = false;

        bool hasTexcoord4Attr = false;
        bool hasTexcoord5Attr = false;
        bool hasTexcoord8Attr = false;
        bool hasTexcoord9Attr = false;

        bool hasTexcoord13Attr = false;
        foreach (var vertex in vertices)
        {
            if (vertex.TexCoord0 is not null) hasTexCoords[0] = true;
            if (vertex.TexCoord1 is not null) 
                hasTexCoords[1] = true;
            if (vertex.TexCoord2 is not null) hasTexCoords[2] = true;
            if (vertex.TexCoord3 is not null) hasTexCoords[3] = true;

            if (vertex.BoneWeights0 is not null) hasBoneWeights[0] = true;
            if (vertex.BoneWeights1 is not null) hasBoneWeights[1] = true;

            if (vertex.BoneIndices0 is not null) hasBoneIndices[0] = true;
            if (vertex.BoneIndices1 is not null) hasBoneIndices[1] = true;

            if (vertex.Normal is not null) hasNormal = true;
            if (vertex.Tangent is not null) hasTangent = true;
            if (vertex.Binormal is not null) hasBinormal = true;
            if (vertex.Color is not null) hasColors = true;

            if (vertex.UnknownColor1Attr is not null) hasColor1Attr = true; // Head/Hair models

            if (vertex.UnknownColor5Attr is not null) hasColor5Attr = true;
            if (vertex.UnknownColor6Attr is not null) hasColor6Attr = true;
            if (vertex.UnknownColor7Attr is not null) hasColor7Attr = true; // Head/Hair models
            if (vertex.UnkTexcoord4Attr is not null) hasTexcoord4Attr = true; // Face models
            if (vertex.UnkTexcoord5Attr is not null) hasTexcoord5Attr = true; // Face models
            if (vertex.UnkTexcoord8Attr is not null) hasTexcoord8Attr = true; // Face models
            if (vertex.UnkTexcoord9Attr is not null) hasTexcoord9Attr = true; // Face models
            if (vertex.UnkTexcoord13Attr is not null) hasTexcoord13Attr = true;
        }

        void AddAttribute(MdlVertexSemantic type, EncodingFormat format, ref byte offset, byte buffer = 0)
        {
            var attr = new MdlFlexVertexAttribute(buffer, offset, type, format);
            vertexAttributes.Add(attr);
            offset += (byte)attr.Size;
        }

        //Buffer 1
        {
            byte offset1 = 0;
            AddAttribute(MdlVertexSemantic.POSITION, EncodingFormat.FLOATx3, ref offset1, 0);

            if (hasTexCoords[0])
            {
                if (hasTexCoords[1])
                    AddAttribute(MdlVertexSemantic.TEXCOORD_0, EncodingFormat.FLOATx4, ref offset1, 0);
                else
                    AddAttribute(MdlVertexSemantic.TEXCOORD_0, EncodingFormat.FLOATx2, ref offset1, 0);
            }

            if (hasTexCoords[2])
            {
                if (hasTexCoords[3])
                    AddAttribute(MdlVertexSemantic.TEXCOORD_1, EncodingFormat.FLOATx4, ref offset1, 0);
                else
                    AddAttribute(MdlVertexSemantic.TEXCOORD_1, EncodingFormat.FLOATx2, ref offset1, 0);
            }

            if (hasBoneWeights[0])
                AddAttribute(MdlVertexSemantic.BLENDWEIGHT_0, EncodingFormat.UNORM8x4, ref offset1, 0);

            if (hasBoneWeights[1])
                AddAttribute(MdlVertexSemantic.BLENDWEIGHT_1, EncodingFormat.UNORM8x4, ref offset1, 0);

            if (hasBoneIndices[0])
                AddAttribute(MdlVertexSemantic.BLENDINDICES_0, EncodingFormat.UINT8x4, ref offset1, 0);

            if (hasBoneIndices[1])
                AddAttribute(MdlVertexSemantic.BLENDINDICES_1, EncodingFormat.UINT8x4, ref offset1, 0);
        }

        //Buffer 2
        {
            byte offset2 = 0;
            if (hasNormal)
                AddAttribute(MdlVertexSemantic.TEXCOORD_10_NORMAL, EncodingFormat.FLOATx3, ref offset2, 1);
            if (hasTangent)
                AddAttribute(MdlVertexSemantic.TEXCOORD_11_TANGENT, EncodingFormat.HALFFLOATx4, ref offset2, 1);
            if (hasBinormal)
                AddAttribute(MdlVertexSemantic.TEXCOORD_12_BITANGENT, EncodingFormat.HALFFLOATx4, ref offset2, 1);
            if (hasColors)
                AddAttribute(MdlVertexSemantic.COLOR_0, EncodingFormat.UNORM8x4, ref offset2, 1);
            if (hasColor1Attr)
                AddAttribute(MdlVertexSemantic.COLOR_1, EncodingFormat.UNORM8x4, ref offset2, 1);

            if (hasTexcoord13Attr)
                AddAttribute(MdlVertexSemantic.TEXCOORD_13, EncodingFormat.HALFFLOATx4, ref offset2, 1);

            // Used by face models
            if (hasTexcoord8Attr)
                AddAttribute(MdlVertexSemantic.TEXCOORD_8, EncodingFormat.UNORM8x4, ref offset2, 1);
            if (hasTexcoord9Attr)
                AddAttribute(MdlVertexSemantic.TEXCOORD_9, EncodingFormat.UINT8x4, ref offset2, 1);
            if (hasTexcoord4Attr)
                AddAttribute(MdlVertexSemantic.TEXCOORD_4, EncodingFormat.UNORM8x4, ref offset2, 1);
            if (hasTexcoord5Attr)
                AddAttribute(MdlVertexSemantic.TEXCOORD_5, EncodingFormat.UINT8x4, ref offset2, 1);

            // Generally always present?
            if (hasColor5Attr)
                AddAttribute(MdlVertexSemantic.COLOR_5, EncodingFormat.UNORM8x4, ref offset2, 1);
            if (hasColor6Attr)
                AddAttribute(MdlVertexSemantic.COLOR_6, EncodingFormat.UINT8x4, ref offset2, 1);

            // Head/hair model
            if (hasColor7Attr)
                AddAttribute(MdlVertexSemantic.COLOR_7, EncodingFormat.FLOATx4, ref offset2, 1);
        }

        return vertexAttributes;
    }
}