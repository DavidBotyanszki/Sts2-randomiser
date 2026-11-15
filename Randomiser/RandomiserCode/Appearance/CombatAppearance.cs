using System.Runtime.CompilerServices;
using BaseLib.Patches.Saves;
using BaseLib.Utils;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Randomiser.RandomiserCode.Appearance;

public static class CombatAppearance
{
    private static readonly SpireField<Player, string> SavedModel = new(() => "");
    private static readonly ConditionalWeakTable<NCreatureVisuals, AbstractModel> Sources = new();
    private static readonly ConditionalWeakTable<NCreatureVisuals, CombatAnimationSet> Animations = new();
    private static readonly ConditionalWeakTable<GodotObject, CombatAnimationSet> AnimationStates = new();
    private static readonly ConditionalWeakTable<NCreatureVisuals, CreatureAnimator> Animators = new();

    public static void Initialize()
    {
        // Stored in the player's actual run save (and BaseLib's network serialization).
        ExtendedSaveHandlers<Player, SerializablePlayer>.RegisterSave<string>(
            "Randomiser.CombatAppearance", player => SavedModel.Get(player) ?? "", SavedModel.Set,
            (value, writer) => writer.WriteString(value), reader => reader.ReadString());
    }

    public static void Roll(Player player)
    {
        // A loaded/synchronized selection is authoritative. Never replace it
        // when the player is initialized again (for example after rejoining).
        if (!string.IsNullOrEmpty(SavedModel.Get(player)) || player.RunState is NullRunState)
            return;

        AbstractModel[] models = ModelDb.AllCharacters.Cast<AbstractModel>()
            .Concat(ModelDb.Monsters)
            .Where(model => ResourceLoader.Exists(VisualsPath(model)))
            .DistinctBy(model => model.Id)
            .OrderBy(model => model.Id.Category, StringComparer.Ordinal)
            .ThenBy(model => model.Id.Entry, StringComparer.Ordinal)
            .ToArray();
        if (models.Length == 0)
            throw new InvalidOperationException("No combat appearance scenes are available.");

        // Every peer knows the same run seed and NetId. Rng's string salt uses
        // the game's deterministic hash; this private stream consumes no shared
        // gameplay RNG and is independent of player initialization order.
        AbstractModel source = models[GetModelIndex(player.RunState.Rng.Seed, player.NetId, models.Length)];
        SavedModel.Set(player, $"{source.Id.Category}/{source.Id.Entry}");
        MainFile.Logger.Info($"Combat appearance for player {player.NetId}: {source.Id}");
    }

    public static int GetModelIndex(ulong runSeed, ulong netId, int modelCount)
    {
        if (modelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(modelCount));
        string playerId = netId.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        return new Rng(runSeed, $"Randomiser.CombatAppearance.v1/{playerId}").NextInt(modelCount);
    }

    public static NCreatureVisuals? CreateVisuals(Player player)
    {
        string saved = SavedModel.Get(player) ?? "";
        int separator = saved.IndexOf('/');
        // Older saves have no selection; preserve their original appearance.
        if (separator < 0)
            return null;

        AbstractModel? source = ModelDb.GetByIdOrNull<AbstractModel>(
            new ModelId(saved[..separator], saved[(separator + 1)..]));
        if (source is not CharacterModel and not MonsterModel)
        {
            MainFile.Logger.Warn($"Saved combat appearance {saved} is unavailable; using the selected character.");
            return null;
        }

        // Load directly: a monster from another act may not be in the room's preload cache.
        PackedScene? scene = ResourceLoader.Load<PackedScene>(VisualsPath(source));
        if (scene == null)
            return null;
        NCreatureVisuals visuals = scene.Instantiate<NCreatureVisuals>();
        Sources.Add(visuals, source);
        return visuals;
    }

    private static string VisualsPath(AbstractModel model) => model switch
    {
        CharacterModel character => (string)AccessTools.Property(typeof(CharacterModel), "VisualsPath").GetValue(character)!,
        MonsterModel monster => (string)AccessTools.Property(typeof(MonsterModel), "VisualsPath").GetValue(monster)!,
        _ => throw new ArgumentException("Not a creature model.", nameof(model))
    };

