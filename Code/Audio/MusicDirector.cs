using System;
using System.Linq;
using Sandbox;
using VoidMage.Boss;
using VoidMage.Run;

namespace VoidMage.Audio;

/// <summary>
/// The music, local to each machine, through the engine's own Game.Music (one track at a time, crossfades, the
/// "Music" mixer, so the s&amp;box music slider in settings rules it). The main menu's songs go through the same player
/// and own it while the menu is up; this takes over once there is a run.
///  - a floor: one of the dungeon tracks at random (never the one just heard), a short silence with only the
///    ambience when it runs out, then another. Every new floor starts a fresh pick (user, 2026-09-23: random per
///    floor rather than one track per biome). The exit menu keeps it going; the descent card and the way home fade it;
///  - the boss: silence in the arena until the Colossus stirs, then its own track, looped, faded out as it dies;
///  - the village: nothing.
/// Levels: the files come mastered up to 12 dB apart (ffmpeg ebur128, 2026-09-23: darkness -20.3 LUFS, the boss
/// track -8.1), so each carries the gain that brings it to -21 LUFS, and the two convars say how far under the
/// effects each kind sits from there (music_dungeon 0.6 is about -25 LUFS, music_boss 1 is the -21). Both apply live.
/// </summary>
public sealed class MusicDirector : GameObjectSystem
{
	[ConVar( "music_enabled" )] public static bool Enabled { get; set; } = true;
	/// <summary>Floor music level, on top of the per-track equalising gain (1 = -21 LUFS, 0.5 = 6 dB under that).</summary>
	[ConVar( "music_dungeon" )] public static float DungeonLevel { get; set; } = 0.6f;
	/// <summary>Boss music level, same scale.</summary>
	[ConVar( "music_boss" )] public static float BossLevel { get; set; } = 1f;
	/// <summary>Seconds of silence (x0.5 .. x1.5 at random) between two floor tracks; 0 chains them.</summary>
	[ConVar( "music_gap" )] public static float GapSeconds { get; set; } = 25f;
	[ConVar( "music_debug" )] public static bool DebugLogging { get; set; } = false;

	private readonly record struct Track( string Path, float Gain );

	// Pixabay, all licensed for commercial use without attribution: Welborn Works (darkness), Dream Protocol
	// (building tension, desert of the real), everything_is_dead (dark scary ambient, dark drone ambient), drmseq
	// (bone goliath); the boss track is Epic 6 by senormusica81.
	private static readonly Track[] DungeonTracks =
	{
		new( "sounds/music/dungeon_darkness.vsnd", 0.92f ),
		new( "sounds/music/dungeon_building_tension.vsnd", 0.37f ),
		new( "sounds/music/dungeon_desert_of_the_real.vsnd", 0.72f ),
		new( "sounds/music/dungeon_dark_scary_ambient.vsnd", 0.50f ),
		new( "sounds/music/dungeon_dark_drone_ambient.vsnd", 0.45f ),
		new( "sounds/music/dungeon_bone_goliath.vsnd", 0.44f ),
	};
	private static readonly Track BossTrack = new( "sounds/music/boss_epic.vsnd", 0.23f );

	private const float DungeonFadeIn = 3f;
	private const float BossFadeIn = 1f;
	private const float BossDeathFade = 4f;

	private enum Want { None, Dungeon, Boss }

	private Want _playing;
	private Want _levelOf = Want.Dungeon;
	private int _last = -1;
	private float _nextStart;      // the earliest a floor track may (re)start: the gap, or the retry hold after a Play
	// A track that dies as soon as it starts did not load, decode or stream on this machine just then (the engine
	// warns "stopped right after starting"; the menu's player was seen skipping forever on a stream, 2026-09-26,
	// while the floor music worked, so most likely the async sound streaming starving on long frames). A track alive
	// for HealthySeconds is real and its end is a normal end (then the gap); a track dead at once is followed by
	// another straight away, and once the whole pool has died in a row the director rests RestSeconds before trying
	// the pool again. A new floor starts fresh.
	private const float HealthySeconds = 3f;
	private const float RestSeconds = 60f;
	private float _playedAt;
	private bool _pending;         // a Play went out and has not yet proven itself
	private bool _healthy;         // the current track proved itself: its end is a normal end
	private int _deadInARow;
	private bool _gaveUp;
	private float _nextBossScan;
	private BossBrain _brain;

