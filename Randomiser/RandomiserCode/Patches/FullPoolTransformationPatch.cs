using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace Randomiser.RandomiserCode.Patches;

[HarmonyPatch]
public static class FullPoolTransformationPatch
{
    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(CardFactory))
        .Where(method => method.Name == nameof(CardFactory.CreateRandomCardForTransform));

    [HarmonyPrefix]
    public static bool Prefix(object[] __args, ref CardModel __result)
    {
        CardModel original = (CardModel)__args[0];
        Rng rng = __args.OfType<Rng>().Single();
        CardModel[] pool = MultiplayerRandom.OrderedPool(ModelDb.AllCards);
        __result = (original.CardScope ?? throw new InvalidOperationException("Transformed card has no scope."))
            .CreateCard(pool[rng.NextInt(pool.Length)], original.Owner);
        return false;
    }
}
