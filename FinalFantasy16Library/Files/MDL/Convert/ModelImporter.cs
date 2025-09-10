using System.Numerics;
using FinalFantasy16Library.Files.MDL.Helpers;
using FinalFantasy16Library.Utils;

using IONET;
using IONET.Core.Model;

using static FinalFantasy16Library.Files.MDL.Helpers.MdlBufferHelper;

namespace FinalFantasy16Library.Files.MDL.Convert;

public class ModelImporter
{
    private List<string> _vertexSetCompare = [];

    // For bones not in base MDL file, map bone names to their generated indices
    private Dictionary<string, int> _generatedBoneIndices = new Dictionary<string, int>();
    private int _nextBoneIndex = 0;
    private List<GeneratedJointData> _generatedJoints = new List<GeneratedJointData>();

    public IReadOnlyList<GeneratedJointData> GeneratedJoints => _generatedJoints;

    public ModelImporter()
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

    public void Import(MdlFile mdlFile, string path, bool clearExistingMeshes = true, ProgressTracker progress = null)
    {
        progress?.SetProgress(10, "Loading model file");

        var scene = IOManager.LoadScene(path, new ImportSettings());
        var model = scene.Models[0];

        progress?.SetProgress(50, "Setting up mdl data");

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

        using (var vWriter = new BinaryWriter(vBuffer))
        using (var idxWriter = new BinaryWriter(idxBuffer))
        {
            MdlLODModelInfo modelInfo = new MdlLODModelInfo();
            modelInfo.MeshIndex = (ushort)mdlFile.MeshInfos.Count;

            List<MdlMeshInfo> meshes = [];

            int index = 0;
            foreach (var iomesh in model.Meshes)
            {
                if (iomesh.Polygons.Count == 0)
                    continue;

                var iomaterial = scene.Materials.FirstOrDefault(x => x.Name == iomesh.Polygons[0].MaterialName);
                string mat = "";
                if (iomaterial != null)
                    mat = iomaterial.Label ?? iomaterial.Name;

                progress?.SetProgress(100 * (float)index / model.Meshes.Count, $"Loading mesh ");

                MdlMeshInfo mesh = ImportMesh(mdlFile, iomesh, mat, vWriter, idxWriter);
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

            progress?.SetProgress(80, "Compressing buffers");

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
        BinaryWriter vWriter, BinaryWriter idxWriter)
    {
        MdlMeshInfo mesh = new MdlMeshInfo();

        var max_bone_count = iomesh.Vertices.Max(x => x.Envelope.Weights.Count);

        //Channel flags
        if (iomesh.HasUVSet(0)) mesh.TexCoordSetFlag |= MdlMeshTexCoordFlags.USE_UV0;
        if (iomesh.HasUVSet(1)) mesh.TexCoordSetFlag |= MdlMeshTexCoordFlags.USE_UV1;
        if (iomesh.HasUVSet(2)) mesh.TexCoordSetFlag |= MdlMeshTexCoordFlags.USE_UV2;
        if (iomesh.HasUVSet(3)) mesh.TexCoordSetFlag |= MdlMeshTexCoordFlags.USE_UV3;

        //Second flag, unsure what this does
        mesh.Flag2 = 0;

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
                Color = iomesh.HasColorSet(0) ? vtx.Colors[0] : null,
            };

            List<int> boneIndices = [];
            List<float> boneWeights = [];

            foreach (var env in vtx.Envelope.Weights)
            {
                // Index is now generated by GetBoneIndex method
                int index = GetBoneIndex(env.BoneName, mdlFile, iomesh);

                // Track which bones are getting extended indices
                if (!mdlFile.JointNames.Contains(env.BoneName) && !extended_bones.Contains(env.BoneName))
                {
                    extended_bones.Add(env.BoneName);
                }

                boneIndices.Add(index);
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

        bool hasUnkAttr24 = false;
        bool hasUnkAttr8 = false;
        bool hasUnkAttr9 = false;
        foreach (var vertex in vertices)
        {
            if (vertex.TexCoord0 is not null) hasTexCoords[0] = true;
            if (vertex.TexCoord1 is not null) hasTexCoords[1] = true;
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

            if (hasUnkAttr24)
                AddAttribute(MdlVertexSemantic.TEXCOORD_13_UNK, EncodingFormat.HALFFLOATx4, ref offset2, 1);
            if (hasUnkAttr8)
                AddAttribute(MdlVertexSemantic.COLOR_5, EncodingFormat.UINT8x4, ref offset2, 1);
            if (hasUnkAttr9)
                AddAttribute(MdlVertexSemantic.COLOR_6, EncodingFormat.UNORM8x4, ref offset2, 1);
        }

        return vertexAttributes;
    }
}