using System.Globalization;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Spells;

/// <summary>
/// Host-only tests of the spell system's host side, each on the nearest skeleton to the local player (TrapNet's
/// Resolve shape) or at the crosshair:
///   spell_test_raise [stacks]         spell_test_status slow|burn [seconds]     spell_test_area [radius] [damage]
///   spell_test_turncoat [stacks]      spell_test_wake [seconds]         skeleton_teams
///   spell_test_leech [damage]         spell_test_wrath
/// </summary>
public static class SpellTestCommands
{
	private static bool Host()
	{
		if ( Networking.IsHost ) return true;
		Log.Info( "[Spell] host only" );
		return false;
	}

	private static float F( string s, float fallback ) => float.TryParse( s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : fallback;
	private static int I( string s, int fallback ) => int.TryParse( s, out var v ) ? v : fallback;

	private static SkeletonNpc Nearest( Team team = Team.Hostile )
	{
		var local = PlayerHealth.Local;
		if ( !local.IsValid() ) { Log.Info( "[Spell] no local player" ); return null; }
		var npc = Game.ActiveScene.GetAllComponents<SkeletonNpc>().Where( n => n.IsValid() && !n.IsDead && n.Team == team ).OrderBy( n => n.WorldPosition.DistanceSquared( local.WorldPosition ) ).FirstOrDefault();
		if ( npc is null ) Log.Info( $"[Spell] no live {team} skeleton" );
		return npc;
	}

	private static Vector3? Crosshair()
	{
		var scene = Game.ActiveScene;
		var camera = scene?.Camera;
		if ( !camera.IsValid() ) return null;
		var tr = scene.Trace.Ray( camera.WorldPosition, camera.WorldPosition + camera.WorldRotation.Forward * 4000f ).WithoutTags( AimSolver.IgnoreTags ).Run();
		return tr.Hit ? tr.HitPosition : tr.EndPosition;
	}

	private static SpellHitInfo TestInfo( ModifierId mod, int stacks, string spell = SpellIds.Fireball )
		=> new() { SpellId = spell, Mods = ModifierStacks.Empty.With( mod, stacks ).Lo, Mods2 = ModifierStacks.Empty.With( mod, stacks ).Hi, Depth = 0, CastId = CastContext.NewCastId(), Flags = SpellHitFlags.None };

	/// <summary>spell_test_raise [stacks]: the nearest hostile dies quietly and rises as an ally of the local player.</summary>
	[ConCmd( "spell_test_raise" )]
	public static void RaiseCommand( string stacks = "1" )
	{
		if ( !Host() ) return;
		var npc = Nearest(); if ( npc is null ) return;
		var info = TestInfo( ModifierId.Turncoat, I( stacks, 1 ) );
		npc.ApplyHitHost( new DamageRouter.Hit { Damage = 100000f, Position = npc.WorldPosition + Vector3.Up * 45f, Push = Vector3.Zero, Attacker = PlayerHealth.Local.GameObject, Kind = "spell_fireball", Spell = info } );
		Log.Info( $"[Spell] killed {npc.KindName} with Turncoat x{I( stacks, 1 )}; it rises in {SpellHostTuning.TurncoatRaiseDelay:0.0} s" );
	}

	/// <summary>spell_test_status slow|burn [seconds]: the nearest hostile is slowed (0.5) or set burning (8 dps).</summary>
	[ConCmd( "spell_test_status" )]
	public static void StatusCommand( string what = "slow", string seconds = "4" )
	{
		if ( !Host() ) return;
		var npc = Nearest(); if ( npc is null ) return;
		var s = F( seconds, 4f );
		if ( what.StartsWith( "b" ) ) npc.Status.Burn( 8f, s, "spell_void_orb", PlayerHealth.Local.GameObject, TestInfo( ModifierId.None, 0, SpellIds.VoidOrb ) );
		else npc.Status.Slow( 0.5f, s );
		Log.Info( $"[Spell] {what} on {npc.KindName} for {s:0.0} s" );
	}

	/// <summary>spell_test_area [radius] [damage]: a splash at the crosshair against every hostile in radius.</summary>
	[ConCmd( "spell_test_area" )]
	public static void AreaCommand( string radius = "150", string damage = "20" )
	{
		if ( !Host() ) return;
		var at = Crosshair(); if ( at is null ) return;
		var r = F( radius, 150f );
		var template = new DamageRouter.Hit { Damage = F( damage, 20f ), Position = at.Value, Push = Vector3.Zero, Attacker = PlayerHealth.Local?.GameObject, Kind = "spell_fireball", Spell = TestInfo( ModifierId.None, 0 ) };
		var n = SpellArea.Damage( at.Value, r, template, Team.Hostile );
		SpellVfx.Burst( Game.ActiveScene, at.Value, Vector3.Up, SpellDatabase.Get( SpellIds.Fireball ), r / 150f, ImpactKind.World );
		Log.Info( $"[Spell] area {r:0} u at {at.Value:0}: {n} hit" );
	}


	/// <summary>spell_test_leech [damage]: the nearest hostile takes a Siphon Bolt hit from the local player; if it KILLS, the heal comes back through Refund (spell_debug 1 logs it; pass a big damage).</summary>
	[ConCmd( "spell_test_leech" )]
	public static void LeechCommand( string damage = "20" )
	{
		if ( !Host() ) return;
		var npc = Nearest(); if ( npc is null ) return;
		var health = PlayerHealth.Local;
		var before = health?.Health ?? 0f;
		var info = TestInfo( ModifierId.None, 0, SpellIds.SiphonBolt );
		npc.ApplyHitHost( new DamageRouter.Hit { Damage = F( damage, 20f ), Position = npc.WorldPosition + Vector3.Up * 45f, Push = Vector3.Zero, Attacker = health?.GameObject, Kind = "spell_siphon_bolt", Spell = info } );
		Log.Info( $"[Spell] siphon hit {npc.KindName} for {F( damage, 20f ):0}: health {before:0.#} -> {health?.Health ?? 0f:0.#} (max {health?.EffectiveMaxHealth ?? 0f:0})" );
	}

	/// <summary>spell_test_wrath: the local player's cap, missing fraction and the multiplier the active spell resolves to.</summary>
	[ConCmd( "spell_test_wrath" )]
	public static void WrathCommand()
	{
		var health = PlayerHealth.Local;
		var loadout = StaffLoadout.Local;
		if ( !health.IsValid() || !loadout.IsValid() ) { Log.Info( "[Spell] no local player" ); return; }
		var missing = 1f - health.Health01;
		var spell = loadout.ActiveSpell;
		var preview = spell is null ? default : ResolvedCast.Preview( spell, loadout.Stacks, PlayerShards.Local?.Carried ?? 0, SpellCaster.Local?.KindlingActive ?? false, missing );
		Log.Info( $"[Spell] wrath x{loadout.Stacks[ModifierId.Wrath]}: max {health.EffectiveMaxHealth:0} (scale {health.MaxHealthScale:0.00}), health {health.Health:0.#}, missing {missing * 100f:0}% -> +{preview.WrathPct * 100f:0}% damage, x{preview.DamageMultiplier:0.00} total" );
	}

	/// <summary>spell_test_turncoat [stacks]: same as spell_test_raise (the modifier's name).</summary>
	[ConCmd( "spell_test_turncoat" )]
	public static void TurncoatCommand( string stacks = "2" ) => RaiseCommand( stacks );

	/// <summary>spell_test_wake [seconds]: a burning line from the player forward, on the host's hazard list.</summary>
	[ConCmd( "spell_test_wake" )]
	public static void WakeCommand( string seconds = "6" )
	{
		if ( !Host() ) return;
		var local = PlayerHealth.Local;
		var camera = Game.ActiveScene?.Camera;
		if ( !local.IsValid() || !camera.IsValid() ) return;
		var from = local.WorldPosition + Vector3.Up * 30f + camera.WorldRotation.Forward.WithZ( 0f ).Normal * 60f;
		var to = from + camera.WorldRotation.Forward.WithZ( 0f ).Normal * 600f;
		SpellNet.AddHazard( HazardKind.Capsule, from, to, 30f, 60f, 0f, F( seconds, 6f ), null, SpellHostTuning.WakeFireDps, 0f, local.GameObject, TestInfo( ModifierId.Wake, 1 ) );
		HazardField.Debug = true;
		Log.Info( $"[Spell] wake line {from:0} -> {to:0} for {F( seconds, 6f ):0} s (spell_hazard_debug on)" );
	}

	[ConCmd( "skeleton_teams" )]
	public static void TeamsCommand()
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		foreach ( var n in scene.GetAllComponents<SkeletonNpc>() )
			Log.Info( $"[Spell] {n.KindName,-14} {n.Team,-8} {n.State,-9} hp {n.Health:0}/{n.MaxHealth:0}{(n.Raised ? $" raised x{n.RaisedStacks}, {n.RaisedUntil - Time.Now:0.0} s left" : "")}{(n.SlowAmount > 0f ? $" slow {n.SlowAmount:0.00}" : "")}{(n.BurnKind > 0 ? " burning" : "")}" );
	}
}
