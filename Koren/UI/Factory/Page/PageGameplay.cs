using Koren.Core;
using Koren.Features.AutoDeafen;
using Koren.Features.ChatterBlocker;
using Koren.Features.KeyLimiter;
using Koren.Features.KeyViewer;
using Koren.Features.Restriction;
using Koren.Resource;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityEngine.EventSystems.PointerEventData;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI.Factory.Page;

// Gameplay tab. Hosts the input/run-rule features ported from the original
// KorenResourcePack: Key Limiter, Keyboard Chatter Blocker, Judgement
// Restriction and Death Limit.
internal static class PageGameplay {
    // Static so a page rebuild replaces (not stacks) the allowed-keys
    // listener from the previous build.
    private static Action keysChangedHandler;
    private static Action syncLockChangedHandler;

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

        CreateKeyLimiter(content.transform);
        CreateChatterBlocker(content.transform);
        CreateJudgementRestriction(content.transform);
        CreateDeathLimit(content.transform);
        CreateAutoDeafen(content.transform);
    }

    // ===== Auto Deafen (Discord) =====

    private static void CreateAutoDeafen(Transform content) {
        AutoDeafen.EnsureConf();
        AutoDeafenSettings conf = AutoDeafen.Conf;
        AutoDeafenSettings def = new();

        var sec = GenerateUI.Collapsible(
            content, "Auto Deafen (Discord)", startExpanded: false,
            v => {
                conf.Enabled = v;
                AutoDeafen.Save();
            },
            conf.Enabled
        );

        UISlider pct = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.DeafenAtPercent, 0f, 100f, conf.DeafenAtPercent,
            v => Mathf.Round(v), null, null,
            "Deafen At %",
            "ad_pct"
        );
        pct.Format = "0";
        pct.OnChanged = v => conf.DeafenAtPercent = v;
        pct.OnComplete = v => {
            conf.DeafenAtPercent = v;
            AutoDeafen.Save();
        };

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.OnlyFromStart,
            conf.OnlyFromStart,
            v => {
                conf.OnlyFromStart = v;
                AutoDeafen.Save();
            },
            "Only When Starting From 0%",
            "ad_start"
        );

        GenerateUI.Input(
            GenerateUI.Row(sec.Body),
            def.DiscordClientId,
            conf.DiscordClientId,
            v => {
                conf.DiscordClientId = v;
                AutoDeafen.Save();
            },
            "Discord Client ID",
            MainCore.Spr.Get(UISprite.Text128),
            "ad_client_id"
        );

        GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => AutoDeafen.OpenAuthorizeUrl(),
            "Authorize (Open Discord)",
            "ad_authorize"
        );

        GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => {
                string url = AutoDeafen.AuthorizeUrl();
                if(!string.IsNullOrEmpty(url)) {
                    GUIUtility.systemCopyBuffer = url;
                }
            },
            "Copy Authorize URL",
            "ad_copy_url"
        ).SetSecondary();

        GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => AutoDeafen.OpenTutorial(),
            "Watch Tutorial",
            "ad_tutorial"
        ).SetSecondary();

        GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => AutoDeafen.Unlink(),
            "Unlink",
            "ad_unlink"
        ).SetSecondary();

        var statusRow = GenerateUI.Row(sec.Body);
        var statusText = GenerateUI.AddText(statusRow);
        statusText.fontSize = 19f;
        statusText.color = new Color(1f, 1f, 1f, 0.6f);
        statusRow.gameObject.AddComponent<AutoDeafenStatusLabel>().Label = statusText;
    }

    // Live status readout ("authorized / ready / deaf"); polled because both
    // the OAuth server and the RPC client update state on background threads.
    private sealed class AutoDeafenStatusLabel : MonoBehaviour {
        public TMP_Text Label;
        private float nextPoll;

        private void Update() {
            if(Label == null || Time.unscaledTime < nextPoll) {
                return;
            }
            nextPoll = Time.unscaledTime + 0.25f;

            string text = MainCore.Tr.Get("STATUS_PREFIX", "Status: ") + AutoDeafen.Status;
            if(Label.text != text) {
                Label.text = text;
            }
        }
    }

    // ===== Key Limiter =====

    private static void CreateKeyLimiter(Transform content) {
        KeyLimiter.EnsureConf();
        KeyLimiterSettings conf = KeyLimiter.Conf;
        KeyLimiterSettings def = new();

        var sec = GenerateUI.Collapsible(
            content, "Key Limiter", startExpanded: true,
            v => {
                conf.Enabled = v;
                KeyLimiter.Save();
            },
            conf.Enabled
        );

        UIButton captureBtn = null;
        captureBtn = GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => {
                if(KeyLimiter.IsCapturing) {
                    KeyLimiter.CancelCapture();
                    return;
                }

                captureBtn.Label.text = MainCore.Tr.Get("PRESS_A_KEY", "Press a key...");
                KeyLimiter.StartCapture(
                    key => KeyLimiter.ToggleAllowedKey(key),
                    () => {
                        if(captureBtn?.Label != null) {
                            captureBtn.Label.text = MainCore.Tr.Get("KL_CAPTURE", "Add / Remove Key");
                        }
                    }
                );
            },
            "Add / Remove Key",
            "kl_capture"
        );
        captureBtn.Rect.AddToolTip(
            "DESC_KL_CAPTURE",
            "Press any key to add/remove it from the allowed list. Escape cancels."
        );

        UIButton clearBtn = GenerateUI.Button(
            GenerateUI.Row(sec.Body),
            () => KeyLimiter.ClearAllowedKeys(),
            "Clear All",
            "kl_clear"
        ).SetSecondary();

        // While the key viewer syncs its keys here, the allowed list is not
        // user-editable — the sync would overwrite any change on the next
        // rebuild anyway.
        var syncNote = GenerateUI.AddText(GenerateUI.Row(sec.Body, 30f));
        GenerateUI.Localize(
            syncNote,
            "KL_SYNC_LOCKED",
            "Keys are managed by the Key Viewer (Sync Keys to Key Limiter is on)."
        );
        syncNote.fontSize = 17f;
        syncNote.color = new Color(1f, 1f, 1f, 0.45f);

        // Allowed-keys list, rebuilt on every change — v1 KrpPages layout:
        // an "Allowed Keys" header and one Remove button per key (or a "No
        // allowed keys." note).
        GameObject list = new("AllowedKeysList");
        list.transform.SetParent(sec.Body, false);
        list.AddComponent<RectTransform>();

        VerticalLayoutGroup listLayout = list.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 6f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        ContentSizeFitter listFitter = list.AddComponent<ContentSizeFitter>();
        listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        void RebuildKeysList() {
            if(list == null) {
                return;
            }

            for(int i = list.transform.childCount - 1; i >= 0; i--) {
                Object.Destroy(list.transform.GetChild(i).gameObject);
            }

            int[] keys = KeyLimiter.Conf?.AllowedKeys ?? [];
            if(keys.Length == 0) {
                var note = GenerateUI.AddText(GenerateUI.Row(list.transform));
                GenerateUI.Localize(note, "KL_NO_ALLOWED_KEYS", "No allowed keys.");
                note.fontSize = 19f;
                note.color = new Color(1f, 1f, 1f, 0.45f);
                return;
            }

            bool locked = KeyViewerOverlay.IsSyncingToKeyLimiter;
            GenerateUI.Localize(GenerateUI.AddTextH1(GenerateUI.Row(list.transform)), "KL_ALLOWED_KEYS", "Allowed Keys");

            for(int i = 0; i < keys.Length; i++) {
                CreateKeyRow(list.transform, KeyLimiter.NormalizeKey((KeyCode)keys[i]), locked);
            }
        }

        void ApplySyncLock() {
            bool locked = KeyViewerOverlay.IsSyncingToKeyLimiter;

            if(locked && KeyLimiter.IsCapturing) {
                KeyLimiter.CancelCapture();
            }

            captureBtn.SetBlocked(locked, true);
            clearBtn.SetBlocked(locked, true);
            syncNote.gameObject.SetActive(locked);
            RebuildKeysList();
        }

        if(keysChangedHandler != null) {
            KeyLimiter.Changed -= keysChangedHandler;
        }
        keysChangedHandler = RebuildKeysList;
        KeyLimiter.Changed += keysChangedHandler;

        if(syncLockChangedHandler != null) {
            KeyViewerOverlay.SyncSettingChanged -= syncLockChangedHandler;
        }
        syncLockChangedHandler = ApplySyncLock;
        KeyViewerOverlay.SyncSettingChanged += syncLockChangedHandler;

        ApplySyncLock();
    }

    // While a row's Set button is listening for its replacement key, the
    // rebuilt row shows "..." on that button (the list rebuilds on capture
    // start, which destroys the clicked button instance).
    private static KeyCode setCaptureKey = KeyCode.None;

    // One allowed-key row: key name on the left, compact Set + Remove
    // buttons on the right. Set rebinds the entry in place via the same
    // single-key capture the Add button uses. locked (key viewer sync owns
    // the list): read-only row, no buttons.
    private static void CreateKeyRow(Transform parent, KeyCode key, bool locked) {
        RectTransform row = GenerateUI.Row(parent);

        RectTransform bg = GenerateUI.BackGround();
        bg.SetParent(row, false);

        var label = GenerateUI.AddText(bg);
        label.text = KeyName(key);

        if(locked) {
            return;
        }

        bool settingThis = setCaptureKey == key && KeyLimiter.IsCapturing;
        MiniButton(bg, settingThis ? "..." : "Set", settingThis ? null : "SET", -106f, 90f, () => {
            if(KeyLimiter.IsCapturing) {
                KeyLimiter.CancelCapture();
                return;
            }

            setCaptureKey = key;
            KeyLimiter.StartCapture(
                newKey => KeyLimiter.ReplaceAllowedKey(key, newKey),
                () => setCaptureKey = KeyCode.None
            );
        });

        MiniButton(bg, "Remove", "REMOVE", -8f, 90f, () => KeyLimiter.ToggleAllowedKey(key));
    }

    private static void MiniButton(Transform parent, string text, string key, float rightOffset, float width, Action onClick) {
        GameObject obj = new("MiniBtn_" + text);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(rightOffset, 0f);
        rect.sizeDelta = new Vector2(width, 36f);

        Image img = obj.AddComponent<Image>();
        img.sprite = MainCore.Spr.Get(UISliceSprite.Circle256P2048);
        img.type = Image.Type.Sliced;
        img.color = UIColors.ObjectButton;

        var label = GenerateUI.AddText(obj.transform, true);
        if(string.IsNullOrEmpty(key)) {
            label.text = text;
        } else {
            GenerateUI.Localize(label, key, text);
        }
        label.fontSize = 18f;
        label.alignment = TextAlignmentOptions.Center;

        GenerateUI.AddButton(obj, btn => {
            if(btn == InputButton.Left) {
                onClick();
            }
        });
    }

    private static string KeyName(KeyCode key) {
        string name = key.ToString();
        if(name.Length == 6 && name.StartsWith("Alpha")) {
            return name[5..];
        }

        return name switch {
            "LeftShift" => "LShift",
            "RightShift" => "RShift",
            "LeftControl" => "LCtrl",
            "RightControl" => "RCtrl",
            "LeftAlt" => "LAlt",
            "RightAlt" => "RAlt",
            "LeftCommand" => "LCmd",
            "RightCommand" => "RCmd",
            "Return" => "Enter",
            "BackQuote" => "`",
            "Backslash" => "\\",
            "Slash" => "/",
            "Minus" => "-",
            "Equals" => "=",
            "Comma" => ",",
            "Period" => ".",
            "Semicolon" => ";",
            "Quote" => "'",
            "LeftBracket" => "[",
            "RightBracket" => "]",
            _ => name,
        };
    }

    // ===== Keyboard Chatter Blocker =====

    private static void CreateChatterBlocker(Transform content) {
        ChatterBlocker.EnsureConf();
        ChatterBlockerSettings conf = ChatterBlocker.Conf;
        ChatterBlockerSettings def = new();

        var sec = GenerateUI.Collapsible(
            content, "Keyboard Chatter Blocker", startExpanded: false,
            v => {
                conf.Enabled = v;
                ChatterBlocker.Save();
            },
            conf.Enabled
        );

        UISlider threshold = GenerateUI.Slider(
            GenerateUI.Row(sec.Body),
            def.ThresholdMs, 0f, 100f, conf.ThresholdMs,
            v => Mathf.Round(v), null, null,
            "Threshold (ms)",
            "kcb_ms"
        );
        threshold.Format = "0 ms";
        threshold.OnChanged = v => conf.ThresholdMs = v;
        threshold.OnComplete = v => {
            conf.ThresholdMs = v;
            ChatterBlocker.Save();
        };
    }

    // ===== Judgement Restriction =====

    private static void CreateJudgementRestriction(Transform content) {
        Restriction.EnsureConf();
        RestrictionSettings conf = Restriction.Conf;
        RestrictionSettings def = new();

        // v1 mode 2 was "XPure Perfect" (XPerfect mod integration), not
        // ported — fall back to Pure Perfect.
        if(conf.JRestrictMode == 2) {
            conf.JRestrictMode = 1;
        }

        var sec = GenerateUI.Collapsible(
            content, "Judgement Restriction", startExpanded: false,
            v => {
                conf.JRestrictEnabled = v;
                Restriction.Save();
            },
            conf.JRestrictEnabled
        );

        RectTransform accuracyRow = null;
        RectTransform[] maskRows = null;

        void RefreshConditionalRows() {
            accuracyRow?.gameObject.SetActive(conf.JRestrictMode == 0);
            if(maskRows != null) {
                foreach(RectTransform row in maskRows) {
                    row?.gameObject.SetActive(conf.JRestrictMode == 3);
                }
            }
        }

        int[] modes = [0, 1, 3, 4];
        GenerateUI.DropDown(
            GenerateUI.Row(sec.Body),
            def.JRestrictMode,
            conf.JRestrictMode,
            modes,
            ModeName,
            v => {
                conf.JRestrictMode = v;
                RefreshConditionalRows();
                Restriction.Save();
            },
            "jr_mode"
        );

        accuracyRow = GenerateUI.Row(sec.Body);
        UISlider accuracy = GenerateUI.Slider(
            accuracyRow,
            def.JRestrictAccuracy, 0f, 100f, conf.JRestrictAccuracy,
            null, null, null,
            "Min Accuracy (%)",
            "jr_acc"
        );
        accuracy.Format = "0.0";
        accuracy.OnChanged = v => conf.JRestrictAccuracy = v;
        accuracy.OnComplete = v => {
            conf.JRestrictAccuracy = v;
            Restriction.Save();
        };

        // Custom mode: which judgements are allowed (everything else fails).
        (HitMargin Margin, string Label, string Id)[] entries = [
            (HitMargin.TooEarly, "Too Early", "jr_allow_tooearly"),
            (HitMargin.VeryEarly, "Very Early", "jr_allow_veryearly"),
            (HitMargin.EarlyPerfect, "Early Perfect", "jr_allow_earlyperfect"),
            (HitMargin.Perfect, "Perfect", "jr_allow_perfect"),
            (HitMargin.LatePerfect, "Late Perfect", "jr_allow_lateperfect"),
            (HitMargin.VeryLate, "Very Late", "jr_allow_verylate"),
            (HitMargin.TooLate, "Too Late", "jr_allow_toolate"),
            (HitMargin.Multipress, "Multipress", "jr_allow_multipress"),
            (HitMargin.FailMiss, "Miss", "jr_allow_miss"),
            (HitMargin.FailOverload, "Overload (No Fail)", "jr_allow_overload_nofail"),
            (HitMargin.OverPress, "Overload (Fail)", "jr_allow_overload_fail"),
        ];

        maskRows = new RectTransform[entries.Length];
        for(int i = 0; i < entries.Length; i++) {
            int bit = 1 << (int)entries[i].Margin;
            maskRows[i] = GenerateUI.Row(sec.Body);
            GenerateUI.Toggle(
                maskRows[i],
                (def.JRestrictAllowedMask & bit) != 0,
                (conf.JRestrictAllowedMask & bit) != 0,
                v => {
                    if(v) {
                        conf.JRestrictAllowedMask |= bit;
                    } else {
                        conf.JRestrictAllowedMask &= ~bit;
                    }
                    Restriction.Save();
                },
                entries[i].Label,
                entries[i].Id
            );
        }

        var message = GenerateUI.Input(
            GenerateUI.Row(sec.Body),
            def.JRestrictMessage,
            conf.JRestrictMessage,
            v => {
                conf.JRestrictMessage = v;
                Restriction.Save();
            },
            "Restriction broken message",
            MainCore.Spr.Get(UISprite.Text128),
            "jr_message"
        );
        message.Rect.AddToolTip(
            "DESC_JR_MESSAGE",
            "Shown on the fail screen when the restriction kills the run."
        );

        RefreshConditionalRows();
    }

    private static string ModeName(int mode) => mode switch {
        0 => MainCore.Tr.Get("JR_MODE_MIN_ACCURACY", "Minimum Accuracy"),
        1 => MainCore.Tr.Get("JR_MODE_PURE_PERFECT", "Pure Perfect Only"),
        3 => MainCore.Tr.Get("JR_MODE_CUSTOM", "Custom Judgements"),
        4 => MainCore.Tr.Get("JR_MODE_NO_TOO_EARLY", "No Too Early"),
        _ => mode.ToString(),
    };

    // ===== Death Limit =====

    private static void CreateDeathLimit(Transform content) {
        Restriction.EnsureConf();
        RestrictionSettings conf = Restriction.Conf;
        RestrictionSettings def = new();

        var sec = GenerateUI.Collapsible(
            content, "Death Limit", startExpanded: false,
            v => {
                conf.DeathLimitEnabled = v;
                Restriction.Save();
            },
            conf.DeathLimitEnabled
        );

        void LimitPair(string toggleLabel, string sliderLabel, string id,
            bool defOn, bool on, Action<bool> setOn,
            int defMax, int max, Action<int> setMax, float sliderMax) {
            RectTransform sliderRow = null;

            GenerateUI.Toggle(
                GenerateUI.Row(sec.Body),
                defOn,
                on,
                v => {
                    setOn(v);
                    sliderRow?.gameObject.SetActive(v);
                    Restriction.Save();
                },
                toggleLabel,
                id + "_on"
            );

            sliderRow = GenerateUI.Row(sec.Body);
            UISlider slider = GenerateUI.Slider(
                sliderRow,
                defMax, 0f, sliderMax, max,
                v => Mathf.Round(v), null, null,
                sliderLabel,
                id + "_max"
            );
            slider.Format = "0";
            slider.OnChanged = v => setMax((int)v);
            slider.OnComplete = v => {
                setMax((int)v);
                Restriction.Save();
            };

            sliderRow.gameObject.SetActive(on);
        }

        LimitPair("Limit Deaths (Miss + Overload)", "Max Deaths", "dl_deaths",
            def.MaxDeathsOn, conf.MaxDeathsOn, v => conf.MaxDeathsOn = v,
            def.MaxDeaths, conf.MaxDeaths, v => conf.MaxDeaths = v, 100f);

        LimitPair("Limit Misses", "Max Misses", "dl_misses",
            def.MaxMissesOn, conf.MaxMissesOn, v => conf.MaxMissesOn = v,
            def.MaxMisses, conf.MaxMisses, v => conf.MaxMisses = v, 50f);

        LimitPair("Limit Overloads", "Max Overloads", "dl_overloads",
            def.MaxOverloadsOn, conf.MaxOverloadsOn, v => conf.MaxOverloadsOn = v,
            def.MaxOverloads, conf.MaxOverloads, v => conf.MaxOverloads = v, 50f);

        var message = GenerateUI.Input(
            GenerateUI.Row(sec.Body),
            def.DeathLimitMessage,
            conf.DeathLimitMessage,
            v => {
                conf.DeathLimitMessage = v;
                Restriction.Save();
            },
            "Limit reached message",
            MainCore.Spr.Get(UISprite.Text128),
            "dl_message"
        );
        message.Rect.AddToolTip(
            "DESC_DL_MESSAGE",
            "Shown on the fail screen when a limit kills the run."
        );
    }
}
