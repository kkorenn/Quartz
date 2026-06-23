using System.Text.RegularExpressions;
using HarmonyLib;
using Quartz.Async;
using Quartz.Core;
using Quartz.Features.Status;
using Quartz.Resource;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Quartz.Features.GameOverlayFont;

// Marker for mod-created text that already lives in the game world (e.g. the
// editor floor readout) and manages its own font. TrackScene skips any Text/TMP
// carrying it, so the twin/override sweep doesn't capture or re-size it.
public sealed class GameFontExclude : MonoBehaviour { }

// Applies the mod's selected font to A Dance of Fire and Ice's OWN text — the
// menus, level titles and in-game HUD — rather than only the mod's UI (which
// FontManager.ApplyToAll already covers).
//
// Most of that text is legacy UnityEngine.UI.Text, which can't render a
// runtime-loaded .ttf, and which the game's own scripts hold references to (for
// fading, sizing and show/hide). Rather than convert or re-font those — which
// breaks the game's control of them — each legacy label is left completely
// intact but made transparent, and a TextMeshProUGUI "twin" is laid over it
// that copies its text, colour (so fades follow), font size and visibility every
// frame, drawn in the mod's TMP font. Text that is already TMP just has its font
// swapped directly.
//
// GameFontMirror (a MonoBehaviour on the mod root) drives the per-frame copy.
public static class GameOverlayFont {
    private sealed class Capture {
        public TMP_Text Tmp;
        public TMP_FontAsset Original;
        public float OriginalSize;
        public bool OriginalAutoSize;
        public bool OriginalWrap;
    }

    // Native-TMP game labels we re-fonted, kept so the font can be put back.
    private static readonly Dictionary<int, Capture> tmpCaptures = [];
    private static bool hooked;

    // Point size relative to the game's for a wrapping TMP paragraph (e.g. the
    // update log) — near the game's own size so it fills its board, trimmed a
    // little because the mod font's lines are taller.
    private const float ParagraphScale = 0.9f;

    public static void Initialize() {
        if(hooked) {
            return;
        }
        hooked = true;
        SceneManager.sceneLoaded += (_, _) => {
            // Sweep hard for a short window right after the load so freshly-spawned
            // game labels get their twin the same frame they appear, instead of
            // flashing the game font until the next idle sweep.
            GameFontMirror.ArmBurst();
            MainThread.Enqueue(Refresh);
        };
    }

    private static bool Active =>
        MainCore.IsModEnabled && MainCore.Conf.ApplyFontToGameOverlay && FontManager.GameOverlayFontAsset != null;

    public static void Refresh() {
        if(!Active) {
            Restore();
            return;
        }
        TrackScene();
        // Enabling the feature (or a fresh scene) should pick up text instantly,
        // so burst-sweep for a moment rather than waiting on the idle timer.
        GameFontMirror.ArmBurst();
    }

