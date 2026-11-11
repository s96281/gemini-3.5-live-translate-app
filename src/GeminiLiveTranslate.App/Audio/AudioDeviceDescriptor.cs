namespace GeminiLiveTranslate.App.Audio;

/// <summary>
/// Describes a native audio capture device (e.g. microphone or audio endpoint).
/// </summary>
public class AudioDeviceDescriptor
{
    /// <summary>
    /// Gets or sets the unique device endpoint ID (e.g. Windows MMDevice ID).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable friendly name of the device.
    /// </summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is the system default capture device.
    public bool IsDefault { get; set; }

    public AudioDeviceDescriptor() { }

    public AudioDeviceDescriptor(string id, string friendlyName, bool isDefault = false)
    {
        Id = id;
        FriendlyName = friendlyName;
        IsDefault = isDefault;
    }

    public override string ToString() => FriendlyName;
}
