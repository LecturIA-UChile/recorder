namespace LecturIA.Core.Models;

/// <summary>
/// A reading passage shown on screen for a student to read aloud during a
/// recording. Reading texts are a fixed, level-organized set embedded in
/// the application; teachers and distributors do not edit them.
/// </summary>
public sealed class ReadingText
{
    /// <summary>Stable identifier of the text (for example <c>el_paseo</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Title shown in the selector and above the body.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// Target school level: <c>1</c> for primero básico, <c>2</c> for
    /// segundo básico.
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// Full text of the passage. Paragraphs are separated by a blank line.
    /// </summary>
    public required string Body { get; init; }

    /// <summary>Level rendered as the Chilean grade label ("Primero Básico", "Segundo Básico").</summary>
    public string LevelLabel => Level switch
    {
        1 => "Primero Básico",
        2 => "Segundo Básico",
        _ => $"{Level}°",
    };

    /// <summary>Label shown in the selector, combining level and title.</summary>
    public string MenuLabel => $"{LevelLabel} · {Title}";
}
