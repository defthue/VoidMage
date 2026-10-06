using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace VoidMage.Dev;

/// <summary>
/// The tape: go back in time and film the same moment from another angle (user, 2026-09-23). While ARMED it
/// records, `dev_tape_hz` times a second, the world transform of every object that moved since its last sample
/// and the world pose of every bone of every skinned body near the camera (`dev_tape_bone_hz`, `dev_tape_radius`),
/// keeping the last `dev_tape_seconds` on a ring. Things that would be destroyed while armed (bolts, debris,
/// dead bodies, corpses, the boss's eye bolts) are PARKED instead (disabled, kept: WorldTape.Retire), so a replay
/// can show them again; they are destroyed on disarm.
///
/// REPLAY freezes the game (Scene.TimeScale 0 through the director) and writes the tape's state into the live
/// objects every frame: transforms by interpolation between samples, bones as physics-bone overrides on the
/// renderers (the ragdoll path: SetBoneTransform, cleared on the way out), objects not yet born hidden, parked
/// ones shown for as long as they lived. The free camera and the camera paths run on top, on real time. Scrub,
/// play forward or back at any speed, then Back to live: the present is put back exactly (a snapshot taken on the
/// way in) and the game runs on. Nothing re-simulates: the past cannot be changed, only filmed. Particles and
/// sounds do not rewind (particle renderers are hidden during a replay so present-time smoke does not sit in a
/// past frame).
/// </summary>
public static class WorldTape
{
	[ConVar( "dev_tape_seconds", ConVarFlags.Saved )] public static float Length { get; set; } = 45f;
	[ConVar( "dev_tape_hz", ConVarFlags.Saved )] public static float Hz { get; set; } = 30f;
	[ConVar( "dev_tape_bone_hz", ConVarFlags.Saved )] public static float BoneHz { get; set; } = 20f;
	/// <summary>Bones are recorded for skinned bodies within this of the camera (further ones are placed, not posed).</summary>
	[ConVar( "dev_tape_radius", ConVarFlags.Saved )] public static float Radius { get; set; } = 3000f;

	public static bool Armed { get; private set; }
	public static bool Replaying { get; private set; }
	public static bool Playing { get; private set; }
	/// <summary>Replay speed; negative runs the tape backward.</summary>
	public static float PlaySpeed { get; set; } = 1f;
	/// <summary>The moment on the tape being shown, real-time seconds (the tape's own clock).</summary>
	public static float TapeTime { get; private set; }
	public static float Oldest { get; private set; }
	public static float Newest { get; private set; }
	public static float Seconds => MathF.Max( 0f, Newest - Oldest );
	/// <summary>Seconds back from the present the shown moment is.</summary>
	public static float Back => Replaying ? MathF.Max( 0f, Newest - TapeTime ) : 0f;

	private sealed class Track
	{
		public GameObject Go;
		public int Depth;
		public bool Parked;
		public bool Hidden;
		public bool EnabledAtPresent = true;
		public readonly List<float> T = new();
		public readonly List<Transform> X = new();
		public SkinnedModelRenderer Skin;
		public readonly List<float> BT = new();
		public readonly List<Transform[]> B = new();
		public Transform Present;
		public Transform[] PresentBones;
		public bool BonesTouched;
		public float LastBoneSample = float.NegativeInfinity;
	}

	private static readonly Dictionary<Guid, Track> _tracks = new();
	private static readonly List<Track> _ordered = new();
	private static readonly List<GameObject> _parked = new();
	private static readonly List<Component> _hiddenParticles = new();
	private static float _nextSample = float.NegativeInfinity;
	private static float _nextTrim = float.NegativeInfinity;
	private static float _armedAt;
	private static bool _pausedBefore;
	private static long _boneTransforms;

	/// <summary>A rough size of what is held, megabytes.</summary>
	public static float MegaBytes => (_tracks.Values.Sum( t => t.X.Count ) * 44L + _boneTransforms * 40L) / 1048576f;
	public static int Tracks => _tracks.Count;
	public static int ParkedCount => _parked.Count;

	// ------------------------------------------------------------------ arming

	public static void Arm()
	{
		if ( Armed ) return;
		Clear();
		Armed = true;
		_armedAt = RealTime.Now;
		Oldest = Newest = _armedAt;
		_nextSample = 0f;
		Log.Info( $"[Tape] recording the last {Length:0} s" );
	}

	public static void Disarm()
	{
		if ( Replaying ) ExitReplay();
		Armed = false;
		Clear();
		Log.Info( "[Tape] stopped" );
	}

	public static void Toggle() { if ( Armed ) Disarm(); else Arm(); }

