using System.Numerics;
using FinalFantasy16Library.Files.MDL.Helpers;
using FinalFantasy16Library.Files.SKL;
using FinalFantasy16Library.Utils;

using IONET;
using IONET.Core;
using IONET.Core.Model;
using IONET.Core.Skeleton;

namespace FinalFantasy16Library.Files.MDL.Convert;

/// <summary>
/// Handles exporting mdl data with an optional skeleton file given using the IONET library.
/// </summary>
public class FaithModelToGLTFConverter
{
    /// <summary>
    /// Exports the mdl and skeleton data with a given file path.
    /// Supported output: .dae, .obj, .gltf, .glb
    /// </summary>
    public static void Convert(MdlFile mdlFile, List<SklFile> skeletons, string path, int lod = 0)
    {
        IOModel iomodel = new IOModel();

        List<IOMaterial> materials = new List<IOMaterial>();
        for (int i = 0; i < mdlFile.MaterialNames.Count; i++)
        {
            IOMaterial material = new IOMaterial() { Name = mdlFile.MaterialNames[i], Label = mdlFile.MaterialNames[i] };
            materials.Add(material);
        }

        for (int i = 0; i < mdlFile.LODModels.Count; i++)
        {
            if (i != lod)
                continue;

            var vertexBuffer = mdlFile.vBuffers[i];
            var indexBuffer = mdlFile.idxBuffers[i];
            var lodModel = mdlFile.LODModels[i];

            Memory<byte> decompressedVbo = vertexBuffer.GetDecompressedData(lodModel.DecompVertexBuffSize);
            Memory<byte> decompressedIbo = indexBuffer.GetDecompressedData(lodModel.DecompIdxBuffSize);

            for (int j = 0; j < lodModel.MeshCount; j++)
            {
                Console.WriteLine($"[{j+1}/{lodModel.MeshCount}] Loading Mesh {j} LOD {i}");

                var mesh = mdlFile.MeshInfos[j + lodModel.MeshIndex];

                
                IOMesh iomesh = new IOMesh();

                string materialName = mdlFile.MaterialNames[mesh.MaterialID];
                if (!string.IsNullOrWhiteSpace(materialName))
                {
                    if (materialName.StartsWith("m_"))
                        iomesh.Name = materialName.Substring(2);
                    else
                        iomesh.Name = materialName;
                }
                else
                    iomesh.Name = $"LOD{i}_Mesh{j}";

                iomodel.Meshes.Add(iomesh);

                var attributeSet = mdlFile.AttributeSets[mesh.FlexVertexInfoID];
                var attributes = mdlFile.Attributes.GetRange(attributeSet.Idx, attributeSet.Count);
                foreach (var attr in attributes)
                    Console.WriteLine($"- {attr}");

                var vertices = MdlBufferHelper.LoadVertices(mdlFile, mesh, decompressedVbo.Span);
                var x = vertices.FindIndex(e => e.TexCoord1 is not null);
                for (int k = 0; k < vertices.Count; k++)
                {
                    MdlBufferHelper.Vertex? v = vertices[k];
                    IOVertex iovertex = new IOVertex();
                    iovertex.Position = v.Position;
                    if (v.Normal is not null)
                    {
                        iovertex.Normal = v.Normal.Value;
                        iomesh.HasNormals = true;
                    }

                    if (v.Tangent is not null)
                    {
                        iovertex.Tangent = new Vector3(v.Tangent.Value.X, v.Tangent.Value.Y, v.Tangent.Value.Z);
                        iomesh.HasTangents = true;
                    }

                    if (v.Binormal is not null)
                    {
                        iovertex.Binormal = new Vector3(v.Binormal.Value.X, v.Binormal.Value.Y, v.Binormal.Value.Z);
                        iomesh.HasBitangents = true;
                    }

                    // NOTE: We set the COLOR channel in a custom attribute. Why?
                    // Because Blender >=4.1 (i've checked) drops the alpha channel, at least it looks like.
                    // This causes clive's face (c1001/f0103 & potentially others) to appear.. aged
                    if (v.Color is not null)
                        iovertex.SetCustom($"_{MdlVertexSemantic.COLOR_0}", v.Color);

                    if (v.UnknownColor1Attr is not null)  // Head/Hair model
                        iovertex.SetCustom($"_{MdlVertexSemantic.COLOR_1}", v.UnknownColor1Attr);

                    if (v.UnknownColor5Attr is not null) // All but map models
                        iovertex.SetCustom($"_{MdlVertexSemantic.COLOR_5}", v.UnknownColor5Attr);
                    if (v.UnknownColor6Attr is not null) // All but map models
                        iovertex.SetCustom($"_{MdlVertexSemantic.COLOR_6}", v.UnknownColor6Attr);
                    if (v.UnknownColor7Attr is not null) // Head/Hair model
                        iovertex.SetCustom($"_{MdlVertexSemantic.COLOR_7}", v.UnknownColor7Attr);

                    if (v.UnkTexcoord4Attr is not null) // Face model
                        iovertex.SetCustom($"_{MdlVertexSemantic.TEXCOORD_4}", v.UnkTexcoord4Attr);
                    if (v.UnkTexcoord5Attr is not null) // Face model
                        iovertex.SetCustom($"_{MdlVertexSemantic.TEXCOORD_5}", v.UnkTexcoord5Attr);
                    if (v.UnkTexcoord8Attr is not null) // Face model
                        iovertex.SetCustom($"_{MdlVertexSemantic.TEXCOORD_8}", v.UnkTexcoord8Attr);
                    if (v.UnkTexcoord9Attr is not null) // Face model
                        iovertex.SetCustom($"_{MdlVertexSemantic.TEXCOORD_9}", v.UnkTexcoord9Attr);
                    if (v.UnkTexcoord13Attr is not null)
                        iovertex.SetCustom($"_{MdlVertexSemantic.TEXCOORD_13}", v.UnkTexcoord13Attr);

                    var boneIndices = v.GetBoneIndices();
                    var boneWeights = v.GetBoneWeights();

                    for (int e = 0; e < boneIndices.Count; e++)
                    {
                        var idx = boneIndices[e];
                        if (boneWeights[e] == 0) //skip unrigged bones
                            continue;

                        iovertex.Envelope.Weights.Add(new IOBoneWeight()
                        {
                            BoneName = mdlFile.JointNames[idx],
                            Weight = boneWeights[e],
                        });
                    }

                    if (mesh.TexCoordSetFlag.HasFlag(MdlMeshTexCoordFlags.USE_UV0) && v.TexCoord0 is not null)
                        iovertex.SetUV(v.TexCoord0.Value.X, v.TexCoord0.Value.Y, 0);
                    if (mesh.TexCoordSetFlag.HasFlag(MdlMeshTexCoordFlags.USE_UV1) && v.TexCoord1 is not null)
                        iovertex.SetUV(v.TexCoord1.Value.X, v.TexCoord1.Value.Y, 1);
                    if (mesh.TexCoordSetFlag.HasFlag(MdlMeshTexCoordFlags.USE_UV2) && v.TexCoord2 is not null)
                        iovertex.SetUV(v.TexCoord2.Value.Y, v.TexCoord2.Value.X, 2);
                    if (mesh.TexCoordSetFlag.HasFlag(MdlMeshTexCoordFlags.USE_UV3) && v.TexCoord3 is not null)
                        iovertex.SetUV(v.TexCoord3.Value.X, v.TexCoord3.Value.Y, 3);

                    iomesh.Vertices.Add(iovertex);
                }

                if (iomesh.Vertices.Count == 0)
                    continue;

                IOPolygon poly = new IOPolygon();
                iomesh.Polygons.Add(poly);

                poly.MaterialName = materials[mesh.MaterialID].Name;
                poly.Indicies.AddRange(MdlBufferHelper.LoadIndices(mesh, decompressedIbo.Span));
            }
        }

        List<IOBone> bones = [];

        foreach (var skelFile in skeletons)
        {
            for (int i = 0; i < skelFile?.m_Skeleton.m_bones.Count; i++)
            {
                var name = skelFile.m_Skeleton.m_bones[i].m_name;
                var transform = skelFile.m_Skeleton.m_referencePose[i];

                //Dupe bone, skip.
                if (bones.Any(x => x.Name == name))
                    continue;

                bones.Add(new IOBone()
                {
                    Name = name,
                    Translation = new Vector3(
                        transform.m_translation.X,
                        transform.m_translation.Y,
                        transform.m_translation.Z),
                    Rotation = new Quaternion(
                        transform.m_rotation.X,
                        transform.m_rotation.Y,
                        transform.m_rotation.Z,
                        transform.m_rotation.W),
                    Scale = new Vector3(
                        transform.m_scale.X,
                        transform.m_scale.Y,
                        transform.m_scale.Z),
                });

                //hack
                if (float.IsNaN(bones[i].RotationEuler.X) ||
                    float.IsNaN(bones[i].RotationEuler.Y) ||
                    float.IsNaN(bones[i].RotationEuler.Z))
                    bones[i].RotationEuler = new Vector3(0);
            }
        }

        //Alterate skeleton if none providied
        if (skeletons.Count == 0)
        {
            foreach (var joint in mdlFile.JointNames)
            {
                iomodel.Skeleton.RootBones.Add(new IOBone()
                {
                    Name = joint,
                });
            }
        }

        foreach (var skelFile in skeletons)
        {
            for (int i = 0; i < skelFile.m_Skeleton.m_parentIndices.Count; i++)
            {
                string boneName = skelFile.m_Skeleton.m_bones[i].m_name;
                var bone = bones.FirstOrDefault(x => x.Name == boneName);

                var parentIdx = skelFile.m_Skeleton.m_parentIndices[i];
                if (parentIdx != -1)
                {
                    var parent = bones.FirstOrDefault(x => x.Name == skelFile.m_Skeleton.m_bones[parentIdx].m_name);
                    bones[parentIdx].AddChild(bone);
                }
                else
                    iomodel.Skeleton.RootBones.Add(bone);
            }
        }

        foreach (var skelFile in skeletons)
        {
            //Add empties for bones not in the skel file for some reason
            foreach (var joint in mdlFile.JointNames.Where(x => !skelFile.m_Skeleton.m_bones.Any(z => z.m_name != x)))
            {
                iomodel.Skeleton.RootBones.Add(new IOBone()
                {
                    Name = joint,
                });
            }
        }

        Console.WriteLine("Exporting Scene");

        IOScene scene = new IOScene();
        scene.Models.Add(iomodel);

        scene.Materials.AddRange(materials);

        IOManager.ExportScene(scene, path, new ExportSettings()
        {
            Optimize = false,
        });
    }
}
