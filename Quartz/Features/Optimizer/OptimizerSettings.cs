using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;

namespace Quartz.Features.Optimizer;

// Persisted config for the Optimizer feature. These are engine/runtime
// performance toggles the game doesn't expose in its own options and that the
// Effect Remover (which strips visual events out of the chart) doesn't touch —
// none of them change how a level looks, only how the engine runs. Lives in
// UserData/Quartz/Optimizer.json. All default off: nothing here alters behaviour
// until the user opts in.
public sealed class OptimizerSettings : ISettingsFile {
    // Defer garbage collection during a run (GC set to Manual), then collect at
    // the end. Stops the stop-the-world GC pauses that otherwise land mid-run
    // and nudge rhythm timing. The heap grows for the duration of the run (a
    // safety valve forces a collect if it grows too far), so it's opt-in.
    public bool SmoothGC = true;

    // Force a collection on every scene load, so a run starts from a clean heap.
    // Pairs with SmoothGC — it's the pre-run clean that SmoothGC deliberately
    // skips (collecting at gameplay start would itself hitch the first frame).
    public bool CollectOnLevelLoad = true;

    // Raise the process priority (AboveNormal) so the OS scheduler hands the game
    // more consistent CPU time. Real effect on Windows; a no-op where the
    // platform doesn't permit it unprivileged (typically macOS/Linux).
    public bool BoostProcessPriority = true;

    // Keep the game running at full speed when its window loses focus, so a run
    // or practice session keeps going while alt-tabbed.
    public bool RunInBackground = true;

    // Compress custom textures loaded from disk with lossy DXT compression
    // (~4-8x less VRAM/RAM, small visual quality cost). Unlike the toggles above
    // this changes how textures look, so it defaults off. Only affects disk-
    // loaded custom textures; internal-level and bundle assets are untouched.
    public bool LossyTextureCompression = false;

    // Force ADOFAI's VideoBloom post-process down the cheaper low-quality path.
    // This targets real GPU work in VideoBloom.OnRenderImage on bloom-heavy
    // levels. It changes bloom softness/quality, so it defaults off.
    public bool FastBloom = false;

    // Skip ADOFAI full-screen post-process components when their public state is
    // visually identity (e.g. screen tile 1x1, screen scroll offset/speed 0). The
    // original shader blit is replaced by a plain copy only in those no-op states,
    // so this should not change visuals and defaults on.
    public bool SkipNoOpScreenFilters = true;

    // Render Quartz overlay text drop-shadows via the font material's GPU underlay
    // (one mesh/draw per label) instead of a second sibling TMP (two meshes/draws).
    // Halves the draw calls of every shadowed overlay label (Combo/Judgement/Panels;
    // SongTitle's rich-text shadow keeps the sibling path for its per-glyph fade).
    // EXPERIMENTAL + defaults off: the underlay offset is font-relative, not pixel-
    // exact, so the shadow can sit slightly differently than the sibling shadow and
    // wants an eyeball check. Only the softness-0 (no blur) shadow uses it.
    public bool LightTextShadows = false;

    public JToken Serialize() {
        return new JObject {
            [nameof(SmoothGC)] = SmoothGC,
            [nameof(CollectOnLevelLoad)] = CollectOnLevelLoad,
            [nameof(BoostProcessPriority)] = BoostProcessPriority,
            [nameof(RunInBackground)] = RunInBackground,
            [nameof(LossyTextureCompression)] = LossyTextureCompression,
            [nameof(FastBloom)] = FastBloom,
            [nameof(SkipNoOpScreenFilters)] = SkipNoOpScreenFilters,
            [nameof(LightTextShadows)] = LightTextShadows,
        };
    }

    public void Deserialize(JToken token) {
        SmoothGC = IOUtils.Read(token, nameof(SmoothGC), SmoothGC);
        CollectOnLevelLoad = IOUtils.Read(token, nameof(CollectOnLevelLoad), CollectOnLevelLoad);
        BoostProcessPriority = IOUtils.Read(token, nameof(BoostProcessPriority), BoostProcessPriority);
        RunInBackground = IOUtils.Read(token, nameof(RunInBackground), RunInBackground);
        LossyTextureCompression = IOUtils.Read(token, nameof(LossyTextureCompression), LossyTextureCompression);
        FastBloom = IOUtils.Read(token, nameof(FastBloom), FastBloom);
        SkipNoOpScreenFilters = IOUtils.Read(token, nameof(SkipNoOpScreenFilters), SkipNoOpScreenFilters);
        LightTextShadows = IOUtils.Read(token, nameof(LightTextShadows), LightTextShadows);
    }
}
