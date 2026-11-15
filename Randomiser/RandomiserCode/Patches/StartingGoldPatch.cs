using MegaCrit.Sts2.Core.Entities.Players;

namespace Randomiser.RandomiserCode.Patches;

public static class StartingGoldPatch
{
    public static void Postfix(Player __result)
    {
        var rng = StartingLoadoutPatch.GetRng(__result, "StartingGold")
            ?? throw new InvalidOperationException("Starting gold must be initialized with the shared run seed.");
        __result.Gold = rng.NextInt(1, 101);
    }
}
