using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Boss;

/// <summary>
/// The summon: the skull rises and draws back, convulses with its jaw clattering (the tell), screams, and green
/// fire leaves its eyes for a ring of spots around the player; where each bolt lands a minion stands up: a pack
/// of light skeletons, a swarm of bats, a pair of dragons, or a mix (<see cref="BossSummons"/>). Then it sags,
/// spent and exposed. Never while the arena is already crowded, and with a cooldown. The minions are ordinary
/// hostiles: they need the arena's navmesh (HellArena bakes one) and die with the skull.
/// </summary>
public sealed class SummonAttack : BossAttack
{
	public override string Name => "summon";
	public override bool UsesHand => false;

	[ConVar( "boss_summon_approach", ConVarFlags.Saved )] public static float ApproachSeconds { get; set; } = 0.5f;
	[ConVar( "boss_summon_tell", ConVarFlags.Saved )] public static float TellSeconds { get; set; } = 1f;
	[ConVar( "boss_summon_lock", ConVarFlags.Saved )] public static float LockFraction { get; set; } = 0.3f;
	[ConVar( "boss_summon_strike", ConVarFlags.Saved )] public static float StrikeSeconds { get; set; } = 1.4f;
	[ConVar( "boss_summon_rest", ConVarFlags.Saved )] public static float RestSeconds { get; set; } = 1.5f;
	[ConVar( "boss_summon_recover", ConVarFlags.Saved )] public static float RecoverSeconds { get; set; } = 1f;

	/// <summary>The most minions alive at once (a new wave fills up to it), the ring's radius around the player, and the seconds between summons.</summary>
	[ConVar( "boss_summon_cap", ConVarFlags.Saved )] public static int Cap { get; set; } = 10;
	[ConVar( "boss_summon_radius", ConVarFlags.Saved )] public static float Radius { get; set; } = 700f;
	[ConVar( "boss_summon_cooldown", ConVarFlags.Saved )] public static float Cooldown { get; set; } = 18f;
	[ConVar( "boss_summon_exposed", ConVarFlags.Saved )] public static float Exposed { get; set; } = 1.5f;

	public override float Approach => ApproachSeconds;
	public override float Tell => TellSeconds;
	public override float LockAt => LockFraction;
	public override float Strike => StrikeSeconds;
	public override float Rest => RestSeconds;
	public override float Recover => RecoverSeconds;
	public override float RestMultiplier => Exposed;

	public override float Weight( BossBrain brain, float dist ) => 0f;

	public override Vector3 LockTarget( IBossFrame f, BossPartKind skull, Vector3 playerFeet ) => playerFeet;

	// host: the running wave (per attack sequence): what lands where and when
	private int _waveSeq = -1;
	private readonly List<(string kind, Vector3 at, float when, bool done)> _wave = new();

	public override MoveIntent Move( IBossFrame f, BossPartKind skull, ref BossAttackCtx ctx, float t, Vector3 tracked )
	{
		var hover = f.SkullHover();
		var face = hover.Rotation;
		var high = hover.Position + Vector3.Up * 260f - f.Facing.Forward * 200f;
		var stage = StageAt( ctx, t, out var p );
		switch ( stage )
		{
			case BossStage.Approach:
				f.JawOpen01 = BossGeometry.Laugh( t ) * Ease( p * 3f );
				f.EyeFire01 = 1f;
				return MoveIntent.To( high, BossDynamics.Approach, face, BossDynamics.TurnIdle ).NoGround().InPhase( 0 );
			case BossStage.Tell:
			{
				var amp = Ease( p );
				f.JawOpen01 = 0.35f + 0.35f * MathF.Sin( t * 40f );
				f.EyeFire01 = 1f + amp;
				return SkullTremble( f, skull, t, amp, face, 1 );
			}
			case BossStage.Strike:
			{
				var amp = 1f - Ease( p );
				f.JawOpen01 = 1f;
				f.EyeFire01 = 2f;
				var (offset, jitter) = BossGeometry.Tremble( t, amp, f.S * 0.5f );
				return MoveIntent.To( high + Vector3.Up * 100f, BossDynamics.Approach, face * Rotation.FromPitch( -16f ) * jitter, BossDynamics.TurnIdle )
					.NoGround().Shifted( offset ).WithLean( 0f ).InPhase( 2 );
			}
			case BossStage.Rest:
				f.JawOpen01 = 0.8f;
				f.EyeFire01 = 0.6f;
				return MoveIntent.To( hover.Position + Vector3.Down * 60f, BossDynamics.Tell, face * Rotation.FromPitch( 12f ), BossDynamics.TurnIdle ).WithClearance( BiteAttack.LowClearance ).InPhase( 3 );
			default:
				f.JawOpen01 = 0.8f * (1f - Ease( p ));
				f.EyeFire01 = 1f;
				return MoveIntent.To( hover.Position, BossDynamics.Recover, hover.Rotation, BossDynamics.TurnIdle ).WithClearance( BiteAttack.LowClearance ).InPhase( 4 );
		}
	}

