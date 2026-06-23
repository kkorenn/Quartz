using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Quartz.Core;
using Quartz.Features.ChatterBlocker;
using Quartz.Features.Combo;
using Quartz.Features.EffectRemover;
using Quartz.Features.Judgement;
using Quartz.Features.KeyLimiter;
using Quartz.Features.KeyViewer;
using Quartz.Features.OttoIcon;
using Quartz.Features.PlanetColors;
using Quartz.Features.ProgressBar;
using Quartz.Features.Restriction;
using Quartz.Features.Tweaks;
using Quartz.Features.UiHider;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Quartz.Features.Interop;

public enum SettingsImportSource {
    KeyboardChatterBlocker,
    JipperKeyViewer,
    JipperResourcePack,
    AdofaiTweaks,
    EnhancedEffectRemover,
    KorenResourcePackV1,
}

public enum SettingsImportReplaceMode {
    ReplaceAll,
    ReplaceCertain,
    KeepOld,
}

[Flags]
public enum SettingsImportKeyViewerPart {
    None = 0,
    KeysLayout = 1 << 0,
    Labels = 1 << 1,
    Colors = 1 << 2,
    Rain = 1 << 3,
    PositionSize = 1 << 4,
    // v2 KeyViewer has no foot/ghost/per-key/streamer/font data, and its press
    // counts are stored by KeyCode name (not the source mods' indexed arrays),
    // so Counts/Display/Font from v1's importer have no v2 target and are
    // deliberately not offered here.
    All = KeysLayout | Labels | Colors | Rain | PositionSize,
}

public sealed class SettingsImportOption {
    public SettingsImportSource Source;
    public string Id;
    public string Label;
    public Assembly Assembly;
    public string Directory;
    public string OptionId;
}

public sealed class SettingsImportResult {
    public bool Success;
    public int ImportedCount;
    public string Message;
}

// Migrates settings INTO Quartz from other ADOFAI mods, reading
// their state purely through reflection + their on-disk config (Quartz never
// hard-links another mod — see UmmInterop). Ported from v1's SettingsImporter,
// with the apply layer rewritten onto v2's per-feature SettingsFile<T> model.
//
// Mapping coverage is bounded by what v2 actually has: feature on/off, key
// limiter keys, the KeyViewer (simple mode), Combo, Judgement, ProgressBar,
// Otto/planet colors, UI hiding, judgement restriction, Tweaks, and the Effect
// Remover all map across. Source data v2 has no home for (text-stat color
// ranges, BPM color, foot/ghost keys, per-key counts, attempt/play history) is
// skipped rather than guessed at.
public static class SettingsImporter {
    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    // v1 stored async keys as 0x1000 + Windows virtual-key; feeding KeyLimiter
    // a value in that range routes it through the VK→Unity translation.
    private const int LegacyAsyncKeyOffset = 0x1000;

    private sealed class ImportSpec {
        public readonly SettingsImportSource Source;
        public readonly string DisplayName;
        public readonly string[] Aliases;

        public ImportSpec(SettingsImportSource source, string displayName, params string[] aliases) {
            Source = source;
            DisplayName = displayName;
            Aliases = aliases;
        }
    }

    private static readonly ImportSpec[] Specs = [
        new(SettingsImportSource.KeyboardChatterBlocker, "KeyboardChatterBlocker",
            "KeyboardChatterBlocker", "Keyboard Chatter Blocker"),
        new(SettingsImportSource.JipperKeyViewer, "JipperKeyViewer",
            "JipperKeyViewer", "JipperKeyViewer-FileBased", "Jipper Key Viewer", "Jipper Key Viewer File Based"),
        new(SettingsImportSource.JipperResourcePack, "JipperResourcePack",
            "JipperResourcePack", "Jipper Resource Pack"),
        new(SettingsImportSource.AdofaiTweaks, "ADOFAI Tweaks",
            "AdofaiTweaks", "ADOFAI Tweaks"),
        new(SettingsImportSource.EnhancedEffectRemover, "EnhancedEffectRemover",
            "EnhancedEffectRemover", "Enhanced Effect Remover"),
        // v1 KorenResourcePack — this mod's own predecessor. It's a UMM mod
        // (Id "KorenResourcePack", DisplayName "koren resource pack"), so it's
        // picked up by the same UMM discovery as the others. Because v2 is a
        // direct rewrite of v1, almost every v1 setting has a v2 home.
        new(SettingsImportSource.KorenResourcePackV1, "KorenResourcePack (v1)",
            "KorenResourcePack", "koren resource pack"),
    ];

    // Every supported source mod UMM knows about, ready to import from. The other
    // sources are read through their LOADED assembly (reflection on live types),
    // so they must be active. v1 is fully importable from its on-disk Settings.xml
    // alone, so it's matched against INSTALLED mods too — an upgrader has usually
    // toggled v1 off in UMM, and it should still show up to import from.
    public static List<SettingsImportOption> GetAvailableOptions() {
        List<SettingsImportOption> options = [];
        if(!UmmInterop.IsPresent) {
            return options;
        }

        List<string> activeIds = UmmInterop.ActiveModIds();
        List<string> installedIds = UmmInterop.InstalledModIds();
        foreach(ImportSpec spec in Specs) {
            List<string> ids = spec.Source == SettingsImportSource.KorenResourcePackV1 ? installedIds : activeIds;
            foreach(string id in ids) {
                object entry = UmmInterop.FindMod(id);
                if(entry == null || !EntryMatches(entry, id, spec)) {
                    continue;
                }

                string display = StripRichText(ReadNested(entry, "Info", "DisplayName") as string);
                // v1's own DisplayName is the lowercase "koren resource pack",
                // which reads as ambiguous sitting inside v2 — force the spec's
                // explicit "(v1)" label so the source is unmistakable.
                if(string.IsNullOrEmpty(display) || spec.Source == SettingsImportSource.KorenResourcePackV1) {
                    display = spec.DisplayName;
                }

                options.Add(new SettingsImportOption {
                    Source = spec.Source,
                    Id = id,
                    Label = display,
                    Assembly = UmmInterop.GetModAssembly(id) ?? FindAssemblyByName(id, spec),
                    Directory = ResolveDirectory(ReadMember(entry, "Path") as string),
                    OptionId = spec.Source + ":" + id,
                });
                break;
            }
        }

        // Safety net for v1: if it didn't surface through modEntries (e.g. UMM
        // didn't parse it, or it was dropped in after UMM started), scan the mods
        // folder on disk for its Info.json + Settings.xml directly.
        if(!options.Any(o => o.Source == SettingsImportSource.KorenResourcePackV1)) {
            SettingsImportOption diskV1 = ScanDiskForKorenV1();
            if(diskV1 != null) {
                options.Add(diskV1);
            }
        }

        return options;
    }

    // Walk the UMM mods folder ("UMMMods") looking for an installed v1 — matched
    // by its Info.json Id (or folder name) and only offered if it has a
    // Settings.xml to read. Independent of whether v1 is enabled/loaded in UMM.
    private static SettingsImportOption ScanDiskForKorenV1() {
        ImportSpec spec = Array.Find(Specs, s => s.Source == SettingsImportSource.KorenResourcePackV1);
        if(spec == null) {
            return null;
        }

        foreach(string root in UmmModsRoots()) {
            string[] dirs;
            try {
                if(string.IsNullOrEmpty(root) || !Directory.Exists(root)) {
                    continue;
                }
                dirs = Directory.GetDirectories(root);
            } catch {
                continue;
            }

            foreach(string dir in dirs) {
                string id = ReadInfoJsonId(dir);
                string folder = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if(!V1FolderMatches(spec, id, folder)) {
                    continue;
                }
                if(!File.Exists(Path.Combine(dir, "Settings.xml"))) {
                    continue; // nothing to import from
                }

                string resolvedId = string.IsNullOrEmpty(id) ? "KorenResourcePack" : id;
                return new SettingsImportOption {
                    Source = SettingsImportSource.KorenResourcePackV1,
                    Id = resolvedId,
                    Label = spec.DisplayName,
                    Assembly = UmmInterop.GetModAssembly(resolvedId),
                    Directory = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    OptionId = SettingsImportSource.KorenResourcePackV1 + ":" + resolvedId,
                };
            }
        }
        return null;
    }

    // Candidate mods-folder roots to scan, most authoritative first: UMM's own
    // resolved modsPath, then the parent of any already-known mod's folder (which
    // is that same UMMMods dir) as a backstop.
    private static IEnumerable<string> UmmModsRoots() {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string modsPath = UmmInterop.ModsPath();
        if(!string.IsNullOrEmpty(modsPath) && seen.Add(modsPath)) {
            yield return modsPath;
        }
        foreach(string id in UmmInterop.InstalledModIds()) {
            object entry = UmmInterop.FindMod(id);
            string dir = ResolveDirectory(ReadMember(entry, "Path") as string);
            if(string.IsNullOrEmpty(dir)) {
                continue;
            }
            string parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if(!string.IsNullOrEmpty(parent) && seen.Add(parent)) {
                yield return parent;
            }
        }
    }

    private static bool V1FolderMatches(ImportSpec spec, string id, string folder) {
        string nId = NormalizeModToken(id);
        string nFolder = NormalizeModToken(folder);
        foreach(string alias in spec.Aliases) {
            string a = NormalizeModToken(alias);
            if((nId.Length > 0 && nId == a) || (nFolder.Length > 0 && nFolder == a)) {
                return true;
            }
        }
        return false;
    }

    private static string ReadInfoJsonId(string dir) {
        foreach(string name in new[] { "Info.json", "info.json" }) {
            string path = Path.Combine(dir, name);
            try {
                if(File.Exists(path)
                    && JObject.Parse(File.ReadAllText(path)) is { } obj
                    && obj.TryGetValue("Id", StringComparison.OrdinalIgnoreCase, out JToken t)) {
                    return t?.ToString();
                }
            } catch {
            }
        }
        return null;
    }

    public static SettingsImportOption FindOption(List<SettingsImportOption> options, string optionId) {
        if(options == null || string.IsNullOrEmpty(optionId)) {
            return null;
        }
        foreach(SettingsImportOption option in options) {
            if(string.Equals(option.OptionId, optionId, StringComparison.Ordinal)) {
                return option;
            }
        }
        return null;
    }

    public static bool HasKeyViewerPayload(SettingsImportSource source) =>
        source is SettingsImportSource.JipperKeyViewer
            or SettingsImportSource.JipperResourcePack
            or SettingsImportSource.KorenResourcePackV1;

    public static SettingsImportKeyViewerPart GetSupportedKeyViewerParts(SettingsImportSource source) =>
        HasKeyViewerPayload(source) ? SettingsImportKeyViewerPart.All : SettingsImportKeyViewerPart.None;

    // NOTE: this file lives under Quartz.Features.Interop, so sibling namespaces
    // (Quartz.Features.Tweaks, .ChatterBlocker, ...) shadow the facade classes
    // whose name matches their namespace leaf. Those are referenced through the
    // fully-qualified Features.X.X form (matching KeyViewerOverlay's convention)
    // rather than a bare name.
    public static SettingsImportResult Import(SettingsImportOption option) =>
        Import(option, SettingsImportReplaceMode.ReplaceAll, SettingsImportKeyViewerPart.All);

