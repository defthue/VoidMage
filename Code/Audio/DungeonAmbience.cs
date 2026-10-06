using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Dungeon;
using VoidMage.Npc;

namespace VoidMage.Audio;

/// <summary>
/// The dungeon's sound bed, local to each player's machine (nothing here is networked):
///  - crackling fire on the nearest few torches and braziers (a long clip, each torch at its own slightly different
///    pitch so no two stay in phase, restarted when it runs out, faded out when the torch drops out of the nearest
///    set). NEVER seek into it: a SoundHandle.Time write is applied on the MIXING thread (SoundHandle.SampleWithSeek),
///    and seeking a five-minute mp3 decodes it from the start up to the offset, which blocked the whole mix for as
///    long as that took. That was the "all sound stutters for half a second, worse when running around" of the
///    reviews (2026-09-26): every torch that entered the nearest set while moving cost one stall;
///  - random unease, the way Minecraft's cave sounds work: drips fairly often and close, knocks now and then,
///    the horror stings rarely, all at unpredictable intervals and from a random point around the player, the
///    knocks and stings preferring somewhere BEHIND you. Stings and knocks hold off while a skeleton is on you.
/// </summary>
public sealed class DungeonAmbience : GameObjectSystem
{
	[ConVar( "ambience_enabled" )] public static bool Enabled { get; set; } = true;
	[ConVar( "ambience_debug" )] public static bool DebugLogging { get; set; } = false;

	public const string FireSound = "sounds/cracklingfire.sound";
	public const string DripSound = "sounds/ambiencedrip.sound";
	public const string KnockSound = "sounds/ambienceknock.sound";
	public const string HorrorSound = "sounds/ambiencehorror.sound";

	/// <summary>Torches that crackle at once (the nearest), and how far a torch can be to get a voice.</summary>
	public static int MaxFires { get; set; } = 3;
	public static float FireRange { get; set; } = 700f;
	/// <summary>How far apart in pitch two torches can sit (x0.96 .. x1.04): enough to drift out of phase within seconds, too little to hear on a crackle.</summary>
	public static float FirePitchSpread { get; set; } = 0.04f;

	private sealed class Cue
	{
		public string Sound;
		public float MinWait, MaxWait;        // seconds between plays
		public float MinDistance, MaxDistance; // from the listener
		public float MinVolume, MaxVolume;
		public bool Behind;                    // prefer the half-space behind the player
		public bool NotInCombat;               // hold while a skeleton is engaged nearby
		public float RepeatChance;             // a second take shortly after (a knock that knocks twice)
		public float NextAt;
	}

	private readonly Cue[] _cues =
	{
		new() { Sound = DripSound, MinWait = 6f, MaxWait = 22f, MinDistance = 120f, MaxDistance = 420f, MinVolume = 0.5f, MaxVolume = 1f, RepeatChance = 0.25f },
		new() { Sound = KnockSound, MinWait = 45f, MaxWait = 140f, MinDistance = 300f, MaxDistance = 750f, MinVolume = 0.6f, MaxVolume = 1f, Behind = true, NotInCombat = true, RepeatChance = 0.3f },
		new() { Sound = HorrorSound, MinWait = 100f, MaxWait = 300f, MinDistance = 250f, MaxDistance = 650f, MinVolume = 0.7f, MaxVolume = 1f, Behind = true, NotInCombat = true },
	};

	private readonly Dictionary<FireLight, SoundHandle> _fires = new();
	private readonly List<(FireLight fire, float dist)> _nearest = new();
	private float _nextFireScan, _nextFireReport;
	private bool _seeded;
	private (string sound, Vector3 at, float volume, float at2)? _repeat;

