using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using Xunit;

public class AssetInjectorTests
{
    // Build a SoftObjectPropertyData mesh slot from raw package/asset strings, using
    // dummy FNames so no UAsset/INameMap is needed. "None" mirrors a shipped-empty slot.
    static SoftObjectPropertyData Slot(string packagePath, string assetName)
        => new SoftObjectPropertyData
        {
            Value = new FSoftObjectPath(
                FName.DefineDummy(null, packagePath, 0),
                FName.DefineDummy(null, assetName, 0),
                null),
        };

    [Fact]
    public void Fills_empty_sex_slot_from_regular_mesh()
    {
        var regular = Slot("/Game/Mesh/Hat", "SK_Hat");
        var sex = Slot("None", "None");

        var fill = AssetInjector.ShouldFillSexSlot(regular, sex, out var pkg, out var name);

        // After the pass, SetSoftObject(asset, sex, pkg, name) copies these into [1],
        // so [1]'s package/asset name end up equal to [0]'s.
        Assert.True(fill);
        Assert.Equal("/Game/Mesh/Hat", pkg);
        Assert.Equal("SK_Hat", name);
    }

    [Fact]
    public void Does_not_overwrite_an_already_set_sex_slot()
    {
        var regular = Slot("/Game/Mesh/Hat", "SK_Hat");
        var sex = Slot("/Game/Mesh/HatSex", "SK_HatSex");

        var fill = AssetInjector.ShouldFillSexSlot(regular, sex, out _, out _);

        Assert.False(fill);
    }

    [Fact]
    public void Does_not_fabricate_when_regular_mesh_is_also_empty()
    {
        var regular = Slot("None", "None");
        var sex = Slot("None", "None");

        var fill = AssetInjector.ShouldFillSexSlot(regular, sex, out _, out _);

        Assert.False(fill);
    }
}
