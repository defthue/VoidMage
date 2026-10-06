using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Sandbox;
using VoidMage.Boss;
using VoidMage.Character;
using VoidMage.Run;

namespace VoidMage.Dev;

/// <summary>One point of a camera path: where, which way, what lens (0 = the player's), and how long the flight from the point before takes.</summary>
public sealed class CamKey
{
	public Vector3 Pos { get; set; }
	public float Pitch { get; set; }
	public float Yaw { get; set; }
	public float Roll { get; set; }
	public float Fov { get; set; }
	public float Seconds { get; set; } = 2f;

	public Angles Angles => new( Pitch, Yaw, Roll );
}

/// <summary>What drives the free camera when a hand is not: a keyframed flight, a circle round something, a chase behind something.</summary>
public enum CamMode { Free, Path, Orbit, Follow }

/// <summary>What the orbit and the follow look at.</summary>
public enum CamTarget { Player, Skull, HandL, HandR, Point }

/// <summary>
/// The director's camera moves (the free camera flown by rules): a PATH of keyframes taken from where the camera
/// is (a Catmull-Rom flight through them, turning by slerp, the first leg easing in and the last easing out), an
/// ORBIT round the player, a boss bone or a picked point, and a FOLLOW behind one of them. All on real time, so a
/// paused fight can be circled; "game time" makes the camera slow with the slow motion instead. Paths save as JSON
/// in data/trailer/paths and eight quick slots hold the ones in use. DevDirector.TickFreeCam calls Tick.
/// </summary>
public static class CameraPath
{
	public static readonly List<CamKey> Keys = new();
	public static CamMode Mode { get; private set; } = CamMode.Free;
	public static bool Playing { get; private set; }
	public static bool Loop { get; set; }
	/// <summary>The camera's clock follows the game's (slow motion slows it) rather than the wall's.</summary>
	public static bool GameTime { get; set; }
	public static float PlaySpeed { get; set; } = 1f;
	/// <summary>Seconds along the path (0..Duration).</summary>
	public static float PathTime { get; private set; }
	/// <summary>The keyframe the flight is between or on (the panel's highlight).</summary>
	public static int AtKey { get; private set; }

	public static float Duration
	{
		get
		{
			var d = 0f;
			for ( var i = 1; i < Keys.Count; i++ ) d += MathF.Max( 0.05f, Keys[i].Seconds );
			return d;
		}
	}

	// ---- orbit / follow
	public static CamTarget Target { get; set; } = CamTarget.Player;
	public static Vector3 Point { get; set; }
	public static float OrbitRadius { get; set; } = 260f;
	public static float OrbitHeight { get; set; } = 90f;
	/// <summary>Degrees per second; negative circles the other way.</summary>
	public static float OrbitRate { get; set; } = 18f;
	/// <summary>Where on the target the camera looks, up from its feet.</summary>
	public static float LookHeight { get; set; } = 60f;
	public static float FollowBack { get; set; } = 220f;
	public static float FollowUp { get; set; } = 80f;
	public static float FollowSide { get; set; } = 0f;
	/// <summary>How quickly the follow catches up (1/s); higher is stiffer.</summary>
	public static float FollowStiffness { get; set; } = 6f;
	private static float _orbitAngle;
	private static Vector3 _followPos;
	private static bool _followSeeded;

	// ------------------------------------------------------------------ keys

	private static float CurrentFov() => DevDirector.CamFov;

	/// <summary>A key at the camera's pose (the free camera's, or the player's view), `seconds` after the last.</summary>
	public static void AddKey( float seconds = 2f )
	{
		var (pos, a) = Pose();
		Keys.Add( new CamKey { Pos = pos, Pitch = a.pitch, Yaw = a.yaw, Roll = a.roll, Fov = CurrentFov(), Seconds = Keys.Count == 0 ? 0f : seconds } );
		Log.Info( $"[Director] key {Keys.Count} at {pos:0} ({Duration:0.0} s of path)" );
	}

