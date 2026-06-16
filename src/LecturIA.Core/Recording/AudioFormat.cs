namespace LecturIA.Core.Recording;

/// <summary>
/// Container format used to persist a recording on disk.
/// </summary>
/// <remarks>
/// Both formats produce mono audio at 16 kHz, the input shape expected by
/// downstream speech models such as Whisper. <see cref="Wav"/> keeps the raw
/// PCM samples for maximum fidelity at a higher disk cost. <see cref="Mp3"/>
/// applies lossy compression and produces files roughly four times smaller,
/// which is more practical for archiving and sharing.
/// </remarks>
public enum AudioFormat
{
    /// <summary>Uncompressed PCM stored in a WAV container.</summary>
    Wav,

    /// <summary>Lossy MP3 compression at a bitrate suited for mono speech.</summary>
    Mp3,
}
