using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.GenerateWithoutOffering))]
public static class CombatRewardPatch
{
    private static readonly AccessTools.FieldRef<RewardsSet, bool> AllowEmpty =
        AccessTools.FieldRefAccess<RewardsSet, bool>("_allowEmptyRewards");
    private static readonly Func<CardReward, int> OptionCount =
        AccessTools.MethodDelegate<Func<CardReward, int>>(AccessTools.PropertyGetter(typeof(CardReward), "OptionCount"));

    [HarmonyPrefix]
    public static void Prefix(RewardsSet __instance, bool ____isGenerated, out bool __state) =>
        __state = !____isGenerated && __instance.Room is CombatRoom;

    [HarmonyPostfix]
    public static void Postfix(RewardsSet __instance, bool __state, ref Task __result)
    {
        if (__state)
            __result = RandomizeAfterGeneration(__result, __instance);
    }

    private static async Task RandomizeAfterGeneration(Task generation, RewardsSet set)
    {
        // Wait for Populate, ModifyRewards and AfterModifyingRewards, so we only
        // randomize reward categories the game actually decided to provide.
        await generation;
        Randomize(set, set.Player.PlayerRng.Rewards);
    }

    public static void Randomize(RewardsSet set, Rng rng)
    {
        Reward[] original = set.Rewards.ToArray();
        var replacements = new List<Reward>();
        Player player = set.Player;

        if (original.OfType<GoldReward>().Any())
            replacements.Add(new GoldReward(rng.NextInt(0, 101), player));

        if (original.OfType<PotionReward>().Any())
        {
            int count = rng.NextInt(0, 6);
            for (int i = 0; i < count; i++)
            {
                var reward = new PotionReward(player);
                reward.Populate();
                replacements.Add(reward);
            }
        }

        CardReward? cardTemplate = original.OfType<CardReward>().FirstOrDefault();
        if (cardTemplate != null)
        {
            int count = rng.NextInt(0, 6);
            for (int i = 0; i < count; i++)
            {
                // Keep real card pools in the options so normal reward save/load
                // remains valid. The card factory patch below bypasses their filters.
                var options = new CardCreationOptions(MultiplayerRandom.OrderedPool(ModelDb.AllCardPools),
                    CardCreationSource.Encounter, CardRarityOddsType.Uniform);
                var reward = new CardReward(options, OptionCount(cardTemplate), player)
                {
                    CanReroll = cardTemplate.CanReroll,
                    CanSkip = cardTemplate.CanSkip
                };
                reward.Populate();
                replacements.Add(reward);
            }

            // The original card rewards subscribe to relic pickups. Remove their
            // handlers when discarding them, so invisible rewards cannot reroll.
            var onRelicObtained = AccessTools.Method(typeof(CardReward), "OnRelicObtained");
            foreach (CardReward discarded in original.OfType<CardReward>())
                player.RelicObtained -= onRelicObtained.CreateDelegate<Action<RelicModel>>(discarded);
        }

        if (original.OfType<RelicReward>().Any())
        {
            int count = rng.NextInt(0, 6);
            RelicModel[] pool = MultiplayerRandom.OrderedPool(ModelDb.AllRelics);
            for (int i = 0; i < count; i++)
            {
                // Predetermined mutable relics still use RelicReward.OnSelect,
                // including RelicCmd.Obtain and the relic's pickup effects.
                RelicModel relic = pool[rng.NextInt(pool.Length)].ToMutable();
                replacements.Add(new RelicReward(relic, player));
            }
        }

        // Preserve any other reward types provided by events or other mods.
        replacements.AddRange(original.Where(reward => reward is not GoldReward and not PotionReward
            and not CardReward and not RelicReward));
        replacements.Sort((left, right) => left.RewardsSetIndex.CompareTo(right.RewardsSetIndex));
        set.Rewards.Clear();
        set.Rewards.AddRange(replacements);
        if (set.Rewards.Count == 0)
            AllowEmpty(set) = true;
    }
}

// Replace only the single-card selection step. The outer factory retains normal
// option counts, upgrade rolls and card reward hooks.
[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.CreateForReward),
    new[] { typeof(Player), typeof(IEnumerable<CardModel>), typeof(CardCreationOptions) })]
public static class CombatRewardCardPoolPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Player __0, IEnumerable<CardModel> __1, CardCreationOptions __2,
        ref CardModel __result)
    {
        CardModel[] allCards = MultiplayerRandom.OrderedPool(ModelDb.AllCards);
        var alreadyOffered = __1.Select(card => card.Id).ToHashSet();
        CardModel[] pool = allCards.Where(card => !alreadyOffered.Contains(card.Id)).ToArray();
        if (pool.Length == 0)
            pool = allCards;
        Rng rng = __2.RngOverride ?? __0.PlayerRng.Rewards;
        CardModel selected = pool[rng.NextInt(pool.Length)];
        __result = __0.RunState.CreateCard(selected, __0);
        return false;
    }
}