	/// <summary>Key `i` rewritten from the camera's pose (its timing kept).</summary>
	public static void UpdateKey( int i )
	{
		if ( i < 0 || i >= Keys.Count ) return;
		var (pos, a) = Pose();
		var k = Keys[i];
		k.Pos = pos; k.Pitch = a.pitch; k.Yaw = a.yaw; k.Roll = a.roll; k.Fov = CurrentFov();
	}

	public static void RemoveKey( int i )
	{
		if ( i < 0 || i >= Keys.Count ) return;
		Keys.RemoveAt( i );
		if ( Keys.Count > 0 ) Keys[0].Seconds = 0f;
	}

	public static void RemoveLast() => RemoveKey( Keys.Count - 1 );

	public static void Clear()
	{
		Stop();
		Keys.Clear();
	}

	/// <summary>The free camera onto key `i` (to look at it, or to record from it).</summary>
	public static void GoToKey( int i )
	{
		if ( i < 0 || i >= Keys.Count ) return;
		Stop();
		if ( !DevDirector.FreeCam ) DevDirector.SetFreeCam( true );
		var k = Keys[i];
		DevDirector.SetCamPose( k.Pos, k.Angles );
		DevDirector.CamFov = k.Fov;
		PathTime = TimeOfKey( i );
		AtKey = i;
	}

	private static float TimeOfKey( int i )
	{
		var t = 0f;
		for ( var k = 1; k <= i && k < Keys.Count; k++ ) t += MathF.Max( 0.05f, Keys[k].Seconds );
		return t;
	}

	private static (Vector3 pos, Angles angles) Pose()
	{
		if ( DevDirector.FreeCam ) return DevDirector.CamPose;
		var cam = Game.ActiveScene?.Camera;
		if ( !cam.IsValid() ) return (Vector3.Zero, Angles.Zero);
		var a = cam.WorldRotation.Angles();
		return (cam.WorldPosition, new Angles( a.pitch, a.yaw, 0f ));
	}

	// ------------------------------------------------------------------ playback

	/// <summary>Fly the path from the start (or from where the scrub stands).</summary>
	public static void Play( bool fromStart = true )
	{
		if ( Keys.Count < 2 ) { Log.Info( "[Director] a path needs two keys" ); return; }
		if ( !DevDirector.FreeCam ) DevDirector.SetFreeCam( true );
		Mode = CamMode.Path;
		Playing = true;
		if ( fromStart || PathTime >= Duration ) PathTime = 0f;
		Apply( Sample( PathTime ) );
	}

	public static void Pause() => Playing = false;

	public static void Stop()
	{
		Playing = false;
		if ( Mode == CamMode.Path ) Mode = CamMode.Free;
	}

	/// <summary>The camera put at `t` seconds along the path (paused there).</summary>
	public static void Scrub( float t )
	{
		if ( Keys.Count < 2 ) return;
		if ( !DevDirector.FreeCam ) DevDirector.SetFreeCam( true );
		Mode = CamMode.Path;
		Playing = false;
		PathTime = Math.Clamp( t, 0f, Duration );
		Apply( Sample( PathTime ) );
	}

	public static void StartOrbit()
	{
		if ( !DevDirector.FreeCam ) DevDirector.SetFreeCam( true );
		Playing = false;
		Mode = CamMode.Orbit;
		// from where the camera stands now: the angle to it, so the circle begins without a jump
		var (pos, _) = DevDirector.CamPose;
		var centre = TargetPosition();
		var d = (pos - centre).WithZ( 0f );
		_orbitAngle = d.IsNearZeroLength ? 0f : MathF.Atan2( d.y, d.x ).RadianToDegree();
		OrbitRadius = MathF.Max( 30f, d.Length > 10f ? d.Length : OrbitRadius );
		OrbitHeight = pos.z - centre.z;
	}

	public static void StartFollow()
	{
		if ( !DevDirector.FreeCam ) DevDirector.SetFreeCam( true );
		Playing = false;
		Mode = CamMode.Follow;
		_followSeeded = false;
	}

	public static void FreeHand()
	{
		Playing = false;
		Mode = CamMode.Free;
	}

