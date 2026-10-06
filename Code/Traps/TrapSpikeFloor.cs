using System;
using Sandbox;

namespace VoidMage.Traps;

/// <summary>
/// The spike floor: a cell of raised spear grates (the tell) with spears underneath. Stepped on (or blasted by a
/// fireball), the grates rattle for the wind-up, then the spears shoot up through them, hold, and sink back; a
/// while later the plate is live again. Anything standing on the cell when the spears are up takes the hit,
/// players and skeletons alike. Sounds are stand-ins from the existing set.
/// </summary>
[Title( "Trap: Spike Floor" ), Category( "Void Mage" ), Icon( "warning" )]
public sealed class TrapSpikeFloor : TrapBase
{
	/// <summary>Seconds from the step to the spears: the rattle a player who knows the tell can still react to.</summary>
	[ConVar( "trap_spike_windup" )] public static float Windup { get; set; } = 0.35f;
	/// <summary>Seconds the spears take to come up.</summary>
	[ConVar( "trap_spike_rise" )] public static float RiseSeconds { get; set; } = 0.08f;
	/// <summary>Seconds the spears stay up (still lethal).</summary>
	[ConVar( "trap_spike_hold" )] public static float Hold { get; set; } = 0.5f;
	/// <summary>Seconds the spears take to sink back.</summary>
	[ConVar( "trap_spike_retract" )] public static float Retract { get; set; } = 0.45f;
	/// <summary>Seconds after the retract before the plate is armed again.</summary>
	[ConVar( "trap_spike_reset" )] public static float ResetSeconds { get; set; } = 2f;
	[ConVar( "trap_spike_damage" )] public static float Damage { get; set; } = 25f;
	/// <summary>Knockback (units/s) away from the plate's centre, plus a small hop.</summary>
	[ConVar( "trap_spike_push" )] public static float Push { get; set; } = 100f;

	public const string SoundFire = "sounds/hitnothingheavy.sound";
	public const string SoundRetract = "sounds/debristhud.sound";
	public const string DamageKind = "trap_spike";

	private const float RattleUnits = 1.5f;

	/// <summary>The four grates (rattle during the wind-up).</summary>
	public GameObject[] Grates { get; set; } = Array.Empty<GameObject>();
	/// <summary>The four spear blocks, children of this object, hidden at rest.</summary>
	public GameObject[] Spears { get; set; } = Array.Empty<GameObject>();
	/// <summary>Local Z of a spear block at rest (tips just under the grate) and fully up.</summary>
	public float RestZ { get; set; }
	public float RaisedZ { get; set; }

	private Vector3[] _grateRest = Array.Empty<Vector3>();
	private Vector3[] _spearXY = Array.Empty<Vector3>();
	private ModelRenderer[] _spearRenderers = Array.Empty<ModelRenderer>();
	private bool _shown = true;
	private bool _rattling;
	private float _rattlePhase;

	public override float WindupSeconds => Windup;

	protected override void OnStart()
	{
		_grateRest = new Vector3[Grates.Length];
		for ( int i = 0; i < Grates.Length; i++ ) _grateRest[i] = Grates[i].IsValid() ? Grates[i].LocalPosition : Vector3.Zero;
		_spearXY = new Vector3[Spears.Length];
		_spearRenderers = new ModelRenderer[Spears.Length];
		for ( int i = 0; i < Spears.Length; i++ )
		{
			if ( !Spears[i].IsValid() ) continue;
			_spearXY[i] = Spears[i].LocalPosition;
			_spearRenderers[i] = Spears[i].Components.Get<ModelRenderer>();
		}
		SetSpearZ( RestZ );
		Show( false );
	}

	protected override void OnArmed()
	{
		if ( Audible( Centre ) ) Audio.GameAudio.Play( SoundEngage, Centre );
		_rattling = true;
		_rattlePhase = Game.Random.Float( 0f, 100f );
	}

	protected override void Tick( float now, float dt, TrapSystem.Targets targets, bool near )
	{
		switch ( State )
		{
			case TrapState.WindUp:
				Rattle( now );
				if ( StateAge >= Windup )
				{
					StopRattle();
					Enter( TrapState.Fire );
					Show( true );
					if ( near ) Audio.GameAudio.Play( SoundFire, Centre );
				}
				break;

			case TrapState.Fire:
			{
				float k = RiseSeconds > 0f ? Math.Clamp( StateAge / RiseSeconds, 0f, 1f ) : 1f;
				SetSpearZ( RestZ + (RaisedZ - RestZ) * k );
				var centre = Centre;
				HitInside( targets, Damage, DamageKind,
					feet => Away( feet, centre ) * Push + Vector3.Up * 60f,
					feet => Away( feet, centre ) );
				if ( StateAge >= RiseSeconds + Hold ) Enter( TrapState.Reset );
				break;
			}

			case TrapState.Reset:
			{
				float k = Retract > 0f ? Math.Clamp( StateAge / Retract, 0f, 1f ) : 1f;
				SetSpearZ( RaisedZ + (RestZ - RaisedZ) * k );
				if ( _shown && k >= 1f )
				{
					Show( false );
					if ( near ) Audio.GameAudio.Play( SoundRetract, Centre );
				}
				if ( StateAge >= Retract + ResetSeconds ) Enter( TrapState.Armed );
				break;
			}
		}
	}

	private static Vector3 Away( Vector3 feet, Vector3 centre )
	{
		var d = (feet - centre).WithZ( 0f );
		return d.IsNearZeroLength ? Vector3.Forward : d.Normal;
	}

	private void Rattle( float now )
	{
		for ( int i = 0; i < Grates.Length; i++ )
		{
			if ( !Grates[i].IsValid() ) continue;
			float t = now + _rattlePhase + i * 0.37f;
			Grates[i].LocalPosition = _grateRest[i] + new Vector3( MathF.Sin( t * 46f ), MathF.Sin( t * 58f ), MathF.Sin( t * 71f ) * 0.4f ) * RattleUnits;
		}
	}

	private void StopRattle()
	{
		if ( !_rattling ) return;
		_rattling = false;
		for ( int i = 0; i < Grates.Length; i++ ) if ( Grates[i].IsValid() ) Grates[i].LocalPosition = _grateRest[i];
	}

	private void SetSpearZ( float z )
	{
		for ( int i = 0; i < Spears.Length; i++ )
			if ( Spears[i].IsValid() ) Spears[i].LocalPosition = _spearXY[i].WithZ( z );
	}

	private void Show( bool on )
	{
		_shown = on;
		foreach ( var r in _spearRenderers ) if ( r.IsValid() ) r.Enabled = on;
	}
}
