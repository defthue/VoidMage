using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Traps;

/// <summary>
/// The saw rail: a spinning disc patrolling a rail laid down the middle of a hallway, always cycling; the edges of
/// the corridor stay walkable, so the way past is along a wall while the saw is elsewhere on its run. The disc
/// rides a cosine between the rail's ends (slow at the ends, fast through the middle) and spins with its travel.
/// Position is time-derived (a freeze stops the clock). Hits: a body inside the disc's box, once per pass.
/// </summary>
[Title( "Trap: Saw Rail" ), Category( "Void Mage" ), Icon( "warning" )]
public sealed class TrapSawRail : TrapBase
{
	/// <summary>Seconds for a full trip there and back.</summary>
	[ConVar( "trap_saw_period" )] public static float Period { get; set; } = 3.2f;
	/// <summary>Disc spin, degrees per second.</summary>
	[ConVar( "trap_saw_spin" )] public static float Spin { get; set; } = 900f;
	[ConVar( "trap_saw_damage" )] public static float Damage { get; set; } = 30f;
	/// <summary>Knockback (units/s) along the saw's travel.</summary>
	[ConVar( "trap_saw_push" )] public static float Push { get; set; } = 240f;

	public const string DamageKind = "trap_saw";

	/// <summary>The disc object (its pivot at the disc's centre); this component sits on the rail root at the run's start.</summary>
	public GameObject Disc { get; set; }
	/// <summary>Unit direction of the rail in world space and the disc's travel along it (units).</summary>
	public Vector3 Along { get; set; } = Vector3.Forward;
	public float Travel { get; set; } = 300f;
	/// <summary>World position of the disc's centre at the rail's start.</summary>
	public Vector3 Start { get; set; }
	public float Radius { get; set; } = 34f;
	public float HalfThick { get; set; } = 3f;
	public float PhaseOffset { get; set; }

	private const float Slack = 14f;

	private float _spin;
	private float _prevS;
	private readonly Dictionary<Guid, int> _lastPass = new();

	public override float WindupSeconds => 0f;

	protected override void OnStart()
	{
		Enter( TrapState.Fire );
	}

	protected override void Tick( float now, float dt, TrapSystem.Targets targets, bool near )
	{
		if ( !Disc.IsValid() ) return;
		float period = MathF.Max( 0.5f, Period );
		float u = (now - FrozenSeconds + PhaseOffset) / period;
		float s = Travel * (1f - MathF.Cos( u * MathF.Tau )) * 0.5f;
		float ds = s - _prevS;
		_prevS = s;
		var centre = Start + Along * s;
		Disc.WorldPosition = centre;
		// the disc lies in the rail's vertical plane and spins about its own axis, with its travel
		_spin += Spin * dt * (ds >= 0f ? 1f : -1f);
		Disc.LocalRotation = Rotation.FromPitch( _spin % 360f );

		int pass = (int)MathF.Floor( 2f * u );
		var pushDir = ds >= 0f ? Along : -Along;
		var rot = Disc.WorldRotation;
		foreach ( var p in targets.Players )
		{
			if ( !p.IsValid() || p.IsProxy ) continue;
			var feet = p.FeetPosition;
			if ( !Struck( feet, p.BodyHeight, centre ) || !NewPass( p.GameObject.Id, pass ) ) continue;
			HitPlayer( p, Damage, pushDir * Push, feet + Vector3.Up * (p.BodyHeight * 0.5f), DamageKind );
		}
		if ( Networking.IsHost )
			foreach ( var n in targets.Skeletons )
			{
				if ( !n.IsValid() ) continue;
				var feet = n.WorldPosition;
				if ( !Struck( feet, n.BodyHeight, centre ) || !NewPass( n.GameObject.Id, pass ) ) continue;
				HitSkeleton( n, Damage, pushDir, feet + Vector3.Up * 40f, DamageKind );
			}
		_ = rot;
	}

	private bool NewPass( Guid id, int pass )
	{
		if ( _lastPass.TryGetValue( id, out var last ) && last == pass ) return false;
		_lastPass[id] = pass;
		return true;
	}

	/// <summary>A body whose footprint sits within the disc's box: along the rail inside the disc's front/back, across within its thickness plus the body's width.</summary>
	private bool Struck( Vector3 feet, float height, Vector3 centre )
	{
		var d = feet - centre;
		float along = Vector3.Dot( d, Along );
		var across = d - Along * along;
		across.z = 0f;
		if ( MathF.Abs( along ) > Radius * 0.8f ) return false;
		if ( across.Length > HalfThick + Slack ) return false;
		// the disc reaches down to the floor and up to its diameter: any standing body overlaps it
		return feet.z + height >= centre.z - Radius && feet.z <= centre.z + Radius;
	}

	public override string DebugLabel() => base.DebugLabel() + $" s={_prevS:0}";
}
