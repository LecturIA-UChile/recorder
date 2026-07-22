namespace LecturIA.Core.Recording;

/// <summary>
/// Descriptive, non-sensitive metadata written into the header of an
/// encrypted recording so downstream processing knows which reading
/// passage the audio corresponds to.
/// </summary>
/// <remarks>
/// Only the passage id is persisted. The id is a governed primary key
/// (see <c>thesis/reading-text-id-convention.md</c>) from which external
/// systems resolve the title, level, and body via the reading text
/// catalog, so title and level are intentionally not duplicated here.
/// </remarks>
/// <param name="TextId">Stable primary key of the reading passage (for example <c>el_paseo_n1</c>).</param>
public sealed record RecordingMetadata(string TextId);
