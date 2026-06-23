using Quartz.IO;
using UnityEngine;

namespace Quartz.Core;

// Makes overlay positions resolution-independent. Overlay X/Y offsets are stored
// relative to a calibration resolution (the display they were authored on, kept
// in CoreSettings.CalibWidth/Height). At apply time each offset is scaled by
// current-screen / calibration, so a layout lands in the same relative spot on a
// different monitor: an x of 1920 calibrated at 1920-wide becomes 1800 on an
// 1800-wide display.
//
// Unset calibration (0) is captured from the CURRENT display the first time it's
// needed, so a user sees no change on their own monitor (factor 1); the scaling
// only kicks in once the same settings run on a different-sized display. The
// user can re-baseline to the current display from the Profiles tab.
public static class OverlayCalibration {
    // Capture the live display as the calibration baseline if none is stored yet.
    public static void EnsureCaptured() {
        CoreSettings c = MainCore.Conf;
        if(c == null || (c.CalibWidth > 0f && c.CalibHeight > 0f)) {
            return;
        }
        c.CalibWidth = Screen.width;
        c.CalibHeight = Screen.height;
        MainCore.ConfMgr?.RequestSave();
    }

    // current-screen / calibration, per axis (≈1,1 on the calibrated display).
    public static Vector2 Factor() {
        EnsureCaptured();
        CoreSettings c = MainCore.Conf;
        float cw = c != null && c.CalibWidth > 0f ? c.CalibWidth : Screen.width;
        float ch = c != null && c.CalibHeight > 0f ? c.CalibHeight : Screen.height;
        return new Vector2(
            cw > 0f ? Screen.width / cw : 1f,
            ch > 0f ? Screen.height / ch : 1f
        );
    }

    // Stored offset → live anchoredPosition (scaled up/down for this display).
    public static Vector2 Scale(Vector2 stored) {
        Vector2 f = Factor();
        return new Vector2(stored.x * f.x, stored.y * f.y);
    }

    // Live anchoredPosition → stored offset (back at the calibration baseline).
    public static Vector2 Unscale(Vector2 anchored) {
        Vector2 f = Factor();
        return new Vector2(
            f.x != 0f ? anchored.x / f.x : anchored.x,
            f.y != 0f ? anchored.y / f.y : anchored.y
        );
    }
}
