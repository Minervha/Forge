using System.Collections.Generic;
using System.IO;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.Unversioned;
using Xunit;

public class CharacterModTests
{
    // Row names of the 2026.09.25 DT_GameCharacterOutfits that have been tricky so far.
    static readonly string[] OutfitRowNames = [
        "Outfits_Maya_AB2", "Outfits_SexBot_AB_NSFW", "Outfits_Jenny_AB", "Outfits_JennyFuta_AB",
        "Outfits_Shey_AB", "Outfits_SheyFuta", "Outfits_Tali_A", "Outfits_TaliFuta",
        "Outfits_Max_NewModel", "Outfits_Max_B", "Outfits_DropoutB", "Outfits_RyanYoung",
    ];

    [Theory]
    [InlineData("Maya", "Outfits_Maya_AB2")]
    [InlineData("SexBot", "Outfits_SexBot_AB_NSFW")]
    [InlineData("Shey", "Outfits_Shey_AB")]
    [InlineData("SheyFuta", "Outfits_SheyFuta")]
    [InlineData("Jenny", "Outfits_Jenny_AB")]
    [InlineData("Tali", "Outfits_Tali_A")]
    [InlineData("Max", "Outfits_Max_NewModel")]
    [InlineData("MaxB", "Outfits_Max_NewModel")]
    [InlineData("Dropout", "Outfits_DropoutB")]
    [InlineData("Ryan", "Outfits_RyanYoung")]
    [InlineData("shey", "Outfits_Shey_AB")]
    public void Resolves_outfit_row(string character, string expected)
        => Assert.Equal(expected, OutfitRows.Resolve(character, OutfitRowNames));

    [Fact]
    public void Prefix_does_not_depend_on_row_order()
    {
        string[] reversed = ["Outfits_SheyFuta", "Outfits_Shey_AB"];
        Assert.Equal("Outfits_Shey_AB", OutfitRows.Resolve("Shey", reversed));
    }

    [Fact]
    public void Unknown_character_has_no_outfit_row()
        => Assert.Null(OutfitRows.Resolve("Nobody", OutfitRowNames));

    static readonly Dictionary<string, string> NpcRows = new()
    {
        ["Jenny"] = "Jenny",
        ["EmptyShey"] = "EmptyShey",
        ["RangerGuardSmall"] = "Alissa",
    };

    [Theory]
    [InlineData("", "Jenny", "Jenny")]               // the base's own row
    [InlineData("", "Shey", "EmptyShey")]            // Empty{base}
    [InlineData("", "Alissa", "RangerGuardSmall")]   // first row built on the base
    [InlineData("EmptyShey", "Alissa", "EmptyShey")] // explicit wins
    public void Resolves_npc_template(string explicitTemplate, string baseRow, string expected)
        => Assert.Equal(expected, CharacterInjector.ResolveNpcTemplate(explicitTemplate, baseRow, NpcRows));

    [Theory]
    [InlineData("Missing", "Shey")]
    [InlineData("", "Nobody")]
    public void Npc_template_not_found(string explicitTemplate, string baseRow)
        => Assert.Null(CharacterInjector.ResolveNpcTemplate(explicitTemplate, baseRow, NpcRows));

    static ModData ParseText(string text)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"forge-test-{System.Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, text);
        try { return ModParser.Parse(tmp); }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Parses_character_mod()
    {
        var mod = ParseText("""
            Variant: Character
            ModName: SheyPlus
            Author: Lou
            [CHARACTER_ID]: SheyPlus
            [CHARACTER_DISPLAY_NAME]: Shey Plus
            [CHARACTER_BASE]: Shey
            [CHARACTER_NPC_TEMPLATE]: EmptyShey
            [CHARACTER_OUTFITS]: Maya
            [CHARACTER_MESH_PATH]: /Game/Mods/SheyPlus/Body
            [CHARACTER_MESH_NAME]: Body
            """);

        Assert.Equal(ModVariant.Character, mod.Variant);
        Assert.Equal("Shey Plus", mod.CharacterEntry!.DisplayName);
        Assert.Equal("EmptyShey", mod.CharacterEntry.NpcTemplate);
        Assert.Equal("Maya", mod.CharacterEntry.Outfits);
        Assert.Equal("/Game/Mods/SheyPlus/Body", mod.CharacterEntry.MeshPath);
        Assert.Empty(ModValidator.Validate(mod));
    }

    [Fact]
    public void Parses_outfit_set_mod()
    {
        var mod = ParseText("""
            Variant: Outfit Set
            ModName: Share
            [OUTFITS_FROM]: Maya
            [OUTFITS_TO]: Tanya
            """);

        Assert.Equal(ModVariant.OutfitSet, mod.Variant);
        Assert.Equal("Maya", mod.OutfitsFrom);
        Assert.Equal("Tanya", mod.OutfitsTo);
        Assert.Empty(ModValidator.Validate(mod));
    }

