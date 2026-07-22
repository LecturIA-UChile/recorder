using System.IO;
using System.Reflection;
using System.Text;

namespace LecturIA.App.Infrastructure;

/// <summary>
/// Loads the embedded reading texts JSON document from the running
/// assembly's manifest resources.
/// </summary>
internal static class EmbeddedReadingTextsResource
{
    /// <summary>
    /// Logical name of the embedded resource. Kept in sync with
    /// <c>LogicalName</c> in <c>LecturIA.App.csproj</c>.
    /// </summary>
    private const string ResourceName = "LecturIA.App.Assets.reading-texts.json";

    /// <summary>Returns the embedded reading texts document as text.</summary>
    /// <exception cref="InvalidOperationException">
    /// The resource is not present in the assembly. Unlike the distributor
    /// key list, the reading texts are essential content, so a missing
    /// resource is a build error rather than a valid fail-closed state.
    /// </exception>
    public static string Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
