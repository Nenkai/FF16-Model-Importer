using CafeLibrary;
using CafeLibrary.ff16;
using FinalFantasy16;
using FinalFantasy16Library.Files.MTL;
using FinalFantasyConvertTool.FNT;
using MdlTest.ff16;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Xml.Linq;

namespace FinalFantasyConvertTool
{
    public class Program
    {
        public static void Main(string[] args)
        {
            foreach (string arg in args)
            {
                if (arg.EndsWith(".tex"))
                {
                    TexFile texFile = new TexFile(File.OpenRead(arg));
                    foreach (var tex in texFile.Textures)
                    {
                        tex.Export(arg + ".png");
                    }
                }
                if (arg.EndsWith(".tex.png"))
                {
                    TexFile texFile = new TexFile(File.OpenRead(arg.Replace(".png", "")));
                    texFile.Textures[0].Replace(arg);
                    texFile.Save(arg.Replace(".png", ""));
                }
                if (Directory.Exists(arg)) //folder to compile back as
                {
                    string name = Path.GetFileNameWithoutExtension(arg);
                    if (!File.Exists($"{name}.mdl"))
                        return;

                    MdlFile mdlFile = new MdlFile(File.OpenRead($"{name}.mdl"));

                    ModelImporter.Clear(mdlFile);
                    for (int i = 0; i < 8; i++)
                    {
                        string filePathGLTF = Path.Combine(name, $"{name}_LOD{i}.gltf");
                        string filePathDAE = Path.Combine(name, $"{name}_LOD{i}.dae");
                        string filePathGLB = Path.Combine(name, $"{name}_LOD{i}.glb");
                        string filePathOBJ = Path.Combine(name, $"{name}_LOD{i}.obj");

                        string inputPath = "";
                        if (File.Exists(filePathGLTF)) inputPath = filePathGLTF;
                        if (File.Exists(filePathGLB)) inputPath = filePathGLB;
                        if (File.Exists(filePathDAE)) inputPath = filePathDAE;
                        if (File.Exists(filePathOBJ)) inputPath = filePathOBJ;

                        if (!string.IsNullOrEmpty(inputPath))
                        {
                            //Import LOD level
                            ModelImporter.Import(mdlFile, inputPath, false);
                        }
                    }

                    // Add one final bounding as the list is always joint count + 1
                    if (mdlFile.JointBoundings.Count > 0)
                        mdlFile.JointBoundings.Add(new MdlFile.JointBounding()
                        {
                            BoundingMax = new MdlFile.Vector3Struct(1, 1, 1),
                            BoundingMin = new MdlFile.Vector3Struct(-1, -1, -1),
                        });


                    mdlFile.Save($"{name}NEW.mdl");

                    Console.WriteLine($"File saved as {name}NEW.mdl");
                }
                if (arg.EndsWith(".mdl"))
                {
                    string name = Path.GetFileNameWithoutExtension(arg);

                    List<SkelFile> skeletons= new List<SkelFile>();

                    var pacFile = args.FirstOrDefault(x => x.EndsWith(".pac"));
                    if (!string.IsNullOrEmpty(pacFile))
                    {
                        //Get skeleton from given pac argument
                        PacFile pac = new PacFile(File.OpenRead(pacFile));
                        //Multiple skeletons
                        foreach (var file in pac.Files.Where(x => x.FileName.EndsWith(".skl")).OrderByDescending(g => g.FileName.Contains("body.skl")))
                            skeletons.Add(new SkelFile(new MemoryStream(file.Data)));
                    }   

                    if (!Directory.Exists(name))
                        Directory.CreateDirectory(name);

                    MdlFile mdlFile = new MdlFile(File.OpenRead(arg));

                    for (int i = 0; i < mdlFile.LODModels.Count; i++)
                        ModelExporter.Export(mdlFile, skeletons, Path.Combine(name, $"{name}_LOD{i}.glb"), i);
                }

                if (arg.EndsWith(".mtl"))
                {
                    JsonSerializerSettings settings = new JsonSerializerSettings
                    {
                        Converters = new List<JsonConverter> { new TextureConstantConverter() },
                        Formatting = Formatting.Indented
                    };
                    MtlFile mtlFile = new MtlFile(File.OpenRead(arg), arg); 

                    File.WriteAllText(arg + ".json", JsonConvert.SerializeObject(mtlFile, settings));
                }
                if (arg.EndsWith(".mtl.json"))
                {
                    JsonSerializerSettings settings = new JsonSerializerSettings
                    {
                        Converters = new List<JsonConverter> { new TextureConstantConverter() },
                        Formatting = Formatting.Indented
                    };
                    MtlFile mtlFile = JsonConvert.DeserializeObject<MtlFile>(File.ReadAllText(arg), settings);
                    mtlFile.Save(arg.Replace(".json", ""));
                }
                if (arg.EndsWith(".pzd"))
                {
                    PzdFile pzdFile = new PzdFile(File.OpenRead(arg));
                    File.WriteAllText(arg + ".xml", pzdFile.ToXml());
                }
                if (arg.EndsWith(".pzd.xml"))
                {
                    string name = Path.GetFileName(arg).Replace(".pzd.xml", "");
                    string dir = Path.GetDirectoryName(arg);

                    PzdFile pzdFile = new PzdFile();
                    pzdFile.FromXML(File.ReadAllText(arg));
                    pzdFile.Save(arg.Replace(".xml", ""));
                }
                if (Directory.Exists(arg))
                {
                    foreach (var f in Directory.GetFiles(arg))
                    {
                        if (!f.EndsWith(".pzd.xml"))
                            continue;

                        PzdFile pzdFile = new PzdFile(File.OpenRead(f));
                        string name = Path.GetFileName(f);
                        pzdFile.Save(f.Replace(".xml", ""));
                    }
                }
            }
        }
    }
}
