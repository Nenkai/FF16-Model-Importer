using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using CafeLibrary.ff16;
using FinalFantasy16Library.Utils;
using IONET;
using IONET.Core.Model;
using Syroot.BinaryData;

namespace MdlTest.ff16
{
    public class ModelImporter
    {
        public static void Clear(MdlFile mdlFile)
        {
            mdlFile.MeshInfos.Clear();
            mdlFile.LODModels.Clear();
            mdlFile.Attributes.Clear();
            mdlFile.AttributeSets.Clear();

            mdlFile.vBuffers = new MdlFile.Buffer[0];
            mdlFile.idxBuffers = new MdlFile.Buffer[0];
        }

        public static void Import(MdlFile mdlFile, string path, bool clearExisting = true, ProgressTracker progress = null)
        {
            progress?.SetProgress(10, "Loading model file");

            var scene = IOManager.LoadScene(path, new ImportSettings());
            var model = scene.Models[0];

            progress?.SetProgress(50, "Setting up mdl data");

            if (clearExisting)
                Clear(mdlFile);

            Console.WriteLine($"Adding LOD {mdlFile.LODModels.Count}");

            if (mdlFile.LODModels.Count >= 8)
                throw new Exception($"Max LOD count present!");

            List<MdlFile.Buffer> vertexBuffers = mdlFile.vBuffers.ToList();
            List<MdlFile.Buffer> indexBuffers = mdlFile.idxBuffers.ToList();

            //prepare vertex and index buffer which are shared between meshes in one big buffer
            var vBuffer = new MemoryStream();
            var idxBuffer = new MemoryStream();

            List<MdlFile.VertexAttributeSet> vertexAttributeSets = new List<MdlFile.VertexAttributeSet>();
            List<string> vertexSetCompare = new List<string>();

            var vertexAttributes = GetAttributes(new ImportMeshSettings());
            var key = string.Join(' ', vertexAttributes.Select(x => x.ToString()));

            //Check if the same attribute set has been used or not
            if (!vertexSetCompare.Contains(key))
            {
                vertexSetCompare.Add(key);
                //Add attribute set
                vertexAttributeSets.Add(new MdlFile.VertexAttributeSet()
                {
                    Idx = (ushort)mdlFile.Attributes.Count,
                    Count = (ushort)vertexAttributes.Count,
                });
                //Add atributes
                mdlFile.Attributes.AddRange(vertexAttributes);
            }
            mdlFile.AttributeSets.AddRange(vertexAttributeSets);

            using (var vWriter = new BinaryWriter(vBuffer))
            using (var idxWriter = new BinaryWriter(idxBuffer))
            {
                MdlFile.LODModelInfo modelInfo = new MdlFile.LODModelInfo();
                modelInfo.MeshIndex = (ushort)mdlFile.MeshInfos.Count;

                List<MdlFile.MeshInfo> meshes = new List<MdlFile.MeshInfo>();

                int index = 0;
                foreach (var iomesh in model.Meshes)
                {
                    if (iomesh.Polygons.Count == 0)
                        continue;

                    var iomaterial = scene.Materials.FirstOrDefault(x => x.Name == iomesh.Polygons[0].MaterialName);
                    string mat = "";
                    if (iomaterial != null)
                        mat = iomaterial.Label != null ? iomaterial.Label : iomaterial.Name;

                    progress?.SetProgress(100 * (float)index / model.Meshes.Count, $"Loading mesh ");
                    meshes.Add(ImportMesh(mdlFile, iomesh, mat, vWriter, idxWriter, new ImportMeshSettings()));
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
                MdlFile.Buffer buffer = new MdlFile.Buffer();
                buffer.CompressedData(vBuffer.ToArray());
                vertexBuffers.Add(buffer);

                MdlFile.Buffer bufferIdx = new MdlFile.Buffer();
                bufferIdx.CompressedData(idxBuffer.ToArray());
                indexBuffers.Add(bufferIdx);
            }

            mdlFile.vBuffers = vertexBuffers.ToArray();
            mdlFile.idxBuffers = indexBuffers.ToArray();
        }

        static List<MdlFile.VertexAttribute> GetAttributes(ImportMeshSettings settings)
        {
            List<MdlFile.VertexAttribute> vertexAttributes = new List<MdlFile.VertexAttribute>();

            void AddAttribute(MdlFile.AttributeType type, MdlFile.AttributeFormat format, ref byte offset, byte buffer = 0)
            {
                var attr = new MdlFile.VertexAttribute(buffer, offset, type, format);
                vertexAttributes.Add(attr);
                offset += (byte)attr.Size;
            }
            //Buffer 1
            {
                byte offset1 = 0;
                AddAttribute(MdlFile.AttributeType.Position, MdlFile.AttributeFormat.Vec3f, ref offset1, 0);
                if (settings.EnableUV0 && settings.EnableUV1)
                    AddAttribute(MdlFile.AttributeType.TexCoord01, MdlFile.AttributeFormat.Vec4f, ref offset1, 0);
                else if (settings.EnableUV0)
                    AddAttribute(MdlFile.AttributeType.TexCoord01, MdlFile.AttributeFormat.Vec2f, ref offset1, 0);

                if (settings.EnableUV2 && settings.EnableUV3)
                    AddAttribute(MdlFile.AttributeType.TexCoord23, MdlFile.AttributeFormat.Vec4f, ref offset1, 0);
                else if (settings.EnableUV2)
                    AddAttribute(MdlFile.AttributeType.TexCoord23, MdlFile.AttributeFormat.Vec2f, ref offset1, 0);

                if (settings.EnableBoneset0)
                {
                    AddAttribute(MdlFile.AttributeType.BoneIndices0, MdlFile.AttributeFormat.Byte4, ref offset1, 0);
                    AddAttribute(MdlFile.AttributeType.BoneWeights0, MdlFile.AttributeFormat.UNorm8, ref offset1, 0);
                }
                if (settings.EnableBoneset1)
                {
                    AddAttribute(MdlFile.AttributeType.BoneWeights1, MdlFile.AttributeFormat.Byte4, ref offset1, 0);
                    AddAttribute(MdlFile.AttributeType.BoneIndices1, MdlFile.AttributeFormat.UNorm8, ref offset1, 0);
                }
            }

            //Buffer 2
            {
                byte offset2 = 0;
                AddAttribute(MdlFile.AttributeType.Normals, MdlFile.AttributeFormat.Vec3f, ref offset2, 1);
                if (settings.EnableTangents)
                    AddAttribute(MdlFile.AttributeType.Tangents, MdlFile.AttributeFormat.Vec4HalfFloat, ref offset2, 1);
                if (settings.EnableBinormal)
                    AddAttribute(MdlFile.AttributeType.Binormal, MdlFile.AttributeFormat.Vec4HalfFloat, ref offset2, 1);

                AddAttribute(MdlFile.AttributeType.UnknownAttr24, MdlFile.AttributeFormat.Vec4HalfFloat, ref offset2, 1);
                AddAttribute(MdlFile.AttributeType.UnknownAttr8, MdlFile.AttributeFormat.Byte4, ref offset2, 1);
                AddAttribute(MdlFile.AttributeType.UnknownAttr9, MdlFile.AttributeFormat.UNorm8, ref offset2, 1);
            }

            return vertexAttributes;
        }

        static MdlFile.MeshInfo ImportMesh(MdlFile mdlFile, IOMesh iomesh, string materialName,
            BinaryWriter vWriter, BinaryWriter idxWriter, ImportMeshSettings settings)
        {
            MdlFile.MeshInfo mesh = new MdlFile.MeshInfo();

            var max_bone_count = iomesh.Vertices.Max(x => x.Envelope.Weights.Count);

            //Channel flags
            if (settings.EnableUV0) mesh.TexCoordSetFlag |= 1;
            if (settings.EnableUV1) mesh.TexCoordSetFlag |= 2;
            if (settings.EnableUV2) mesh.TexCoordSetFlag |= 4;
            if (settings.EnableUV3) mesh.TexCoordSetFlag |= 8;

            if (settings.EnableBoneset0) mesh.BoneSetFlag |= 2;
            if (settings.EnableBoneset1) mesh.BoneSetFlag |= 1;

            if (settings.EnableColor1) mesh.ColorSetFlag |= 1;

            //Second flag, unsure what this does
            mesh.Flag2 = 18;


            mesh.MaterialID = 0;
            if (mdlFile.MaterialNames.Contains(materialName))
            {
                mesh.MaterialID = (ushort)mdlFile.MaterialNames.IndexOf(materialName);
            }
            else
            {
                Console.WriteLine($"Non matching material {materialName}. First material will be used {mdlFile.MaterialNames[0]}");

            }

            List<string> missing_bones = new List<string>();

            List<MdlBufferHelper.Vertex> vertices = new List<MdlBufferHelper.Vertex>();
            foreach (var vtx in iomesh.Vertices)
            {
                var texCoord0 = iomesh.HasUVSet(0) ? vtx.UVs[0] : Vector2.Zero;
                var texCoord1 = iomesh.HasUVSet(1) ? vtx.UVs[1] : Vector2.Zero;
                var texCoord2 = iomesh.HasUVSet(2) ? vtx.UVs[2] : Vector2.Zero;
                var texCoord3 = iomesh.HasUVSet(3) ? vtx.UVs[3] : Vector2.Zero;
                var color0 = iomesh.HasColorSet(0) ? vtx.Colors[0] : Vector4.One;

                var vertex = new MdlBufferHelper.Vertex()
                {
                    Position = vtx.Position,
                    Normal = vtx.Normal,
                    TexCoord01 = new Vector4(texCoord0.X, texCoord0.Y, texCoord1.X, texCoord1.Y),
                    TexCoord23 = new Vector4(texCoord2.X, texCoord2.Y, texCoord3.X, texCoord3.Y),
                    Tangent = new Vector4(vtx.Tangent.X, vtx.Tangent.Y, vtx.Tangent.Z, 1f),
                    Binormal = new Vector4(vtx.Binormal.X, vtx.Binormal.Y, vtx.Binormal.Z, 1f),
                    Color = new Vector4(color0.X, color0.Y, color0.Z, color0.W),
                };

                List<int> boneIndices = new List<int>();
                List<float> boneWeights = new List<float>();

                foreach (var env in vtx.Envelope.Weights)
                {
                    int index = mdlFile.JointNames.IndexOf(env.BoneName);
                    if (index == -1 && !missing_bones.Contains(env.BoneName))
                    {
                        missing_bones.Add(env.BoneName);
                        continue;
                    }
                    boneIndices.Add(index);
                    boneWeights.Add(env.Weight);
                }

                vertex.SetBoneIndicesWeights(boneIndices, boneWeights);
                vertices.Add(vertex);
            }

            foreach (var bone in missing_bones)
                Console.WriteLine($"Bone not present in skeleton! {bone}");

            MdlBufferHelper.WriteVertexBuffer(mdlFile, mesh, vertices, vWriter);

            List<ushort> indices = new List<ushort>();
            foreach (var ind in iomesh.Polygons[0].Indicies)
                indices.Add((ushort)ind);

            MdlBufferHelper.WriteIndicesBuffer(mesh, indices, idxWriter);

            return mesh;
        }

        public class ImportMeshSettings
        {
            public bool EnableUV0 = true;
            public bool EnableUV1 = true;
            public bool EnableUV2 = true;
            public bool EnableUV3;

            public bool EnableBoneset0 = true;
            public bool EnableBoneset1;

            public bool EnableColor0;
            public bool EnableColor1;

            public bool EnableTangents = true;
            public bool EnableBinormal = true;
        }
    }
}
