using Jega.Core;

namespace Jega.Core.Tests;

public class ContextTests
{
    // As SiteFetcher.ContextJson writes it: a 4 × 4 m house 6 m high on the lot, and a shed with no height.
    private const string Sample = """
        {"version":1,"units":"mm","origin":"address point","z":"DVR90","source":"…","buildings":[
          {"own":true,"bbr":"house","base":10000,"top":16000,"ring":[[-3000,-3000],[1000,-3000],[1000,1000],[-3000,1000]]},
          {"own":false,"bbr":null,"base":10000,"top":10000,"ring":[[0,0],[1000,0],[1000,1000]]}
        ]}
        """;

    [Fact]
    public void ReadsTheBuildings()
    {
        var b = Assert.Single(Context.Parse(Sample));
        Assert.True(b.Own);
        Assert.Equal("house", b.Bbr);
        Assert.Equal(6000, b.Height);
        Assert.Equal([new Point2(-3000, -3000), new Point2(1000, -3000), new Point2(1000, 1000), new Point2(-3000, 1000)], b.Ring);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":2,"units":"mm","buildings":[]}""")]
    [InlineData("""{"version":1,"units":"m","buildings":[]}""")]
    // Damaged: no buildings, a version that's text, a ring corner of one number.
    [InlineData("""{"version":1,"units":"mm"}""")]
    [InlineData("""{"version":"1","units":"mm","buildings":[]}""")]
    [InlineData("""{"version":1,"units":"mm","buildings":[{"own":true,"base":0,"top":1,"ring":[[0],[1,0],[1,1]]}]}""")]
    public void RefusesWhatIsNotAContextFile(string json) => Assert.Throws<ContextException>(() => Context.Parse(json));
}