	public MusicDirector( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 11, Tick, "MusicDirector" );
	}

	private void Tick()
	{
		if ( !Scene.IsValid() || Scene.IsEditor ) return;
		var run = RunManager.Instance;
		var state = run.IsValid() ? run.State : RunState.Village;
		// the menu plays its own songs through the same player and stops them itself when Play is pressed
		if ( state == RunState.MainMenu ) { _playing = Want.None; return; }

		var want = Want.None;
		if ( Enabled )
		{
			// the Colossus wherever it stands: the run's arena, or the boss test scene, whose stage is adopted without
			// the run ever reaching RunState.Boss (that is where the silent boss fight of 2026-09-23 came from)
			if ( BossAwake() ) want = Want.Boss;
			else if ( state is RunState.InFloor or RunState.ExitMenu ) want = Want.Dungeon;
		}

		// the level knobs are live: the game-side volume sits on top of the track's own equalising gain, and a track
		// fading out keeps the level of its kind (the boss track dies at boss level, not at the floor's)
		if ( want != Want.None ) _levelOf = want;
		Game.Music.Volume = Math.Clamp( _levelOf == Want.Boss ? BossLevel : DungeonLevel, 0f, 1f );

		if ( want != _playing )
		{
			_pending = false;
			_healthy = false;
			_deadInARow = 0;
			_gaveUp = false;
			switch ( want )
			{
				case Want.None:
					// the Colossus dying: a long fade under its death scene; a descent or the way home: quicker, under the card
					var fade = _playing == Want.Boss ? BossDeathFade : state == RunState.Descending ? 1.5f : 2.5f;
					Game.Music.Stop( fade );
					if ( DebugLogging ) Log.Info( $"[Music] stop over {fade:0.0} s ({state})" );
					break;
				case Want.Dungeon:
					PlayRandomFloorTrack();
					break;
				case Want.Boss:
					Game.Music.Play( BossTrack.Path, BossFadeIn, loop: true, BossTrack.Gain );
					if ( DebugLogging ) Log.Info( $"[Music] boss: {BossTrack.Path} x{BossTrack.Gain:0.00}" );
					break;
			}
			_playing = want;
			return;
		}

		if ( want != Want.Dungeon ) return;

		if ( Game.Music.IsPlaying )
		{
			if ( _pending && Time.Now - _playedAt >= HealthySeconds ) { _pending = false; _healthy = true; _deadInARow = 0; }
			return;
		}
		// a floor track ran out: a while of just the drips and the torches, then the next one
		if ( _healthy )
		{
			_healthy = false;
			_nextStart = Time.Now + MathF.Max( GapSeconds, 0f ) * Game.Random.Float( 0.5f, 1.5f );
			if ( DebugLogging ) Log.Info( $"[Music] track over, next in {_nextStart - Time.Now:0} s" );
			return;
		}
		if ( Time.Now < _nextStart ) return;
		if ( _gaveUp ) { _gaveUp = false; _deadInARow = 0; }   // the rest is over: the pool again
		// a track that died as soon as it started: another, until the whole pool has died in a row
		if ( _pending && ++_deadInARow >= DungeonTracks.Length )
		{
			_pending = false;
			_gaveUp = true;
			_nextStart = Time.Now + RestSeconds;
			Log.Warning( $"[Music] every floor track stopped as soon as it started; the music rests {RestSeconds:0} s" );
			return;
		}
		PlayRandomFloorTrack();
	}

	private void PlayRandomFloorTrack()
	{
		if ( DungeonTracks.Length == 0 ) return;
		var i = Game.Random.Int( 0, DungeonTracks.Length - 1 );
		if ( DungeonTracks.Length > 1 && i == _last ) i = (i + 1 + Game.Random.Int( 0, DungeonTracks.Length - 2 )) % DungeonTracks.Length;
		_last = i;
		var track = DungeonTracks[i];
		Game.Music.Play( track.Path, DungeonFadeIn, loop: false, track.Gain );
		_playedAt = Time.Now;
		_pending = true;
		_healthy = false;
		_nextStart = Time.Now + 3f;   // a missing clip only warns; do not ask again every frame
		if ( DebugLogging ) Log.Info( $"[Music] floor: {track.Path} x{track.Gain:0.00}" );
	}

	/// <summary>A Colossus that has stirred and is not dead yet. Looked for twice a second while there is none (or the one we had is dead: the test scene can raise another).</summary>
	private bool BossAwake()
	{
		if ( (!_brain.IsValid() || _brain.Phase == BossPhase.Dead) && Time.Now >= _nextBossScan )
		{
			_nextBossScan = Time.Now + 0.5f;
			_brain = Scene.GetAllComponents<BossBrain>().FirstOrDefault( b => b.IsValid() && b.Phase != BossPhase.Dead ) ?? _brain;
		}
		return _brain.IsValid() && _brain.Phase is BossPhase.Waking or BossPhase.Idle;
	}
}
