using System;
using VoidMage.Spells;

namespace VoidMage.Combat;

/// <summary>Where a spell hit came from in the cast's lineage, and how the host should treat it.</summary>
[Flags]
public enum SpellHitFlags
{
	None = 0,
	/// <summary>A free miniature cast: The Conduit, a Familiar's copy, The Hail, The Retort, The Edge (no Hollow refund, no Echo, never multiplied).</summary>
	Proc = 1,
	/// <summary>An Echo repeat.</summary>
	Echo = 2,
	/// <summary>The Stranger's random spell fired alongside the cast.</summary>
	StrangerCopy = 4,
	/// <summary>The Mirror's copy of the whole pattern in another direction (multiplied like the cast; never mirrored again).</summary>
	Mirror = 8,
	/// <summary>Dealt by a raised minion (its blast): procs nothing.</summary>
	FromMinion = 16,
	/// <summary>Splash or zone damage rather than the direct victim.</summary>
	Area = 32,
	/// <summary>A damage-over-time or hazard tick: no stagger, no guard, numbers rate-limited.</summary>
	Tick = 64,
	/// <summary>A Fracture shard: never fractures again.</summary>
	Shard = 128,
	/// <summary>Made by the host out of another hit (a Storm arc, a Fuse going off, a Pyre): triggers no host effect again.</summary>
	Secondary = 256,
	/// <summary>A ray's own strike (the Void Lance): still a Tick, but every one shows its own number instead of being summed.</summary>
	Pulse = 512,
	/// <summary>The Edge: the sword's own strike carrying the last spell. A real cast, not a blade, is what lodges The Fuse (user, 2026-09-22).</summary>
	Blade = 1024,
}

/// <summary>
/// The compact, RPC-safe context that rides every spell hit from the caster's client to the host, so the host can
/// resolve statuses, the brands, The Turncoat and The Hollow's refund without asking the owner anything.
/// Six plain values: the spell, the staff's packed modifier stacks (two longs), the proc depth, an id for the cast and
/// the lineage flags. `default` means "not a spell".
/// </summary>
public readonly struct SpellHitInfo
{
	public string SpellId { get; init; }
	/// <summary><see cref="ModifierStacks"/>.Lo.</summary>
	public long Mods { get; init; }
	/// <summary><see cref="ModifierStacks"/>.Hi.</summary>
	public long Mods2 { get; init; }
	/// <summary>0 a direct cast, 1 a Conduit/Echo proc, +1 per minion blast. The host refuses past MaxProcDepth.</summary>
	public int Depth { get; init; }
	/// <summary>Owner-unique per cast (one id per pipeline run, shared by its splash and hazards).</summary>
	public int CastId { get; init; }
	public SpellHitFlags Flags { get; init; }

	public static readonly SpellHitInfo None = default;

	public bool IsSpell => !string.IsNullOrEmpty( SpellId );
	public ModifierStacks Stacks => new( Mods, Mods2 );
	public int Stack( ModifierId id ) => Stacks[id];
	public bool Has( SpellHitFlags flag ) => (Flags & flag) != 0;
	public SpellDefinition Spell => SpellDatabase.Get( SpellId );

	public SpellHitInfo WithStack( ModifierId id, int count )
	{
		var s = Stacks.With( id, count );
		return this with { Mods = s.Lo, Mods2 = s.Hi };
	}
	public SpellHitInfo With( SpellHitFlags add, int depthDelta = 0 ) => this with { Flags = Flags | add, Depth = Depth + depthDelta };

	public override string ToString() => IsSpell ? $"{SpellId} [{Stacks}] depth {Depth} cast {CastId} {Flags}" : "none";
}
