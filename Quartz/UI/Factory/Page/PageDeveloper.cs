using Quartz.Core;
using Quartz.UI.Generator;
using Quartz.UI.Utility;
using Quartz.Update;
using UnityEngine;
using UnityEngine.UI;

using TMPro;

namespace Quartz.UI.Factory.Page;

// Developer tools. Only built in "dev" builds — MenuFactory and PageFactory
// both gate on Info.IsDev, so this never appears in alpha/beta/rc/stable.
internal static class PageDeveloper {
    private static TextMeshProUGUI statusText;
    private static bool hooked;

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

        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(content.transform)), "DEVELOPER", "Developer");

        // Force the updater to offer an update for the current version. Install
        // goes through the motions but changes no files.
        GenerateUI.Toggle(
            GenerateUI.Row(content.transform),
            false,
            UpdateService.DevSimulate,
            v => UpdateService.SetDevSimulate(v),
            "Simulate Update Available",
            "dev_sim_update"
        );

        GenerateUI.Button(
            GenerateUI.Row(content.transform),
            () => UpdateService.Check(),
            "Run Update Check",
            "dev_check"
        );

        GenerateUI.Button(
            GenerateUI.Row(content.transform),
            () => {
                MainCore.Conf.SkippedVersion = "";
                MainCore.ConfMgr.RequestSave();
                RefreshStatus();
            },
            "Clear Skipped Version",
            "dev_clear_skip"
        );

        GenerateUI.Button(
            GenerateUI.Row(content.transform),
            RefreshStatus,
            "Refresh Status",
            "dev_refresh"
        );

        GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(content.transform)), "STATUS", "Status");

        statusText = GenerateUI.AddText(GenerateUI.Row(content.transform, 320f));
        statusText.alignment = TextAlignmentOptions.TopLeft;
        statusText.enableWordWrapping = true;

        if(!hooked) {
            UpdateService.OnChanged += RefreshStatus;
            hooked = true;
        }
        RefreshStatus();
    }

    internal static void RefreshStatus() {
        if(statusText == null) {
            return;
        }

        UpdateInfo available = UpdateService.Available;
        string skipped = string.IsNullOrEmpty(MainCore.Conf.SkippedVersion)
            ? "none"
            : MainCore.Conf.SkippedVersion;

        statusText.text = string.Join("\n", new[] {
            $"Version:         v{Info.DisplayVersion}",
            $"Channel:         {Info.ChannelKind}",
            $"Mod enabled:     {MainCore.IsModEnabled}",
            $"Update channel:  {MainCore.Conf.GetUpdateChannel()}",
            $"Update status:   {UpdateService.Status}",
            $"Failure:         {UpdateService.Failure}",
            $"Progress:        {(UpdateService.Progress < 0f ? "n/a" : UpdateService.Progress.ToString("P0"))}",
            $"Available:       {(available == null ? "none" : available.Tag)}",
            $"Skipped version: {skipped}",
            $"Simulate update: {UpdateService.DevSimulate}",
            $"Repo:            {Info.RepoOwner}/{Info.RepoName}",
            $"Last message:    {UpdateService.Message}",
        });
    }
}
