using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Runs;
using Randomiser.RandomiserCode.Appearance;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
public static class PublishStartingAppearancesPatch
{
    [HarmonyPostfix]
    public static void Postfix(RunManager __instance, RunState __0) =>
        AppearanceSynchronization.Publish(__0, __instance.NetService);
}

[HarmonyPatch(typeof(PreloadManager), nameof(PreloadManager.LoadRunAssets))]
public static class SynchronizeRunAppearancesPatch
{
    [HarmonyPostfix]
    public static void Postfix(ref Task __result) => __result = AfterPreload(__result);

    private static async Task AfterPreload(Task preload)
    {
        await preload;
        if (RunManager.Instance?.DebugOnlyGetState() is { } run)
            await AppearanceSynchronization.EnsureReady(run);
    }
}

[HarmonyPatch(typeof(PreloadManager), nameof(PreloadManager.LoadRoomCombatAssets))]
public static class SynchronizeCombatAppearancesPatch
{
    [HarmonyPostfix]
    public static void Postfix(IRunState __1, ref Task __result) => __result = AfterPreload(__result, __1);

    private static async Task AfterPreload(Task preload, IRunState run)
    {
        await preload;
        await AppearanceSynchronization.EnsureReady(run);
    }
}
