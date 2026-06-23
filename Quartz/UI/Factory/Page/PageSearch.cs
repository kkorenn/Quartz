using GTweens.Builders;
using GTweens.Easings;
using GTweens.Extensions;
using Quartz.Core;
using Quartz.Localization;
using Quartz.Resource;
using Quartz.UI.Generator;
using Quartz.UI.Utility;
using Quartz.Utility;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

using TMPro;

namespace Quartz.UI.Factory.Page;

// Global search tab. Indexes the live UI of every other tab — collapsible
// categories, toggles, buttons, sliders, dropdowns, section headings — by
// walking the page hierarchies at query time (so it always reflects the
// current language and any dynamically built rows). Clicking a result jumps
// to its tab, snaps open any collapsed sections hiding it, scrolls it into
// view and flashes it.
internal static class PageSearch {
    private sealed class Entry {
        public int State;
        public string Text;
        public string Section;
        public RectTransform Target;
        public bool IsCategory;
    }

    private const int MAX_RESULTS = 40;

    private static RectTransform resultsContainer;
    private static TextMeshProUGUI statusText;

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

        var inputRow = GenerateUI.Row(content.transform);
        var input = GenerateUI.Input(
            inputRow,
            "",
            "",
            RunSearch,
            "Search",
            MainCore.Spr.Get(UISprite.MagnifyingGlass128),
            "search_query"
        );
        input.Placeholder.gameObject.AddComponent<TextLocalization>().Init("SEARCH", "Search");
        input.InputField.characterLimit = 40;

        var statusRow = GenerateUI.Row(content.transform, 32f);
        statusText = GenerateUI.AddText(statusRow);
        statusText.color = new Color(1f, 1f, 1f, 0.45f);
        statusText.fontSize = 18f;

        GameObject results = new("Results");
        results.transform.SetParent(content.transform, false);

        resultsContainer = results.AddComponent<RectTransform>();

        VerticalLayoutGroup resultsLayout = results.AddComponent<VerticalLayoutGroup>();
        resultsLayout.spacing = 8f;
        resultsLayout.childControlWidth = true;
        resultsLayout.childControlHeight = true;
        resultsLayout.childForceExpandWidth = true;
        resultsLayout.childForceExpandHeight = false;

