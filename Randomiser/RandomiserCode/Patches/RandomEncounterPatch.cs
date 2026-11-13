using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.GenerateMonstersWithSlots))]
public static class RandomEncounterPatch
{
    private static readonly ConditionalWeakTable<EncounterModel, object> Randomized = new();
    private static readonly AccessTools.FieldRef<EncounterModel, Rng> EncounterRng =
        AccessTools.FieldRefAccess<EncounterModel, Rng>("_rng");
    private static readonly MethodInfo OriginalGenerator = AccessTools.Method(typeof(EncounterModel), "GenerateMonsters");
    private static readonly FieldInfo RegisteredModels = AccessTools.Field(typeof(ModelDb), "_contentById");

    public static bool IsRandomized(EncounterModel encounter) => Randomized.TryGetValue(encounter, out _);

    // The original method still initializes the encounter RNG, stores the roster,
    // and validates mutable monsters. Only its roster-generation call is replaced.
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo replacement = AccessTools.Method(typeof(RandomEncounterPatch), nameof(Generate));
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(OriginalGenerator))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return new CodeInstruction(OpCodes.Call, replacement);
                replaced++;
            }
            else
                yield return instruction;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Expected one encounter roster generator; found {replaced}.");
    }

    public static IReadOnlyList<(MonsterModel, string?)> Generate(EncounterModel encounter, IRunState runState)
    {
        // Bestiary previews also generate monsters; leave those alone.
        if (runState is NullRunState)
            return (IReadOnlyList<(MonsterModel, string?)>)OriginalGenerator.Invoke(encounter, null)!;

        // ModelDb.Monsters only walks act encounters, omitting standalone pets
        // and some event-only models. Read the complete registered model database.
        var models = (Dictionary<ModelId, AbstractModel>)RegisteredModels.GetValue(null)!;
        MonsterModel[] pool = models.Values.OfType<MonsterModel>()
            .Where(monster => monster is not BigDummy)
            .OrderBy(monster => monster.Id.Category, StringComparer.Ordinal)
            .ThenBy(monster => monster.Id.Entry, StringComparer.Ordinal).ToArray();
        if (pool.Length == 0)
            throw new InvalidOperationException("The monster database is empty.");

        IReadOnlyList<(MonsterModel, string?)> roster = RollRoster(EncounterRng(encounter), pool);
        Randomized.GetValue(encounter, _ => new object());
        MainFile.Logger.Info($"Random encounter {encounter.Id}: {roster.Count} enemies: {string.Join(", ", roster.Select(entry => entry.Item1.Id))}");
        return roster;
    }

    public static IReadOnlyList<(MonsterModel, string?)> RollRoster(Rng rng, IReadOnlyList<MonsterModel> pool)
    {
        if (pool.Count == 0)
            throw new ArgumentException("The monster pool is empty.", nameof(pool));
        int count = rng.NextInt(1, 5); // Upper bound is exclusive.
        var roster = new List<(MonsterModel, string?)>(count);
        for (int i = 0; i < count; i++)
        {
            // No act, rarity, boss, event, summon or pet restrictions. Duplicates
            // are allowed and each roll gets its own mutable monster instance.
            MonsterModel monster = pool[rng.NextInt(pool.Count)].ToMutable();
            roster.Add((monster, null));
        }

        return roster;
    }
}

// Fixed encounter scenes contain slots for the original group. Use the game's
// automatic layout instead, while retaining the room type and background.
[HarmonyPatch]
public static class RandomEncounterLayoutPatch
{
    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods() =>
        typeof(EncounterModel).Assembly.GetTypes()
            .Where(type => typeof(EncounterModel).IsAssignableFrom(type))
            .Select(type => type.GetMethod("get_HasScene", BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .OfType<MethodInfo>().Where(method => !method.IsAbstract);

    [HarmonyPrefix]
    public static bool Prefix(EncounterModel __instance, ref bool __result)
    {
        if (!RandomEncounterPatch.IsRandomized(__instance))
            return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.GetNextSlot))]
public static class RandomEncounterSummonSlotPatch
{
    [HarmonyPrefix]
    public static bool Prefix(EncounterModel __instance, ref string __result)
    {
        if (!RandomEncounterPatch.IsRandomized(__instance))
            return true;
        __result = "";
        return false;
    }
}
