using System.Globalization;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace Randomiser.RandomiserCode;

public static class MultiplayerRandom
{
    public static Rng ForPlayer(string runSeed, ulong playerId, string feature) =>
        new(StringHelper.GetDeterministicHashCode(runSeed),
            $"Randomiser.{feature}.v1/{playerId.ToString("X16", CultureInfo.InvariantCulture)}");

    public static T[] OrderedPool<T>(IEnumerable<T> models) where T : AbstractModel => models
        .DistinctBy(model => model.Id)
        .OrderBy(model => model.Id.Category, StringComparer.Ordinal)
        .ThenBy(model => model.Id.Entry, StringComparer.Ordinal).ToArray();
}
