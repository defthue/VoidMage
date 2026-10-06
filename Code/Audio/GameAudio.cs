using System;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Audio;

namespace VoidMage.Audio;

/// <summary>
/// The one place game code plays a sound event by path. Wraps Sound.Play so a path that resolves to
/// nothing is reported once in the console instead of failing silently, and the first play of each
/// event is logged, which is how "I hear nothing" gets diagnosed from the log.
///
/// It is also where a pile-up of sound is kept from turning into noise, in two layers:
///  - stacking: every further copy of the SAME event inside a short window plays quieter than the one
///    before it (a geometric falloff, so twenty explosions sum to about three, not twenty);
///  - a MasterLimiter on the master mixer, which turns the whole mix down before the engine's hard clip
///    can chop it, for the pile-ups that are made of different events.
/// </summary>
public static class GameAudio
{
	/// <summary>Seconds a played event keeps quietening the next copy of itself (fades out linearly over this).</summary>
	[ConVar( "audio_stack_window" )] public static float StackWindow { get; set; } = 0.35f;
	/// <summary>Volume of each further copy against the one before it. 1 turns stacking off.</summary>
	[ConVar( "audio_stack_falloff" )] public static float StackFalloff { get; set; } = 0.65f;
	/// <summary>The quietest a stacked copy is ever played.</summary>
	[ConVar( "audio_stack_floor" )] public static float StackFloor { get; set; } = 0.06f;
	/// <summary>The master limiter; 0 gives the engine's raw hard clip back, for comparing.</summary>
	[ConVar( "audio_limiter" )] public static bool LimiterEnabled { get; set; } = true;
	/// <summary>Level (0..1) the limiter holds the mix under.</summary>
	[ConVar( "audio_limiter_threshold" )] public static float LimiterThreshold { get; set; } = 0.7f;
	/// <summary>Seconds the limiter takes to let the mix back up.</summary>
	[ConVar( "audio_limiter_release" )] public static float LimiterRelease { get; set; } = 0.15f;
	/// <summary>Seconds the limiter takes to clamp down on a peak.</summary>
	[ConVar( "audio_limiter_attack" )] public static float LimiterAttack { get; set; } = 0.003f;
	/// <summary>The deepest the limiter ever turns the whole mix down (0.35 = -9 dB); louder peaks are rounded off instead of ducking everything.</summary>
	[ConVar( "audio_limiter_floor" )] public static float LimiterFloor { get; set; } = 0.35f;
	/// <summary>Log what the two layers are doing: stacked plays as they happen, the limiter's cut once a second.</summary>
	[ConVar( "audio_debug" )] public static bool DebugLog { get; set; } = false;

	/// <summary>heden's ReceiveItem (the ore and wood pickup chime): every pickup in the game, shards to frogs.</summary>
	public const string ReceiveItemSound = "sounds/receiveitem.sound";

	/// <summary>Past this from the listener a one-shot is not started at all: the engine mixes at most 64 voices per
	/// mixer, NEWEST first, and a voice out of earshot still holds a slot (a footstep 40k units away did, in the log),
	/// which is what silenced older sounds mid-play and let them back in later. 0 turns the cull off.</summary>
	[ConVar( "audio_cull_distance" )] public static float CullDistance { get; set; } = 1600f;

	/// <summary>Tags whose bodies never block a sound (added to the master mixer's ignore list in Upkeep).</summary>
	private static readonly string[] PassAudioTags = { VoidMage.Combat.Debris.Tag, "potion", "critter", "pickup", "viewmodel", "posesource" };

	private static readonly HashSet<string> _seen = new();
	private static readonly HashSet<string> _failed = new();
	/// <summary>Per event: when it was played lately and how near the listener (0 out of earshot .. 1 on top of it).</summary>
	private static readonly Dictionary<string, List<(float time, float nearness)>> _recent = new();
	private static float _nextUpkeep;

	/// <summary>True when a one-shot at `position` would be out of earshot anyway (see <see cref="CullDistance"/>).</summary>
	public static bool OutOfEarshot( Vector3 position )
	{
		var cull = CullDistance;
		return cull > 0f && Sound.Listener.Position.DistanceSquared( position ) > cull * cull;
	}

	/// <summary>A one-shot in the world. Copies of the same event landing together are stacked down; one nobody could hear is not started.</summary>
	public static SoundHandle Play( string eventPath, Vector3 position )
	{
		Upkeep();
		if ( OutOfEarshot( position ) ) return null;
		var handle = Sound.Play( eventPath, position );
		Report( eventPath, handle, position );
		Stack( eventPath, handle, position );
		return handle;
	}

	/// <summary>
	/// A sound that is held and stopped later (a loop, a charge, a torch's crackle). Never stacked down: the
	/// cut would last as long as the handle does, and a room's torches all start on the same frame.
	/// </summary>
	public static SoundHandle PlayLoop( string eventPath, Vector3 position )
	{
		Upkeep();
		var handle = Sound.Play( eventPath, position );
		Report( eventPath, handle, position );
		return handle;
	}

