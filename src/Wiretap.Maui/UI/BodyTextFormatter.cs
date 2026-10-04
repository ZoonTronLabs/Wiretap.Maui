using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Wiretap.Maui.UI;

internal enum BodyFormatting
{
    Original,
    IndentedJson
}

/// <summary>Formats the captured body into small, virtualized text rows without discarding its tail.</summary>
internal static class BodyTextFormatter
{
    private const int MaxRowCharacters = 1_024;
    private static readonly JsonSerializerOptions PrettyPrintOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IReadOnlyList<string> CreateRows(string? body, BodyFormatting formatting)
    {
        var text = FormatBody(body, formatting);
        var rows = new List<string>(1 + text.Count(character => character == '\n'));
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            AddLine(rows, line);
        return rows.AsReadOnly();
    }

    private static string FormatBody(string? body, BodyFormatting formatting)
    {
        if (string.IsNullOrEmpty(body))
            return "(no body)";
        if (IsBinaryContent(body))
            return $"(binary content, {body.Length:N0} bytes)";
        return formatting switch
        {
            BodyFormatting.Original => body,
            BodyFormatting.IndentedJson => IndentJson(body),
            _ => throw new UnreachableException($"Unknown body formatting: {formatting}")
        };
    }

    private static string IndentJson(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(document.RootElement, PrettyPrintOptions);
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static void AddLine(List<string> rows, string line)
    {
        if (line.Length == 0)
        {
            rows.Add("\u00a0"); // Keep blank lines measurable in the native list.
            return;
        }
        for (var offset = 0; offset < line.Length;)
        {
            var length = Math.Min(MaxRowCharacters, line.Length - offset);
            if (offset + length < line.Length && char.IsHighSurrogate(line[offset + length - 1]) &&
                char.IsLowSurrogate(line[offset + length]))
                length--;
            rows.Add(line.Substring(offset, length));
            offset += length;
        }
    }

    private static bool IsBinaryContent(string text)
    {
        var sample = text.AsSpan(0, Math.Min(512, text.Length));
        var nonPrintable = 0;
        foreach (var character in sample)
            if (char.IsControl(character) && character is not ('\n' or '\r' or '\t'))
                nonPrintable++;
        return nonPrintable > sample.Length * 0.1;
    }
}