    public static SettingsImportResult Import(
        SettingsImportOption option,
        SettingsImportReplaceMode keyViewerMode,
        SettingsImportKeyViewerPart keyViewerParts
    ) {
        SettingsImportResult result = new();
        if(option == null) {
            result.Message = "No import target selected.";
            return result;
        }

        if(HasKeyViewerPayload(option.Source)) {
            keyViewerParts &= GetSupportedKeyViewerParts(option.Source);
            // The "pick at least one group" guard only makes sense when the
            // KeyViewer is the source's ONLY payload (the Jipper mods). v1 also
            // brings combo/judgement/restriction/tweaks/etc., so an empty
            // KeyViewer selection there just means "skip the KeyViewer", not
            // "nothing to do".
            if(option.Source != SettingsImportSource.KorenResourcePackV1
                && keyViewerMode == SettingsImportReplaceMode.ReplaceCertain
                && keyViewerParts == SettingsImportKeyViewerPart.None) {
                result.Message = "Select at least one KeyViewer setting group.";
                return result;
            }
        }

        try {
            int count = option.Source switch {
                SettingsImportSource.KeyboardChatterBlocker => ImportKeyboardChatterBlocker(option),
                SettingsImportSource.JipperKeyViewer => ImportJipperKeyViewer(option, keyViewerMode, keyViewerParts),
                SettingsImportSource.JipperResourcePack => ImportJipperResourcePack(option, keyViewerMode, keyViewerParts),
                SettingsImportSource.AdofaiTweaks => ImportAdofaiTweaks(option),
                SettingsImportSource.EnhancedEffectRemover => ImportEnhancedEffectRemover(option),
                SettingsImportSource.KorenResourcePackV1 => ImportKorenResourcePackV1(option, keyViewerMode, keyViewerParts),
                _ => 0,
            };

            if(count <= 0) {
                if(HasKeyViewerPayload(option.Source) && keyViewerMode == SettingsImportReplaceMode.KeepOld) {
                    result.Success = true;
                    return result;
                }
                result.Message = "No readable settings found.";
                return result;
            }

            PostImportRefresh();
            result.Success = true;
            result.ImportedCount = count;
            return result;
        } catch(Exception ex) {
            MainCore.Log.Err($"[SettingsImporter] {ex}");
            result.Message = ex.Message;
            return result;
        }
    }

    // ===== KeyboardChatterBlocker =====

    private static int ImportKeyboardChatterBlocker(SettingsImportOption option) {
        int count = 0;
        bool importedKeys = false;

        Type mainType = FindType(option, "KeyboardChatterBlocker.Main");
        object setting = GetStaticMember(mainType, "setting");
        object profile = GetStaticMember(mainType, "selectedKeyLimiterProfile");

        if(setting != null) {
            if(TryGetInt(setting, "inputInterval", out int interval)) {
                Features.ChatterBlocker.ChatterBlocker.EnsureConf();
                Features.ChatterBlocker.ChatterBlocker.Conf.ThresholdMs = Mathf.Max(0, interval);
                count++;
            }
            if(TryGetBool(setting, "enableKeyLimiter", out bool limiterOn)) {
                Features.KeyLimiter.KeyLimiter.EnsureConf();
                Features.KeyLimiter.KeyLimiter.Conf.Enabled = limiterOn;
                count++;
            }
            profile ??= FindSelectedProfile(GetMemberValue(setting, "keyLimiterProfiles"));
            int[] keys = ReadChatterBlockerProfileKeys(profile);
            if(keys.Length > 0) {
                Features.KeyLimiter.KeyLimiter.SetAllowedKeys(keys);
                count++;
                importedKeys = true;
            }
        }

        if(count == 0 || !importedKeys) {
            count += ImportChatterBlockerXml(option, count == 0, !importedKeys);
        }

        if(count > 0) {
            Features.ChatterBlocker.ChatterBlocker.EnsureConf();
            Features.ChatterBlocker.ChatterBlocker.Conf.Enabled = true;
            count++;
        }

        return count;
    }

    private static int ImportChatterBlockerXml(SettingsImportOption option, bool importBasics, bool importKeys) {
        XDocument doc = LoadXml(option, "Setting.xml");
        if(doc == null) {
            return 0;
        }

        int count = 0;
        if(importBasics) {
            if(TryReadXmlInt(doc, "inputInterval", out int interval)) {
                Features.ChatterBlocker.ChatterBlocker.EnsureConf();
                Features.ChatterBlocker.ChatterBlocker.Conf.ThresholdMs = Mathf.Max(0, interval);
                count++;
            }
            if(TryReadXmlBool(doc, "enableKeyLimiter", out bool limiterOn)) {
                Features.KeyLimiter.KeyLimiter.EnsureConf();
                Features.KeyLimiter.KeyLimiter.Conf.Enabled = limiterOn;
                count++;
            }
        }

        if(importKeys) {
            XElement profile = FindSelectedProfileElement(doc, "KeyLimiterProfile");
            int[] keys = ReadChatterBlockerProfileKeys(profile);
            if(keys.Length > 0) {
                Features.KeyLimiter.KeyLimiter.SetAllowedKeys(keys);
                count++;
            }
        }
        return count;
    }

    // ===== JipperKeyViewer =====

    private static int ImportJipperKeyViewer(
        SettingsImportOption option,
        SettingsImportReplaceMode mode,
        SettingsImportKeyViewerPart parts
    ) {
        if(mode == SettingsImportReplaceMode.KeepOld) {
            return 0;
        }

        ImportedKeyViewer imported = null;
        object runtime = GetStaticMember(FindType(option, "JipperKeyViewer.KeyViewer.KeyViewer"), "Settings");
        if(runtime != null) {
            imported = ReadKeyViewerFromObject(runtime);
        }
        if(imported == null || imported.Available == SettingsImportKeyViewerPart.None) {
            string json = ReadFirstText(JkvConfigPaths(option));
            if(!string.IsNullOrEmpty(json)) {
                try { imported = ReadKeyViewerFromJson(JObject.Parse(json)); } catch { }
            }
        }

        if(imported == null || imported.Available == SettingsImportKeyViewerPart.None) {
            return 0;
        }

        return ApplyKeyViewerImport(imported, mode, parts);
    }

    // ===== JipperResourcePack =====

    private static int ImportJipperResourcePack(
        SettingsImportOption option,
        SettingsImportReplaceMode mode,
        SettingsImportKeyViewerPart parts
    ) {
        int count = 0;
        count += ImportJrpProgressBar(option);
        count += ImportJrpCombo(option);
        count += ImportJrpJudgement(option);
        count += ImportJrpResourceChanger(option);
        count += ImportJrpKeyViewer(option, mode, parts);
        return count;
    }

    private static int ImportJrpProgressBar(SettingsImportOption option) {
        object settings = GetStaticMember(FindType(option, "JipperResourcePack.OverlayContents.Status"), "Settings")
            ?? GetStaticMember(FindType(option, "JipperResourcePack.Jongyeol.JStatus"), "Settings");
        if(settings == null) {
            return 0;
        }

        // Only the progress-BAR data maps; v2 has no flat text-stat toggles.
        if(!TryGetBool(settings, "ShowProgressBar", out bool barOn)) {
            return 0;
        }

        int count = 0;
        ProgressBarOverlay.EnsureConf();
        ProgressBarOverlay.Conf.Enabled = barOn;
        count++;

        if(TryGetColorRangeEndpoints(GetMemberValue(settings, "ProgressBarColor"), out Color fill, out _)) {
            ProgressBarOverlay.Conf.SetFillColor(fill);
            count++;
        }
        if(TryGetColorRangeEndpoints(GetMemberValue(settings, "ProgressBarBackgroundColor"), out Color back, out _)) {
            ProgressBarOverlay.Conf.SetBackColor(back);
            count++;
        }
        if(TryGetColorRangeEndpoints(GetMemberValue(settings, "ProgressBarBorderColor"), out Color border, out _)) {
            ProgressBarOverlay.Conf.SetOutlineColor(border);
            count++;
        }
        return count;
    }

    private static int ImportJrpCombo(SettingsImportOption option) {
        object settings = GetStaticMember(FindType(option, "JipperResourcePack.OverlayContents.Combo"), "Settings")
            ?? GetStaticMember(FindType(option, "JipperResourcePack.Jongyeol.JCombo"), "Settings");
        if(settings == null) {
            return 0;
        }

        int count = 0;
        ComboOverlay.EnsureConf();
        ComboOverlay.Conf.Enabled = true;
        count++;
        if(TryGetBool(settings, "EnableAutoCombo", out bool auto)) {
            ComboOverlay.Conf.CountAuto = auto;
            count++;
        }
        if(TryGetInt(settings, "ComboColorMax", out int colorMax)) {
            ComboOverlay.Conf.ColorMax = colorMax;
            count++;
        }
        if(TryGetColorRangeEndpoints(GetMemberValue(settings, "ComboColor"), out Color low, out Color high)) {
            ComboOverlay.Conf.SetColorLow(low);
            ComboOverlay.Conf.SetColorHigh(high);
            count++;
        }
        return count;
    }

    private static int ImportJrpJudgement(SettingsImportOption option) {
        object settings = GetStaticMember(FindType(option, "JipperResourcePack.OverlayContents.Judgement"), "Settings");
        if(settings == null) {
            return 0;
        }

        int count = 0;
        JudgementOverlay.EnsureConf();
        JudgementOverlay.Conf.Enabled = true;
        count++;
        if(TryGetBool(settings, "LocationUp", out bool up)) {
            JudgementOverlay.Conf.OffsetY = up ? 90f : 0f;
            count++;
        }
        return count;
    }

    private static int ImportJrpResourceChanger(SettingsImportOption option) {
        object settings = GetStaticMember(FindType(option, "JipperResourcePack.ResourceChanger"), "_settings");
        if(settings == null) {
            return 0;
        }

        int count = 0;
        if(TryGetBool(settings, "ChangeRabbit", out bool otto)) {
            Features.OttoIcon.OttoIcon.EnsureConf();
            Features.OttoIcon.OttoIcon.Conf.Enabled = otto;
            count++;
        }
        if(TryGetBool(settings, "ChangeBallColor", out bool ball)) {
            Features.PlanetColors.PlanetColors.EnsureConf();
            Features.PlanetColors.PlanetColors.Conf.Enabled = ball;
            count++;
        }
        return count;
    }

    private static int ImportJrpKeyViewer(
        SettingsImportOption option,
        SettingsImportReplaceMode mode,
        SettingsImportKeyViewerPart parts
    ) {
        if(mode == SettingsImportReplaceMode.KeepOld) {
            return 0;
        }

        object settings = GetStaticMember(FindType(option, "JipperResourcePack.KeyViewerContents.KeyViewer"), "Settings");
        if(settings == null) {
            return 0;
        }

        ImportedKeyViewer imported = ReadKeyViewerFromObject(settings);
        if(imported == null || imported.Available == SettingsImportKeyViewerPart.None) {
            return 0;
        }
        return ApplyKeyViewerImport(imported, mode, parts);
    }

    // ===== AdofaiTweaks =====

    private static int ImportAdofaiTweaks(SettingsImportOption option) {
        int count = 0;
        foreach(object settings in GetAdofaiTweaksRuntimeSettings(option)) {
            count += ImportAdofaiTweaksSettingsObject(settings);
        }
        count += ImportAdofaiTweaksXml(option);
        return count;
    }

    private static int ImportAdofaiTweaksSettingsObject(object settings) {
        if(settings == null) {
            return 0;
        }
        return settings.GetType().Name switch {
            "KeyLimiterSettings" => ImportAdofaiKeyLimiterObject(settings),
            "KeyViewerSettings" => ImportAdofaiKeyViewerObject(settings),
            "MiscellaneousSettings" => ImportAdofaiMiscObject(settings),
            "HideUiElementsSettings" => ImportAdofaiHideUiObject(settings),
            "RestrictGameplaySettings" => ImportAdofaiRestrictObject(settings),
            _ => 0,
        };
    }

    private static int ImportAdofaiKeyLimiterObject(object settings) {
        int count = 0;
        if(TryGetBool(settings, "IsEnabled", out bool enabled)) {
            Features.KeyLimiter.KeyLimiter.EnsureConf();
            Features.KeyLimiter.KeyLimiter.Conf.Enabled = enabled;
            count++;
        }
        int[] keys = ReadKeyCodesFromMember(settings, "ActiveKeys");
        if(keys.Length > 0) {
            Features.KeyLimiter.KeyLimiter.SetAllowedKeys(keys);
            count++;
        }
        return count;
    }

