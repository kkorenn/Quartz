using UnityEngine;
using UnityEngine.UI;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI.Utility;

public static class TMPTextShadow {
    private const string RootName = "__KorenTextShadow";

    public static void Apply(
        TextMeshProUGUI text,
        bool enabled,
        float offsetX,
        float offsetY,
        float softness,
        Color color
    ) {
        if(text == null) {
            return;
        }

        DisableUnderlay(text);

        RectTransform shadowRoot = GetOrCreateRoot(text);
        if(shadowRoot == null) {
            return;
        }

        bool on = enabled && text.gameObject.activeSelf && color.a > 0.001f;
        shadowRoot.gameObject.SetActive(on);
        if(!on) {
            return;
        }

        SyncRootRect(text.rectTransform, shadowRoot);
        KeepRootBehindTarget(text, shadowRoot);

        float soft = Mathf.Clamp(softness, 0f, 50f);
        int layerCount = soft > 0.001f ? 9 : 1;
        EnsureLayerCount(shadowRoot, layerCount);

        Vector2 baseOffset = new(offsetX, offsetY);
        float spread = soft * 0.25f;

        for(int i = 0; i < shadowRoot.childCount; i++) {
            TextMeshProUGUI layer = shadowRoot.GetChild(i).GetComponent<TextMeshProUGUI>();
            bool active = i < layerCount;
            layer.gameObject.SetActive(active);
            if(!active) {
                continue;
            }

            Color layerColor = color;
            Vector2 layerOffset = baseOffset;
            if(i > 0) {
                layerOffset += SoftnessOffset(i - 1, spread);
                layerColor.a *= 0.28f;
            }

            SyncLayer(text, layer, layerColor, layerOffset);
        }
    }

    private static RectTransform GetOrCreateRoot(TextMeshProUGUI text) {
        Transform parent = text.transform.parent;
        if(parent == null) {
            return null;
        }

        foreach(Transform child in parent) {
            ShadowRoot marker = child.GetComponent<ShadowRoot>();
            if(marker != null && marker.Target == text) {
                return (RectTransform)child;
            }
        }

        GameObject obj = new(RootName);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        ShadowRoot root = obj.AddComponent<ShadowRoot>();
        root.Target = text;

        KeepRootBehindTarget(text, rect);
        return rect;
    }

    private static void EnsureLayerCount(RectTransform root, int count) {
        while(root.childCount < count) {
            GameObject obj = new("Layer");
            obj.transform.SetParent(root, false);

            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.raycastTarget = false;
        }
    }

    private static void KeepRootBehindTarget(TextMeshProUGUI text, RectTransform root) {
        int rootIndex = root.GetSiblingIndex();
        int textIndex = text.transform.GetSiblingIndex();

        if(rootIndex > textIndex) {
            root.SetSiblingIndex(textIndex);
        } else if(rootIndex < textIndex - 1) {
            root.SetSiblingIndex(textIndex - 1);
        }
    }

    private static void SyncRootRect(RectTransform source, RectTransform root) {
        root.anchorMin = source.anchorMin;
        root.anchorMax = source.anchorMax;
        root.pivot = source.pivot;
        root.localScale = source.localScale;
        root.localRotation = source.localRotation;
        root.offsetMin = source.offsetMin;
        root.offsetMax = source.offsetMax;
    }

    private static void SyncLayer(
        TextMeshProUGUI source,
        TextMeshProUGUI layer,
        Color color,
        Vector2 offset
    ) {
        RectTransform rect = layer.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = source.rectTransform.pivot;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.offsetMin = offset;
        rect.offsetMax = offset;

        layer.font = source.font;
        layer.text = source.text;
        layer.fontSize = source.fontSize;
        layer.fontStyle = source.fontStyle;
        layer.alignment = source.alignment;
        layer.color = color;
        layer.lineSpacing = source.lineSpacing;
        layer.characterSpacing = source.characterSpacing;
        layer.wordSpacing = source.wordSpacing;
        layer.paragraphSpacing = source.paragraphSpacing;
        layer.richText = source.richText;
        layer.overflowMode = source.overflowMode;
        layer.enableAutoSizing = source.enableAutoSizing;
        layer.fontSizeMin = source.fontSizeMin;
        layer.fontSizeMax = source.fontSizeMax;
        layer.margin = source.margin;
        layer.raycastTarget = false;
    }

    private static Vector2 SoftnessOffset(int index, float spread) {
        if(spread <= 0.001f) {
            return Vector2.zero;
        }

        return index switch {
            0 => new Vector2(spread, 0f),
            1 => new Vector2(-spread, 0f),
            2 => new Vector2(0f, spread),
            3 => new Vector2(0f, -spread),
            4 => new Vector2(spread, spread),
            5 => new Vector2(spread, -spread),
            6 => new Vector2(-spread, spread),
            _ => new Vector2(-spread, -spread),
        };
    }

    private static void DisableUnderlay(TextMeshProUGUI text) {
        Material mat = text.fontMaterial;
        if(mat == null) {
            return;
        }

        mat.DisableKeyword("UNDERLAY_ON");
        mat.DisableKeyword("UNDERLAY_INNER");
        mat.SetFloat("_UnderlayOffsetX", 0f);
        mat.SetFloat("_UnderlayOffsetY", 0f);
        mat.SetFloat("_UnderlaySoftness", 0f);
        mat.SetFloat("_UnderlayDilate", 0f);
    }

    private sealed class ShadowRoot : MonoBehaviour {
        public TextMeshProUGUI Target;
    }
}
