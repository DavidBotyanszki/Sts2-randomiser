using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch(typeof(Player), nameof(Player.CreateForNewRun),
    new[] { typeof(CharacterModel), typeof(UnlockState), typeof(ulong) })]
public static class StartingHpPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player __result)
    {
        // Next's upper bound is exclusive: every value from 1 through 100 is possible.
        int startingHp = System.Random.Shared.Next(1, 101);
        __result.Creature.SetMaxHpInternal(startingHp);
        __result.Creature.SetCurrentHpInternal(startingHp);
    }
}
