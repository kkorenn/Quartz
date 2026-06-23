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
}
