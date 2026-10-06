using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Boss;

/// <summary>
/// The Bone Colossus's registry and dev commands. The three parts (skull, left hand, right hand) are separate
/// network-spawned roots (their transforms sync for free; children never do), each carrying a <see cref="BossPart"/>
/// that registers here, and the skull's <see cref="BossBrain"/> runs the fight on the host. The sword and the spells
/// find the parts through this list the way crates are found through BreakableNet: by the closest point of their
/// box, since a 7 m hand is not a 14 u capsule at its origin.
/// </summary>
public static class BossNet
{
	/// <summary>1: every part's box, its closest point to the local player, and a state label; 2: plus the motion (trails, targets, waypoints, keep-out) on the host.</summary>
	[ConVar( "boss_debug" )] public static int Debug { get; set; }

	private static readonly List<BossPart> _parts = new();

	/// <summary>Every enabled part, dead ones included (they unregister when destroyed).</summary>
	public static IReadOnlyList<BossPart> Parts => _parts;

	internal static void Register( BossPart part )
	{
		if ( !_parts.Contains( part ) ) _parts.Add( part );
	}

	internal static void Unregister( BossPart part ) => _parts.Remove( part );

	public static BossPart Find( BossPartKind kind ) => _parts.FirstOrDefault( p => p.IsValid() && p.Kind == kind );

	/// <summary>Live parts whose box comes within `reach` of `at` (dormant ones too: a blade in a sleeping skull wakes it).</summary>
	public static IEnumerable<BossPart> Near( Vector3 at, float reach )
	{
		var r2 = reach * reach;
		foreach ( var p in _parts )
		{
			if ( !p.IsValid() || p.Dead ) continue;
			if ( p.ClosestPoint( at ).DistanceSquared( at ) <= r2 ) yield return p;
		}
	}

	public static void Clear()
	{
		foreach ( var p in _parts.ToArray() )
			if ( p.IsValid() ) p.GameObject.Destroy();
		_parts.Clear();
		BossSummons.Clear();
	}

	// ------------------------------------------------------------------ dev commands

	/// <summary>`boss_kill [skull, l, r or all]`: host, kills a part outright (its death sequence still plays).</summary>
	[ConCmd( "boss_kill" )]
	public static void KillCommand( string which = "all" )
	{
		if ( !Networking.IsHost ) { Log.Warning( "[Boss] host only" ); return; }
		foreach ( var p in _parts.ToArray() )
		{
			if ( !p.IsValid() || p.Dead ) continue;
			var w = which.ToLowerInvariant();
			var match = w is "all" or "" || (w is "skull" && p.Kind == BossPartKind.Skull) || (w is "l" or "left" && p.Kind == BossPartKind.HandL) || (w is "r" or "right" && p.Kind == BossPartKind.HandR);
			if ( match ) p.KillHost();
		}
	}

	/// <summary>`boss_pose [l, r or both] [0-5]`: host, sets a hand's finger pose (0 idle, 1 open, 2 fist, 3 point, 4 grab, 5 splay).</summary>
	[ConCmd( "boss_pose" )]
	public static void PoseCommand( string which = "both", int pose = 0 )
	{
		if ( !Networking.IsHost ) { Log.Warning( "[Boss] host only" ); return; }
		foreach ( var p in _parts )
		{
			if ( !p.IsValid() || p.Kind == BossPartKind.Skull ) continue;
			if ( which is "both" || (which is "l" && p.Kind == BossPartKind.HandL) || (which is "r" && p.Kind == BossPartKind.HandR) )
				p.Pose = Math.Clamp( pose, 0, 5 );
		}
	}

	/// <summary>`boss_wake`: host, starts the wake-up from wherever the boss is.</summary>
	[ConCmd( "boss_wake" )]
	public static void WakeCommand()
	{
		if ( !Networking.IsHost ) { Log.Warning( "[Boss] host only" ); return; }
		BossBrain.Current?.Wake();
	}

	/// <summary>`boss_jaw [0-1]`: host, holds the jaw open by that much (a look check for the hinge).</summary>
	[ConCmd( "boss_jaw" )]
	public static void JawCommand( float open = 1f )
	{
		if ( !Networking.IsHost ) { Log.Warning( "[Boss] host only" ); return; }
		if ( BossBrain.Current.IsValid() ) BossBrain.Current.JawOpen01 = Math.Clamp( open, 0f, 1f );
	}

