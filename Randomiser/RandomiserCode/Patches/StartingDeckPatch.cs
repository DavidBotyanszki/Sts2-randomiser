using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch(typeof(Player), "PopulateStartingDeck")]
public static class StartingDeckPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Player __instance)
    {
        // AllCards includes every character's starters and all shared pools,
        // including curses, statuses, event, quest and token cards.
        CardModel[] cards = ModelDb.AllCards.ToArray();
        CardModel[] attacks = cards.Where(card => card.Type == CardType.Attack).ToArray();
        int deckSize = System.Random.Shared.Next(1, 21);

        for (int i = 0; i < deckSize; i++)
        {
            CardModel[] pool = i == 0 ? attacks : cards;
            // Roll with replacement; duplicates are separate mutable card instances.
            CardModel card = pool[System.Random.Shared.Next(pool.Length)].ToMutable();
            card.FloorAddedToDeck = 1;
            __instance.Deck.AddInternal(card);
        }

        return false;
    }
}
