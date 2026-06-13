using Koren.Core;
using Koren.Resource;
using Koren.Tween;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEngine.EventSystems.PointerEventData;
using GTweens.Builders;
using GTweens.Easings;
using GTweens.Extensions;
using GTweens.Tweens;
using GTweenExtensions = GTweens.Extensions.GTweenExtensions;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI.Generator;

// Collapsible — a header bar that toggles a body container visible/hidden.
// Body has its own VerticalLayoutGroup so caller just adds child Rows to it.
public static partial class GenerateUI {

    public sealed class CollapsibleSection {
        public string Title;
        public RectTransform Section;
        public GameObject HeaderObj;
        public RectTransform Body;
        public bool Expanded;

        internal string stateKey;
        internal Image arrow;
        internal System.Action applyState;
        internal System.Action applyInstant;

        public void SetExpanded(bool v) => SetExpanded(v, true, true);

        // animate: false snaps open/closed without the slide tween — used by
        // search navigation, which needs final layout heights immediately to
        // compute the scroll position.
        public void SetExpanded(bool v, bool animate) => SetExpanded(v, animate, true);

        public void SetExpanded(bool v, bool animate, bool save) {
            if(Expanded == v) {
                return;
            }
            Expanded = v;
            if(save && !string.IsNullOrEmpty(stateKey)) {
                MainCore.Conf.SetCollapsibleExpanded(stateKey, v);
                MainCore.ConfMgr.RequestSave();
            }
            if(animate) {
                applyState?.Invoke();
            } else {
                applyInstant?.Invoke();
            }
        }
    }

    // Every live collapsible, so search can expand the sections that hide a
    // result before scrolling to it. Cleared when the pages are (re)built.
    public static readonly List<CollapsibleSection> Sections = [];

    public static void ClearSections() => Sections.Clear();

    private static string GetCollapsibleKey(Transform parent, string title) {
        List<string> parts = [];

        Transform current = parent;
        while(current != null) {
            string name = current.name;
            if(name.StartsWith("Page") || name.StartsWith("Section_")) {
                parts.Add(name);
            }

            current = current.parent;
        }

        parts.Reverse();
        parts.Add("Section_" + title);
        return string.Join("/", parts);
    }

    // Adds a collapsible section under `parent` (which must have a vertical
    // layout). The returned CollapsibleSection.Body is where the caller adds
    // body rows. Body is hidden when collapsed.
    public static CollapsibleSection Collapsible(
        Transform parent,
        string title,
        bool startExpanded
    ) => Collapsible(parent, title, startExpanded, null, false);

