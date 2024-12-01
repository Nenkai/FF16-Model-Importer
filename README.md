# FF16-Model-Importer
A tool to export and import FF16 .mdl file binaries as .gltf 

## Requirements
- [.NET Desktop Runtime 8.0.11](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

## Supports
- MDL exporting and replacing
- MTL exporting and replacing

## Usage

In a command line window

Commands:
- Export from MDL to GLTF:  `MdlConverter.exe body.mdl c1002.pac` (Extracts contents to a folder called `body` with all LOD models as .gltf. The .pac file from the character `pack` folder is required to export skeleton data)
- Import from GLTF to MDL: `MdlConverter.exe body` (Imports mdl contents back from `body` folder. Requires putting a copy of a "base" MDL file in the same directory as `body` folder. Requires all LODs in the `body` folder to be named `body_LODx` with `x` as a number)
- Export from MTL to JSON: `MdlConverter.exe material.mtl` 
- Import from JSON to MTL: `MdlConverter.exe material.mtl.json` (Currently this will overwrite old MTL file, recommended to put working JSON in another directory)

To properly import to MDL, your GLTF model must point to only materials that are used by the "base" MDL file. You can look up the materials using a hex editor. 
Example: One of the materials that Clive's `chara/c1001/model/body/b0001/body.mdl` model uses is named `m_c1001b0001_body_a.mtl`. To assign that file to your mesh, create a material named `m_body_a` and make sure to include materials when generating your GLTF file. 


## Contributors and donation links

- KillzXGaming: Original author of this tool  [donate](https://ko-fi.com/simplykxg)
- Nenkai: Code revisions, MDL file research, and pac handling needed to get .skl data and some .tex info  [donate](https://ko-fi.com/nenkai)
- Joschuka/Dimy and others: Various research and assistance on MDL file binary
- Maybri: Additional documentation and functionality to handle bones not present in "base" MDL 
