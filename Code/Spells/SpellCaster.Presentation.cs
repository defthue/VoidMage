using System;
using Sandbox;

namespace VoidMage.Spells;

/// <summary>Every peer's view of a caster: the charge orb at the gem, the launch flash, the cancel puff, the refusal click, the echoes.</summary>
public sealed partial class SpellCaster
{
	/// <summary>
	/// The refusal has no sound of its own: the user cut the borrowed whiff on 2026-09-17 ("i will add a sound later")
	/// and the staff sparks instead. Set this convar once there is an asset for it.
	/// </summary>
	[ConVar( "spell_deny_sound" )] public static string SoundDeny { get; set; } = "";

	private FireballVfx.Rig _orb;
	private FireballVfx.Rig _dyingOrb;
	private float _orbDeathAt;
	private bool _wasCharging;
	private int _seenCastCount;
	private int _seenDenyCount;
	private bool _castSeeded;
	private SoundHandle _chargeSound;
	private float _lastOrbScale;
	private SpellDefinition _shownSpell;
	private ChargeVfx.AimLine _aimLine;

	private void DisablePresentation()
	{
		FireballVfx.Destroy( _orb ); _orb = null;
		FireballVfx.Destroy( _dyingOrb ); _dyingOrb = null;
		_aimLine?.Destroy(); _aimLine = null;
		_chargeSound?.Stop( 0f ); _chargeSound = null;
	}

	/// <summary>
	/// Where the caster is pointing, for every peer: the local camera for the owner, the body's eyes for a proxy.
	/// The charge rig is turned by this, which is what lets a shard lie down the shot and a ring stand across it.
	/// </summary>
	private Vector3 AimForward()
	{
		if ( !IsProxy && Scene.Camera.IsValid() ) return Scene.Camera.WorldRotation.Forward;
		if ( _controller.IsValid() ) return _controller.EyeAngles.Forward;
		return WorldRotation.Forward;
	}

	private void UpdatePresentation()
	{
		var tip = StaffTip();
		var spell = CastingSpell;
		var scale = spell?.VfxScale ?? 0.65f;

		// A release that fired: the pop and the cast sound at the gem. Seen through the synced count
		// so proxies get it too; a late joiner's first value is history, not a cast.
		if ( !_castSeeded ) { _castSeeded = true; _seenCastCount = CastCount; _seenDenyCount = DenyCount; }
		if ( CastCount != _seenCastCount )
		{
			_seenCastCount = CastCount;
			var at = tip ?? WorldPosition + Vector3.Up * 60f;
			SpellVfx.LaunchFlash( Scene, at, spell, scale );
			if ( spell?.SoundCast != "" ) VoidMage.Audio.GameAudio.Play( spell?.SoundCast ?? FireballVfx.SoundCast, at );   // "" = a spell cast in silence (the laser)
			_chargeSound?.Stop( 0.3f );
			_chargeSound = null;
		}
		if ( DenyCount != _seenDenyCount )
		{
			_seenDenyCount = DenyCount;
			var at = tip ?? WorldPosition + Vector3.Up * 60f;
			// not enough mana: the staff spits mana-coloured sparks where the bolt would have been born
			SpellVfx.DenySparks( Scene, at );
			if ( !string.IsNullOrEmpty( SoundDeny ) ) VoidMage.Audio.GameAudio.Play( SoundDeny, at );
		}

		// an instant cast's raise is too short to be a charge: only a real hold (Gluttony, the old rhythm) shows one
		if ( ShowsCharge )
		{
			if ( !_wasCharging )
			{
				_wasCharging = true;
				_lastOrbScale = -1f;
				_shownSpell = spell;
				if ( tip is { } start )
					_chargeSound = VoidMage.Audio.GameAudio.PlayLoop( spell?.SoundCharge ?? FireballVfx.SoundCharge, start );
			}

			if ( tip is null )
			{
				if ( _orb is not null && _orb.Root.IsValid() ) _orb.Root.Enabled = false;
			}
			else
			{
				if ( _orb is null || !_orb.Root.IsValid() )
					_orb = SpellVfx.BuildCharge( null, spell );

				if ( !_orb.Root.Enabled ) _orb.Root.Enabled = true;
				_orb.Root.WorldPosition = tip.Value;
				// the charge faces the shot: everything with a direction in it (the shard, the focus ring) follows the aim
				var forward = AimForward();
				if ( !forward.IsNearZeroLength ) _orb.Root.WorldRotation = Rotation.LookAt( forward );

				// Born from nearly nothing and grown by the charge: size and spread together, population on its own
				// squared curve so the early hold is a few motes gathering and the swarm only assembles at full.
				// The envelope is the charge's own, NOT the bolt's VfxScale: each recipe is authored to read at full
				// hold, so a small bolt (the swarm's 0.3) does not get a charge too small to see.
				var charge = Charge01;
				var k = MathX.Lerp( 0.12f, 0.8f, charge );
				if ( MathF.Abs( k - _lastOrbScale ) > 0.003f )
				{
					_lastOrbScale = k;
					FireballVfx.SetScale( _orb, k );
					FireballVfx.SetSpread( _orb, k );
					FireballVfx.SetEmission( _orb, MathX.Lerp( 0.08f, 1f, charge * charge ) );
				}
				_orb.LightBrightness = 0.3f + 0.7f * charge;
				SpellVfx.Drive( _orb );

				DriveAimLine( spell, tip.Value, charge );

				if ( _chargeSound is not null ) _chargeSound.Position = tip.Value;
			}
		}
		else if ( _wasCharging )
		{
			_wasCharging = false;
			_aimLine?.Destroy(); _aimLine = null;

			// The hum is already fading when a cast was seen; otherwise this was a cancel.
			if ( _chargeSound is not null )
			{
				_chargeSound.Stop( 0.05f );
				_chargeSound = null;
				if ( _orb is not null && _orb.Root.IsValid() )
					SpellVfx.CancelPoof( Scene, _orb.Root.WorldPosition, _shownSpell, MathF.Max( _lastOrbScale, 0.2f ) );
			}

			// The released or cancelled orb dies on its own clock instead of blinking out under the bolt. Its bodies
			// and meshes go at once (StopEmitting), so nothing solid is left hanging at the gem while the bolt flies.
			if ( _orb is not null )
			{
				FireballVfx.Destroy( _dyingOrb );
				_dyingOrb = _orb; _orb = null;
				SpellVfx.StopEmitting( _dyingOrb );
				_orbDeathAt = Time.Now + 0.6f;
			}
		}

		if ( _dyingOrb is not null )
		{
			if ( !_dyingOrb.Root.IsValid() ) { _dyingOrb = null; return; }
			// it keeps riding the staff while its last particles burn out, instead of hanging where the shot left
			if ( tip is { } at ) _dyingOrb.Root.WorldPosition = at;
			var left = (_orbDeathAt - Time.Now) / 0.6f;
			_dyingOrb.LightBrightness = MathF.Max( 0f, left );
			SpellVfx.Drive( _dyingOrb );
			if ( left <= 0f ) { FireballVfx.Destroy( _dyingOrb ); _dyingOrb = null; }
		}
	}

