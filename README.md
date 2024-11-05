# FF16-Model-Importer
A tool to export and import FF16 .mdl file binaries as .gltf or .dae

If you appreciate my tools, feel free to [donate](https://ko-fi.com/simplykxg)

## Usage

In a command line window

Extract:
- MdlConverter.exe body.mdl c1002.pac

**Ensure you provide the .pac from a character pack folder for skeleton data**

Extracts contents to a folder called "body" with all LOD models as .gltf

Inject:
- MdlConverter.exe body

Imports the contents back from "body" folder.

Credits:
- Nenkai for pac handling needed to get .skl data and some .tex info.
- Joschuka/Dimy/Someone else for various research and help for MDL file binary.

