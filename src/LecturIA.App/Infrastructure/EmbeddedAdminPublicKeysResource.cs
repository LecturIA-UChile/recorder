using System.IO;
using System.Reflection;
using System.Text;

namespace LecturIA.App.Infrastructure;

/// <summary>
/// Loads the application's embedded list of distributor Ed25519 public
/// keys (used to verify admin login tokens) from the running assembly's
/// manifest resources.
/// </summary>
internal static class EmbeddedAdminPublicKeysResource
{
    /// <summary>
    /// Logical name of the embedded resource. Kept in sync with
    /// <c>LogicalName</c> in <c>LecturIA.App.csproj</c>.
    /// </summary>
    private const string ResourceName = "LecturIA.App.Assets.admin-public-keys.txt";

    /// <summary>
    /// Returns the embedded key list as text.
    /// </summary>
    /// <remarks>
    /// A missing or empty resource is not fatal: it simply means no
    /// distributor can authenticate, so the application only ever runs in
    /// teacher mode. That is a valid (fail-closed) deployment, so this
    /// method returns an empty string instead of throwing.
    /// </remarks>
    public static string Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
