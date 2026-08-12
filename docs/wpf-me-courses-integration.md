# Integración WPF — `GET /v1/me/courses`

Guía para que la app WPF (.NET 8) muestre a un profesor autenticado sus propios
cursos con los estudiantes matriculados, consumiendo el endpoint del control
plane de LecturIA Export.

## Resumen

- El profesor **ya se autentica** con Amazon Cognito en la app.
- Con ese login, WPF obtiene un **access token** (JWT).
- WPF llama al endpoint con ese token en el header `Authorization`.
- El backend deriva la identidad del token y devuelve **solo** los cursos de ese
  profesor. No se envían credenciales AWS desde WPF.

## Endpoint

| | |
|---|---|
| Método | `GET` |
| URL | `https://api.lecturia.maxfloresv.cl/v1/me/courses` |
| URL alternativa | `https://2chpqjygsk.execute-api.us-east-1.amazonaws.com/v1/me/courses` |
| Autenticación | `Authorization: Bearer <access_token>` |
| Content-Type respuesta | `application/json` |

Ambas URLs apuntan al mismo API. Usa la de dominio propio; la de `execute-api`
queda como respaldo.

## El token: usar el ACCESS token (no el ID token)

Cognito devuelve tres tokens al autenticar: `IdToken`, `AccessToken` y
`RefreshToken`. Este endpoint valida el **access token** (el authorizer compara
el `client_id` del token contra el App Client). Debes enviar el `AccessToken`.

- No hardcodees el token: usa el que tu flujo de login ya obtiene en memoria.
- El access token expira (típicamente ~1 hora). Si está por vencer, refréscalo
  con el `RefreshToken` antes de llamar.
- Nunca lo persistas en texto plano ni lo registres en logs.

## Respuestas

### 200 OK

```json
{
  "courses": [
    {
      "id": "b3f1...uuid",
      "year": 2026,
      "section": "A",
      "institution": "Colegio Demo LecturIA",
      "level": "1 Basico",
      "students": [
        { "id": "uuid", "pseudonym": "R1-SEED-0001", "name": "Ana Perez" },
        { "id": "uuid", "pseudonym": "R1-SEED-0002", "name": "Beto Soto" }
      ]
    }
  ]
}
```

- `courses` puede venir vacío (`[]`) si el profesor no tiene cursos asignados.
- `students` puede venir vacío si el curso no tiene matriculados.

### Otros códigos

| Código | Significado | Qué hace WPF |
|---|---|---|
| `401` | Falta el token, está vencido o es inválido. | Re-autenticar / refrescar token y reintentar. |
| `403` | El usuario autenticado no está dado de alta como profesor (`not_a_professor`). | Mostrar mensaje: cuenta sin perfil de profesor. |
| `5xx` / timeout | Error transitorio (p. ej. la base recién despierta). | Reintentar 1 vez con backoff corto (ver más abajo). |

## Cold start: timeout y reintento

La base de datos puede estar en pausa para ahorrar costo y tarda unos segundos
en despertar en la **primera** llamada tras inactividad. Recomendaciones:

- `HttpClient.Timeout` en torno a **35 segundos**.
- Reintentar **una vez** ante `5xx` o timeout, con una espera corta (2–3 s).
  El segundo intento casi siempre responde porque la base ya quedó activa.
- No reintentar ante `401`/`403` (no son transitorios).

## Ejemplo en C# (.NET 8)

DTOs (System.Text.Json):

```csharp
public sealed record EnrolledStudent(string Id, string Pseudonym, string Name);

public sealed record Course(
    string Id,
    int Year,
    string Section,
    string Institution,
    string Level,
    IReadOnlyList<EnrolledStudent> Students);

public sealed record MyCourses(IReadOnlyList<Course> Courses);
```

Cliente:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class CoursesClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public CoursesClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://api.lecturia.maxfloresv.cl");
        _http.Timeout = TimeSpan.FromSeconds(35);
    }

    /// <summary>
    /// Returns the authenticated professor's courses with enrolled students.
    /// Pass the Cognito ACCESS token (not the ID token).
    /// </summary>
    public async Task<MyCourses> GetMyCoursesAsync(string accessToken, CancellationToken ct = default)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/me/courses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (TaskCanceledException) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct); // transient timeout: retry once
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Token invalido o vencido; re-autenticar.");

            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("La cuenta no esta dada de alta como profesor.");

            if ((int)response.StatusCode >= 500 && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct); // transient 5xx: retry once
                continue;
            }

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<MyCourses>(JsonOptions, ct);
            return result ?? new MyCourses(Array.Empty<Course>());
        }
    }
}
```

Uso:

```csharp
// accessToken proviene de tu flujo de login Cognito existente.
var client = new CoursesClient(new HttpClient());
MyCourses mine = await client.GetMyCoursesAsync(accessToken);

foreach (var course in mine.Courses)
{
    Console.WriteLine($"{course.Year} {course.Section} - {course.Level} ({course.Institution})");
    foreach (var student in course.Students)
        Console.WriteLine($"  - {student.Name} [{student.Pseudonym}]");
}
```

> Reutiliza una sola instancia de `HttpClient` (o usa `IHttpClientFactory`); no
> crees una por llamada.

## Prueba rápida sin WPF (opcional)

```powershell
$tok = "<access-token>"
curl.exe -s "https://api.lecturia.maxfloresv.cl/v1/me/courses" -H "Authorization: Bearer $tok"
```

Sin token (o token inválido) debe responder `401`.

## Notas de seguridad

- WPF nunca recibe ni usa credenciales AWS; solo el JWT de Cognito.
- El alcance es por identidad: el backend usa el `sub` del token; un profesor no
  puede ver cursos de otro.
- La respuesta incluye nombres de estudiantes (dato personal). Manéjala en
  memoria según la política de privacidad; no la registres en logs.

## Checklist de integración

- [ ] Enviar el **access token** (no el ID token) en `Authorization: Bearer`.
- [ ] `HttpClient.Timeout` ~35 s.
- [ ] Reintentar 1 vez ante `5xx`/timeout; no reintentar ante `401`/`403`.
- [ ] Refrescar el token si está por expirar.
- [ ] Manejar `courses` y `students` vacíos sin error.
- [ ] No loguear el token ni los nombres de estudiantes.
