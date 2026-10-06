using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Spells;

/// <summary>
/// Host only: what the 2026-09-20 scrolls do to a body a spell has HIT (the brands, The Fuse, The Ram) and to one a
/// spell has KILLED (The Pyre, The Harvest), read off the staff's stacks riding the hit. Everything the host makes
/// out of a hit is flagged Secondary, and a Secondary hit triggers nothing here, so an arc never brands, a fuse never
/// lodges a fuse and one pyre never lights the next.
/// </summary>
public static class SpellHostEffects
{
	[ConVar( "spell_embers_dps_1" )] public static float EmbersDps1 { get; set; } = 4f;
	[ConVar( "spell_embers_dps_2" )] public static float EmbersDps2 { get; set; } = 7f;
	[ConVar( "spell_embers_dps_3" )] public static float EmbersDps3 { get; set; } = 10f;
	[ConVar( "spell_embers_seconds" )] public static float EmbersSeconds { get; set; } = 3f;
	[ConVar( "spell_frost_seconds" )] public static float FrostSeconds { get; set; } = 2.5f;
	private static readonly float[] FrostSlow = { 0f, 0.3f, 0.45f, 0.6f };

	[ConVar( "spell_storm_range" )] public static float StormRange { get; set; } = 320f;
	[ConVar( "spell_storm_fraction" )] public static float StormFraction { get; set; } = 0.3f;

	[ConVar( "spell_fuse_seconds" )] public static float FuseSeconds { get; set; } = 1.5f;
	[ConVar( "spell_fuse_radius" )] public static float FuseRadius { get; set; } = 90f;
	private static readonly float[] FusePct = { 0f, 0.5f, 1f, 1.5f };

	private static readonly float[] RamDistance = { 0f, 110f, 160f, 210f };
	private static readonly float[] RamSlamDamage = { 0f, 10f, 20f, 30f };
	[ConVar( "spell_ram_seconds" )] public static float RamSeconds { get; set; } = 0.22f;

	private static readonly float[] PyreDamage = { 0f, 20f, 35f, 50f };
	private static readonly float[] PyreRadius = { 0f, 130f, 160f, 190f };
	/// <summary>Per kill, on floors that carry a hundred bodies.</summary>
	private static readonly float[] HarvestMana = { 0f, 2f, 3f, 5f };

	public static float EmbersDps( int stacks ) => stacks >= 3 ? EmbersDps3 : stacks == 2 ? EmbersDps2 : stacks == 1 ? EmbersDps1 : 0f;

	private sealed class Fuse { public SkeletonNpc Npc; public float DueAt, Damage; public GameObject Attacker; public SpellHitInfo Info; public string Kind; }
	private static readonly List<Fuse> _fuses = new();

	// ------------------------------------------------------------------ a hit that did not kill

	/// <summary>A tick is a slice of a cast (a beam's 0.15 s, an area's quarter second): what it lodges, arcs or shoves with is weighed up to about a cast's worth, and only so often per body.</summary>
	private const float TickWeight = 4f;
	private const float TickEverySeconds = 0.6f;
	private static readonly Dictionary<Guid, float> _nextTickEffect = new();

	/// <summary>
	/// Host, from SpellHostTuning.ApplyStatus: the victim lived. Ticks come here too (user, 2026-09-20: every scroll
	/// must do something on every spell, and the laser and the void orb only ever deal ticks): they brand like any
	/// hit, and lodge, arc and shove at most once per body per TickEverySeconds.
	/// </summary>
	public static void OnHit( SkeletonNpc npc, in DamageRouter.Hit hit )
	{
		var info = hit.Spell;
		if ( !info.IsSpell || info.Has( SpellHitFlags.Secondary ) || info.Has( SpellHitFlags.FromMinion ) ) return;
		var stacks = info.Stacks;
		if ( stacks.IsEmpty ) return;
		var tick = info.Has( SpellHitFlags.Tick );

		// the brands: whatever the spell is, the body burns and slows (the splash and the areas brand too). The
		// burn has a kind of its own, so it never merges with the spell's own and its ticks (Secondary) start nothing.
		var embers = stacks[ModifierId.BrandEmbers];
		if ( embers > 0 ) npc.Status.Burn( EmbersDps( embers ), EmbersSeconds, "spell_embers", hit.Attacker, info.With( SpellHitFlags.Secondary ) );
		var frost = stacks[ModifierId.BrandFrost];
		if ( frost > 0 ) npc.Status.Slow( FrostSlow[frost], FrostSeconds );

		// the rest is for the body the spell actually touched. An orb never touches anybody: its area IS its touch.
		if ( info.Has( SpellHitFlags.Area ) && !(tick && info.Spell?.Delivery == SpellDelivery.Orb) ) return;
		if ( !stacks.Has( ModifierId.BrandStorm ) && !stacks.Has( ModifierId.Fuse ) && !stacks.Has( ModifierId.Ram ) ) return;
		if ( tick )
		{
			var id = npc.GameObject.Id;
			if ( _nextTickEffect.TryGetValue( id, out var next ) && Time.Now < next ) return;
			if ( _nextTickEffect.Count > 256 ) _nextTickEffect.Clear();
			_nextTickEffect[id] = Time.Now + TickEverySeconds;
		}
		var worth = hit.Damage * (tick ? TickWeight : 1f);

		var storm = stacks[ModifierId.BrandStorm];
		if ( storm > 0 ) StormArcs( npc, hit, storm, worth );

		// spells only (user, 2026-09-22): the sword carrying a spell (The Edge) brands and shoves but lodges no charge
		var fuse = stacks[ModifierId.Fuse];
		if ( fuse > 0 && !info.Has( SpellHitFlags.Blade ) && !_fuses.Any( f => f.Npc == npc ) )
		{
			_fuses.Add( new Fuse { Npc = npc, DueAt = Time.Now + FuseSeconds, Damage = worth * FusePct[fuse], Attacker = hit.Attacker, Info = info.With( SpellHitFlags.Secondary ), Kind = hit.Kind } );
			SpellNet.ShowFuse( npc.GameObject, info.SpellId, FuseSeconds );
		}

		var ram = stacks[ModifierId.Ram];
		if ( ram > 0 ) Ram( npc, hit, ram, tick ? 0.6f : 1f );
	}

