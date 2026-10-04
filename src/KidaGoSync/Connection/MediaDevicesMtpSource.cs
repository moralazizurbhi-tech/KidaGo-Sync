using MediaDevices;

namespace KidaGoSync.Connection;

/// <summary>
/// MTP presence through the MediaDevices library (a managed wrapper over Windows' own WPD API, so no driver or extra
/// runtime is needed). Chosen here because the TD leaves the library to implementation; file access for Importar and
/// Exportar can use the same library.
/// </summary>
public sealed class MediaDevicesMtpSource : IMtpDeviceSource
{
    public IReadOnlyList<PdaDevice> ListDevices() =>
        (MediaDeviceManager.Instance.GetDevices() ?? [])
            .Select(d => new PdaDevice(d.DeviceId, string.IsNullOrWhiteSpace(d.FriendlyName) ? d.Description ?? d.DeviceId : d.FriendlyName))
            .ToList();
}
