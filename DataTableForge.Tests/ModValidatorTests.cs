using System.Collections.Generic;
using Xunit;

public class ModValidatorTests {
    static ModData NewAddMod() => new ModData {
        ModId = "mod_test",
        SourcePath = @"C:\fake\mod_test\file.txt",
        ModName = "Test",
        Variant = ModVariant.Add,
        ClothingId = "Test_001",
        ClothingName = "A Test",
        NameMapEntries = new[] { "Foo", "Bar" },
    };

    [Fact]
    public void Validate_returns_empty_list_for_a_well_formed_Add_mod() {
        var errors = ModValidator.Validate(NewAddMod());
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_flags_empty_NameMap_entries_for_any_variant() {
        var mod = NewAddMod();
        mod.NameMapEntries = new[] { "Foo", "", "Bar" };

        var errors = ModValidator.Validate(mod);

        Assert.Contains(errors, e => e.Contains("NameMap") && e.Contains("empty"));
    }

    [Fact]
    public void Validate_flags_whitespace_only_NameMap_entries() {
        var mod = NewAddMod();
        mod.NameMapEntries = new[] { "Foo", "   ", "Bar" };

        var errors = ModValidator.Validate(mod);

        Assert.Contains(errors, e => e.Contains("NameMap"));
    }

    [Fact]
    public void Validate_flags_null_NameMap_entries() {
        var mod = NewAddMod();
        mod.NameMapEntries = new string[] { "Foo", null!, "Bar" };

        var errors = ModValidator.Validate(mod);

        Assert.Contains(errors, e => e.Contains("NameMap"));
    }

    [Fact]
    public void Validate_flags_empty_ClothingId_for_Add_variant() {
        var mod = NewAddMod();
        mod.ClothingId = "";

        var errors = ModValidator.Validate(mod);

        Assert.Contains(errors, e => e.Contains("ClothingId"));
    }

    [Fact]
    public void Validate_does_not_flag_empty_ClothingId_for_non_Add_variants() {
        var mod = NewAddMod();
        mod.Variant = ModVariant.Tattoo;
        mod.ClothingId = "";  // unused for Tattoo
        mod.TattooEntries = new List<TattooData> {
            new TattooData { TattooId = "T_001", DisplayName = "X" }
        };

        var errors = ModValidator.Validate(mod);

        Assert.DoesNotContain(errors, e => e.Contains("ClothingId"));
    }

    [Fact]
    public void Validate_flags_empty_TattooId_on_any_TattooEntry_for_Tattoo_variant() {
        var mod = NewAddMod();
        mod.Variant = ModVariant.Tattoo;
        mod.ClothingId = "";
        mod.TattooEntries = new List<TattooData> {
            new TattooData { TattooId = "T_001" },
            new TattooData { TattooId = "" },  // bad
        };

        var errors = ModValidator.Validate(mod);

        Assert.Contains(errors, e => e.Contains("TattooId"));
    }

    [Fact]
    public void Validate_flags_empty_Character_for_Port_variant() {
        var mod = NewAddMod();
        mod.Variant = ModVariant.Port;
        mod.ClothingId = "";
        mod.Character = "";

        var errors = ModValidator.Validate(mod);

        Assert.Contains(errors, e => e.Contains("Character"));
    }

    [Fact]
    public void Validate_returns_multiple_errors_when_multiple_invariants_fail() {
        var mod = NewAddMod();
        mod.ClothingId = "";
        mod.NameMapEntries = new[] { "" };

        var errors = ModValidator.Validate(mod);

        Assert.True(errors.Count >= 2, $"expected >= 2 errors, got {errors.Count}: {string.Join(", ", errors)}");
    }

    [Fact]
    public void Validate_does_not_flag_empty_ClothingId_for_CharacterCustomization_variant() {
        // Pins the current no-op behavior — CharacterCustomization mods don't carry a ClothingId.
        // Prevents a future invariant addition from silently regressing this case.
        var mod = NewAddMod();
        mod.Variant = ModVariant.CharacterCustomization;
        mod.ClothingId = "";

        var errors = ModValidator.Validate(mod);

        Assert.DoesNotContain(errors, e => e.Contains("ClothingId"));
    }

    [Fact]
    public void CompleteNameMap_adds_the_clothing_id_and_set_slot_values_of_an_Add_mod() {
        var mod = NewAddMod();
        mod.Slots["CHEST"] = new SlotData { MeshPath = "/Game/X/SK_X", MeshName = "SK_X", MorphTarget = "Morph_X" };

        ModValidator.CompleteNameMap(mod);

        Assert.Contains("Test_001", mod.NameMapEntries);
        Assert.Contains("/Game/X/SK_X", mod.NameMapEntries);
        Assert.Contains("SK_X", mod.NameMapEntries);
        Assert.Contains("Morph_X", mod.NameMapEntries);
    }

    [Fact]
    public void CompleteNameMap_skips_unset_values_and_keeps_existing_entries_once() {
        var mod = NewAddMod();
        mod.NameMapEntries = new[] { "Test_001", "Foo" };
        mod.Slots["HEAD"] = new SlotData();

        ModValidator.CompleteNameMap(mod);

        Assert.Equal(new[] { "Test_001", "Foo" }, mod.NameMapEntries);
    }

    [Fact]
    public void CompleteNameMap_covers_customization_and_tattoo_entries() {
        var cust = new ModData { Variant = ModVariant.CharacterCustomization };
        cust.CustEntries.Add(new CustEntry { Target = "Hair", Path = "/Game/H", Name = "H", PhysicsPath = "/Game/P", PhysicsName = "P" });
        var tattoo = new ModData { Variant = ModVariant.Tattoo };
        tattoo.TattooEntries.Add(new TattooData { TattooId = "T1", TexturePath = "/Game/T", TextureName = "T", IconPath = "/Game/I", IconName = "I" });

        ModValidator.CompleteNameMap(cust);
        ModValidator.CompleteNameMap(tattoo);

        Assert.Equal(new[] { "/Game/H", "H", "/Game/P", "P" }, cust.NameMapEntries);
        Assert.Equal(new[] { "T1", "/Game/T", "T", "/Game/I", "I" }, tattoo.NameMapEntries);
    }

    [Fact]
    public void FindCharacterWarning_flags_an_outfit_for_an_unknown_character() {
        var mod = NewAddMod();
        mod.Character = "Nobody";
        Assert.Contains("Nobody", ModValidator.FindCharacterWarning(mod, new[] { "Maya" }));
    }

    [Fact]
    public void FindCharacterWarning_accepts_a_known_character_in_any_case() {
        var mod = NewAddMod();
        mod.Character = "maya";
        Assert.Null(ModValidator.FindCharacterWarning(mod, new[] { "Maya" }));
    }

    [Fact]
    public void FindCharacterWarning_ignores_variants_not_tied_to_a_character_list() {
        var mod = new ModData { Variant = ModVariant.Tattoo, Character = "" };
        Assert.Null(ModValidator.FindCharacterWarning(mod, new[] { "Maya" }));
    }

    [Fact]
    public void FindCharacterWarning_stays_silent_without_a_character_list() {
        var mod = NewAddMod();
        mod.Character = "Nobody";
        Assert.Null(ModValidator.FindCharacterWarning(mod, System.Array.Empty<string>()));
    }

    [Fact]
    public void FindSuspects_returns_the_mods_owning_the_name_quoted_in_the_error() {
        var good = NewAddMod();
        var bad = NewAddMod();
        bad.ModId = "mod_bad";
        bad.ClothingId = "Bad_Outfit";

        var suspects = ModValidator.FindSuspects("Attempt to serialize dummy FName 'Bad_Outfit' - never added", new[] { good, bad });

        Assert.Equal(new[] { "mod_bad" }, suspects);
    }

    [Fact]
    public void FindSuspects_is_empty_when_the_error_names_nothing_owned_by_a_mod() {
        Assert.Empty(ModValidator.FindSuspects("usmap: File signature mismatch", new[] { NewAddMod() }));
    }
}
