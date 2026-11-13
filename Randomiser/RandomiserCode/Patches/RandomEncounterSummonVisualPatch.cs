using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature))]
public static class RandomEncounterSummonVisualPatch
{
    private static readonly Action<NCombatRoom, List<NCreature>, float> PositionEnemies =
        AccessTools.MethodDelegate<Action<NCombatRoom, List<NCreature>, float>>(
            AccessTools.Method(typeof(NCombatRoom), "PositionEnemies"));

    public static bool UsesAutomaticLayout(Creature creature) =>
        creature.Side == CombatSide.Enemy && creature.CombatState?.Encounter is { } encounter &&
        RandomEncounterPatch.IsRandomized(encounter);

    public static string? GetVisualSlot(Creature creature) =>
        UsesAutomaticLayout(creature) ? null : creature.SlotName;

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var slotGetter = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.SlotName));
        var visualSlotGetter = AccessTools.Method(typeof(RandomEncounterSummonVisualPatch), nameof(GetVisualSlot));
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(slotGetter))
            {
                // Change only this UI method's interpretation of the slot. Keep
                // Creature.SlotName intact for enemy AI, save data and checksums.
                instruction.opcode = OpCodes.Call;
                instruction.operand = visualSlotGetter;
                replaced++;
            }
            yield return instruction;
        }
        if (replaced == 0)
            throw new InvalidOperationException("Could not find creature slot placement in NCombatRoom.AddCreature.");
    }

    [HarmonyPostfix]
    public static void Postfix(Creature creature, NCombatRoom __instance, List<NCreature> ____creatureNodes)
    {
        if (!UsesAutomaticLayout(creature) || creature.SlotName == null)
            return;

        // Named summons used to be positioned by their encounter's fixed scene.
        // Reflow the enemy group so the new creature gets an actual screen position.
        List<NCreature> enemies = ____creatureNodes
            .Where(node => node.Entity.Side == CombatSide.Enemy).ToList();
        PositionEnemies(__instance, enemies, creature.CombatState!.Encounter!.GetCameraScaling());
    }
}
