using System.Text.Json;
using Wiretap.Maui.UI;
using Xunit;

namespace Wiretap.Maui.Tests;

public class BodyTextFormatterTests
{
    [Fact]
    public void LargeJsonKeepsItsLastPropertyAndRemainsReadable()
    {
        var body = JsonSerializer.Serialize(new
        {
            items = Enumerable.Range(0, 2_000).Select(index => new { index, title = $"Груз №{index}" }),
            tail = "LAST_VISIBLE_VALUE"
        });
        Assert.True(body.Length > 32_768);

        var rows = BodyTextFormatter.CreateRows(body, BodyFormatting.IndentedJson);
        using var document = JsonDocument.Parse(string.Join('\n', rows));

        Assert.Equal("LAST_VISIBLE_VALUE", document.RootElement.GetProperty("tail").GetString());
        Assert.Equal(2_000, document.RootElement.GetProperty("items").GetArrayLength());
        Assert.Contains(rows, row => row.Contains("Груз №1999"));
        Assert.All(rows, row => Assert.InRange(row.Length, 1, 1_024));
    }

    [Fact]
    public void LongUnbrokenBodyIsBoundedPerRowWithoutLosingCharacters()
    {
        var body = new string('x', 1_048_576) + "LAST_VISIBLE_VALUE";
        var rows = BodyTextFormatter.CreateRows(body, BodyFormatting.Original);

        Assert.Equal(body, string.Concat(rows));
        Assert.All(rows, row => Assert.InRange(row.Length, 1, 1_024));
    }

    [Fact]
    public void RowBoundaryDoesNotSplitUnicodeSurrogatePairs()
    {
        var body = new string('x', 1_023) + "🚚" + new string('я', 2_000) + "🚛";
        var rows = BodyTextFormatter.CreateRows(body, BodyFormatting.Original);

        Assert.Equal(body, string.Concat(rows));
        Assert.All(rows, row =>
        {
            Assert.False(char.IsLowSurrogate(row[0]));
            Assert.False(char.IsHighSurrogate(row[^1]));
        });
    }

    [Theory]
    [InlineData("first\nsecond\nthird")]
    [InlineData("first\r\nsecond\r\nthird")]
    [InlineData("first\rsecond\rthird")]
    public void TextLinesRemainSeparate(string body)
    {
        Assert.Equal(["first", "second", "third"],
            BodyTextFormatter.CreateRows(body, BodyFormatting.Original));
    }

    [Fact]
    public void BlankLinesHaveMeasurableRows()
    {
        Assert.Equal(["first", "\u00a0", "last"],
            BodyTextFormatter.CreateRows("first\n\nlast", BodyFormatting.Original));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingBodyKeepsTheExistingPlaceholder(string? body)
    {
        Assert.Equal(["(no body)"], BodyTextFormatter.CreateRows(body, BodyFormatting.IndentedJson));
    }

    [Fact]
    public void IncompleteCapturedJsonKeepsTheAvailableTail()
    {
        var body = "{\"value\":\"" + new string('x', 40_000) + "CAPTURED_TAIL";
        var rows = BodyTextFormatter.CreateRows(body, BodyFormatting.IndentedJson);

        Assert.Equal(body, string.Concat(rows));
    }

    [Fact]
    public void OriginalFormattingDoesNotPrettyPrintJson()
    {
        const string body = "{\"city\":\"Алматы\",\"value\":1}";
        Assert.Equal([body], BodyTextFormatter.CreateRows(body, BodyFormatting.Original));
    }

    [Fact]
    public void BinaryBodyKeepsTheExistingPlaceholder()
    {
        var rows = BodyTextFormatter.CreateRows(new string('\0', 20), BodyFormatting.IndentedJson);
        Assert.Equal(["(binary content, 20 bytes)"], rows);
    }
}