	private static void Clear()
	{
		foreach ( var go in _parked ) if ( go.IsValid() ) go.Destroy();
		_parked.Clear();
		_tracks.Clear();
		_ordered.Clear();
		_boneTransforms = 0;
	}

	/// <summary>
	/// What a thing that has finished calls in place of GameObject.Destroy(): with the tape armed it is parked
	/// (taken off its own registry through `unregister`, then disabled and kept for the replay); otherwise destroyed.
	/// </summary>
	public static void Retire( GameObject go, Action unregister = null )
	{
		if ( !go.IsValid() ) return;
		if ( !Armed || Replaying )
		{
			go.Destroy();
			return;
		}
		unregister?.Invoke();
		go.Enabled = false;
		_parked.Add( go );
		if ( _tracks.TryGetValue( go.Id, out var track ) ) track.Parked = true;
		foreach ( var child in go.GetAllObjects( false ) )
			if ( child != go && _tracks.TryGetValue( child.Id, out var ct ) ) ct.Parked = true;
	}

	// ------------------------------------------------------------------ recording

	private static readonly string[] SkipTags = { "viewmodel", "posesource" };

	internal static void Tick( Scene scene )
	{
		if ( !Armed || !scene.IsValid() ) return;
		if ( Replaying )
		{
			if ( Playing ) Advance( RealTime.Delta );
			return;
		}
		var now = RealTime.Now;
		if ( now < _nextSample ) return;
		_nextSample = now + 1f / MathF.Max( 1f, Hz );
		Sample( scene, now );
		if ( now >= _nextTrim ) { _nextTrim = now + 2f; Trim( now - Length ); }
	}

	private static void Sample( Scene scene, float now )
	{
		Newest = now;
		var camera = scene.Camera;
		var eye = camera.IsValid() ? camera.WorldPosition : Vector3.Zero;
		var cameraGo = camera.IsValid() ? camera.GameObject : null;
		var director = DevDirector.Current.IsValid() ? DevDirector.Current.GameObject : null;
		foreach ( var go in scene.GetAllObjects( true ) )
		{
			if ( !go.IsValid() || go == scene || go == cameraGo || go == director ) continue;
			var flags = go.Flags;
			if ( flags.HasFlag( GameObjectFlags.Bone ) || flags.HasFlag( GameObjectFlags.ProceduralBone ) || flags.HasFlag( GameObjectFlags.Attachment ) ) continue;
			if ( go.Tags.HasAny( SkipTags ) ) continue;
			var x = go.WorldTransform;
			if ( !_tracks.TryGetValue( go.Id, out var track ) )
			{
				track = new Track { Go = go, Depth = DepthOf( go ), Skin = SkinOf( go ) };
				_tracks[go.Id] = track;
				_ordered.Add( track );
				_orderDirty = true;
			}
			if ( track.X.Count == 0 || Moved( track.X[^1], x ) )
			{
				track.T.Add( now );
				track.X.Add( x );
			}
			// the bones of a skinned body near the camera, at their own rate
			var skin = track.Skin;
			if ( skin.IsValid() && skin.Enabled && now >= track.LastBoneSample + 1f / MathF.Max( 1f, BoneHz ) && x.Position.DistanceSquared( eye ) < Radius * Radius )
			{
				track.LastBoneSample = now;
				var bones = skin.BoneWorldTransforms;
				if ( bones.Length > 0 && (track.B.Count == 0 || BonesMoved( track.B[^1], bones )) )
				{
					track.BT.Add( now );
					track.B.Add( bones.ToArray() );
					_boneTransforms += bones.Length;
				}
			}
		}
		if ( _orderDirty ) { _ordered.Sort( ( a, b ) => a.Depth.CompareTo( b.Depth ) ); _orderDirty = false; }
	}

	private static bool _orderDirty;

