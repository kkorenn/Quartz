using GTweens.Tweens;
using System.Diagnostics;

namespace GTweens.Contexts;

/// <summary>
/// Manages and updates a collection of GTweens.
/// </summary>
public sealed class GTweensContext {
    /// <summary>
    /// Gets or sets the time scale at which the tweens should play.
    /// </summary>
    public float TimeScale { get; set; } = 1f;

    /// <summary>
    /// Gets the duration of the last tweens tick in milliseconds.
    /// </summary>
    public float TickDurationMs { get; private set; }

    readonly List<GTween> _aliveTweens = [];
    readonly List<GTween> _tweensToAdd = [];
    readonly List<GTween> _tweensToRemove = [];

    readonly Stopwatch _updateStopwatch = new();

    /// <summary>
    /// Plays a GTween within the context.
    /// </summary>
    /// <param name="gTween">The GTween to play.</param>
    public void Play(GTween gTween) {
        if(gTween.IsNested) {
            return;
        }

        if(gTween.IsAlive) {
            TryStartTween(gTween);
            return;
        }

        gTween.IsAlive = true;

        _tweensToAdd.Add(gTween);

        TryStartTween(gTween);
    }

    /// <summary>
    /// Updates the context and all managed tweens.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since the last update in seconds.</param>
    public void Tick(float deltaTime) {
        float scaledDeltaTime = deltaTime * TimeScale;

        _updateStopwatch.Restart();

        foreach(GTween tween in _tweensToAdd) {
            _aliveTweens.Add(tween);
        }

        _tweensToAdd.Clear();

        foreach(GTween tween in _aliveTweens) {
            if(tween.IsPlaying) {
                // Isolate each tween: a throw here (e.g. a setter touching a
                // destroyed Unity object) must not abort the whole batch —
                // otherwise every later tween and tick stops for the frame and
                // the UI appears frozen. Drop the offender so it can't respam.
                try {
                    tween.Tick(scaledDeltaTime);
                } catch(System.Exception e) {
                    Quartz.Core.MainCore.Log.Err($"[GTween] tween threw, dropping it: {e}");
                    _tweensToRemove.Add(tween);
                }
            } else {
                _tweensToRemove.Add(tween);
            }
        }

        foreach(GTween tween in _tweensToRemove) {
            tween.IsAlive = false;

            _tweensToAdd.Remove(tween);
        }

        // Compact the alive list in one O(n) pass instead of an O(n) List.Remove
        // per finished tween — when a group of same-duration UI tweens completes
        // on the same frame, the per-tween Remove is O(M*n) and spikes the frame.
        // IsAlive is true for every surviving tween (set in Play, cleared only in
        // the loop just above), so this drops exactly the _tweensToRemove set. The
        // static lambda captures nothing, so it allocates no per-frame closure.
        _aliveTweens.RemoveAll(static tween => !tween.IsAlive);

        _tweensToRemove.Clear();

        _updateStopwatch.Stop();

        TickDurationMs = _updateStopwatch.ElapsedMilliseconds;
    }

    /// <summary>
    /// Clears all tweens from the context.
    /// </summary>
    public void Clear() {
        _aliveTweens.Clear();
        _tweensToAdd.Clear();
        _tweensToRemove.Clear();
    }

    void TryStartTween(GTween gTween) {
        if(!gTween.IsPlaying) {
            gTween.Start();
        }
    }
}