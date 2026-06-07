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

// Two-panel HUD showing live ADOFAI stats. Each stat has its own enable
// toggle and an OnRight flag that routes it to either the left- or right-
// anchored panel. Both panels are independently draggable in Reorganize
// mode and persist their positions separately. Each panel hugs its own
// text content (only sized while visible).
public static class StatusOverlay {
    public static SettingsFile<StatusSettings> ConfMgr { get; private set; }
    public static StatusSettings Conf => ConfMgr.Data;

    private static GameObject canvasObj;
    private static Panel left;
    private static Panel right;
    private static Updater updater;

    private const float PadX = 14f;
    private const float PadY = 10f;

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
        canvas.sortingOrder = 32760;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        left = CreatePanel("StatusPanelLeft", anchorRight: false, Conf.LeftPosX, Conf.LeftPosY);
        right = CreatePanel("StatusPanelRight", anchorRight: true, Conf.RightPosX, Conf.RightPosY);

        updater = canvasObj.AddComponent<Updater>();

        Apply();
    }

    private static Panel CreatePanel(string name, bool anchorRight, float posX, float posY) {
        GameObject panelObj = new(name);
        panelObj.transform.SetParent(canvasObj.transform, false);

        Vector2 anchor = anchorRight ? new Vector2(1f, 1f) : new Vector2(0f, 1f);

        RectTransform rect = panelObj.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = new Vector2(posX, posY);

        Image bg = panelObj.AddComponent<Image>();
        bg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P1024);
        bg.type = Image.Type.Sliced;
        bg.color = UIColors.PanelBG;
        bg.raycastTarget = false;

        GameObject drag = new("Drag");
        drag.transform.SetParent(rect, false);
        RectTransform dragRect = drag.AddComponent<RectTransform>();
        dragRect.anchorMin = Vector2.zero;
        dragRect.anchorMax = Vector2.one;
        dragRect.offsetMin = Vector2.zero;
        dragRect.offsetMax = Vector2.zero;
        drag.AddComponent<EmptyGraphic>().raycastTarget = true;
        drag.AddComponent<DragHandler>();
        drag.SetActive(false);

        GameObject textObj = new("Text");
        textObj.transform.SetParent(rect, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(PadX, PadY);
        textRect.offsetMax = new Vector2(-PadX, -PadY);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.font = FontManager.Current;
        text.alignment = anchorRight ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = "";

        return new Panel {
            Rect = rect,
            Background = bg,
            DragObj = drag,
            Text = text,
            AnchorRight = anchorRight,
        };
    }

    public static void Apply() {
        if(left == null && right == null) {
            return;
        }

        ApplyPanel(left);
        ApplyPanel(right);
    }

    private static void ApplyPanel(Panel p) {
        if(p == null) {
            return;
        }

        if(p.Text != null) {
            p.Text.fontSize = Conf.FontSize;
            p.Text.color = Conf.GetTextColor();
            p.Text.lineSpacing = Conf.LineSpacing;
        }

        if(p.Background != null) {
            p.Background.enabled = Conf.BackgroundEnabled;
        }
    }

    public static void ResetLeftPosition() {
        StatusSettings def = new();
        Conf.LeftPosX = def.LeftPosX;
        Conf.LeftPosY = def.LeftPosY;
        if(left?.Rect != null) {
            left.Rect.anchoredPosition = new Vector2(def.LeftPosX, def.LeftPosY);
        }
        Save();
    }

    public static void ResetRightPosition() {
        StatusSettings def = new();
        Conf.RightPosX = def.RightPosX;
        Conf.RightPosY = def.RightPosY;
        if(right?.Rect != null) {
            right.Rect.anchoredPosition = new Vector2(def.RightPosX, def.RightPosY);
        }
        Save();
    }

    public static void Save() => ConfMgr?.Save();

    public static void Dispose() {
        if(canvasObj == null) {
            return;
        }

        if(left?.Rect != null) {
            Conf.LeftPosX = left.Rect.anchoredPosition.x;
            Conf.LeftPosY = left.Rect.anchoredPosition.y;
        }

        if(right?.Rect != null) {
            Conf.RightPosX = right.Rect.anchoredPosition.x;
            Conf.RightPosY = right.Rect.anchoredPosition.y;
        }

        ConfMgr?.Save();

        Object.Destroy(canvasObj);
        canvasObj = null;
        left = null;
        right = null;
        updater = null;
    }

    private sealed class Panel {
        public RectTransform Rect;
        public Image Background;
        public GameObject DragObj;
        public TextMeshProUGUI Text;
        public bool AnchorRight;
    }

    private sealed class Updater : MonoBehaviour {
        private readonly StringBuilder sbLeft = new();
        private readonly StringBuilder sbRight = new();

        private void Update() {
            if(left?.Text == null || right?.Text == null) {
                return;
            }

            bool isReorganizing = UICore.IsOpen && UICore.CurrentMenuState == (int)OriginalMenuState.Reorganize;
            bool show = (Conf.Enabled && GameStats.InGame) || isReorganizing;

            sbLeft.Clear();
            sbRight.Clear();

            if(show) {
                if(!string.IsNullOrEmpty(Conf.Prefix)) {
                    sbLeft.AppendLine(Conf.Prefix);
                }

                if(Conf.ShowProgress) {
                    string val = GameStats.RunHasStartProgress
                        ? Pct(GameStats.RunStartProgress) + " - " + Pct(GameStats.Progress)
                        : Pct(GameStats.Progress);
                    Line(Conf.ProgressOnRight, "Progress", val);
                }

                if(Conf.ShowAccuracy) {
                    Line(Conf.AccuracyOnRight, "Accuracy", Pct(GameStats.Accuracy));
                }

                if(Conf.ShowXAccuracy) {
                    Line(Conf.XAccuracyOnRight, "XAccuracy", Pct(GameStats.XAccuracy));
                }

                if(Conf.ShowMaxAccuracy) {
                    Line(Conf.MaxAccuracyOnRight, "Max Acc", Pct(GameStats.MaxAccuracy));
                }

                if(Conf.ShowMaxXAccuracy) {
                    Line(Conf.MaxXAccuracyOnRight, "Max XAcc", Pct(GameStats.MaxXAccuracy));
                }

                if(Conf.ShowMusicTime) {
                    Line(Conf.MusicTimeOnRight, "Music Time", GameStats.MusicTimeText);
                }

                if(Conf.ShowMapTime) {
                    Line(Conf.MapTimeOnRight, "Map Time", GameStats.MapTimeText);
                }

                if(Conf.ShowCheckpoint) {
                    Line(Conf.CheckpointOnRight, "Checkpoints",
                        GameStats.CheckpointCount.ToString(CultureInfo.InvariantCulture));
                }

                if(Conf.ShowTbpm || Conf.ShowCbpm || Conf.ShowKps) {
                    GameStats.GetBpm(out float tbpm, out float cbpm);
                    if(Conf.ShowTbpm) {
                        Line(Conf.TbpmOnRight, "TBPM",
                            tbpm.ToString("0.##", CultureInfo.InvariantCulture));
                    }
                    if(Conf.ShowCbpm) {
                        Line(Conf.CbpmOnRight, "CBPM",
                            cbpm.ToString("0.##", CultureInfo.InvariantCulture));
                    }
                    if(Conf.ShowKps) {
                        Line(Conf.KpsOnRight, "KPS",
                            (cbpm / 60f).ToString("0.##", CultureInfo.InvariantCulture));
                    }
                }

                if(Conf.ShowHold) {
                    string hold = GameStats.HoldBehaviorLabel;
                    if(!string.IsNullOrEmpty(hold)) {
                        Line(Conf.HoldOnRight, "Holds", hold);
                    }
                }

                if(Conf.ShowTimingScale) {
                    Line(Conf.TimingScaleOnRight, "Timing Scale",
                        (GameStats.MarginScale * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%");
                }

                if(Conf.ShowCombo) {
                    Line(Conf.ComboOnRight, "Combo",
                        GameStats.Combo.ToString(CultureInfo.InvariantCulture));
                }

                if(Conf.ShowAttempt) {
                    Line(Conf.AttemptOnRight, "Attempt",
                        GameStats.SessionAttempts.ToString(CultureInfo.InvariantCulture));
                }

                if(Conf.ShowTotalAttempt) {
                    Line(Conf.TotalAttemptOnRight, "Total Attempts",
                        GameStats.TotalAttempts.ToString(CultureInfo.InvariantCulture));
                }

                if(Conf.ShowBest) {
                    Line(Conf.BestOnRight, "Best", Pct(GameStats.Best));
                }

                if(Conf.ShowFps) {
                    Line(Conf.FpsOnRight, "FPS",
                        GameStats.Fps.ToString(CultureInfo.InvariantCulture));
                }
            }

            ApplyPanel(left, sbLeft, isReorganizing);
            ApplyPanel(right, sbRight, isReorganizing);
        }

        private static void ApplyPanel(Panel p, StringBuilder sb, bool isReorganizing) {
            if(p?.Text == null) {
                return;
            }

            string body = sb.Length == 0 ? "" : sb.ToString().TrimEnd();
            // Reorganize mode forces the empty panel to render its name so the
            // user has a hit target to grab.
            if(isReorganizing && body.Length == 0) {
                body = p.AnchorRight ? "Status (Right)" : "Status (Left)";
            }

            bool active = body.Length > 0 || isReorganizing;
            if(p.Rect.gameObject.activeSelf != active) {
                p.Rect.gameObject.SetActive(active);
            }

            if(p.DragObj != null && p.DragObj.activeSelf != isReorganizing) {
                p.DragObj.SetActive(isReorganizing);
            }

            if(!active) {
                return;
            }

            p.Text.text = body;

            Vector2 pref = p.Text.GetPreferredValues(p.Text.text);
            p.Rect.sizeDelta = new Vector2(pref.x + PadX * 2f, pref.y + PadY * 2f);

            if(p.AnchorRight) {
                Conf.RightPosX = p.Rect.anchoredPosition.x;
                Conf.RightPosY = p.Rect.anchoredPosition.y;
            } else {
                Conf.LeftPosX = p.Rect.anchoredPosition.x;
                Conf.LeftPosY = p.Rect.anchoredPosition.y;
            }
        }

        private void Line(bool onRight, string label, string value) {
            StringBuilder sb = onRight ? sbRight : sbLeft;
            sb.Append(label).Append(Conf.LabelSeparator).AppendLine(value);
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
