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
        public RectTransform Section;
        public GameObject HeaderObj;
        public RectTransform Body;
        public bool Expanded;

        internal Image arrow;
        internal System.Action applyState;

        public void SetExpanded(bool v) {
            if(Expanded == v) {
                return;
            }
            Expanded = v;
            applyState?.Invoke();
        }
    }

    // Adds a collapsible section under `parent` (which must have a vertical
    // layout). The returned CollapsibleSection.Body is where the caller adds
    // body rows. Body is hidden when collapsed.
    public static CollapsibleSection Collapsible(
        Transform parent,
        string title,
        bool startExpanded
    ) {
        GameObject sectionObj = new("Section_" + title);
        sectionObj.transform.SetParent(parent, false);

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

        GameObject labelObj = new("Label");
        labelObj.transform.SetParent(barObj.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(16f, 0f);
        labelRect.offsetMax = new Vector2(-44f, 0f);
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.font = FontManager.Current;
        label.fontSize = 20f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Left;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.text = title;
        label.characterSpacing = -3f;
        label.raycastTarget = false;

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
            Section = sectionRect,
            HeaderObj = headerObj,
            Body = bodyRect,
            Expanded = startExpanded,
            arrow = arrowImg,
        };

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
        Apply(false);

        AddButton(barObj, btn => {
            if(btn == InputButton.Left) {
                c.SetExpanded(!c.Expanded);
            }
        });

        return c;
    }
}
