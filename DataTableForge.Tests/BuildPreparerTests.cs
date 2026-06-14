using Xunit;

public class BuildPreparerTests
{
    [Fact]
    public void Maps_only_the_four_injectable_datatables()
    {
        var keys = BuildPreparer.AutoModDtPaths.Keys;
        Assert.Equal(4, BuildPreparer.AutoModDtPaths.Count);
        Assert.Contains("DT_ClothesOutfit", keys);
        Assert.Contains("DT_GameCharacterOutfits", keys);
        Assert.Contains("DT_GameCharacterCustomization", keys);
        Assert.Contains("DT_Tattoo", keys);
        // Passthrough tables must NOT be emitted (would change pak output).
        Assert.DoesNotContain("DT_GFur", keys);
        Assert.DoesNotContain("DT_SandboxProps", keys);
    }

    [Theory]
    [InlineData("DT_ClothesOutfit", "WildLifeC/Content/DataTables/DT_ClothesOutfit.uasset")]
    [InlineData("DT_GameCharacterOutfits", "WildLifeC/Content/DataTables/NPC/DT_GameCharacterOutfits.uasset")]
    [InlineData("DT_GameCharacterCustomization", "WildLifeC/Content/DataTables/NPC/DT_GameCharacterCustomization.uasset")]
    [InlineData("DT_Tattoo", "WildLifeC/Content/DataTables/DT_Tattoo.uasset")]
    public void AutoModAssetPath_places_uasset_under_AutoMod_P(string dtName, string expectedRel)
    {
        var path = BuildPreparer.AutoModAssetPath("/build", dtName).Replace('\\', '/');
        Assert.Equal($"/build/AutoMod_P/{expectedRel}", path);
    }
}
