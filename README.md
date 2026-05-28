# DataTableForge

CLI tool that generates Unreal Engine 5 `.pak` files for [WildLife](https://store.steampowered.com/app/1927950/Wild_Life/) modding. Parses mod definition `.txt` files, injects data into UE5 DataTables, and packs everything into a game-ready `.pak`.

## Prerequisites

- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [repak](https://github.com/trumank/repak) — UE4/UE5 .pak packer (place `repak.exe` somewhere accessible)
- Extracted DataTables from the game (see [Extraction](#extraction) below)

## Usage

```bash
DataTableForge.exe generate --config forge.config.json
```

### forge.config.json

```json
{
  "buildDir": "path/to/extracted/DataTables/build_folder",
  "modsDir": "path/to/mods",
  "outputDir": "path/to/output",
  "usmapPath": "path/to/build.usmap",
  "repakExe": "path/to/repak.exe",
  "pakName": "MyMods",
  "userId": "your-id",
  "mods": ["mod_folder_1", "mod_folder_2"],
  "modSources": {
    "mod_folder_2": "path/to/overlay/for/mod_folder_2"
  }
}
```

| Field | Description |
|-------|-------------|
| `buildDir` | Folder with extracted DataTables (`*_Default.json`, `*_Debug.json`, `AutoMod_P/`) |
| `modsDir` | Parent folder containing mod subfolders — used as the implicit fallback for mods not listed in `modSources` |
| `outputDir` | Where to write the `.pak` file |
| `usmapPath` | Path to the `.usmap` mappings file for this game build |
| `repakExe` | Path to `repak.exe` |
| `pakName` | Output pak name (produces `{pakName}_P.pak`) |
| `userId` | User identifier (used for staging subfolder) |
| `mods` | Array of mod ids — each must resolve to a directory either via `modSources[id]` or `modsDir/id` |
| `modSources` | *Optional.* Per-mod content directory override. Lets callers point most mods straight at a shared cache while keeping a separate overlay dir for the few that need pre-processing (e.g. asset filtering). Omitted entries fall back to `modsDir/<modId>`. |

### Mod folder structure

Each mod folder (resolved via `modSources[modId]` or `modsDir/<modId>`) should contain:

```
mod_folder/
├── ModName.txt          # Mod definition (see format below)
├── AutoMod/             # Optional: legacy wlmm-import layout — *.txt files
│   └── *.txt            #   here are scanned alongside the root-level rules
├── WildLifeC/           # UE5 assets (meshes, textures, materials)
│   └── Content/
│       └── ...
```

## Pipeline

```
[1/5] Parse .txt files  →  ModParser.Parse() → ModData[]
[2/5] Inject into DTs   →  AssetInjector (binary) or TemplateInjector (JSON fallback)
[3/5] Write .uasset     →  UAssetAPI.Write()
[4/5] Copy mod assets   →  WildLifeC/ trees to staging
[5/5] Pack .pak          →  repak pack
```

### DataTables modified

| DataTable | Purpose | Mod types |
|-----------|---------|-----------|
| DT_ClothesOutfit | Outfit definitions (meshes, physics, icons) | Add |
| DT_GameCharacterOutfits | Which characters can wear which outfits | Add, Port |
| DT_GameCharacterCustomization | Character customization options (hair, skin, eyes) | CharacterCustomization |
| DT_GFur | Fur rendering settings | (passthrough) |
| DT_SandboxProps | Sandbox props | (passthrough) |

## Mod .txt format

Three variants are supported:

### Add (default)

Adds a new outfit to the game.

```
ModName: MyOutfit
Author: YourName
Character: Jenny
NameMap: /Game/Path/To/Asset1, /Game/Path/To/Asset2
[CLOTHING_ID]: MyOutfit_Jenny
[CLOTHING_NAME]: My Custom Outfit

[HEAD_MESH_PATH]: None
[HEAD_MESH_NAME]: None
[CHEST_MESH_PATH]: /Game/Meshes/Characters/Jenny/Costumes/MyOutfit_chest
[CHEST_MESH_NAME]: MyOutfit_chest
[CHEST_SEX_MESH_PATH]: /Game/Meshes/Characters/Jenny/Costumes/MyOutfit_chest
[CHEST_SEX_MESH_NAME]: MyOutfit_chest
[CHEST_ICON_PATH]: /Game/Textures/Icons/MyOutfit_icon
[CHEST_ICON_NAME]: MyOutfit_icon
[CHEST_PHYSICSAREAS]: 0
[CHEST_FLEXREGIONS]: 0
[CHEST_MORPHTARGETVALUE]: 1
[CHEST_MORPHTARGET]: null
[CHEST_CONSTRAINTPROFILE]: null
[CHEST_AROUSALBLEND]: 0
...
```

Slots: `HEAD`, `CHEST`, `HANDS`, `LEGS`, `FEET`. Each slot has: `MESH_PATH`, `MESH_NAME`, `SEX_MESH_PATH`, `SEX_MESH_NAME`, `ICON_PATH`, `ICON_NAME`, `PHYSICSAREAS`, `FLEXREGIONS`, `MORPHTARGETVALUE`, `MORPHTARGET`, `CONSTRAINTPROFILE`, `AROUSALBLEND`, `FURMASK_PATH`, `FURMASK_NAME`, `SEX_FURMASK_PATH`, `SEX_FURMASK_NAME`.

### Port

Ports an existing outfit to a different character. Only needs the clothing ID.

```
Variant: Port
ModName: PortedOutfit
Author: YourName
Character: Maya
[CLOTHING_ID]: ExistingOutfit_Maya
```

### Character Customization

Adds custom hair, skin, eyes, etc.

```
Variant: Character Customization
ModName: CustomHair
Author: YourName
Character: Jenny
NameMap: /Game/Path/To/Hair
Hair: /Game/Path/To/Hair/Package, HairAssetName, /Game/Path/To/Physics, PhysicsName
Skin: /Game/Path/To/Skin/Package, SkinAssetName
Eyes: /Game/Path/To/Eyes/Package, EyesAssetName
```

Targets: `Hair`, `Beard`, `Skin`, `PubicHair`, `Eyes`, `EyeLiner`, `EyeShadow`, `Lipstick`, `Tanlines`. Hair entries have 4 values (path, name, physics_path, physics_name). All others have 2 (path, name).

## Extraction

To extract base DataTables from a game build:

```bash
DataTableForge.exe <pak-path> <usmap-path> <output-dir> <repak-exe>
```

This unpacks the game's `.pak`, reads all DataTables, and produces the JSON templates + `AutoMod_P/` binary assets that the `generate` command needs.

## Building

```bash
dotnet build
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish/
```

## License

MIT
