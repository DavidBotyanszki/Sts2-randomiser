using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch]
public static class FullPoolMerchantCardPatch
{
    private static readonly Action<Player, CardModel, decimal, Rng> RollForUpgrade =
        AccessTools.MethodDelegate<Action<Player, CardModel, decimal, Rng>>(
            AccessTools.Method(typeof(CardFactory), "RollForUpgrade", [typeof(Player), typeof(CardModel), typeof(decimal), typeof(Rng)]));

    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(CardFactory))
        .Where(method => method.Name == nameof(CardFactory.CreateForMerchant));

    [HarmonyPrefix]
    public static bool Prefix(Player player, IEnumerable<CardModel> blacklist, ref CardCreationResult __result)
    {
        var excluded = blacklist.Select(card => card.Id).ToHashSet();
        CardModel[] all = MultiplayerRandom.OrderedPool(ModelDb.AllCards);
        CardModel[] pool = all.Where(card => !excluded.Contains(card.Id)).ToArray();
        if (pool.Length == 0)
            pool = all;
        CardModel card = player.RunState.CreateCard(pool[player.PlayerRng.Shops.NextInt(pool.Length)], player);
        RollForUpgrade(player, card, -999999999m, player.PlayerRng.Shops);
        __result = new CardCreationResult(card);
        return false;
    }
}

[HarmonyPatch(typeof(MerchantRelicEntry), "FillSlot")]
public static class FullPoolMerchantRelicPatch
{
    private static readonly Action<MerchantRelicEntry, RelicModel> SetModel =
        AccessTools.MethodDelegate<Action<MerchantRelicEntry, RelicModel>>(
            AccessTools.Method(typeof(MerchantRelicEntry), "SetModel"));

    public static RelicModel[] GetPool(IEnumerable<RelicModel>? blacklist)
    {
        var excluded = blacklist?.Select(relic => relic.Id).ToHashSet() ?? [];
        RelicModel[] all = MultiplayerRandom.OrderedPool(ModelDb.AllRelics);
        RelicModel[] pool = all.Where(relic => !excluded.Contains(relic.Id)).ToArray();
        return pool.Length > 0 ? pool : all;
    }

    [HarmonyPrefix]
    public static bool Prefix(MerchantRelicEntry __instance, Player ____player, IEnumerable<RelicModel>? blacklist)
    {
        RelicModel[] pool = GetPool(blacklist);
        SetModel(__instance, pool[____player.PlayerRng.Shops.NextInt(pool.Length)].ToMutable());
        return false;
    }
}

[HarmonyPatch(typeof(MerchantRelicEntry), nameof(MerchantRelicEntry.CalcCost))]
public static class MerchantRelicBaseCostPatch
{
    public static int GetBaseCost(RelicModel relic)
    {
        // Starter/event/ancient relics use a sentinel price, not a real shop cost.
        // Preserve explicit custom costs when supplied by a relic subclass.
        bool hasOverride = AccessTools.PropertyGetter(relic.GetType(), nameof(RelicModel.MerchantCost)).DeclaringType != typeof(RelicModel);
        if (!hasOverride && relic.Rarity is not (RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare or RelicRarity.Shop))
            return 275;
        int cost = relic.MerchantCost;
        return cost <= 0 || cost >= 999999999 ? 275 : cost;
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.PropertyGetter(typeof(RelicModel), nameof(RelicModel.MerchantCost));
        var replacement = AccessTools.Method(typeof(MerchantRelicBaseCostPatch), nameof(GetBaseCost));
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
            throw new InvalidOperationException($"Expected one merchant relic price lookup; found {replaced}.");
    }
}