    [Theory]
    [InlineData("", "Name", "Shey", "/Game/M", "M", "CHARACTER_ID")]
    [InlineData("Has Space", "Name", "Shey", "/Game/M", "M", "CHARACTER_ID")]
    [InlineData("Id", "", "Shey", "/Game/M", "M", "DISPLAY_NAME")]
    [InlineData("Id", "Name", "", "/Game/M", "M", "CHARACTER_BASE")]
    [InlineData("Id", "Name", "Shey", "", "M", "MESH_PATH")]
    [InlineData("Id", "Name", "Shey", "/Game/M", "", "MESH_PATH")]
    public void Validator_rejects_incomplete_character(string id, string name, string baseRow, string path, string mesh, string expected)
    {
        var mod = new ModData
        {
            Variant = ModVariant.Character,
            CharacterEntry = new CharacterData { Id = id, DisplayName = name, Base = baseRow, MeshPath = path, MeshName = mesh },
        };
        Assert.Contains(ModValidator.Validate(mod), e => e.Contains(expected));
    }

    [Fact]
    public void Validator_rejects_incomplete_outfit_set()
        => Assert.NotEmpty(ModValidator.Validate(new ModData { Variant = ModVariant.OutfitSet, OutfitsFrom = "Maya" }));

    // ── With real game data ──────────────────────────────────────────────────
    // The tables aren't in this repo: set FORGE_TEST_BUILD (build folder) and
    // FORGE_TEST_USMAP to run these. Without them they pass without checking anything.

    static (string build, string usmap)? GameData()
    {
        var build = System.Environment.GetEnvironmentVariable("FORGE_TEST_BUILD");
        var usmap = System.Environment.GetEnvironmentVariable("FORGE_TEST_USMAP");
        return build != null && usmap != null ? (build, usmap) : null;
    }

    static ModData Character(string id, string baseRow, string outfits = "", string profile = "") => new()
    {
        Variant = ModVariant.Character,
        ModId = id,
        CharacterEntry = new CharacterData
        {
            Id = id, DisplayName = id + " Name", Base = baseRow, Outfits = outfits, Profile = profile,
            MeshPath = "/Game/Mods/Test/Body", MeshName = "Body",
        },
    };

    static StructPropertyData Row(UAsset asset, string name)
        => ((DataTableExport)asset.Exports[0]).Table.Data.First(r => r.Name.ToString() == name);

    static string Handle(StructPropertyData row, string field)
        => row.Value.OfType<StructPropertyData>().First(p => p.Name.ToString() == field)
              .Value.OfType<NamePropertyData>().First(p => p.Name.ToString() == "RowName").Value.ToString();

    [Fact]
    public void Injects_character_rows_on_real_tables()
    {
        if (GameData() is not var (build, usmap)) return;
        var mods = new List<ModData> { Character("SheyPlus", "Shey") };

        var tables = CharacterInjector.Inject(build, usmap, mods);

        Assert.Single(mods);
        var st = Row(tables["DT_NPCStaticData"], "SheyPlus");
        var mesh = st.Value.OfType<SoftObjectPropertyData>().First(p => p.Name.ToString() == "Mesh");
        Assert.Equal("/Game/Mods/Test/Body", mesh.Value.AssetPath.PackageName.ToString());
        Assert.Equal("Outfits_Shey_AB", Handle(st, "Outfits"));

        var npc = Row(tables["DT_NPCData_Sandbox"], "SheyPlus");
        Assert.Equal("SheyPlus", Handle(npc, "StaticData"));
        Assert.Equal("Shey", Handle(npc, "customizationProfile"));
        var name = npc.Value.OfType<TextPropertyData>().First(p => p.Name.ToString() == "DisplayName");
        Assert.Equal("SheyPlus Name", name.CultureInvariantString.ToString());
        Assert.Equal("Minervha_Character_SheyPlus", name.Value.ToString());
    }

