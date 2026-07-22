using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Storage;

/// <summary>
/// Persists the list of courses as a JSON document under
/// <c>%LOCALAPPDATA%\LecturIA\</c>. Students are stored as opaque UIDs
/// only; the repository never writes RUT or name in plain text.
/// </summary>
/// <remarks>
/// <para>
/// Two earlier on-disk schemas, both of which contained PII in plain
/// text, are migrated transparently on load and rewritten in the new
/// UID-only format. The legacy file at
/// <c>%LOCALAPPDATA%\LecturIA\names.json</c> is deleted after a
/// successful migration.
/// </para>
/// </remarks>
public sealed class JsonCourseRepository : ICourseRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _filePath;
    private readonly string? _legacyPath;
    private readonly IStudentUidCodec _uidCodec;
    private readonly ICourseUidCodec _courseUidCodec;

    /// <summary>
    /// Creates a repository bound to a specific JSON file and an optional
    /// legacy file used only for one-shot migration.
    /// </summary>
    /// <param name="filePath">Absolute path of the file used for persistence.</param>
    /// <param name="uidCodec">Codec used to encode and decode student UIDs.</param>
    /// <param name="courseUidCodec">Codec used to encode and decode course metadata UIDs.</param>
    /// <param name="legacyFilePath">
    /// Optional path of an older file (e.g. <c>names.json</c>) that
    /// contained PII in plain text. When provided and present on disk on
    /// load, its content is migrated and the legacy file is deleted.
    /// </param>
    public JsonCourseRepository(
        string filePath,
        IStudentUidCodec uidCodec,
        ICourseUidCodec courseUidCodec,
        string? legacyFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(uidCodec);
        ArgumentNullException.ThrowIfNull(courseUidCodec);

        _filePath = filePath;
        _uidCodec = uidCodec;
        _courseUidCodec = courseUidCodec;
        _legacyPath = string.IsNullOrWhiteSpace(legacyFilePath) ? null : legacyFilePath;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Course>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var migrated = await TryMigrateLegacyAsync(cancellationToken).ConfigureAwait(false);
        if (migrated is not null)
        {
            return migrated;
        }

        if (!File.Exists(_filePath))
        {
            return Array.Empty<Course>();
        }

        await using var stream = File.OpenRead(_filePath);
        var payload = await JsonSerializer.DeserializeAsync<Payload>(
            stream, SerializerOptions, cancellationToken).ConfigureAwait(false);

        if (payload?.Courses is not { Count: > 0 } records)
        {
            return Array.Empty<Course>();
        }

        var courses = new List<Course>(records.Count);
        foreach (var record in records)
        {
            if (record is null)
            {
                continue;
            }

            var (school, level, section) = _courseUidCodec.Decode(record.CourseUid ?? string.Empty);

            var students = new List<Student>(record.StudentUids?.Count ?? 0);
            foreach (var uid in record.StudentUids ?? Enumerable.Empty<string?>())
            {
                if (string.IsNullOrWhiteSpace(uid))
                {
                    continue;
                }

                students.Add(_uidCodec.Decode(uid));
            }

            courses.Add(new Course(
                record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
                school,
                level,
                section,
                students));
        }

        return courses;
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
                .Select(c => new CourseRecord
                {
                    Id = c.Id,
                    CourseUid = _courseUidCodec.Encode(c.School, c.Level, c.Section),
                    StudentUids = c.Students
                        .Select(_uidCodec.Encode)
                        .ToList<string?>(),
                })
                .ToList<CourseRecord?>(),
        };

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Course>?> TryMigrateLegacyAsync(CancellationToken cancellationToken)
    {
        if (_legacyPath is null || !File.Exists(_legacyPath))
        {
            return null;
        }

        // Best-effort migration: deserialize the legacy file with its old
        // shape, encode every student to a UID, persist in the new format,
        // then delete the legacy file. If anything fails halfway, the
        // legacy file stays in place and the next launch retries.
        LegacyPayload? legacy;
        await using (var stream = File.OpenRead(_legacyPath))
        {
            legacy = await JsonSerializer.DeserializeAsync<LegacyPayload>(
                stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        var courses = MigrateLegacy(legacy);

        await SaveAsync(courses, cancellationToken).ConfigureAwait(false);

        try
        {
            File.Delete(_legacyPath);
        }
        catch (IOException)
        {
            // Best effort: the legacy file is intentionally left to be
            // retried on next launch if deletion fails. The new file is
            // already in place, so this is non-fatal.
        }

        return courses;
    }

    private static List<Course> MigrateLegacy(LegacyPayload? legacy)
    {
        if (legacy is null)
        {
            return new List<Course>();
        }

        if (legacy.Courses is { Count: > 0 } records)
        {
            return records
                .Where(static c => c is not null)
                .Select(c => new Course(
                    c!.Id == Guid.Empty ? Guid.NewGuid() : c.Id,
                    c.School ?? string.Empty,
                    c.Level ?? string.Empty,
                    c.Section ?? string.Empty,
                    BuildStudents(c.Students)))
                .ToList();
        }

        if (legacy.Students is { Count: > 0 })
        {
            var students = BuildStudents(legacy.Students);
            return students.Count == 0
                ? new List<Course>()
                : new List<Course> { Course.CreateNew(string.Empty, string.Empty, string.Empty, students) };
        }

        if (legacy.Nombres is { Count: > 0 })
        {
            // Oldest schema: full-name strings without RUT. These records
            // cannot be migrated because the codec rejects students
            // without RUT, which is the desired behaviour. The course is
            // created empty so the user has to re-import the planilla.
            return new List<Course>();
        }

        return new List<Course>();
    }

    private static List<Student> BuildStudents(List<LegacyStudentRecord?>? records)
    {
        if (records is null)
        {
            return new List<Student>();
        }

        return records
            .Where(static r => r is not null && !string.IsNullOrWhiteSpace(r.Rut))
            .Select(static r => new Student(
                r!.Rut ?? string.Empty,
                r!.FirstName ?? string.Empty,
                r!.LastName ?? string.Empty))
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Current schema
    // -------------------------------------------------------------------------

    private sealed class Payload
    {
        [JsonPropertyName("courses")]
        public List<CourseRecord?>? Courses { get; set; }
    }

    private sealed class CourseRecord
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("courseUid")]
        public string? CourseUid { get; set; }

        [JsonPropertyName("studentUids")]
        public List<string?>? StudentUids { get; set; }
    }

    // -------------------------------------------------------------------------
    // Legacy schemas (read-only, used on first migration then deleted)
    // -------------------------------------------------------------------------

    private sealed class LegacyPayload
    {
        [JsonPropertyName("courses")]
        public List<LegacyCourseRecord?>? Courses { get; set; }

        [JsonPropertyName("students")]
        public List<LegacyStudentRecord?>? Students { get; set; }

        [JsonPropertyName("nombres")]
        public List<string?>? Nombres { get; set; }
    }

    private sealed class LegacyCourseRecord
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
        public List<LegacyStudentRecord?>? Students { get; set; }
    }

    private sealed class LegacyStudentRecord
    {
        [JsonPropertyName("rut")]
        public string? Rut { get; set; }

        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }
    }
}
