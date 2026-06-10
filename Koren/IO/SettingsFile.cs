using Newtonsoft.Json.Linq;
using Koren.Core;
using Koren.IO.Interface;

namespace Koren.IO;

public sealed class SettingsFile<T>(string path) where T : class, ISettingsFile, new() {
    public T Data { get; } = new();

    public readonly string Path = path;

    private readonly object saveLock = new();

    private CancellationTokenSource saveCts;

    public bool Load() {
        try {
            if(!File.Exists(Path)) {
                return false;
            }

            string json = File.ReadAllText(Path);
            JToken token = JToken.Parse(json);
            Data.Deserialize(token);

            return true;
        } catch(Exception e) {
            MainCore.Log.Err(
                $"[{nameof(SettingsFile<>)}] Failed to load settings '{Path}': {e}"
            );

            return false;
        }
    }

    public bool Save() {
        lock(saveLock) {
            try {
                string dir = System.IO.Path.GetDirectoryName(Path);

                if(!string.IsNullOrEmpty(dir)) {
                    Directory.CreateDirectory(dir);
                }

                string json = Data.Serialize().ToString();
                File.WriteAllText(Path, json);

                return true;
            } catch(Exception e) {
                MainCore.Log.Err(
                    $"[{nameof(SettingsFile<>)}] Failed to save settings '{Path}': {e}"
                );

                return false;
            }
        }
    }

    // Debounced save: each request cancels the previous pending one and
    // schedules its own. (An earlier version reused the pending task but had
    // already cancelled its token — a second request inside the delay window
    // could silently drop the save.)
    public void RequestSave(
        int delay = 500
    ) {
        saveCts?.Cancel();

        saveCts = new CancellationTokenSource();

        CancellationToken token = saveCts.Token;

        _ = Task.Run(async () => {
            try {
                await Task.Delay(delay, token);

                if(token.IsCancellationRequested) {
                    return;
                }

                Save();
            } catch(OperationCanceledException) {
            } catch(Exception e) {
                MainCore.Log.Err(
                    $"[{nameof(SettingsFile<>)}] Failed to request save '{Path}': {e}"
                );
            }
        });
    }

    public void Dispose() {
        if(Data is IDisposable disposable) {
            disposable.Dispose();
        }
    }
}