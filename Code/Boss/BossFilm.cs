using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Boss;

/// <summary>
/// The camera crew: dev commands that photograph the boss so the pictures, not a description, say what is wrong.
/// `boss_film [take]` shoots a take (the stance from five angles, the wake-up, or any move forced on the spot)
/// from a director camera framing the boss and from the player's own view, every `boss_film_step` seconds, and
/// writes the frames plus one contact sheet per camera to the game's data folder
/// (`sbox/data/local/voidmage#local/boss_film/`). Optional: the tuning loop is `boss_tune` + the saved convars.
/// </summary>
public static class BossFilm
{
	[ConVar( "boss_film_width" )] public static int Width { get; set; } = 960;
	[ConVar( "boss_film_step" )] public static float Step { get; set; } = 0.4f;
	/// <summary>Director camera distance from the skull, world units, and its yaw off the player's line.</summary>
	[ConVar( "boss_film_distance" )] public static float Distance { get; set; } = 2400f;
	[ConVar( "boss_film_yaw" )] public static float Yaw { get; set; } = 35f;

	private const string Dir = "boss_film";

	private static string _take = "";
	private static float _nextAt, _endAt;
	private static int _frame;
	private static readonly List<Bitmap> _director = new();
	private static readonly List<Bitmap> _pov = new();
	private static Action _startAction;
	private static float _startAt;
	private static Texture _rt;

	public static bool Rolling => !string.IsNullOrEmpty( _take );

	/// <summary>`boss_film [stance, wake, slam, sweep, bite or shake] [l or r] [speed]`: host, while playing with the boss up.</summary>
	[ConCmd( "boss_film" )]
	public static void Command( string take = "stance", string hand = "l", float speed = 1f )
	{
		if ( !Networking.IsHost || !Game.IsPlaying ) { Log.Warning( "[Film] host, while playing" ); return; }
		var brain = BossBrain.Current;
		if ( !brain.IsValid() ) { Log.Warning( "[Film] no boss (boss_arena first)" ); return; }
		if ( Rolling ) { Log.Warning( $"[Film] still rolling '{_take}'" ); return; }
		FileSystem.Data.CreateDirectory( Dir );
		take = take.ToLowerInvariant();
		switch ( take )
		{
			case "stance":
				ShootStance( brain );
				return;
			case "wake":
			{
				if ( brain.Phase != BossPhase.Dormant )
				{
					BossNet.Clear();
					HellArena.SpawnBossHost( Game.ActiveScene );
				}
				var seconds = (BossBrain.Current.IsValid() ? BossBrain.Current.WakeSeconds : 8f) + 2f;
				Start( take, seconds, () => BossBrain.Current?.Wake(), 0.6f );
				return;
			}
			default:
			{
				var attack = BossAttacks.Find( take );
				if ( attack is null ) { Log.Warning( $"[Film] takes: stance, wake, {string.Join( ", ", BossAttacks.All.Select( a => a.Name ) )}" ); return; }
				if ( !brain.Awake ) { Log.Warning( "[Film] not awake (boss_wake first)" ); return; }
				var part = BossPartKind.Skull;
				if ( attack.UsesHand ) part = hand is "r" or "right" ? BossPartKind.HandR : BossPartKind.HandL;
				var s = MathF.Max( 0.2f, speed );
				var plan = new BossAttackCtx { Speed = s, ApproachSeconds = attack.PlanApproach( 1200f, s ) };
				Start( $"{take}_{(attack.UsesHand ? part.ToString().ToLowerInvariant() : "skull")}", attack.Total( plan ) + 2f, () => BossBrain.Current?.Begin( attack, part, s ), 0.2f );
				return;
			}
		}
	}

	// ------------------------------------------------------------------ takes

	private static void Start( string take, float seconds, Action action, float delay )
	{
		_take = take;
		_frame = 0;
		_startAction = action;
		_startAt = Time.Now + delay;
		_nextAt = Time.Now;
		_endAt = _startAt + seconds;
		Log.Info( $"[Film] rolling '{take}' for {seconds:0.0} s every {Step:0.00} s" );
	}

	internal static void Tick()
	{
		if ( !Rolling ) return;
		if ( _startAction is not null && Time.Now >= _startAt )
		{
			_startAction();
			_startAction = null;
		}
		if ( Time.Now >= _nextAt )
		{
			_nextAt = Time.Now + MathF.Max( 0.1f, Step );
			var brain = BossBrain.Current;
			if ( brain.IsValid() )
			{
				_director.Add( Shot( DirectorPose( brain, Yaw, Distance, 0.85f ), 70f ) );
				_pov.Add( Shot( null, 0f ) );
				_frame++;
			}
		}
		if ( Time.Now >= _endAt ) Finish();
	}

	private static void Finish()
	{
		var take = _take;
		_take = "";
		WriteSequence( take, "dir", _director );
		WriteSequence( take, "pov", _pov );
		Log.Info( $"[Film] '{take}' done: {_frame} frames x 2 cameras, sheets {Dir}/{take}_dir_sheet.png and {take}_pov_sheet.png (step {Step:0.00} s, row-major)" );
	}

	private static void WriteSequence( string take, string cam, List<Bitmap> frames )
	{
		for ( var i = 0; i < frames.Count; i++ )
			if ( frames[i] is not null ) FileSystem.Data.WriteAllBytes( $"{Dir}/{take}_{i:00}_{cam}.png", frames[i].ToPng() );
		WriteSheet( $"{Dir}/{take}_{cam}_sheet.png", frames );
		foreach ( var b in frames ) b?.Dispose();
		frames.Clear();
	}

