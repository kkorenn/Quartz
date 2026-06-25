using ADOFAI;
using HarmonyLib;
using Quartz.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Quartz.Features.Optimizer;

// Lossy DXT compression of custom textures loaded from disk, ported from v1's
// OptimizerPatches (itself from PACL2's MemoryOptimizer). The game already
// frees the CPU copy after upload and copy-on-writes custom textures, so the
// one net-new memory win left is compressing disk-loaded textures (~4-8x less
// VRAM/RAM at a small visual quality cost).
//
// The prefix takes over only the simple file-loading branch of
// TextureManager.LoadTexture, and only when the toggle is on; internal-level
// and bundle-level loads fall through to the original method untouched.
public static class OptimizerPatches {
    [HarmonyPatch(typeof(TextureManager), "LoadTexture")]
    private static class LoadTextureCompressPatch {
        private static bool Prefix(string filePath, ref LoadResult status, int maxSideSize, ref Texture2D __result) {
            Optimizer.EnsureConf();
            if(!MainCore.IsModEnabled || Optimizer.Conf == null || !Optimizer.Conf.LossyTextureCompression) {
                return true;
            }

            // Let the original handle internal levels (Resources) and bundle
            // levels (Addressables) — those aren't plain files on disk.
            if(GCS.internalLevelName != null || ADOBase.isBundleLevel) {
                return true;
            }

            status = LoadResult.MissingFile;

            if(!RDFile.Exists(filePath)) {
                __result = null;
                return false;
            }

            byte[] data = RDFile.ReadAllBytes(filePath, out status);
            Texture2D tex = new(2, 2, TextureFormat.RGBA32, false);
            if(!tex.LoadImage(data)) {
                Object.Destroy(tex);
                __result = null;
                return false;
            }

            if(maxSideSize != -1) {
                TextureManager.ShrinkImage(tex, maxSideSize);
            }
            tex.name = filePath;

            // DXT block compression requires dimensions divisible by 4.
            if(tex.width % 4 == 0 && tex.height % 4 == 0) {
                tex.Compress(false);
            }

            if(tex.isReadable) {
                tex.Apply(false, true);
            }
            tex.wrapMode = TextureWrapMode.Repeat;

            __result = tex;
            return false;
        }
    }

    // VideoBloom.OnRenderImage does the expensive bloom post-process. ADOFAI's
    // component has a HighQuality switch; forcing it off while the render method
    // runs removes work from every bloom frame instead of merely limiting FPS.
    // The original value is restored after each call so turning Quartz off (or
    // disabling Fast Bloom) hands full control back to the game immediately.
    [HarmonyPatch(typeof(VideoBloom), "OnRenderImage")]
    private static class FastBloomPatch {
        private static bool Prepare() => AccessTools.Method(typeof(VideoBloom), "OnRenderImage") != null;

        private static void Prefix(VideoBloom __instance, out bool __state) {
            __state = __instance.HighQuality;
            if(Optimizer.FastBloomActive) {
                __instance.HighQuality = false;
            }
        }

        private static void Postfix(VideoBloom __instance, bool __state) {
            __instance.HighQuality = __state;
        }
    }

    // Some ADOFAI screen effects stay attached and still run their full-screen
    // shader even when their current public values are visually identity. In
    // those states, replace the shader pass with a plain source->destination copy.
    // This is not a settings wrapper: it removes actual per-frame render work only
    // when the component would draw the same frame anyway.
    [HarmonyPatch(typeof(ScreenTile), "OnRenderImage")]
    private static class NoOpScreenTilePatch {
        private static bool Prepare() => AccessTools.Method(typeof(ScreenTile), "OnRenderImage") != null;

        private static bool Prefix(ScreenTile __instance, RenderTexture sourceTexture, RenderTexture destTexture) {
            if(!Optimizer.SkipNoOpScreenFiltersActive) {
                return true;
            }

            if(IsOne(__instance.tileX) && IsOne(__instance.tileY)) {
                Graphics.Blit(sourceTexture, destTexture);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ScreenScroll), "OnRenderImage")]
    private static class NoOpScreenScrollPatch {
        private static bool Prepare() => AccessTools.Method(typeof(ScreenScroll), "OnRenderImage") != null;

        private static bool Prefix(ScreenScroll __instance, RenderTexture sourceTexture, RenderTexture destTexture) {
            if(!Optimizer.SkipNoOpScreenFiltersActive) {
                return true;
            }

            if(IsZero(__instance.scrollOffset) && IsZero(__instance.scrollSpeed)) {
                Graphics.Blit(sourceTexture, destTexture);
                return false;
            }

            return true;
        }
    }

    private static bool IsOne(float value) => Mathf.Abs(value - 1f) <= 0.0001f;
    private static bool IsZero(Vector2 value) => value.sqrMagnitude <= 0.00000001f;
}
