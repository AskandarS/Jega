using System.Text.Json;
using Jega.Core;

namespace Jega.Core.Tests;

public class ConfigTests
{
    // The config is meant to be read from JSON later: every value must survive the round trip.
    [Fact]
    public void ReadsBackFromJson()
    {
        var json = JsonSerializer.Serialize(new Config());
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<Config>(json)));
        Assert.Contains("\"AFSTAND_SKEL\"", json);
        Assert.Contains("\"Jega Line\"", json);
    }

    [Fact]
    public void AMissingValueKeepsItsDefault()
    {
        var c = JsonSerializer.Deserialize<Config>("""{"Revit":{"CrossMm":3}}""")!;
        Assert.Equal(3, c.Revit.CrossMm);
        Assert.Equal("Jega Grid", c.Revit.GridFrame.Name);
        Assert.Equal(2.5, c.Siteplan.SetbackM);
    }

    // Settings > Layers names each layer by Layer_<name> (looked up by name, so the build can't see one missing; the
    // window would then show the bare layer name). A layer added to Config needs its words in both languages.
    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.da.resx")]
    public void EveryLayerHasItsNameInSettings(string resx)
    {
        var names = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Revit", resx)).Root!
            .Elements("data").Select(d => (string?)d.Attribute("name")).ToHashSet();
        foreach (var layer in new Config().Siteplan.Layers)
        {
            Assert.True(names.Contains("Layer_" + layer.Name), $"{resx}: no Layer_{layer.Name}");
        }
    }
}
