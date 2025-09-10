using FinalFantasy16Library.Files.MDL;
using FinalFantasy16Library.Files.MDL.Convert;
using FinalFantasy16Library.Files.MTL;
using FinalFantasy16Library.Files.PAC;
using FinalFantasy16Library.Files.PZDF;
using FinalFantasy16Library.Files.SKL;
using FinalFantasy16Library.Files.TEX;

using Newtonsoft.Json;

namespace MdlConverter;

public class Program
{
    public static void Main(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.EndsWith(".tex"))
                HandleTexToImageConversion(arg);

            if (arg.EndsWith(".tex.png"))
                HandleImageToTexConversion(arg);

            if (Directory.Exists(arg)) //folder to compile back as
                HandleModelFolderToModelConversion(arg);

            if (arg.EndsWith(".mdl"))
                ExportModelToGLTF(args, arg);

            if (arg.EndsWith(".mtl"))
                ConvertMtlToMaterialJson(arg);

            if (arg.EndsWith(".mtl.json"))
                ConvertJsonMaterialToMtl(arg);

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

    private static void ConvertJsonMaterialToMtl(string arg)
    {
        JsonSerializerSettings settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter> { new TextureConstantConverter() },
            Formatting = Formatting.Indented
        };

        MtlFile mtlFile = JsonConvert.DeserializeObject<MtlFile>(File.ReadAllText(arg), settings);
        mtlFile.Save(arg.Replace(".json", ""));
    }

    private static void ConvertMtlToMaterialJson(string arg)
    {
        JsonSerializerSettings settings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter> { new TextureConstantConverter() },
            Formatting = Formatting.Indented
        };

        MtlFile mtlFile = new MtlFile(File.OpenRead(arg), arg);
        File.WriteAllText(arg + ".json", JsonConvert.SerializeObject(mtlFile, settings));
    }

    private static void ExportModelToGLTF(string[] args, string arg)
    {
        string fullPath = Path.GetFullPath(arg);
        string dir = Path.GetDirectoryName(fullPath);
        string modelFileName = Path.GetFileNameWithoutExtension(arg);

        List<SklFile> skeletons = [];

        var pacFile = args.FirstOrDefault(x => x.EndsWith(".pac"));
        if (!string.IsNullOrEmpty(pacFile))
        {
            //Get skeleton from given pac argument
            PacFile pac = new PacFile(File.OpenRead(pacFile));

            //Multiple skeletons
            foreach (var file in pac.Files.Where(x => x.FileName.EndsWith(".skl")).OrderByDescending(g => g.FileName.Contains("body.skl")))
            {
                SklFile skel = SklFile.Open(file.Data);
                skeletons.Add(skel);
            }
        }

        string outDir = Path.Combine(dir, modelFileName);
        if (!Directory.Exists(outDir))
            Directory.CreateDirectory(outDir);

        MdlFile mdlFile = new MdlFile(File.OpenRead(arg));

        for (int i = 0; i < mdlFile.LODModels.Count; i++)
            ModelExporter.Export(mdlFile, skeletons, Path.Combine(outDir, $"{modelFileName}_LOD{i}.glb"), i);
    }

    private static void HandleModelFolderToModelConversion(string arg)
    {
        Console.WriteLine("Input Type: Model Folder");

        string name = Path.GetFileNameWithoutExtension(arg);
        string fullPath = Path.GetFullPath(arg);
        string parent = Path.GetDirectoryName(fullPath);
        string baseModel = Path.Combine(parent, $"{name}.mdl");
        if (!File.Exists(baseModel))
        {
            Console.WriteLine($"ERROR: Base model '{baseModel}' missing.");
            Console.WriteLine("Converting a folder with models requires a base model to use.");
            return;
        }

        Console.WriteLine($"Loading base model: '{baseModel}'");
        MdlFile mdlFile = new MdlFile(File.OpenRead(baseModel));

        ModelImporter.ClearMeshes(mdlFile);
        for (int i = 0; i < 8; i++)
        {
            string filePathGLTF = Path.Combine(fullPath, $"{name}_LOD{i}.gltf");
            string filePathDAE = Path.Combine(fullPath, $"{name}_LOD{i}.dae");
            string filePathGLB = Path.Combine(fullPath, $"{name}_LOD{i}.glb");
            string filePathOBJ = Path.Combine(fullPath, $"{name}_LOD{i}.obj");

            string inputPath = "";
            if (File.Exists(filePathGLTF))
            {
                inputPath = filePathGLTF;
                Console.WriteLine($"Importing LOD{i} (GLTF)");
            }
            else if (File.Exists(filePathGLB))
            {
                inputPath = filePathGLB;
                Console.WriteLine($"Importing LOD{i} (DAE)");
            }
            else if (File.Exists(filePathDAE))
            {
                inputPath = filePathDAE;
                Console.WriteLine($"Importing LOD{i} (GLB)");
            }
            else if (File.Exists(filePathOBJ))
            {
                inputPath = filePathOBJ;
                Console.WriteLine($"Importing LOD{i} (OBJ)");
            }

            var importer = new ModelImporter();
            if (!string.IsNullOrEmpty(inputPath))
            {
                //Import LOD level
                importer.Import(mdlFile, inputPath, false);

                // Prepare generated joint info for extra bones not found in base MDL file
                mdlFile.SetGeneratedJoints(importer.GeneratedJoints);
            }

        }

        string outputModelFile = $"{fullPath}NEW.mdl";
        Console.WriteLine("Saving model file...");
        mdlFile.Save(outputModelFile);

        Console.WriteLine($"File saved as '{outputModelFile}'");
    }

    private static void HandleImageToTexConversion(string arg)
    {
        TexFile texFile = new TexFile(File.OpenRead(arg.Replace(".png", "")));
        texFile.Textures[0].Replace(arg);
        texFile.Save(arg.Replace(".png", ""));
    }

    private static void HandleTexToImageConversion(string arg)
    {
        TexFile texFile = new TexFile(File.OpenRead(arg));
        foreach (var tex in texFile.Textures)
        {
            tex.Export(arg + ".png");
        }
    }
}
