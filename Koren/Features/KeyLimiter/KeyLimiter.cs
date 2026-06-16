using Koren.Core;
using Koren.IO;
using MonsterLove.StateMachine;
using SkyHook;
using System.Threading;
using UnityEngine;

namespace Koren.Features.KeyLimiter;

// Only counts the allowed keys as gameplay hits, ported from the original
// KorenResourcePack's KeyLimiter (itself modeled on fangshenghan's
// KeyboardChatterBlocker mod, which bundles a key limiter with the chatter
// blocker). Mouse buttons are always allowed. Enforcement only happens during
// PlayerControl, so menus/editor typing are untouched.
//
// The actual blocking lives in ChatterBlocker's patches (one shared
// CountValidKeysPressed prefix and one SkyHook prefix handle both features,
// like v1): this class owns the allowed set, the player-control check and the
// add/remove key capture mode.
public static class KeyLimiter {
    public static SettingsFile<KeyLimiterSettings> ConfMgr { get; private set; }
    public static KeyLimiterSettings Conf => ConfMgr?.Data;

    // Fired when the allowed set or capture state changes, so the Gameplay
    // page can refresh its key list live.
    public static event Action Changed;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<KeyLimiterSettings>(
            Path.Combine(MainCore.Paths.RootPath, "KeyLimiter.json")
        );
        ConfMgr.Load();
        EnsureTicker();
    }

    public static void Save() => ConfMgr?.RequestSave();

    public static bool IsEnabled() {
        EnsureConf();
        return MainCore.IsModEnabled && Conf.Enabled;
    }

    // Capture mode suspends enforcement, like v1's KeyCapture did — otherwise
    // adding a key would require pressing a currently-blocked key mid-play.
    public static bool IsActive() => IsEnabled() && !IsCapturing;

    // ===== player-control state =====
    //
    // The SkyHook callback runs off the main thread, where Unity API access
    // (and scrController) is off-limits; it reads a volatile snapshot the
    // main-thread ticker refreshes every frame, exactly like v1.

    private static int cachedPlayerControlFrame = -1;
    private static bool cachedPlayerControl;
    private static int cachedPlayerControlForHooks;

    public static bool InPlayerControl() {
        int frame = Time.frameCount;
        if(cachedPlayerControlFrame == frame) {
            return cachedPlayerControl;
        }

        cachedPlayerControlFrame = frame;
        SetCachedPlayerControl(false);
        try {
            scrController controller = scrController.instance;
            if(controller == null) {
                return false;
            }
            if(controller.paused || !controller.gameworld) {
                return false;
            }
            SetCachedPlayerControl(((StateBehaviour)controller).stateMachine.GetState() is States state
                && state == States.PlayerControl);
            return cachedPlayerControl;
        } catch {
            SetCachedPlayerControl(false);
            return false;
        }
    }

    public static bool InPlayerControlCached() => Volatile.Read(ref cachedPlayerControlForHooks) != 0;

    private static void SetCachedPlayerControl(bool value) {
        cachedPlayerControl = value;
        Volatile.Write(ref cachedPlayerControlForHooks, value ? 1 : 0);
    }

    // ===== allowed keys =====

    private static readonly HashSet<int> cachedAllowedKeys = [];
    private static int[] cachedAllowedSource;
    private static int cachedAllowedLength = -1;

    public static bool IsAllowedKey(KeyCode key) {
        int[] allowed = Conf?.AllowedKeys;
        if(allowed == null) {
            return false;
        }

        if(!ReferenceEquals(allowed, cachedAllowedSource) || allowed.Length != cachedAllowedLength) {
            cachedAllowedKeys.Clear();
            for(int i = 0; i < allowed.Length; i++) {
                cachedAllowedKeys.Add((int)NormalizeKey((KeyCode)allowed[i]));
            }

            cachedAllowedSource = allowed;
            cachedAllowedLength = allowed.Length;
        }

        return cachedAllowedKeys.Contains((int)NormalizeKey(key));
    }

    public static bool IsMouseKey(KeyCode key) => key is >= KeyCode.Mouse0 and <= KeyCode.Mouse6;

    public static bool ShouldBlockKey(KeyCode key)
        => IsActive() && InPlayerControl() && !IsMouseKey(key) && !IsAllowedKey(key);

    public static void ToggleAllowedKey(KeyCode key) {
        EnsureConf();

        key = NormalizeKey(key);
        if(key == KeyCode.None || IsMouseKey(key)) {
            return;
        }

        List<int> keys = [.. Conf.AllowedKeys];
        if(!keys.Remove((int)key)) {
            keys.Add((int)key);
        }

        Conf.AllowedKeys = [.. keys];
        Save();
        Changed?.Invoke();
    }

    // Wholesale replacement of the allowed list — used by the key viewer's
    // "sync to key limiter" option.
    public static void SetAllowedKeys(int[] keys) {
        EnsureConf();

        Conf.AllowedKeys = keys ?? [];
        Save();
        Changed?.Invoke();
    }

    // ===== key normalization (ported from v1's KeyCodeCompat) =====

    // v1 stored async keys as 0x1000 + Windows virtual-key in old configs.
    private const int LegacyAsyncKeyOffset = 0x1000;
    private const int LegacyAsyncKeyMax = LegacyAsyncKeyOffset + 0xFF;

    public static KeyCode NormalizeKey(KeyCode key) {
        key = NormalizeLegacyAsyncKey(key);
        if(key == KeyCode.AltGr) {
            return KeyCode.RightAlt;
        }
        // Unity's legacy Input reports the numpad Enter as Return and can't tell
        // them apart, while the SkyHook hook gives the distinct KeypadEnter.
        // Fold them together so an allowed-key set captured one way still matches
        // a press detected the other way (the numpad Enter was getting blocked).
        if(key == KeyCode.KeypadEnter) {
            return KeyCode.Return;
        }
        return key;
    }

    // DM Note presets store some keys as raw Windows virtual-key codes instead of
    // names — notably the Korean Hangul / Right-Alt key, which Windows reports as
    // VK 0x15 (=21). Interpret a numeric key token as a VK code first; only fall
    // back to a raw KeyCode cast when it isn't a known virtual key, so a bare "21"
    // resolves to KeyCode.RightAlt rather than the undefined (KeyCode)21 (which
    // renders as "21" and never registers under Input.GetKey).
    public static KeyCode NormalizeNumericKey(int numeric) {
        if(numeric >= 0 && numeric <= 0xFF) {
            KeyCode vk = WindowsVirtualKeyToUnityKey((ushort)numeric);
            if(vk != KeyCode.None) {
                return vk;
            }
        }
        return NormalizeKey((KeyCode)numeric);
    }

    private static KeyCode NormalizeLegacyAsyncKey(KeyCode key) {
        int raw = (int)key;
        if(raw < LegacyAsyncKeyOffset || raw > LegacyAsyncKeyMax) {
            return key;
        }

        KeyCode mapped = WindowsVirtualKeyToUnityKey((ushort)(raw - LegacyAsyncKeyOffset));
        return mapped == KeyCode.None ? key : mapped;
    }

    // ===== async (SkyHook) key mapping, ported from v1 =====

    public static bool IsMouseLabel(KeyLabel label) {
        switch(label.ToString()) {
            case "MouseLeft":
            case "MouseRight":
            case "MouseMiddle":
            case "MouseX1":
            case "MouseX2":
                return true;
            default:
                return false;
        }
    }

    public static bool ShouldBlockAsyncKeyFromHook(ushort key, KeyLabel label) {
        if(!IsActive() || !InPlayerControlCached()) {
            return false;
        }
        if(IsMouseLabel(label)) {
            return false;
        }

        KeyCode unityKey = HookKeyToPhysicalUnityKey(key, label);
        if(IsMouseKey(unityKey)) {
            return false;
        }
        if(unityKey != KeyCode.None && IsAllowedKey(unityKey)) {
            return false;
        }

        KeyCode mappedKey = AsyncKeyMapper.AsyncKeyToUnityKey(label);
        if(mappedKey == KeyCode.None && IsAllowedGenericModifierVirtualKey(key)) {
            return false;
        }

        return mappedKey == KeyCode.None || !IsAllowedKey(mappedKey);
    }

    private static bool IsAllowedGenericModifierVirtualKey(ushort key) {
        switch(key) {
            case 0x10:
                return IsAllowedKey(KeyCode.LeftShift) || IsAllowedKey(KeyCode.RightShift);
            case 0x11:
                return IsAllowedKey(KeyCode.LeftControl) || IsAllowedKey(KeyCode.RightControl);
            case 0x12:
                return IsAllowedKey(KeyCode.LeftAlt) || IsAllowedKey(KeyCode.RightAlt)
                    || IsAllowedKey(KeyCode.AltGr);
            default:
                return false;
        }
    }

    public static KeyCode HookKeyToPhysicalUnityKey(ushort key, KeyLabel label) {
        KeyCode labelKey = AsyncKeyMapper.AsyncKeyToUnityKey(label);
        if(IsNumpadOrArrowKey(labelKey)) {
            return labelKey;
        }

        if(IsWindowsRuntime()) {
            KeyCode hookKey = WindowsVirtualKeyToUnityKey(key);
            if(hookKey != KeyCode.None) {
                return hookKey;
            }
        }

        KeyCode mapped = AsyncLabelToPhysicalUnityKey(label);
        if(mapped != KeyCode.None) {
            return mapped;
        }

        return KeyCode.None;
    }

    private static bool IsNumpadOrArrowKey(KeyCode key) {
        switch(key) {
            case KeyCode.UpArrow:
            case KeyCode.DownArrow:
            case KeyCode.LeftArrow:
            case KeyCode.RightArrow:
            case KeyCode.Keypad0:
            case KeyCode.Keypad1:
            case KeyCode.Keypad2:
            case KeyCode.Keypad3:
            case KeyCode.Keypad4:
            case KeyCode.Keypad5:
            case KeyCode.Keypad6:
            case KeyCode.Keypad7:
            case KeyCode.Keypad8:
            case KeyCode.Keypad9:
            case KeyCode.KeypadPeriod:
            case KeyCode.KeypadDivide:
            case KeyCode.KeypadMultiply:
            case KeyCode.KeypadMinus:
            case KeyCode.KeypadPlus:
            case KeyCode.KeypadEnter:
                return true;
            default:
                return false;
        }
    }

    private static bool IsWindowsRuntime() {
        RuntimePlatform platform = Application.platform;
        return platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor;
    }

    private static KeyCode AsyncLabelToPhysicalUnityKey(KeyLabel label) {
        string name = label.ToString();

        if(name.Length == 1 && name[0] >= 'A' && name[0] <= 'Z') {
            return (KeyCode)((int)KeyCode.A + (name[0] - 'A'));
        }

        if(name.Length == 6 && name.StartsWith("Alpha") && name[5] >= '0' && name[5] <= '9') {
            return (KeyCode)((int)KeyCode.Alpha0 + (name[5] - '0'));
        }

        if(name.Length >= 2 && name[0] == 'F') {
            if(int.TryParse(name[1..], out int functionKey) && functionKey >= 1 && functionKey <= 15) {
                return (KeyCode)((int)KeyCode.F1 + (functionKey - 1));
            }
        }

        if(name.Length == 7 && name.StartsWith("Keypad") && name[6] >= '0' && name[6] <= '9') {
            return (KeyCode)((int)KeyCode.Keypad0 + (name[6] - '0'));
        }

        switch(name) {
            case "Escape": return KeyCode.Escape;
            case "Grave": return KeyCode.BackQuote;
            case "Minus": return KeyCode.Minus;
            case "Equal": return KeyCode.Equals;
            case "Backspace": return KeyCode.Backspace;
            case "Tab": return KeyCode.Tab;
            case "LeftBrace": return KeyCode.LeftBracket;
            case "RightBrace": return KeyCode.RightBracket;
            case "BackSlash": return KeyCode.Backslash;
            case "CapsLock": return KeyCode.CapsLock;
            case "Semicolon": return KeyCode.Semicolon;
            case "Apostrophe": return KeyCode.Quote;
            case "Enter": return KeyCode.Return;
            case "LShift": return KeyCode.LeftShift;
            case "LeftShift": return KeyCode.LeftShift;
            case "Comma": return KeyCode.Comma;
            case "Dot": return KeyCode.Period;
            case "Slash": return KeyCode.Slash;
            case "RShift": return KeyCode.RightShift;
            case "RightShift": return KeyCode.RightShift;
            case "LControl": return KeyCode.LeftControl;
            case "LCtrl": return KeyCode.LeftControl;
            case "LeftControl": return KeyCode.LeftControl;
            case "LeftCtrl": return KeyCode.LeftControl;
            case "Super": return KeyCode.LeftCommand;
            case "LWin": return KeyCode.LeftWindows;
            case "LeftWin": return KeyCode.LeftWindows;
            case "LeftWindows": return KeyCode.LeftWindows;
            case "LAlt": return KeyCode.LeftAlt;
            case "Space": return KeyCode.Space;
            case "RAlt": return KeyCode.RightAlt;
            case "AltGr": return KeyCode.RightAlt;
            case "Hangul": return KeyCode.RightAlt;
            case "RControl": return KeyCode.RightControl;
            case "RCtrl": return KeyCode.RightControl;
            case "RightControl": return KeyCode.RightControl;
            case "RightCtrl": return KeyCode.RightControl;
            case "Hanja": return KeyCode.RightControl;
            case "RWin": return KeyCode.RightWindows;
            case "RightWin": return KeyCode.RightWindows;
            case "RightWindows": return KeyCode.RightWindows;
            case "PrintScreen": return KeyCode.Print;
            case "ScrollLock": return KeyCode.ScrollLock;
            case "PauseBreak": return KeyCode.Pause;
            case "Insert": return KeyCode.Insert;
            case "Home": return KeyCode.Home;
            case "PageUp": return KeyCode.PageUp;
            case "Delete": return KeyCode.Delete;
            case "End": return KeyCode.End;
            case "PageDown": return KeyCode.PageDown;
            case "ArrowUp": return KeyCode.UpArrow;
            case "ArrowLeft": return KeyCode.LeftArrow;
            case "ArrowDown": return KeyCode.DownArrow;
            case "ArrowRight": return KeyCode.RightArrow;
            case "NumLock": return KeyCode.Numlock;
            case "KeypadSlash": return KeyCode.KeypadDivide;
            case "KeypadAsterisk": return KeyCode.KeypadMultiply;
            case "KeypadMinus": return KeyCode.KeypadMinus;
            case "KeypadDot": return KeyCode.KeypadPeriod;
            case "KeypadPlus": return KeyCode.KeypadPlus;
            case "KeypadEnter": return KeyCode.KeypadEnter;
            case "Application": return KeyCode.Menu;
            case "Apps": return KeyCode.Menu;
            case "Menu": return KeyCode.Menu;
            case "MouseLeft": return KeyCode.Mouse0;
            case "MouseRight": return KeyCode.Mouse1;
            case "MouseMiddle": return KeyCode.Mouse2;
            case "MouseX1": return KeyCode.Mouse3;
            case "MouseX2": return KeyCode.Mouse4;
        }

        return AsyncKeyMapper.AsyncKeyToUnityKey(label);
    }

    private static KeyCode WindowsVirtualKeyToUnityKey(ushort key) {
        switch(key) {
            case 0x15:
            case 0xA5:
                return KeyCode.RightAlt;
            case 0x19:
            case 0xA3:
                return KeyCode.RightControl;
            case 0x5D: return KeyCode.Menu;
            case 0x08: return KeyCode.Backspace;
            case 0x09: return KeyCode.Tab;
            case 0x0D: return KeyCode.Return;
            case 0x10:
            case 0xA0:
                return KeyCode.LeftShift;
            case 0x11:
            case 0xA2:
                return KeyCode.LeftControl;
            case 0x12:
            case 0xA4:
                return KeyCode.LeftAlt;
            case 0x13: return KeyCode.Pause;
            case 0x14: return KeyCode.CapsLock;
            case 0x1B: return KeyCode.Escape;
            case 0x20: return KeyCode.Space;
            case 0x21: return KeyCode.PageUp;
            case 0x22: return KeyCode.PageDown;
            case 0x23: return KeyCode.End;
            case 0x24: return KeyCode.Home;
            case 0x25: return KeyCode.LeftArrow;
            case 0x26: return KeyCode.UpArrow;
            case 0x27: return KeyCode.RightArrow;
            case 0x28: return KeyCode.DownArrow;
            case 0x2C: return KeyCode.Print;
            case 0x2D: return KeyCode.Insert;
            case 0x2E: return KeyCode.Delete;
            case 0x5B: return KeyCode.LeftWindows;
            case 0x5C: return KeyCode.RightWindows;
            case 0x6A: return KeyCode.KeypadMultiply;
            case 0x6B: return KeyCode.KeypadPlus;
            case 0x6D: return KeyCode.KeypadMinus;
            case 0x6E: return KeyCode.KeypadPeriod;
            case 0x6F: return KeyCode.KeypadDivide;
            case 0x90: return KeyCode.Numlock;
            case 0x91: return KeyCode.ScrollLock;
            case 0xA1: return KeyCode.RightShift;
            case 0xBA: return KeyCode.Semicolon;
            case 0xBB: return KeyCode.Equals;
            case 0xBC: return KeyCode.Comma;
            case 0xBD: return KeyCode.Minus;
            case 0xBE: return KeyCode.Period;
            case 0xBF: return KeyCode.Slash;
            case 0xC0: return KeyCode.BackQuote;
            case 0xDB: return KeyCode.LeftBracket;
            case 0xDC: return KeyCode.Backslash;
            case 0xDD: return KeyCode.RightBracket;
            case 0xDE: return KeyCode.Quote;
        }

        if(key >= 0x30 && key <= 0x39) {
            return (KeyCode)((int)KeyCode.Alpha0 + (key - 0x30));
        }
        if(key >= 0x41 && key <= 0x5A) {
            return (KeyCode)((int)KeyCode.A + (key - 0x41));
        }
        if(key >= 0x60 && key <= 0x69) {
            return (KeyCode)((int)KeyCode.Keypad0 + (key - 0x60));
        }
        if(key >= 0x70 && key <= 0x7E) {
            return (KeyCode)((int)KeyCode.F1 + (key - 0x70));
        }

        return KeyCode.None;
    }

    // ===== capture mode =====
    //
    // Single-key capture like v1's KrpPages: the button arms it, the next
    // key pressed is reported and capture ends. Escape (or clicking the
    // button again) cancels.

    public static bool IsCapturing { get; private set; }

    private static Action<KeyCode> captureOnKey;
    private static Action captureOnEnded;

    public static void StartCapture(Action<KeyCode> onKey, Action onEnded) {
        CancelCapture();

        IsCapturing = true;
        captureOnKey = onKey;
        captureOnEnded = onEnded;
        // Suppresses the menu-toggle keybind while listening, same flag the
        // Settings rebind widget uses.
        Keybind.Capturing = true;
        Changed?.Invoke();
    }

    public static void CancelCapture() => EndCapture(KeyCode.None);

    private static void EndCapture(KeyCode key) {
        if(!IsCapturing) {
            return;
        }

        IsCapturing = false;
        Keybind.Capturing = false;

        Action<KeyCode> onKey = captureOnKey;
        Action onEnded = captureOnEnded;
        captureOnKey = null;
        captureOnEnded = null;

        if(key != KeyCode.None && key != KeyCode.Escape) {
            onKey?.Invoke(key);
        }
        onEnded?.Invoke();
        Changed?.Invoke();
    }

    public static void ClearAllowedKeys() {
        EnsureConf();
        Conf.AllowedKeys = [];
        Save();
        Changed?.Invoke();
    }

    // Rebinds one allowed-list entry in place (keeps its position). If the
    // new key is already allowed, the old entry is just removed instead of
    // duplicating.
    public static void ReplaceAllowedKey(KeyCode oldKey, KeyCode newKey) {
        EnsureConf();

        oldKey = NormalizeKey(oldKey);
        newKey = NormalizeKey(newKey);
        if(newKey == KeyCode.None || IsMouseKey(newKey)) {
            return;
        }

        List<int> keys = [.. Conf.AllowedKeys];
        int index = keys.IndexOf((int)oldKey);
        if(index < 0) {
            ToggleAllowedKey(newKey);
            return;
        }

        if(keys.Contains((int)newKey)) {
            keys.RemoveAt(index);
        } else {
            keys[index] = (int)newKey;
        }

        Conf.AllowedKeys = [.. keys];
        Save();
        Changed?.Invoke();
    }

    // ===== per-frame ticker =====

    private static Ticker ticker;

    private static void EnsureTicker() {
        if(ticker != null || MainCore.Root == null) {
            return;
        }
        ticker = MainCore.Root.AddComponent<Ticker>();
    }

    // Candidate keys for capture: every keyboard KeyCode (mouse and joystick
    // ranges skipped — mouse is always allowed, joysticks aren't keys).
    private static KeyCode[] captureCandidates;

    private static KeyCode[] CaptureCandidates {
        get {
            if(captureCandidates != null) {
                return captureCandidates;
            }

            List<KeyCode> list = [];
            foreach(KeyCode key in Enum.GetValues(typeof(KeyCode))) {
                if(key == KeyCode.None || IsMouseKey(key)) {
                    continue;
                }
                if(key >= KeyCode.JoystickButton0) {
                    continue;
                }
                list.Add(key);
            }
            captureCandidates = [.. list];
            return captureCandidates;
        }
    }

    // Refreshes the off-thread player-control snapshot every frame (v1 did
    // this from Main.Update) and runs capture-mode key polling. Held-state
    // edge detection instead of GetKeyDown: macOS doesn't deliver down-edges
    // for modifier keys, but held state reads fine.
    private sealed class Ticker : MonoBehaviour {
        private readonly HashSet<KeyCode> prevHeld = [];
        private bool wasCapturing;

        private void Update() {
            InPlayerControl();

            if(!IsCapturing) {
                wasCapturing = false;
                if(prevHeld.Count > 0) {
                    prevHeld.Clear();
                }
                return;
            }

            // First frame of a capture: remember what's already held so a
            // key the user hadn't released yet isn't captured instantly.
            bool priming = !wasCapturing;
            wasCapturing = true;

            KeyCode[] candidates = CaptureCandidates;
            for(int i = 0; i < candidates.Length; i++) {
                KeyCode key = candidates[i];
                bool held;
                try { held = UnityEngine.Input.GetKey(key); }
                catch { continue; }

                if(held && !priming && !prevHeld.Contains(key)) {
                    prevHeld.Add(key);
                    EndCapture(key);
                    return;
                }

                if(held) {
                    prevHeld.Add(key);
                } else {
                    prevHeld.Remove(key);
                }
            }
        }
    }
}
