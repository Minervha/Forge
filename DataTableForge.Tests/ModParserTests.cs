using System.IO;
using Xunit;

public class ModParserTests {
    [Fact]
    public void Parse_sets_SourcePath_to_the_input_txt_path() {
        var tmp = Path.Combine(Path.GetTempPath(), $"forge-test-{System.Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, """
            Variant: Add
            ModName: TestMod
            ClothingId: Test_001
            ClothingName: A Test Hat
            """);
        try {
            var mod = ModParser.Parse(tmp);
            Assert.Equal(tmp, mod.SourcePath);
        } finally {
            File.Delete(tmp);
        }
    }
}
