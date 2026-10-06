using System;
using Sandbox;

namespace VoidMage.Combat;

/// <summary>
/// The local player's hit feel, in one place: a small camera shake, and on taking a hit a chromatic aberration
/// pulse that starts at maximum and falls off with the shake. The freeze frames are NOT here: each body freezes
/// its own animation (heden's hitstop: `FirstPersonArms.Hitstop` for the arms, `SkeletonBody.Hitstop` for the
/// skeleton, both halves on every hit), nothing touches the scene's time, so co-op peers are unaffected. The
/// shake runs on real time so a frozen animation never stalls it. Applied by
/// <see cref="Character.PlayerHealth"/>'s camera modifier; <see cref="Rendering.RetroFilter"/> reads the
/// aberration boost.
/// Also the test switch: `combat_test_damage 1` makes every hit, the player's and the skeletons', do exactly that
/// much damage; the default 0 is real damage.
/// </summary>
public static class HitFeel
{
	/// <summary>Every hit does this much damage while above 0 (testing the exchange without anyone dying); 0 = real damage.</summary>
	[ConVar( "combat_test_damage" )] public static float TestDamage { get; set; } = 0f;

	public static float Adjust( float damage ) => TestDamage > 0f ? TestDamage : damage;

	/// <summary>Enemy hits on the player do this much while above 0 (the player's own damage stays real); 0 = real damage.</summary>
	[ConVar( "combat_test_enemy_damage" )] public static float TestEnemyDamage { get; set; } = 0f;

	public static float AdjustEnemy( float damage ) => TestEnemyDamage > 0f ? TestEnemyDamage : Adjust( damage );

	/// <summary>Trap hits on anyone do this much while above 0; 0 = real damage. Traps never go through the enemy test clamp.</summary>
	[ConVar( "combat_test_trap_damage" )] public static float TestTrapDamage { get; set; } = 0f;

	public static float AdjustTrap( float damage ) => TestTrapDamage > 0f ? TestTrapDamage : damage;

	/// <summary>Damage kinds the dungeon's traps deal (trap_spike, trap_blade, trap_flame...).</summary>
	public static bool IsTrap( string kind ) => kind != null && kind.StartsWith( "trap_", StringComparison.Ordinal );

	/// <summary>Every swing lands somewhere in this band of its damage, +-15 % (user, 2026-09-18). 0 disables the roll.</summary>
	[ConVar( "combat_damage_variance" )] public static float DamageVariance { get; set; } = 0.15f;

	/// <summary>
	/// One swing's damage, rolled: the same blow never does exactly the same number twice. Rolled at the SOURCE (once
	/// per swing, shared by every body that swing catches) rather than per victim, so a wide swing reads as one good
	/// or one poor hit. Traps and damage-over-time ticks do not go through it — a burn ticking a different number
	/// twice a second is noise, not character.
	/// </summary>
	public static float Vary( float damage )
	{
		var spread = MathF.Max( 0f, DamageVariance );
		return spread <= 0f ? damage : damage * (1f + Game.Random.Float( -spread, spread ));
	}

	// dealt: ~5 frames of freeze (a heavy ~7) and a directional kick against the blade; taken: longer, the symmetric
	// shake and the aberration pulse
	public static float DealtHitstopSeconds { get; set; } = 0.09f;
	public static float DealtHeavyHitstopSeconds { get; set; } = 0.12f;
	public static float DealtKickDegrees { get; set; } = 0.7f;
	public static float DealtKickUnits { get; set; } = 1.5f;
	public static float DealtKickSeconds { get; set; } = 0.12f;
	public static float DealtHeavyKickDegrees { get; set; } = 1.2f;
	public static float DealtHeavyKickUnits { get; set; } = 2.5f;
	public static float DealtHeavyKickSeconds { get; set; } = 0.15f;
	public static float TakenHitstopSeconds { get; set; } = 0.08f;
	public static float TakenShakeDegrees { get; set; } = 1.5f;
	public static float TakenShakeSeconds { get; set; } = 0.3f;

	private static float _shakeStart = float.NegativeInfinity, _shakeSeconds = 1f, _shakeDegrees;
	private static float _pulseStart = float.NegativeInfinity, _pulseSeconds = 1f;
	private static float _kickStart = float.NegativeInfinity, _kickSeconds = 1f, _kickDegrees, _kickUnits;
	private static Vector2 _kickDir;
	private static Vector3 _kickRecoil;