	/// <summary>A contact sheet: four thumbnails per row, half size, in frame order.</summary>
	private static void WriteSheet( string path, List<Bitmap> frames )
	{
		var valid = frames.Where( f => f is not null ).ToList();
		if ( valid.Count == 0 ) return;
		var cols = Math.Min( 4, valid.Count );
		var rows = (valid.Count + cols - 1) / cols;
		var tw = valid[0].Width / 2;
		var th = valid[0].Height / 2;
		using var sheet = new Bitmap( cols * tw, rows * th );
		sheet.Clear( Color.Black );
		for ( var i = 0; i < valid.Count; i++ )
		{
			using var thumb = valid[i].Resize( tw, th );
			sheet.DrawBitmap( thumb, new Rect( (i % cols) * tw, (i / cols) * th, tw, th ) );
		}
		FileSystem.Data.WriteAllBytes( path, sheet.ToPng() );
	}

	/// <summary>The stance from five angles: front, side, high, and a close-up of each hand.</summary>
	private static void ShootStance( BossBrain brain )
	{
		var shots = new List<Bitmap>
		{
			Shot( DirectorPose( brain, 0f, Distance * 0.8f, 0.55f ), 70f ),
			Shot( DirectorPose( brain, 90f, Distance * 0.8f, 0.55f ), 70f ),
			Shot( DirectorPose( brain, 35f, Distance * 0.7f, 1.4f ), 70f ),
		};
		foreach ( var kind in new[] { BossPartKind.HandL, BossPartKind.HandR } )
		{
			var hand = BossNet.Find( kind );
			if ( !hand.IsValid() ) continue;
			var from = hand.AimPoint + brain.Facing.Forward * hand.WorldBounds.Size.Length * 1.1f + Vector3.Up * 120f;
			shots.Add( Shot( new Transform( from, Rotation.LookAt( hand.AimPoint - from ) ), 60f ) );
		}
		shots.Add( Shot( null, 0f ) );
		var names = new[] { "front", "side", "high", "hand_l", "hand_r", "pov" };
		for ( var i = 0; i < shots.Count; i++ )
			if ( shots[i] is not null ) FileSystem.Data.WriteAllBytes( $"{Dir}/stance_{names[Math.Min( i, names.Length - 1 )]}.png", shots[i].ToPng() );
		WriteSheet( $"{Dir}/stance_sheet.png", shots );
		foreach ( var b in shots ) b?.Dispose();
		Log.Info( $"[Film] stance: {shots.Count} shots in {Dir}/stance_*.png + stance_sheet.png" );
	}

	/// <summary>A camera on the player's side of the skull, `yaw` degrees off the player line, looking at the boss; `heightFactor` scales the elevation.</summary>
	private static Transform DirectorPose( BossBrain brain, float yaw, float distance, float heightFactor )
	{
		var skull = brain.WorldPosition;
		var player = PlayerHealth.Local;
		var dir = player.IsValid() ? (player.FeetPosition - skull).WithZ( 0f ) : -brain.Facing.Forward;
		if ( dir.IsNearZeroLength ) dir = brain.Facing.Forward;
		dir = Rotation.FromYaw( yaw ) * dir.Normal;
		var pos = skull + dir * distance + Vector3.Up * distance * 0.22f * heightFactor;
		var look = skull + Vector3.Down * 60f * brain.S;
		return new Transform( pos, Rotation.LookAt( look - pos ) );
	}

	/// <summary>Render the main camera from a pose (null = as it is: the player's view) into a fresh bitmap.</summary>
	private static Bitmap Shot( Transform? pose, float fov )
	{
		var cam = Game.ActiveScene?.Camera;
		if ( !cam.IsValid() ) return null;
		var w = Math.Max( 320, Width );
		var h = w * 9 / 16;
		var bmp = new Bitmap( w, h );
		var savedPos = cam.WorldPosition;
		var savedRot = cam.WorldRotation;
		var savedFov = cam.FieldOfView;
		if ( pose is { } p )
		{
			cam.WorldPosition = p.Position;
			cam.WorldRotation = p.Rotation;
			cam.FieldOfView = fov;
		}
		try
		{
			cam.RenderToBitmap( bmp, false );
			if ( IsBlank( bmp ) )
			{
				// the older path: render to a texture and copy the pixels
				_rt ??= Texture.CreateRenderTarget().WithSize( w, h ).WithFormat( ImageFormat.RGBA8888 ).Create();
				if ( cam.RenderToTexture( _rt ) )
				{
					var pixels = _rt.GetPixels();
					for ( var y = 0; y < h; y++ )
						for ( var x = 0; x < w; x++ )
							bmp.SetPixel( x, y, pixels[y * w + x] );
				}
			}
		}
		finally
		{
			if ( pose is not null )
			{
				cam.WorldPosition = savedPos;
				cam.WorldRotation = savedRot;
				cam.FieldOfView = savedFov;
			}
		}
		return bmp;
	}

	private static bool IsBlank( Bitmap bmp )
	{
		var px = bmp.GetPixels32();
		var step = Math.Max( 1, px.Length / 64 );
		for ( var i = 0; i < px.Length; i += step )
			if ( px[i].r > 2 || px[i].g > 2 || px[i].b > 2 ) return false;
		return true;
	}
}

/// <summary>Ticks the film takes after everything else moved this frame.</summary>
public sealed class BossFilmSystem : GameObjectSystem
{
	public BossFilmSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 9, Tick, "BossFilm" );
	}

	private void Tick()
	{
		if ( Scene.IsEditor ) return;
		BossFilm.Tick();
	}
}
