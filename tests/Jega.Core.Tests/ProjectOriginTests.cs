using Jega.Core;

namespace Jega.Core.Tests;

public class ProjectOriginTests
{
    [Fact]
    public void Skagens_Museum_s_address_point_to_whole_metres() =>
        Assert.Equal((595129.0, 6399156.0), ProjectOrigin.Of(595128.91, 6399156.18));

    // A half goes up, not to the even metre (Math.Round's default).
    [Theory]
    [InlineData(595124.5, 595125.0)]
    [InlineData(595125.5, 595126.0)]
    [InlineData(595124.49, 595124.0)]
    [InlineData(595124.0, 595124.0)]
    public void A_half_goes_up(double e, double expected) =>
        Assert.Equal((expected, expected), ProjectOrigin.Of(e, e));
}
