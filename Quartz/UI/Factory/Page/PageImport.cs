using Quartz.Core;
using Quartz.Features.Interop;
using Quartz.UI.Generator;
using Quartz.UI.Objects.Impl;
using Quartz.UI.Utility;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

using TMPro;

namespace Quartz.UI.Factory.Page;

// Import tab. Lists every supported ADOFAI mod currently loaded through Unity
// Mod Manager and copies its settings into Quartz. Mods that ship a
// KeyViewer (KorenResourcePack v1, JipperResourcePack, JipperKeyViewer) get a
// replace mode + the per-group toggles, mirroring v1's importer. The heavy
// lifting lives in SettingsImporter; this page is just the surface.
internal static class PageImport {
    private static RectTransform listContainer;
    private static TextMeshProUGUI statusText;

    // Per-mod KeyViewer import choices, keyed by option id so they survive a
    // list rebuild (e.g. when switching the replace mode re-draws the card).
    private static readonly Dictionary<string, SettingsImportReplaceMode> modes = [];
    private static readonly Dictionary<string, SettingsImportKeyViewerPart> parts = [];

    private static readonly (SettingsImportKeyViewerPart Flag, string Id, string Default)[] PartDefs = [
        (SettingsImportKeyViewerPart.KeysLayout, "import_part_keys", "Keys / layout"),
        (SettingsImportKeyViewerPart.Labels, "import_part_labels", "Labels"),
        (SettingsImportKeyViewerPart.Colors, "import_part_colors", "Colors"),
        (SettingsImportKeyViewerPart.Rain, "import_part_rain", "Rain"),
        (SettingsImportKeyViewerPart.PositionSize, "import_part_position", "Position / size"),
    ];

    public static void Create(RectTransform parent) {
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

        TextMeshProUGUI headerText = GenerateUI.AddTextH1(GenerateUI.Row(content.transform));
        GenerateUI.Localize(headerText, "IMPORT_HEADER", "Import from other mods");

        var hintRow = GenerateUI.Row(content.transform, 96f);
        var hintText = GenerateUI.AddText(hintRow, noPad: true);
        hintText.fontSize = 17f;
        hintText.color = new Color(1f, 1f, 1f, 0.45f);
        // CreateText stretches the rect to the row's full width and leaves TMP
        // at NoWrap. Enable wrapping and inset the right edge by 250 (the same
        // gutter BackGround() rows use) so the copy wraps instead of running off
        // the row, and sits off the right edge like the other tabs.
        hintText.textWrappingMode = TextWrappingModes.Normal;
        hintText.rectTransform.offsetMax = new Vector2(-250f, 0f);
        GenerateUI.Localize(
            hintText,
            "IMPORT_HINT",
            "Pull your settings in from another ADOFAI mod. Quartz reads each supported mod " +
            "loaded through Unity Mod Manager and copies over what it has a home for. Your other settings are left untouched."
        );

        var topRow = GenerateUI.Row(content.transform);
        UIButton rescanBtn = GenerateUI.Button(topRow, RebuildList, "Rescan", "import_rescan").SetSecondary();
        {
            var br = rescanBtn.Rect;
            br.pivot = new(1f, 1f);
            br.anchorMin = new(1f, 1f);
            br.anchorMax = new(1f, 1f);
            br.sizeDelta = new(160f, 50f);
            br.offsetMax = Vector2.zero;
        }
        rescanBtn.Rect.AddToolTip("DESC_IMPORT_RESCAN", "Re-scan for supported mods loaded through Unity Mod Manager.");

        var statusRow = GenerateUI.Row(content.transform, 32f);
        statusText = GenerateUI.AddText(statusRow, noPad: true);
        statusText.fontSize = 18f;
        statusText.color = new Color(1f, 1f, 1f, 0.45f);
        statusText.text = "";

        GameObject list = new("Mods");
        list.transform.SetParent(content.transform, false);

        listContainer = list.AddComponent<RectTransform>();

        VerticalLayoutGroup listLayout = list.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 16f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        ContentSizeFitter listFitter = list.AddComponent<ContentSizeFitter>();
        listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RebuildList();

        // The hint is built before content's layout runs, so its TMP can cache a
        // one-line mesh at a stale (full) width and never re-wrap. Force the
        // layout now so every row gets its real width and the text re-wraps —
        // same trick DropDown uses after building its popup.
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
    }

    private static string Tr(string key, string def) => MainCore.Tr.Get(key, def);

    private static void FixWidth(UIButton button, float width) {
        LayoutElement le = button.Rect.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        le.flexibleWidth = 0f;
    }

