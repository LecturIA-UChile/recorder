namespace LecturIA.Infrastructure.Crypto;

/// <summary>
/// RFC 4648 base32 encoding without padding. Uppercase only; the alphabet
/// avoids characters that look alike (no <c>0/O</c> or <c>1/I</c>) and is
/// case-insensitive on every file system, which makes it safe to use in
/// recording file names.
/// </summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Encodes the supplied bytes as base32 without padding.
    /// </summary>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        var outputLength = ((data.Length * 8) + 4) / 5;
        var buffer = outputLength <= 256
            ? stackalloc char[outputLength]
            : new char[outputLength];

        var bitBuffer = 0;
        var bitCount = 0;
        var index = 0;

        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                buffer[index++] = Alphabet[(bitBuffer >> bitCount) & 0x1F];
            }
        }

        if (bitCount > 0)
        {
            buffer[index++] = Alphabet[(bitBuffer << (5 - bitCount)) & 0x1F];
        }

        return new string(buffer[..index]);
    }

    /// <summary>
    /// Decodes a base32 string (with or without padding) back to bytes.
    /// </summary>
    /// <exception cref="FormatException">
    /// The string contains characters outside the base32 alphabet.
    /// </exception>
    public static byte[] Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.AsSpan().TrimEnd('=');
        if (trimmed.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        var output = new byte[trimmed.Length * 5 / 8];
        var bitBuffer = 0;
        var bitCount = 0;
        var index = 0;

        foreach (var c in trimmed)
        {
            var ch = char.ToUpperInvariant(c);
            var digit = Alphabet.IndexOf(ch);
            if (digit < 0)
            {
                throw new FormatException($"Invalid base32 character: '{c}'.");
            }

            bitBuffer = (bitBuffer << 5) | digit;
            bitCount += 5;
            if (bitCount >= 8)
            {
                bitCount -= 8;
                output[index++] = (byte)((bitBuffer >> bitCount) & 0xFF);
            }
        }

        return output;
    }
}
