using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using FinalFantasy16Library.Files.MDL;
using FinalFantasy16Library.Files.MDL.Helpers;
using FinalFantasy16Library.Files.SKL;
using FinalFantasy16Library.Utils;

using IONET;
using IONET.Core;
using IONET.Core.Model;
using IONET.Core.Skeleton;

using Toolbox.Core;

namespace FinalFantasy16Library.Files.MDL.Convert;

/// <summary>
/// Handles exporting mdl data with an optional skeleton file given using the IONET library.
/// </summary>
public class ModelExporter
{
    /// <summary>
    /// Exports the mdl and skeleton data with a given file path.
    /// Supported output: .dae, .obj, .gltf, .glb
    /// </summary>
    public static void Export(MdlFile mdlFile, List<SkelFile> skeletons, string path, int lod = 0,
        ProgressTracker progress = null)
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
                progress?.SetProgress(100 * (j / (float)lodModel.MeshCount), $"Loading Mesh {j} LOD {i}");

                var mesh = mdlFile.MeshInfos[j + lodModel.MeshIndex];

                IOMesh iomesh = new IOMesh();
                iomesh.Name = $"LOD{i}_Mesh{j}";
                iomodel.Meshes.Add(iomesh);

                var attributeSet = mdlFile.AttributeSets[mesh.FlexVertexInfoID];
                var attributes = mdlFile.Attributes.GetRange(attributeSet.Idx, attributeSet.Count);

                bool hasAttr8 = attributes.Any(x => x.Type == MdlVertexSemantic.COLOR_5);
                bool hasAttr9 = attributes.Any(x => x.Type == MdlVertexSemantic.COLOR_5);
                bool hasAttr24 = attributes.Any(x => x.Type == MdlVertexSemantic.TEXCOORD_13_UNK);

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

                    if (v.Color is not null)
                    {
                        iovertex.SetColor(v.Color.Value.X, v.Color.Value.Y, v.Color.Value.Z, v.Color.Value.W);
                        iomesh.HasColorSet(0);
                    }

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

        progress?.SetProgress(50, $"Exporting Scene");

        IOScene scene = new IOScene();
        scene.Models.Add(iomodel);

        scene.Materials.AddRange(materials);

        IOManager.ExportScene(scene, path, new ExportSettings()
        {
            Optimize = false,
        });
    }
}
