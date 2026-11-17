using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Randomiser.RandomiserCode.Patches;

// The original encounter contains only segments, so its final death animation
// removes every enemy node. Mixed randomized groups must retain other enemies.
[HarmonyPatch(typeof(ReattachPower), "DoFadeOutOnAllSegments")]
public static class DecimillipedeDeathPatch
{
    public static IReadOnlyList<Creature> GetFadeTargets(ICombatState state) =>
        state.Encounter is { } encounter && RandomEncounterPatch.IsRandomized(encounter)
            ? state.Enemies.Where(creature => creature.Monster is DecimillipedeSegment).ToArray()
            : state.Enemies;

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.PropertyGetter(typeof(ICombatState), nameof(ICombatState.Enemies));
        var replacement = AccessTools.Method(typeof(DecimillipedeDeathPatch), nameof(GetFadeTargets));
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(original))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Expected one segment death enemy lookup; found {replaced}.");
    }
}