    // Sweeps the live scene: twins any new legacy Text, swaps the font on any new
    // TMP. Re-run periodically by the mirror so text shown after a scene loads
    // (pause menu, popups) is picked up too. Idempotent.
    internal static void TrackScene() {
        if(!Active) {
            return;
        }

        GameFontMirror mirror = GameFontMirror.Ensure();
        if(mirror == null) {
            return;
        }

        GameObject root = MainCore.Root;
        foreach(Text text in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None)) {
            if(!IsModUi(text, root) && text.GetComponent<GameFontExclude>() == null) {
                mirror.Track(text);
            }
        }
        foreach(TMP_Text tmp in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)) {
            if(tmp == null) {
                continue;
            }
            // A label the game/another mod Instantiated carried a copy of our twin
            // as a child (e.g. TUFPlay cloning the "Calibration" object). That copy
            // is an unmanaged ghost rendering stale mod-font text — destroy it
            // rather than re-fonting it. The clone's own real twin is made when its
            // source is Tracked.
            if(GameFontMirror.IsOrphanTwin(tmp)) {
                UnityEngine.Object.Destroy(tmp.gameObject);
                continue;
            }
            if(!IsModUi(tmp, root) && !GameFontMirror.IsTwin(tmp) && tmp.GetComponent<GameFontExclude>() == null) {
                OverrideTmp(tmp);
            }
        }
    }

    public static void Restore() {
        GameFontMirror.DisposeInstance();

        foreach(Capture cap in tmpCaptures.Values) {
            if(cap.Tmp == null) {
                continue;
            }
            if(cap.Tmp.font != cap.Original) {
                cap.Tmp.font = cap.Original;
            }
            cap.Tmp.enableAutoSizing = cap.OriginalAutoSize;
            cap.Tmp.enableWordWrapping = cap.OriginalWrap;
            cap.Tmp.fontSize = cap.OriginalSize;
        }
        tmpCaptures.Clear();
    }

    internal static bool IsModUi(Graphic graphic, GameObject root) =>
        graphic == null || (root != null && graphic.transform.IsChildOf(root.transform));

    private static void OverrideTmp(TMP_Text tmp) {
        TMP_FontAsset want = FontManager.GameOverlayFontAsset;
        if(tmp == null || want == null) {
            return;
        }

        // Hit text is short-lived and spawned per-hit; don't capture it (would
        // leak) — just swap its font here; its size is HitTextSizePatch's job.
        bool isHitText = tmp.name.Contains("HitText");
        int id = tmp.GetInstanceID();

        if(!isHitText && !tmpCaptures.ContainsKey(id)) {
            SizeAndCapture(tmp, want, id);
        }

        if(tmp.font != want) {
            tmp.font = want;
            tmp.fontSharedMaterial = want.material;
        }
    }

    // Re-fonts a game TMP label and sizes it to match the game in the mod font:
    //   * the game's own auto-sized labels are left to size themselves;
    //   * multi-line paragraphs (e.g. the update log) get a fixed readable scale;
    //   * single-line labels keep the game's size but shrink ONLY when the wider
    //     mod font overflows the box horizontally (so wide rows like the editor
    //     menu stay full size while narrow ones like the pause menu shrink to fit).
    private static void SizeAndCapture(TMP_Text tmp, TMP_FontAsset want, int id) {
        float gameSize = tmp.fontSize;

        // The game auto-sizes some labels to fill their box (e.g. the update log
        // filling its board). Leave that on — just the font changes — so they
        // keep filling; only fixed-size labels need our help.
        if(tmp.enableAutoSizing) {
            tmpCaptures[id] = new Capture {
                Tmp = tmp,
                Original = tmp.font,
                OriginalSize = gameSize,
                OriginalAutoSize = true,
                OriginalWrap = tmp.enableWordWrapping,
            };
            return;
        }

        bool paragraph = AllowsWrap(tmp.rectTransform, gameSize);

        // Single-line width-fit needs a laid-out box; defer to a later sweep if
        // the rect isn't measured yet so the fit isn't computed against width 0.
        if(!paragraph && tmp.rectTransform.rect.width <= 0f) {
            return;
        }

        tmpCaptures[id] = new Capture {
            Tmp = tmp,
            Original = tmp.font,
            OriginalSize = gameSize,
            OriginalAutoSize = false,
            OriginalWrap = tmp.enableWordWrapping,
        };

        tmp.font = want;
        tmp.fontSharedMaterial = want.material;
        ApplySize(tmp, gameSize);
    }

    // Sizes a fixed-size game TMP label (mod font already set): paragraphs get a
    // readable scale, single-line labels keep the game's size but shrink when the
    // wider mod font overflows the box horizontally. Re-runnable on font change.
    private static void ApplySize(TMP_Text tmp, float gameSize) {
        if(AllowsWrap(tmp.rectTransform, gameSize)) {
            tmp.enableWordWrapping = true;
            tmp.fontSize = gameSize * ParagraphScale;
        } else {
            tmp.enableWordWrapping = false;
            tmp.fontSize = gameSize;
            float boxW = tmp.rectTransform.rect.width;
            float wantW = tmp.GetPreferredValues(tmp.text).x;
            if(boxW > 0f && wantW > boxW) {
                tmp.fontSize = gameSize * (boxW / wantW) * 0.98f;
            }
        }
    }

    // Re-applies the current font to every already-overridden game TMP and
    // re-fits it, so changing the font in the mod refreshes the in-game overlay.
    // (Legacy twins follow the font on their own each frame.)
    public static void ApplyFontChange() {
        if(!Active) {
            Restore();
            return;
        }

        TMP_FontAsset want = FontManager.GameOverlayFontAsset;
        foreach(Capture cap in tmpCaptures.Values) {
            if(cap.Tmp == null) {
                continue;
            }
            cap.Tmp.font = want;
            cap.Tmp.fontSharedMaterial = want.material;
            if(!cap.OriginalAutoSize) {
                ApplySize(cap.Tmp, cap.OriginalSize);
            }
        }

        // Pick up anything new too.
        TrackScene();
    }

    // True only when the text's box is tall enough for more than one line, so
    // genuine multi-line paragraphs keep wrapping while single-line labels
    // overflow rather than break under the wider mod font.
    internal static bool AllowsWrap(RectTransform rect, float fontSize) =>
        rect != null && fontSize > 0f && rect.rect.height > fontSize * 1.8f;

    [HarmonyPatch(typeof(RDString), "SetLocalizedFont", new[] { typeof(TMP_Text) })]
    private static class TmpFontPatch {
        private static void Postfix(TMP_Text text) {
            if(!Active || IsModUi(text, MainCore.Root) || GameFontMirror.IsTwin(text)) {
                return;
            }
            try {
                OverrideTmp(text);
            } catch {
            }
        }
    }

    // Legacy UnityEngine.UI.Text is fonted through the same RDString call, but it
    // can't render the runtime mod .ttf, so it's mirrored onto a TMP twin. Hooking
    // the apply means the twin is created — and the game font hidden — the very
    // instant the game fonts the label (panel open / language change), instead of
    // waiting for the next periodic sweep. That closes the window where the game
    // font shows for a few frames before the swap.
    [HarmonyPatch(typeof(RDString), "SetLocalizedFont", new[] { typeof(Text) })]
    private static class TextFontPatch {
        private static void Postfix(Text text) {
            if(!Active || IsModUi(text, MainCore.Root)) {
                return;
            }
            try {
                GameFontMirror.Ensure()?.Track(text);
            } catch {
            }
        }
    }

    // NOTE: an OnEnable hook (Text/TextMeshProUGUI) to twin labels the instant they
    // show was tried to kill the residual flash, but reverted — it twinned
    // transient/animated labels (e.g. an external mod's "Calibration" splash) that
    // fade via CanvasRenderer alpha (CrossFadeAlpha), which the twin (mirroring
    // Text.color, not the canvasRenderer alpha) doesn't follow, leaving a ghost
    // duplicate in the mod font. The SetLocalizedFont patches + burst + idle sweep
    // remain the discovery path.

    // The in-game judgement hit text ("Perfect", "Early"…) renders larger in the
    // mod font. Init sets its fontSize (after the font swap) and animates only
    // transform scale, so trimming fontSize here sticks without fighting the
    // punch animation.
    private const float HitTextScale = 0.45f;

    [HarmonyPatch(typeof(scrHitTextMesh), "Init")]
    private static class HitTextSizePatch {
        private static void Postfix(scrHitTextMesh __instance) {
            if(!Active) {
                return;
            }
            try {
                if(__instance.text != null) {
                    __instance.text.fontSize *= HitTextScale;
                }
            } catch {
            }
        }
    }
}