    public static bool IsReplacement(NCreatureVisuals visuals) => Sources.TryGetValue(visuals, out _);

    public static void PrepareVisuals(NCreatureVisuals visuals)
    {
        if (!Sources.TryGetValue(visuals, out AbstractModel? source))
            return;

        if (source is MonsterModel monster)
        {
            // Mirror the artwork, not the root: negative root scale breaks hitbox sizes.
            Node2D body = visuals.GetCurrentBody();
            body.Scale = new Vector2(-body.Scale.X, body.Scale.Y);
            if (visuals.HasSpineAnimation)
            {
                try
                {
                    visuals.SetUpSkin(monster);
                }
                catch (InvalidOperationException)
                {
                    // Some phase-specific skin selectors require a live enemy/AI.
                    // Keep the scene's default skin instead of creating enemy state.
                    MainFile.Logger.Info($"Using default combat skin for {monster.Id}.");
                }
            }
        }

        Vector2 size = visuals.Bounds.Size * visuals.Scale.Abs();
        float factor = Math.Min(1f, Math.Min(360f / Math.Max(1f, size.X), 420f / Math.Max(1f, size.Y)));
        visuals.Scale *= factor;
        visuals.DefaultScale = visuals.Scale.X;

        if (!visuals.HasSpineAnimation)
            return;
        var names = visuals.SpineBody!.GetSkeleton()?.GetData()?.GetAnimationNames() ?? [];
        var animations = new CombatAnimationSet(names);
        Animations.Add(visuals, animations);
        AnimationStates.AddOrUpdate(visuals.SpineBody.GetAnimationState().BoundObject, animations);
    }

    // Called in place of CharacterModel.GenerateAnimator, before the original
    // character can request clips that do not exist on the replacement skeleton.
    public static CreatureAnimator? CreateAnimator(CharacterModel character, MegaSprite sprite, Creature creature, NCreature node)
    {
        if (!Sources.TryGetValue(node.Visuals, out _))
            return character.GenerateAnimator(sprite, creature);
        if (!Animations.TryGetValue(node.Visuals, out CombatAnimationSet? clips) || clips.Idle == null)
            return null; // Static artwork or no idle clip: keep the scene's setup pose.

        var idle = new AnimState(clips.Idle, true);
        var animator = new CreatureAnimator(idle, sprite);
        foreach (string trigger in new[] { "Idle", "Relaxed", "Revive", "Attack", "Cast", "PowerUp", "Hit", "Dead" })
            AddTrigger(animator, clips, idle, trigger);
        Animators.Add(node.Visuals, animator);
        return animator;
    }

    public static void EnsureTrigger(NCreature node, string trigger)
    {
        if (!Animators.TryGetValue(node.Visuals, out CreatureAnimator? animator) || animator.HasTrigger(trigger))
            return;
        CombatAnimationSet clips = Animations.GetValue(node.Visuals, _ => throw new InvalidOperationException());
        AddTrigger(animator, clips, new AnimState(clips.Idle!, true), trigger);
    }

    private static void AddTrigger(CreatureAnimator animator, CombatAnimationSet clips, AnimState idle, string trigger)
    {
        string? name = clips.ForTrigger(trigger);
        if (name == null)
            return;
        AnimState state = name == clips.Idle ? idle : new AnimState(name, false);
        if (state != idle && trigger != "Dead")
            state.NextState = idle;
        animator.AddAnyState(trigger, state);
    }

    public static bool ResolveDirectAnimation(MegaAnimationState state, ref string name, ref bool loop)
    {
        if (!AnimationStates.TryGetValue(state.BoundObject, out CombatAnimationSet? clips))
            return true;
        string? resolved = clips.ForAnimation(name);
        if (resolved == null || clips.Idle == null)
            return false;
        name = resolved;
        if (resolved == clips.Idle)
            loop = true;
        return true;
    }
}
