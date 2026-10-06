using System;
using Sandbox;

namespace VoidMage.Traps;

/// <summary>
/// The wall slab: a stone slab on two shafts set into one wall of a hallway cell, its shafts hidden in the rock
/// behind. The cell in front is the plate: step in and after a very short grind the slab shoots across the cell
/// and squashes whatever is there against the far wall, then creeps back. The slab is a damage volume with a hard
/// shove (no physical push): everything in the cell as it crosses takes the crush. The tell is not subtle: the
/// slab and its shafts are in plain sight; the punishment is for not looking.
/// </summary>
[Title( "Trap: Wall Slab" ), Category( "Void Mage" ), Icon( "warning" )]
public sealed class TrapWallSlab : TrapBase
{
	/// <summary>Seconds of grinding shudder before the slab moves.</summary>
	[ConVar( "trap_slab_windup" )] public static float Windup { get; set; } = 0.3f;
	/// <summary>Seconds the slab takes to cross the cell.</summary>
	[ConVar( "trap_slab_slide" )] public static float SlideSeconds { get; set; } = 0.3f;
	/// <summary>Seconds it stays pressed against the far wall.</summary>
	[ConVar( "trap_slab_hold" )] public static float Hold { get; set; } = 0.4f;
	/// <summary>Seconds it takes to creep back.</summary>
	[ConVar( "trap_slab_retract" )] public static float Retract { get; set; } = 2.5f;
	/// <summary>Seconds after the retract before the plate listens again.</summary>
	[ConVar( "trap_slab_reset" )] public static float ResetSeconds { get; set; } = 1.5f;
	[ConVar( "trap_slab_damage" )] public static float Damage { get; set; } = 40f;
	/// <summary>Knockback (units/s) along the slab's travel, into the far wall.</summary>
	[ConVar( "trap_slab_push" )] public static float Push { get; set; } = 380f;

	/// <summary>The whole crossing in one clip: 0.3 s of slide into the impact, so it starts as the slab moves. Retune trap_slab_slide and the impact drifts off the far wall.</summary>
	public const string SoundSlide = "sounds/walltrapslide.sound";
	public const string DamageKind = "trap_slab";

	/// <summary>The slab object (mesh under it); this component sits on the rest-position root.</summary>
	public GameObject Slab { get; set; }
	/// <summary>Unit direction the slab travels (the wall's normal into the hallway) and how far (units).</summary>
	public Vector3 Along { get; set; } = Vector3.Forward;
	public float Travel { get; set; } = 160f;

	private const float ShudderUnits = 1.5f;

	private Vector3 _rest;
	private bool _restSet;

	public override float WindupSeconds => Windup;

	protected override void OnStart()
	{
		if ( Slab.IsValid() ) { _rest = Slab.LocalPosition; _restSet = true; }
	}

	protected override void OnArmed()
	{
		if ( Slab.IsValid() && Audible( Slab.WorldPosition ) ) Audio.GameAudio.Play( SoundEngage, Slab.WorldPosition );
	}

	protected override void Tick( float now, float dt, TrapSystem.Targets targets, bool near )
	{
		if ( !Slab.IsValid() ) return;
		if ( !_restSet ) { _rest = Slab.LocalPosition; _restSet = true; }
		switch ( State )
		{
			case TrapState.WindUp:
				Slab.LocalPosition = _rest + new Vector3( MathF.Sin( now * 46f ), MathF.Sin( now * 58f ), MathF.Sin( now * 71f ) * 0.4f ) * ShudderUnits;
				if ( StateAge >= Windup )
				{
					Slab.LocalPosition = _rest;
					Enter( TrapState.Fire );
					if ( near ) Audio.GameAudio.Play( SoundSlide, Slab.WorldPosition + Along * (Travel * 0.5f) );
				}
				break;

			case TrapState.Fire:
			{
				// out fast (ease-in), everything in the cell crushed as it crosses, a slam at the far wall, a hold
				float k = SlideSeconds > 0f ? Math.Clamp( StateAge / SlideSeconds, 0f, 1f ) : 1f;
				float eased = k * k;
				Slab.LocalPosition = _rest + Slab.Parent.WorldTransform.NormalToLocal( Along ) * (Travel * eased);
				if ( k > 0.15f )
				{
					var push = Along.WithZ( 0f );
					var pushDir = push.IsNearZeroLength ? Vector3.Zero : push.Normal;
					HitInside( targets, Damage, DamageKind, _ => pushDir * Push + Vector3.Up * 30f, _ => pushDir );
				}
				if ( StateAge >= SlideSeconds + Hold ) Enter( TrapState.Reset );
				break;
			}

			case TrapState.Reset:
			{
				float k = Retract > 0f ? Math.Clamp( StateAge / Retract, 0f, 1f ) : 1f;
				Slab.LocalPosition = _rest + Slab.Parent.WorldTransform.NormalToLocal( Along ) * (Travel * (1f - k));
				if ( StateAge >= Retract + ResetSeconds ) Enter( TrapState.Armed );
				break;
			}
		}
	}
}