// Mirrors each tracked legacy UI.Text onto an overlaid TextMeshProUGUI twin in
// the mod font, hiding the original's glyphs (CanvasRenderer alpha, which is
// independent of the Text's own colour/enabled state, so the game keeps full
// control). Lives on the mod root so it survives scene loads.
public sealed class GameFontMirror : MonoBehaviour {
    private sealed class Pair {
        public Text Source;
        public TextMeshProUGUI Twin;
        // Last raw source string we sanitized into the twin. Cached so the regex
        // sweep + TMP re-tessellate only run when the title text actually changes.
        public string LastRaw;
        // True for the settings-menu rows (PauseSettingButton label/value). Their
        // best-fit boxes are tall, so the normal auto-fill twin balloons them; they
        // get a fixed small size instead. Resolved once at Track (parent walk).
        public bool IsSettingRow;
    }

    internal const string TwinName = "QuartzFontTwin";

    // An orphan twin is a QuartzFontTwin GameObject that the game/another mod copied
    // by Instantiating an already-twinned label (e.g. TUFPlay clones the game's
    // "Calibration" object, which by then has our twin as a child). The copy keeps
    // the TwinName but a fresh instance id never registered in twinIds, so it is
    // never synced or hidden and renders stale baked text in the mod font. Detect
    // it so the sweep can destroy it.
    public static bool IsOrphanTwin(Component c) =>
        c != null && c.gameObject.name == TwinName && !twinIds.Contains(c.GetInstanceID());
    // Twin point size relative to the original's nominal point size. Shared so
    // the TMP path scales game text to match.
    internal const float SizeScale = 0.5f;

