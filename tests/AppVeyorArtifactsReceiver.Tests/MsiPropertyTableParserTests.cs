using AppVeyorArtifactsReceiver.Metadata;

namespace AppVeyorArtifactsReceiver.Tests;

public sealed class MsiPropertyTableParserTests
{
    [Fact]
    public void Parses_selected_properties_and_ignores_other_rows()
    {
        string export = Export(
            "Manufacturer\tNefarius",
            "ProductCode\t{23170F69-40C1-2702-0000-000004000000}",
            "ProductName\tDsHidMini",
            "ProductVersion\t1.2.3",
            "ALLUSERS\t2");

        Assert.True(MsiPropertyTableParser.TryParse(export, out Dictionary<string, string>? properties, out string? error));
        Assert.Null(error);
        Assert.NotNull(properties);
        Assert.Equal("1.2.3", properties["ProductVersion"]);
        Assert.Equal("DsHidMini", properties["ProductName"]);
        Assert.Equal("Nefarius", properties["Manufacturer"]);
        Assert.Equal("{23170F69-40C1-2702-0000-000004000000}", properties["ProductCode"]);
        Assert.Equal("2", properties["ALLUSERS"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Property\tValue\r\ns72\tl0")]
    [InlineData("Name\tData\r\ns72\ts0\r\nName\tName\r\n")]
    [InlineData("Property\tValue\r\ns72\tl0\r\nFile\tFile\r\nProductVersion\t1\r\n")]
    [InlineData("Property\tValue\r\ns72\tl0\r\nProperty\tProperty\r\nProductVersion\r\n")]
    public void Rejects_malformed_exports(string export)
    {
        Assert.False(MsiPropertyTableParser.TryParse(export, out Dictionary<string, string>? properties, out string? error));
        Assert.Null(properties);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private static string Export(params string[] rows)
    {
        return "Property\tValue\r\ns72\tl0\r\nProperty\tProperty\r\n" + string.Join("\r\n", rows) + "\r\n";
    }
}
