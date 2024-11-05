# FF16-Model-Importer
A tool to export and import FF16 .mdl file binaries as .gltf or .dae

If you appreciate my tools, feel free to [donate](https://ko-fi.com/simplykxg)

## Requirements
- [net8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

## Supports
- MDL exporting and replacing
- MTL exporting and replacing

## Usage

In a command line window

Commands:
- `MdlConverter.exe body.mdl` c1002.pac (Extracts contents to a folder called "body" with all LOD models as .gltf)
- `MdlConverter.exe material.mtl` (mtl conversion)
- `MdlConverter.exe material.json` (conversion back to mtl)
- `MdlConverter.exe body` (Imports mdl contents back from "body" folder.)

**Ensure you provide the .pac from a character pack folder for skeleton data**


Credits:
- Nenkai for pac handling needed to get .skl data and some .tex info.
- Joschuka/Dimy/Someone else for various research and help for MDL file binary.

