using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Storage;

/// <summary>
/// Persists the list of courses (each containing its students) as a JSON
/// document under <c>%LOCALAPPDATA%\LecturIA\</c>.
/// </summary>
/// <remarks>
/// The current schema groups students by course. Two earlier formats are
/// migrated transparently on load:
/// <list type="bullet">
///   <item>A flat <c>{ "students": [...] }</c> list of student records.</item>
///   <item>A flat <c>{ "nombres": ["First Last", ...] }</c> list of strings.</item>
/// </list>
/// In both cases the students are wrapped into a single course with empty
/// metadata, which the user can re-import to populate. The next save
/// rewrites the file in the new format. The file holds personally
/// identifiable information and is never shipped with the application or
/// committed to source control.
/// </remarks>
public sealed class JsonCourseRepository : ICourseRepository
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
    public JsonCourseRepository(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Course>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<Course>();
        }

        await using var stream = File.OpenRead(_filePath);
        var payload = await JsonSerializer.DeserializeAsync<Payload>(
            stream, SerializerOptions, cancellationToken).ConfigureAwait(false);

        if (payload is null)
        {
            return Array.Empty<Course>();
        }

        if (payload.Courses is { Count: > 0 })
        {
            return payload.Courses
                .Where(static c => c is not null)
                .Select(static c => new Course(
                    c!.Id == Guid.Empty ? Guid.NewGuid() : c.Id,
                    c.School ?? string.Empty,
                    c.Level ?? string.Empty,
                    c.Section ?? string.Empty,
                    MapStudents(c.Students)))
                .ToList();
        }

        // Legacy: flat student records. Wrap into a single course with empty metadata.
        if (payload.Students is { Count: > 0 })
        {
            var students = MapStudents(payload.Students);
            return students.Count == 0
                ? Array.Empty<Course>()
                : new List<Course> { Course.CreateNew(string.Empty, string.Empty, string.Empty, students) };
        }

        // Legacy: flat name strings. Split on the first whitespace to recover names.
        if (payload.Nombres is { Count: > 0 })
        {
            var students = payload.Nombres
                .Where(static n => !string.IsNullOrWhiteSpace(n))
                .Select(static n =>
                {
                    var parts = n!.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    return new Student(string.Empty, parts[0], parts.Length > 1 ? parts[1] : string.Empty);
                })
                .ToList();

            return students.Count == 0
                ? Array.Empty<Course>()
                : new List<Course> { Course.CreateNew(string.Empty, string.Empty, string.Empty, students) };
        }

        return Array.Empty<Course>();
    }

    /// <inheritdoc />
    public async Task SaveAsync(IEnumerable<Course> courses, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(courses);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new Payload
        {
            Courses = courses
                .Select(static c => new CourseRecord
                {
                    Id = c.Id,
                    School = c.School,
                    Level = c.Level,
                    Section = c.Section,
                    Students = c.Students
                        .Select(static s => new StudentRecord
                        {
                            Rut = s.Rut,
                            FirstName = s.FirstName,
                            LastName = s.LastName,
                        })
                        .ToList<StudentRecord?>(),
                })
                .ToList<CourseRecord?>(),
        };

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<Student> MapStudents(List<StudentRecord?>? records)
    {
        if (records is null)
        {
            return new List<Student>();
        }

        return records
            .Where(static r => r is not null)
            .Select(static r => new Student(
                r!.Rut ?? string.Empty,
                r!.FirstName ?? string.Empty,
                r!.LastName ?? string.Empty))
            .ToList();
    }

    private sealed class Payload
    {
        /// <summary>Current schema: list of courses with their students.</summary>
        [JsonPropertyName("courses")]
        public List<CourseRecord?>? Courses { get; set; }

        /// <summary>Legacy schema (pre-courses): flat list of student records.</summary>
        [JsonPropertyName("students")]
        public List<StudentRecord?>? Students { get; set; }

        /// <summary>Oldest legacy schema: flat list of full-name strings.</summary>
        [JsonPropertyName("nombres")]
        public List<string?>? Nombres { get; set; }
    }

    private sealed class CourseRecord
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("school")]
        public string? School { get; set; }

        [JsonPropertyName("level")]
        public string? Level { get; set; }

        [JsonPropertyName("section")]
        public string? Section { get; set; }

        [JsonPropertyName("students")]
        public List<StudentRecord?>? Students { get; set; }
    }

    private sealed class StudentRecord
    {
        [JsonPropertyName("rut")]
        public string? Rut { get; set; }

        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }
    }
}
