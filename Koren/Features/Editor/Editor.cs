using Koren.Compat.Interface;
using Koren.Core;
using Koren.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ADOFAI;
using Object = UnityEngine.Object;

namespace Koren.Features.Editor;

// Editor tab feature. Tweaks that target A Dance of Fire and Ice's level
// editor. Currently: "Horizontal Properties" — each inspector property renders
// as "label [field]" on one row instead of the stock "label above field".
//
// Approach (adapted from the original KorenResourcePack's EditorUI): the editor
// instantiates every property row from one shared template, ADOBase.gc
// .prefab_property, whose VerticalLayoutGroup is what stacks the label over the
// control. Swapping that group for a HorizontalLayoutGroup — with child width
// control + force-expand on, so the label and control each take half the row and
// neither collapses to zero — flips the whole inspector to a single-row layout,
// and the layout group keeps the field sized to the row as the panel is resized.
// Because the change lives on the template, every rebuilt row picks it up; we
// rebuild the open inspector so it applies immediately, and the original group
// is captured so it can be restored exactly.
//
// A per-frame reconcile (registered as a runtime tick) keeps the template in
// sync with the toggle even before the editor/game controller exists at startup.
public static partial class EditorFeature {
    public static SettingsFile<EditorSettings> ConfMgr { get; private set; }
    public static EditorSettings Conf => ConfMgr?.Data;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<EditorSettings>(
            Path.Combine(MainCore.Paths.RootPath, "Editor.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    private static bool Enabled {
        get {
            EnsureConf();
            return MainCore.IsModEnabled;
        }
    }

    internal static bool ShouldUseHorizontalProperties => Enabled && Conf.HorizontalProperties;

    // Runtime tick: re-checked every frame, but only acts when the desired state
    // differs from what's applied (i.e. on toggle, mod enable/disable, or once
    // the game controller finally exists).
    public static readonly IRuntimeTick Ticker = new TickImpl();

    private sealed class TickImpl : IRuntimeTick {
        public void Tick() {
            Reconcile();
            ReconcileAngleLabels();
        }
    }

    // Immediate path for the settings toggle.
    public static void Apply() {
        Reconcile();
        ReconcileAngleLabels();
    }

    // Hard revert, used when the mod is disabled / torn down so the shared
    // template doesn't stay flipped after the mod stops running.
    public static void Restore() {
        if(applied) {
            DisableHorizontal();
        }
        ClearAngleLabels();
    }

    private static bool applied;
    private static bool captured;
    private static LayoutSnapshot snapshot;

    private struct LayoutSnapshot {
        public RectOffset padding;
        public float spacing;
        public TextAnchor alignment;
        public bool controlWidth, controlHeight, expandWidth, expandHeight;
    }

    // A property's label (TMP, best-fit) and its control share the row evenly.
    // A text property (Song / Artist) whose value is a long rich-text string
    // reports a huge preferred width on its control, which eats the whole row
    // in the preferred-width pass and squeezes the label column to a few px —
    // the best-fit caption then shrinks to fit. So on the shared template cap
    // the control's preferred width to 0 (it still force-expands to its half)
    // and give the label a readable minimum width. Both are restored on revert.
    private const float LabelMinWidth = 140f;

    private struct LeSnapshot {
        public LayoutElement le;
        public bool created;
        public float min, pref, flex;
    }

    private static LeSnapshot labelLe;
    private static LeSnapshot controlLe;

    private static LeSnapshot ApplyLe(GameObject go, float min, float pref, float flex) {
        LeSnapshot s = default;
        if(go == null) {
            return s;
        }

        LayoutElement le = go.GetComponent<LayoutElement>();
        if(le == null) {
            le = go.AddComponent<LayoutElement>();
            s.created = true;
        } else {
            s.min = le.minWidth;
            s.pref = le.preferredWidth;
            s.flex = le.flexibleWidth;
        }

        s.le = le;
        le.minWidth = min;
        le.preferredWidth = pref;
        le.flexibleWidth = flex;
        return s;
    }

    private static void RestoreLe(ref LeSnapshot s) {
        if(s.le != null) {
            if(s.created) {
                Object.DestroyImmediate(s.le);
            } else {
                s.le.minWidth = s.min;
                s.le.preferredWidth = s.pref;
                s.le.flexibleWidth = s.flex;
            }
        }
        s = default;
    }

    private static void Reconcile() {
        bool want;
        try { want = ShouldUseHorizontalProperties; }
        catch { return; }

        try {
            if(want && !applied) {
                EnableHorizontal();
            } else if(!want && applied) {
                DisableHorizontal();
            }
        } catch {
            // Never let a layout hiccup spam every frame — applied is set before
            // the rebuild, so a throw here won't loop.
        }
    }

    private static void EnableHorizontal() {
        GameObject template = ADOBase.gc?.prefab_property;
        if(template == null) {
            return; // controller not up yet; a later tick retries.
        }

        VerticalLayoutGroup vertical = template.GetComponent<VerticalLayoutGroup>();
        if(vertical != null) {
            if(!captured) {
                snapshot = new LayoutSnapshot {
                    padding = vertical.padding,
                    spacing = vertical.spacing,
                    alignment = vertical.childAlignment,
                    controlWidth = vertical.childControlWidth,
                    controlHeight = vertical.childControlHeight,
                    expandWidth = vertical.childForceExpandWidth,
                    expandHeight = vertical.childForceExpandHeight,
                };
                captured = true;
            }
            Object.DestroyImmediate(vertical);
        }

        HorizontalLayoutGroup horizontal = template.GetComponent<HorizontalLayoutGroup>()
            ?? template.AddComponent<HorizontalLayoutGroup>();

        if(captured) {
            horizontal.padding = snapshot.padding;
            horizontal.spacing = snapshot.spacing;
            horizontal.childAlignment = snapshot.alignment;
        }

        // The label and control carry no LayoutElement of their own, so the group
        // has to size them — otherwise they collapse to zero width and vanish.
        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = true;
        horizontal.childForceExpandHeight = true;

        // Keep the label readable on long-content (text) properties — see the
        // LeSnapshot note above.
        Property prop = template.GetComponent<Property>();
        if(prop != null) {
            labelLe = ApplyLe(prop.label != null ? prop.label.gameObject : null, LabelMinWidth, -1f, 1f);
            controlLe = ApplyLe(prop.controlContainer != null ? prop.controlContainer.gameObject : null, 0f, 0f, 1f);
        }

        applied = true;
        RebuildInspector();
    }

    private static void DisableHorizontal() {
        GameObject template = ADOBase.gc?.prefab_property;
        if(template != null) {
            HorizontalLayoutGroup horizontal = template.GetComponent<HorizontalLayoutGroup>();
            if(horizontal != null) {
                Object.DestroyImmediate(horizontal);
            }

            RestoreLe(ref labelLe);
            RestoreLe(ref controlLe);

            if(captured) {
                VerticalLayoutGroup vertical = template.GetComponent<VerticalLayoutGroup>()
                    ?? template.AddComponent<VerticalLayoutGroup>();
                vertical.padding = snapshot.padding;
                vertical.spacing = snapshot.spacing;
                vertical.childAlignment = snapshot.alignment;
                vertical.childControlWidth = snapshot.controlWidth;
                vertical.childControlHeight = snapshot.controlHeight;
                vertical.childForceExpandWidth = snapshot.expandWidth;
                vertical.childForceExpandHeight = snapshot.expandHeight;
            }

            applied = false;
            RebuildInspector();
        } else {
            applied = false;
        }
    }

    // Tear down and re-create the inspector's property panels so they re-
    // instantiate from the (now re-laid-out) template. No-op outside the editor.
    private static void RebuildInspector() {
        try {
            scnEditor editor = scnEditor.instance;
            if(editor == null) {
                return;
            }

            InspectorPanel settings = editor.settingsPanel;
            InspectorPanel events = editor.levelEventsPanel;
            if(settings == null || events == null) {
                return;
            }

            RebuildPanel(settings, GCS.settingsInfo, isLevelEvents: false);
            RebuildPanel(events, GCS.levelEventsInfo, isLevelEvents: true);

            // The teardown dropped the visible selection — put it back.
            settings.ShowPanel(settings.selectedEventType, events.cacheEventIndex);
            events.HideAllInspectorTabs();
            events.ShowInspector(false, false);
        } catch {
        }
    }

    private static void RebuildPanel(
        InspectorPanel panel,
        Dictionary<string, LevelEventInfo> infos,
        bool isLevelEvents
    ) {
        if(panel.panelsList != null) {
            foreach(PropertiesPanel built in panel.panelsList) {
                if(built != null) {
                    Object.DestroyImmediate(built.gameObject);
                }
            }
        }

        RectTransform tabs = panel.tabs;
        if(tabs != null) {
            for(int i = tabs.childCount - 1; i >= 0; i--) {
                Object.DestroyImmediate(tabs.GetChild(i).gameObject);
            }
        }

        panel.Init(infos, isLevelEvents);
    }
}