    private static int ImportAdofaiKeyViewerObject(object settings) {
        object profile = GetActiveIndexedProfile(settings, "Profiles", "ProfileIndex");
        int[] keys = ReadKeyCodesFromMember(profile, "ActiveKeys");
        if(keys.Length == 0) {
            return 0;
        }
        Features.KeyLimiter.KeyLimiter.SetAllowedKeys(keys);
        return 1;
    }

    private static int ImportAdofaiMiscObject(object settings) {
        if(TryGetBool(settings, "IsEnabled", out bool enabled) && enabled
            && TryGetBool(settings, "DisableEditorZoom", out bool noZoom) && noZoom) {
            Features.Tweaks.Tweaks.EnsureConf();
            Features.Tweaks.Tweaks.Conf.BlockMouseWheelScrollWhilePlaying = true;
            return 1;
        }
        return 0;
    }

    private static int ImportAdofaiHideUiObject(object settings) {
        if(!TryGetBool(settings, "IsEnabled", out bool enabled) || !enabled) {
            return 0;
        }

        Features.UiHider.UiHider.EnsureConf();
        int count = 0;
        count += ApplyAdofaiHideUiProfile(GetMemberValue(settings, "PlayingProfile"), Features.UiHider.UiHider.Conf.Playing);
        count += ApplyAdofaiHideUiProfile(GetMemberValue(settings, "RecordingProfile"), Features.UiHider.UiHider.Conf.Recording);

        if(TryGetBool(settings, "RecordingMode", out bool rec)) {
            Features.UiHider.UiHider.Conf.RecordingMode = rec;
            count++;
        }
        if(TryGetBool(settings, "UseRecordingModeShortcut", out bool useShortcut)) {
            Features.UiHider.UiHider.Conf.UseShortcut = useShortcut;
            count++;
        }

        object shortcut = GetMemberValue(settings, "RecordingModeShortcut");
        if(shortcut != null) {
            ApplyShortcutModifier(shortcut);
            if(TryGetInt(shortcut, "PressKey", out int key)) {
                Features.UiHider.UiHider.Conf.ShortcutKey = NormalizeKeyInt(key);
                count++;
            }
        }

        if(count > 0) {
            Features.UiHider.UiHider.Conf.Enabled = true;
            count++;
        }
        return count;
    }

    private static int ImportAdofaiRestrictObject(object settings) {
        if(!TryGetBool(settings, "IsEnabled", out bool enabled) || !enabled) {
            return 0;
        }
        if(!TryGetBool(settings, "RestrictJudgment", out bool restrict) || !restrict) {
            return 0;
        }

        bool[] restricted = ReadBoolArray(GetMemberValue(settings, "RestrictedJudgments"));
        if(restricted.Length == 0) {
            return 0;
        }

        return ApplyRestrictMask(restricted);
    }

    private static int ImportAdofaiTweaksXml(SettingsImportOption option) {
        int count = 0;

        XDocument keyLimiter = LoadXml(option, "KeyLimiterSettings.xml");
        if(keyLimiter != null) {
            if(TryReadXmlBool(keyLimiter, "IsEnabled", out bool enabled)) {
                Features.KeyLimiter.KeyLimiter.EnsureConf();
                Features.KeyLimiter.KeyLimiter.Conf.Enabled = enabled;
                count++;
            }
            int[] keys = ReadKeyCodesFromXml(keyLimiter.Root, "ActiveKeys");
            if(keys.Length > 0) {
                Features.KeyLimiter.KeyLimiter.SetAllowedKeys(keys);
                count++;
            }
        }

        XDocument keyViewer = LoadXml(option, "KeyViewerSettings.xml");
        if(keyViewer != null) {
            int[] keys = ReadAdofaiKeyViewerXmlKeys(keyViewer);
            if(keys.Length > 0) {
                Features.KeyLimiter.KeyLimiter.SetAllowedKeys(keys);
                count++;
            }
        }

        XDocument misc = LoadXml(option, "MiscellaneousSettings.xml");
        if(misc != null
            && TryReadXmlBool(misc, "IsEnabled", out bool miscOn) && miscOn
            && TryReadXmlBool(misc, "DisableEditorZoom", out bool noZoom) && noZoom) {
            Features.Tweaks.Tweaks.EnsureConf();
            Features.Tweaks.Tweaks.Conf.BlockMouseWheelScrollWhilePlaying = true;
            count++;
        }

        XDocument hideUi = LoadXml(option, "HideUiElementsSettings.xml");
        if(hideUi != null && TryReadXmlBool(hideUi, "IsEnabled", out bool hideOn) && hideOn) {
            Features.UiHider.UiHider.EnsureConf();
            int profileCount = 0;
            profileCount += ApplyAdofaiHideUiProfileXml(FindFirstDescendant(hideUi, "PlayingProfile"), Features.UiHider.UiHider.Conf.Playing);
            profileCount += ApplyAdofaiHideUiProfileXml(FindFirstDescendant(hideUi, "RecordingProfile"), Features.UiHider.UiHider.Conf.Recording);
            if(TryReadXmlBool(hideUi, "RecordingMode", out bool rec)) { Features.UiHider.UiHider.Conf.RecordingMode = rec; profileCount++; }
            if(TryReadXmlBool(hideUi, "UseRecordingModeShortcut", out bool useSc)) { Features.UiHider.UiHider.Conf.UseShortcut = useSc; profileCount++; }

            XElement shortcut = FindFirstDescendant(hideUi, "RecordingModeShortcut");
            if(shortcut != null) {
                ApplyShortcutModifierXml(shortcut);
                if(TryReadXmlKeyCode(shortcut, "PressKey", out int key)) {
                    Features.UiHider.UiHider.Conf.ShortcutKey = NormalizeKeyInt(key);
                    profileCount++;
                }
            }

            if(profileCount > 0) {
                Features.UiHider.UiHider.Conf.Enabled = true;
                count += profileCount + 1;
            }
        }

        XDocument restrict = LoadXml(option, "RestrictGameplaySettings.xml");
        if(restrict != null
            && TryReadXmlBool(restrict, "IsEnabled", out bool rOn) && rOn
            && TryReadXmlBool(restrict, "RestrictJudgment", out bool rJ) && rJ) {
            bool[] restricted = ReadXmlBoolArray(restrict, "RestrictedJudgments");
            if(restricted.Length > 0) {
                count += ApplyRestrictMask(restricted);
            }
        }

        return count;
    }

    private static int ApplyRestrictMask(bool[] restricted) {
        int allowedMask = 0;
        for(int i = 0; i < restricted.Length; i++) {
            if(!restricted[i]) {
                allowedMask |= 1 << i;
            }
        }
        Features.Restriction.Restriction.EnsureConf();
        Features.Restriction.Restriction.Conf.JRestrictEnabled = true;
        Features.Restriction.Restriction.Conf.JRestrictMode = 3;
        Features.Restriction.Restriction.Conf.JRestrictAllowedMask = allowedMask;
        return 3;
    }

    // ===== EnhancedEffectRemover =====

