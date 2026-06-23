using Newtonsoft.Json.Linq;
using Quartz.Async;
using Quartz.Core;
using Quartz.IO.Interface;

namespace Quartz.IO;

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

    public static bool SaveAll() {
        bool success = true;
        foreach(ISettingsHandle handle in Snapshot()) {
            success &= handle.Save();
        }
        return success;
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
    private readonly object requestLock = new();

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
        CancelPendingSave();
        return SaveCore();
    }

    private bool SaveCore() {
        lock(saveLock) {
            try {
                string dir = System.IO.Path.GetDirectoryName(Path);

                if(!string.IsNullOrEmpty(dir)) {
                    Directory.CreateDirectory(dir);
                }

                string json = Data.Serialize().ToString();
                AtomicFile.WriteAllText(Path, json);

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
        CancellationTokenSource request = new();
        CancellationTokenSource previous;
        lock(requestLock) {
            previous = saveCts;
            saveCts = request;
        }
        previous?.Cancel();
        _ = SaveAfterDelay(delay, request);
    }

    private async Task SaveAfterDelay(int delay, CancellationTokenSource request) {
        CancellationToken token = request.Token;
        try {
            await Task.Delay(delay, token);

            if(token.IsCancellationRequested) {
                return;
            }

            // Data belongs to the Unity/main thread. Serialize and write there so
            // a UI edit cannot race a background enumeration of config arrays.
            MainThread.Enqueue(() => {
                bool isCurrent;
                lock(requestLock) {
                    isCurrent = ReferenceEquals(saveCts, request);
                    if(isCurrent) {
                        saveCts = null;
                    }
                }

                if(isCurrent && !token.IsCancellationRequested) {
                    SaveCore();
                }
                request.Dispose();
            });
        } catch(OperationCanceledException) {
            lock(requestLock) {
                if(ReferenceEquals(saveCts, request)) {
                    saveCts = null;
                }
            }
            request.Dispose();
        } catch(Exception e) {
            lock(requestLock) {
                if(ReferenceEquals(saveCts, request)) {
                    saveCts = null;
                }
            }
            request.Dispose();
            MainCore.Log.Err(
                $"[{nameof(SettingsFile<>)}] Failed to request save '{Path}': {e}"
            );
        }
    }

    public void CancelPendingSave() {
        CancellationTokenSource pending;
        lock(requestLock) {
            pending = saveCts;
            saveCts = null;
        }
        pending?.Cancel();
    }

    public void Dispose() {
        if(Data is IDisposable disposable) {
            disposable.Dispose();
        }
    }
}