	/// <summary>Where the camera looks at for the orbit and the follow: a pick at the aim point.</summary>
	public static void PickPoint()
	{
		if ( DevActions.AimPoint() is { } p ) { Point = p; Target = CamTarget.Point; Log.Info( $"[Director] orbit point {p:0}" ); }
	}

	private static Vector3 TargetPosition()
	{
		switch ( Target )
		{
			case CamTarget.Skull: if ( BossBrain.Current.IsValid() ) return BossBrain.Current.SkullCenter; break;
			case CamTarget.HandL: if ( BossBrain.Current.IsValid() && BossBrain.Current.HandL.IsValid() ) return BossBrain.Current.HandL.WorldBounds.Center; break;
			case CamTarget.HandR: if ( BossBrain.Current.IsValid() && BossBrain.Current.HandR.IsValid() ) return BossBrain.Current.HandR.WorldBounds.Center; break;
			case CamTarget.Point: return Point;
		}
		var p = PlayerHealth.Local;
		return p.IsValid() ? p.FeetPosition : Point;
	}

	private static float TargetYaw()
	{
		if ( Target == CamTarget.Player )
		{
			var c = PlayerHealth.Local.IsValid() ? PlayerHealth.Local.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors ) : null;
			if ( c.IsValid() ) return c.EyeAngles.yaw;
		}
		if ( Target is CamTarget.Skull or CamTarget.HandL or CamTarget.HandR && BossBrain.Current.IsValid() ) return BossBrain.Current.Facing.Yaw();
		return 0f;
	}

	/// <summary>Every frame from the director while the free camera is on: the rules move it.</summary>
	public static void Tick( float realDt )
	{
		var dt = GameTime ? Time.Delta : realDt;
		switch ( Mode )
		{
			case CamMode.Path:
				if ( !Playing || Keys.Count < 2 ) return;
				PathTime += dt * PlaySpeed;
				if ( PathTime >= Duration )
				{
					if ( Loop ) PathTime -= Duration;
					else { PathTime = Duration; Playing = false; }
				}
				Apply( Sample( PathTime ) );
				break;

			case CamMode.Orbit:
			{
				_orbitAngle += OrbitRate * dt;
				var centre = TargetPosition();
				var look = centre + Vector3.Up * LookHeight;
				var pos = centre + Rotation.FromYaw( _orbitAngle ).Forward * OrbitRadius + Vector3.Up * OrbitHeight;
				var a = Rotation.LookAt( (look - pos).Normal ).Angles();
				DevDirector.SetCamPose( pos, new Angles( a.pitch, a.yaw, 0f ) );
				break;
			}

			case CamMode.Follow:
			{
				var centre = TargetPosition();
				var yaw = Rotation.FromYaw( TargetYaw() );
				var wanted = centre - yaw.Forward * FollowBack + yaw.Left * FollowSide + Vector3.Up * FollowUp;
				if ( !_followSeeded ) { _followPos = wanted; _followSeeded = true; }
				_followPos = Vector3.Lerp( _followPos, wanted, 1f - MathF.Exp( -FollowStiffness * dt ) );
				var look = centre + Vector3.Up * LookHeight;
				var a = Rotation.LookAt( (look - _followPos).Normal ).Angles();
				DevDirector.SetCamPose( _followPos, new Angles( a.pitch, a.yaw, 0f ) );
				break;
			}
		}
	}

	private static void Apply( (Vector3 pos, Angles angles, float fov) s )
	{
		DevDirector.SetCamPose( s.pos, s.angles );
		DevDirector.CamFov = s.fov;
	}

	/// <summary>The camera at `t` seconds along the path.</summary>
	public static (Vector3 pos, Angles angles, float fov) Sample( float t )
	{
		if ( Keys.Count == 0 ) return (Vector3.Zero, Angles.Zero, 0f);
		if ( Keys.Count == 1 ) return (Keys[0].Pos, Keys[0].Angles, Keys[0].Fov);
		t = Math.Clamp( t, 0f, Duration );
		var seg = 0;
		var start = 0f;
		for ( var i = 1; i < Keys.Count; i++ )
		{
			var len = MathF.Max( 0.05f, Keys[i].Seconds );
			if ( t <= start + len || i == Keys.Count - 1 ) { seg = i - 1; break; }
			start += len;
		}
		var segLen = MathF.Max( 0.05f, Keys[seg + 1].Seconds );
		var u = Math.Clamp( (t - start) / segLen, 0f, 1f );
		var last = Keys.Count - 2;
		// the first leg sets off gently and the last one comes to rest; the middle keeps its pace through the keys
		if ( seg == 0 && seg == last ) u = Ease.InOutCubic( u );
		else if ( seg == 0 ) u = Ease.InQuad( u );
		else if ( seg == last ) u = Ease.OutQuad( u );
		AtKey = seg;

		var p0 = Keys[Math.Max( 0, seg - 1 )].Pos;
		var p1 = Keys[seg].Pos;
		var p2 = Keys[seg + 1].Pos;
		var p3 = Keys[Math.Min( Keys.Count - 1, seg + 2 )].Pos;
		var pos = CatmullRom( p0, p1, p2, p3, u );
		var rot = Rotation.Slerp( Keys[seg].Angles.ToRotation(), Keys[seg + 1].Angles.ToRotation(), u );
		var a = rot.Angles();
		var f0 = Keys[seg].Fov > 1f ? Keys[seg].Fov : Preferences.FieldOfView;
		var f1 = Keys[seg + 1].Fov > 1f ? Keys[seg + 1].Fov : Preferences.FieldOfView;
		return (pos, new Angles( a.pitch, a.yaw, DevDirector.CamRollAllowed ? a.roll : 0f ), MathX.Lerp( f0, f1, u ));
	}

	private static Vector3 CatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;
		return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}

	// ------------------------------------------------------------------ files

	private const string Folder = "trailer/paths";

	public static string[] Saved()
	{
		try
		{
			FileSystem.Data.CreateDirectory( Folder );
			return FileSystem.Data.FindFile( Folder, "*.json" ).Select( f => System.IO.Path.GetFileNameWithoutExtension( f ) ).OrderBy( n => n ).ToArray();
		}
		catch { return Array.Empty<string>(); }
	}

	public static void Save( string name )
	{
		if ( Keys.Count == 0 ) return;
		try
		{
			FileSystem.Data.CreateDirectory( Folder );
			FileSystem.Data.WriteAllText( $"{Folder}/{Clean( name )}.json", JsonSerializer.Serialize( Keys ) );
			Log.Info( $"[Director] path '{name}' saved ({Keys.Count} keys)" );
		}
		catch ( Exception e ) { Log.Warning( $"[Director] save failed: {e.Message}" ); }
	}

	public static void Load( string name )
	{
		try
		{
			var text = FileSystem.Data.ReadAllText( $"{Folder}/{Clean( name )}.json" );
			var keys = JsonSerializer.Deserialize<List<CamKey>>( text );
			if ( keys is null ) return;
			Stop();
			Keys.Clear();
			Keys.AddRange( keys );
			PathTime = 0f;
			Log.Info( $"[Director] path '{name}' loaded ({Keys.Count} keys, {Duration:0.0} s)" );
		}
		catch ( Exception e ) { Log.Warning( $"[Director] load failed: {e.Message}" ); }
	}

	/// <summary>The quick paths are named after the key that flies them: 3 to 9.</summary>
	public const int FirstKey = 3, LastKey = 9;
	public static bool KeyUsed( int key ) => FileSystem.Data.FileExists( $"{Folder}/key{key}.json" );
	public static void SaveToKey( int key ) => Save( $"key{key}" );
	public static void LoadKey( int key ) => Load( $"key{key}" );

	private static string Clean( string name ) => new string( (name ?? "path").Where( c => char.IsLetterOrDigit( c ) || c is '_' or '-' ).ToArray() ) is { Length: > 0 } s ? s : "path";
}
