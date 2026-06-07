using HarmonyLib;
using Koren.Compat.Interface;

namespace Koren.Core.Service;

// Owns the HarmonyX instance for the runtime. All [HarmonyPatch] classes in
// Koren.dll are picked up by PatchAll on Initialize and reversed by UnpatchSelf
// on Dispose. Patches stay applied for the lifetime of the mod regardless of
// SetModEnabled — individual prefixes/postfixes should gate on
// MainCore.IsModEnabled themselves when their behavior is feature-conditional.
// This mirrors the original KorenResourcePack's lifecycle model.
public sealed class HarmonyService : IRuntimeService {
    public Harmony Harmony { get; private set; }

    public void Initialize() {
        Harmony = new Harmony(Info.Name);
        Harmony.PatchAll(MainCore.Asm);
    }

    public void Dispose() {
        Harmony?.UnpatchSelf();
        Harmony = null;
    }
}
