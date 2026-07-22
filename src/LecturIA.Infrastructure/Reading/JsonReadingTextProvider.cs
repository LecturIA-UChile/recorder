using System.Text.Json;
using System.Text.Json.Serialization;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Reading;

/// <summary>
/// <see cref="IReadingTextProvider"/> that parses the reading passages from
/// a JSON document (embedded in the application at build time).
/// </summary>
public sealed class JsonReadingTextProvider : IReadingTextProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IReadOnlyList<ReadingText> _texts;

    /// <summary>
    /// Parses the supplied JSON document into the reading text set.
    /// </summary>
    /// <param name="json">Contents of the reading texts JSON document.</param>
    /// <exception cref="FormatException">
    /// The document is empty, is not valid JSON, has no <c>texts</c> array,
    /// or contains a text missing its id, title, or body.
    /// </exception>
    public JsonReadingTextProvider(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new FormatException("The reading texts document is empty.");
        }

        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The reading texts document is not valid JSON.", ex);
        }

        if (document?.Texts is null)
        {
            throw new FormatException("The reading texts document has no 'texts' array.");
        }

        var texts = document.Texts
            .Select(Map)
            .OrderBy(t => t.Level)
            .ThenBy(t => t.Title, StringComparer.CurrentCulture)
            .ToList();

        // The id is the primary key used to reference a passage from a
        // recording's metadata, so a duplicate is a hard authoring error.
        var duplicate = texts
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new FormatException($"Duplicate reading text id: '{duplicate.Key}'.");
        }

        _texts = texts;
    }

    /// <inheritdoc />
    public IReadOnlyList<ReadingText> GetAll() => _texts;

    private static ReadingText Map(TextDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id) ||
            string.IsNullOrWhiteSpace(dto.Title) ||
            string.IsNullOrWhiteSpace(dto.Body))
        {
            throw new FormatException("A reading text is missing its id, title, or body.");
        }

        return new ReadingText
        {
            Id = dto.Id,
            Title = dto.Title,
            Level = dto.Level,
            Body = dto.Body,
        };
    }

    private sealed class Document
    {
        [JsonPropertyName("texts")]
        public List<TextDto>? Texts { get; set; }
    }

    private sealed class TextDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }
    }
}
