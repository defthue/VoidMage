using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Spells;

public enum HazardKind
{
	/// <summary>A ball of the element at A.</summary>
	Sphere,
	/// <summary>The element along the segment A-B.</summary>
	Capsule,
	/// <summary>A line growing from A along B-A at Speed from StartTime (The Wake behind a bolt), capped by EndHazard.</summary>
	Ray,
	/// <summary>A ball riding a networked object (the void orb's zone).</summary>
	FollowSphere,
	/// <summary>A ring of Radius around a followed object, Width wide (The Wake of an orbiting bolt).</summary>
	Ring,
	/// <summary>Amber: a parked bolt that goes off when a hostile comes within Radius.</summary>
	Mine,
}

/// <summary>One registered area of a spell's element. Every peer holds the same list (for the visuals); only the host ticks damage.</summary>
public sealed class Hazard
{
	public Guid Key;
	public HazardKind Kind;
	public Vector3 A, B;
	public float Radius, Width, Speed, StartTime, Until;
	/// <summary>Ray: the length the line stops at (set by EndHazard; until then the full A-B).</summary>
	public float MaxLength;
	public GameObject Follow;
	public float Dps, Slow;
	public GameObject Attacker;
	public string DamageKind;
	public SpellHitInfo Info;
	/// <summary>Mine: the impact it delivers when it goes off.</summary>
	public float MineDamage, MineSplashRadius, MineSplashFraction;
	public bool Triggered;
	public readonly Dictionary<Guid, float> NextTick = new();

	public SpellDefinition Spell => SpellDatabase.Get( Info.SpellId );
	public Vector3 Centre => Kind is HazardKind.FollowSphere or HazardKind.Ring && Follow.IsValid() ? Follow.WorldPosition : A;

	/// <summary>The Ray's current length: grown at Speed since StartTime, never past MaxLength.</summary>
	public float RayLength( float now ) => MathF.Min( MaxLength, MathF.Max( 0f, (now - StartTime) * Speed ) );

	public Vector3 RayEnd( float now )
	{
		var dir = (B - A);
		var len = dir.Length;
		if ( len < 0.001f ) return A;
		return A + dir / len * RayLength( now );
	}

	/// <summary>Is a body (its feet at `feet`, `height` tall) inside the element?</summary>
	public bool Contains( Vector3 feet, float height, float now )
	{
		var chest = feet + Vector3.Up * height * 0.5f;
		switch ( Kind )
		{
			case HazardKind.Sphere:
			case HazardKind.Mine:
				return chest.DistanceSquared( A ) <= (Radius + 14f) * (Radius + 14f);
			case HazardKind.FollowSphere:
				return Follow.IsValid() && chest.DistanceSquared( Follow.WorldPosition ) <= (Radius + 14f) * (Radius + 14f);
			case HazardKind.Capsule:
				return SegmentDistance( chest, A, B ) <= Radius + 14f;
			case HazardKind.Ray:
				return SegmentDistance( chest, A, RayEnd( now ) ) <= Radius + 14f;
			case HazardKind.Ring:
			{
				if ( !Follow.IsValid() ) return false;
				var c = Follow.WorldPosition;
				if ( MathF.Abs( feet.z - c.z ) > 90f ) return false;
				var d = (feet - c).WithZ( 0f ).Length;
				return MathF.Abs( d - Radius ) <= Width * 0.5f + 14f;
			}
		}
		return false;
	}

	private static float SegmentDistance( Vector3 p, Vector3 a, Vector3 b )
	{
		var ab = b - a;
		var len2 = ab.LengthSquared;
		if ( len2 < 1e-4f ) return p.Distance( a );
		var t = Math.Clamp( Vector3.Dot( p - a, ab ) / len2, 0f, 1f );
		return p.Distance( a + ab * t );
	}
}

/// <summary>
/// The live hazards, on every peer (SpellNet's broadcasts keep the lists identical), and the host's tick over them:
/// every awake hostile skeleton standing in one takes a Tick hit at DotHz and keeps the element's slow; a mine goes
/// off once and is removed for everyone. The visuals read <see cref="All"/> and the Added/Removed events.
/// </summary>
public static class HazardField
{
	private static readonly List<Hazard> _all = new();
	private static readonly Dictionary<Guid, Hazard> _byKey = new();

	public static IReadOnlyList<Hazard> All => _all;
	public static event Action<Hazard> Added;
	public static event Action<Hazard> Removed;

	/// <summary>Draw every hazard's shape and the bodies it is ticking.</summary>
	[ConVar( "spell_hazard_debug" )] public static bool Debug { get; set; }

	public static Hazard Get( Guid key ) => _byKey.GetValueOrDefault( key );

	internal static void Add( Hazard h )
	{
		if ( _byKey.ContainsKey( h.Key ) ) return;
		_all.Add( h );
		_byKey[h.Key] = h;
		Added?.Invoke( h );
	}

