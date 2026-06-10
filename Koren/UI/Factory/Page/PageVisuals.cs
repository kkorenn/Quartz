using Koren.Features.EffectRemover;
using Koren.Features.Judgement;
using Koren.Features.PlanetColors;
using Koren.Features.Tweaks;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace Koren.UI.Factory.Page;

// Visuals tab. Hosts the Effect Remover category — the full v1
// KorenResourcePack effect remover settings reimplemented in uGUI. Grouping
// (Non-DLC / Planet / Track / DLC / Misc) and conditional rows (e.g. the
// camera zoom slider only while Camera removal + Set Camera Zoom are on)
// mirror the original IMGUI layout. Also hosts Hide Judgements — v1's "Hide
// judgement popups" tweak with its per-judgement mask.
internal static class PageVisuals {
    public static void Create(RectTransform parent) {
        EffectRemover.EnsureConf();
        EffectRemoverSettings conf = EffectRemover.Conf;
        EffectRemoverSettings def = new();

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

        void Save() => EffectRemover.Save();

        var sec = GenerateUI.Collapsible(content.transform, "Effect Remover", startExpanded: true);

        var onToggle = GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.On,
            conf.On,
            v => {
                conf.On = v;
                EffectRemover.RefreshEditorSaveButtons();
                Save();
            },
            "Enable Effect Remover",
            "fxrm_on"
        );
        onToggle.Rect.AddToolTip(
            "DESC_FXRM_ON",
            "Strips the selected effect events from charts as they load. Reload the level to apply changes."
        );

