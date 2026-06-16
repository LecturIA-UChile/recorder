using LecturIA.Core.Recording;

namespace LecturIA.App.ViewModels;

/// <summary>
/// View model wrapper that pairs an <see cref="AudioFormat"/> value with a
/// localized label suitable for a UI selector.
/// </summary>
/// <param name="Value">Underlying format passed to the recorder.</param>
/// <param name="DisplayLabel">Human-readable description shown to the user.</param>
public sealed record AudioFormatOption(AudioFormat Value, string DisplayLabel);
