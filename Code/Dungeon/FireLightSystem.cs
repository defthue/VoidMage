using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Dungeon;

/// <summary>
/// Drives every FireLight in the scene from one place each frame (the heden FireLightSystem model)
/// and parks a sprite flame rig (FireVfxRig) on the nearest ones. A cached rescan every couple of
/// seconds instead of per-frame lookups. Runs in the editor as well as in play: the dungeon's lights
/// and rigs are never saved, so a flickered colour can't leak into the scene file.
///
/// The rig is a child of the PROP (the light's parent), not of the light: the flame stays anchored
/// on the torch and bends toward the fire's wander on its own.
///
/// In play the rigs are distance-gated to the fog: the retro fog is black at 1000 units, so a rig
/// spawns at 1400 (already burning when the torch resolves out of the dark) and is kept to 1750 so
/// the boundary never strobes; one spawn per frame, nearest first. In the editor every fire gets a
/// rig so placement can be tuned.
///
/// The lights themselves are gated too, whenever there is a camera to gate against. A floor carries
/// up to ~100 point lights and the engine does not cull shadow casters by distance: every shadowed
/// light in the frustum renders six shadow views (r.shadows.max is 256, sorted by screen size).
/// So: lights well past the fog are disabled outright (no scene object at all), and of the ones
/// awake only a small budget casts shadows, chosen by apparent size with a bonus for being in view
/// and a stickiness bonus for already holding a slot, so slots don't flip-flop as you walk.
/// </summary>
public sealed class FireLightSystem : GameObjectSystem
{
	private const float SpawnRadius = 1400f;
	private const float DespawnRadius = 1750f;
	private const int MaxRigs = 48;
	/// <summary>A bare fire takes a full cap's farthest rig only when it is this much nearer (1.3x distance, squared).</summary>
	private const float RigStealRatioSq = 1.69f;
	private const int MaxSpawnsPerFrameEditor = 8;

	/// <summary>A sleeping light wakes inside this distance from the eye (the fog is black at 1000).</summary>
	private const float LightWakeRadius = 1500f;
	/// <summary>An awake light sleeps again past this distance; the gap keeps the boundary from strobing.</summary>
	private const float LightSleepRadius = 1850f;
	/// <summary>Only lights inside this distance compete for a shadow slot; further ones are deep in the fog.</summary>
	private const float ShadowRadius = 1150f;
	/// <summary>A light already casting shadows keeps its slot unless a challenger beats its score by this factor.</summary>
	private const float ShadowStickiness = 1.3f;
	/// <summary>Score multiplier for a light whose sphere touches the view frustum.</summary>
	private const float InViewBonus = 3f;
	/// <summary>
	/// Inside this distance a light gets the in-view bonus whether or not the camera looks at it: its shadows are
	/// already cast before you turn to it, so they never pop in as you spin or run through a room.
	/// </summary>
	private const float NearAlwaysRadius = 1000f;

	/// <summary>
	/// How many fire lights may cast shadows at once. Each one costs six shadow views of the dynamic
	/// objects in its radius every frame (static geometry is cached per light). The engine itself
	/// never delays a cube shadow (no screen-size cull, no per-frame update cap, hard ceiling 256),
	/// so any shadow that pops in nearby is a light that had to wait for one of these slots.
	/// 16 ran out in dense rooms (every light inside NearAlwaysRadius competes); 32 covers the near
	/// ring, the room's candles and the lit doorway ahead. Live: firelight_shadow_max.
	/// </summary>
	[ConVar( "firelight_shadow_max" )] public static int MaxShadowedLights { get; set; } = 32;

	/// <summary>Editor-only switch: flames in the viewport, or the editor's frames back.</summary>
	public static bool EditorPreview { get; set; } = true;

	private readonly List<FireLight> _fires = new();
	private readonly HashSet<FireLight> _known = new();
	private readonly List<(FireLight fire, float score)> _shadowCandidates = new();
	private float _nextScan = float.NegativeInfinity;
	private bool _wasInDungeon = true;
	private int _reportedRigs = -1;
	private int _reportedAwake = -1;
	private bool _reportedStarved;

