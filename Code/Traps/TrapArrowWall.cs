using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Traps;

/// <summary>
/// The arrow wall: holes in one side wall of a hallway cell (the tell, with old arrows stuck in the wall opposite)
/// and the cell in front of them is the plate. Step in and after a short wind-up (clicks) a volley flies across the
/// cell from the holes at chest and head height; whatever stands tall in the cell as it crosses is hit, a crouching
/// body is under it. The arrows stick in the far wall for a moment; the wall reloads and listens again.
/// </summary>
[Title( "Trap: Arrow Wall" ), Category( "Void Mage" ), Icon( "warning" )]
public sealed class TrapArrowWall : TrapBase
{
	/// <summary>Seconds from the step to the volley: the clicks a player who knows the tell can still react to.</summary>
	[ConVar( "trap_arrow_windup" )] public static float Windup { get; set; } = 0.5f;
	/// <summary>Arrow flight speed, units/s.</summary>
	[ConVar( "trap_arrow_speed" )] public static float Speed { get; set; } = 1400f;
	/// <summary>Seconds a spent arrow stays stuck in the far wall.</summary>
	[ConVar( "trap_arrow_linger" )] public static float Linger { get; set; } = 4f;
	/// <summary>Seconds after the volley before the wall listens again.</summary>
	[ConVar( "trap_arrow_reset" )] public static float ResetSeconds { get; set; } = 3f;
	/// <summary>Damage of the whole volley on a body it crosses.</summary>
	[ConVar( "trap_arrow_damage" )] public static float Damage { get; set; } = 20f;
	/// <summary>Knockback (units/s) along the flight.</summary>
	[ConVar( "trap_arrow_push" )] public static float Push { get; set; } = 220f;

	public const string SoundVolley = "sounds/hitnothinglight.sound";
	public const string SoundStick = "sounds/swordhittable.sound";
	public const string DamageKind = "trap_arrow";

	/// <summary>World points the arrows leave from (the holes' mouths) and the unit direction they fly.</summary>
	public Vector3[] Holes { get; set; } = Array.Empty<Vector3>();
	public Vector3 Aim { get; set; } = Vector3.Forward;
	/// <summary>Flight distance (units) to the far wall face.</summary>
	public float Range { get; set; } = 170f;
	public string ArrowModel { get; set; } = "dungeonpack/sm_item_arrow_01.vmdl";
	/// <summary>The dungeon object the flying arrows are parented under.</summary>
	public GameObject Dungeon { get; set; }

	private readonly List<(GameObject go, float born, bool stuck)> _arrows = new();
	private bool _hitDone;

	public override float WindupSeconds => Windup;

	protected override void OnArmed()
	{
		if ( Audible( Centre ) ) Audio.GameAudio.Play( SoundEngage, Centre );
	}

	protected override void Tick( float now, float dt, TrapSystem.Targets targets, bool near )
	{
		FlyArrows( now, dt, near );
		switch ( State )
		{
			case TrapState.WindUp:
				if ( StateAge >= Windup )
				{
					Enter( TrapState.Fire );
					_hitDone = false;
					Loose( near );
				}
				break;

			case TrapState.Fire:
			{
				// the volley crosses the cell in a blink: judge once when it is about halfway
				float crossing = Range * 0.5f / MathF.Max( 100f, Speed );
				if ( !_hitDone && StateAge >= crossing )
				{
					_hitDone = true;
					var push = Aim.WithZ( 0f );
					var pushDir = push.IsNearZeroLength ? Vector3.Zero : push.Normal;
					HitInside( targets, Damage, DamageKind, _ => pushDir * Push, _ => pushDir );
				}
				if ( StateAge >= crossing + 0.2f ) Enter( TrapState.Reset );
				break;
			}

			case TrapState.Reset:
				if ( StateAge >= ResetSeconds ) Enter( TrapState.Armed );
				break;
		}
	}

	private void Loose( bool near )
	{
		if ( near ) Audio.GameAudio.Play( SoundVolley, Centre );
		var parent = Dungeon.IsValid() ? Dungeon : GameObject;
		var model = Model.Load( ArrowModel );
		foreach ( var hole in Holes )
		{
			var go = new GameObject( parent, true, "trap arrow" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
			go.WorldPosition = hole;
			go.WorldRotation = Rotation.LookAt( Aim );
			var r = go.Components.Create<ModelRenderer>();
			r.Model = model;
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
			_arrows.Add( (go, Time.Now, false) );
		}
	}

	/// <summary>Arrows in flight move along the aim; at the far wall they stop and stay a while, then vanish.</summary>
	private void FlyArrows( float now, float dt, bool near )
	{
		for ( int i = _arrows.Count - 1; i >= 0; i-- )
		{
			var (go, born, stuck) = _arrows[i];
			if ( !go.IsValid() ) { _arrows.RemoveAt( i ); continue; }
			if ( stuck )
			{
				if ( now - born >= Linger ) { go.Destroy(); _arrows.RemoveAt( i ); }
				continue;
			}
			float flown = (now - born) * Speed;
			if ( flown >= Range )
			{
				// the tip buries itself: the arrow stops a little short of the wall face
				go.WorldPosition = go.WorldPosition + Aim * (Range - flown + Speed * dt) - Aim * 12f;
				_arrows[i] = (go, now, true);
				if ( near ) Audio.GameAudio.Play( SoundStick, go.WorldPosition );
				continue;
			}
			go.WorldPosition += Aim * (Speed * dt);
		}
	}

	protected override void OnDestroy()
	{
		foreach ( var (go, _, _) in _arrows ) if ( go.IsValid() ) go.Destroy();
		_arrows.Clear();
	}
}
