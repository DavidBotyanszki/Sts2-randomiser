namespace Randomiser.RandomiserCode.Appearance;

// Resolves clips without depending on a monster's AI, phases or move state machine.
public sealed class CombatAnimationSet
{
    private readonly HashSet<string> _names;
    public string? Idle { get; }
    public string? Attack { get; }
    public string? Hurt { get; }
    public string? Death { get; }

    public CombatAnimationSet(IEnumerable<string> names)
    {
        _names = new HashSet<string>(names, StringComparer.Ordinal);
        Idle = First("idle_loop", "idle", "combat_idle_loop", "awake_loop", "sleep_loop")
            ?? Matching(name => name.Contains("idle", StringComparison.OrdinalIgnoreCase))
            ?? First("burrowed_loop", "stunned_loop");
        Attack = First("attack", "attack_single", "attack_light")
            ?? Matching(name => name.StartsWith("attack_", StringComparison.Ordinal) && !name.EndsWith("_loop"))
            ?? First("headbutt", "bite", "slash")
            ?? Idle;
        Hurt = First("hurt", "hit")
            ?? Matching(name => name.StartsWith("hurt_", StringComparison.Ordinal))
            ?? Idle;
        Death = First("die", "death", "dead", "dead_loop")
            ?? Matching(name => name.StartsWith("die_", StringComparison.Ordinal) && !name.EndsWith("_loop"))
            ?? Idle;
    }

    public string? ForTrigger(string trigger) => trigger switch
    {
        "Idle" or "Relaxed" or "Revive" => Idle,
        "Attack" => Attack,
        "Hit" => Hurt,
        "Dead" => Death,
        _ => ForAnimation(trigger.ToLowerInvariant())
    };

    public string? ForAnimation(string name)
    {
        if (_names.Contains(name))
            return name;
        if (name.StartsWith("hurt", StringComparison.OrdinalIgnoreCase) || name.Equals("hit", StringComparison.OrdinalIgnoreCase))
            return Hurt;
        if (name.StartsWith("die", StringComparison.OrdinalIgnoreCase) || name.StartsWith("death", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("dead", StringComparison.OrdinalIgnoreCase))
            return Death;
        if (name.Contains("idle", StringComparison.OrdinalIgnoreCase) || name is "relaxed_loop" or "revive")
            return Idle;
        return Attack;
    }

    private string? First(params string[] names) => names.FirstOrDefault(_names.Contains);
    private string? Matching(Func<string, bool> predicate) => _names.OrderBy(name => name, StringComparer.Ordinal).FirstOrDefault(predicate);
}