    // Drop shadow for the re-fonted game text. Uses TMP's built-in material
    // underlay (a GPU effect in the same draw) rather than the layered
    // TMPTextShadow the mod HUD uses: the underlay needs no extra GameObjects and
    // no per-frame re-mesh, and it fades with the glyph alpha for free — so it
    // stays off the gameplay hot path even across dozens of mirrored labels. The
    // offsets are in TMP underlay units (~fraction of an em), so they scale with
    // each label's own size automatically. Applied to a per-twin material
    // INSTANCE (twin.fontMaterial) so it never leaks onto the shared font
    // material / the mod's own UI.
    private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.75f);
    private const float ShadowOffsetX = 0.5f;
    private const float ShadowOffsetY = -0.5f;
    private const float ShadowSoftness = 0.15f;
    private const float ShadowDilate = 0.1f;

    private static void ApplyShadow(TMP_Text twin) {
        Material mat = twin.fontMaterial; // instances the material on first access
        if(mat == null) {
            return;
        }
        mat.EnableKeyword("UNDERLAY_ON");
        mat.SetColor("_UnderlayColor", ShadowColor);
        mat.SetFloat("_UnderlayOffsetX", ShadowOffsetX);
        mat.SetFloat("_UnderlayOffsetY", ShadowOffsetY);
        mat.SetFloat("_UnderlaySoftness", ShadowSoftness);
        mat.SetFloat("_UnderlayDilate", ShadowDilate);

        // Enabling the underlay by poking the material directly does NOT tell the
        // TMP component to grow each glyph's quad padding — it stays sized for no
        // shadow, so the underlay (drawn in the same mesh, offset down-right) is
        // culled at the quad edge into a hard square box behind the text. TMP only
        // recomputes that padding when it notices the material change, which a raw
        // SetFloat/EnableKeyword bypasses. Force the recompute so the quads get
        // room and the shadow renders soft. (Flags the mesh dirty — it rebuilds on
        // the next canvas pass, alongside the text Apply sets right after.)
        twin.UpdateMeshPadding();
    }

    private static GameFontMirror instance;
    private static readonly HashSet<int> twinIds = [];
    // One-shot diagnostic: settings-row source ids already logged, so the size
    // dump prints once per label, not every frame. Temporary while tuning.
    private static readonly HashSet<int> diagLogged = [];

    private readonly List<Pair> pairs = [];
    private readonly HashSet<int> trackedSources = [];

    public static GameFontMirror Ensure() {
        if(instance == null && MainCore.Root != null) {
            instance = MainCore.Root.AddComponent<GameFontMirror>();
        }
        return instance;
    }

    public static bool IsTwin(Component c) => c != null && twinIds.Contains(c.GetInstanceID());

    public static void DisposeInstance() {
        if(instance != null) {
            instance.Clear();
            Destroy(instance);
            instance = null;
        }
        twinIds.Clear();
    }

    public void Track(Text source) {
        if(source == null || !trackedSources.Add(source.GetInstanceID())) {
            return;
        }

        var twinGo = new GameObject(TwinName);
        twinGo.transform.SetParent(source.transform, false);

        var rt = twinGo.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        var twin = twinGo.AddComponent<TextMeshProUGUI>();
        twin.font = FontManager.GameOverlayFontAsset;
        twin.raycastTarget = false;
        ApplyShadow(twin);
        twinIds.Add(twin.GetInstanceID());

        var pair = new Pair {
            Source = source,
            Twin = twin,
            IsSettingRow = source.GetComponentInParent<PauseSettingButton>() != null,
        };
        pairs.Add(pair);
        Apply(pair);
    }

    // Frames remaining of the aggressive post-load sweep (see ArmBurst). Starts
    // armed so a freshly-created mirror catches the current scene immediately.
    private int burstFrames = BurstFrames;
    private int rescanCountdown;
    private int lastSyncFrame = -1;

    // ~0.75s at 60fps: long enough to catch a HUD that animates in over the first
    // frames of a level, short enough to stay off the steady gameplay hot path.
    private const int BurstFrames = 45;
    private const int BurstScanInterval = 5;

    // Re-arm the post-load burst on the live mirror (scene load / feature enable).
    public static void ArmBurst() {
        if(instance != null) {
            instance.burstFrames = BurstFrames;
        }
    }

    private void LateUpdate() {
        // Right after a load, sweep every frame so new labels are twinned the
        // moment they appear (no game-font flash). Once that window closes, idle:
        // during an active run nothing new spawns and the HUD is static, so the
        // two whole-scene FindObjectsOfType scans would only add a recurring
        // main-thread + GC hitch to the gameplay hot path. So rescan rarely while
        // dancing (InGame), gently in menus/pause where popups appear.
        if(burstFrames > 0) {
            burstFrames--;
            if(burstFrames % BurstScanInterval == 0) {
                GameOverlayFont.TrackScene();
            }
        } else if(--rescanCountdown <= 0) {
            rescanCountdown = GameStats.InGame ? 300 : 30;
            GameOverlayFont.TrackScene();
        }
    }

    // The per-frame twin sync runs on the canvas pre-render, NOT in LateUpdate.
    // willRenderCanvases is the last main-thread callback before the UI is drawn
    // (after every Update/LateUpdate and any coroutine), and SetAlpha(0) on the
    // source's CanvasRenderer there takes effect this frame with no rebuild — so a
    // label the game shows or re-enables late in the frame (e.g. a panel re-open,
    // which doesn't re-run SetLocalizedFont) is hidden behind its twin before it
    // can draw even one frame of the game font. Same work as before, just moved to
    // the latest possible point — no extra cost.
    private void OnEnable() {
        Canvas.willRenderCanvases += SyncPairs;
    }

    private void OnDisable() {
        Canvas.willRenderCanvases -= SyncPairs;
    }

    private void SyncPairs() {
        // Unity may raise willRenderCanvases multiple times in one frame. Mirrored
        // values cannot change between those callbacks, so sync once per frame.
        int frame = Time.frameCount;
        if(lastSyncFrame == frame) {
            return;
        }
        lastSyncFrame = frame;

        for(int i = pairs.Count - 1; i >= 0; i--) {
            Pair pair = pairs[i];
            if(pair.Source == null || pair.Twin == null) {
                if(pair.Twin != null) {
                    twinIds.Remove(pair.Twin.GetInstanceID());
                    Destroy(pair.Twin.gameObject);
                }
                if(pair.Source != null) {
                    trackedSources.Remove(pair.Source.GetInstanceID());
                }
                pairs.RemoveAt(i);
                continue;
            }
            Apply(pair);
        }
    }

    private static void Apply(Pair pair) {
        Text source = pair.Source;
        TextMeshProUGUI twin = pair.Twin;

        // Follow the in-game overlay font live, so changing it (or the overlay
        // font it may follow) in the mod refreshes here.
        TMP_FontAsset want = FontManager.GameOverlayFontAsset;
        if(twin.font != want) {
            twin.font = want;
            // Re-fonting resets the twin to the new font's SHARED material, dropping
            // our underlay instance — re-instance and re-arm the shadow.
            ApplyShadow(twin);
        }
        // TMP's text setter does NOT early-out on an equal string — it re-parses
        // and re-tessellates the whole mesh. Most mirrored labels never change
        // their text, so guard on the RAW source string (pre-sanitize) so that
        // per-frame re-mesh — and the regex sweep below — stay off the gameplay
        // hot path. (The other TMP setters below already early-out internally on
        // unchanged values, so they stay unconditional.)
        if(pair.LastRaw != source.text) {
            pair.LastRaw = source.text;
            twin.text = SanitizeMarkup(source.text);
        }
        twin.color = source.color;
        twin.fontStyle = MapStyle(source.fontStyle);
        twin.alignment = MapAnchor(source.alignment);
        twin.richText = source.supportRichText;
        // Wrapping mirrors the source: best-fit is orthogonal to wrap (the game
        // best-fits BOTH single-line titles AND wrapped paragraphs like the
        // difficulty blurb), so the line count must come from the source's own
        // wrap setting, not from whether it best-fits.
        twin.enableWordWrapping = source.horizontalOverflow == HorizontalWrapMode.Wrap
            && GameOverlayFont.AllowsWrap(source.rectTransform, source.fontSize);
        // Always Overflow, never Truncate. Truncate CULLS any glyph geometry that
        // extends past the rect — and the drop-shadow underlay is part of that same
        // mesh, so a down-right shadow gets sliced off at the rect edge, leaving the
        // text sitting in a hard shadow box. The twin's size already matches the
        // game's, so the text itself never needs clipping; Overflow just lets the
        // shadow bleed out as it should.
        twin.overflowMode = TextOverflowModes.Overflow;

        if(pair.IsSettingRow) {
            // Settings rows (PauseSettingButton label + value) are best-fit UI.Text
            // in a tall row box. Letting the twin auto-FILL that box balloons it (the
            // mod font fills the height at a far bigger point size than the game's
            // width-limited best-fit → huge menu). But these labels' nominal fontSize
            // is ~0 and the resolved fontSizeUsedForBestFit reads 0 on the hidden
            // source, so anchoring on either gave a 0pt, invisible twin.
            //
            // Anchor on resizeTextMaxSize instead — the serialized best-fit ceiling,
            // always non-zero — and auto-size within [1, max*SizeScale], single line.
            // The cap keeps it small (can't balloon); auto-size + min 1 keeps it
            // visible and shrinks a long row to fit width instead of clipping.
            float anchor = source.resizeTextMaxSize > 0 ? source.resizeTextMaxSize
                : source.fontSize > 0 ? source.fontSize
                : 24f;
            twin.enableAutoSizing = true;
            twin.enableWordWrapping = false;
            twin.fontSizeMin = 1f;
            twin.fontSizeMax = Mathf.Max(1f, anchor * SizeScale);

            int sid = source.GetInstanceID();
            if(diagLogged.Add(sid)) {
                RectTransform sr = source.rectTransform;
                MainCore.Log.Msg(
                    $"[GameOverlayFont] setting '{source.name}' nominal={source.fontSize} " +
                    $"min={source.resizeTextMinSize} max={source.resizeTextMaxSize} " +
                    $"fit={source.cachedTextGenerator.fontSizeUsedForBestFit} " +
                    $"rect={sr.rect.width:F0}x{sr.rect.height:F0} cap={twin.fontSizeMax:F1}");
            }
        } else if(source.resizeTextForBestFit) {
            // The game best-fits this label between its min/max to FILL its box
            // (one line or wrapped). Auto-sizing the twin to the SAME box makes it
            // fill identically — and "fill the box" is metric-independent, so it
            // matches the game's rendered size without the fixed-label SizeScale
            // guess. (That guess halved the size and left short words like the
            // editor's "Strict" badge tiny, and a fixed point size clipped wide
            // titles — auto-fit fixes both.) Cap at the game's OWN best-fit ceiling
            // so the twin never overshoots it; min 1 lets it shrink to dodge clips.
            float maxPx = source.resizeTextMaxSize > 0 ? source.resizeTextMaxSize : source.fontSize;
            twin.enableAutoSizing = true;
            twin.fontSizeMin = 1f;
            twin.fontSizeMax = Mathf.Max(1f, maxPx);
        } else {
            // Fixed-size game label: copy its size directly.
            twin.enableAutoSizing = false;
            twin.fontSize = source.fontSize * SizeScale;
        }
        twin.enabled = source.enabled;

        // Hide the original's pixels without touching its colour/enabled state,
        // so the game's own fade and show/hide logic keeps driving the twin.
        source.canvasRenderer.SetAlpha(0f);
    }

    private void Clear() {
        foreach(Pair pair in pairs) {
            if(pair.Twin != null) {
                Destroy(pair.Twin.gameObject);
            }
            if(pair.Source != null) {
                pair.Source.canvasRenderer.SetAlpha(1f);
            }
        }
        pairs.Clear();
        trackedSources.Clear();
        diagLogged.Clear();
    }

    // Game labels (notably the level title) can embed UnityEngine.UI.Text
    // rich-text that the level author wrote. TMP's rich-text parser is NOT the
    // same language: it honours <size> absolutely (fighting the twin's own
    // best-fit so the title balloons and wraps) and rejects malformed hex
    // colours like <color=#F4FA588> (7 digits) by printing the tag as literal
    // text instead of swallowing it the way UI.Text does. Repair the markup
    // before it reaches the twin: drop <size> (let the twin's sizing rule, which
    // mirrors UI.Text best-fit ignoring embedded size) and coerce hex colours to
    // a length TMP accepts, while leaving <color>/<b>/etc. intact.
    private static readonly Regex SizeTag =
        new(@"</?size(=[^>]*)?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ColorHex =
        new(@"<color=#([0-9a-fA-F]+)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string SanitizeMarkup(string s) {
        // No tags -> nothing to fix; skip the regex work (the common case).
        if(string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) {
            return s;
        }

        s = SizeTag.Replace(s, "");
        s = ColorHex.Replace(s, m => {
            string hex = m.Groups[1].Value;
            // TMP (this game's build) accepts #RGB, #RGBA, #RRGGBB and #RRGGBBAA,
            // so keep the largest VALID length instead of zero-padding short
            // forms: the old pad turned a hidden #RGBA like #FFF0 (alpha 0) into
            // an opaque #FFF000, so text the level author had made invisible via
            // a 00-alpha tag showed up. Mirrors SongTitle.NormalizeColorTags.
            int valid = hex.Length switch {
                >= 8 => 8,      // RRGGBBAA (drop any extra digits)
                6 or 7 => 6,    // RRGGBB
                4 or 5 => 4,    // RGBA short form (preserves the alpha nibble)
                3 => 3,         // RGB short form
                _ => 0,         // 1-2 digits: not a colour, drop the tag
            };
            return valid == 0 ? string.Empty
                : valid == hex.Length ? m.Value
                : "<color=#" + hex.Substring(0, valid) + ">";
        });
        return s;
    }

    private static FontStyles MapStyle(FontStyle style) => style switch {
        FontStyle.Bold => FontStyles.Bold,
        FontStyle.Italic => FontStyles.Italic,
        FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
        _ => FontStyles.Normal,
    };

    private static TextAlignmentOptions MapAnchor(TextAnchor anchor) => anchor switch {
        TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
        TextAnchor.UpperCenter => TextAlignmentOptions.Top,
        TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
        TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
        TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
        TextAnchor.MiddleRight => TextAlignmentOptions.Right,
        TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
        TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
        TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
        _ => TextAlignmentOptions.Center,
    };
}
