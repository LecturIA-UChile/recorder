using System.Globalization;

namespace LecturIA.Infrastructure.Crypto;

/// <summary>
/// Parses the embedded distributor public key list into raw Ed25519 public
/// keys.
/// </summary>
/// <remarks>
/// The list format is deliberately trivial so it can be hand-edited during
/// a key ceremony: one base64-encoded 32-byte Ed25519 public key per line.
/// Blank lines are ignored; a <c>#</c> starts a comment that runs to the
/// end of the line, so each key can carry a human-readable distributor
/// label.
/// </remarks>
public static class AdminPublicKeySet
{
    /// <summary>Raw byte length of an Ed25519 public key.</summary>
    public const int PublicKeyLength = 32;

    /// <summary>
    /// Parses <paramref name="text"/> into the list of valid Ed25519
    /// public keys it contains.
    /// </summary>
    /// <param name="text">Contents of the embedded key list. May be
    /// <see langword="null"/> or empty, which yields an empty list.</param>
    /// <returns>One 32-byte array per accepted key, in file order.</returns>
    /// <exception cref="FormatException">
    /// A non-comment, non-blank line is not valid base64 or does not
    /// decode to exactly <see cref="PublicKeyLength"/> bytes. A malformed
    /// key list is a build/ceremony error and must fail loudly rather than
    /// silently dropping a distributor.
    /// </exception>
    public static IReadOnlyList<byte[]> Parse(string? text)
    {
        var keys = new List<byte[]>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return keys;
        }

        var lines = text.Split('\n');
        for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            var line = lines[lineNumber];

            var commentStart = line.IndexOf('#', StringComparison.Ordinal);
            if (commentStart >= 0)
            {
                line = line[..commentStart];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(line);
            }
            catch (FormatException ex)
            {
                throw new FormatException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Admin public key list line {0} is not valid base64.",
                        lineNumber + 1),
                    ex);
            }

            if (key.Length != PublicKeyLength)
            {
                throw new FormatException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Admin public key list line {0} decodes to {1} bytes; expected {2}.",
                        lineNumber + 1,
                        key.Length,
                        PublicKeyLength));
            }

            keys.Add(key);
        }

        return keys;
    }
}
