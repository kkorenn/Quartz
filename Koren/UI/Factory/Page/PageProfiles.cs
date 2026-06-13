using Koren.Async;
using Koren.Core;
using Koren.IO;
using Koren.Localization;
using Koren.Resource;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;
using UnityFileDialog;
using Object = UnityEngine.Object;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI.Factory.Page;

// Profiles tab. Lists the settings profiles, lets the user add one from the
// current settings, switch between them, and move them in/out of the install
// as single .krprofile files (so a manual update that wipes UserData doesn't
// take the settings with it). File pickers go through the game's bundled
// UnityFileDialog.
internal static class PageProfiles {
    private static RectTransform listContainer;
    private static TextMeshProUGUI statusText;
    private static UIInput nameInput;
    private static string pendingName = "";

    public static void Create(RectTransform parent) {
        pendingName = "";

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

        var headerRow = GenerateUI.Row(content.transform);
        var headerText = GenerateUI.AddTextH1(headerRow);
        headerText.gameObject.AddComponent<TextLocalization>().Init("PROFILES", "Profiles");

        var hintRow = GenerateUI.Row(content.transform, 54f);
        var hintText = GenerateUI.AddText(hintRow, noPad: true);
        hintText.fontSize = 17f;
        hintText.color = new Color(1f, 1f, 1f, 0.45f);
        hintText.gameObject.AddComponent<TextLocalization>().Init(
            "PROFILE_HINT",
            "Every setting lives in the active profile. Export a profile before updating the mod manually, then import it back if your UserData gets replaced."
        );

        // Name input with the Add button in the right-hand gutter the
        // BackGround() widgets leave free (same pattern as the language row
        // on the Settings page).
        var addRow = GenerateUI.Row(content.transform);
        nameInput = GenerateUI.Input(
            addRow,
            null,
            null,
            value => pendingName = value,
            "Profile Name",
            MainCore.Spr.Get(UISprite.Users128),
            "profile_name"
        );
        nameInput.Placeholder.gameObject.AddComponent<TextLocalization>().Init("PROFILE_NAME", "Profile Name");
        nameInput.InputField.characterLimit = 32;

        UIButton addBtn = GenerateUI.Button(
            addRow,
            AddProfile,
            "Add Profile",
            "profile_add"
        );
        {
            var br = addBtn.Rect;
            br.pivot = new(1f, 1f);
            br.anchorMin = new(1f, 1f);
            br.anchorMax = new(1f, 1f);
            br.sizeDelta = new(160f, 50f);
            br.offsetMax = Vector2.zero;
        }
        addBtn.Rect.AddToolTip(
            "DESC_PROFILE_ADD",
            "Creates a new profile from your current settings and switches to it."
        );

        var ioRow = GenerateUI.Row(content.transform);
        HorizontalLayoutGroup ioLayout = ioRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        ioLayout.spacing = 12f;
        ioLayout.padding = new RectOffset(16, 12, 0, 0);
        ioLayout.childControlWidth = true;
        ioLayout.childControlHeight = true;
        ioLayout.childForceExpandWidth = false;
        ioLayout.childForceExpandHeight = true;
        ioLayout.childAlignment = TextAnchor.MiddleLeft;

        UIButton importBtn = GenerateUI.Button(
            ioRow,
            ImportProfile,
            "Import",
            "profile_import"
        );
        FixWidth(importBtn, 160f);
        importBtn.Rect.AddToolTip(
            "DESC_PROFILE_IMPORT",
            "Loads a .krprofile file as a new profile. It won't be selected automatically."
        );

        UIButton folderBtn = GenerateUI.Button(
            ioRow,
            () => {
                try {
                    Directory.CreateDirectory(ProfileManager.ProfilesPath);
                    FileBrowser.Reveal(ProfileManager.ProfilesPath);
                } catch(Exception e) {
                    MainCore.Log.Err($"[{nameof(PageProfiles)}] Reveal failed: {e}");
                }
            },
            "Open Folder",
            "profile_open_folder"
        ).SetSecondary();
        FixWidth(folderBtn, 160f);

        var statusRow = GenerateUI.Row(content.transform, 32f);
        statusText = GenerateUI.AddText(statusRow, noPad: true);
        statusText.fontSize = 18f;
        statusText.color = new Color(1f, 1f, 1f, 0.45f);
        statusText.text = "";

        GameObject list = new("Profiles");
        list.transform.SetParent(content.transform, false);

        listContainer = list.AddComponent<RectTransform>();

        VerticalLayoutGroup listLayout = list.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 8f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        ContentSizeFitter listFitter = list.AddComponent<ContentSizeFitter>();
        listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RebuildList();
    }

    private static string Tr(string key, string def) => MainCore.Tr.Get(key, def);

    private static void FixWidth(UIButton button, float width) {
        LayoutElement le = button.Rect.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        le.flexibleWidth = 0f;
    }