	/// <summary>Host: at the scream the wave is rolled and a harmless bolt leaves an eye for every spot; each minion stands up when its bolt lands.</summary>
	public override void HostTick( BossBrain brain, in BossAttackCtx ctx, float t )
	{
		if ( _waveSeq != ctx.Seq )
		{
			_waveSeq = ctx.Seq;
			_wave.Clear();
		}
		var screamAt = TellEnd( ctx );
		if ( t < screamAt ) return;
		if ( _wave.Count == 0 )
		{
			var kinds = BossSummons.RollWave( brain.Fight, Cap - BossSummons.Live );
			if ( kinds.Count == 0 ) { _wave.Add( ("", Vector3.Zero, float.MaxValue, true) ); return; }
			BossSummons.LastAt = Time.Now;
			var player = brain.Nearest( out _ );
			var centre = player is not null ? player.FeetPosition : ctx.Target;
			var start = Game.Random.Float( 0f, 360f );
			for ( var i = 0; i < kinds.Count; i++ )
			{
				var ang = start + 360f * i / kinds.Count + Game.Random.Float( -18f, 18f );
				var r = Radius * Game.Random.Float( 0.8f, 1.2f );
				var flat = brain.ClampToArena( centre + Rotation.FromYaw( ang ).Forward * r );
				var at = BossSummons.Ground( brain, flat );
				brain.LaunchBoltTo( i, at + Vector3.Up * 20f, mark: false, harmless: true );
				_wave.Add( (kinds[i], at, Time.Now + BossBolt.Flight, false) );
			}
			return;
		}
		for ( var i = 0; i < _wave.Count; i++ )
		{
			var w = _wave[i];
			if ( w.done || Time.Now < w.when ) continue;
			_wave[i] = (w.kind, w.at, w.when, true);
			BossSummons.Spawn( brain.Scene, w.kind, w.at );
		}
	}

	/// <summary>Nothing to judge: the minions are the hazard.</summary>
	public override void Judge( BossBrain brain, BossPart skull, in BossAttackCtx ctx, float t, BossHazardState hz ) { }

	public override void Present( BossBrain brain, BossPart skull, in BossAttackCtx ctx, BossStage entered, float t )
	{
		switch ( entered )
		{
			case BossStage.Tell:
				Sound( "sounds/ambienceknock.sound", skull.AimPoint );
				HitFeel.Shake( 1.2f, 1.2f );
				break;
			case BossStage.Strike:
				Sound( "sounds/ambiencehorror.sound", skull.AimPoint );
				HitFeel.Shake( 4f, 0.7f );
				break;
		}
	}
}

/// <summary>Host: the Colossus's minions, spawned through the skeleton spawner, capped, killed with the skull and swept with the arena.</summary>
public static class BossSummons
{
	private static readonly List<GameObject> _spawned = new();
	public static float LastAt { get; set; } = float.NegativeInfinity;

	/// <summary>Minions alive right now.</summary>
	public static int Live
	{
		get
		{
			var n = 0;
			for ( var i = _spawned.Count - 1; i >= 0; i-- )
			{
				var go = _spawned[i];
				if ( !go.IsValid() ) { _spawned.RemoveAt( i ); continue; }
				var npc = go.Components.Get<SkeletonNpc>();
				if ( npc.IsValid() && !npc.IsDead ) n++;
			}
			return n;
		}
	}

	/// <summary>A wave's kinds: a pack of light skeletons, a swarm of bats, a pair of dragons, or a mix; bigger in phase 2; cut to the room left under the cap.</summary>
	public static List<string> RollWave( int fight, int room )
	{
		var list = new List<string>();
		var extra = fight >= 2 ? 2 : 0;
		switch ( Game.Random.Next( 4 ) )
		{
			case 0:
				for ( var i = 0; i < Game.Random.Next( 5, 9 ) + extra; i++ ) list.Add( "soldier_01" );
				break;
			case 1:
				for ( var i = 0; i < Game.Random.Next( 6, 11 ) + extra; i++ ) list.Add( "monster_bat" );
				break;
			case 2:
				for ( var i = 0; i < Game.Random.Next( 2, 4 ) + (fight >= 2 ? 1 : 0); i++ ) list.Add( "monster_dragon" );
				break;
			default:
				for ( var i = 0; i < 3 + extra; i++ ) list.Add( "soldier_01" );
				for ( var i = 0; i < 4; i++ ) list.Add( "monster_bat" );
				break;
		}
		if ( list.Count > room ) list.RemoveRange( Math.Max( 0, room ), list.Count - Math.Max( 0, room ) );
		return list;
	}

	/// <summary>A spot on the ground (snapped onto the arena's navmesh when there is one, so the minion can walk from it).</summary>
	public static Vector3 Ground( BossBrain brain, Vector3 flat )
	{
		var at = flat.WithZ( brain.GroundZ( flat ) );
		if ( HellArena.NavReady )
		{
			var nav = brain.Scene.NavMesh;
			var on = nav?.GetClosestPoint( at, 240f );
			if ( on.HasValue ) at = on.Value;
		}
		return at;
	}

	public static void Spawn( Scene scene, string kind, Vector3 at )
	{
		if ( !Networking.IsHost || string.IsNullOrEmpty( kind ) ) return;
		var go = SkeletonSpawner.Spawn( scene, at, Rotation.FromYaw( Game.Random.Float( 0f, 360f ) ), null, kind, "", -1, -1, false );
		if ( go.IsValid() ) _spawned.Add( go );
	}

	/// <summary>The skull died: every minion dies with it.</summary>
	public static void KillAll()
	{
		if ( !Networking.IsHost ) return;
		foreach ( var go in _spawned.ToArray() )
		{
			if ( !go.IsValid() ) continue;
			var npc = go.Components.Get<SkeletonNpc>();
			if ( npc.IsValid() && !npc.IsDead ) npc.ApplyHitHost( new DamageRouter.Hit { Damage = 100000f, Position = go.WorldPosition + Vector3.Up * 40f, Push = Vector3.Up * 200f, Kind = "boss_death", Attacker = null } );
		}
	}

	/// <summary>The arena is torn down: whatever is left goes with it.</summary>
	public static void Clear()
	{
		foreach ( var go in _spawned ) if ( go.IsValid() ) go.Destroy();
		_spawned.Clear();
		LastAt = float.NegativeInfinity;
	}
}