        var saveToggle = GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.EnableSave,
            conf.EnableSave,
            v => {
                conf.EnableSave = v;
                EffectRemover.RefreshEditorSaveButtons();
                Save();
            },
            "Allow Saving in Editor",
            "fxrm_save"
        );
        saveToggle.Rect.AddToolTip(
            "DESC_FXRM_SAVE",
            "While the remover is on, the editor holds the stripped copy of the level — saving would overwrite the original chart, so it's blocked unless you enable this."
        );

        // === Non-DLC events ===
        GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)).text = "Non-DLC Events";

        // Conditional rows created later; these callbacks update their
        // visibility when the toggle that gates them changes.
        RectTransform removeAllRow = null;
        RectTransform setZoomRow = null;
        RectTransform zoomSliderRow = null;
        RectTransform resetAnimRow = null;
        RectTransform resetColorRow = null;

        void RefreshConditionalRows() {
            removeAllRow?.gameObject.SetActive(conf.Decorations);
            setZoomRow?.gameObject.SetActive(conf.Cameras);
            zoomSliderRow?.gameObject.SetActive(conf.Cameras && conf.SetCameraZoom);
            resetAnimRow?.gameObject.SetActive(conf.TrackAnimations);
            resetColorRow?.gameObject.SetActive(conf.TrackColors);
        }

        void SimpleToggle(Transform body, bool defVal, bool val, System.Action<bool> set, string label, string id) {
            GenerateUI.Toggle(
                GenerateUI.Row(body),
                defVal,
                val,
                v => {
                    set(v);
                    RefreshConditionalRows();
                    Save();
                },
                label,
                id
            );
        }

        SimpleToggle(sec.Body, def.Filters, conf.Filters, v => conf.Filters = v, "Filter", "fxrm_filters");
        SimpleToggle(sec.Body, def.AdvancedFilters, conf.AdvancedFilters, v => conf.AdvancedFilters = v, "Advanced Filter", "fxrm_advfilters");
        SimpleToggle(sec.Body, def.Particles, conf.Particles, v => conf.Particles = v, "Particles", "fxrm_particles");
        SimpleToggle(sec.Body, def.Decorations, conf.Decorations, v => conf.Decorations = v, "Decoration", "fxrm_decorations");
        SimpleToggle(sec.Body, def.Backgrounds, conf.Backgrounds, v => conf.Backgrounds = v, "Background", "fxrm_backgrounds");
        SimpleToggle(sec.Body, def.Cameras, conf.Cameras, v => conf.Cameras = v, "Camera", "fxrm_cameras");
        SimpleToggle(sec.Body, def.RepeatEvents, conf.RepeatEvents, v => conf.RepeatEvents = v, "Repeat Event", "fxrm_repeat");
        SimpleToggle(sec.Body, def.FrameRate, conf.FrameRate, v => conf.FrameRate = v, "Frame Rate", "fxrm_framerate");
        SimpleToggle(sec.Body, def.HitSounds, conf.HitSounds, v => conf.HitSounds = v, "HitSound", "fxrm_hitsounds");

        // === Planet events ===
        {
            var planet = GenerateUI.Collapsible(sec.Body, "Planet Events", startExpanded: false);

            UIToggle orbit = null, scale = null, radius = null;

            GenerateUI.Button(
                GenerateUI.Row(planet.Body),
                () => {
                    // v1 semantics: if none are on, turn all on; else all off.
                    bool value = !conf.PlanetOrbit && !conf.PlanetScale && !conf.PlanetRadius;
                    orbit.Set(value);
                    scale.Set(value);
                    radius.Set(value);
                },
                "Toggle All",
                "fxrm_planet_all"
            ).SetSecondary();

            orbit = GenerateUI.Toggle(
                GenerateUI.Row(planet.Body), def.PlanetOrbit, conf.PlanetOrbit,
                v => { conf.PlanetOrbit = v; Save(); }, "Planet Orbit", "fxrm_planet_orbit");
            scale = GenerateUI.Toggle(
                GenerateUI.Row(planet.Body), def.PlanetScale, conf.PlanetScale,
                v => { conf.PlanetScale = v; Save(); }, "Planet Scale", "fxrm_planet_scale");
            radius = GenerateUI.Toggle(
                GenerateUI.Row(planet.Body), def.PlanetRadius, conf.PlanetRadius,
                v => { conf.PlanetRadius = v; Save(); }, "Planet Radius", "fxrm_planet_radius");
        }

        // === Track events ===
        {
            var track = GenerateUI.Collapsible(sec.Body, "Track Events", startExpanded: false);

            UIToggle anims = null, moves = null, positions = null, colors = null;

            GenerateUI.Button(
                GenerateUI.Row(track.Body),
                () => {
                    bool value = !conf.TrackAnimations && !conf.TrackPositions
                        && !conf.TrackMoves && !conf.TrackColors;
                    anims.Set(value);
                    moves.Set(value);
                    positions.Set(value);
                    colors.Set(value);
                },
                "Toggle All",
                "fxrm_track_all"
            ).SetSecondary();

            anims = GenerateUI.Toggle(
                GenerateUI.Row(track.Body), def.TrackAnimations, conf.TrackAnimations,
                v => { conf.TrackAnimations = v; RefreshConditionalRows(); Save(); }, "Animate Track", "fxrm_track_anims");
            moves = GenerateUI.Toggle(
                GenerateUI.Row(track.Body), def.TrackMoves, conf.TrackMoves,
                v => { conf.TrackMoves = v; Save(); }, "Move Track", "fxrm_track_moves");
            positions = GenerateUI.Toggle(
                GenerateUI.Row(track.Body), def.TrackPositions, conf.TrackPositions,
                v => { conf.TrackPositions = v; Save(); }, "Position Track", "fxrm_track_positions");
            colors = GenerateUI.Toggle(
                GenerateUI.Row(track.Body), def.TrackColors, conf.TrackColors,
                v => { conf.TrackColors = v; RefreshConditionalRows(); Save(); }, "Track Color", "fxrm_track_colors");
        }

        // === DLC events ===
        GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)).text = "DLC Events";

        SimpleToggle(sec.Body, def.HoldSounds, conf.HoldSounds, v => conf.HoldSounds = v, "HoldSound", "fxrm_holdsounds");
        SimpleToggle(sec.Body, def.HideIcons, conf.HideIcons, v => conf.HideIcons = v, "HideIcon & Judgements", "fxrm_hideicons");

        // === Misc ===
        GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)).text = "Misc";

        removeAllRow = GenerateUI.Row(sec.Body);
        GenerateUI.Toggle(
            removeAllRow, def.RemoveAllDecorations, conf.RemoveAllDecorations,
            v => { conf.RemoveAllDecorations = v; Save(); },
            "Remove All Decorations",
            "fxrm_remove_all_deco"
        ).Rect.AddToolTip(
            "DESC_FXRM_REMOVE_ALL_DECO",
            "Off keeps decorations that judgement-conditional events reference (hit/miss feedback) and removes the rest."
        );

        SimpleToggle(sec.Body, def.ResetTrackOpacity, conf.ResetTrackOpacity,
            v => conf.ResetTrackOpacity = v,
            "Reset All 'Track Opacity' Values to 100%", "fxrm_reset_opacity");

        setZoomRow = GenerateUI.Row(sec.Body);
        GenerateUI.Toggle(
            setZoomRow, def.SetCameraZoom, conf.SetCameraZoom,
            v => {
                conf.SetCameraZoom = v;
                RefreshConditionalRows();
                Save();
            },
            "Set Camera Zoom",
            "fxrm_set_zoom"
        );

        zoomSliderRow = GenerateUI.Row(sec.Body);
        UISlider zoom = GenerateUI.Slider(
            zoomSliderRow,
            def.CameraZoomScale, 100f, 1000f, conf.CameraZoomScale,
            v => Mathf.Clamp(Mathf.Round(v), 100f, 1000f), null, null,
            "Camera Zoom",
            "fxrm_zoom_scale"
        );
        zoom.Format = "0 %";
        zoom.OnChanged = v => conf.CameraZoomScale = v;
        zoom.OnComplete = v => { conf.CameraZoomScale = v; Save(); };

        resetAnimRow = GenerateUI.Row(sec.Body);
        GenerateUI.Toggle(
            resetAnimRow, def.ResetTrackAnimation, conf.ResetTrackAnimation,
            v => { conf.ResetTrackAnimation = v; Save(); },
            "Set Track Animation to Default",
            "fxrm_reset_anim"
        );

        resetColorRow = GenerateUI.Row(sec.Body);
        GenerateUI.Toggle(
            resetColorRow, def.ResetTrackColor, conf.ResetTrackColor,
            v => { conf.ResetTrackColor = v; Save(); },
            "Set Track Color to Default",
            "fxrm_reset_color"
        );

        RefreshConditionalRows();

        CreateHideJudgements(content.transform);
        CreateVisualTweaks(content.transform);
        CreatePlanetColors(content.transform);
    }

    // v1 ResourceChanger's "Change ball color": per planet slot a ball color
    // (picker, RGB only) + ball opacity slider, and a tail opacity slider.
    // "Separate Tail Color" reveals per-planet tail color pickers; while off,
    // tails reuse the ball RGB, exactly like the original.
    private static void CreatePlanetColors(Transform content) {
        PlanetColors.EnsureConf();
        PlanetColorsSettings conf = PlanetColors.Conf;
        PlanetColorsSettings def = new();

        var sec = GenerateUI.Collapsible(content, "Planet Colors", startExpanded: false);

        void Apply() => PlanetColors.Refresh();
        void Save() => PlanetColors.Save();

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.Enabled,
            conf.Enabled,
            v => {
                conf.Enabled = v;
                if(v) {
                    PlanetColors.Refresh();
                } else {
                    PlanetColors.Restore();
                }
                Save();
            },
            "Change Ball Color",
            "pcol_on"
        ).Rect.AddToolTip(
            "DESC_PCOL_ON",
            "Recolors the planets (and their tails) with the colors below, overriding level events and skins."
        );

        RectTransform[] tailColorRows = new RectTransform[PlanetColorsSettings.Slots];

        void RefreshTailRows() {
            foreach(RectTransform row in tailColorRows) {
                row?.gameObject.SetActive(conf.SeparateTailColor);
            }
        }

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.SeparateTailColor,
            conf.SeparateTailColor,
            v => {
                conf.SeparateTailColor = v;
                RefreshTailRows();
                Apply();
                Save();
            },
            "Separate Tail Color",
            "pcol_sep_tail"
        ).Rect.AddToolTip(
            "DESC_PCOL_SEP_TAIL",
            "Off: tails use the ball color (with their own opacity). On: each planet's tail gets its own color."
        );

        for(int i = 0; i < PlanetColorsSettings.Slots; i++) {
            int slot = i;
            string n = (slot + 1).ToString();

            GenerateUI.AddTextH1(GenerateUI.Row(sec.Body)).text = $"Planet {n}";

            GenerateUI.ColorPicker(
                GenerateUI.Row(sec.Body),
                new Color(def.BallR[slot], def.BallG[slot], def.BallB[slot]),
                new Color(conf.BallR[slot], conf.BallG[slot], conf.BallB[slot]),
                c => { conf.SetBallRgb(slot, c); Apply(); },
                c => { conf.SetBallRgb(slot, c); Apply(); Save(); },
                $"Planet {n} Color",
                $"pcol_ball{n}",
                showAlpha: false
            );

            UISlider ballOp = GenerateUI.Slider(
                GenerateUI.Row(sec.Body),
                def.BallOpacity[slot], 0f, 1f, conf.BallOpacity[slot],
                null, null, null,
                $"Planet {n} Ball Opacity",
                $"pcol_ballop{n}"
            );
            ballOp.Format = "0 %";
            ballOp.OnChanged = v => { conf.BallOpacity[slot] = v; Apply(); };
            ballOp.OnComplete = v => { conf.BallOpacity[slot] = v; Apply(); Save(); };

            tailColorRows[slot] = GenerateUI.Row(sec.Body);
            GenerateUI.ColorPicker(
                tailColorRows[slot],
                new Color(def.TailR[slot], def.TailG[slot], def.TailB[slot]),
                new Color(conf.TailR[slot], conf.TailG[slot], conf.TailB[slot]),
                c => { conf.SetTailRgb(slot, c); Apply(); },
                c => { conf.SetTailRgb(slot, c); Apply(); Save(); },
                $"Planet {n} Tail Color",
                $"pcol_tail{n}",
                showAlpha: false
            );

            UISlider tailOp = GenerateUI.Slider(
                GenerateUI.Row(sec.Body),
                def.TailOpacity[slot], 0f, 1f, conf.TailOpacity[slot],
                null, null, null,
                $"Planet {n} Tail Opacity",
                $"pcol_tailop{n}"
            );
            tailOp.Format = "0 %";
            tailOp.OnChanged = v => { conf.TailOpacity[slot] = v; Apply(); };
            tailOp.OnComplete = v => { conf.TailOpacity[slot] = v; Apply(); Save(); };
        }

        RefreshTailRows();
    }

    // v1's visual tweaks: checkpoint removal, ball core particle removal,
    // tile hit glow and planet glow suppression. The two non-visual tweaks
    // from the same v1 section live on the Tweaks tab.
    private static void CreateVisualTweaks(Transform content) {
        Tweaks.EnsureConf();
        TweaksSettings conf = Tweaks.Conf;
        TweaksSettings def = new();

        var sec = GenerateUI.Collapsible(content, "Visual Tweaks", startExpanded: false);

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.RemoveAllCheckpoints,
            conf.RemoveAllCheckpoints,
            v => {
                conf.RemoveAllCheckpoints = v;
                Tweaks.RefreshCheckpointTweak();
                Tweaks.Save();
            },
            "Remove All Checkpoints",
            "tw_cp"
        ).Rect.AddToolTip(
            "DESC_TW_CP",
            "Strips checkpoint icons and behavior from the level — dying always restarts the run. Turning this off needs a level reload to bring icons back."
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.RemoveBallCoreParticles,
            conf.RemoveBallCoreParticles,
            v => {
                conf.RemoveBallCoreParticles = v;
                Tweaks.RefreshBallCoreParticlesTweak();
                Tweaks.Save();
            },
            "Remove Ball Core Particles",
            "tw_bcp"
        ).Rect.AddToolTip(
            "DESC_TW_BCP",
            "Removes the planets' core and spark particles."
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.DisableTileHitGlow,
            conf.DisableTileHitGlow,
            v => {
                conf.DisableTileHitGlow = v;
                Tweaks.RefreshTileHitGlowTweak();
                Tweaks.Save();
            },
            "Disable Tile Hit Glow",
            "tw_glow"
        ).Rect.AddToolTip(
            "DESC_TW_GLOW",
            "Suppresses the glow flash tiles get when the planet lands on them."
        );

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.RemovePlanetGlow,
            conf.RemovePlanetGlow,
            v => {
                conf.RemovePlanetGlow = v;
                Tweaks.RefreshPlanetGlowTweak();
                Tweaks.Save();
            },
            "Remove Planet Glow",
            "tw_pglow"
        ).Rect.AddToolTip(
            "DESC_TW_PGLOW",
            "Hides the glow sprite drawn around the planets."
        );
    }

    // v1's "Hide judgement popups" tweak: a master toggle plus one toggle per
    // vanilla judgement choosing which popups get suppressed. The mask rows
    // are only shown while the master toggle is on, like the IMGUI original.
    private static void CreateHideJudgements(Transform content) {
        JudgementPopupHider.EnsureConf();
        JudgementPopupHiderSettings conf = JudgementPopupHider.Conf;
        JudgementPopupHiderSettings def = new();

        var sec = GenerateUI.Collapsible(content, "Hide Judgements", startExpanded: false);

        (HitMargin Margin, string Label, string Id)[] entries = [
            (HitMargin.TooEarly, "Too Early", "jpop_tooearly"),
            (HitMargin.VeryEarly, "Very Early", "jpop_veryearly"),
            (HitMargin.EarlyPerfect, "Early Perfect", "jpop_earlyperfect"),
            (HitMargin.Perfect, "Perfect", "jpop_perfect"),
            (HitMargin.LatePerfect, "Late Perfect", "jpop_lateperfect"),
            (HitMargin.VeryLate, "Very Late", "jpop_verylate"),
            (HitMargin.TooLate, "Too Late", "jpop_toolate"),
            (HitMargin.Multipress, "Multipress", "jpop_multipress"),
            (HitMargin.FailMiss, "Miss", "jpop_miss"),
            (HitMargin.FailOverload, "Overload (No Fail)", "jpop_overload_nofail"),
            (HitMargin.Auto, "Auto", "jpop_auto"),
            (HitMargin.OverPress, "Overload (Fail)", "jpop_overload_fail"),
        ];

        RectTransform[] maskRows = new RectTransform[entries.Length];

        void RefreshMaskRows() {
            foreach(RectTransform row in maskRows) {
                row?.gameObject.SetActive(conf.Enabled);
            }
        }

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.Enabled,
            conf.Enabled,
            v => {
                conf.Enabled = v;
                RefreshMaskRows();
                JudgementPopupHider.Save();
            },
            "Hide Judgement Popups",
            "jpop_on"
        ).Rect.AddToolTip(
            "DESC_JPOP_ON",
            "Suppresses the selected judgement popups — the text that pops up at the planet on every hit."
        );

        for(int i = 0; i < entries.Length; i++) {
            int bit = 1 << (int)entries[i].Margin;
            maskRows[i] = GenerateUI.Row(sec.Body);
            GenerateUI.Toggle(
                maskRows[i],
                (def.HiddenMask & bit) != 0,
                (conf.HiddenMask & bit) != 0,
                v => {
                    if(v) {
                        conf.HiddenMask |= bit;
                    } else {
                        conf.HiddenMask &= ~bit;
                    }
                    JudgementPopupHider.Save();
                },
                entries[i].Label,
                entries[i].Id
            );
        }

        RefreshMaskRows();
    }
}
