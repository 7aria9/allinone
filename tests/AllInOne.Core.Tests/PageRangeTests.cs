namespace AllInOne.Core.Tests;

public class PageRangeTests
{
    [Theory]
    [InlineData("", 4, new[] { 0, 1, 2, 3 })]
    [InlineData("*", 3, new[] { 0, 1, 2 })]
    [InlineData("1-3,5", 6, new[] { 0, 1, 2, 4 })]
    [InlineData("4-", 6, new[] { 3, 4, 5 })]
    [InlineData("-2", 6, new[] { 0, 1 })]
    [InlineData("3-1", 3, new[] { 2, 1, 0 })]
    [InlineData("2, 2, 1", 3, new[] { 1, 0 })]
    public void Parse_returns_zero_based_indexes(string text, int count, int[] expected) =>
        Assert.Equal(expected, PageRange.Parse(text, count));

    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("a")]
    public void Parse_rejects_invalid_pages(string text) =>
        Assert.ThrowsAny<Exception>(() => PageRange.Parse(text, 4));
}
