using LecturIA.Core.Models;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Provides the fixed set of reading passages embedded in the application.
/// </summary>
public interface IReadingTextProvider
{
    /// <summary>
    /// Returns every reading text, ordered by level and then by title.
    /// </summary>
    IReadOnlyList<ReadingText> GetAll();
}