	private static void StormArcs( SkeletonNpc npc, in DamageRouter.Hit hit, int arcs, float worth )
	{
		var from = Chest( npc );
		var near = CombatTargets.Bodies( npc.Team )
			.Where( b => b.IsValid() && b.GameObject != npc.GameObject && b.CanBeTargeted && b.AimPoint.Distance( from ) <= StormRange )
			.OrderBy( b => b.AimPoint.DistanceSquared( from ) ).Take( arcs ).ToList();
		if ( near.Count == 0 ) return;
		var info = hit.Spell.With( SpellHitFlags.Secondary | SpellHitFlags.Area );
		foreach ( var body in near )
		{
			var to = body.AimPoint;
			if ( Game.ActiveScene.Trace.Ray( from, to ).WithoutTags( SpellArea.LosIgnore ).Run().Hit ) continue;
			body.ApplyHitHost( new DamageRouter.Hit { Damage = worth * StormFraction, Position = to, Push = (to - from).WithZ( 0f ).Normal, Attacker = hit.Attacker, Kind = hit.Kind, Spell = info } );
			SpellNet.ChainArc( SpellIds.ChainLightning, new[] { from, to }, 0.3f );
		}
	}

	private static void Ram( SkeletonNpc npc, in DamageRouter.Hit hit, int stacks, float scale )
	{
		var dir = hit.Push.WithZ( 0f );
		if ( dir.IsNearZeroLength && hit.Attacker.IsValid() ) dir = (npc.WorldPosition - hit.Attacker.WorldPosition).WithZ( 0f );
		if ( dir.IsNearZeroLength ) return;
		dir = dir.Normal;
		var distance = RamDistance[stacks] * scale;
		var chest = Chest( npc );
		var wall = Game.ActiveScene.Trace.Ray( chest, chest + dir * (distance + 24f) ).Radius( 10f ).WithoutTags( SpellArea.LosIgnore ).Run();
		if ( wall.Hit )
		{
			distance = MathF.Max( 0f, wall.Distance - 24f );
			npc.ApplyHitHost( new DamageRouter.Hit { Damage = RamSlamDamage[stacks], Position = wall.HitPosition, Push = Vector3.Zero, Attacker = hit.Attacker, Kind = hit.Kind, Spell = hit.Spell.With( SpellHitFlags.Secondary | SpellHitFlags.Area ) } );
			SpellNet.ShowBurst( wall.HitPosition, wall.Normal, hit.Spell.SpellId, 0.5f, false );
		}
		if ( distance > 4f && npc.IsValid() && !npc.IsDead ) npc.RamShove( dir, distance, RamSeconds );
	}

	// ------------------------------------------------------------------ a kill

	/// <summary>Host, from the death stage: a hostile body died to a spell.</summary>
	public static void OnKill( SkeletonNpc victim, in KillContext ctx )
	{
		var info = ctx.Spell;
		var stacks = info.Stacks;
		if ( stacks.IsEmpty || info.Has( SpellHitFlags.FromMinion ) ) return;
		var at = Chest( victim );

		var harvest = stacks[ModifierId.Harvest];
		if ( harvest > 0 && ctx.Attacker.IsValid() ) SpellNet.ManaMote( at, ctx.Attacker, HarvestMana[harvest] );

		var pyre = stacks[ModifierId.Pyre];
		if ( pyre > 0 && !info.Has( SpellHitFlags.Secondary ) )
		{
			var template = new DamageRouter.Hit { Damage = PyreDamage[pyre], Position = at, Attacker = ctx.Attacker, Kind = ctx.Kind, Spell = info.With( SpellHitFlags.Secondary ) };
			SpellArea.Damage( at, PyreRadius[pyre], template, Team.Hostile, victim.GameObject, falloff: true, needLos: true, maxTargets: 8 );
			SpellNet.ShowBurst( at, Vector3.Up, info.SpellId, 1.1f + 0.2f * pyre, true );
		}
	}

	/// <summary>AimPoint is ICombatant's default member: only reachable through the interface.</summary>
	private static Vector3 Chest( SkeletonNpc npc ) => ((ICombatant)npc).AimPoint;

	// ------------------------------------------------------------------ the queue

	public static void Tick( Scene scene, float now )
	{
		for ( var i = _fuses.Count - 1; i >= 0; i-- )
		{
			var f = _fuses[i];
			if ( now < f.DueAt ) continue;
			_fuses.RemoveAt( i );
			if ( !f.Npc.IsValid() || f.Npc.IsDead ) continue;
			var at = Chest( f.Npc );
			var template = new DamageRouter.Hit { Damage = f.Damage, Position = at, Attacker = f.Attacker, Kind = f.Kind, Spell = f.Info };
			SpellNet.ShowBurst( at, Vector3.Up, f.Info.SpellId, 0.8f, true );
			// the neighbours first: the carrier may die to its own charge and take its object with it
			SpellArea.Damage( at, FuseRadius, template with { Damage = f.Damage * 0.5f }, Team.Hostile, f.Npc.GameObject, falloff: true, needLos: true, maxTargets: 6 );
			f.Npc.ApplyHitHost( template );
		}
	}
}
