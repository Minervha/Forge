# DataTableForge

CLI tool that generates Unreal Engine 5 `.pak` files for [WildLife](https://www.patreon.com/c/adeptussteve/posts?vanity=adeptussteve) modding. Parses mod definition `.txt` files, injects data into UE5 DataTables, and packs everything into a game-ready `.pak`.

## Prerequisites

- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [repak](https://github.com/trumank/repak) — UE4/UE5 .pak packer (place `repak.exe` somewhere accessible)
- Extracted DataTables from the game (see [Extraction](#extraction) below)

## Usage

```bash
DataTableForge.exe generate --config forge.config.json
```

Any config field can be overridden on the command line — `--<key> <value>` takes
precedence over the file (e.g. `--pakName MyMods --mods modA,modB`).

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
  "fillEmptySexSlots": false,
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
| `fillEmptySexSlots` | *Optional, default `false`.* When `true`, empty sex-mesh slots on every **base** `DT_ClothesOutfit` row are filled by copying the regular mesh. Legacy configs without the key stay off. |
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

### DataTables

Four tables are **injected** (rows added by mods); two are **passthrough** (copied unchanged so mods can ship their own assets against them).

| DataTable | Purpose | Mode |
|-----------|---------|------|
| DT_ClothesOutfit | Outfit definitions (meshes, physics, icons) | injected — Add |
| DT_GameCharacterOutfits | Which characters can wear which outfits | injected — Add, Port |
| DT_GameCharacterCustomization | Character customization options (hair, skin, eyes) | injected — Character Customization |
| DT_Tattoo | Tattoo definitions (texture, icon, color, cost) | injected — Tattoo |
| DT_GFur | Fur rendering settings | passthrough |
| DT_SandboxProps | Sandbox props | passthrough |

## Mod .txt format

Four variants are supported, selected by the `Variant:` line (defaults to `Add` when absent).

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

Slots: `HEAD`, `CHEST`, `HANDS`, `LEGS`, `FEET`. Each slot has: `MESH_PATH`, `MESH_NAME`, `SEX_MESH_PATH`, `SEX_MESH_NAME`, `ICON_PATH`, `ICON_NAME`, `PHYSICSAREAS`, `FLEXREGIONS`, `MORPHTARGETVALUE`, `MORPHTARGET`, `CONSTRAINTPROFILE`, `AROUSALBLEND`, `FURMASK_PATH`, `FURMASK_NAME`, `SEX_FURMASK_PATH`, `SEX_FURMASK_NAME`. Missing path/name fields default to `None`.

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

### Tattoo

Adds a tattoo to the global `DT_Tattoo` table. Tattoos are character-agnostic — there is no `Character:` field.

```
Variant: Tattoo
ModName: MyTattoo
Author: YourName
NameMap: /Game/Path/To/Texture
[TATTOO_ID]: MyTattoo
[TATTOO_DISPLAY_NAME]: My Tattoo
[TATTOO_TEXTURE_PATH]: /Game/Textures/Tattoos/MyTattoo
[TATTOO_TEXTURE_NAME]: MyTattoo
[TATTOO_ICON_PATH]: /Game/Textures/Icons/MyTattoo_icon
[TATTOO_ICON_NAME]: MyTattoo_icon
[TATTOO_COLOR_R]: 0
[TATTOO_COLOR_G]: 0
[TATTOO_COLOR_B]: 0
[TATTOO_COLOR_A]: 1.0
[TATTOO_COVERED_SLOTS]: 0
[TATTOO_COST]: 100
[TATTOO_UV_SET]: Set1
[TATTOO_TRADER_TYPE]: None
```

`[TATTOO_ID]` is required (entries with an empty id are skipped). Optional fields
default as follows: icon path/name fall back to the texture path/name, `COST` to
`100`, `UV_SET` to `Set1`, `TRADER_TYPE` to `None`, `COLOR_A` to `1.0`, and the
remaining colors/`COVERED_SLOTS` to `0`.

## prepare-build (enable the fast v2 path)

```bash
DataTableForge prepare-build --buildDir <build-folder> --usmapPath <build.usmap>
```

Materialises the `AutoMod_P/` base `.uasset` files from a build's `*_Debug.json`
exports. Their presence makes `generate` use the fast **v2** binary injector
(`AssetInjector`, parallelised) instead of the **v1** JSON fallback — measured
~12.5s → ~2.8s per job on a single-mod build.

Run it **once per build version** (the output is permanent — base DataTables
never change for a given version). Produces the 4 injectable tables only
(DT_ClothesOutfit, DT_GameCharacterOutfits, DT_GameCharacterCustomization,
DT_Tattoo); GFur/SandboxProps are passthrough and intentionally excluded. To
roll back to v1, delete the build's `AutoMod_P/` folder.

This is the JSON-only equivalent of [Extraction](#extraction): use it when you
have the extracted DataTable JSON but not the original game `.pak`.

## Extraction

To extract base DataTables from a game build:

```bash
DataTableForge.exe <pak-path> <usmap-path> <output-dir> <repak-exe>
```

This unpacks the game's `.pak`, reads all DataTables, and produces the JSON templates + `AutoMod_P/` binary assets that the `generate` command needs.

## Building

```bash
dotnet build

# Windows (framework-dependent) — packaged into Minervha Studio. See publish.ps1.
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish/

# Linux (self-contained) — standalone VM, needs no .NET runtime. See publish-linux.ps1.
dotnet publish -c Release -r linux-x64 --self-contained true -o ./publish-linux/
```

On Linux the produced binary is `DataTableForge` (no extension); `repakExe` in the config must point to the Linux `repak` build (also no `.exe`). See [docs/plans/features/forge-linux/linux-setup.md](docs/plans/features/forge-linux/linux-setup.md).

## License

MIT
