using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Unlocks;

namespace Randomiser.RandomiserCode.Patches;

// Combat generation has its own factory and never passes through CreateForReward.
[HarmonyPatch]
public static class FullPoolCombatGenerationPatch
{
    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(CardFactory), nameof(CardFactory.GetDistinctForCombat)),
        AccessTools.Method(typeof(CardFactory), nameof(CardFactory.GetForCombat))
    };

    public static CardModel[] Roll(int count, Rng rng, bool distinct)
    {
        var pool = MultiplayerRandom.OrderedPool(ModelDb.AllCards).ToList();
        var result = new List<CardModel>();
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int index = rng.NextInt(pool.Count);
            result.Add(pool[index]);
            if (distinct)
                pool.RemoveAt(index);
        }
        return result.ToArray();
    }

    [HarmonyPrefix]
    public static bool Prefix(Player player, int count, Rng rng, MethodBase __originalMethod,
        ref IEnumerable<CardModel> __result)
    {
        // Materialize immediately: enumerating choices again must not consume RNG again.
        var combat = player.Creature.CombatState ?? throw new InvalidOperationException("Card generation requires combat.");
        __result = Roll(count, rng, __originalMethod.Name == nameof(CardFactory.GetDistinctForCombat))
            .Select(card => combat.CreateCard(card, player)).ToArray();
        return false;
    }
}

// Some relics select directly from GetUnlockedCards instead of using a factory.
// Rewrite only relic/potion callers, including their async and iterator bodies,
// so character deck definitions and fixed card references keep their meaning.
[HarmonyPatch]
public static class FullPoolRelicPotionPatch
{
    private static readonly MethodInfo Original = AccessTools.Method(typeof(CardPoolModel), nameof(CardPoolModel.GetUnlockedCards));

    public static IEnumerable<CardModel> AllCards(CardPoolModel _, UnlockState __, CardMultiplayerConstraint ___) =>
        MultiplayerRandom.OrderedPool(ModelDb.AllCards);

    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods() => typeof(RelicModel).Assembly.GetTypes()
        .Where(type => IsRelicOrPotion(type))
        .SelectMany(type => AccessTools.GetDeclaredMethods(type))
        .Where(method => !method.IsAbstract && method.GetMethodBody() != null)
        .Where(method => PatchProcessor.GetOriginalInstructions(method).Any(instruction => instruction.Calls(Original)));

    private static bool IsRelicOrPotion(Type type)
    {
        for (Type? current = type; current != null; current = current.DeclaringType)
            if (typeof(RelicModel).IsAssignableFrom(current) || typeof(PotionModel).IsAssignableFrom(current))
                return true;
        return false;
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var replacement = AccessTools.Method(typeof(FullPoolRelicPotionPatch), nameof(AllCards));
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(Original))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            yield return instruction;
        }
    }
}
