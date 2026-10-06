using System;
using Sandbox;
using VoidMage.Dungeon;

namespace VoidMage.Traps;

/// <summary>
/// The flame head: a nozzle on the wall beside a room's door at head height, with a pilot flame and soot on the
/// tile under it (the tell). Its jet crosses the doorway cell at head height: walk through and the head fires the
/// moment you are in the jet, and the way past is to crouch under it. The jet box is both the trigger and the
/// damage volume, and a body counts only if it stands tall enough to reach it. A burst of fire breath sweeps
/// the jet for a second, ticking damage; then the head cools for a while before it listens again. Purple
/// (magic) numbers on skeletons.
/// </summary>
[Title( "Trap: Flame Head" ), Category( "Void Mage" ), Icon( "warning" )]
public sealed class TrapFlameHead : TrapBase
{
	/// <summary>Seconds of hiss between stepping into the jet and the fire (near instant: the counter is crouching, not reacting).</summary>
	[ConVar( "trap_flame_windup" )] public static float Windup { get; set; } = 0.12f;
	/// <summary>Seconds the burst lasts.</summary>
	[ConVar( "trap_flame_burst" )] public static float BurstSeconds { get; set; } = 1.2f;
	/// <summary>Seconds between damage ticks during the burst (kept above the player's hit grace).</summary>
	[ConVar( "trap_flame_tick" )] public static float TickSeconds { get; set; } = 0.35f;
	/// <summary>Seconds after the burst before the head listens again.</summary>
	[ConVar( "trap_flame_reset" )] public static float ResetSeconds { get; set; } = 2.5f;
	/// <summary>Damage per tick.</summary>
	[ConVar( "trap_flame_damage" )] public static float Damage { get; set; } = 8f;
	/// <summary>Knockback (units/s) along the jet.</summary>
	[ConVar( "trap_flame_push" )] public static float Push { get; set; } = 150f;

	public const string SoundHiss = "sounds/cracklingfire.sound";
	/// <summary>The burst itself: the fireball's charge roar (user), held for the burst and faded with it.</summary>
	public const string SoundFire = "sounds/fireballcharge.sound";
	public const string DamageKind = "trap_flame";

	private const float PilotIdle = 0.25f, PilotHiss = 0.9f;

	/// <summary>World point the fire leaves from and the unit direction it goes (the wall's normal, into the room).</summary>
	public Vector3 Nozzle { get; set; }
	public Vector3 Aim { get; set; } = Vector3.Forward;
	/// <summary>The pilot flame's light: flares during the hiss and the burst.</summary>
	public FireLight Pilot { get; set; }
	/// <summary>The fire breath, built by the dungeon builder.</summary>
	public TrapFlameVfx.Jet Jet { get; set; }

	private SoundHandle _hiss;
	private SoundHandle _fire;
	private float _nextTick;

	public override float WindupSeconds => Windup;

	protected override void OnArmed()
	{
		if ( Pilot.IsValid() ) Pilot.Amount = PilotHiss;
		_hiss?.Stop();
		_hiss = Audio.GameAudio.PlayLoop( SoundHiss, Nozzle );
	}

	protected override void Tick( float now, float dt, TrapSystem.Targets targets, bool near )
	{
		switch ( State )
		{
			case TrapState.WindUp:
				if ( StateAge >= Windup )
				{
					Enter( TrapState.Fire );
					TrapFlameVfx.Set( Jet, true );
					_hiss?.Stop( 0.1f );
					_hiss = null;
					_fire?.Stop();
					_fire = Audio.GameAudio.PlayLoop( SoundFire, Nozzle );
					_nextTick = now;
				}
				break;

			case TrapState.Fire:
				TrapFlameVfx.Drive( Jet, now );
				if ( now >= _nextTick )
				{
					_nextTick = now + MathF.Max( 0.3f, TickSeconds );
					HitThisFire.Clear();
					var along = Aim.WithZ( 0f );
					var push = along.IsNearZeroLength ? Vector3.Zero : along.Normal;
					HitInside( targets, Damage, DamageKind, _ => push * Push, _ => push );
				}
				if ( StateAge >= BurstSeconds )
				{
					Enter( TrapState.Reset );
					Cool();
				}
				break;

			case TrapState.Reset:
				if ( StateAge >= ResetSeconds ) Enter( TrapState.Armed );
				break;
		}
	}

	private void Cool()
	{
		TrapFlameVfx.Set( Jet, false );
		if ( Pilot.IsValid() ) Pilot.Amount = PilotIdle;
		_hiss?.Stop( 0.2f );
		_hiss = null;
		_fire?.Stop( 0.3f );
		_fire = null;
	}

	protected override void OnDestroy()
	{
		_hiss?.Stop();
		_hiss = null;
		_fire?.Stop();
		_fire = null;
	}
}
