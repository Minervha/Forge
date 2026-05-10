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
}
