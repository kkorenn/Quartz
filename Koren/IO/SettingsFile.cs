using Newtonsoft.Json.Linq;
using Koren.Core;
using Koren.IO.Interface;

namespace Koren.IO;

// Non-generic view of a SettingsFile<T>, so the profile system can flush and
// reload every live settings file without knowing the data types.
public interface ISettingsHandle {
    string Path { get; }
    bool Load();
    void LoadOrDefaults();
    bool Save();
    void CancelPendingSave();
}

// Every SettingsFile<T> registers itself here on construction. Profiles use
// this to snapshot (SaveAll) and switch (CancelPendingSaves + reload) the
// whole set of live settings at once.
public static class SettingsRegistry {
    private static readonly List<ISettingsHandle> handles = [];
    private static readonly object sync = new();

    public static void Register(ISettingsHandle handle) {
        lock(sync) {
            // Features recreate their SettingsFile only in odd paths; keep the
            // newest instance per path so reloads hit live data.
            handles.RemoveAll(h => h.Path == handle.Path);
            handles.Add(handle);
        }
    }

    public static ISettingsHandle[] Snapshot() {
        lock(sync) {
            return [.. handles];
        }
    }

    public static void SaveAll() {
        foreach(ISettingsHandle handle in Snapshot()) {
            handle.Save();
        }
    }

    public static void CancelPendingSaves() {
        foreach(ISettingsHandle handle in Snapshot()) {
            handle.CancelPendingSave();
        }
    }

    // Re-reads every registered file from disk; files that don't exist reset
    // their data to defaults (a profile that lacks a file means "defaults").
    public static void ReloadAll() {
        foreach(ISettingsHandle handle in Snapshot()) {
            handle.CancelPendingSave();
            handle.LoadOrDefaults();
        }
    }
}

public sealed class SettingsFile<T> : ISettingsHandle where T : class, ISettingsFile, new() {
    public T Data { get; } = new();

    public readonly string Path;

    string ISettingsHandle.Path => Path;

    private readonly object saveLock = new();

    private CancellationTokenSource saveCts;

    public SettingsFile(string path) {
        Path = path;
        SettingsRegistry.Register(this);
    }

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

    // Load, but when the file is missing reset Data back to a fresh T's
    // serialized state. Used when switching profiles: a file absent from the
    // applied profile must not leak the previous profile's values.
    public void LoadOrDefaults() {
        if(Load()) {
            return;
        }

        try {
            Data.Deserialize(new T().Serialize());
        } catch(Exception e) {
            MainCore.Log.Err(
                $"[{nameof(SettingsFile<>)}] Failed to reset settings '{Path}': {e}"
            );
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

    public void CancelPendingSave() => saveCts?.Cancel();

    public void Dispose() {
        if(Data is IDisposable disposable) {
            disposable.Dispose();
        }
    }
}