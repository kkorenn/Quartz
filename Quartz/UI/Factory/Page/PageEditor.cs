using Quartz.Features.Editor;
using Quartz.UI.Generator;
using Quartz.UI.Objects.Impl;
using Quartz.UI.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace Quartz.UI.Factory.Page;

// Editor tab. Hosts tweaks that target A Dance of Fire and Ice's level editor.
internal static partial class PageEditor {
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

        // === Selected-tile readout (ported from AdofaiTweaks) ===
        GenerateUI.Localize(
            GenerateUI.AddTextH1(GenerateUI.Row(content.transform)),
            "HEADING_TILE_READOUT", "Selected Tile Readout"
        );

        var showFloorAngle = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.ShowFloorAngle,
            conf.ShowFloorAngle,
            v => {
                conf.ShowFloorAngle = v;
                EditorFeature.Apply();
                EditorFeature.Save();
            },
            "Show selected tiles' angle",
            "editor_show_floor_angle"
        );
        showFloorAngle.Rect.AddToolTip(
            "DESC_EDITOR_SHOW_FLOOR_ANGLE",
            "Shows the total angle (in degrees) of the selected tiles, on one of the tiles in the level editor. Affects the in-game editor, not this settings window."
        );

        var showFloorBeats = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.ShowFloorBeats,
            conf.ShowFloorBeats,
            v => {
                conf.ShowFloorBeats = v;
                EditorFeature.Apply();
                EditorFeature.Save();
            },
            "Show selected tiles' beats",
            "editor_show_floor_beats"
        );
        showFloorBeats.Rect.AddToolTip(
            "DESC_EDITOR_SHOW_FLOOR_BEATS",
            "Shows the selected tiles' length in beats (total angle ÷ 180), on one of the tiles in the level editor. Affects the in-game editor, not this settings window."
        );

        var showFloorCount = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.ShowFloorCount,
            conf.ShowFloorCount,
            v => {
                conf.ShowFloorCount = v;
                EditorFeature.Apply();
                EditorFeature.Save();
            },
            "Show selected tiles' count",
            "editor_show_floor_count"
        );
        showFloorCount.Rect.AddToolTip(
            "DESC_EDITOR_SHOW_FLOOR_COUNT",
            "Shows how many tiles are selected, on one of the tiles in the level editor. Affects the in-game editor, not this settings window."
        );

        var showFloorDuration = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.ShowFloorDuration,
            conf.ShowFloorDuration,
            v => {
                conf.ShowFloorDuration = v;
                EditorFeature.Apply();
                EditorFeature.Save();
            },
            "Show selected tiles' duration (in seconds)",
            "editor_show_floor_duration"
        );
        showFloorDuration.Rect.AddToolTip(
            "DESC_EDITOR_SHOW_FLOOR_DURATION",
            "Shows how long the selected tiles take to play, in seconds, on one of the tiles in the level editor. Affects the in-game editor, not this settings window."
        );

        var useTulttak = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.UseTulttakModBehavior,
            conf.UseTulttakModBehavior,
            v => {
                conf.UseTulttakModBehavior = v;
                EditorFeature.Apply();
                EditorFeature.Save();
            },
            "Prevent the last tile from being counted in timing calculation",
            "editor_use_tulttak_behavior"
        );
        useTulttak.Rect.AddToolTip(
            "DESC_EDITOR_USE_TULTTAK_BEHAVIOR",
            "Matches the Tulttak mod's behaviour by leaving the last selected tile out of the angle, beats, and duration totals."
        );

        // === BGA Mod ===
        GenerateUI.Localize(
            GenerateUI.AddTextH1(GenerateUI.Row(content.transform)),
            "HEADING_BGA", "BGA Mod"
        );

        var bgaMod = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.BgaMod,
            conf.BgaMod,
            v => {
                conf.BgaMod = v;
                EditorFeature.Apply();
                EditorFeature.Save();
            },
            "Hide tiles and planets",
            "editor_bga_mod"
        );
        bgaMod.Rect.AddToolTip(
            "DESC_EDITOR_BGA_MOD",
            "Hides every tile and planet while the level is playing, so only the background shows — for recording a background animation (BGA) to composite gameplay over. The editor's edit view is unaffected; hiding only kicks in during play-test or actual gameplay."
        );

        var bgaTileDeco = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.BgaHideTileDeco,
            conf.BgaHideTileDeco,
            v => {
                conf.BgaHideTileDeco = v;
                EditorFeature.Apply();
                EditorFeature.Save();
            },
            "Disable tile decorations",
            "editor_bga_hide_tile_deco"
        );
        bgaTileDeco.Rect.AddToolTip(
            "DESC_EDITOR_BGA_HIDE_TILE_DECO",
            "Also hides decorations attached to tiles while BGA Mod is hiding the level. Background and camera-anchored decorations are left visible."
        );

        var bgaPlanetDeco = GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            def.BgaHidePlanetDeco,
            conf.BgaHidePlanetDeco,
            v => {
                conf.BgaHidePlanetDeco = v;
                EditorFeature.Apply();
                EditorFeature.Save();
            },
            "Disable planet decorations",
            "editor_bga_hide_planet_deco"
        );
        bgaPlanetDeco.Rect.AddToolTip(
            "DESC_EDITOR_BGA_HIDE_PLANET_DECO",
            "Also hides decorations attached to a planet while BGA Mod is hiding the level. Background and camera-anchored decorations are left visible."
        );

        // === FFmpeg Renderer ===
        CreateRendererSection(content.transform);

        NostalgiaUI.AddEditorSection(content.transform);
    }
}