	internal static void End( Guid key, float finalLength, float now )
	{
		var h = Get( key );
		if ( h is null ) return;
		h.MaxLength = MathF.Max( 0f, finalLength );
		// the trail lives its life from the moment the bolt stopped drawing it
		h.Until = now + (h.Until - h.StartTime - (h.Kind == HazardKind.Ray ? MathF.Max( 0f, h.MaxLength / MathF.Max( 1f, h.Speed ) ) : 0f)).Clamp( 0.1f, 30f );
	}

	internal static void Remove( Guid key )
	{
		var h = Get( key );
		if ( h is null ) return;
		_all.Remove( h );
		_byKey.Remove( key );
		Removed?.Invoke( h );
	}

	public static void Clear()
	{
		foreach ( var h in _all.ToArray() ) Remove( h.Key );
	}

	private static readonly List<Hazard> _expired = new();
	private static readonly List<Hazard> _ticking = new();

	/// <summary>Every peer: expire what is over. Host: tick damage and fire mines.</summary>
	public static void Tick( Scene scene, float now )
	{
		if ( _all.Count == 0 ) return;
		_expired.Clear();
		foreach ( var h in _all )
		{
			if ( now > h.Until || (h.Kind is HazardKind.FollowSphere or HazardKind.Ring && !h.Follow.IsValid()) ) _expired.Add( h );
		}
		foreach ( var h in _expired ) Remove( h.Key );

		if ( Debug ) Draw( scene, now );
		if ( !Networking.IsHost ) return;

		var hz = MathF.Max( 1f, SpellHostTuning.DotHz );
		// a snapshot: a mine firing removes itself from `_all` on the spot (the host runs its own broadcast at once),
		// and a tick that kills can spawn or end other hazards ("Collection was modified" in the boss fight, 2026-09-23)
		_ticking.Clear();
		_ticking.AddRange( _all );
		foreach ( var h in _ticking )
		{
			if ( h.Triggered || !_byKey.ContainsKey( h.Key ) ) continue;
			var team = CombatTargets.TeamOf( h.Attacker );
			var hitTeam = team == Team.Ally ? Team.Hostile : Team.Ally;
			foreach ( var body in CombatTargets.Bodies( hitTeam ) )
			{
				// a boss part is judged by the point of its box nearest the hazard, a walker by its feet
				var feet = body is Boss.BossPart part ? part.ClosestPoint( h.Centre ).WithZ( part.WorldBounds.Mins.z ) : body.WorldPosition;
				if ( !h.Contains( feet, body.BodyHeight, now ) ) continue;

				if ( h.Kind == HazardKind.Mine )
				{
					h.Triggered = true;
					SpellNet.FireMine( h );
					break;
				}

				if ( h.Slow > 0f && body is SkeletonNpc npc ) npc.Status.Slow( h.Slow, 0.4f );
				if ( h.Dps <= 0f ) continue;
				var id = body.GameObject.Id;
				if ( h.NextTick.TryGetValue( id, out var next ) && now < next ) continue;
				h.NextTick[id] = now + 1f / hz;
				body.ApplyHitHost( new DamageRouter.Hit
				{
					Damage = h.Dps / hz,
					Position = body.AimPoint,
					Push = Vector3.Zero,
					Attacker = h.Attacker,
					Kind = h.DamageKind,
					Spell = h.Info.With( SpellHitFlags.Tick | SpellHitFlags.Area ),
				} );
			}
		}
	}

	private static void Draw( Scene scene, float now )
	{
		var overlay = scene?.DebugOverlay;
		if ( overlay is null ) return;
		foreach ( var h in _all )
		{
			var c = h.Kind == HazardKind.Mine ? Color.Orange : Color.Cyan;
			switch ( h.Kind )
			{
				case HazardKind.Sphere:
				case HazardKind.Mine:
				case HazardKind.FollowSphere:
					overlay.Sphere( new Sphere( h.Centre, h.Radius ), c, duration: 0f );
					break;
				case HazardKind.Capsule:
					overlay.Line( h.A, h.B, c, duration: 0f );
					break;
				case HazardKind.Ray:
					overlay.Line( h.A, h.RayEnd( now ), c, duration: 0f );
					break;
				case HazardKind.Ring:
					if ( h.Follow.IsValid() ) overlay.Sphere( new Sphere( h.Follow.WorldPosition, h.Radius ), c, duration: 0f );
					break;
			}
		}
	}
}

/// <summary>
/// The host's per-tick spell bookkeeping in one place (the SkeletonSystem model): refresh the target lists, tick
/// every skeleton's statuses, tick the hazards, drain the death stage's queue. Every peer expires its hazards here.
/// </summary>
public sealed class SpellHostSystem : GameObjectSystem
{
	public SpellHostSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 3, Tick, "SpellHost" );
	}

	private void Tick()
	{
		if ( Scene.IsEditor ) return;
		var now = Time.Now;
		HazardVisuals.Hook();
		if ( Networking.IsHost )
		{
			CombatTargets.Tick( Scene );
			foreach ( var npc in CombatTargets.Skeletons )
				if ( npc.IsValid() && !npc.IsDead && !npc.Dormant ) npc.TickStatus( now );
			SpellDeathStage.Tick( Scene, now );
			SpellHostEffects.Tick( Scene, now );
		}
		HazardField.Tick( Scene, now );
		HazardVisuals.Tick( now );
	}
}
