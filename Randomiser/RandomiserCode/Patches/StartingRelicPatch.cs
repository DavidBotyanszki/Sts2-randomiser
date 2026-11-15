using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch(typeof(Player), "PopulateStartingRelics")]
public static class StartingRelicPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Player __instance)
    {
        var rng = StartingLoadoutPatch.GetRng(__instance, "StartingRelic");
        if (rng == null)
            return false;
        // AllRelics includes every relic pool and all characters' starter relics.
        // Deliberately bypass availability, unlock, rarity and character checks.
        RelicModel[] relics = MultiplayerRandom.OrderedPool(ModelDb.AllRelics);
        RelicModel relic = relics[rng.NextInt(relics.Length)].ToMutable();
        relic.FloorAddedToDeck = 1;
        SaveManager.Instance?.MarkRelicAsSeen(relic);
        __instance.AddRelicInternal(relic);

        // RunManager.FinalizeStartingRelics awaits AfterObtained once the run is
        // ready, including effects that open selection screens. Do not fire it here.
        return false;
    }
}