    // Variant with an enable toggle embedded in the header (left of the
    // arrow), so a feature section can be switched on/off without expanding
    // it first. Clicking the dot only toggles; the rest of the header still
    // expands/collapses.
    public static CollapsibleSection Collapsible(
        Transform parent,
        string title,
        bool startExpanded,
        Action<bool> onToggle,
        bool toggleValue
    ) {
        GameObject sectionObj = new("Section_" + title);
        sectionObj.transform.SetParent(parent, false);
        string stateKey = GetCollapsibleKey(parent, title);
        bool expanded = MainCore.Conf.GetCollapsibleExpanded(stateKey);

        RectTransform sectionRect = sectionObj.AddComponent<RectTransform>();

        VerticalLayoutGroup sectionLayout = sectionObj.AddComponent<VerticalLayoutGroup>();
        sectionLayout.spacing = 6f;
        sectionLayout.childControlWidth = true;
        sectionLayout.childControlHeight = true;
        sectionLayout.childForceExpandWidth = true;
        sectionLayout.childForceExpandHeight = false;

        ContentSizeFitter sectionFitter = sectionObj.AddComponent<ContentSizeFitter>();
        sectionFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Header.
        GameObject headerObj = new("Header");
        headerObj.transform.SetParent(sectionRect, false);

        LayoutElement headerLE = headerObj.AddComponent<LayoutElement>();
        headerLE.preferredHeight = 44f;
        headerLE.minHeight = 44f;

        // Inset bar so the header lines up with the rows below it — everything
        // else (toggles, dropdowns) stops 250px short of the right edge.
        GameObject barObj = new("Bar");
        barObj.transform.SetParent(headerObj.transform, false);
        RectTransform barRect = barObj.AddComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.offsetMin = Vector2.zero;
        barRect.offsetMax = new Vector2(-250f, 0f);

        Image headerBg = barObj.AddComponent<Image>();
        headerBg.color = UIColors.ObjectBG;
        headerBg.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        headerBg.type = Image.Type.Sliced;
        headerBg.raycastTarget = true;

        GameObject arrowObj = new("Arrow");
        arrowObj.transform.SetParent(barObj.transform, false);
        RectTransform arrowRect = arrowObj.AddComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1f, 0.5f);
        arrowRect.anchorMax = new Vector2(1f, 0.5f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.anchoredPosition = new Vector2(-23f, 0f);
        arrowRect.sizeDelta = new Vector2(26f, 26f);
        Image arrowImg = arrowObj.AddComponent<Image>();
        arrowImg.sprite = MainCore.Spr.Get(UISprite.Triangle128);
        arrowImg.color = UIColors.ObjectInactive;
        arrowImg.raycastTarget = false;

        // Header enable toggle (optional): an accent dot left of the arrow,
        // same look as the stat rows' enable dot. Its own EventTrigger
        // swallows the press so toggling never expands the section.
        if(onToggle != null) {
            GameObject toggleZone = new("HeaderToggle");
            toggleZone.transform.SetParent(barObj.transform, false);

            RectTransform zoneRect = toggleZone.AddComponent<RectTransform>();
            zoneRect.anchorMin = new Vector2(1f, 0.5f);
            zoneRect.anchorMax = new Vector2(1f, 0.5f);
            zoneRect.pivot = new Vector2(0.5f, 0.5f);
            zoneRect.anchoredPosition = new Vector2(-64f, 0f);
            zoneRect.sizeDelta = new Vector2(40f, 44f);
            toggleZone.AddComponent<EmptyGraphic>().raycastTarget = true;

            GameObject dotObj = new("Dot");
            dotObj.transform.SetParent(toggleZone.transform, false);
            RectTransform dotRect = dotObj.AddComponent<RectTransform>();
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(26f, 26f);

            Image dotImg = dotObj.AddComponent<Image>();
            dotImg.sprite = MainCore.Spr.Get(UISprite.Circle256);
            dotImg.raycastTarget = false;

            bool on = toggleValue;
            Color OffColor() => new(1f, 1f, 1f, 0.18f);
            dotImg.color = on ? UIColors.ObjectActive : OffColor();

            GTween dotSeq = null;
            EventTrigger zoneTrigger = toggleZone.AddComponent<EventTrigger>();
            UnityUtils.AddClickEvent(zoneTrigger, e => {
                if(e.button != InputButton.Left) {
                    return;
                }

                on = !on;
                onToggle(on);

                dotSeq?.Kill();
                dotSeq = GTweenSequenceBuilder.New()
                    .Append(dotImg.GTColor(on ? UIColors.ObjectActive : OffColor(), 0.15f)
                        .SetEasing(Easing.OutSine))
                    .Build();
                MainCore.TC.Play(dotSeq);
            });
        }

        GameObject labelObj = new("Label");
        labelObj.transform.SetParent(barObj.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(16f, 0f);
        labelRect.offsetMax = new Vector2(onToggle != null ? -88f : -44f, 0f);
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.font = FontManager.Current;
        label.fontSize = 20f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Left;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.text = title;
        label.characterSpacing = -3f;
        label.raycastTarget = false;
        if(parent == null || parent.name != "PanelsList") {
            Localize(label, LocaleKeyFromText("SECTION", title), title);
        }

        // Body: lays out the caller's rows and sizes to them. A RectMask2D
        // clips the slide during the open/close animation; once open we hand
        // sizing back to the ContentSizeFitter so nested widgets (dropdowns,
        // color pickers) can still grow the section naturally.
        GameObject bodyObj = new("Body");
        bodyObj.transform.SetParent(sectionRect, false);
        RectTransform bodyRect = bodyObj.AddComponent<RectTransform>();

        VerticalLayoutGroup bodyLayout = bodyObj.AddComponent<VerticalLayoutGroup>();
        bodyLayout.spacing = 8f;
        bodyLayout.padding = new RectOffset(20, 0, 0, 0);
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = false;

        ContentSizeFitter bodyFitter = bodyObj.AddComponent<ContentSizeFitter>();
        bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        LayoutElement bodyLE = bodyObj.AddComponent<LayoutElement>();
        bodyObj.AddComponent<RectMask2D>();
        CanvasGroup bodyCg = bodyObj.AddComponent<CanvasGroup>();

        CollapsibleSection c = new() {
            Title = title,
            Section = sectionRect,
            HeaderObj = headerObj,
            Body = bodyRect,
            Expanded = expanded,
            stateKey = stateKey,
            arrow = arrowImg,
        };
        Sections.Add(c);

        GTween openSeq = null;
        GTween arrowSeq = null;

        // Open/close like a dropdown: slide the section height + fade, rotate
        // the arrow (down collapsed / up expanded) and recolor it.
        void Apply(bool animate) {
            bool exp = c.Expanded;
            Vector3 targetRot = exp ? new Vector3(0f, 0f, 180f) : Vector3.zero;
            Color targetCol = exp ? UIColors.ObjectActive : UIColors.ObjectInactive;

            bodyCg.blocksRaycasts = exp;
            bodyCg.interactable = exp;

            openSeq?.Kill();
            arrowSeq?.Kill();

            if(!animate) {
                bodyObj.SetActive(exp);
                bodyLayout.enabled = exp;
                bodyFitter.enabled = exp;
                bodyLE.preferredHeight = exp ? -1f : 0f;
                bodyCg.alpha = exp ? 1f : 0f;
                arrowRect.localRotation = Quaternion.Euler(targetRot);
                arrowImg.color = targetCol;
                LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
                return;
            }

            // Lay the content out once, then freeze the layout group so the
            // hand-driven LayoutElement height isn't overridden by the group's
            // own preferred height. The RectMask2D clips the frozen rows as the
            // section slides; sizing is handed back once open.
            //
            // Rebuild from the SECTION (with the height override cleared) so the
            // body first gets its real width — the section's layout controls it.
            // Rebuilding only the body lays the rows out at zero width on the
            // first open, so their backgrounds and the side bars don't show.
            bodyObj.SetActive(true);
            bodyLayout.enabled = true;
            bodyFitter.enabled = true;
            bodyLE.preferredHeight = -1f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
            float content = bodyRect.rect.height;

            bodyLayout.enabled = false;
            bodyFitter.enabled = false;

            float to = exp ? content : 0f;
            bodyLE.preferredHeight = exp ? 0f : content;

            // Body slide + fade. The cleanup runs when THIS finishes (0.16s) so
            // the body is hidden the instant it reaches zero height. The arrow
            // spin below is a separate, longer tween and must not gate it —
            // otherwise a collapsed-but-still-active body leaves the section's
            // layout spacing showing as a gap until the arrow finishes.
            // Open overshoots for a bounce (OutBack); close eases straight to 0
            // (OutSine) so the height never plateaus at 0 before the tween ends.
            openSeq = GTweenSequenceBuilder.New()
                .Join(GTweenExtensions.Tween(
                    () => bodyLE.preferredHeight,
                    x => {
                        bodyLE.preferredHeight = Mathf.Max(0f, x);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
                    },
                    to,
                    0.16f
                ).SetEasing(exp ? Easing.OutBack : Easing.OutSine))
                .Join(GTweenExtensions.Tween(
                    () => bodyCg.alpha,
                    x => bodyCg.alpha = x,
                    exp ? 1f : 0f,
                    0.16f
                ).SetEasing(Easing.OutSine))
                .AppendCallback(() => {
                    if(c.Expanded) {
                        // Hand sizing back so nested widgets can grow the body.
                        bodyLayout.enabled = true;
                        bodyFitter.enabled = true;
                        bodyLE.preferredHeight = -1f;
                        LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
                    }
                    else {
                        bodyObj.SetActive(false);
                        bodyLE.preferredHeight = 0f;
                    }
                })
                .Build();

            MainCore.TC.Play(openSeq);

            // Arrow spin + recolor — runs independently so its longer duration
            // doesn't delay hiding the body on collapse.
            arrowSeq = GTweenSequenceBuilder.New()
                .Join(arrowRect.GTRotate(targetRot, 0.4f).SetEasing(Easing.OutBack))
                .Join(arrowImg.GTColor(targetCol, 0.2f).SetEasing(Easing.OutSine))
                .Build();

            MainCore.TC.Play(arrowSeq);
        }

        c.applyState = () => Apply(true);
        c.applyInstant = () => Apply(false);
        Apply(false);

        AddButton(barObj, btn => {
            if(btn == InputButton.Left) {
                c.SetExpanded(!c.Expanded, true, true);
            }
        });

        return c;
    }
}
