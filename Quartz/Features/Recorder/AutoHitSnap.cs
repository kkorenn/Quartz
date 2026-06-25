using System;
using System.Reflection;
using HarmonyLib;
using Quartz.Core;

namespace Quartz.Features.Recorder;

// Per-frame tile snap for the REALTIME capture loop.
//
// Problem: in realtime 1x capture the level auto-plays and we re-render the game
// cameras at WaitForEndOfFrame. The auto-hit lives in scrPlayer.OttoHoldHit (run
// from scrController's Update via Simulated_PlayerControl_Update), while the planet
// angle that decides "should I hit now" is written by scrPlanet.Update_RefreshAngles
// from scrPlanet's OWN Update. The two are separate MonoBehaviours with undefined
// relative Update order, so on the frame where the angle first crosses a tile's
// targetExitAngle the auto-hit pass may have already run against a stale (pre-cross)
// angle. The hit then lands one frame later and the camera renders the planet sitting
// PAST the tile it should have landed on.
//
// Fix: at end-of-frame (where the recorder already is, right before re-rendering the
// cameras), re-sync each chosen planet's angle to the song clock and then re-run the
// game's OWN auto-hit loop. Because that loop is gated on AutoShouldHitNow(), it is a
// no-op when nothing is due and advances exactly the tiles that became due this frame
// — never more. The planet is then drawn ON the tile the same frame its angle reaches
// it, never past it.
//
// Why force-the-hit and not a cosmetic transform clamp: the hit must actually advance
// currfloor so the planet is genuinely on the next tile (ring switch, trail, camera
// follow, FFX, score, error meter all consistent with a real play-through). The hit
// SOUND is unaffected by when we fire the hit: ADOFAI schedules hitsounds by absolute
// song DSP time (scrConductor queues them via AudioManager.Play(time) /
// PlayScheduled up to 5s ahead, keyed off floor.entryTime), NOT as a side effect of
// Hit()/SwitchChosen(). So firing the hit a frame earlier moves the picture onto the
// beat WITHOUT moving the sound — the live OnAudioFilterRead tap still records each
// hitsound at its scheduled time. A cosmetic clamp would fix only the pixels while
// leaving the simulation a frame behind (trail/ring/camera still lag), so we advance
// the real state instead.
internal static class AutoHitSnap {
    // private void scrPlayer.OttoHoldHit(ulong? targetTick = null)
    // Private, so invoked by reflection. AutoShouldHitNow / Update_RefreshAngles are
    // public and called directly.
    private static readonly MethodInfo OttoHoldHitMethod =
        AccessTools.Method(typeof(scrPlayer), "OttoHoldHit");

    // Reused arg array: a single null => OttoHoldHit's frame-clock (non-async) branch,
    // which is exactly the path PlayerControl_Update uses when async input is disabled
    // (the recorder disables it for the render). Passing a real tick would route through
    // AsyncInputUtils.AdjustAngle, which we must NOT do here.
    private static readonly object[] NullTickArgs = { null };

    private static bool warnedNoMethod;

    // Diagnostics (reset per render in RecorderSession). Invocations = frames where the
    // flush passed all guards and ran the player loop; ForcedAdvances = frames where our
    // end-of-frame flush actually advanced a tile the game's own pass had not yet hit.
    internal static int Invocations;
    internal static int ForcedAdvances;
    internal static void ResetStats() { Invocations = 0; ForcedAdvances = 0; loggedGuards = false; }

    // Flush any auto-hit that became due this frame, at end-of-frame, so the planet
    // advances the SAME frame its angle reaches the tile. Call right before the cameras
    // are re-rendered in the realtime loop. Caller has already verified realtime mode;
    // this method re-checks the gameplay-state guards so it is always safe to call.
    private static bool loggedGuards;