	/// <summary>
	/// The laser's hairline while it charges: from the gem to whatever the crosshair is on, so a burst that fires the
	/// moment the button lets go is aimed and not guessed. Only the owner traces; a proxy shows the line to its own
	/// aim point, which is close enough for a thing that lives under a second.
	/// </summary>
	private void DriveAimLine( SpellDefinition spell, Vector3 tip, float charge01 )
	{
		if ( spell?.Delivery != SpellDelivery.Beam )
		{
			_aimLine?.Destroy(); _aimLine = null;
			return;
		}
		_aimLine ??= ChargeVfx.AimLine.Build( spell.Palette );
		var eye = _controller.IsValid() ? _controller.EyePosition : tip;
		var aim = AimSolver.Solve( Scene, PlayerRoot, eye, tip, MathF.Max( 4f, spell.BeamRadius ), MathF.Max( 100f, spell.BeamRange ) );
		_aimLine.Drive( tip, aim.Target, charge01 );
	}

	/// <summary>The Echo went off: a smaller flash where the cast was thrown, on every peer.</summary>
	[Rpc.Broadcast( NetFlags.Unreliable )]
	private void ShowEcho( Vector3 at, string spellId )
	{
		var spell = SpellDatabase.Get( spellId );
		SpellVfx.LaunchFlash( Scene, at, spell, (spell?.VfxScale ?? 0.65f) * 0.6f );
		if ( spell?.SoundCast != "" ) VoidMage.Audio.GameAudio.Play( spell?.SoundCast ?? FireballVfx.SoundCast, at );   // "" = a spell cast in silence (the laser)
	}

	/// <summary>The Edge struck with the blade: the carried spell's small burst on the body, on every peer.</summary>
	[Rpc.Broadcast( NetFlags.Unreliable )]
	private void ShowEdge( Vector3 at, string spellId )
	{
		var spell = SpellDatabase.Get( spellId );
		if ( spell is null ) return;
		SpellVfx.Bounce( Scene, at, Vector3.Up, spell, spell.VfxScale * 1.4f );
	}

	/// <summary>A proc (The Conduit, a wisp, the hail) left: the launch flash without the gesture.</summary>
	[Rpc.Broadcast( NetFlags.Unreliable )]
	private void ShowProc( Vector3 at, string spellId )
	{
		var spell = SpellDatabase.Get( spellId );
		SpellVfx.LaunchFlash( Scene, at, spell, (spell?.VfxScale ?? 0.65f) * 0.5f );
	}
}
