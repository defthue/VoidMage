using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Traps;

/// <summary>
/// The swing blade: two pendulum blades side by side across the hallway (unscaled, together they span the
/// corridor so a wall is no escape), hung from the middle cell's ceiling and swinging ALONG the hallway, always
/// cycling. The motion is a real pendulum's: a lookup table integrated once per amplitude (theta'' = -sin theta),
/// so a blade hangs at the top of its arc and gathers speed on the way down, plus an optional extra dwell at the
/// top (trap_blade_hang) for timing room. The two blades run a fraction of a period apart. This component sits on
/// the yawed pivot root (+X = down the hallway); each "swing" child pitches with its phase (a child rotation composes
/// unambiguously in the parent's frame), each blade mesh hangs under its swing. The phase is time-derived (no
/// integration), so a far blade stays coherent and a freeze just stops the clock. Hits: three points up each body
/// are tested against the crescent in this frame's and the previous frame's blade frame (a cheap sweep), one hit per
/// body per blade per half period, pushed the way the tip travels.
/// </summary>
[Title( "Trap: Swing Blade" ), Category( "Void Mage" ), Icon( "warning" )]
public sealed class TrapSwingBlade : TrapBase
{
	/// <summary>Seconds of a full back-and-forth (a 4.5 m pendulum takes about 4-5 s for real).</summary>
	[ConVar( "trap_blade_period" )] public static float Period { get; set; } = 4f;
	/// <summary>Degrees each way from hanging straight down; 85 lifts the blade almost to the ceiling so you can pass under it.</summary>
	[ConVar( "trap_blade_amplitude" )] public static float Amplitude { get; set; } = 85f;
	/// <summary>Extra dwell at the top of the arc, 0 = a real pendulum, up to 0.9 = long hangs and a fast drop.</summary>
	[ConVar( "trap_blade_hang" )] public static float Hang { get; set; } = 0.3f;
	/// <summary>Fraction of a period the second blade runs behind the first (0.5 = they scissor).</summary>
	[ConVar( "trap_blade_pair_offset" )] public static float PairOffset { get; set; } = 0.5f;
	[ConVar( "trap_blade_damage" )] public static float Damage { get; set; } = 35f;
	/// <summary>Knockback (units/s) along the tip's travel.</summary>
	[ConVar( "trap_blade_push" )] public static float Push { get; set; } = 260f;

	public const string DamageKind = "trap_blade";

	/// <summary>The children that pitch (one per blade); a blade mesh hangs under each.</summary>
	public GameObject[] Swings { get; set; } = Array.Empty<GameObject>();
	/// <summary>The blade meshes, one per swing (turned 90 degrees under it so the crescent cuts edge-first); hits are judged in their frame.</summary>
	public GameObject[] Blades { get; set; } = Array.Empty<GameObject>();
	/// <summary>Seconds added to the clock so blade pairs on one floor are not in lockstep.</summary>
	public float PhaseOffset { get; set; }
	/// <summary>The blade's extents in the MESH frame, units: half its thickness (X), half its width (Y), and its length down (-Z).</summary>
	public float HalfThick { get; set; } = 6f;
	public float HalfWidth { get; set; } = 42f;
	public float Length { get; set; } = 179f;

	private const float Slack = 18f;
	private const int TableSize = 256;

	private Transform[] _prev = Array.Empty<Transform>();
	private float[] _prevAngle = Array.Empty<float>();
	private bool _hasPrev;
	private readonly Dictionary<(Guid body, int blade), int> _lastPass = new();
	private static readonly Dictionary<int, float[]> _tables = new();

	public override float WindupSeconds => 0f;

	protected override void OnStart()
	{
		Enter( TrapState.Fire );
	}

	/// <summary>
	/// One period of a real pendulum released from `amplitude` degrees, sampled evenly in time (degrees). Integrated
	/// with g/L = 1; the period only scales the clock, so the shape holds for any Period.
	/// </summary>
	private static float[] Table( int amplitude )
	{
		if ( _tables.TryGetValue( amplitude, out var cached ) ) return cached;
		float a = Math.Clamp( amplitude, 1, 179 ) * MathF.PI / 180f;
		var thetas = new List<float>( 20000 );
		float th = a, om = 0f, prevOm = 0f;
		const float dt = 0.001f;
		int crossings = 0;
		for ( int step = 0; step < 200000; step++ )
		{
			om += -MathF.Sin( th ) * dt;
			th += om * dt;
			thetas.Add( th );
			if ( (prevOm < 0f && om >= 0f) || (prevOm > 0f && om <= 0f) ) crossings++;
			prevOm = om;
			if ( crossings == 2 ) break;
		}
		var table = new float[TableSize];
		for ( int i = 0; i < TableSize; i++ )
			table[i] = thetas[Math.Min( thetas.Count - 1, (int)((long)i * thetas.Count / TableSize) )] * 180f / MathF.PI;
		_tables[amplitude] = table;
		return table;
	}

