using Koren.UI.Factory.Page;
using Koren.UI.Generator;
using UnityEngine;

namespace Koren.UI.Factory;

public static class PageFactory {
    public static RectTransform PagesContaner;
    public static RectTransform CreatePages(GameObject panel) {
        // Sections registered by a previous build of the pages are stale.
        GenerateUI.ClearSections();

        GameObject pagesContainer = new("PagesContainer");
        pagesContainer.transform.SetParent(panel.transform, false);

        PagesContaner = pagesContainer.AddComponent<RectTransform>();

        PagesContaner.anchorMin = new Vector2(0, 0);
        PagesContaner.anchorMax = new Vector2(1, 1);
        PagesContaner.pivot = new Vector2(0.5f, 0.5f);

        PagesContaner.offsetMin = Vector2.zero;
        PagesContaner.offsetMax = new Vector2(0, -60);

        for(int i = 0; i < Enum.GetValues(typeof(OriginalMenuState)).Length; i++) {
            CreatePageBase(i);
        }

        UICore.Pages[UICore.CurrentMenuState].GetComponent<CanvasGroup>().alpha = 1f;
        UICore.Pages[UICore.CurrentMenuState].GetComponent<CanvasGroup>().interactable = true;
        UICore.Pages[UICore.CurrentMenuState].GetComponent<CanvasGroup>().blocksRaycasts = true;

        PageCredits.Create(UICore.Pages[(int)OriginalMenuState.Credits]);
        PageProfiles.Create(UICore.Pages[(int)OriginalMenuState.Profiles]);
        PageSettings.Create(UICore.Pages[(int)OriginalMenuState.Settings]);
        PageOverlay.Create(UICore.Pages[(int)OriginalMenuState.Overlay]);
        PageGameplay.Create(UICore.Pages[(int)OriginalMenuState.Gameplay]);
        PageVisuals.Create(UICore.Pages[(int)OriginalMenuState.Visuals]);
        PageTweaks.Create(UICore.Pages[(int)OriginalMenuState.Tweaks]);
        PageSearch.Create(UICore.Pages[(int)OriginalMenuState.Search]);

        // Developer page — only populated in "dev" builds (its tab is likewise
        // only created then).
        if(Koren.Core.Info.IsDev) {
            PageDeveloper.Create(UICore.Pages[(int)OriginalMenuState.Developer]);
        }

        return PagesContaner;
    }

    public static RectTransform CreatePageBase(int num) {
        GameObject obj = new($"Page{num}");
        obj.transform.SetParent(PagesContaner, false);

        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);

        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        CanvasGroup cg = obj.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;

        UICore.Pages[num] = rt;

        return rt;
    }
}
