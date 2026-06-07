using System.Globalization;
using System.Text;
using Koren.Core;
using Koren.IO;
using Koren.Resource;
using Koren.UI;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.Features.Status;

// First feature: a draggable on-screen HUD showing live status (FPS, frame time,
// mod state). Self-contained — its own canvas + settings, no tag/text engine.
// Driven entirely by StatusSettings, edited live from PageStatus.
public static class StatusOverlay {
    public static SettingsFile<StatusSettings> ConfMgr { get; private set; }
    public static StatusSettings Conf => ConfMgr.Data;

    private static GameObject canvasObj;
    private static RectTransform panel;
    private static GameObject dragObj;
    private static Image background;
    private static TextMeshProUGUI text;
    private static Updater updater;

    private const float PadX = 14f;
    private const float PadY = 10f;

    // Loads the settings file without building the canvas. Lets PageStatus read
    // config even while the mod is disabled (canvas not yet created).
    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<StatusSettings>(
            Path.Combine(MainCore.Paths.RootPath, "Status.json")
        );
        ConfMgr.Load();
    }

    public static void Initialize(GameObject root) {
        if(canvasObj != null) {
            return;
        }

        EnsureConf();

        canvasObj = new GameObject("KorenStatusCanvas");
        canvasObj.transform.SetParent(root.transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Below the settings panel (32767) so the menu always draws on top.
        canvas.sortingOrder = 32760;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // Panel — top-left anchored, draggable.
        GameObject panelObj = new("StatusPanel");
        panelObj.transform.SetParent(canvasObj.transform, false);
        panel = panelObj.AddComponent<RectTransform>();
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(Conf.PosX, Conf.PosY);

        background = panelObj.AddComponent<Image>();
        background.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P1024);
        background.type = Image.Type.Sliced;
        background.color = UIColors.PanelBG;
        background.raycastTarget = false;

        // Drag area (full-stretch, receives pointer; drags the parent panel).
        GameObject drag = new("Drag");
        dragObj = drag;
        drag.transform.SetParent(panel, false);
        RectTransform dragRect = drag.AddComponent<RectTransform>();
        dragRect.anchorMin = Vector2.zero;
        dragRect.anchorMax = Vector2.one;
        dragRect.offsetMin = Vector2.zero;
        dragRect.offsetMax = Vector2.zero;
        drag.AddComponent<EmptyGraphic>().raycastTarget = true;
        drag.AddComponent<DragHandler>();

        // Text.
        GameObject textObj = new("Text");
        textObj.transform.SetParent(panel, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(PadX, PadY);
        textRect.offsetMax = new Vector2(-PadX, -PadY);

        text = textObj.AddComponent<TextMeshProUGUI>();
        text.font = FontManager.Current;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = "";

        updater = canvasObj.AddComponent<Updater>();

        Apply();
    }

    // Pushes settings into the live visuals. Called on init + whenever PageStatus
    // changes a value.
    public static void Apply() {
        if(panel == null) {
            return;
        }

        // Visibility (Enabled + in-game) is owned by the per-frame Updater; Apply
        // only pushes look settings.
        if(text != null) {
            text.fontSize = Conf.FontSize;
            text.color = Conf.GetTextColor();
        }

        if(background != null) {
            background.enabled = Conf.BackgroundEnabled;
        }
    }

    public static void Save() => ConfMgr?.Save();

    public static void Dispose() {
        if(canvasObj == null) {
            return;
        }

        if(panel != null) {
            Conf.PosX = panel.anchoredPosition.x;
            Conf.PosY = panel.anchoredPosition.y;
        }

        ConfMgr?.Save();

        Object.Destroy(canvasObj);
        canvasObj = null;
        panel = null;
        dragObj = null;
        background = null;
        text = null;
        updater = null;
    }

    // Per-frame HUD refresh. Hidden unless enabled AND actually in a level; shows
    // the live ADOFAI stats while playing.
    private sealed class Updater : MonoBehaviour {
        private readonly StringBuilder sb = new();

        private void Update() {
            if(text == null) {
                return;
            }

            bool isReorganizing = UICore.IsOpen && UICore.CurrentMenuState == (int)OriginalMenuState.Reorganize;
            bool show = (Conf.Enabled && GameStats.InGame) || isReorganizing;
            
            if(panel.gameObject.activeSelf != show) {
                panel.gameObject.SetActive(show);
            }

            if(dragObj != null && dragObj.activeSelf != isReorganizing) {
                dragObj.SetActive(isReorganizing);
            }

            if(!show) {
                return;
            }

            sb.Clear();

            if(!string.IsNullOrEmpty(Conf.Prefix)) {
                sb.AppendLine(Conf.Prefix);
            }

            if(Conf.ShowProgress) {
                Line("Progress", GameStats.Progress);
            }

            if(Conf.ShowAccuracy) {
                Line("Accuracy", GameStats.Accuracy);
            }

            if(Conf.ShowXAccuracy) {
                Line("XAccuracy", GameStats.XAccuracy);
            }

            if(Conf.ShowMaxXAccuracy) {
                Line("Max XAcc", GameStats.MaxXAccuracy);
            }

            text.text = sb.ToString().TrimEnd();

            // Hug the text + keep the live position in settings (saved on Dispose).
            Vector2 pref = text.GetPreferredValues(text.text);
            panel.sizeDelta = new Vector2(pref.x + PadX * 2f, pref.y + PadY * 2f);

            Conf.PosX = panel.anchoredPosition.x;
            Conf.PosY = panel.anchoredPosition.y;
        }

        private void Line(string label, float ratio) {
            sb.Append(label).Append("  ").AppendLine(Pct(ratio));
        }

        private static string Pct(float ratio) {
            if(float.IsNaN(ratio) || float.IsInfinity(ratio)) {
                ratio = 0f;
            }

            int d = Mathf.Clamp(Conf.Decimals, 0, 6);
            string fmt = d == 0 ? "0" : "0." + new string('0', d);
            return (ratio * 100f).ToString(fmt, CultureInfo.InvariantCulture) + "%";
        }
    }
}