	/// <summary>
	/// The player's sword landed: a kick AGAINST the blade's travel (`againstEye` in eye space: x = yaw left, y = pitch
	/// down) and a positional recoil along `recoilWorld`, both an impulse that springs back with one small overshoot.
	/// The arms' and the body's hitstops are the callers' job.
	/// </summary>
	public static void DealtHit( Vector2 againstEye, Vector3 recoilWorld, bool heavy )
	{
		_kickStart = RealTime.Now;
		_kickSeconds = heavy ? DealtHeavyKickSeconds : DealtKickSeconds;
		_kickDegrees = heavy ? DealtHeavyKickDegrees : DealtKickDegrees;
		_kickUnits = heavy ? DealtHeavyKickUnits : DealtKickUnits;
		_kickDir = againstEye.LengthSquared > 1e-4f ? againstEye.Normal : new Vector2( 0f, 1f );
		_kickRecoil = recoilWorld.IsNearZeroLength ? Vector3.Zero : recoilWorld.Normal;
	}

	/// <summary>Spells and anything without a blade direction: a straight nod.</summary>
	public static void DealtHit() => DealtHit( new Vector2( 0f, 1f ), Vector3.Zero, false );

	private static float KickEnvelope( out float t )
	{
		t = (RealTime.Now - _kickStart) / _kickSeconds;
		if ( t < 0f || t >= 1f ) return 0f;
		var d = 1f - t;
		// full on the contact frame, through zero at a third, a small overshoot, home by the end
		return d * d * MathF.Cos( t * MathF.PI * 1.5f );
	}

	/// <summary>Angle offset of the dealt-hit kick this frame (yaw, pitch), degrees.</summary>
	public static Angles KickAngles()
	{
		var env = KickEnvelope( out _ );
		if ( env == 0f ) return Angles.Zero;
		return new Angles( _kickDir.y * _kickDegrees * env, _kickDir.x * _kickDegrees * env, 0f );
	}

	/// <summary>World position offset of the dealt-hit kick this frame (the recoil back along the blade).</summary>
	public static Vector3 KickOffset()
	{
		var env = KickEnvelope( out var t );
		if ( env == 0f ) return Vector3.Zero;
		var d = 1f - t;
		return _kickRecoil * _kickUnits * d * d;
	}

	/// <summary>An enemy just landed on the player.</summary>
	public static void TookHit()
	{
		Shake( TakenShakeDegrees, TakenShakeSeconds );
		_pulseStart = RealTime.Now;
		_pulseSeconds = MathF.Max( 0.05f, TakenShakeSeconds );
	}

	public static void Shake( float degrees, float realSeconds )
	{
		if ( Dev.DevDirector.CleanFx ) return;   // a clean plate for the camera
		// a bigger shake replaces a smaller one; a smaller one arriving mid-shake is ignored
		if ( RealTime.Now < _shakeStart + _shakeSeconds && degrees < ShakeEnvelope() ) return;
		_shakeStart = RealTime.Now;
		_shakeSeconds = MathF.Max( 0.05f, realSeconds );
		_shakeDegrees = degrees;
	}

	/// <summary>1 the instant a hit is taken, falling to 0 over the shake: the aberration boost.</summary>
	public static float AberrationBoost01
	{
		get
		{
			var t = (RealTime.Now - _pulseStart) / _pulseSeconds;
			if ( t < 0f || t >= 1f ) return 0f;
			return 1f - t * t;
		}
	}

	private static float ShakeEnvelope()
	{
		var t = (RealTime.Now - _shakeStart) / _shakeSeconds;
		if ( t < 0f || t >= 1f ) return 0f;
		var d = 1f - t;
		return _shakeDegrees * d * d;
	}

	/// <summary>Camera angle offset this frame: three incommensurate sines under a decaying envelope.</summary>
	public static Angles ShakeAngles()
	{
		var env = ShakeEnvelope();
		if ( env <= 0f ) return Angles.Zero;
		var s = RealTime.Now;
		return new Angles(
			env * MathF.Sin( s * 143f ),
			env * MathF.Sin( s * 171f + 1.3f ),
			env * 0.5f * MathF.Sin( s * 97f + 2.1f ) );
	}
}
