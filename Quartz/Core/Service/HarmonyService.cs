using System;
using HarmonyLib;
using Quartz.Compat.Interface;

namespace Quartz.Core.Service;

// Owns the Harmony instance for the runtime. All [HarmonyPatch] classes in the
// mod assembly are applied on Initialize and reversed by UnpatchSelf on Dispose.
// Patches stay applied for the lifetime of the mod regardless of SetModEnabled —
// individual prefixes/postfixes should gate on MainCore.IsModEnabled themselves
// when their behavior is feature-conditional. Mirrors the original
// KorenResourcePack's lifecycle model.
public sealed class HarmonyService : IRuntimeService {
    // Fully qualified: MelonLoader.dll ships a legacy `Harmony` shim namespace
    // that would otherwise shadow the HarmonyLib.Harmony type.
    public HarmonyLib.Harmony Harmony { get; private set; }

    public void Initialize() {
        Harmony = new HarmonyLib.Harmony(Info.Name);
        PatchAllResilient();
    }

    // Apply each [HarmonyPatch] class on its own instead of Harmony.PatchAll.
    // On HarmonyX (MelonLoader) PatchAll already isolates per class, but on plain
    // Harmony 2.x (vanilla UnityModManager) PatchAll is ALL-OR-NOTHING: a single
    // class that can't apply (a target method removed by a game update, a bad
    // attribute) aborts the entire call and the mod fails to initialize. Patching
    // per class with a try/catch means one drifted patch is skipped (logged)
    // while the rest of the mod still loads. CreateClassProcessor /
    // GetTypesFromAssembly exist on both Harmony flavors.
    private void PatchAllResilient() {
        foreach(Type type in AccessTools.GetTypesFromAssembly(MainCore.Asm)) {
            try {
                Harmony.CreateClassProcessor(type).Patch();
            } catch(Exception e) {
                MainCore.Log.Wrn($"[Harmony] skipped patch class {type.FullName}: {e.Message}");
            }
        }
    }

    public void Dispose() {
        Harmony?.UnpatchSelf();
        Harmony = null;
    }
}
