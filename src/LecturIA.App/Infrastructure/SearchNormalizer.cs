using System.Globalization;
using System.Text;

namespace LecturIA.App.Infrastructure;

/// <summary>
/// Normalizes user-facing strings for diacritic-insensitive,
/// case-insensitive substring matching. Used by the student search box
/// so that "max" finds "MAXIMO", "MAximo", and "MAXIMO".
/// </summary>
internal static class SearchNormalizer
{
    /// <summary>
    /// Returns a lowercase, diacritic-stripped form of <paramref name="value"/>.
    /// The result is suitable for <see cref="string.Contains(string)"/> comparisons.
    /// </summary>
    /// <param name="value">String to normalize. <see langword="null"/> is treated as empty.</param>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Form D splits accented characters into base + combining mark, so we
        // can drop the combining marks (Unicode category NonSpacingMark) and
        // keep the base letters. Cedillas decompose the same way: "ç" -> "c"
        // + combining cedilla, which the loop below also strips.
        var decomposed = value.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant();
    }
}
