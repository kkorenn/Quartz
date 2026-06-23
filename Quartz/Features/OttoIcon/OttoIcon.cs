using HarmonyLib;
using Quartz.Core;
using Quartz.IO;
using Quartz.Resource;
using UnityEngine;
using UnityEngine.UI;

namespace Quartz.Features.OttoIcon;

// Replaces the editor's Otto (auto-play) icon with the mod's own sprite,
// recolored and repositioned — ported from the original KorenResourcePack's
// ResourceChanger ChangeOttoIcon. The sprite was v1's bundled Auto.png; v2
// embeds it directly instead of shipping an asset bundle.
//
// The editor reasserts the icon's sprite/color from OttoUpdate, OttoBlink and
// its sprite-state array, so the swap is re-applied from postfixes on those
// paths. Apply is cheap when nothing changed: the last applied state is
// remembered and matched before touching anything. Everything original is
// remembered so Restore can hand the icon back.
public static class OttoIcon {
    public static SettingsFile<OttoIconSettings> ConfMgr { get; private set; }
    public static OttoIconSettings Conf => ConfMgr?.Data;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<OttoIconSettings>(
            Path.Combine(MainCore.Paths.RootPath, "OttoIcon.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    private static bool ShouldChange {
        get {
            EnsureConf();
            return MainCore.IsModEnabled && Conf.Enabled;
        }
    }

    private const float Scale = 0.85f;
    // The idle (auto off) icon is the active color dimmed, same factor v1 used.
    private const float IdleDimFactor = 0.343f;

    // Vanilla's highBPM check (scnEditor.highBPM is private): tints Otto red
    // while the level's top BPM is 300+.
    private static bool IsHighBpm => scnGame.instance != null && scnGame.instance.highestBPM >= 300f;

    private static Color ActiveColor =>
        Conf.UseHighBpmColor && IsHighBpm ? Conf.GetHighBpmColor() : Conf.GetColor();

    private static Color IdleColor {
        get {
            Color c = ActiveColor;
            return new Color(c.r * IdleDimFactor, c.g * IdleDimFactor, c.b * IdleDimFactor, c.a);
        }
    }

    // ===== original state, for Restore =====
    private static Sprite originalSprite;
    private static Sprite[] originalAutoSprites;
    private static Sprite[] trackedAutoSprites;
    private static SpriteState originalSpriteState;
    private static bool hasOriginalSpriteState;
    private static Button spriteStateButton;
    private static Image spriteStateImage;
    private static bool hasOriginalTransform;
    private static Vector2 originalAnchoredPosition;
    private static Vector3 originalLocalScale;
    private static Color originalColor;
    private static Image trackedTransformImage;
    // The embedded Otto sprite resolves once and never changes; memoize it so the
    // per-frame postfix doesn't box the Asset enum into SpriteManager's
    // object-keyed cache on every editor frame just to re-fetch the same instance.
    private static Sprite resolvedReplacement;

    // ===== last applied state, so the per-frame postfixes are no-ops =====
    private static bool applyStateValid;
    private static scnEditor cachedEditor;
    private static Image cachedImage;
    private static Sprite cachedReplacement;
    private static bool cachedAutoState;
    private static Color cachedTargetColor;
    private static Vector2 cachedPosition;
    private static Vector3 cachedScale;

    private static void InvalidateApplyState() {
        applyStateValid = false;
        cachedEditor = null;
        cachedImage = null;
        cachedReplacement = null;
    }

    public static void Refresh() {
        InvalidateApplyState();
        Apply();
    }

    internal static void Apply() {
        if(!ShouldChange) {
            return;
        }

        scnEditor editor = scnEditor.instance;
        if(editor == null) {
            return;
        }

        Image autoImage = editor.autoImage;
        if(autoImage == null) {
            return;
        }

        Sprite replacement = resolvedReplacement;
        if(replacement == null) {
            replacement = MainCore.Spr.Get(Asset.OttoAuto);
            if(replacement == null) {
                return;
            }
            resolvedReplacement = replacement;
        }

        bool autoState;
        try { autoState = RDC.auto; } catch { autoState = false; }
        Color targetColor = autoState ? ActiveColor : IdleColor;
        RectTransform rt = autoImage.rectTransform;
        Vector2 targetPosition = new(Conf.OffsetX, Conf.OffsetY);
        Vector3 targetScale = Vector3.one * Scale;

        if(!hasOriginalTransform || trackedTransformImage != autoImage) {
            originalAnchoredPosition = rt.anchoredPosition;
            originalLocalScale = rt.localScale;
            originalColor = autoImage.color;
            trackedTransformImage = autoImage;
            hasOriginalTransform = true;
        }

        if(ApplyStateMatches(editor, autoImage, replacement, autoState, targetColor, targetPosition, targetScale)) {
            return;
        }

        if(autoImage.sprite != replacement) {
            originalSprite = autoImage.sprite;
        }

        OverrideAutoSpriteArray(editor, replacement);
        if(autoImage.sprite != replacement) {
            autoImage.sprite = replacement;
        }
        OverrideAutoButtonSpriteState(autoImage, replacement);

        if(autoImage.color != targetColor) {
            autoImage.color = targetColor;
        }

        if(rt.anchoredPosition != targetPosition) {
            rt.anchoredPosition = targetPosition;
        }
        if(rt.localScale != targetScale) {
            rt.localScale = targetScale;
        }

        applyStateValid = true;
        cachedEditor = editor;
        cachedImage = autoImage;
        cachedReplacement = replacement;
        cachedAutoState = autoState;
        cachedTargetColor = targetColor;
        cachedPosition = targetPosition;
        cachedScale = targetScale;
    }

    private static bool ApplyStateMatches(
        scnEditor editor, Image autoImage, Sprite replacement,
        bool autoState, Color targetColor, Vector2 targetPosition, Vector3 targetScale
    ) {
        if(!applyStateValid) {
            return false;
        }
        // Live color/transform must be checked too: OttoUpdate reasserts
        // autoImage.color every frame, so a cache hit on intent alone would
        // skip the re-apply and let the game's color win.
        return cachedEditor == editor
            && cachedImage == autoImage
            && cachedReplacement == replacement
            && cachedAutoState == autoState
            && cachedTargetColor == targetColor
            && cachedPosition == targetPosition
            && cachedScale == targetScale
            && autoImage != null
            && autoImage.sprite == replacement
            && autoImage.color == targetColor
            && autoImage.rectTransform.anchoredPosition == targetPosition
            && autoImage.rectTransform.localScale == targetScale;
    }

    private static void OverrideAutoSpriteArray(scnEditor editor, Sprite replacement) {
        if(editor == null || editor.autoSprites == null || replacement == null) {
            return;
        }
        if(trackedAutoSprites != editor.autoSprites ||
            originalAutoSprites == null ||
            originalAutoSprites.Length != editor.autoSprites.Length) {
            trackedAutoSprites = editor.autoSprites;
            originalAutoSprites = (Sprite[])editor.autoSprites.Clone();
        }

        for(int i = 0; i < editor.autoSprites.Length; i++) {
            if(editor.autoSprites[i] != replacement) {
                editor.autoSprites[i] = replacement;
            }
        }
    }

    private static void OverrideAutoButtonSpriteState(Image autoImage, Sprite replacement) {
        if(autoImage == null || replacement == null) {
            return;
        }

        Button btn;
        if(spriteStateImage == autoImage && spriteStateButton != null) {
            btn = spriteStateButton;
        } else {
            btn = autoImage.GetComponent<Button>();
            if(btn == null) {
                btn = autoImage.GetComponentInParent<Button>();
            }
            if(btn != null) {
                spriteStateImage = autoImage;
            }
        }

        if(btn == null) {
            return;
        }

        if(!hasOriginalSpriteState || spriteStateButton != btn) {
            originalSpriteState = btn.spriteState;
            hasOriginalSpriteState = true;
            spriteStateButton = btn;
        }

        SpriteState state = btn.spriteState;
        if(state.highlightedSprite == replacement &&
            state.pressedSprite == replacement &&
            state.selectedSprite == replacement &&
            state.disabledSprite == replacement) {
            return;
        }

        state.highlightedSprite = replacement;
        state.pressedSprite = replacement;
        state.selectedSprite = replacement;
        state.disabledSprite = replacement;
        btn.spriteState = state;
    }

    public static void Restore() {
        InvalidateApplyState();
        try {
            scnEditor editor = scnEditor.instance;
            if(editor == null || editor.autoImage == null) {
                return;
            }

            if(originalSprite != null) {
                editor.autoImage.sprite = originalSprite;
            }

            if(originalAutoSprites != null &&
                editor.autoSprites != null &&
                trackedAutoSprites == editor.autoSprites &&
                editor.autoSprites.Length == originalAutoSprites.Length) {
                for(int i = 0; i < editor.autoSprites.Length; i++) {
                    editor.autoSprites[i] = originalAutoSprites[i];
                }
            }

            Button btn = editor.autoImage.GetComponent<Button>();
            if(btn == null) {
                btn = editor.autoImage.GetComponentInParent<Button>();
            }
            if(btn != null && hasOriginalSpriteState) {
                btn.spriteState = originalSpriteState;
            }

            if(hasOriginalTransform && trackedTransformImage == editor.autoImage) {
                RectTransform rt = editor.autoImage.rectTransform;
                if(rt != null) {
                    rt.anchoredPosition = originalAnchoredPosition;
                    rt.localScale = originalLocalScale;
                }
                editor.autoImage.color = originalColor;
            }

            hasOriginalSpriteState = false;
            spriteStateButton = null;
            spriteStateImage = null;
            hasOriginalTransform = false;
            trackedTransformImage = null;
        } catch {
        }
    }

    [HarmonyPatch(typeof(scnEditor), "OttoUpdate")]
    private static class OttoUpdatePatch {
        private static void Postfix() => Apply();
    }

    // The editor can swap the icon back outside OttoUpdate (mode switches,
    // sprite-state animation), so the swap also rides the editor's Update.
    // Apply() short-circuits when the cached state still matches.
    [HarmonyPatch(typeof(scnEditor), "Update")]
    private static class EditorUpdatePatch {
        private static void Postfix() => Apply();
    }

    [HarmonyPatch(typeof(scnEditor), "OttoBlink")]
    private static class OttoBlinkPatch {
        private static void Postfix() => Apply();
    }

    [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
    private static class ClearOnSceneChangePatch {
        private static void Postfix() => InvalidateApplyState();
    }
}
