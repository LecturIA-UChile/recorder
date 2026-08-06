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
    /// Target school level from <c>1</c> for primero básico through <c>4</c>
    /// for cuarto básico.
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// Full text of the passage. Paragraphs are separated by a blank line.
    /// </summary>
    public required string Body { get; init; }

    /// <summary>Level rendered as its Chilean grade label.</summary>
    public string LevelLabel => Level switch
    {
        1 => "Primero Básico",
        2 => "Segundo Básico",
        3 => "Tercero Básico",
        4 => "Cuarto Básico",
        _ => $"{Level}°",
    };

    /// <summary>Label shown in the selector, combining level and title.</summary>
    public string MenuLabel => $"{LevelLabel} · {Title}";
}
