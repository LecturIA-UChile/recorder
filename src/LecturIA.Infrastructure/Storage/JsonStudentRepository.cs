using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Storage;

/// <summary>
/// Persists the list of students as a JSON document with the shape
/// <c>{ "nombres": [...] }</c>.
/// </summary>
/// <remarks>
/// The default storage path lives under <c>%LOCALAPPDATA%\LecturIA\</c>.
/// The file contains personally identifiable information and is never
/// shipped with the application or committed to source control.
/// </remarks>
public sealed class JsonStudentRepository : IStudentRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _filePath;

    /// <summary>
    /// Creates a repository bound to a specific JSON file.
    /// </summary>
    /// <param name="filePath">Absolute path of the file used for persistence.</param>
    public JsonStudentRepository(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Student>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<Student>();
        }

        await using var stream = File.OpenRead(_filePath);
        var payload = await JsonSerializer.DeserializeAsync<NamesPayload>(
            stream, SerializerOptions, cancellationToken).ConfigureAwait(false);

        if (payload?.Nombres is null || payload.Nombres.Count == 0)
        {
            return Array.Empty<Student>();
        }

        return payload.Nombres
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Select(static n => new Student(n.Trim()))
            .ToList();
    }

    /// <inheritdoc />
    public async Task SaveAsync(IEnumerable<Student> students, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(students);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new NamesPayload
        {
            Nombres = students.Select(s => s.Name).ToList(),
        };

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class NamesPayload
    {
        [JsonPropertyName("nombres")]
        public List<string>? Nombres { get; set; }
    }
}