	/// <summary>The angle at phase u (fraction of a period, any value), with the hang warp: slower through the extremes, faster through the bottom.</summary>
	private static float AngleAt( float[] table, float u )
	{
		float hang = Math.Clamp( Hang, 0f, 0.9f );
		u -= MathF.Floor( u );
		u -= hang * MathF.Sin( 4f * MathF.PI * u ) / (4f * MathF.PI);
		u -= MathF.Floor( u );
		float f = u * TableSize;
		int i = (int)f;
		float t = f - i;
		float a = table[i % TableSize], b = table[(i + 1) % TableSize];
		return a + (b - a) * t;
	}

	protected override void Tick( float now, float dt, TrapSystem.Targets targets, bool near )
	{
		int n = Swings.Length;
		if ( n == 0 ) return;
		if ( !_hasPrev ) { _prev = new Transform[n]; _prevAngle = new float[n]; }
		var table = Table( (int)MathF.Round( Amplitude ) );
		float period = MathF.Max( 0.5f, Period );
		float baseU = (now - FrozenSeconds + PhaseOffset) / period;
		var tipLocal = new Vector3( 0f, 0f, -Length );

		for ( int i = 0; i < n; i++ )
		{
			var swing = Swings[i];
			if ( !swing.IsValid() ) continue;
			float u = baseU + i * PairOffset;
			float angle = AngleAt( table, u );
			swing.LocalRotation = Rotation.FromPitch( angle );
			// judged in the mesh's own frame (it hangs turned under the swing), so the model's extents apply as measured
			var cur = i < Blades.Length && Blades[i].IsValid() ? Blades[i].WorldTransform : swing.WorldTransform;
			if ( !_hasPrev ) { _prev[i] = cur; _prevAngle[i] = angle; }

			var tipNow = cur.PointToWorld( tipLocal );
			var tipPrev = _prev[i].PointToWorld( tipLocal );
			// no swing sound: the whiff take on every pass was "so annoying" (user); a proper pendulum creak can come with its own asset

			var travel = (tipNow - tipPrev).WithZ( 0f );
			var pushDir = travel.IsNearZeroLength ? Vector3.Zero : travel.Normal;
			int pass = (int)MathF.Floor( 2f * u );

			foreach ( var p in targets.Players )
			{
				if ( !p.IsValid() || p.IsProxy ) continue;
				var feet = p.FeetPosition;
				if ( !Struck( feet, p.BodyHeight, cur, _prev[i] ) || !NewPass( p.GameObject.Id, i, pass ) ) continue;
				HitPlayer( p, Damage, pushDir * Push, feet + Vector3.Up * (p.BodyHeight * 0.6f), DamageKind );
			}
			if ( Networking.IsHost )
				foreach ( var s in targets.Skeletons )
				{
					if ( !s.IsValid() ) continue;
					var feet = s.WorldPosition;
					if ( !Struck( feet, s.BodyHeight, cur, _prev[i] ) || !NewPass( s.GameObject.Id, i, pass ) ) continue;
					HitSkeleton( s, Damage, pushDir, feet + Vector3.Up * 50f, DamageKind );
				}

			_prev[i] = cur;
			_prevAngle[i] = angle;
		}
		_hasPrev = true;
	}

	private bool NewPass( Guid id, int blade, int pass )
	{
		var key = (id, blade);
		if ( _lastPass.TryGetValue( key, out var last ) && last == pass ) return false;
		_lastPass[key] = pass;
		return true;
	}

	private bool Struck( Vector3 feet, float height, in Transform now, in Transform before )
	{
		foreach ( var k in new[] { 0.3f, 0.6f, 0.9f } )
		{
			var p = feet + Vector3.Up * (height * k);
			if ( InBlade( now.PointToLocal( p ) ) || InBlade( before.PointToLocal( p ) ) ) return true;
		}
		return false;
	}

	/// <summary>Inside the crescent, not the thin shaft: the lower two thirds of the blade, with some slack for the body's width.</summary>
	private bool InBlade( Vector3 l )
		=> MathF.Abs( l.x ) <= HalfThick + Slack && MathF.Abs( l.y ) <= HalfWidth + Slack && l.z >= -Length - 6f && l.z <= -Length * 0.35f;

	public override string DebugLabel() => base.DebugLabel() + $" x{Swings.Length}";
}
