using System.Collections.Generic;
using UnityEngine;

namespace Quartz.Features.Editor;

// "BGA Mod" — hides every tile and planet so only the level background shows,
// for recording a background animation (BGA) to composite gameplay over.
//
// Hiding switches the renderers off; it never destroys or deactivates anything.
// The game's logic (path geometry, planet motion, collision, editor selection)
// keeps running untouched, so the level still plays and edits still work, and
// the moment the toggle is turned off everything is drawn again. Restoring just
// enables the renderers: the primary tile/planet renderers default to on, and
// the secondary visuals the game gates (glows, particles, faces) are gated by
// GameObject.active, not renderer.enabled, so force-enabling them is inert.
//
// Once a tile's renderers are off the game doesn't switch them back on, so the
// tiles are only re-swept when the path is (re)built — detected by a cheap
// canary on the first/last tile's renderer rather than scanning every frame.
// Planets are re-enabled by the game (revive, scene load), so they're re-hidden
// every frame; there are only a handful, so the cost is negligible.
//
// Like the rest of the Editor feature this rides the per-frame reconcile tick
// rather than a web of Harmony patches.
public static partial class EditorFeature {
    internal static bool ShouldHideForBga => Enabled && Conf.BgaMod && IsPlaying;

    // Only hide while the level is actually playing — never in the editor's edit
    // view. In the level editor that means play-test mode (scnEditor.playMode);
    // in a normal gameplay scene it's scrController.gameworld (false at menus).
    private static bool IsPlaying {
        get {
            if(ADOBase.isLevelEditor) {
                scnEditor ed = scnEditor.instance;
                return ed != null && ed.playMode;
            }
            scrController c = ADOBase.controller;
            return c != null && c.gameworld;
        }
    }

    private static bool bgaApplied;

    private static void ReconcileBga() {
        bool want;
        try { want = ShouldHideForBga; }
        catch { return; }

        if(!want) {
            if(bgaApplied) {
                try {
                    SetFloorsVisible(true);
                    SetPlanetsVisible(true);
                } catch {
                    // Best-effort restore; a later tick retries while applied.
                }
                bgaApplied = false;
            }
            ReconcileBgaDecorations(false);
            return;
        }

        try {
            // Planets get re-enabled by the game (revive/scene load), so re-hide
            // them every frame — there are only a few.
            SetPlanetsVisible(false);

            // Tiles stay hidden once switched off, so only re-sweep when the path
            // was (re)built — a fresh tile reports its renderer enabled again.
            List<scrFloor> floors = ADOBase.lm?.listFloors;
            if(floors != null && floors.Count > 0 && (!bgaApplied || FloorsLookVisible(floors))) {
                SetFloorsVisible(floors, false);
            }

            bgaApplied = true;
        } catch {
            // A dead reference mid-rebuild shouldn't spam every frame; the next
            // tick recovers.
        }

        ReconcileBgaDecorations(true);
    }

    // Hard revert, used when the mod is disabled / torn down.
    private static void RestoreBga() {
        if(bgaApplied) {
            try {
                SetFloorsVisible(true);
                SetPlanetsVisible(true);
            } catch {
            }
            bgaApplied = false;
        }
        ReconcileBgaDecorations(false);
    }

    // Cheap O(1) check: a (re)built path hands back tiles whose renderers are
    // enabled, so if either end is showing the whole path needs re-hiding.
    private static bool FloorsLookVisible(List<scrFloor> floors) {
        return FloorBodyEnabled(floors[0]) || FloorBodyEnabled(floors[floors.Count - 1]);
    }

    private static bool FloorBodyEnabled(scrFloor floor) {
        Renderer r = floor != null && floor.floorRenderer != null ? floor.floorRenderer.renderer : null;
        return r != null && r.enabled;
    }

    private static void SetFloorsVisible(bool visible)
        => SetFloorsVisible(ADOBase.lm?.listFloors, visible);

    private static void SetFloorsVisible(List<scrFloor> floors, bool visible) {
        if(floors == null) {
            return;
        }
        foreach(scrFloor floor in floors) {
            if(floor == null) {
                continue;
            }
            if(floor.floorRenderer != null) {
                Set(floor.floorRenderer.renderer, visible);
            }
            Set(floor.legacyFloorSpriteRenderer, visible);
            Set(floor.iconsprite, visible);
            Set(floor.outlineSprite, visible);
            Set(floor.topGlow, visible);
            Set(floor.bottomGlow, visible);
            Set(floor.multiplanetLine, visible);
            if(floor.holdRenderer != null) {
                Set(floor.holdRenderer.m_meshRenderer, visible);
            }
        }
    }

