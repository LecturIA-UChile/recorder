namespace LecturIA.Infrastructure.Audio;

internal sealed class DuplicatingWriteStream : Stream
{
    private readonly Stream _first;
    private readonly Stream _second;

    public DuplicatingWriteStream(Stream first, Stream second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (!first.CanWrite || !second.CanWrite)
        {
            throw new ArgumentException("Both destination streams must be writable.");
        }

        _first = first;
        _second = second;
    }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush()
    {
        _first.Flush();
        _second.Flush();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        _first.Write(buffer, offset, count);
        _second.Write(buffer, offset, count);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _first.Write(buffer);
        _second.Write(buffer);
    }
}
