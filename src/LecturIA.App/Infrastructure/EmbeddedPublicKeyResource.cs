using System.IO;
using System.Reflection;
using System.Text;

namespace LecturIA.App.Infrastructure;

/// <summary>
/// Loads the application's embedded RSA public key (PEM SubjectPublicKeyInfo)
/// from the running assembly's manifest resources.
/// </summary>
internal static class EmbeddedPublicKeyResource
{
    /// <summary>
    /// Logical name of the embedded resource. Kept in sync with
    /// <c>LogicalName</c> in <c>LecturIA.App.csproj</c>.
    /// </summary>
    private const string ResourceName = "LecturIA.App.Assets.public-key.pem";

    /// <summary>
    /// Returns the embedded public key as PEM text.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The embedded resource is missing or empty, which means the build
    /// did not include the public key. The application cannot start in
    /// that state because every recording needs to be encrypted.
    /// </exception>
    public static string Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found. The application binary is missing the public key.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var pem = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is empty. The application binary is missing the public key.");
        }

        return pem;
    }
}
