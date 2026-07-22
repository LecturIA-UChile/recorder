using System.IO;
using System.Reflection;
using System.Text;

namespace LecturIA.App.Infrastructure;

/// <summary>
/// Loads the application's embedded UID key (32-byte secret encoded as
/// base64) from the running assembly's manifest resources.
/// </summary>
internal static class EmbeddedUidKeyResource
{
    /// <summary>
    /// Logical name of the embedded resource. Kept in sync with
    /// <c>LogicalName</c> in <c>LecturIA.App.csproj</c>.
    /// </summary>
    private const string ResourceName = "LecturIA.App.Assets.uid-key.txt";

    /// <summary>
    /// Returns the embedded UID key as raw bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The embedded resource is missing or malformed.
    /// </exception>
    public static byte[] Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found. The application binary is missing the UID key. "
                + "Generate one with the keys tool before building.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd().Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is empty. The application binary is missing the UID key.");
        }

        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is not valid base64.",
                ex);
        }
    }
}
