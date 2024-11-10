using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using CafeLibrary.ff16;
using FinalFantasy16Library.Utils;
using IONET;
using IONET.Core;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using Toolbox.Core;

namespace MdlTest.ff16
{
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

                byte[] decompressedVbo = vertexBuffer.GetDecompressedData(lodModel.DecompVertexBuffSize);
                byte[] decompressedIbo = indexBuffer.GetDecompressedData(lodModel.DecompIdxBuffSize);

                for (int j = 0; j < lodModel.MeshCount; j++)
                {
                    progress?.SetProgress(100 * (j / (float)lodModel.MeshCount), $"Loading Mesh {j} LOD {i}");

                    var mesh = mdlFile.MeshInfos[j + lodModel.MeshIndex];

                    IOMesh iomesh = new IOMesh();
                    iomesh.Name = $"LOD{i}_Mesh{j}";
                    iomodel.Meshes.Add(iomesh);

                    var attributeSet = mdlFile.AttributeSets[mesh.AttributeSetIdx];
                    var attributes = mdlFile.Attributes.GetRange(attributeSet.Idx, attributeSet.Count);

                    bool hasVertexColor0 = attributes.Any(x => x.Type == MdlFile.AttributeType.Color0);
                    bool hasAttr8  = attributes.Any(x => x.Type == MdlFile.AttributeType.UnknownAttr8);
                    bool hasAttr9  = attributes.Any(x => x.Type == MdlFile.AttributeType.UnknownAttr9);
                    bool hasAttr24 = attributes.Any(x => x.Type == MdlFile.AttributeType.UnknownAttr24);

                    var vertices = MdlBufferHelper.LoadVertices(mdlFile, mesh, decompressedVbo);

                    foreach (var v in vertices)
                    {
                        IOVertex iovertex = new IOVertex()
                        {
                            Position = new Vector3(v.Position.X, v.Position.Y, v.Position.Z),
                            Normal = new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z),
                            Tangent = new Vector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z),
                        };

                        var boneIndices = v.GetBoneIndices();
                        var boneWeights = v.GetBoneWeights();

                        for (int e = 0 ; e < boneIndices.Count; e++)
                        {
                            var idx = boneIndices[e];
                            if (boneWeights[e] == 0) //skip unrigged bones
                                continue;

                            iovertex.Envelope.Weights.Add(new IONET.Core.IOBoneWeight()
                            {
                                BoneName = mdlFile.JointNames[idx],
                                Weight = boneWeights[e],
                            });
                        }

                        if ((mesh.TexCoordSetFlag & 1) != 0) iovertex.SetUV(v.TexCoord01.X, v.TexCoord01.Y, 0);
                        if ((mesh.TexCoordSetFlag & 2) != 0) iovertex.SetUV(v.TexCoord01.Z, v.TexCoord01.W, 1);
                        if ((mesh.TexCoordSetFlag & 4) != 0) iovertex.SetUV(v.TexCoord23.X, v.TexCoord23.Y, 2);
                        if ((mesh.TexCoordSetFlag & 8) != 0) iovertex.SetUV(v.TexCoord23.Z, v.TexCoord23.W, 3);

                        if (hasVertexColor0) iovertex.SetColor(v.Color.X, v.Color.Y, v.Color.Z, v.Color.W);

                        iomesh.Vertices.Add(iovertex);
                    }

                    if (iomesh.Vertices.Count == 0)
                    {
                        continue;
                    }

                    IOPolygon poly = new IOPolygon();
                    iomesh.Polygons.Add(poly);

                    poly.MaterialName = materials[mesh.MaterialID].Name;
                    poly.Indicies.AddRange(MdlBufferHelper.LoadIndices(mesh, decompressedIbo));
                }
            }

            List<IOBone> bones = new List<IOBone>();

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

            IOManager.ExportScene(scene, path, new ExportSettings());
        }
    }
}