    private static void SetPlanetsVisible(bool visible) {
        scrPlayerManager pm = ADOBase.playerManager;
        if(pm != null && pm.players != null) {
            foreach(scrPlayer player in pm.players) {
                List<scrPlanet> planets = player != null && player.planetarySystem != null
                    ? player.planetarySystem.planetList
                    : null;
                if(planets == null) {
                    continue;
                }
                foreach(scrPlanet planet in planets) {
                    if(planet != null) {
                        SetPlanetRendererVisible(planet.planetRenderer, visible);
                    }
                }
            }
        }

        // Multi-planet dummies are parented to tiles but are planets too.
        List<PlanetRenderer> dummies = ADOBase.controller != null ? ADOBase.controller.dummyPlanets : null;
        if(dummies != null) {
            foreach(PlanetRenderer dummy in dummies) {
                SetPlanetRendererVisible(dummy, visible);
            }
        }
    }

    private static void SetPlanetRendererVisible(PlanetRenderer pr, bool visible) {
        if(pr == null) {
            return;
        }
        Renderer[] renderers = pr.appearanceRenderers; // body, particles, ring, glow
        for(int i = 0; i < renderers.Length; i++) {
            Set(renderers[i], visible);
        }
        Set(pr.faceSprite, visible);
        Set(pr.faceDetails, visible);
        Set(pr.samuraiSprite, visible);
    }

    private static void Set(Renderer r, bool visible) {
        if(r != null && r.enabled != visible) {
            r.enabled = visible;
        }
    }

    // --- Decorations ----------------------------------------------------------
    //
    // The two extra toggles hide tile-attached and planet-attached decorations
    // respectively (DecPlacementType.Tile vs. *Planet); Global/Camera-anchored
    // decorations are the background being recorded, so they're left alone.
    //
    // Decoration subclasses each hide differently (renderer.enabled, SetActive,
    // text.enabled, particle GO), so this drives the uniform scrDecoration API:
    // SetVisible(false) hides now, forceHide=true keeps it hidden if the game
    // re-runs the decoration's Setup, and the per-frame scan re-hides anything a
    // visibility level-event turned back on. Only currently-visible decorations
    // are hidden, and exactly those are remembered so the restore pass shows the
    // same set again (and clears forceHide) without disturbing decorations the
    // level meant to keep hidden.

    private enum DecoKind { Tile, Planet }

    private static readonly HashSet<scrDecoration> bgaTileDecos = new();
    private static readonly HashSet<scrDecoration> bgaPlanetDecos = new();

    private static void ReconcileBgaDecorations(bool bgaActive) {
        try {
            UpdateDecoSet(bgaTileDecos, bgaActive && Conf.BgaHideTileDeco, DecoKind.Tile);
            UpdateDecoSet(bgaPlanetDecos, bgaActive && Conf.BgaHidePlanetDeco, DecoKind.Planet);
        } catch {
            // A dead reference mid-rebuild shouldn't spam every frame.
        }
    }

    private static void UpdateDecoSet(HashSet<scrDecoration> hidden, bool hide, DecoKind kind) {
        if(hide) {
            scrDecorationManager mgr = scrDecorationManager.instance;
            List<scrDecoration> all = mgr != null ? mgr.allDecorations : null;
            if(all == null) {
                return;
            }
            foreach(scrDecoration deco in all) {
                if(deco == null || !Matches(deco, kind) || !deco.GetVisible()) {
                    continue;
                }
                deco.forceHide = true;
                deco.SetVisible(false);
                hidden.Add(deco);
            }
        } else if(hidden.Count > 0) {
            foreach(scrDecoration deco in hidden) {
                if(deco != null) {
                    deco.forceHide = false;
                    deco.SetVisible(true);
                }
            }
            hidden.Clear();
        }
    }

    private static bool Matches(scrDecoration deco, DecoKind kind) {
        DecPlacementType p = deco.placementType;
        return kind == DecoKind.Tile
            ? p == DecPlacementType.Tile
            : p == DecPlacementType.RedPlanet
                || p == DecPlacementType.BluePlanet
                || p == DecPlacementType.GreenPlanet;
    }
}