	private static SkinnedModelRenderer SkinOf( GameObject go )
	{
		var skin = go.Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelf );
		return skin.IsValid() && skin.BoneMergeTarget is null ? skin : null;
	}

	private static int DepthOf( GameObject go )
	{
		var d = 0;
		for ( var p = go.Parent; p.IsValid(); p = p.Parent ) d++;
		return d;
	}

	private static bool Moved( in Transform a, in Transform b )
		=> a.Position.DistanceSquared( b.Position ) > 0.0001f || a.Rotation.Distance( b.Rotation ) > 0.01f || a.Scale.DistanceSquared( b.Scale ) > 0.00001f;

	private static bool BonesMoved( Transform[] last, ReadOnlySpan<Transform> now )
	{
		if ( last.Length != now.Length ) return true;
		for ( var i = 0; i < now.Length; i++ )
			if ( last[i].Position.DistanceSquared( now[i].Position ) > 0.0025f || last[i].Rotation.Distance( now[i].Rotation ) > 0.05f ) return true;
		return false;
	}

	private static void Trim( float cutoff )
	{
		var oldest = float.MaxValue;
		for ( var i = _ordered.Count - 1; i >= 0; i--)
		{
			var t = _ordered[i];
			if ( !t.Go.IsValid() ) { _tracks.Remove( t.Go?.Id ?? Guid.Empty ); _ordered.RemoveAt( i ); continue; }
			// keep the last sample before the cutoff: the state the object was in as the window opens
			var k = 0;
			while ( k + 1 < t.T.Count && t.T[k + 1] <= cutoff ) k++;
			if ( k > 0 ) { t.T.RemoveRange( 0, k ); t.X.RemoveRange( 0, k ); }
			var kb = 0;
			while ( kb + 1 < t.BT.Count && t.BT[kb + 1] <= cutoff ) kb++;
			if ( kb > 0 )
			{
				for ( var j = 0; j < kb; j++ ) _boneTransforms -= t.B[j].Length;
				t.BT.RemoveRange( 0, kb ); t.B.RemoveRange( 0, kb );
			}
			if ( t.T.Count > 0 ) oldest = MathF.Min( oldest, t.T[0] );
		}
		Oldest = MathF.Max( cutoff, oldest == float.MaxValue ? cutoff : MathF.Min( oldest, cutoff ) );
		if ( Oldest < _armedAt ) Oldest = _armedAt;
		if ( Newest - Oldest > Length ) Oldest = Newest - Length;
	}

	// ------------------------------------------------------------------ replay

	public static void EnterReplay()
	{
		if ( !Armed || Replaying || Seconds < 0.2f ) { if ( !Armed ) Log.Info( "[Tape] arm the tape first" ); return; }
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		Sample( scene, RealTime.Now );   // the present, exactly, as the last frame of the tape
		foreach ( var t in _ordered )
		{
			if ( !t.Go.IsValid() ) continue;
			t.Present = t.Go.WorldTransform;
			t.EnabledAtPresent = t.Go.Enabled;
			t.Hidden = false;
			t.BonesTouched = false;
			t.PresentBones = t.Skin.IsValid() && t.Skin.Enabled ? t.Skin.GetBoneTransforms( true ) : null;
		}
		_hiddenParticles.Clear();
		foreach ( var r in scene.GetAll<ParticleSpriteRenderer>() ) if ( r.IsValid() && r.Enabled ) { r.Enabled = false; _hiddenParticles.Add( r ); }
		foreach ( var r in scene.GetAll<ParticleModelRenderer>() ) if ( r.IsValid() && r.Enabled ) { r.Enabled = false; _hiddenParticles.Add( r ); }
		foreach ( var r in scene.GetAll<TrailRenderer>() ) if ( r.IsValid() && r.Enabled ) { r.Enabled = false; _hiddenParticles.Add( r ); }
		_pausedBefore = DevDirector.Paused;
		DevDirector.SetPaused( true );
		if ( !DevDirector.FreeCam ) DevDirector.SetFreeCam( true );
		Replaying = true;
		Playing = false;
		TapeTime = Newest;
		Log.Info( $"[Tape] replay: {Seconds:0.0} s on the tape, {Tracks} tracks, {MegaBytes:0} MB" );
	}

	public static void ExitReplay()
	{
		if ( !Replaying ) return;
		foreach ( var t in _ordered )
		{
			if ( !t.Go.IsValid() ) continue;
			if ( t.Parked ) { t.Go.Enabled = false; continue; }
			if ( t.Go.Enabled != t.EnabledAtPresent ) t.Go.Enabled = t.EnabledAtPresent;
			t.Go.WorldTransform = t.Present;
			if ( t.BonesTouched && t.Skin.IsValid() ) t.Skin.ClearPhysicsBones();
			t.Hidden = false;
		}
		foreach ( var r in _hiddenParticles ) if ( r.IsValid() ) r.Enabled = true;
		_hiddenParticles.Clear();
		Replaying = false;
		Playing = false;
		DevDirector.SetPaused( _pausedBefore );
		_nextSample = 0f;
		Log.Info( "[Tape] live" );
	}

	public static void Play( float speed )
	{
		if ( !Replaying ) EnterReplay();
		if ( !Replaying ) return;
		PlaySpeed = speed;
		Playing = true;
		if ( speed > 0f && TapeTime >= Newest - 0.001f ) Seek( Oldest );
		if ( speed < 0f && TapeTime <= Oldest + 0.001f ) Seek( Newest );
	}

	public static void Pause() => Playing = false;

	/// <summary>Seconds along the tape from its start (the panel's scrub).</summary>
	public static void SeekBack( float secondsBack )
	{
		if ( !Replaying ) EnterReplay();
		if ( Replaying ) Seek( Newest - secondsBack );
	}

	public static void Step( float seconds )
	{
		if ( !Replaying ) EnterReplay();
		if ( Replaying ) { Playing = false; Seek( TapeTime + seconds ); }
	}

	private static void Advance( float realDt )
	{
		var t = TapeTime + realDt * PlaySpeed;
		if ( t >= Newest ) { t = Newest; Playing = false; }
		if ( t <= Oldest ) { t = Oldest; Playing = false; }
		Seek( t );
	}

	public static void Seek( float time )
	{
		if ( !Replaying ) return;
		TapeTime = Math.Clamp( time, Oldest, Newest );
		foreach ( var t in _ordered ) Write( t, TapeTime );
	}

	private static void Write( Track t, float time )
	{
		var go = t.Go;
		if ( !go.IsValid() || t.T.Count == 0 ) return;
		// not born yet: hidden
		if ( time < t.T[0] - 0.02f && t.T[0] > _armedAt + 0.5f )
		{
			if ( !t.Hidden ) { go.Enabled = false; t.Hidden = true; }
			return;
		}
		// parked (it ended): hidden once its life on the tape is over
		var last = t.T[^1];
		if ( t.Parked && time > last + 0.1f )
		{
			if ( !t.Hidden ) { go.Enabled = false; t.Hidden = true; }
			return;
		}
		if ( t.Hidden || (t.Parked && !go.Enabled) || (!go.Enabled && t.EnabledAtPresent) ) { go.Enabled = true; t.Hidden = false; }

		go.WorldTransform = SampleAt( t.T, t.X, time );
		if ( t.Skin.IsValid() && t.B.Count > 0 )
		{
			var bones = BonesAt( t, time );
			var all = t.Skin.Model?.Bones?.AllBones;
			if ( bones is not null && all is not null )
			{
				var n = Math.Min( bones.Length, all.Count );
				for ( var i = 0; i < n; i++ ) t.Skin.SetBoneTransform( all[i], bones[i] );
				t.BonesTouched = true;
			}
		}
	}

	private static Transform SampleAt( List<float> times, List<Transform> xs, float time )
	{
		var i = Index( times, time );
		if ( i < 0 ) return xs[0];
		if ( i >= times.Count - 1 ) return xs[^1];
		var span = times[i + 1] - times[i];
		var f = span > 0.0001f ? Math.Clamp( (time - times[i]) / span, 0f, 1f ) : 0f;
		return Lerp( xs[i], xs[i + 1], f );
	}

	private static readonly Dictionary<Track, Transform[]> _scratch = new();

	private static Transform[] BonesAt( Track t, float time )
	{
		var i = Index( t.BT, time );
		if ( i < 0 ) return t.B[0];
		if ( i >= t.BT.Count - 1 ) return t.B[^1];
		var a = t.B[i];
		var b = t.B[i + 1];
		var span = t.BT[i + 1] - t.BT[i];
		var f = span > 0.0001f ? Math.Clamp( (time - t.BT[i]) / span, 0f, 1f ) : 0f;
		if ( a.Length != b.Length ) return f < 0.5f ? a : b;
		if ( !_scratch.TryGetValue( t, out var buf ) || buf.Length != a.Length ) { buf = new Transform[a.Length]; _scratch[t] = buf; }
		for ( var k = 0; k < a.Length; k++ ) buf[k] = Lerp( a[k], b[k], f );
		return buf;
	}

	/// <summary>The last index whose time is at or before `time` (-1 before the first).</summary>
	private static int Index( List<float> times, float time )
	{
		var lo = 0; var hi = times.Count - 1; var best = -1;
		while ( lo <= hi )
		{
			var mid = (lo + hi) / 2;
			if ( times[mid] <= time ) { best = mid; lo = mid + 1; } else hi = mid - 1;
		}
		return best;
	}

	private static Transform Lerp( in Transform a, in Transform b, float f )
		=> new( Vector3.Lerp( a.Position, b.Position, f ), Rotation.Slerp( a.Rotation, b.Rotation, f ), Vector3.Lerp( a.Scale, b.Scale, f ) );
}

/// <summary>Runs the tape after everything has moved and posed this frame.</summary>
public sealed class WorldTapeSystem : GameObjectSystem
{
	public WorldTapeSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 95, Tick, "WorldTape" );
	}

	private void Tick()
	{
		if ( !Game.IsPlaying || Scene.IsEditor ) return;
		WorldTape.Tick( Scene );
	}
}