    private static int ImportEnhancedEffectRemover(SettingsImportOption option) {
        int count = 0;
        object settings = GetStaticMember(FindType(option, "EnhancedEffectRemover.Settings"), "Instance");
        if(settings != null) {
            count += ApplyEffectRemover(name => GetMemberValue(settings, name));
        }

        if(count == 0) {
            string json = ReadFirstText([Path.Combine(option.Directory ?? "", "Settings.json")]);
            if(!string.IsNullOrEmpty(json)) {
                try {
                    JObject root = JObject.Parse(json);
                    count += ApplyEffectRemover(name =>
                        root.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken t) ? t : null);
                } catch { }
            }
        }
        return count;
    }

    // Shared mapping for both the runtime object and the JSON file: `get`
    // returns a raw value (boxed CLR value or JToken) for a source field name.
    private static int ApplyEffectRemover(Func<string, object> get) {
        Features.EffectRemover.EffectRemover.EnsureConf();
        EffectRemoverSettings c = Features.EffectRemover.EffectRemover.Conf;
        int count = 0;

        c.On = true;
        count++;

        void Flag(string srcName, Action<bool> set) {
            if(TryConvertBool(get(srcName), out bool v)) { set(v); count++; }
        }

        if(TryConvertFloat(get("CameraZoomScale"), out float zoom)) { c.CameraZoomScale = zoom; count++; }
        Flag("EnableSave", v => c.EnableSave = v);
        Flag("ResetTrackAnimation", v => c.ResetTrackAnimation = v);
        Flag("ResetTrackColor", v => c.ResetTrackColor = v);
        Flag("RemoveAllDecorations", v => c.RemoveAllDecorations = v);
        Flag("ResetTrackOpacity", v => c.LimitTrackOpacity = v);
        Flag("SetCameraZoomScale", v => c.SetCameraZoom = v);
        Flag("Filters", v => c.Filters = v);
        Flag("AdvFilters", v => c.AdvancedFilters = v);
        Flag("Particles", v => c.Particles = v);
        Flag("Decorations", v => c.Decorations = v);
        Flag("Backgrounds", v => c.Backgrounds = v);
        Flag("Cameras", v => c.Cameras = v);
        Flag("RepeatEvents", v => c.RepeatEvents = v);
        Flag("FrameRate", v => c.FrameRate = v);
        Flag("HitSounds", v => c.HitSounds = v);
        Flag("PlanetOrbit", v => c.PlanetOrbit = v);
        Flag("PlanetScale", v => c.PlanetScale = v);
        Flag("PlanetRadius", v => c.PlanetRadius = v);
        Flag("TrackAnimations", v => c.TrackAnimations = v);
        Flag("TrackPos", v => c.TrackPositions = v);
        Flag("TrackMove", v => c.TrackMoves = v);
        Flag("TrackColors", v => c.TrackColors = v);
        Flag("HoldSounds", v => c.HoldSounds = v);
        Flag("HideIcons", v => c.HideIcons = v);
        if(TryConvertBool(get("CheckPoints"), out bool cp)) {
            Features.Tweaks.Tweaks.EnsureConf();
            Features.Tweaks.Tweaks.Conf.RemoveAllCheckpoints = cp;
            count++;
        }
        return count;
    }

    // ===== KorenResourcePackV1 =====

    // v1 is this mod's predecessor and v2 is a near-faithful rewrite of it, so
    // almost every v1 field has a direct v2 home. v1 persists ONE flat
    // `Settings : ModSettings` object whose field names equal its Settings.xml
    // element names, so a single mapping body (ApplyV1Common) drives both the
    // live-object path (reflection, primary) and the on-disk XML path (fallback)
    // via a small reader abstraction.
    private sealed class V1Reader {
        public Func<string, object> Scalar;    // bool/int/float/string leaf
        public Func<string, int[]> Keys;       // positional, length preserved, per-slot normalized
        public Func<string, string[]> Labels;  // positional labels
    }

    private static int ImportKorenResourcePackV1(
        SettingsImportOption option,
        SettingsImportReplaceMode keyViewerMode,
        SettingsImportKeyViewerPart keyViewerParts
    ) {
        int count = 0;

        object live = GetStaticMember(FindType(option, "KorenResourcePack.Main"), "settings");
        if(live != null) {
            count += ApplyV1Common(V1FromObject(live), keyViewerMode, keyViewerParts);
            count += ImportV1UiHider(live); // nested profile objects — reflection only
        }

        // Fall back to the on-disk Settings.xml only if nothing came through live
        // (e.g. the mod's static settings weren't reachable).
        if(count == 0) {
            XDocument doc = LoadXml(option, "Settings.xml");
            if(doc?.Root != null) {
                count += ApplyV1Common(V1FromXml(doc.Root), keyViewerMode, keyViewerParts);
            }
        }

        return count;
    }

    private static V1Reader V1FromObject(object live) => new() {
        Scalar = name => GetMemberValue(live, name),
        Keys = name => ReadPositionalKeys(GetMemberValue(live, name)),
        Labels = name => ReadStringArray(GetMemberValue(live, name)),
    };

    private static V1Reader V1FromXml(XElement root) => new() {
        Scalar = name => FindFirstDescendant(root, name)?.Value,
        Keys = name => ReadPositionalKeysXml(root, name),
        Labels = name => ReadPositionalLabelsXml(root, name),
    };

    private static int ApplyV1Common(
        V1Reader r,
        SettingsImportReplaceMode keyViewerMode,
        SettingsImportKeyViewerPart keyViewerParts
    ) {
        int count = 0;

        // --- ChatterBlocker ---
        if(TryConvertBool(r.Scalar("KCBOn"), out bool kcbOn)) {
            Features.ChatterBlocker.ChatterBlocker.EnsureConf();
            Features.ChatterBlocker.ChatterBlocker.Conf.Enabled = kcbOn;
            count++;
        }
        if(TryConvertFloat(r.Scalar("KCBThresholdMs"), out float kcbMs)) {
            Features.ChatterBlocker.ChatterBlocker.EnsureConf();
            Features.ChatterBlocker.ChatterBlocker.Conf.ThresholdMs = Mathf.Max(0f, kcbMs);
            count++;
        }

        // --- KeyLimiter ---
        if(TryConvertBool(r.Scalar("KeyLimiterOn"), out bool klOn)) {
            Features.KeyLimiter.KeyLimiter.EnsureConf();
            Features.KeyLimiter.KeyLimiter.Conf.Enabled = klOn;
            count++;
        }
        int[] klKeys = r.Keys("KeyLimiterAllowed");
        if(klKeys is { Length: > 0 }) {
            Features.KeyLimiter.KeyLimiter.SetAllowedKeys(klKeys);
            count++;
        }

        // --- KeyViewer (simple) ---
        ImportedKeyViewer kv = ReadKeyViewerFromV1(r);
        if(kv.Available != SettingsImportKeyViewerPart.None) {
            count += ApplyKeyViewerImport(kv, keyViewerMode, keyViewerParts);
        }

        // --- Combo ---
        ComboOverlay.EnsureConf();
        if(TryConvertBool(r.Scalar("comboOn"), out bool comboOn)) { ComboOverlay.Conf.Enabled = comboOn; count++; }
        if(TryConvertBool(r.Scalar("EnableAutoCombo"), out bool autoCombo)) { ComboOverlay.Conf.CountAuto = autoCombo; count++; }
        if(TryConvertInt(r.Scalar("ComboColorMax"), out int comboMax)) { ComboOverlay.Conf.ColorMax = comboMax; count++; }
        if(TryConvertBool(r.Scalar("XPerfectComboEnabled"), out bool xperfCombo)) { ComboOverlay.Conf.XPerfectComboEnabled = xperfCombo; count++; }
        if(V1Color(r, "ComboColorLow") is { } comboLow) { ComboOverlay.Conf.SetColorLow(comboLow); count++; }
        if(V1Color(r, "ComboColorHigh") is { } comboHigh) { ComboOverlay.Conf.SetColorHigh(comboHigh); count++; }

        // --- Judgement ---
        JudgementOverlay.EnsureConf();
        if(TryConvertBool(r.Scalar("judgementOn"), out bool judgeOn)) { JudgementOverlay.Conf.Enabled = judgeOn; count++; }
        // v1 drives the judgement position purely from judgementPositionY; its
        // LocationUp flag is set in the old UI but never read by the renderer, so
        // it's deliberately ignored here rather than mapped to a phantom shift.
        if(TryConvertFloat(r.Scalar("judgementPositionY"), out float judgeY)) { JudgementOverlay.Conf.OffsetY = judgeY; count++; }
        if(TryConvertFloat(r.Scalar("judgementSize"), out float judgeSize)) { JudgementOverlay.Conf.Size = judgeSize; count++; }
        if(TryConvertFloat(r.Scalar("judgementSpacing"), out float judgeSpace)) { JudgementOverlay.Conf.Spacing = judgeSpace; count++; }

        // --- ProgressBar ---
        ProgressBarOverlay.EnsureConf();
        if(TryConvertBool(r.Scalar("progressBarOn"), out bool pbOn)) { ProgressBarOverlay.Conf.Enabled = pbOn; count++; }
        if(V1Color(r, "ProgressBarFill") is { } pbFill) { ProgressBarOverlay.Conf.SetFillColor(pbFill); count++; }
        if(V1Color(r, "ProgressBarBack") is { } pbBack) { ProgressBarOverlay.Conf.SetBackColor(pbBack); count++; }
        if(V1Color(r, "ProgressBarBorder") is { } pbBorder) { ProgressBarOverlay.Conf.SetOutlineColor(pbBorder); count++; }

        // --- Otto icon ---
        Features.OttoIcon.OttoIcon.EnsureConf();
        if(TryConvertBool(r.Scalar("ChangeOttoIcon"), out bool ottoOn)) { Features.OttoIcon.OttoIcon.Conf.Enabled = ottoOn; count++; }
        if(V1Color(r, "Otto") is { } ottoColor) { Features.OttoIcon.OttoIcon.Conf.SetColor(ottoColor); count++; }
        if(TryConvertFloat(r.Scalar("OttoOffsetX"), out float ottoX)) { Features.OttoIcon.OttoIcon.Conf.OffsetX = ottoX; count++; }
        if(TryConvertFloat(r.Scalar("OttoOffsetY"), out float ottoY)) { Features.OttoIcon.OttoIcon.Conf.OffsetY = ottoY; count++; }

        // --- Planet (ball/ring) colors ---
        Features.PlanetColors.PlanetColors.EnsureConf();
        PlanetColorsSettings planet = Features.PlanetColors.PlanetColors.Conf;
        if(TryConvertBool(r.Scalar("ChangeBallColor"), out bool ballOn)) { planet.Enabled = ballOn; count++; }
        for(int slot = 0; slot < 3; slot++) {
            string prefix = "BallPlanet" + (slot + 1);
            if(V1Color(r, prefix) is { } ballColor) { planet.SetBallRgb(slot, ballColor); count++; }
            if(TryConvertFloat(r.Scalar(prefix + "Opacity"), out float ballOp)) { planet.BallOpacity[slot] = Mathf.Clamp01(ballOp); count++; }
        }
        if(TryConvertBool(r.Scalar("ChangeRingColor"), out bool ringOn)) { planet.EnableRingRecolor = ringOn; count++; }
        if(V1Color(r, "Ring") is { } ringColor) { planet.SetRingRgb(ringColor); count++; }

        // --- Judgement restriction + death limit (field names line up directly) ---
        Features.Restriction.Restriction.EnsureConf();
        RestrictionSettings restrict = Features.Restriction.Restriction.Conf;
        if(TryConvertBool(r.Scalar("JRestrictOn"), out bool jrOn)) { restrict.JRestrictEnabled = jrOn; count++; }
        if(TryConvertInt(r.Scalar("JRestrictMode"), out int jrMode)) { restrict.JRestrictMode = jrMode; count++; }
        if(TryConvertFloat(r.Scalar("JRestrictAccuracy"), out float jrAcc)) { restrict.JRestrictAccuracy = jrAcc; count++; }
        if(TryConvertInt(r.Scalar("JRestrictAllowedMask"), out int jrMask)) { restrict.JRestrictAllowedMask = jrMask; count++; }
        if(TryConvertBool(r.Scalar("DeathLimitOn"), out bool dlOn)) { restrict.DeathLimitEnabled = dlOn; count++; }
        if(TryConvertBool(r.Scalar("DeathLimitMaxDeathsOn"), out bool dlDeathsOn)) { restrict.MaxDeathsOn = dlDeathsOn; count++; }
        if(TryConvertInt(r.Scalar("DeathLimitMaxDeaths"), out int dlDeaths)) { restrict.MaxDeaths = dlDeaths; count++; }
        if(TryConvertBool(r.Scalar("DeathLimitMaxMissesOn"), out bool dlMissOn)) { restrict.MaxMissesOn = dlMissOn; count++; }
        if(TryConvertInt(r.Scalar("DeathLimitMaxMisses"), out int dlMiss)) { restrict.MaxMisses = dlMiss; count++; }
        if(TryConvertBool(r.Scalar("DeathLimitMaxOverloadsOn"), out bool dlOverOn)) { restrict.MaxOverloadsOn = dlOverOn; count++; }
        if(TryConvertInt(r.Scalar("DeathLimitMaxOverloads"), out int dlOver)) { restrict.MaxOverloads = dlOver; count++; }

        // --- Tweaks (the toggles v2 still has) ---
        Features.Tweaks.Tweaks.EnsureConf();
        TweaksSettings tweaks = Features.Tweaks.Tweaks.Conf;
        void TweakFlag(string name, Action<bool> set) {
            if(TryConvertBool(r.Scalar(name), out bool v)) { set(v); count++; }
        }
        TweakFlag("RemoveAllCheckpoints", v => tweaks.RemoveAllCheckpoints = v);
        TweakFlag("RemoveBallCoreParticles", v => tweaks.RemoveBallCoreParticles = v);
        TweakFlag("DisableTileHitGlow", v => tweaks.DisableTileHitGlow = v);
        TweakFlag("RemovePlanetGlow", v => tweaks.RemovePlanetGlow = v);
        TweakFlag("DisableAutoPause", v => tweaks.DisableAutoPause = v);
        TweakFlag("BlockMouseWheelScrollWhilePlaying", v => tweaks.BlockMouseWheelScrollWhilePlaying = v);

        // --- Effect remover (v1 names are the v2 ones, prefixed "EffectRemover") ---
        Features.EffectRemover.EffectRemover.EnsureConf();
        EffectRemoverSettings effect = Features.EffectRemover.EffectRemover.Conf;
        if(TryConvertBool(r.Scalar("EffectRemoverOn"), out bool erOn)) { effect.On = erOn; count++; }
        if(TryConvertFloat(r.Scalar("EffectRemoverCameraZoomScale"), out float erZoom)) { effect.CameraZoomScale = erZoom; count++; }
        void EffectFlag(string name, Action<bool> set) {
            if(TryConvertBool(r.Scalar(name), out bool v)) { set(v); count++; }
        }
        EffectFlag("EffectRemoverEnableSave", v => effect.EnableSave = v);
        EffectFlag("EffectRemoverResetTrackAnimation", v => effect.ResetTrackAnimation = v);
        EffectFlag("EffectRemoverResetTrackColor", v => effect.ResetTrackColor = v);
        EffectFlag("EffectRemoverRemoveAllDecorations", v => effect.RemoveAllDecorations = v);
        EffectFlag("EffectRemoverResetTrackOpacity", v => effect.LimitTrackOpacity = v);
        EffectFlag("EffectRemoverSetCameraZoom", v => effect.SetCameraZoom = v);
        EffectFlag("EffectRemoverFilters", v => effect.Filters = v);
        EffectFlag("EffectRemoverAdvancedFilters", v => effect.AdvancedFilters = v);
        EffectFlag("EffectRemoverParticles", v => effect.Particles = v);
        EffectFlag("EffectRemoverDecorations", v => effect.Decorations = v);
        EffectFlag("EffectRemoverBackgrounds", v => effect.Backgrounds = v);
        EffectFlag("EffectRemoverCameras", v => effect.Cameras = v);
        EffectFlag("EffectRemoverRepeatEvents", v => effect.RepeatEvents = v);
        EffectFlag("EffectRemoverFrameRate", v => effect.FrameRate = v);
        EffectFlag("EffectRemoverHitSounds", v => effect.HitSounds = v);
        EffectFlag("EffectRemoverPlanetOrbit", v => effect.PlanetOrbit = v);
        EffectFlag("EffectRemoverPlanetScale", v => effect.PlanetScale = v);
        EffectFlag("EffectRemoverPlanetRadius", v => effect.PlanetRadius = v);
        EffectFlag("EffectRemoverTrackAnimations", v => effect.TrackAnimations = v);
        EffectFlag("EffectRemoverTrackPositions", v => effect.TrackPositions = v);
        EffectFlag("EffectRemoverTrackMoves", v => effect.TrackMoves = v);
        EffectFlag("EffectRemoverTrackColors", v => effect.TrackColors = v);
        EffectFlag("EffectRemoverHoldSounds", v => effect.HoldSounds = v);
        EffectFlag("EffectRemoverHideIcons", v => effect.HideIcons = v);

        return count;
    }

    // UI hiding lives in nested profile objects (UiHidingPlayingProfile /
    // RecordingProfile), whose fields match v2's UiHiderProfile one-for-one — so
    // the AdofaiTweaks profile copier applies verbatim. Reflection only; the XML
    // fallback skips it.
    private static int ImportV1UiHider(object live) {
        Features.UiHider.UiHider.EnsureConf();
        int count = 0;
        count += ApplyAdofaiHideUiProfile(GetMemberValue(live, "UiHidingPlayingProfile"), Features.UiHider.UiHider.Conf.Playing);
        count += ApplyAdofaiHideUiProfile(GetMemberValue(live, "UiHidingRecordingProfile"), Features.UiHider.UiHider.Conf.Recording);

        if(TryGetBool(live, "UiHidingRecordingMode", out bool rec)) { Features.UiHider.UiHider.Conf.RecordingMode = rec; count++; }
        if(TryGetBool(live, "UiHidingUseRecordingModeShortcut", out bool useShortcut)) { Features.UiHider.UiHider.Conf.UseShortcut = useShortcut; count++; }

        if(TryGetBool(live, "UiHidingShortcutCtrl", out bool ctrl) && ctrl) {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.Ctrl;
        } else if(TryGetBool(live, "UiHidingShortcutAlt", out bool alt) && alt) {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.Alt;
        } else if(TryGetBool(live, "UiHidingShortcutShift", out bool shift) && shift) {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.Shift;
        } else {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.None;
        }
        if(TryGetInt(live, "UiHidingShortcutKey", out int key)) { Features.UiHider.UiHider.Conf.ShortcutKey = NormalizeKeyInt(key); count++; }

        if(TryGetBool(live, "UiHidingOn", out bool on)) { Features.UiHider.UiHider.Conf.Enabled = on; count++; }
        return count;
    }

    private static ImportedKeyViewer ReadKeyViewerFromV1(V1Reader r) {
        ImportedKeyViewer kv = new();

        if(TryConvertInt(r.Scalar("KeyViewerSimpleStyle"), out int style)) { kv.HasStyle = true; kv.Style = style; }
        kv.Key10 = r.Keys("KeyViewerSimpleKey10");
        kv.Key12 = r.Keys("KeyViewerSimpleKey12");
        kv.Key16 = r.Keys("KeyViewerSimpleKey16");
        kv.Key20 = r.Keys("KeyViewerSimpleKey20");

        // Foot keys: v1 keeps a separate array per foot style; v2 keeps one flat
        // FootKeys[16] + a style whose key-count is style*2. v1 styles 1-4 (2/4/
        // 6/8 keys) line up 1:1; v1 style 5 is the 16-key layout, which is v2's
        // style 8.
        if(TryConvertInt(r.Scalar("KeyViewerSimpleFootStyle"), out int footStyle)) {
            kv.HasFoot = true;
            kv.FootStyle = footStyle == 5 ? 8 : Mathf.Clamp(footStyle, 0, 4);
            string footField = footStyle switch {
                1 => "KeyViewerSimpleFootKey2",
                2 => "KeyViewerSimpleFootKey4",
                3 => "KeyViewerSimpleFootKey6",
                4 => "KeyViewerSimpleFootKey8",
                5 => "KeyViewerSimpleFootKey16",
                _ => null,
            };
            kv.FootKeys = footField == null ? null : r.Keys(footField);
        }

        // Ghost keys map array-for-array (v2 kept v1's per-style ghost layout).
        kv.GhostKey10 = r.Keys("KeyViewerSimpleGhostKey10");
        kv.GhostKey12 = r.Keys("KeyViewerSimpleGhostKey12");
        kv.GhostKey16 = r.Keys("KeyViewerSimpleGhostKey16");
        kv.GhostKey20 = r.Keys("KeyViewerSimpleGhostKey20");

        if(kv.HasStyle || AnyKeys(kv) || kv.HasFoot
            || AnyGhost(kv.GhostKey10, kv.GhostKey12, kv.GhostKey16, kv.GhostKey20)) {
            kv.Available |= SettingsImportKeyViewerPart.KeysLayout;
        }

        kv.Key10Text = r.Labels("KeyViewerSimpleKey10Text");
        kv.Key12Text = r.Labels("KeyViewerSimpleKey12Text");
        kv.Key16Text = r.Labels("KeyViewerSimpleKey16Text");
        kv.Key20Text = r.Labels("KeyViewerSimpleKey20Text");
        if(AnyLabels(kv)) {
            kv.Available |= SettingsImportKeyViewerPart.Labels;
        }

        kv.Bg = V1Color(r, "SKvBg");
        kv.BgClicked = V1Color(r, "SKvBgc");
        kv.Outline = V1Color(r, "SKvOut");
        kv.OutlineClicked = V1Color(r, "SKvOutc");
        kv.Text = V1Color(r, "SKvTxt");
        kv.TextClicked = V1Color(r, "SKvTxtc");
        kv.Rain = V1Color(r, "SKvRain");
        kv.Rain2 = V1Color(r, "SKvRain2");
        kv.Rain3 = V1Color(r, "SKvRain3");
        kv.GhostRain = V1Color(r, "SKvGhostRain");
        if(AnyColors(kv) || kv.GhostRain != null) {
            kv.Available |= SettingsImportKeyViewerPart.Colors;
        }

        if(TryConvertBool(r.Scalar("KeyViewerSimpleUseRain"), out bool useRain)) { kv.HasRainEnabled = true; kv.RainEnabled = useRain; }
        if(TryConvertFloat(r.Scalar("KeyViewerSimpleRainSpeed"), out float rainSpeed)) { kv.HasRainSpeed = true; kv.RainSpeed = rainSpeed; }
        if(TryConvertFloat(r.Scalar("KeyViewerSimpleRainHeight"), out float rainHeight)) { kv.HasRainHeight = true; kv.RainHeight = rainHeight; }
        if(kv.HasRainEnabled || kv.HasRainSpeed || kv.HasRainHeight) {
            kv.Available |= SettingsImportKeyViewerPart.Rain;
        }

        if(TryConvertFloat(r.Scalar("KeyViewerSimpleSize"), out float size)) { kv.HasSize = true; kv.Size = Mathf.Clamp(size, 0.1f, 3f); }
        if(kv.HasSize) {
            kv.Available |= SettingsImportKeyViewerPart.PositionSize;
        }

        if(TryConvertBool(r.Scalar("keyViewerOn"), out bool en)) { kv.HasEnabled = true; kv.Enabled = en; }
        if(TryConvertBool(r.Scalar("KeyViewerSimpleSyncToKeyLimiter"), out bool sync)) { kv.HasSync = true; kv.SyncToKeyLimiter = sync; }

        return kv;
    }

    private static bool AnyGhost(params int[][] arrays) {
        foreach(int[] arr in arrays) {
            if(arr != null && arr.Any(k => k != 0)) {
                return true;
            }
        }
        return false;
    }

    // Assemble a v2 Color from v1's flat per-channel float fields (prefix + R/G/
    // B/A). Alpha is optional — fields like the ball-planet colors store no A.
    private static Color? V1Color(V1Reader r, string prefix) {
        if(TryConvertFloat(r.Scalar(prefix + "R"), out float cr)
            && TryConvertFloat(r.Scalar(prefix + "G"), out float cg)
            && TryConvertFloat(r.Scalar(prefix + "B"), out float cb)) {
            float a = TryConvertFloat(r.Scalar(prefix + "A"), out float ca) ? ca : 1f;
            return new Color(Mathf.Clamp01(cr), Mathf.Clamp01(cg), Mathf.Clamp01(cb), Mathf.Clamp01(a));
        }
        return null;
    }

    // Positional key reader: unlike ReadKeyCodeEnumerable (a deduped SET, used
    // for allow-lists), these arrays are slot-indexed — a 0 means "no key for
    // this slot" and duplicates/positions must be preserved.
    private static int[] ReadPositionalKeys(object value) {
        if(value is not IEnumerable enumerable || value is string) {
            return null;
        }
        List<int> result = [];
        foreach(object item in enumerable) {
            result.Add(TryConvertKeyCode(item, out int key) ? key : 0);
        }
        return result.Count > 0 ? [.. result] : null;
    }

    private static int[] ReadPositionalKeysXml(XElement root, string name) {
        XElement list = FindFirstDescendant(root, name);
        if(list == null) {
            return null;
        }
        List<int> result = [];
        foreach(XElement item in list.Elements()) {
            result.Add(TryConvertKeyCode(item.Value, out int key) ? key : 0);
        }
        return result.Count > 0 ? [.. result] : null;
    }

    private static string[] ReadPositionalLabelsXml(XElement root, string name) {
        XElement list = FindFirstDescendant(root, name);
        if(list == null) {
            return null;
        }
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        List<string> result = [];
        foreach(XElement item in list.Elements()) {
            bool nil = string.Equals((string)item.Attribute(xsi + "nil"), "true", StringComparison.OrdinalIgnoreCase);
            result.Add(nil ? "" : item.Value);
        }
        return result.Count > 0 ? [.. result] : null;
    }

    // ===== KeyViewer intermediate =====

    // The subset of a source KeyViewer's settings that v2 can receive. Filled
    // from either a runtime object (reflection) or a parsed config (JSON), then
    // merged into v2's KeyViewerSettings honoring the replace mode/parts.
    private sealed class ImportedKeyViewer {
        public SettingsImportKeyViewerPart Available;

        public bool HasStyle;
        public int Style;
        public int[] Key10, Key12, Key16, Key20;
        public string[] Key10Text, Key12Text, Key16Text, Key20Text;

        public Color? Bg, BgClicked, Outline, OutlineClicked, Text, TextClicked, Rain, Rain2, Rain3;

        public bool HasRainEnabled; public bool RainEnabled;
        public bool HasRainSpeed; public float RainSpeed;
        public bool HasRainHeight; public float RainHeight;

        public bool HasSize; public float Size;
        public bool HasEnabled; public bool Enabled;
        public bool HasSync; public bool SyncToKeyLimiter;

        // v1 KorenResourcePack extras. v2 inherited v1's foot-key and ghost-rain
        // model, so these map across; the other sources never set them (they
        // stay null/false and ApplyKeyViewerImport skips them).
        public bool HasFoot; public int FootStyle; public int[] FootKeys;
        public int[] GhostKey10, GhostKey12, GhostKey16, GhostKey20;
        public Color? GhostRain;
    }

    private static ImportedKeyViewer ReadKeyViewerFromObject(object src) {
        ImportedKeyViewer kv = new();

        if(TryParseKvStyle(GetMemberValue(src, "KeyViewerStyle"), out int style)) {
            kv.HasStyle = true;
            kv.Style = style;
        }
        kv.Key10 = ReadKeyCodesFromMember(src, "key10");
        kv.Key12 = ReadKeyCodesFromMember(src, "key12");
        kv.Key16 = ReadKeyCodesFromMember(src, "key16");
        kv.Key20 = ReadKeyCodesFromMember(src, "key20");
        if(kv.HasStyle || AnyKeys(kv)) {
            kv.Available |= SettingsImportKeyViewerPart.KeysLayout;
        }

        kv.Key10Text = ReadStringArray(GetMemberValue(src, "key10Text"));
        kv.Key12Text = ReadStringArray(GetMemberValue(src, "key12Text"));
        kv.Key16Text = ReadStringArray(GetMemberValue(src, "key16Text"));
        kv.Key20Text = ReadStringArray(GetMemberValue(src, "key20Text"));
        if(AnyLabels(kv)) {
            kv.Available |= SettingsImportKeyViewerPart.Labels;
        }

        kv.Bg = TryGetColor(GetMemberValue(src, "Background"), out Color bg) ? bg : null;
        kv.BgClicked = TryGetColor(GetMemberValue(src, "BackgroundClicked"), out Color bgc) ? bgc : null;
        kv.Outline = TryGetColor(GetMemberValue(src, "Outline"), out Color ol) ? ol : null;
        kv.OutlineClicked = TryGetColor(GetMemberValue(src, "OutlineClicked"), out Color olc) ? olc : null;
        kv.Text = TryGetColor(GetMemberValue(src, "Text"), out Color tx) ? tx : null;
        kv.TextClicked = TryGetColor(GetMemberValue(src, "TextClicked"), out Color txc) ? txc : null;
        kv.Rain = TryGetColor(GetMemberValue(src, "RainColor"), out Color rc) ? rc : null;
        kv.Rain2 = TryGetColor(GetMemberValue(src, "RainColor2"), out Color rc2) ? rc2 : null;
        kv.Rain3 = TryGetColor(GetMemberValue(src, "RainColor3"), out Color rc3) ? rc3 : null;
        if(AnyColors(kv)) {
            kv.Available |= SettingsImportKeyViewerPart.Colors;
        }

        if(TryGetBool(src, "useRain", out bool useRain)) { kv.HasRainEnabled = true; kv.RainEnabled = useRain; }
        if(TryGetFloat(src, "rainSpeed", out float rs)) { kv.HasRainSpeed = true; kv.RainSpeed = rs; }
        if(TryGetFloat(src, "rainHeight", out float rh)) { kv.HasRainHeight = true; kv.RainHeight = rh; }
        if(kv.HasRainEnabled || kv.HasRainSpeed || kv.HasRainHeight) {
            kv.Available |= SettingsImportKeyViewerPart.Rain;
        }

        if(TryGetFloat(src, "Size", out float size)) { kv.HasSize = true; kv.Size = Mathf.Clamp(size, 0.1f, 3f); }
        if(kv.HasSize) {
            kv.Available |= SettingsImportKeyViewerPart.PositionSize;
        }

        if(TryGetBool(src, "Enabled", out bool en)) { kv.HasEnabled = true; kv.Enabled = en; }
        if(TryGetBool(src, "SyncToKeyLimiter", out bool sync)) { kv.HasSync = true; kv.SyncToKeyLimiter = sync; }

        return kv;
    }

    private static ImportedKeyViewer ReadKeyViewerFromJson(JObject src) {
        if(src == null) {
            return null;
        }
        ImportedKeyViewer kv = new();

        if(TryParseKvStyle(JsonValue(src, "KeyViewerStyle"), out int style)) {
            kv.HasStyle = true;
            kv.Style = style;
        }
        kv.Key10 = ReadKeyCodesFromJson(src["key10"]);
        kv.Key12 = ReadKeyCodesFromJson(src["key12"]);
        kv.Key16 = ReadKeyCodesFromJson(src["key16"]);
        kv.Key20 = ReadKeyCodesFromJson(src["key20"]);
        if(kv.HasStyle || AnyKeys(kv)) {
            kv.Available |= SettingsImportKeyViewerPart.KeysLayout;
        }

        kv.Key10Text = ReadStringArrayJson(src["key10Text"]);
        kv.Key12Text = ReadStringArrayJson(src["key12Text"]);
        kv.Key16Text = ReadStringArrayJson(src["key16Text"]);
        kv.Key20Text = ReadStringArrayJson(src["key20Text"]);
        if(AnyLabels(kv)) {
            kv.Available |= SettingsImportKeyViewerPart.Labels;
        }

        kv.Bg = ReadJsonColor(src["Background"]);
        kv.BgClicked = ReadJsonColor(src["BackgroundClicked"]);
        kv.Outline = ReadJsonColor(src["Outline"]);
        kv.OutlineClicked = ReadJsonColor(src["OutlineClicked"]);
        kv.Text = ReadJsonColor(src["Text"]);
        kv.TextClicked = ReadJsonColor(src["TextClicked"]);
        kv.Rain = ReadJsonColor(src["RainColor"]);
        kv.Rain2 = ReadJsonColor(src["RainColor2"]);
        kv.Rain3 = ReadJsonColor(src["RainColor3"]);
        if(AnyColors(kv)) {
            kv.Available |= SettingsImportKeyViewerPart.Colors;
        }

        if(TryConvertBool(JsonValue(src, "useRain"), out bool useRain)) { kv.HasRainEnabled = true; kv.RainEnabled = useRain; }
        if(TryConvertFloat(JsonValue(src, "rainSpeed"), out float rs)) { kv.HasRainSpeed = true; kv.RainSpeed = rs; }
        if(TryConvertFloat(JsonValue(src, "rainHeight"), out float rh)) { kv.HasRainHeight = true; kv.RainHeight = rh; }
        if(kv.HasRainEnabled || kv.HasRainSpeed || kv.HasRainHeight) {
            kv.Available |= SettingsImportKeyViewerPart.Rain;
        }

        if(TryConvertFloat(JsonValue(src, "Size"), out float size)) { kv.HasSize = true; kv.Size = Mathf.Clamp(size, 0.1f, 3f); }
        if(kv.HasSize) {
            kv.Available |= SettingsImportKeyViewerPart.PositionSize;
        }

        if(TryConvertBool(JsonValue(src, "Enabled"), out bool en)) { kv.HasEnabled = true; kv.Enabled = en; }
        if(TryConvertBool(JsonValue(src, "SyncToKeyLimiter"), out bool sync)) { kv.HasSync = true; kv.SyncToKeyLimiter = sync; }

        return kv;
    }

    private static int ApplyKeyViewerImport(
        ImportedKeyViewer kv,
        SettingsImportReplaceMode mode,
        SettingsImportKeyViewerPart parts
    ) {
        if(kv == null || mode == SettingsImportReplaceMode.KeepOld) {
            return 0;
        }

        SettingsImportKeyViewerPart effective = mode == SettingsImportReplaceMode.ReplaceCertain
            ? parts & kv.Available
            : kv.Available;
        if(effective == SettingsImportKeyViewerPart.None) {
            return 0;
        }

        KeyViewerOverlay.EnsureConf();
        KeyViewerSettings target = KeyViewerOverlay.Conf;
        target.Mode = KeyViewerSettings.ModeSimple;
        int count = 0;

        if((effective & SettingsImportKeyViewerPart.KeysLayout) != 0) {
            if(kv.HasStyle) { target.Style = Mathf.Clamp(kv.Style, 0, 3); }
            if(kv.Key10 is { Length: 10 }) { target.Key10 = kv.Key10; }
            if(kv.Key12 is { Length: 12 }) { target.Key12 = kv.Key12; }
            if(kv.Key16 is { Length: 16 }) { target.Key16 = kv.Key16; }
            if(kv.Key20 is { Length: 20 }) { target.Key20 = kv.Key20; }

            // Foot keys + ghost keys ride along with the layout (v1 only).
            if(kv.HasFoot) {
                target.FootStyle = Mathf.Clamp(kv.FootStyle, 0, 8);
                if(kv.FootKeys is { Length: > 0 }) {
                    int n = Mathf.Min(kv.FootKeys.Length, target.FootKeys.Length);
                    for(int i = 0; i < n; i++) { target.FootKeys[i] = kv.FootKeys[i]; }
                }
            }
            if(kv.GhostKey10 is { Length: 10 }) { target.GhostKey10 = kv.GhostKey10; }
            if(kv.GhostKey12 is { Length: 12 }) { target.GhostKey12 = kv.GhostKey12; }
            if(kv.GhostKey16 is { Length: 16 }) { target.GhostKey16 = kv.GhostKey16; }
            if(kv.GhostKey20 is { Length: 20 }) { target.GhostKey20 = kv.GhostKey20; }
            count++;
        }

        if((effective & SettingsImportKeyViewerPart.Labels) != 0) {
            if(kv.Key10Text is { Length: 10 }) { target.Key10Text = kv.Key10Text; }
            if(kv.Key12Text is { Length: 12 }) { target.Key12Text = kv.Key12Text; }
            if(kv.Key16Text is { Length: 16 }) { target.Key16Text = kv.Key16Text; }
            if(kv.Key20Text is { Length: 20 }) { target.Key20Text = kv.Key20Text; }
            count++;
        }

        if((effective & SettingsImportKeyViewerPart.Colors) != 0) {
            if(kv.Bg is { } bg) { target.SetBg(bg); }
            if(kv.BgClicked is { } bgc) { target.SetBgPressed(bgc); }
            if(kv.Outline is { } ol) { target.SetOutline(ol); }
            if(kv.OutlineClicked is { } olc) { target.SetOutlinePressed(olc); }
            if(kv.Text is { } tx) { target.SetText(tx); }
            if(kv.TextClicked is { } txc) { target.SetTextPressed(txc); }
            if(kv.Rain is { } rc) { target.SetRain(rc); }
            if(kv.Rain2 is { } rc2) { target.SetRain2(rc2); }
            if(kv.Rain3 is { } rc3) { target.SetRain3(rc3); }
            if(kv.GhostRain is { } gr) { target.SetGhostRain(gr); }
            count++;
        }

        if((effective & SettingsImportKeyViewerPart.Rain) != 0) {
            if(kv.HasRainEnabled) { target.RainEnabled = kv.RainEnabled; }
            if(kv.HasRainSpeed) { target.RainSpeed = kv.RainSpeed; }
            if(kv.HasRainHeight) { target.RainHeight = kv.RainHeight; }
            count++;
        }

        if((effective & SettingsImportKeyViewerPart.PositionSize) != 0) {
            if(kv.HasSize) { target.Size = kv.Size; }
            count++;
        }

        // Display fields ride along only on a full replace (no part toggle).
        if(mode == SettingsImportReplaceMode.ReplaceAll) {
            if(kv.HasEnabled) { target.Enabled = kv.Enabled; }
            if(kv.HasSync) { target.SyncToKeyLimiter = kv.SyncToKeyLimiter; }
        }

        return count;
    }

    private static bool AnyKeys(ImportedKeyViewer kv) =>
        kv.Key10?.Length > 0 || kv.Key12?.Length > 0 || kv.Key16?.Length > 0 || kv.Key20?.Length > 0;

    private static bool AnyLabels(ImportedKeyViewer kv) =>
        kv.Key10Text?.Length > 0 || kv.Key12Text?.Length > 0 || kv.Key16Text?.Length > 0 || kv.Key20Text?.Length > 0;

    private static bool AnyColors(ImportedKeyViewer kv) =>
        kv.Bg != null || kv.BgClicked != null || kv.Outline != null || kv.OutlineClicked != null
        || kv.Text != null || kv.TextClicked != null || kv.Rain != null || kv.Rain2 != null || kv.Rain3 != null;

    // The source's style is an enum/int/string; v2 styles are 0=10,1=12,2=16,
    // 3=20. Map by the key count embedded in the name; fall back to the raw int.
    private static bool TryParseKvStyle(object value, out int style) {
        style = 0;
        if(value == null) {
            return false;
        }
        string text = value.ToString();
        string digits = new(text.Where(char.IsDigit).ToArray());
        if(int.TryParse(digits, out int keys)) {
            style = keys switch { 10 => 0, 12 => 1, 20 => 3, _ => 2 };
            return true;
        }
        if(int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw)) {
            style = Mathf.Clamp(raw, 0, 3);
            return true;
        }
        return false;
    }

    // ===== post-import refresh =====

    // Re-apply + persist every feature the import could have touched. Each call
    // is guarded so an overlay that isn't live (mod disabled) can't abort the
    // run, and every feature's Apply/Refresh is already change-guarded.
    private static void PostImportRefresh() {
        Try(() => { Features.ChatterBlocker.ChatterBlocker.EnsureConf(); Features.ChatterBlocker.ChatterBlocker.Save(); });
        Try(() => { Features.KeyLimiter.KeyLimiter.EnsureConf(); Features.KeyLimiter.KeyLimiter.Save(); });
        Try(() => { KeyViewerOverlay.EnsureConf(); KeyViewerOverlay.SyncKeysToKeyLimiter(); });
        Try(() => { KeyViewerOverlay.Rebuild(); });
        Try(() => { KeyViewerOverlay.Apply(); });
        Try(() => { KeyViewerOverlay.Save(); });
        Try(() => { ComboOverlay.EnsureConf(); ComboOverlay.Apply(); ComboOverlay.Save(); });
        Try(() => { JudgementOverlay.EnsureConf(); JudgementOverlay.Apply(); JudgementOverlay.Save(); });
        Try(() => { ProgressBarOverlay.EnsureConf(); ProgressBarOverlay.Apply(); ProgressBarOverlay.Save(); });
        Try(() => { Features.Tweaks.Tweaks.EnsureConf(); Features.Tweaks.Tweaks.RefreshAll(); Features.Tweaks.Tweaks.Save(); });
        Try(() => { Features.OttoIcon.OttoIcon.EnsureConf(); Features.OttoIcon.OttoIcon.Refresh(); Features.OttoIcon.OttoIcon.Save(); });
        Try(() => { Features.PlanetColors.PlanetColors.EnsureConf(); Features.PlanetColors.PlanetColors.Refresh(); Features.PlanetColors.PlanetColors.Save(); });
        Try(() => { Features.UiHider.UiHider.EnsureConf(); Features.UiHider.UiHider.ApplyNow(); Features.UiHider.UiHider.Save(); });
        Try(() => { Features.Restriction.Restriction.EnsureConf(); Features.Restriction.Restriction.Save(); });
        Try(() => { Features.EffectRemover.EffectRemover.EnsureConf(); Features.EffectRemover.EffectRemover.RefreshEditorSaveButtons(); Features.EffectRemover.EffectRemover.Save(); });
    }

    private static void Try(Action action) {
        try { action(); } catch(Exception e) { MainCore.Log.Wrn($"[SettingsImporter] refresh step failed: {e.Message}"); }
    }

    // ===== UI-hiding profile copy =====

    private static int ApplyAdofaiHideUiProfile(object profile, UiHiderProfile target) {
        if(profile == null || target == null) {
            return 0;
        }
        int count = 0;
        void Flag(string name, Action<bool> set) {
            if(TryGetBool(profile, name, out bool v)) { set(v); count++; }
        }
        Flag("HideEverything", v => target.HideEverything = v);
        Flag("HideJudgment", v => target.HideJudgment = v);
        Flag("HideMissIndicators", v => target.HideMissIndicators = v);
        Flag("HideTitle", v => target.HideTitle = v);
        Flag("HideOtto", v => target.HideOtto = v);
        Flag("HideTimingTarget", v => target.HideTimingTarget = v);
        Flag("HideNoFailIcon", v => target.HideNoFailIcon = v);
        Flag("HideBeta", v => target.HideBeta = v);
        Flag("HideResult", v => target.HideResult = v);
        Flag("HideHitErrorMeter", v => target.HideHitErrorMeter = v);
        Flag("HideLastFloorFlash", v => target.HideLastFloorFlash = v);
        return count;
    }

    private static int ApplyAdofaiHideUiProfileXml(XElement profile, UiHiderProfile target) {
        if(profile == null || target == null) {
            return 0;
        }
        int count = 0;
        void Flag(string name, Action<bool> set) {
            if(TryReadXmlBool(profile, name, out bool v)) { set(v); count++; }
        }
        Flag("HideEverything", v => target.HideEverything = v);
        Flag("HideJudgment", v => target.HideJudgment = v);
        Flag("HideMissIndicators", v => target.HideMissIndicators = v);
        Flag("HideTitle", v => target.HideTitle = v);
        Flag("HideOtto", v => target.HideOtto = v);
        Flag("HideTimingTarget", v => target.HideTimingTarget = v);
        Flag("HideNoFailIcon", v => target.HideNoFailIcon = v);
        Flag("HideBeta", v => target.HideBeta = v);
        Flag("HideResult", v => target.HideResult = v);
        Flag("HideHitErrorMeter", v => target.HideHitErrorMeter = v);
        Flag("HideLastFloorFlash", v => target.HideLastFloorFlash = v);
        return count;
    }

    private static void ApplyShortcutModifier(object shortcut) {
        if(TryGetBool(shortcut, "PressCtrl", out bool ctrl) && ctrl) {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.Ctrl;
        } else if(TryGetBool(shortcut, "PressAlt", out bool alt) && alt) {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.Alt;
        } else if(TryGetBool(shortcut, "PressShift", out bool shift) && shift) {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.Shift;
        } else {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.None;
        }
    }

    private static void ApplyShortcutModifierXml(XElement shortcut) {
        if(TryReadXmlBool(shortcut, "PressCtrl", out bool ctrl) && ctrl) {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.Ctrl;
        } else if(TryReadXmlBool(shortcut, "PressAlt", out bool alt) && alt) {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.Alt;
        } else if(TryReadXmlBool(shortcut, "PressShift", out bool shift) && shift) {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.Shift;
        } else {
            Features.UiHider.UiHider.Conf.ShortcutModifier = (int)Keybind.KeyModifier.None;
        }
    }

    // ===== matching / discovery =====

    private static bool EntryMatches(object entry, string id, ImportSpec spec) {
        if(entry == null) {
            return false;
        }
        string normId = NormalizeModToken(id);
        string normDisplay = NormalizeModToken(StripRichText(ReadNested(entry, "Info", "DisplayName") as string));
        string normFolder = NormalizeModToken(Path.GetFileName(ResolveDirectory(ReadMember(entry, "Path") as string) ?? ""));
        foreach(string alias in spec.Aliases) {
            string normAlias = NormalizeModToken(alias);
            if(normId == normAlias || normDisplay == normAlias || normFolder == normAlias) {
                return true;
            }
        }
        return false;
    }

    private static Assembly FindAssemblyByName(string id, ImportSpec spec) {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach(Assembly asm in assemblies) {
            string name = NormalizeModToken(asm.GetName().Name);
            if(name == NormalizeModToken(id)) {
                return asm;
            }
            foreach(string alias in spec.Aliases) {
                if(name == NormalizeModToken(alias)) {
                    return asm;
                }
            }
        }
        return null;
    }

    private static Type FindType(SettingsImportOption option, string fullName) {
        Type type = option.Assembly?.GetType(fullName, false);
        if(type != null) {
            return type;
        }
        // Fallback: an assembly loaded from the mod's folder.
        if(!string.IsNullOrEmpty(option.Directory)) {
            foreach(Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                try {
                    string loc = asm.Location;
                    if(!string.IsNullOrEmpty(loc)
                        && loc.StartsWith(option.Directory, StringComparison.OrdinalIgnoreCase)) {
                        Type t = asm.GetType(fullName, false);
                        if(t != null) {
                            return t;
                        }
                    }
                } catch { }
            }
        }
        return null;
    }

    // ===== reflection helpers =====

    private static object GetStaticMember(Type type, string name) {
        if(type == null || string.IsNullOrEmpty(name)) {
            return null;
        }
        FieldInfo field = type.GetField(name, AllMembers);
        if(field != null) {
            return field.GetValue(null);
        }
        PropertyInfo prop = type.GetProperty(name, AllMembers);
        return prop?.GetValue(null, null);
    }

    private static object GetMemberValue(object obj, string name) {
        if(obj == null || string.IsNullOrEmpty(name)) {
            return null;
        }
        Type type = obj as Type ?? obj.GetType();
        object instance = obj is Type ? null : obj;
        FieldInfo field = type.GetField(name, AllMembers);
        if(field != null) {
            return field.GetValue(instance);
        }
        PropertyInfo prop = type.GetProperty(name, AllMembers);
        return prop?.GetValue(instance, null);
    }

    private static object ReadMember(object target, string name) {
        if(target == null) {
            return null;
        }
        try {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if(f != null) {
                return f.GetValue(target);
            }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(target);
        } catch {
            return null;
        }
    }

    private static object ReadNested(object target, string first, string second) =>
        ReadMember(ReadMember(target, first), second);

    private static bool TryGetBool(object obj, string name, out bool value) => TryConvertBool(GetMemberValue(obj, name), out value);

    private static bool TryConvertBool(object obj, out bool value) {
        value = false;
        switch(obj) {
            case null:
                return false;
            case bool b:
                value = b;
                return true;
            case JValue jv when jv.Type == JTokenType.Boolean:
                value = jv.Value<bool>();
                return true;
        }
        string text = obj.ToString();
        if(bool.TryParse(text, out value)) {
            return true;
        }
        if(int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) {
            value = i != 0;
            return true;
        }
        return false;
    }

    private static bool TryGetInt(object obj, string name, out int value) => TryConvertInt(GetMemberValue(obj, name), out value);

    private static bool TryConvertInt(object raw, out int value) {
        value = 0;
        if(raw == null) {
            return false;
        }
        try {
            value = Convert.ToInt32(raw is JValue jv ? jv.Value : raw, CultureInfo.InvariantCulture);
            return true;
        } catch {
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }

    private static bool TryGetFloat(object obj, string name, out float value) => TryConvertFloat(GetMemberValue(obj, name), out value);

    private static bool TryConvertFloat(object raw, out float value) {
        value = 0f;
        if(raw == null) {
            return false;
        }
        try {
            value = Convert.ToSingle(raw is JValue jv ? jv.Value : raw, CultureInfo.InvariantCulture);
            return true;
        } catch {
            return float.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    private static bool TryGetColor(object value, out Color color) {
        color = Color.white;
        if(value == null) {
            return false;
        }
        if(value is Color c) {
            color = c;
            return true;
        }
        if(TryGetFloat(value, "r", out float r) && TryGetFloat(value, "g", out float g) && TryGetFloat(value, "b", out float b)) {
            float a = TryGetFloat(value, "a", out float aa) ? aa : 1f;
            color = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), Mathf.Clamp01(a));
            return true;
        }
        return false;
    }

    // The source mods store stat colors as a "ColorRange": a list of
    // progress→color points plus a PerfectColor. v2 takes flat colors, so pull
    // the lowest- and highest-progress points as the low/high endpoints.
    private static bool TryGetColorRangeEndpoints(object value, out Color low, out Color high) {
        low = high = Color.white;
        if(value == null) {
            return false;
        }

        List<(float progress, Color color)> points = [];
        if(GetMemberValue(value, "List") is IEnumerable list) {
            foreach(object item in list) {
                if(TryGetFloat(item, "Progress", out float p) && TryGetColor(item, out Color c)) {
                    points.Add((Mathf.Clamp01(p), c));
                }
            }
        }

        if(points.Count > 0) {
            points.Sort((x, y) => x.progress.CompareTo(y.progress));
            low = points[0].color;
            high = points[^1].color;
            return true;
        }

        if(TryGetColor(GetMemberValue(value, "PerfectColor"), out Color perfect)) {
            low = high = perfect;
            return true;
        }
        return false;
    }

    // ===== key/array readers =====

    private static int[] ReadKeyCodesFromMember(object obj, string member) => ReadKeyCodeEnumerable(GetMemberValue(obj, member));

    private static int[] ReadKeyCodeEnumerable(object value) {
        if(value is not IEnumerable enumerable || value is string) {
            return [];
        }
        List<int> result = [];
        foreach(object item in enumerable) {
            if(TryConvertKeyCode(item, out int key) && !result.Contains(key)) {
                result.Add(key);
            }
        }
        return [.. result];
    }

    private static int[] ReadKeyCodesFromJson(JToken token) {
        if(token is not JArray arr) {
            return [];
        }
        List<int> result = [];
        foreach(JToken t in arr) {
            if(TryConvertKeyCode(t is JValue jv ? jv.Value : t, out int key) && !result.Contains(key)) {
                result.Add(key);
            }
        }
        return [.. result];
    }

    private static bool TryConvertKeyCode(object value, out int key) {
        key = 0;
        if(value == null) {
            return false;
        }
        if(value is KeyCode kc) {
            key = NormalizeKeyInt((int)kc);
            return true;
        }
        if(value.GetType().IsEnum) {
            try { key = NormalizeKeyInt(Convert.ToInt32(value, CultureInfo.InvariantCulture)); return true; } catch { return false; }
        }
        if(value is IConvertible and not string) {
            try { key = NormalizeKeyInt(Convert.ToInt32(value, CultureInfo.InvariantCulture)); return true; } catch { }
        }
        string text = value.ToString();
        if(int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
            key = NormalizeKeyInt(parsed);
            return true;
        }
        if(Enum.TryParse(text, true, out KeyCode named)) {
            key = NormalizeKeyInt((int)named);
            return true;
        }
        return false;
    }

    private static int NormalizeKeyInt(int raw) => (int)Features.KeyLimiter.KeyLimiter.NormalizeKey((KeyCode)raw);

    private static string[] ReadStringArray(object value) {
        if(value is not IEnumerable enumerable || value is string) {
            return null;
        }
        List<string> result = [];
        foreach(object item in enumerable) {
            result.Add(item?.ToString() ?? "");
        }
        return result.Count > 0 ? [.. result] : null;
    }

    private static string[] ReadStringArrayJson(JToken token) {
        if(token is not JArray arr || arr.Count == 0) {
            return null;
        }
        string[] result = new string[arr.Count];
        for(int i = 0; i < arr.Count; i++) {
            result[i] = arr[i].Type == JTokenType.String ? arr[i].ToString() : "";
        }
        return result;
    }

    private static Color? ReadJsonColor(JToken token) {
        if(token is not JObject obj) {
            return null;
        }
        if(TryConvertFloat(JsonValue(obj, "r"), out float r)
            && TryConvertFloat(JsonValue(obj, "g"), out float g)
            && TryConvertFloat(JsonValue(obj, "b"), out float b)) {
            float a = TryConvertFloat(JsonValue(obj, "a"), out float aa) ? aa : 1f;
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), Mathf.Clamp01(a));
        }
        return null;
    }

    private static object JsonValue(JObject obj, string name) =>
        obj != null && obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken t) ? t : null;

    private static bool[] ReadBoolArray(object value) {
        if(value is not IEnumerable enumerable || value is string) {
            return [];
        }
        List<bool> values = [];
        foreach(object item in enumerable) {
            if(TryConvertBool(item, out bool b)) {
                values.Add(b);
            }
        }
        return [.. values];
    }

    // ===== ChatterBlocker key reading (allowed + async/VK) =====

    private static int[] ReadChatterBlockerProfileKeys(object profile) {
        List<int> result = [];
        AddChatterBlockerKeys(result, ReadKeyCodesFromMember(profile, "allowedKeys"));
        AddChatterBlockerVkKeys(result, GetMemberValue(profile, "allowedAsyncKeys"));
        return [.. result];
    }

    private static int[] ReadChatterBlockerProfileKeys(XElement profile) {
        List<int> result = [];
        AddChatterBlockerKeys(result, ReadKeyCodesFromXml(profile, "allowedKeys"));
        if(profile != null && FindFirstDescendant(profile, "allowedAsyncKeys") is XElement asyncList) {
            foreach(XElement item in asyncList.Elements()) {
                if(int.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vk)) {
                    AddVk(result, vk);
                }
            }
        }
        return [.. result];
    }

    private static void AddChatterBlockerKeys(List<int> result, int[] keys) {
        foreach(int raw in keys) {
            int key = raw;
            if(key == (int)KeyCode.None || Features.KeyLimiter.KeyLimiter.IsMouseKey((KeyCode)key) || result.Contains(key)) {
                continue;
            }
            result.Add(key);
        }
    }

    private static void AddChatterBlockerVkKeys(List<int> result, object value) {
        if(value is not IEnumerable enumerable || value is string) {
            return;
        }
        foreach(object item in enumerable) {
            if(TryConvertInt(item, out int vk)) {
                AddVk(result, vk);
            }
        }
    }

    private static void AddVk(List<int> result, int vk) {
        if(vk is < ushort.MinValue or > ushort.MaxValue) {
            return;
        }
        // Route the Windows VK through the legacy-async path so it lands on the
        // matching Unity KeyCode.
        int key = (int)Features.KeyLimiter.KeyLimiter.NormalizeNumericKey(LegacyAsyncKeyOffset + vk);
        if(key == (int)KeyCode.None || Features.KeyLimiter.KeyLimiter.IsMouseKey((KeyCode)key) || result.Contains(key)) {
            return;
        }
        result.Add(key);
    }

    // ===== misc source helpers =====

    private static List<object> GetAdofaiTweaksRuntimeSettings(SettingsImportOption option) {
        List<object> settings = [];
        object runners = GetStaticMember(FindType(option, "AdofaiTweaks.AdofaiTweaks"), "tweakRunners");
        if(runners is not IEnumerable enumerable) {
            return settings;
        }
        foreach(object runner in enumerable) {
            object value = GetMemberValue(runner, "Settings");
            if(value != null) {
                settings.Add(value);
            }
        }
        return settings;
    }

    private static object GetActiveIndexedProfile(object settings, string listMember, string indexMember) {
        if(GetMemberValue(settings, listMember) is not IEnumerable enumerable) {
            return null;
        }
        int index = TryGetInt(settings, indexMember, out int i) ? i : 0;
        int n = 0;
        object first = null;
        foreach(object item in enumerable) {
            first ??= item;
            if(n == index) {
                return item;
            }
            n++;
        }
        return first;
    }

    private static object FindSelectedProfile(object profiles) {
        if(profiles is not IEnumerable enumerable) {
            return null;
        }
        object first = null;
        foreach(object profile in enumerable) {
            first ??= profile;
            if(TryGetBool(profile, "isSelected", out bool selected) && selected) {
                return profile;
            }
        }
        return first;
    }

    private static int[] ReadAdofaiKeyViewerXmlKeys(XDocument doc) {
        TryReadXmlInt(doc, "ProfileIndex", out int profileIndex);
        XElement profiles = FindFirstDescendant(doc, "Profiles");
        if(profiles == null) {
            return [];
        }
        List<XElement> list = profiles.Elements().ToList();
        if(list.Count == 0) {
            return [];
        }
        if(profileIndex < 0 || profileIndex >= list.Count) {
            profileIndex = 0;
        }
        return ReadKeyCodesFromXml(list[profileIndex], "ActiveKeys");
    }

    // ===== XML / file helpers =====

    private static XDocument LoadXml(SettingsImportOption option, string fileName) {
        if(string.IsNullOrEmpty(option.Directory)) {
            return null;
        }
        string path = Path.Combine(option.Directory, fileName);
        if(!File.Exists(path)) {
            return null;
        }
        try { return XDocument.Load(path); } catch { return null; }
    }

    private static int[] ReadKeyCodesFromXml(XElement parent, string listName) {
        if(parent == null) {
            return [];
        }
        XElement list = FindFirstDescendant(parent, listName);
        if(list == null) {
            return [];
        }
        List<int> result = [];
        foreach(XElement item in list.Elements()) {
            if(TryConvertKeyCode(item.Value, out int key) && !result.Contains(key)) {
                result.Add(key);
            }
        }
        return [.. result];
    }

    private static bool[] ReadXmlBoolArray(XDocument doc, string arrayName) {
        XElement parent = FindFirstDescendant(doc, arrayName);
        if(parent == null) {
            return [];
        }
        List<bool> values = [];
        foreach(XElement item in parent.Elements()) {
            if(TryParseBool(item.Value, out bool b)) {
                values.Add(b);
            }
        }
        return [.. values];
    }

    private static bool TryReadXmlBool(XContainer root, string name, out bool value) {
        value = false;
        XElement element = FindFirstDescendant(root, name);
        return element != null && TryParseBool(element.Value, out value);
    }

    private static bool TryReadXmlInt(XContainer root, string name, out int value) {
        value = 0;
        XElement element = FindFirstDescendant(root, name);
        return element != null && int.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadXmlKeyCode(XContainer root, string name, out int value) {
        value = 0;
        XElement element = FindFirstDescendant(root, name);
        if(element == null) {
            return false;
        }
        if(int.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) {
            return true;
        }
        try { value = (int)(KeyCode)Enum.Parse(typeof(KeyCode), element.Value, true); return true; } catch { return false; }
    }

    private static bool TryParseBool(string text, out bool value) {
        value = false;
        if(bool.TryParse(text, out value)) {
            return true;
        }
        if(int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) {
            value = i != 0;
            return true;
        }
        return false;
    }

    private static XElement FindFirstDescendant(XContainer root, string name) {
        if(root == null || string.IsNullOrEmpty(name)) {
            return null;
        }
        return root.Descendants().FirstOrDefault(e => e.Name.LocalName == name);
    }

    private static XElement FindSelectedProfileElement(XDocument doc, string profileName) {
        if(doc == null) {
            return null;
        }
        XElement first = null;
        foreach(XElement profile in doc.Descendants().Where(e => e.Name.LocalName == profileName)) {
            first ??= profile;
            if(TryReadXmlBool(profile, "isSelected", out bool selected) && selected) {
                return profile;
            }
        }
        return first;
    }

    private static IEnumerable<string> JkvConfigPaths(SettingsImportOption option) {
        string dir = option.Directory;
        if(string.IsNullOrEmpty(dir)) {
            yield break;
        }
        string parent = Path.GetDirectoryName(dir);
        yield return Path.Combine(dir, "config", "settings.json");
        yield return Path.Combine(dir, "settings.json");
        if(!string.IsNullOrEmpty(parent)) {
            yield return Path.Combine(parent, "config", "settings.json");
            yield return Path.Combine(parent, "JipperKeyViewer", "config", "settings.json");
        }
    }

    private static string ReadFirstText(IEnumerable<string> paths) {
        foreach(string path in paths) {
            try {
                if(!string.IsNullOrEmpty(path) && File.Exists(path)) {
                    return File.ReadAllText(path);
                }
            } catch { }
        }
        return null;
    }

    private static string ResolveDirectory(string path) {
        if(string.IsNullOrEmpty(path)) {
            return null;
        }
        try {
            return File.Exists(path)
                ? Path.GetDirectoryName(path)
                : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        } catch {
            return path;
        }
    }

    private static string NormalizeModToken(string text) {
        if(string.IsNullOrEmpty(text)) {
            return "";
        }
        StringBuilder sb = new(text.Length);
        foreach(char ch in text) {
            char c = char.ToLowerInvariant(ch);
            if(char.IsLetterOrDigit(c)) {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string StripRichText(string text) {
        if(string.IsNullOrEmpty(text)) {
            return "";
        }
        StringBuilder sb = new(text.Length);
        bool inTag = false;
        foreach(char c in text) {
            if(c == '<') { inTag = true; continue; }
            if(c == '>') { inTag = false; continue; }
            if(!inTag) { sb.Append(c); }
        }
        return sb.ToString().Trim();
    }
}
