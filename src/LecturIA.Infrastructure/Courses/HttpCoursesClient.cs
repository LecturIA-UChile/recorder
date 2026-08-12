using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Courses;

/// <summary>
/// Reads the authenticated professor's courses from the LecturIA Export
/// control plane over HTTPS, authenticating with the Cognito access token.
/// </summary>
/// <remarks>
/// The database backing the endpoint may be paused to save cost and take a
/// few seconds to wake up on the first call after inactivity, so the client
/// uses a generous timeout and retries once on transient failures. The
/// response carries student names (personal data); this type maps them into
/// the in-memory domain model and never writes them to disk or logs.
/// </remarks>
public sealed class HttpCoursesClient : ICoursesClient, IDisposable
{
    /// <summary>Default base address of the control-plane API.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://api.lecturia.maxfloresv.cl");

    private const string MyCoursesPath = "/v1/me/courses";
    private const int MaxAttempts = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(35);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    /// <summary>
    /// Creates a client that owns a dedicated <see cref="HttpClient"/>
    /// configured with the default base address and timeout.
    /// </summary>
    public HttpCoursesClient()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Creates a client over the supplied <see cref="HttpClient"/>. The base
    /// address and timeout are set on it, so the instance must not be shared
    /// with callers that expect a different configuration.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for every request.</param>
    /// <param name="ownsHttpClient">
    /// Whether disposing this client should also dispose
    /// <paramref name="httpClient"/>.
    /// </param>
    public HttpCoursesClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _http.BaseAddress ??= DefaultBaseAddress;
        _http.Timeout = RequestTimeout;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Course>> GetMyCoursesAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, MyCoursesPath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A cancellation not requested by the caller is the client
                // timeout firing: the paused database is likely waking up.
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new CoursesRequestException(
                    CoursesRequestFailure.Unavailable,
                    "The request to the courses endpoint timed out.",
                    "No se pudieron cargar tus cursos a tiempo. Revisa tu conexión e inténtalo nuevamente.");
            }
            catch (HttpRequestException ex)
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new CoursesRequestException(
                    CoursesRequestFailure.Unavailable,
                    "The request to the courses endpoint failed at the transport layer.",
                    "No se pudieron cargar tus cursos. Revisa tu conexión e inténtalo nuevamente.",
                    ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new CoursesRequestException(
                        CoursesRequestFailure.Unauthorized,
                        "The courses endpoint rejected the access token (HTTP 401).",
                        "Tu sesión no es válida o expiró. Vuelve a iniciar sesión.");
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new CoursesRequestException(
                        CoursesRequestFailure.NotAProfessor,
                        "The authenticated account is not registered as a professor (HTTP 403).",
                        "Tu cuenta no está registrada como profesor. Contacta al administrador.");
                }

                if ((int)response.StatusCode >= 500)
                {
                    if (attempt < MaxAttempts)
                    {
                        await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new CoursesRequestException(
                        CoursesRequestFailure.Unavailable,
                        $"The courses endpoint returned HTTP {(int)response.StatusCode}.",
                        "El servicio no está disponible por ahora. Inténtalo nuevamente en unos segundos.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new CoursesRequestException(
                        CoursesRequestFailure.Unavailable,
                        $"The courses endpoint returned an unexpected HTTP {(int)response.StatusCode}.",
                        "No se pudieron cargar tus cursos. Inténtalo nuevamente.");
                }

                MyCoursesResponse? payload;
                try
                {
                    payload = await response.Content
                        .ReadFromJsonAsync<MyCoursesResponse>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    throw new CoursesRequestException(
                        CoursesRequestFailure.Unavailable,
                        "The courses endpoint returned a response that could not be parsed.",
                        "El servicio devolvió una respuesta inválida. Inténtalo nuevamente.",
                        ex);
                }

                return MapCourses(payload);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private static IReadOnlyList<Course> MapCourses(MyCoursesResponse? payload)
    {
        if (payload?.Courses is not { Count: > 0 } courses)
        {
            return Array.Empty<Course>();
        }

        var result = new List<Course>(courses.Count);
        foreach (var course in courses)
        {
            if (course is null)
            {
                continue;
            }

            result.Add(MapCourse(course));
        }

        return result;
    }

    private static Course MapCourse(ApiCourse course)
    {
        var id = Guid.TryParse(course.Id, out var parsed) ? parsed : Guid.NewGuid();

        var students = new List<Student>(course.Students?.Count ?? 0);
        foreach (var student in course.Students ?? Enumerable.Empty<ApiStudent?>())
        {
            if (student is null || string.IsNullOrWhiteSpace(student.Pseudonym))
            {
                continue;
            }

            students.Add(MapStudent(student));
        }

        return new Course(
            id,
            course.Institution ?? string.Empty,
            course.Level ?? string.Empty,
            course.Section ?? string.Empty,
            students)
        {
            Year = course.Year > 0 ? course.Year : null,
        };
    }

    private static Student MapStudent(ApiStudent student)
    {
        var (firstName, lastName) = SplitName(student.Name);
        return new Student(student.Rut?.Trim() ?? string.Empty, firstName, lastName)
        {
            RecordingId = student.Pseudonym!.Trim(),
        };
    }

    /// <summary>
    /// Splits a single display name into a first name and the remaining
    /// words as the surname. The endpoint returns the name as one field, so
    /// this is a best-effort split for the two-column roster; it keeps the
    /// full name recoverable through <see cref="Student.Name"/>.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (string.Empty, string.Empty);
        }

        var tokens = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1)
        {
            return (tokens[0], string.Empty);
        }

        return (tokens[0], string.Join(' ', tokens[1..]));
    }

    private sealed class MyCoursesResponse
    {
        [JsonPropertyName("courses")]
        public List<ApiCourse?>? Courses { get; init; }
    }

    private sealed class ApiCourse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("year")]
        public int Year { get; init; }

        [JsonPropertyName("section")]
        public string? Section { get; init; }

        [JsonPropertyName("institution")]
        public string? Institution { get; init; }

        [JsonPropertyName("level")]
        public string? Level { get; init; }

        [JsonPropertyName("students")]
        public List<ApiStudent?>? Students { get; init; }
    }

    private sealed class ApiStudent
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("pseudonym")]
        public string? Pseudonym { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("rut")]
        public string? Rut { get; init; }
    }
}
