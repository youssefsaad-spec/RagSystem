using RagSystem.Text;

namespace RagSystem.Tests.Text;

public class TokenEstimatorTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]   // 0.29 rounds up
    [InlineData(3, 1)]   // 0.86
    [InlineData(4, 2)]   // 1.14
    [InlineData(7, 2)]   // exactly 2.0
    [InlineData(8, 3)]   // 2.29
    [InlineData(35, 10)] // exactly 10.0
    [InlineData(36, 11)] // 10.29
    [InlineData(3500, 1000)]
    public void Estimate_is_chars_divided_by_3_point_5_rounded_up(int length, int expected)
    {
        Assert.Equal(expected, TokenEstimator.Estimate(new string('a', length)));
    }
}
