using Koren.Features.Editor;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace Koren.UI.Factory.Page;

// Editor tab. Hosts tweaks that target A Dance of Fire and Ice's level editor.
internal static class PageEditor {
    public static void Create(RectTransform parent) {
        EditorFeature.EnsureConf();
        EditorSettings conf = EditorFeature.Conf;
        EditorSettings def = new();

        GameObject pad = new("Pad");
        pad.transform.SetParent(parent, false);

        RectTransform padRect = pad.AddComponent<RectTransform>();
        padRect.anchorMin = Vector2.zero;
        padRect.anchorMax = Vector2.one;
        padRect.pivot = new Vector2(0.5f, 0.5f);
        padRect.offsetMin = new Vector2(18f, 18f);
        padRect.offsetMax = new Vector2(-18f, -18f);

        GameObject viewport = new("Viewport");
        viewport.transform.SetParent(pad.transform, false);

        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportRect.pivot = new Vector2(0.5f, 0.5f);

        viewport.AddComponent<EmptyGraphic>().raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        GameObject content = new("Content");
        content.transform.SetParent(viewport.transform, false);

        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        pad.AddComponent<UIScrollController>().SetContent(contentRect, viewportRect);

        // === Inspector ===
        GenerateUI.Localize(
            GenerateUI.AddTextH1(GenerateUI.Row(content.transform)),
            "HEADING_INSPECTOR", "Inspector"
        );

        var horizontalProperties = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.HorizontalProperties,
            conf.HorizontalProperties,
            v => {
                conf.HorizontalProperties = v;
                EditorFeature.Apply();
                EditorFeature.Save();
            },
            "Horizontal Properties",
            "editor_horizontal_properties"
        );
        horizontalProperties.Rect.AddToolTip(
            "DESC_EDITOR_HORIZONTAL_PROPERTIES",
            "Lays each level-editor inspector property out as \"label [field]\" on one row, instead of the label stacked above the field. Affects the in-game editor, not this settings window."
        );

        var showTileAngle = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.ShowTileAngle,
            conf.ShowTileAngle,
            v => {
                conf.ShowTileAngle = v;
                EditorFeature.Apply();
                EditorFeature.Save();
            },
            "Show Tile Angle",
            "editor_show_tile_angle"
        );
        showTileAngle.Rect.AddToolTip(
            "DESC_EDITOR_SHOW_TILE_ANGLE",
            "Shows the selected tile's angle in degrees as a label on the tile, in the level editor. Affects the in-game editor, not this settings window."
        );

        GenerateUI.ColorPicker(
            GenerateUI.Row(content.transform),
            def.GetAngleColor(),
            conf.GetAngleColor(),
            c => conf.SetAngleColor(c),
            c => { conf.SetAngleColor(c); EditorFeature.Save(); },
            "Tile Angle Color",
            "editor_tile_angle_color"
        );
    }
}
