using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Unlocks;
using Randomiser.RandomiserCode.Appearance;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch(typeof(Player), nameof(Player.CreateForNewRun),
    new[] { typeof(CharacterModel), typeof(UnlockState), typeof(ulong) })]
public static class StartingAppearancePatch
{
    [HarmonyPostfix]
    public static void Postfix(Player __result) => CombatAppearance.Roll(__result);
}

[HarmonyPatch(typeof(Creature), nameof(Creature.CreateVisuals))]
public static class CombatVisualsPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Creature __instance, ref NCreatureVisuals? __result)
    {
        if (__instance.Player == null)
            return true;
        __result = CombatAppearance.CreateVisuals(__instance.Player);
        return __result == null;
    }
}

[HarmonyPatch(typeof(NCreatureVisuals), nameof(NCreatureVisuals._Ready))]
public static class PrepareCombatVisualsPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCreatureVisuals __instance) => CombatAppearance.PrepareVisuals(__instance);
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
public static class CombatAnimatorPatch
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(typeof(CharacterModel), nameof(CharacterModel.GenerateAnimator));
        var replacement = AccessTools.Method(typeof(CombatAppearance), nameof(CombatAppearance.CreateAnimator));
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(original))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return new CodeInstruction(OpCodes.Call, replacement);
                replaced++;
            }
            else
                yield return instruction;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Expected one player animator setup; found {replaced}.");
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.SetAnimationTrigger))]
public static class CombatAnimationTriggerPatch
{
    [HarmonyPrefix]
    public static void Prefix(NCreature __instance, string __0) => CombatAppearance.EnsureTrigger(__instance, __0);
}

// Some character effects request Spine clips directly, bypassing animation triggers.
[HarmonyPatch(typeof(MegaAnimationState), nameof(MegaAnimationState.SetAnimation))]
public static class CombatDirectAnimationPatch
{
    [HarmonyPrefix]
    public static bool Prefix(MegaAnimationState __instance, ref string animationName, ref bool loop) =>
        CombatAppearance.ResolveDirectAnimation(__instance, ref animationName, ref loop);
}

[HarmonyPatch(typeof(MegaAnimationState), nameof(MegaAnimationState.AddAnimation))]
public static class CombatQueuedAnimationPatch
{
    [HarmonyPrefix]
    public static bool Prefix(MegaAnimationState __instance, ref string animationName, ref bool loop) =>
        CombatAppearance.ResolveDirectAnimation(__instance, ref animationName, ref loop);
}
