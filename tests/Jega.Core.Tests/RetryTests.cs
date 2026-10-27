using Jega.Core;

namespace Jega.Core.Tests;

public class RetryTests
{
    [Fact]
    public void AServerErrorIsTriedTwiceMore()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), Retry.Wait(1, 500));
        Assert.Equal(TimeSpan.FromSeconds(2), Retry.Wait(2, 503));
        Assert.Null(Retry.Wait(3, 500));
    }

    [Fact]
    public void NoAnswerIsTriedAgain()
    {
        // A network error or a timeout: as a server error.
        Assert.Equal(TimeSpan.FromSeconds(1), Retry.Wait(1, null));
        Assert.Null(Retry.Wait(3, null));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public void AnAnswerThatWontChangeIsNotTriedAgain(int status) => Assert.Null(Retry.Wait(1, status));

    [Fact]
    public void TooManyCallsWaitsAsAskedAtMost5Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), Retry.Wait(1, 429, TimeSpan.FromSeconds(3)));
        Assert.Equal(TimeSpan.FromSeconds(5), Retry.Wait(1, 429, TimeSpan.FromSeconds(60)));
        // Not said, or a date already past: as a server error.
        Assert.Equal(TimeSpan.FromSeconds(1), Retry.Wait(1, 429));
        Assert.Equal(TimeSpan.FromSeconds(2), Retry.Wait(2, 429, TimeSpan.FromSeconds(-4)));
        Assert.Null(Retry.Wait(3, 429, TimeSpan.FromSeconds(1)));
    }
}
