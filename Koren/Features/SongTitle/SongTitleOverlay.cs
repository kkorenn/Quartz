using HarmonyLib;
using Koren.Core;
using Koren.Features.Panels;
using Koren.Features.Status;
using Koren.IO;
using Koren.Resource;
using Koren.UI;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

using TMPro;

namespace Koren.Features.SongTitle;

// Customizable in-game song-title overlay. When enabled it hides the game's own
// level-title HUD (the scrHUDText labels flagged isTitle) and draws a
// replacement built from the {artist}/{title} tags in Conf.Format, with its own
// font, size, color, drop shadow and drag-positioned placement. Mirrors the
// other HUD overlays (Combo/Judgement): gated by the master "Enable Overlays"
// switch and shown only in-game (plus while reorganizing).
public static class SongTitleOverlay {
    public static SettingsFile<SongTitleSettings> ConfMgr { get; private set; }
    public static SongTitleSettings Conf => ConfMgr.Data;

    private static GameObject canvasObj;
    private static RectTransform root;
    private static TextMeshProUGUI text;
    private static GameObject dragObj;
    private static Updater updater;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<SongTitleSettings>(
            Path.Combine(MainCore.Paths.RootPath, "SongTitle.json")
        );
        ConfMgr.Load();
    }

    // True when the feature should take over the title — gates both our overlay
    // and the patch that hides the game's own title. Safe before Initialize
    // (EnsureConf lazily loads), so the Harmony patch can call it.
    public static bool TakesOverTitle {
        get {
            if(!MainCore.IsModEnabled) {
                return false;
            }
            EnsureConf();
            return PanelsOverlay.IsEnabled && Conf.Enabled;
        }
    }

    public static void Initialize(GameObject rootObject) {
        if(canvasObj != null) {
            return;
        }

        EnsureConf();

        canvasObj = new GameObject("KorenSongTitleCanvas");
        canvasObj.transform.SetParent(rootObject.transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32756;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject titleObj = new("SongTitle");
        titleObj.transform.SetParent(canvasObj.transform, false);
        root = titleObj.AddComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 1f);
        root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);

        // The text lives on its own child of root — not on root itself — so the
        // drop-shadow that TMPTextShadow builds as a *sibling* of the text lands
        // inside root and therefore hides and positions with it. (With TMP on
        // root, that sibling would be parented to the Canvas, so it survived on
        // screen after the title hid on level exit, and mis-tracked the
        // point-anchored root.)
        GameObject labelObj = new("Label");
        labelObj.transform.SetParent(root, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 1f);
        labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);

        text = labelObj.AddComponent<TextMeshProUGUI>();
        text.font = FontManager.Current;
        text.alignment = TextAlignmentOptions.Top;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.text = "";

        GameObject drag = new("Drag");
        dragObj = drag;
        drag.transform.SetParent(root, false);
        RectTransform dragRect = drag.AddComponent<RectTransform>();
        dragRect.anchorMin = Vector2.zero;
        dragRect.anchorMax = Vector2.one;
        dragRect.offsetMin = Vector2.zero;
        dragRect.offsetMax = Vector2.zero;
        drag.AddComponent<EmptyGraphic>().raycastTarget = true;
        ReorganizeHandle handle = drag.AddComponent<ReorganizeHandle>();
        handle.Target = root;
        handle.GetName = () => MainCore.Tr.Get("SONG_TITLE", "Song Title");
        handle.OnMoved = Save;
        drag.SetActive(false);

        updater = canvasObj.AddComponent<Updater>();

        Apply();
    }

    // Re-applies appearance (position, scale, font, size, color, shadow). Called
    // on init and from the settings UI; the text content is driven per-frame by
    // the Updater.
    public static void Apply() {
        if(root == null) {
            return;
        }

        root.anchoredPosition = new Vector2(Conf.OffsetX, Conf.OffsetY);
        root.localScale = Vector3.one * Mathf.Max(0.01f, Conf.MasterSize);

        if(text != null) {
            text.font = FontManager.Current;
            text.fontSize = Mathf.Clamp(Conf.FontSize, 4f, 400f);
            text.color = Conf.GetColor();
            ApplyShadow();
        }
    }

    public static void ApplyShadow() {
        if(text == null) {
            return;
        }

        TMPTextShadow.Apply(
            text,
            Conf.ShadowEnabled,
            Conf.ShadowX,
            Conf.ShadowY,
            Conf.ShadowSoftness,
            Conf.GetShadowColor()
        );
    }

    public static void Save() => ConfMgr?.Save();

    public static void ResetPosition() {
        SongTitleSettings def = new();
        Conf.OffsetX = def.OffsetX;
        Conf.OffsetY = def.OffsetY;
        Apply();
        Save();
    }

    public static void Dispose() {
        if(canvasObj == null) {
            return;
        }

        ConfMgr?.Save();

        Object.Destroy(canvasObj);
        canvasObj = null;
        root = null;
        text = null;
        dragObj = null;
        updater = null;
    }

    // Builds the displayed string from the current level's artist/title and the
    // user's Format. Falls back to the game's combined title when the separate
    // metadata isn't available (built-in levels).
    internal static string BuildBody(bool isReorganizing) {
        string artist = GameStats.SongArtist;
        string title = GameStats.SongTitle;

        if(string.IsNullOrEmpty(artist) && string.IsNullOrEmpty(title)) {
            if(isReorganizing) {
                artist = "Artist";
                title = "Title";
            } else {
                return GameStats.SongTitleRaw;
            }
        }

        string fmt = string.IsNullOrEmpty(Conf.Format) ? "{artist} - {title}" : Conf.Format;
        return fmt.Replace("{artist}", artist).Replace("{title}", title);
    }

    private sealed class Updater : MonoBehaviour {
        private string lastBody;

        private void Update() {
            if(root == null || text == null) {
                return;
            }

            bool isReorganizing = UICore.IsReorganizing;
            bool show = (PanelsOverlay.IsEnabled && Conf.Enabled && GameStats.InGame) || isReorganizing;
            if(root.gameObject.activeSelf != show) {
                root.gameObject.SetActive(show);
            }

            if(dragObj != null && dragObj.activeSelf != isReorganizing) {
                dragObj.SetActive(isReorganizing);
            }

            if(!show) {
                return;
            }

            // Position only changes while dragging in Reorganize mode.
            if(isReorganizing) {
                Conf.OffsetX = root.anchoredPosition.x;
                Conf.OffsetY = root.anchoredPosition.y;
            }

            TMP_FontAsset font = FontManager.Current;
            if(text.font != font) {
                text.font = font;
                ApplyShadow();
            }

            // Guard the TMP text rewrite so the mesh only re-tessellates when the
            // title actually changes (per level / format edit), not every frame.
            string body = BuildBody(isReorganizing);
            if(body != lastBody) {
                text.text = body;
                lastBody = body;
            }
        }
    }

    // Hides the game's own level-title HUD while the feature is taking over, so
    // only KRP's replacement shows. scrHUDText.Update re-enables the graphic each
    // frame, so this postfix re-hides it after. (A hidden source also hides its
    // GameOverlayFont twin, which copies the source's enabled state.)
    [HarmonyPatch(typeof(scrHUDText), "Update")]
    private static class HideGameTitlePatch {
        private static void Postfix(scrHUDText __instance) {
            try {
                if(__instance == null || !__instance.isTitle) {
                    return;
                }
                if(!TakesOverTitle || !GameStats.InGame) {
                    return;
                }
                Graphic g = __instance.GetComponent<Graphic>();
                if(g != null && g.enabled) {
                    g.enabled = false;
                }
            } catch {
            }
        }
    }
}
