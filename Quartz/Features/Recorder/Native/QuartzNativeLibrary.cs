using System.Runtime.InteropServices;
using UnityEngine;

namespace Quartz.Features.Recorder.Native;

// Loads a native shared library by absolute path and resolves its exports as
// delegates. We don't rely on [DllImport("name")] because the game runs under
// MelonLoader's Mono backend, whose DllImport search path is opaque and doesn't
// include our UserData/Quartz/native folder — so name-based resolution fails. By
// dlopen-ing the explicit path and binding each symbol with dlsym we sidestep
// the search entirely and the dylib's rpath (@loader_path) pulls in its bundled
// libav* siblings.
internal static class QuartzNativeLibrary {
    // --- macOS / Linux (libdl) ---
    private const int RTLD_NOW = 0x2;
    private const int RTLD_GLOBAL = 0x100; // 0x8 on macOS, 0x100 on Linux; both set is harmless

    [DllImport("libdl", EntryPoint = "dlopen")]
    private static extern IntPtr dlopen(string path, int flag);

    [DllImport("libdl", EntryPoint = "dlsym")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    [DllImport("libdl", EntryPoint = "dlerror")]
    private static extern IntPtr dlerror();

    // --- Windows (kernel32) ---
    [DllImport("kernel32", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string path);

    [DllImport("kernel32", EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    private static bool IsWindows =>
        Application.platform is RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor;

    // The platform-specific file name of the encoder library.
    public static string LibraryFileName {
        get {
            if(IsWindows) {
                return "koren_encoder.dll";
            }
            return Application.platform is RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor
                ? "koren_encoder.dylib"
                : "koren_encoder.so";
        }
    }

    // Sub-folder of native/ that holds this platform's binaries.
    public static string PlatformDir {
        get {
            if(IsWindows) {
                return "win";
            }
            return Application.platform is RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor
                ? "osx"
                : "linux";
        }
    }

    public static IntPtr Open(string absolutePath) {
        if(IsWindows) {
            IntPtr h = LoadLibraryW(absolutePath);
            if(h == IntPtr.Zero) {
                throw new DllNotFoundException(
                    $"LoadLibrary failed for '{absolutePath}' (win32 error {Marshal.GetLastWin32Error()})");
            }
            return h;
        }

        // Clear any stale error, then report dlopen's own message on failure.
        dlerror();
        IntPtr handle = dlopen(absolutePath, RTLD_NOW | RTLD_GLOBAL);
        if(handle == IntPtr.Zero) {
            IntPtr msg = dlerror();
            string detail = msg == IntPtr.Zero ? "unknown error" : Marshal.PtrToStringAnsi(msg);
            throw new DllNotFoundException($"dlopen failed for '{absolutePath}': {detail}");
        }
        return handle;
    }

    public static T GetExport<T>(IntPtr handle, string symbol) where T : Delegate {
        IntPtr addr = IsWindows ? GetProcAddress(handle, symbol) : dlsym(handle, symbol);
        if(addr == IntPtr.Zero) {
            throw new EntryPointNotFoundException($"symbol '{symbol}' not found in native encoder");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(addr);
    }
}