    private static void RebuildList() {
        if(listContainer == null) {
            return;
        }

        for(int i = listContainer.childCount - 1; i >= 0; i--) {
            Object.Destroy(listContainer.GetChild(i).gameObject);
        }

        List<SettingsImportOption> options = SettingsImporter.GetAvailableOptions();
        if(options.Count == 0) {
            var emptyRow = GenerateUI.Row(listContainer, 96f);
            var emptyText = GenerateUI.AddText(emptyRow, noPad: true);
            emptyText.fontSize = 18f;
            emptyText.color = new Color(1f, 1f, 1f, 0.6f);
            emptyText.textWrappingMode = TextWrappingModes.Normal;
            emptyText.rectTransform.offsetMax = new Vector2(-250f, 0f);
            GenerateUI.Localize(
                emptyText,
                "IMPORT_NONE",
                "No supported mods detected. Load one through Unity Mod Manager — KorenResourcePack (v1), " +
                "JipperResourcePack, JipperKeyViewer, ADOFAI Tweaks, KeyboardChatterBlocker, or Enhanced Effect Remover — then press Rescan."
            );
            return;
        }

        foreach(SettingsImportOption option in options) {
            CreateOptionCard(option);
        }
    }

    private static void CreateOptionCard(SettingsImportOption option) {
        // Title row: mod name on the left, Import on the right.
        var row = GenerateUI.Row(listContainer, 50f);

        HorizontalLayoutGroup rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 12f;
        rowLayout.padding = new RectOffset(16, 12, 0, 0);
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;

        var label = GenerateUI.AddText(row, noPad: true);
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        label.text = option.Label;

        UIButton importBtn = GenerateUI.Button(row, () => RunImport(option), "Import", "import_do");
        FixWidth(importBtn, 140f);
        importBtn.Rect.AddToolTip(
            "DESC_IMPORT_DO",
            "Copy this mod's settings into Quartz. Settings it doesn't cover are left as they are."
        );

        if(!SettingsImporter.HasKeyViewerPayload(option.Source)) {
            return;
        }

        // KeyViewer replace mode + (for "Replace certain") the group toggles.
        SettingsImportReplaceMode mode = modes.TryGetValue(option.OptionId, out var m) ? m : SettingsImportReplaceMode.ReplaceAll;

        var modeHeaderRow = GenerateUI.Row(listContainer, 30f);
        var modeHeader = GenerateUI.AddText(modeHeaderRow, noPad: true);
        modeHeader.fontSize = 16f;
        modeHeader.color = new Color(1f, 1f, 1f, 0.55f);
        GenerateUI.Localize(modeHeader, "IMPORT_KV_MODE", "KeyViewer import");

        IReadOnlyList<SettingsImportReplaceMode> modeValues = new[] {
            SettingsImportReplaceMode.ReplaceAll,
            SettingsImportReplaceMode.ReplaceCertain,
            SettingsImportReplaceMode.KeepOld,
        };

        var modeRow = GenerateUI.Row(listContainer);
        GenerateUI.DropDown(
            modeRow,
            SettingsImportReplaceMode.ReplaceAll,
            mode,
            modeValues,
            ModeLabel,
            chosen => {
                modes[option.OptionId] = chosen;
                RebuildList();
            },
            "import_mode_" + option.OptionId
        );

        if(mode != SettingsImportReplaceMode.ReplaceCertain) {
            return;
        }

        SettingsImportKeyViewerPart selected = parts.TryGetValue(option.OptionId, out var p) ? p : SettingsImportKeyViewerPart.All;

        foreach((SettingsImportKeyViewerPart flag, string id, string def) in PartDefs) {
            GenerateUI.Toggle(
                listContainer,
                true,
                (selected & flag) != 0,
                on => {
                    SettingsImportKeyViewerPart cur = parts.TryGetValue(option.OptionId, out var cp)
                        ? cp
                        : SettingsImportKeyViewerPart.All;
                    cur = on ? cur | flag : cur & ~flag;
                    parts[option.OptionId] = cur;
                },
                def,
                id
            );
        }
    }

    private static string ModeLabel(SettingsImportReplaceMode mode) => mode switch {
        SettingsImportReplaceMode.ReplaceAll => Tr("IMPORT_MODE_REPLACE_ALL", "Replace all"),
        SettingsImportReplaceMode.ReplaceCertain => Tr("IMPORT_MODE_REPLACE_CERTAIN", "Replace certain"),
        _ => Tr("IMPORT_MODE_KEEP_OLD", "Keep old"),
    };

    private static void RunImport(SettingsImportOption option) {
        SettingsImportReplaceMode mode = modes.TryGetValue(option.OptionId, out var m) ? m : SettingsImportReplaceMode.ReplaceAll;
        SettingsImportKeyViewerPart p = parts.TryGetValue(option.OptionId, out var pp) ? pp : SettingsImportKeyViewerPart.All;

        SettingsImportResult result = SettingsImporter.Import(option, mode, p);

        if(!result.Success) {
            statusText.text = string.Format(Tr("IMPORT_FAIL", "Import failed: {0}"), result.Message);
            return;
        }

        statusText.text = result.ImportedCount > 0
            ? string.Format(Tr("IMPORT_OK", "Imported {0} settings from {1}."), result.ImportedCount, option.Label)
            : string.Format(Tr("IMPORT_OK_NONE", "Nothing to import from {0}."), option.Label);
    }
}