    internal static void FlushDueAutoHits() {
        if(OttoHoldHitMethod == null) {
            if(!warnedNoMethod) {
                warnedNoMethod = true;
                MainCore.Log.Wrn("[Recorder] tile snap disabled: scrPlayer.OttoHoldHit not found (game update?)");
            }
            return;
        }

        scrController ctrl = ADOBase.controller;
        scrPlayerManager pm = ADOBase.playerManager;

        // One-time guard dump so a blocked snap tells us EXACTLY which condition failed.
        if(!loggedGuards) {
            loggedGuards = true;
            MainCore.Log.Msg($"[Recorder] tile snap guards: RDC.auto={RDC.auto} ctrl={(ctrl != null)} " +
                             $"gameworld={(ctrl != null && ctrl.gameworld)} state={(ctrl != null ? ctrl.state.ToString() : "?")} " +
                             $"paused={(ctrl != null && ctrl.paused)} cutscene={(ctrl != null && ctrl.isCutscene)} pm={(pm != null)}");
        }

        // Only in real auto playback.
        if(!RDC.auto) {
            return;
        }
        if(ctrl == null || !ctrl.gameworld) {
            return;
        }
        // Skip when not actively playing so we never poke player code after the run ends
        // (post-Won/Fail currfloor can be stale). Use the live state PROPERTY
        // (stateMachine.GetState()), NOT the cached `currentState` field — that field is
        // only written in scrController.Update and reads `None` during the render even
        // though the state machine is in PlayerControl (the bug that blocked the snap).
        if(ctrl.state != States.PlayerControl || ctrl.paused || ctrl.isCutscene) {
            return;
        }
        if(pm == null) {
            return;
        }

        Invocations++;
        foreach(scrPlayer p in pm) {
            if(p == null || !p.alive) {
                continue;
            }

            PlanetarySystem ps = p.planetarySystem;
            scrPlanet chosen = ps != null ? ps.chosenPlanet : null;
            if(chosen == null || chosen.currfloor == null) {
                continue;
            }
            int seqBefore = p.currFloor != null ? p.currFloor.seqID : -1;

            // 1) Re-sync angle to the clock as of THIS end-of-frame. OttoHoldHit /
            //    AutoShouldHitNow read scrPlanet.angle, which only Update_RefreshAngles
            //    writes; calling it now guarantees we test the final post-everything
            //    angle, not a stale early-in-frame one. (No-ops unless isChosen and the
            //    song clock is running, so it's safe and cheap.)
            try {
                chosen.Update_RefreshAngles();
            } catch(Exception e) {
                // Never let a snap hiccup break the capture.
                MainCore.Log.Wrn($"[Recorder] tile snap refresh failed: {e.Message}");
                continue;
            }

            // 2) Run the game's OWN auto-hit, wrapped exactly like
            //    Simulated_PlayerControl_Update does (WhileFloorNotChange), so multi-tile
            //    catch-up within one frame behaves identically to vanilla. The inner
            //    while(AutoShouldHitNow()) makes this a no-op when nothing is due, so it
            //    can never double-hit; each Hit(true) re-refreshes + re-tests against the
            //    new tile. HitInputEvent(isAuto:true) returns true with no frame-count
            //    debounce, so re-invoking in the same frame as the normal pass is safe.
            try {
                _pendingPlayer = p;
                AsyncInputUtils.WhileFloorNotChange(p, RunOttoHoldHit);
            } catch(TargetInvocationException tie) {
                MainCore.Log.Wrn($"[Recorder] tile snap hit failed: {(tie.InnerException ?? tie).Message}");
            } catch(Exception e) {
                MainCore.Log.Wrn($"[Recorder] tile snap hit failed: {e.Message}");
            } finally {
                _pendingPlayer = null;
            }

            int seqAfter = p.currFloor != null ? p.currFloor.seqID : -1;
            if(seqAfter != seqBefore) {
                ForcedAdvances++;
            }
        }
    }

    // WhileFloorNotChange wants an Action; bind the current player without allocating a
    // closure each frame by stashing it in a field the cached delegate reads.
    private static scrPlayer _pendingPlayer;
    private static readonly Action RunOttoHoldHit = InvokeOttoHoldHit;

    private static void InvokeOttoHoldHit() {
        scrPlayer p = _pendingPlayer;
        if(p != null) {
            OttoHoldHitMethod.Invoke(p, NullTickArgs);
        }
    }
}