	public DungeonAmbience( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 10, Tick, "DungeonAmbience" );
	}

	private void Tick()
	{
		if ( !Enabled || !Scene.IsValid() || Scene.IsEditor ) { StopFires(); return; }
		// the drips and the knocks are the dungeon's; in the village (the run loop's home state) they stay quiet
		if ( !VoidMage.Run.RunManager.InDungeon ) { StopFires(); return; }
		var player = PlayerHealth.Local;
		if ( !player.IsValid() ) { StopFires(); return; }
		var listener = player.WorldPosition;
		var forward = Scene.Camera.IsValid() ? Scene.Camera.WorldRotation.Forward.WithZ( 0f ).Normal : Vector3.Forward;

		if ( !_seeded )
		{
			// the first stings wait a while: silence first, then the dungeon starts talking
			_seeded = true;
			foreach ( var c in _cues ) c.NextAt = Time.Now + Game.Random.Float( c.MinWait * 0.5f, c.MaxWait * 0.5f ) + (c.NotInCombat ? 30f : 0f);
		}

		TickFires( listener );
		TickCues( listener, forward );
	}

	// ------------------------------------------------------------------ torches

	private void TickFires( Vector3 listener )
	{
		if ( Time.Now < _nextFireScan ) return;
		_nextFireScan = Time.Now + 0.5f;

		_nearest.Clear();
		var total = 0; var kinds = 0;
		foreach ( var fire in Scene.GetAllComponents<FireLight>() )
		{
			total++;
			if ( !fire.IsValid() || fire.Kind is not (LightKind.Torch or LightKind.Brazier or LightKind.Chandelier) ) continue;
			kinds++;
			var d = fire.WorldPosition.Distance( listener );
			if ( d > FireRange ) continue;
			_nearest.Add( (fire, d) );
		}
		_nearest.Sort( ( a, b ) => a.dist.CompareTo( b.dist ) );
		if ( _nearest.Count > MaxFires ) _nearest.RemoveRange( MaxFires, _nearest.Count - MaxFires );
		if ( DebugLogging && Time.Now > _nextFireReport )
		{
			_nextFireReport = Time.Now + 5f;
			Log.Info( $"[Ambience] fires: {total} FireLights, {kinds} torch/brazier/chandelier, {_nearest.Count} within {FireRange:0} u, {_fires.Count} voices" );
		}

		// voices for the newcomers; a voice is only dropped once its torch is well out of range (hysteresis: churning
		// the nearest-N set on every step made torches cut out and come back), restarts for clips that ran out
		List<FireLight> gone = null;
		foreach ( var (fire, handle) in _fires )
		{
			var keep = fire.IsValid() && fire.WorldPosition.Distance( listener ) < FireRange * 1.4f;
			if ( !keep ) { handle?.Stop( 1.5f ); (gone ??= new()).Add( fire ); }
		}
		if ( gone is not null ) foreach ( var f in gone ) _fires.Remove( f );

		foreach ( var (fire, _) in _nearest )
		{
			if ( _fires.TryGetValue( fire, out var handle ) && handle.IsValid() && handle.IsPlaying ) continue;
			var h = GameAudio.PlayLoop( FireSound, fire.WorldPosition );
			if ( h is null || !h.IsValid() ) continue;
			// no h.Time here (see the summary): the phase difference comes from the pitch instead
			h.Pitch *= 1f + Game.Random.Float( -FirePitchSpread, FirePitchSpread );
			h.Volume *= fire.Kind == LightKind.Brazier ? 1.3f : 1f;
			_fires[fire] = h;
			if ( DebugLogging ) Log.Info( $"[Ambience] fire voice on {fire.GameObject.Name} at {fire.WorldPosition:0}" );
		}
	}

	private void StopFires()
	{
		if ( _fires.Count == 0 ) return;
		foreach ( var (_, handle) in _fires ) handle?.Stop( 0.3f );
		_fires.Clear();
	}

	// ------------------------------------------------------------------ the unease

	private void TickCues( Vector3 listener, Vector3 forward )
	{
		if ( _repeat is { } r && Time.Now >= r.at2 )
		{
			_repeat = null;
			Play( r.sound, r.at, r.volume );
		}

		foreach ( var c in _cues )
		{
			if ( Time.Now < c.NextAt ) continue;
			if ( c.NotInCombat && InCombat( listener ) ) { c.NextAt = Time.Now + Game.Random.Float( 8f, 20f ); continue; }
			c.NextAt = Time.Now + Game.Random.Float( c.MinWait, c.MaxWait );

			// a random direction, biased behind the player for the ones meant to make you turn around
			var yaw = Game.Random.Float( 0f, 360f );
			if ( c.Behind && Game.Random.Float( 0f, 1f ) < 0.75f )
				yaw = (-forward).EulerAngles.yaw + Game.Random.Float( -70f, 70f );
			var dir = Rotation.FromYaw( yaw ).Forward;
			var at = listener + dir * Game.Random.Float( c.MinDistance, c.MaxDistance ) + Vector3.Up * Game.Random.Float( 20f, 90f );
			var volume = Game.Random.Float( c.MinVolume, c.MaxVolume );
			Play( c.Sound, at, volume );
			if ( c.RepeatChance > 0f && Game.Random.Float( 0f, 1f ) < c.RepeatChance )
				_repeat = (c.Sound, at + dir * Game.Random.Float( -40f, 40f ), volume * 0.8f, Time.Now + Game.Random.Float( 1.2f, 3.5f ));
		}
	}

	private void Play( string sound, Vector3 at, float volume )
	{
		var h = GameAudio.Play( sound, at );
		if ( h is null || !h.IsValid() ) return;
		h.Volume *= volume;
		h.Pitch *= Game.Random.Float( 0.94f, 1.06f );
		if ( DebugLogging ) Log.Info( $"[Ambience] {sound} at {at:0} volume {volume:0.00}" );
	}

	/// <summary>A skeleton is on the player: no stings, they would step on the fight's own sound.</summary>
	private bool InCombat( Vector3 listener )
	{
		foreach ( var npc in Scene.GetAllComponents<SkeletonNpc>() )
		{
			if ( !npc.IsValid() || npc.IsDead || npc.Dormant ) continue;
			// a critter is never a fight, whatever it is doing: a frog hopping past would otherwise silence
			// the dungeon's ambience for as long as it was near you
			if ( npc.Passive || npc.AtRest ) continue;
			if ( npc.WorldPosition.Distance( listener ) < 600f ) return true;
		}
		return false;
	}
}
