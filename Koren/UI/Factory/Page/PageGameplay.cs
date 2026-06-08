using Koren.Localization;
using Koren.UI.Generator;
using UnityEngine;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI.Factory.Page;

// Placeholder for the upcoming Gameplay tab — empty for now.
internal static class PageGameplay {
    public static void Create(RectTransform parent) {
        var text = GenerateUI.AddTextH1(parent);
        text.text = "Coming soon";
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(1f, 1f, 1f, 0.4f);
        text.gameObject.AddComponent<TextLocalization>().Init("COMING_SOON", "Coming soon");
    }
}