	/// <summary>A 2D play (no position: rain, thunder, UI); reported against the listener so the log line reads the same.</summary>
	public static SoundHandle Play2D( string eventPath )
	{
		Upkeep();
		var handle = Sound.Play( eventPath );
		Report( eventPath, handle, Sound.Listener.Position );
		return handle;
	}

	// ------------------------------------------------------------------ stacking

	private static void Stack( string eventPath, SoundHandle handle, Vector3 position )
	{
		if ( handle is null || !handle.IsValid() ) return;
		var window = StackWindow;
		var falloff = Math.Clamp( StackFalloff, 0.05f, 1f );
		if ( window <= 0f || falloff >= 1f ) return;

		if ( !_recent.TryGetValue( eventPath, out var plays ) ) _recent[eventPath] = plays = new();

		// how much of this event is already in the air: each earlier play counts for less as it ages, and for
		// less the further off it was, so a blast down the corridor does not swallow the one at your feet
		var now = RealTime.Now;
		var load = 0f;
		for ( var i = plays.Count - 1; i >= 0; i-- )
		{
			var age = now - plays[i].time;
			if ( age < 0f || age >= window ) { plays.RemoveAt( i ); continue; }
			load += (1f - age / window) * plays[i].nearness;
		}

		var reach = MathF.Max( handle.Distance, 1f );
		var nearness = 1f - Math.Clamp( Sound.Listener.Position.Distance( position ) / reach, 0f, 1f );
		plays.Add( (now, MathF.Max( nearness, 0.15f )) );

		if ( load <= 0f ) return;
		var scale = MathF.Max( MathF.Pow( falloff, load ), StackFloor );
		handle.Volume *= scale;
		if ( DebugLog ) Log.Info( $"[Audio] stacked '{eventPath}' x{scale:0.00} (load {load:0.0})" );
	}

	// ------------------------------------------------------------------ limiter

	/// <summary>
	/// Keeps the limiter on the master mixer and its knobs on the convars. Checked from the plays themselves
	/// (twice a second at most): the engine rebuilds the mixer tree when a session starts, which drops it.
	/// </summary>
	private static void Upkeep()
	{
		var now = RealTime.Now;
		if ( now < _nextUpkeep && now > _nextUpkeep - 1f ) return;
		_nextUpkeep = now + 0.5f;

		var master = Mixer.Master;
		if ( master is null ) return;

		var limiter = master.GetProcessor<MasterLimiter>();
		if ( limiter is null )
		{
			limiter = new MasterLimiter();
			master.AddProcessor( limiter );
			Log.Info( "[Audio] master limiter on" );
		}

		limiter.Enabled = LimiterEnabled;
		limiter.Threshold = LimiterThreshold;
		limiter.Release = LimiterRelease;
		limiter.Attack = LimiterAttack;
		limiter.Floor = LimiterFloor;

		// the engine's occlusion rays treat every physics body as a wall unless its tag is on the master's ignore list,
		// and debris pieces (up to 40 per crate, boxes on rigidbodies) are exactly what flies between the listener
		// and a break's own sound: three of them on the ray and the smash went silent, then came back as they landed.
		// The pickups and the frogs never muffle anything either. Master's list is non-empty by default, so
		// adding to it keeps the engine's own entries (passaudio, sky, trigger, player...).
		var ignored = master.IgnoredTags;
		if ( ignored is not null )
		{
			foreach ( var tag in PassAudioTags )
				if ( !ignored.Has( tag ) ) ignored.Add( tag );
		}

		// the deepest cut of the last half second, not just the last 12 ms buffer: a dip that reads as a drop-out lasts
		// a few buffers and would slip between two upkeeps otherwise
		var deepest = limiter.ReadDeepest();
		if ( DebugLog && deepest < 0.9f )
			Log.Info( $"[Audio] limiter dipped to x{deepest:0.00} ({20f * MathF.Log10( deepest ):0.0} dB) in the last {(_nextUpkeep - now + 0.5f):0.0} s" );
	}

	private static void Report( string eventPath, SoundHandle handle, Vector3 position )
	{
		if ( handle is null || !handle.IsValid() )
		{
			if ( _failed.Add( eventPath ) )
				Log.Warning( $"[Audio] '{eventPath}' did not play (no handle). Check the .sound exists and its clips compiled." );
			return;
		}

		if ( DebugLog && _seen.Add( eventPath ) )
		{
			var listener = Sound.Listener.Position;
			Log.Info( $"[Audio] '{eventPath}' playing at {position:0} (listener {listener:0}, {listener.Distance( position ):0} u away, volume {handle.Volume:0.00}, master {Sound.MasterVolume:0.00})" );
		}
	}
}