        ContentSizeFitter resultsFitter = results.AddComponent<ContentSizeFitter>();
        resultsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RunSearch("");
    }

    private static string Tr(string key, string def) => MainCore.Tr.Get(key, def);

    // Normalize for matching; in Korean also collapse syllables to 초성 so
    // consonant-only queries (e.g. "ㅈㄷ" for "진동") match.
    private static string Norm(string input) {
        string normalized = StringUtils.Normalize(input);

        if(MainCore.Conf.Language == "ko-KR" && !string.IsNullOrEmpty(normalized)) {
            normalized = StringUtils.NormalizeToHangulChosung(normalized);
        }

        return normalized;
    }

    private static string TabName(int state) => (OriginalMenuState)state switch {
        OriginalMenuState.Overlay => Tr("OVERLAY", "Overlay"),
        OriginalMenuState.Gameplay => Tr("GAMEPLAY", "Gameplay"),
        OriginalMenuState.Visuals => Tr("VISUALS", "Visuals"),
        OriginalMenuState.Tweaks => Tr("TWEAKS", "Tweaks"),
        OriginalMenuState.Profiles => Tr("PROFILES", "Profiles"),
        OriginalMenuState.Import => Tr("IMPORT", "Import"),
        OriginalMenuState.Settings => Tr("SETTINGS", "Settings"),
        OriginalMenuState.Credits => Tr("CREDITS", "Credits"),
        OriginalMenuState.Editor => Tr("EDITOR", "Editor"),
        OriginalMenuState.Developer => Tr("DEVELOPER", "Developer"),
        _ => "?",
    };

    private static void RunSearch(string query) {
        if(resultsContainer == null) {
            return;
        }

        for(int i = resultsContainer.childCount - 1; i >= 0; i--) {
            Object.Destroy(resultsContainer.GetChild(i).gameObject);
        }

        string q = Norm(query ?? "");

        if(string.IsNullOrWhiteSpace(q)) {
            statusText.text = Tr("SEARCH_HINT", "Type to search every tab — categories, toggles, buttons, everything.");
            return;
        }

        List<Entry> matches = [];
        foreach(Entry e in BuildIndex()) {
            if(Norm(e.Text).Contains(q)) {
                matches.Add(e);
            }
        }

        // Prefix matches first, categories before plain rows, then A-Z.
        matches.Sort((a, b) => {
            bool ap = Norm(a.Text).StartsWith(q);
            bool bp = Norm(b.Text).StartsWith(q);
            if(ap != bp) {
                return ap ? -1 : 1;
            }
            if(a.IsCategory != b.IsCategory) {
                return a.IsCategory ? -1 : 1;
            }
            return string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
        });

        if(matches.Count == 0) {
            statusText.text = Tr("SEARCH_NO_RESULTS", "No results.");
            return;
        }

        int shown = Mathf.Min(matches.Count, MAX_RESULTS);
        statusText.text = matches.Count > MAX_RESULTS
            ? string.Format(Tr("SEARCH_RESULTS_CAPPED", "{0} results (showing first {1})"), matches.Count, MAX_RESULTS)
            : string.Format(Tr("SEARCH_RESULTS", "{0} result(s)"), matches.Count);

        for(int i = 0; i < shown; i++) {
            AddResultRow(matches[i]);
        }
    }

    private static void AddResultRow(Entry e) {
        var row = GenerateUI.Row(resultsContainer);

        string text = e.Text.Length > 60 ? e.Text[..57] + "…" : e.Text;
        string path = string.IsNullOrEmpty(e.Section)
            ? TabName(e.State)
            : $"{TabName(e.State)} › {e.Section}";

        var btn = GenerateUI.Button(
            row,
            () => Navigate(e),
            // TMP has no </alpha> closing tag — alpha applies until the next
            // alpha tag, so reset to opaque explicitly.
            $"<alpha=#77>{path} ›<alpha=#FF> {text}",
            "search_result"
        ).SetSecondary();
        btn.Label.overflowMode = TextOverflowModes.Ellipsis;
        btn.Label.fontSize = 19f;
    }

    // Index of every visible-text UI element on the other tabs, rebuilt per
    // query. Walking the live hierarchy (inactive included — collapsed bodies
    // are deactivated) keeps it in sync with language switches and rows shown
    // or hidden dynamically.
    private static List<Entry> BuildIndex() {
        List<Entry> list = [];

        foreach(KeyValuePair<int, RectTransform> page in UICore.Pages) {
            if(page.Key == (int)OriginalMenuState.Search || page.Value == null) {
                continue;
            }

            Walk(page.Value, page.Key, null, list);
        }

        return list;
    }

    private static void Walk(Transform t, int state, string section, List<Entry> list) {
        for(int i = 0; i < t.childCount; i++) {
            Transform child = t.GetChild(i);
            string name = child.name;

            // Dropdown option lists would add one entry per option (the font
            // list alone has dozens) — the dropdown's own row already matches.
            if(name == "List") {
                continue;
            }

            if(name.StartsWith("Section_")) {
                TMP_Text headerLabel = child.Find("Header/Bar/Label")?.GetComponent<TMP_Text>();
                string title = headerLabel != null ? headerLabel.text : name["Section_".Length..];

                list.Add(new Entry {
                    State = state,
                    Text = title,
                    Section = section,
                    Target = (RectTransform)child,
                    IsCategory = true,
                });

                Transform body = child.Find("Body");
                if(body != null) {
                    Walk(body, state, title, list);
                }
                continue;
            }

            if((name == "Text" || name == "Label") && child.TryGetComponent(out TMP_Text tmp)) {
                string text = tmp.text;
                // Index only stable, translated labels — setting names, button
                // captions, headings (all carry a TextLocalization). Dynamic value
                // displays (slider numbers, dropdown selections, input contents)
                // carry none, so they're skipped: search matches what a setting IS,
                // not its current value.
                if(!string.IsNullOrWhiteSpace(text) && child.GetComponent<TextLocalization>() != null) {
                    RectTransform target = RowTarget(child, state);
                    if(target != null) {
                        list.Add(new Entry {
                            State = state,
                            Text = text,
                            Section = section,
                            Target = target,
                        });
                    }
                }
            }

            Walk(child, state, section, list);
        }
    }

    // The row-level ancestor of a label: climb until the parent is a vertical
    // layout container (page content or a collapsible body). That node is
    // what gets scrolled to and flashed.
    private static RectTransform RowTarget(Transform label, int state) {
        RectTransform page = UICore.Pages[state];
        Transform cur = label;

        while(cur.parent != null && cur != page) {
            if(cur.parent.GetComponent<VerticalLayoutGroup>() != null) {
                return cur as RectTransform;
            }
            cur = cur.parent;
        }

        return null;
    }

    private static void Navigate(Entry e) {
        if(e.Target == null) {
            return;
        }

        // Snap open every collapsed section between the page root and the
        // target (instant — the scroll math needs settled layout heights).
        // A category result also opens itself so its contents show.
        foreach(GenerateUI.CollapsibleSection s in GenerateUI.Sections) {
            if(s.Body == null) {
                continue;
            }
            if(e.Target == s.Section || e.Target.IsChildOf(s.Body)) {
                s.SetExpanded(true, false, false);
            }
        }

        MenuFactory.SetState(e.State);

        RectTransform page = UICore.Pages[e.State];
        UIScrollController scroller = page.GetComponentInChildren<UIScrollController>(true);
        if(scroller == null || scroller.content == null) {
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(scroller.content);

        Vector3 worldCenter = e.Target.TransformPoint(e.Target.rect.center);
        float localY = scroller.content.InverseTransformPoint(worldCenter).y;
        float top = -localY - (e.Target.rect.height * 0.5f);
        scroller.ScrollTo(top - 8f);

        Flash(e.Target);
    }

    // Brief accent-colored pulse over the target row so the eye lands on it.
    private static void Flash(RectTransform target) {
        GameObject flash = new("SearchFlash");
        flash.transform.SetParent(target, false);

        RectTransform rect = flash.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        // Rows stop 250 units short of the right edge, like every widget.
        rect.offsetMax = new Vector2(-250f, 0f);

        Image img = flash.AddComponent<Image>();
        img.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        img.type = Image.Type.Sliced;
        Color accent = UIColors.ObjectActive;
        img.color = new Color(accent.r, accent.g, accent.b, 0.45f);
        img.raycastTarget = false;

        var seq = GTweenSequenceBuilder.New()
            .AppendTime(0.35f)
            .Append(GTweenExtensions.Tween(
                () => img.color.a,
                a => {
                    Color c = img.color;
                    c.a = a;
                    img.color = c;
                },
                0f,
                0.8f
            ).SetEasing(Easing.OutSine))
            .AppendCallback(() => {
                if(flash != null) {
                    Object.Destroy(flash);
                }
            })
            .Build();
        MainCore.TC.Play(seq);
    }
}