    private static void AddProfile() {
        string name = ProfileManager.Sanitize(pendingName);

        if(name == null) {
            statusText.text = Tr("PROFILE_STATUS_NAME_INVALID", "Enter a name first.");
            return;
        }

        if(ProfileManager.Exists(name)) {
            statusText.text = Tr("PROFILE_STATUS_NAME_TAKEN", "That name is already used.");
            return;
        }

        if(ProfileManager.Create(name)) {
            pendingName = "";
            nameInput.Set("", false);
            statusText.text = "";
            RebuildList();
        }
    }

    private static void ImportProfile() {
        string path;

        try {
            path = FileBrowser.PickFile(
                null,
                "Koren Profile",
                [ProfileManager.EXPORT_EXTENSION, "json"],
                Tr("PROFILE_IMPORT_TITLE", "Import Koren Profile")
            );
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(PageProfiles)}] PickFile failed: {e}");
            return;
        }

        if(string.IsNullOrEmpty(path)) {
            return;
        }

        string name = ProfileManager.Import(path);

        if(name == null) {
            statusText.text = Tr("PROFILE_STATUS_IMPORT_FAILED", "Import failed — not a Koren profile file.");
            return;
        }

        statusText.text = string.Format(Tr("PROFILE_STATUS_IMPORTED", "Imported as '{0}'."), name);
        RebuildList();
    }

    private static void ExportProfile(string name) {
        string path;

        try {
            path = FileBrowser.SaveFile(
                null,
                $"{name}.{ProfileManager.EXPORT_EXTENSION}",
                "Koren Profile",
                [ProfileManager.EXPORT_EXTENSION],
                Tr("PROFILE_EXPORT_TITLE", "Export Koren Profile")
            );
        } catch(Exception e) {
            MainCore.Log.Err($"[{nameof(PageProfiles)}] SaveFile failed: {e}");
            return;
        }

        if(string.IsNullOrEmpty(path)) {
            return;
        }

        if(!path.EndsWith($".{ProfileManager.EXPORT_EXTENSION}", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
            path += $".{ProfileManager.EXPORT_EXTENSION}";
        }

        statusText.text = ProfileManager.Export(name, path)
            ? string.Format(Tr("PROFILE_STATUS_EXPORTED", "Exported '{0}'."), Path.GetFileName(path))
            : Tr("PROFILE_STATUS_EXPORT_FAILED", "Export failed.");
    }

    private static void SelectProfile(string name, UIButton button) {
        button.SetBlocked(true);

        // Applying tears the whole settings UI down and rebuilds it — run
        // outside the click dispatch that lives on the object being destroyed.
        MainThread.Enqueue(() => {
            if(ProfileManager.Apply(name)) {
                UICore.Rebuild();
            } else {
                RebuildList();
            }
        });
    }

    private static void RebuildList() {
        if(listContainer == null) {
            return;
        }

        for(int i = listContainer.childCount - 1; i >= 0; i--) {
            Object.Destroy(listContainer.GetChild(i).gameObject);
        }

        foreach(string name in ProfileManager.List()) {
            CreateProfileRow(name, name == ProfileManager.Active);
        }
    }

    private static void CreateProfileRow(string name, bool active) {
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

        LayoutElement labelLe = label.gameObject.AddComponent<LayoutElement>();
        labelLe.flexibleWidth = 1f;

        if(active) {
            string accent = ColorUtility.ToHtmlStringRGB(UIColors.ObjectActiveBright);
            label.text = $"{name}  <size=70%><color=#{accent}>●  {Tr("PROFILE_ACTIVE", "Active")}</color></size>";
        } else {
            label.text = name;
        }

        if(!active) {
            UIButton selectBtn = null;
            selectBtn = GenerateUI.Button(
                row,
                () => SelectProfile(name, selectBtn),
                "Select",
                "profile_select"
            );
            FixWidth(selectBtn, 110f);
        }

        UIButton exportBtn = GenerateUI.Button(
            row,
            () => ExportProfile(name),
            "Export",
            "profile_export"
        ).SetSecondary();
        FixWidth(exportBtn, 110f);

        if(!active) {
            // Two-step delete: the first click arms the button (red "Sure?"),
            // the second one actually deletes.
            bool armed = false;
            UIButton deleteBtn = null;
            deleteBtn = GenerateUI.Button(
                row,
                () => {
                    if(!armed) {
                        armed = true;
                        deleteBtn.Label.text = Tr("PROFILE_DELETE_CONFIRM", "Sure?");
                        deleteBtn.RestColor = static () => UIColors.SoftRed;
                        deleteBtn.Background.color = UIColors.SoftRed;
                        return;
                    }

                    if(ProfileManager.Delete(name)) {
                        statusText.text = "";
                        RebuildList();
                    }
                },
                "Delete",
                "profile_delete"
            ).SetSecondary();
            FixWidth(deleteBtn, 110f);
        }
    }
}
