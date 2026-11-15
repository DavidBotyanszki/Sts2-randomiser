using System.Runtime.CompilerServices;
using BaseLib.Patches.Saves;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch(typeof(RunState), nameof(RunState.CreateForNewRun))]
public static class StartingLoadoutPatch
{
    private sealed record SeedContext(string Seed);
    private static readonly ConditionalWeakTable<Player, SeedContext> Seeds = new();
    private static readonly SpireField<Player, bool> Pending = new(() => false);

    public static void Initialize() => ExtendedSaveHandlers<Player, SerializablePlayer>.RegisterSave<bool>(
        "Randomiser.StartingLoadoutPending", player => Pending.Get(player), Pending.Set,
        (value, writer) => writer.WriteBool(value), reader => reader.ReadBool());

    public static void MarkPending(Player player) => Pending.Set(player, true);

    public static void InitializeJoiningPlayer(Player player)
    {
        if (Pending.Get(player) && player.RunState is not NullRunState)
            Apply(player, player.RunState.Rng.StringSeed);
    }

    [HarmonyPrefix]
    public static void Prefix(IReadOnlyList<Player> __0, string __6)
    {
        // All peers receive the seed here, before CreateShared registers cards
        // and before AfterCreated, ascension changes or starting relic pickups.
        // Save loading uses a separate path and never invokes this initializer.
        foreach (Player player in __0)
            Apply(player, __6);
    }

    private static void Apply(Player player, string seed)
    {
        Seeds.Add(player, new SeedContext(seed));
        try
        {
            StartingHpPatch.Postfix(player);
            StartingGoldPatch.Postfix(player);
            StartingDeckPatch.Prefix(player);
            StartingRelicPatch.Prefix(player);
            Pending.Set(player, false);
        }
        finally
        {
            Seeds.Remove(player);
        }
    }

    public static Rng? GetRng(Player player, string feature) => Seeds.TryGetValue(player, out SeedContext? context)
        ? MultiplayerRandom.ForPlayer(context.Seed, player.NetId, feature)
        : null;
}

[HarmonyPatch(typeof(Player), nameof(Player.CreateForNewRun), new[]
    { typeof(MegaCrit.Sts2.Core.Models.CharacterModel), typeof(MegaCrit.Sts2.Core.Unlocks.UnlockState), typeof(ulong) })]
public static class MarkStartingLoadoutPendingPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player __result) => StartingLoadoutPatch.MarkPending(__result);
}

[HarmonyPatch(typeof(Player), nameof(Player.InitializeSeed))]
public static class JoiningStartingLoadoutPatch
{
    [HarmonyPostfix]
    public static void Postfix(Player __instance) => StartingLoadoutPatch.InitializeJoiningPlayer(__instance);
}
