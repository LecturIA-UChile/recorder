using LecturIA.Core.Recording;

namespace LecturIA.Core.Abstractions;

/// <summary>
/// Wraps a writable destination stream into an encrypting stream that
/// produces a LecturIA encrypted recording (<c>.lra</c>) on the fly.
/// </summary>
/// <remarks>
/// Encryption happens as bytes are written. The implementation is meant
/// to be plugged into the audio capture pipeline so the unencrypted
/// audio never touches the file system.
/// </remarks>
public interface IRecordingEncryptor
{
    /// <summary>
    /// Returns a new stream that encrypts everything written to it and
    /// forwards the resulting bytes to <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">
    /// Destination of the encrypted bytes. The returned stream takes
    /// ownership of <paramref name="destination"/>: disposing the returned
    /// stream finalizes the encrypted container (final frame plus end
    /// sentinel) and disposes the destination.
    /// </param>
    /// <param name="metadata">
    /// Optional descriptive metadata written into the recording header.
    /// When <see langword="null"/>, an empty metadata block is written.
    /// </param>
    /// <returns>A write-only stream producing the LecturIA encrypted format.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="destination"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is not writable.
    /// </exception>
    Stream WrapForWrite(Stream destination, RecordingMetadata? metadata = null);
}
