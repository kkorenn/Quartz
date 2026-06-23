#nullable enable

using System.Text;

namespace Quartz.IO;

// Small same-directory atomic writer used by settings, profiles, and counters.
// Writing beside the destination keeps the final rename on one filesystem.
internal static class AtomicFile {
    public static void WriteAllText(string path, string contents) {
        WriteAllBytes(path, Encoding.UTF8.GetBytes(contents ?? string.Empty));
    }

    public static void WriteAllBytes(string path, byte[] contents) {
        if(string.IsNullOrEmpty(path)) {
            throw new ArgumentException("Destination path is required.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if(!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool committed = false;
        try {
            using(FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                stream.Write(contents, 0, contents.Length);
                stream.Flush(true);
            }

            if(File.Exists(fullPath)) {
                try {
                    File.Replace(tempPath, fullPath, null);
                    committed = true;
                    return;
                } catch(PlatformNotSupportedException) {
                } catch(IOException) {
                    // Some filesystems do not implement Replace. The fallback is
                    // still crash-safe for the existing file until the copy starts.
                }

                File.Copy(tempPath, fullPath, true);
                File.Delete(tempPath);
                committed = true;
                return;
            }

            File.Move(tempPath, fullPath);
            committed = true;
        } finally {
            if(!committed) {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }
}