	/// <summary>`boss_attack [slam, sweep, bite, shake, hammer, palm, clap, ram, rake, march, rain, grab, summon or reposition] [l or r] [speed]`: host, forces a move now (a hand move defaults to the left hand if alive; the clap and the march take both). Refused while that part is in another move.</summary>
	[ConCmd( "boss_attack" )]
	public static void AttackCommand( string name = "slam", string hand = "", float speed = 1f )
	{
		if ( !Networking.IsHost ) { Log.Warning( "[Boss] host only" ); return; }
		var brain = BossBrain.Current;
		if ( !brain.IsValid() ) { Log.Warning( "[Boss] no boss" ); return; }
		if ( !brain.Awake ) { Log.Warning( "[Boss] not awake (boss_wake first)" ); return; }
		var attack = BossAttacks.Find( name );
		if ( attack is null ) { Log.Warning( $"[Boss] no move '{name}': {string.Join( ", ", BossAttacks.All.Select( a => a.Name ) )}" ); return; }
		var part = BossPartKind.Skull;
		if ( attack.UsesHand )
		{
			part = attack.OnlyHand ?? (hand is "l" or "left" ? BossPartKind.HandL : hand is "r" or "right" ? BossPartKind.HandR
				: (Find( BossPartKind.HandL ) is { Dead: false } ? BossPartKind.HandL : BossPartKind.HandR));
		}
		brain.Begin( attack, part, speed );
	}

	/// <summary>`boss_phase [1|2]`: host, forces the fight phase (2 strikes the skull's first layer off; 1 only holds while that layer is still up).</summary>
	[ConCmd( "boss_phase" )]
	public static void PhaseCommand( int phase = 2 )
	{
		if ( !Networking.IsHost ) { Log.Warning( "[Boss] host only" ); return; }
		var brain = BossBrain.Current;
		if ( !brain.IsValid() ) return;
		if ( phase >= 2 ) brain.Skull?.BreakGuardHost();
		brain.Fight = Math.Clamp( phase, 1, 2 );
	}

	/// <summary>`boss_status`: every part's health and the phase.</summary>
	[ConCmd( "boss_status" )]
	public static void StatusCommand()
	{
		var brain = BossBrain.Current;
		Log.Info( $"[Boss] phase {(brain.IsValid() ? brain.Phase.ToString() : "none")} fight {(brain.IsValid() ? brain.Fight : 0)} moves {(brain.IsValid() ? brain.RunningNames : "-")}, {_parts.Count} parts, {BossSummons.Live} minions, arena {(HellArena.Built ? "built" : "not built")}, nav {(HellArena.NavReady ? "ready" : "none")}" );
		foreach ( var p in _parts )
			if ( p.IsValid() ) Log.Info( $"   {p.Kind,-6} hp {p.Health:0}/{p.MaxHealth:0}{(p.GuardHealth > 0f ? $" guard {p.Guard:0}/{p.GuardHealth:0}" : "")} {(p.Dead ? "DEAD" : "")} pose {p.Pose} at {p.WorldPosition}" );
	}

	internal static void Draw( Scene scene )
	{
		if ( Debug <= 0 || !scene.IsValid() ) return;
		var overlay = scene.DebugOverlay;
		var local = PlayerHealth.Local;
		if ( Debug >= 2 && BossBrain.Current.IsValid() ) BossBrain.Current.DrawMotion( overlay );
		foreach ( var p in _parts )
		{
			if ( !p.IsValid() ) continue;
			var c = p.Dead ? Color.Gray : p.CanBeTargeted ? Color.Red : Color.Orange;
			// red: what the sword and the spells hit (every shape, a little bigger than the bone); yellow inside it: the
			// strike surface that hits the player (palm, fingers, wrist, pulled in by boss_hit_forgive; the skull's mouth)
			foreach ( var (world, size, strike, radius) in p.Shapes )
			{
				if ( radius > 0f )
				{
					var r = radius * MathF.Max( 0.01f, world.Scale.x );
					overlay.Sphere( new Sphere( world.Position, r ), c.WithAlpha( 0.5f ) );
					if ( strike && r > BossAttack.HitForgive ) overlay.Sphere( new Sphere( world.Position, r - BossAttack.HitForgive ), Color.Yellow.WithAlpha( 0.8f ) );
					continue;
				}
				overlay.Box( new BBox( -size * 0.5f, size * 0.5f ), c.WithAlpha( 0.5f ), transform: world );
				var inner = size - new Vector3( BossAttack.HitForgive * 2f );
				if ( strike && inner.x > 0f && inner.y > 0f && inner.z > 0f )
					overlay.Box( new BBox( -inner * 0.5f, inner * 0.5f ), Color.Yellow.WithAlpha( 0.8f ), transform: world );
			}
			if ( p.Kind == BossPartKind.Skull )
				overlay.Box( BiteAttack.MouthBox, Color.Yellow.WithAlpha( 0.8f ), transform: p.WorldTransform );
			overlay.Text( p.AimPoint + Vector3.Up * 40f, $"{p.Kind} {p.Health:0}/{p.MaxHealth:0}{(p.Guarded ? $" +{p.Guard:0}" : "")}", 14f, color: c );
			if ( local.IsValid() )
			{
				var cp = p.ClosestPoint( local.FeetPosition + Vector3.Up * 40f );
				overlay.Sphere( new Sphere( cp, 6f ), Color.Yellow );
			}
		}
	}
}
