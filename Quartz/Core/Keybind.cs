using System.Collections.Generic;
using UnityEngine;

namespace Quartz.Core;

// The modifier + key combo for the menu toggle, plus the shared capture and
// formatting helpers. A bind is either a single key, or exactly one modifier
// plus one key — never two ordinary keys.
//
// Modifiers: Ctrl / Alt / Shift, plus Cmd for the macOS Command key. Unity
// reports Command under different keycodes depending on version (LeftMeta,
// LeftCommand, LeftWindows...), so the meta keys are resolved by name at
// runtime rather than referenced directly (a missing one would not compile).
public static class Keybind {
    public enum KeyModifier { None, Ctrl, Alt, Shift, Cmd }

    // True while a settings keybind capture is listening. UICore checks this so
    // pressing keys to rebind doesn't also fire the menu toggle.
    public static bool Capturing;

    public static bool IsMac =>
        Application.platform == RuntimePlatform.OSXPlayer
        || Application.platform == RuntimePlatform.OSXEditor;

    // The Command / Meta / Windows keys that exist in this Unity's KeyCode enum.
    private static readonly KeyCode[] CmdKeys = ResolveCmdKeys();

    private static KeyCode[] ResolveCmdKeys() {
        string[] names = {
            "LeftMeta", "RightMeta",
            "LeftCommand", "RightCommand",
            "LeftApple", "RightApple",
            "LeftWindows", "RightWindows",
        };
        List<KeyCode> keys = [];
        foreach(string name in names) {
            if(System.Enum.TryParse(name, out KeyCode kc) && !keys.Contains(kc)) {
                keys.Add(kc);
            }
        }
        return [.. keys];
    }

    private static bool AnyCmdHeld() {
        for(int i = 0; i < CmdKeys.Length; i++) {
            if(Input.GetKey(CmdKeys[i])) {
                return true;
            }
        }
        return false;
    }

    private static bool IsCmdKey(KeyCode key) {
        for(int i = 0; i < CmdKeys.Length; i++) {
            if(CmdKeys[i] == key) {
                return true;
            }
        }
        return false;
    }

    // A modifier key itself — never bound as the main key.
    public static bool IsModifier(KeyCode key) {
        switch(key) {
            case KeyCode.LeftControl or KeyCode.RightControl
                or KeyCode.LeftAlt or KeyCode.RightAlt or KeyCode.AltGr
                or KeyCode.LeftShift or KeyCode.RightShift:
                return true;
            default:
                return IsCmdKey(key);
        }
    }

    public static bool ModifierHeld(KeyModifier mod) => mod switch {
        KeyModifier.None => true,
        KeyModifier.Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
        KeyModifier.Alt => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr),
        KeyModifier.Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
        KeyModifier.Cmd => AnyCmdHeld(),
        _ => false,
    };

    // The modifier currently held (Ctrl > Cmd > Alt > Shift priority), or None.
    public static KeyModifier HeldModifier() {
        if(ModifierHeld(KeyModifier.Ctrl)) {
            return KeyModifier.Ctrl;
        }
        if(ModifierHeld(KeyModifier.Cmd)) {
            return KeyModifier.Cmd;
        }
        if(ModifierHeld(KeyModifier.Alt)) {
            return KeyModifier.Alt;
        }
        if(ModifierHeld(KeyModifier.Shift)) {
            return KeyModifier.Shift;
        }
        return KeyModifier.None;
    }

    public static string ModifierName(KeyModifier mod) => mod switch {
        KeyModifier.Ctrl => "Ctrl",
        KeyModifier.Alt => IsMac ? "Option" : "Alt",
        KeyModifier.Shift => "Shift",
        KeyModifier.Cmd => IsMac ? "Cmd" : "Win",
        _ => "",
    };

    public static string KeyName(KeyCode key) => key switch {
        KeyCode.BackQuote => "`",
        KeyCode.Return => "Enter",
        KeyCode.Escape => "Esc",
        KeyCode.Space => "Space",
        KeyCode.Alpha0 => "0",
        KeyCode.Alpha1 => "1",
        KeyCode.Alpha2 => "2",
        KeyCode.Alpha3 => "3",
        KeyCode.Alpha4 => "4",
        KeyCode.Alpha5 => "5",
        KeyCode.Alpha6 => "6",
        KeyCode.Alpha7 => "7",
        KeyCode.Alpha8 => "8",
        KeyCode.Alpha9 => "9",
        _ => key.ToString(),
    };

    // "Option + K" on macOS, "Alt + K" elsewhere; just "K" with no modifier.
    public static string Format(KeyModifier mod, KeyCode key) {
        string k = KeyName(key);
        return mod == KeyModifier.None ? k : ModifierName(mod) + " + " + k;
    }
}
