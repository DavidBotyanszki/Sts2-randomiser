using MegaCrit.Sts2.Core.Entities.Players;

namespace Randomiser.RandomiserCode.Patches;

public static class StartingHpPatch
{
    public static void Postfix(Player __result)
    {
        var rng = StartingLoadoutPatch.GetRng(__result, "StartingHp")
            ?? throw new InvalidOperationException("Starting HP must be initialized with the shared run seed.");
        int startingHp = rng.NextInt(40, 101);
        __result.Creature.SetMaxHpInternal(startingHp);
        __result.Creature.SetCurrentHpInternal(startingHp);
    }
}