    [Fact]
    public void Written_tables_read_back_with_the_new_rows()
    {
        if (GameData() is not var (build, usmap)) return;
        var mods = new List<ModData> { Character("SheyPlus", "Shey") };
        var tables = CharacterInjector.Inject(build, usmap, mods);
        var dir = Directory.CreateTempSubdirectory("forge-char-").FullName;
        try
        {
            var path = Path.Combine(dir, "DT_NPCStaticData.uasset");
            tables["DT_NPCStaticData"].Write(path);
            var back = new UAsset(path, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4, new Usmap(usmap));
            Assert.Equal("Outfits_Shey_AB", Handle(Row(back, "SheyPlus"), "Outfits"));
            Assert.Equal("Outfits_Shey_AB", Handle(Row(back, "Shey"), "Outfits"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Outfit_source_points_to_another_list_without_changing_the_base()
    {
        if (GameData() is not var (build, usmap)) return;
        var mods = new List<ModData> { Character("SheyPlus", "Shey", outfits: "Aoi") };
        var tables = CharacterInjector.Inject(build, usmap, mods);
        Assert.Equal("Outfits_SexBot_AB_NSFW", Handle(Row(tables["DT_NPCStaticData"], "SheyPlus"), "Outfits"));
        Assert.Equal("Outfits_Shey_AB", Handle(Row(tables["DT_NPCStaticData"], "Shey"), "Outfits"));
    }

    [Fact]
    public void New_character_is_listed_even_when_its_template_is_hidden()
    {
        if (GameData() is not var (build, usmap)) return;
        var mods = new List<ModData> { Character("AlissaPlus", "Alissa", profile: "Alissa") };
        var tables = CharacterInjector.Inject(build, usmap, mods);
        var npc = Row(tables["DT_NPCData_Sandbox"], "AlissaPlus");
        Assert.Equal(0, npc.Value.OfType<IntPropertyData>().First(p => p.Name.ToString() == "Options").Value);
        Assert.Equal(1, Row(tables["DT_NPCData_Sandbox"], "RangerGuardSmall").Value.OfType<IntPropertyData>().First(p => p.Name.ToString() == "Options").Value);
    }

    [Fact]
    public void Outfit_source_accepts_a_list_name_without_a_static_row()
    {
        if (GameData() is not var (build, usmap)) return;
        var mods = new List<ModData> { Character("SheyPlus", "Shey", outfits: "SexBot") };
        var tables = CharacterInjector.Inject(build, usmap, mods);
        Assert.Equal("Outfits_SexBot_AB_NSFW", Handle(Row(tables["DT_NPCStaticData"], "SheyPlus"), "Outfits"));
    }

    [Theory]
    [InlineData("Shey", "Shey", "already exists")]
    [InlineData("EmptyShey", "Shey", "already exists")]
    [InlineData("NewOne", "NoSuchBase", "not found")]
    public void Refuses_bad_characters_and_leaves_the_tables_out(string id, string baseRow, string _)
    {
        if (GameData() is not var (build, usmap)) return;
        var mods = new List<ModData> { Character(id, baseRow) };
        var tables = CharacterInjector.Inject(build, usmap, mods);
        Assert.Empty(mods);
        Assert.Empty(tables);
    }

    [Fact]
    public void Nothing_loaded_without_character_mods()
        => Assert.Empty(CharacterInjector.Inject("Z:/does/not/exist", "Z:/none.usmap", new List<ModData>()));

    [Fact]
    public void Missing_build_data_is_a_mod_error_not_a_crash()
    {
        var mods = new List<ModData> { Character("SheyPlus", "Shey") };
        var tables = CharacterInjector.Inject("Z:/does/not/exist", "Z:/none.usmap", mods);
        Assert.Empty(mods);
        Assert.Empty(tables);
    }

    [Fact]
    public void Outfit_set_expands_to_port_entries()
    {
        if (GameData() is not var (build, usmap)) return;
        var mods = new List<ModData>
        {
            new() { Variant = ModVariant.OutfitSet, ModId = "share", OutfitsFrom = "Alissa", OutfitsTo = "Maya" },
            new() { Variant = ModVariant.Add, ModId = "extra", Character = "Alissa", ClothingId = "My_New_Outfit" },
        };

        CharacterInjector.PrepareOutfitMods(build, usmap, mods);

        var ports = mods.Where(m => m.Variant == ModVariant.Port).ToList();
        // Alissa has 17 outfits, 3 already on Maya, plus the one the other mod adds.
        Assert.Equal(15, ports.Count);
        Assert.All(ports, p => Assert.Equal("Maya", p.Character));
        Assert.Contains(ports, p => p.ClothingId == "My_New_Outfit");
        Assert.Equal(ports.Count, ports.Select(p => p.ClothingId).Distinct().Count());
    }

    [Fact]
    public void Outfit_set_with_unknown_character_is_dropped()
    {
        if (GameData() is not var (build, usmap)) return;
        var mods = new List<ModData> { new() { Variant = ModVariant.OutfitSet, ModId = "x", OutfitsFrom = "Nobody", OutfitsTo = "Maya" } };
        CharacterInjector.PrepareOutfitMods(build, usmap, mods);
        Assert.Empty(mods);
    }

    [Fact]
    public void Outfit_set_between_characters_sharing_a_list_is_dropped()
    {
        if (GameData() is not var (build, usmap)) return;
        var mods = new List<ModData> { new() { Variant = ModVariant.OutfitSet, ModId = "x", OutfitsFrom = "Shey", OutfitsTo = "EmptyShey" } };
        CharacterInjector.PrepareOutfitMods(build, usmap, mods);
        Assert.Empty(mods);
    }

    [Fact]
    public void Add_mod_for_a_new_character_goes_to_its_list()
    {
        if (GameData() is not var (build, usmap)) return;
        var mods = new List<ModData>
        {
            Character("SheyPlus", "Shey"),
            new() { Variant = ModVariant.Add, ModId = "outfit", Character = "SheyPlus", ClothingId = "Mine" },
        };
        CharacterInjector.PrepareOutfitMods(build, usmap, mods);
        Assert.Equal("Shey", mods[1].Character);
    }
}