	public FireLightSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 0, Tick, "FireLights" );
	}

	private void Tick()
	{
		float t = Time.Now;
		float dt = Time.Delta;
		// the village has no fires: while the list is empty the only work is the (slower) rescan, and a
		// village/dungeon flip forces one at once so a fresh floor's torches never wait for the timer
		var inDungeon = VoidMage.Run.RunManager.InDungeon;
		if ( inDungeon != _wasInDungeon ) { _wasInDungeon = inDungeon; _nextScan = float.NegativeInfinity; }
		// a scene reload (the director's trailer intro) keeps this system and its list of the OLD scene's fires: the
		// village's 10 s timer left the campfire bare for the whole intro. A dead entry means the list is stale: rescan now.
		if ( t < _nextScan && _fires.Count > 0 && !_fires[0].IsValid() ) _nextScan = float.NegativeInfinity;
		if ( t >= _nextScan ) Rescan( t );
		if ( _fires.Count == 0 ) return;

		// the gate is for play: in the editor Scene.Camera is the scene's game camera, not the viewport, so gating
		// there switched off (and saved switched off) every light placed far from wherever that camera sits
		var camera = Scene.Camera;
		if ( Scene.IsEditor ) EditorLights();
		else if ( camera.IsValid() ) GateLights( camera );

		foreach ( var f in _fires )
		{
			if ( !f.IsValid() ) continue;
			f.Apply( t );
		}

		if ( Scene.IsEditor ) EditorTick( t, dt );
		else PlayTick( t, dt );
	}

	private static GameObject RigParent( FireLight f ) => f.GameObject.Parent.IsValid() ? f.GameObject.Parent : f.GameObject;

	private void Rescan( float t )
	{
		_nextScan = t + (VoidMage.Run.RunManager.InDungeon ? 2f : 10f);
		_fires.Clear();
		int rigs = 0;
		foreach ( var fire in Scene.GetAllComponents<FireLight>() )
		{
			// Spell fire (fireball, charge orb) drives its own FireLight and rig; it is not a dungeon light.
			if ( fire.GameObject.Tags.Has( "spellfire" ) ) continue;
			_fires.Add( fire );
			if ( _known.Add( fire ) )
			{
				// a hotload rebuilds this system while the old incarnation's rigs still stand: sweep them
				foreach ( var child in RigParent( fire ).Children.ToArray() )
					if ( child.Name == FireVfxRig.RigName ) child.Destroy();
				fire.Rig = null;
			}
			if ( fire.Rig != null && fire.Rig.Root.IsValid() ) rigs++;
		}
		_known.RemoveWhere( f => !f.IsValid() );
		if ( rigs != _reportedRigs )
		{
			_reportedRigs = rigs;
			Log.Info( $"[FireLight] driving {_fires.Count} lights, {rigs} rigs (editor: {Scene.IsEditor})" );
		}
	}

	/// <summary>
	/// The editor: every fire's light is awake, and its shadows are simply what FireLight.CastShadows says (Auto
	/// counts as on; there is no budget to fight over in a hand-built scene). The PointLight's own Shadows
	/// checkbox works too: this system rewrites it every frame, so a tick there used to vanish at once; now a
	/// change that did not come from here is taken as the user's and adopted into CastShadows.
	/// </summary>
	private void EditorLights()
	{
		foreach ( var f in _fires )
		{
			if ( !f.IsValid() ) continue;
			var light = f.Light;
			if ( light == null ) continue;
			if ( !light.Enabled ) light.Enabled = true;   // heals lights the old editor gate saved as off
			if ( f.ShadowsWritten.HasValue && light.Shadows != f.ShadowsWritten.Value )
				f.CastShadows = light.Shadows ? FireShadowMode.On : FireShadowMode.Off;
			bool want = f.CastShadows != FireShadowMode.Off;
			if ( light.Shadows != want ) light.Shadows = want;
			f.ShadowsWritten = want;
		}
	}

	/// <summary>
	/// Sleep the lights past the fog, wake the ones coming into range, and hand the shadow budget to
	/// the few that matter most: biggest on screen first, in-view before out-of-view, incumbents
	/// favoured. Cheap: one distance and one frustum test per fire.
	/// </summary>
	private void GateLights( CameraComponent camera )
	{
		var eye = camera.WorldPosition;
		var frustum = camera.GetFrustum();
		_shadowCandidates.Clear();
		int awake = 0;

		foreach ( var f in _fires )
		{
			if ( !f.IsValid() ) continue;
			var light = f.Light;
			if ( light == null ) continue;

			var pos = f.WorldPosition;
			float dist = pos.Distance( eye );
			if ( light.Enabled )
			{
				if ( dist > LightSleepRadius ) { light.Enabled = false; continue; }
			}
			else
			{
				if ( dist > LightWakeRadius ) continue;
				light.Enabled = true;
			}
			awake++;

			if ( dist > ShadowRadius || f.CastShadows == FireShadowMode.Off )
			{
				if ( light.Shadows ) light.Shadows = false;
				continue;
			}

			float radius = f.BaseRadius > 0f ? f.BaseRadius : light.Radius;
			float score = radius / MathF.Max( dist, 1f );   // apparent size
			if ( dist < NearAlwaysRadius || frustum.IsInside( pos, radius, true ) ) score *= InViewBonus;
			if ( light.Shadows ) score *= ShadowStickiness;
			if ( f.CastShadows == FireShadowMode.On ) score += 1e6f;   // hand-picked: ahead of every Auto light
			_shadowCandidates.Add( (f, score) );
		}

		_shadowCandidates.Sort( ( a, b ) => b.score.CompareTo( a.score ) );
		for ( int i = 0; i < _shadowCandidates.Count; i++ )
		{
			var light = _shadowCandidates[i].fire.Light;
			bool want = i < MaxShadowedLights;
			if ( light.Shadows != want ) light.Shadows = want;
		}

		// A starved slot is a shadow that will pop in later: say so once per change, not per frame.
		int starved = Math.Max( 0, _shadowCandidates.Count - MaxShadowedLights );
		// only the starvation flips are worth a line; the awake count changes with every step
		if ( (starved > 0) != _reportedStarved )
		{
			_reportedAwake = awake;
			_reportedStarved = starved > 0;
			Log.Info( $"[FireLight] {awake} lights awake, {Math.Min( _shadowCandidates.Count, MaxShadowedLights )} shadowed, {starved} waiting for a shadow slot (firelight_shadow_max {MaxShadowedLights})" );
		}
	}

	private void PlayTick( float t, float dt )
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		var eye = camera.WorldPosition;
		int rigged = 0;
		FireLight nearest = null;
		float nearestSq = SpawnRadius * SpawnRadius;
		FireLight farthest = null;
		float farthestSq = 0f;
		foreach ( var f in _fires )
		{
			if ( !f.IsValid() ) continue;
			float distSq = f.WorldPosition.DistanceSquared( eye );
			if ( f.Rig != null )
			{
				if ( !f.Rig.Root.IsValid() ) { f.Rig = null; }
				else if ( distSq > DespawnRadius * DespawnRadius ) { Destroy( f ); continue; }
				else if ( f.Rig.BuildKey != f.BuildKey ) { Destroy( f ); }   // settings changed at runtime: respawns below
				else
				{
					rigged++;
					FireVfxRig.Drive( f.Rig, f, t, dt );
					if ( distSq > farthestSq ) { farthestSq = distSq; farthest = f; }
					continue;
				}
			}
			if ( distSq < nearestSq ) { nearestSq = distSq; nearest = f; }
		}
		// one spawn per frame: a dense room fills the cap over a few frames instead of hitching
		if ( nearest == null ) return;
		if ( rigged < MaxRigs ) Spawn( nearest );
		else if ( farthest != null && nearestSq * RigStealRatioSq < farthestSq )
		{
			// the cap is full of flames left behind: the farthest gives its rig to the nearest bare torch, so a lit torch
			// next to the player never burns without a flame (the ratio keeps two torches at similar range from trading)
			Destroy( farthest );
			Spawn( nearest );
		}
	}

	private void EditorTick( float t, float dt )
	{
		if ( !EditorPreview )
		{
			foreach ( var f in _fires ) if ( f.IsValid() && f.Rig != null ) Destroy( f );
			return;
		}
		int spawns = 0;
		foreach ( var f in _fires )
		{
			if ( !f.IsValid() ) continue;
			// a flame setting was edited in the inspector: rebuild so the change shows at once
			if ( f.Rig != null && f.Rig.Root.IsValid() && f.Rig.BuildKey != f.BuildKey ) Destroy( f );
			if ( f.Rig != null && f.Rig.Root.IsValid() )
			{
				// hand-placed fires (prefabs): the rig follows the light object and its FlameOffset as they are edited
				f.Rig.Root.LocalPosition = FlameBase( f );
				FireVfxRig.Drive( f.Rig, f, t, dt );
				continue;
			}
			f.Rig = null;
			if ( spawns < MaxSpawnsPerFrameEditor ) { Spawn( f ); spawns++; }
		}
	}

	/// <summary>The flame base in the rig parent's frame: where the light object sits, plus the offset onto the prop.</summary>
	private static Vector3 FlameBase( FireLight f )
		=> RigParent( f ) == f.GameObject ? f.FlameOffset : f.LocalPosition + f.FlameOffset;

	private static void Spawn( FireLight f )
	{
		var parent = RigParent( f );
		f.Rig = FireVfxRig.Build( parent, f, FlameBase( f ) );
		if ( f.FlameCard.IsValid() && f.FlameCard.SceneObject != null )
			f.FlameCard.SceneObject.Attributes.Set( "RuneTint", new Vector3( 1f, 0.72f, 0.32f ) );
	}

	private static void Destroy( FireLight f )
	{
		if ( f.Rig?.Root.IsValid() == true ) f.Rig.Root.Destroy();
		f.Rig = null;
	}
}
