using System.IO;
using KidaGoSync.Sync;
using MediaDevices;

namespace KidaGoSync.Connection;

/// <summary>File access on the PDA through the same MediaDevices library that detects it.</summary>
public sealed class MediaDevicesPdaStorage : IPdaStorage
{
    public Task SendFileAsync(PdaDevice device, string localPath, string folder, string fileName) =>
        OnDevice(device, mtp =>
        {
            var target = EnsureFolder(mtp, folder);
            var destination = target + "\\" + fileName;
            if (mtp.FileExists(destination)) mtp.DeleteFile(destination); // a same-name file is overwritten
            using var source = File.OpenRead(localPath);
            mtp.UploadFile(source, destination);
            return 0;
        });

    public Task<byte[]?> ReadFileAsync(PdaDevice device, string path) =>
        OnDevice(device, mtp =>
        {
            var full = FullPath(mtp, path);
            if (!mtp.FileExists(full)) return null;
            // MTP refuses to open a zero-length file (COM 0x800710D2), and an emptied list is a normal state, not an error.
            var declared = (long)mtp.GetFileInfo(full).Length;
            if (declared == 0) return [];
            using var buffer = new MemoryStream();
            mtp.DownloadFile(full, buffer);
            // The list is about to be emptied on the strength of this copy: a short read must fail, never pass as the list.
            if (buffer.Length != declared) throw new IOException($"Lectura incompleta de la lista ({buffer.Length} de {declared} bytes)");
            return buffer.ToArray();
        });

    public Task EmptyFileAsync(PdaDevice device, string path) =>
        OnDevice(device, mtp =>
        {
            // Emptied, not deleted: an empty file is what tells the mobile app an export happened; a missing one is not.
            var full = FullPath(mtp, path);
            if (mtp.FileExists(full)) mtp.DeleteFile(full);
            mtp.UploadFile(new MemoryStream(), full);
            return 0;
        });

    private static Task<T> OnDevice<T>(PdaDevice device, Func<MediaDevice, T> work) =>
        Task.Run(() =>
        {
            using var mtp = (MediaDeviceManager.Instance.GetDevices() ?? []).FirstOrDefault(d => d.DeviceId == device.Id)
                ?? throw new IOException("La PDA ya no está conectada");
            mtp.Connect();
            try
            {
                return work(mtp);
            }
            finally
            {
                mtp.Disconnect();
            }
        });

    /// <summary>The phone's shared storage is the first (normally only) storage the device lists.</summary>
    private static string StorageRoot(MediaDevice mtp) =>
        mtp.GetRootDirectory().EnumerateDirectories().FirstOrDefault()?.FullName
            ?? throw new IOException("La PDA no expone almacenamiento");

    private static string FullPath(MediaDevice mtp, string path) =>
        StorageRoot(mtp) + "\\" + path.Replace('/', '\\');

    private static string EnsureFolder(MediaDevice mtp, string folder)
    {
        var target = StorageRoot(mtp);
        foreach (var part in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            target = target + "\\" + part;
            if (!mtp.DirectoryExists(target)) mtp.CreateDirectory(target);
        }
        return target;
    }
}
