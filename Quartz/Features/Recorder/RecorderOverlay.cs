using Quartz.Core;
using Quartz.Resource;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Quartz.Features.Recorder;

// Full-screen white "rendering" screen shown to the player while a render runs.
// It lives on a ScreenSpaceOverlay canvas, so it is composited only to the
// display — the capture re-renders the game cameras into an off-screen texture
// and never sees this overlay. It doubles as something to look at while the
// level plays back at the (variable, often slow) offline rate.
internal static class RecorderOverlay {
    private static GameObject canvasObj;
    private static TextMeshProUGUI title;
    private static TextMeshProUGUI detail;

    public static void Show() {
        if(canvasObj != null) {
            return;
        }

        canvasObj = new GameObject("QuartzRecorderOverlay");
        canvasObj.transform.SetParent(MainCore.Root.transform, false);
        Object.DontDestroyOnLoad(canvasObj);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760; // above the mod's own overlays

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Opaque white fill.
        GameObject bg = new("White");
        bg.transform.SetParent(canvasObj.transform, false);
        RectTransform bgRect = bg.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        bg.AddComponent<Image>().color = Color.white;

        title = MakeText(40f, FontStyles.Bold, new Color(0.08f, 0.08f, 0.1f), new Vector2(0f, 70f));
        detail = MakeText(28f, FontStyles.Normal, new Color(0.3f, 0.3f, 0.34f), new Vector2(0f, -10f));

        Set(0, 0, 0);
    }

    private static TextMeshProUGUI MakeText(float size, FontStyles style, Color color, Vector2 offset) {
        GameObject go = new("Text");
        go.transform.SetParent(canvasObj.transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = FontManager.Current;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        RectTransform rect = tmp.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1400f, 120f);
        rect.anchoredPosition = offset;
        return tmp;
    }

    // frame/total drive the headline; renderFps is the wall-clock processing rate.
    public static void Set(int frame, int total, double renderFps) {
        if(title == null) {
            return;
        }
        title.text = MainCore.Tr.Get("RENDER_OVERLAY_TITLE", "Rendering…");

        string line1 = total > 0
            ? $"{frame:N0} / {total:N0} {FramesWord()}  ·  {Mathf.Clamp01(frame / (float)total) * 100f:0.0}%"
            : $"{frame:N0} {FramesWord()}";

        string line2 = renderFps > 0 ? $"{renderFps:0} {FpsWord()}" : "…";
        if(total > 0 && renderFps > 0.01) {
            line2 += $"  ·  {EtaWord()} {FormatEta((total - frame) / renderFps)}";
        }

        detail.text = $"{line1}\n{line2}\n{EscHint()}";
    }

    private static string FramesWord() => MainCore.Tr.Get("RENDER_OVERLAY_FRAMES", "frames");
    private static string FpsWord() => MainCore.Tr.Get("RENDER_OVERLAY_FPS", "fps");
    private static string EtaWord() => MainCore.Tr.Get("RENDER_OVERLAY_ETA", "ETA");
    private static string EscHint() => MainCore.Tr.Get("RENDER_OVERLAY_CANCEL", "Esc to cancel");

    private static string FormatEta(double seconds) {
        if(seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) {
            seconds = 0;
        }
        int s = (int)seconds;
        int m = s / 60;
        s %= 60;
        return m > 0 ? $"{m}m{s:00}s" : $"{s}s";
    }

    public static void Hide() {
        if(canvasObj != null) {
            Object.Destroy(canvasObj);
            canvasObj = null;
            title = null;
            detail = null;
        }
    }
}
