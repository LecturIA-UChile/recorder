namespace LecturIA.Core.Recording;

/// <summary>
/// Describes an audio capture device available to the recorder.
/// </summary>
public sealed record AudioInputDevice
{
    /// <summary>
    /// Creates a device descriptor.
    /// </summary>
    /// <param name="deviceNumber">Index assigned by the operating system for the current session.</param>
    /// <param name="name">Display name reported by the audio driver.</param>
    public AudioInputDevice(int deviceNumber, string name)
    {
        DeviceNumber = deviceNumber;
        Name = name;
    }

    /// <summary>Index assigned by the operating system for the current session.</summary>
    public int DeviceNumber { get; }

    /// <summary>Display name reported by the audio driver.</summary>
    public string Name { get; }
}
