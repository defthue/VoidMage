using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Village;

/// <summary>
/// Every frog the player has ever brought home, hopping about the village. The collection is meant to grow without
/// limit, so a frog here is NOT a GameObject: the whole population is one array of small structs, drawn in a single
/// instanced draw call (Graphics.DrawModelInstanced) from one SceneCustomObject. A thousand frogs therefore cost one
/// draw call and one pass over an array, not a thousand prefabs with a skinned renderer, an animgraph, a navmesh
/// agent and a capsule each.
///
/// What the player asked for, and where it lands here:
/// - frogs come out of the bag where the player arrives home and then wander (<see cref="Release"/>)
/// - only the ones near the player hop: everything inside <see cref="HopRange"/> is a candidate and the closest
///   <see cref="HopMax"/> of those actually hop, the rest sit still until the player comes over
/// - they can still be killed at home, but never picked up again: the closest few get a real body (<see cref="BodyMax"/>
///   of them, inside <see cref="BodyRange"/>) and no catch component is ever put on them. That body is both the
///   collider a sword or a spell can find and a skinned renderer on the frog's own animgraph, because an instanced
///   draw carries no skinning — so the ones you can see hop properly and everything beyond them stays free.
/// - a frog is tagged `critter`, which ProjectSettings/Collision.config pairs with `player` as Ignore: you can cut
///   one down, you cannot shove it about or be blocked by it
/// - the population is saved (RunSaveData.Frogs), so a kill at home costs the player a frog for good
///
/// Positions are not saved, only the count: a new session scatters them around the village spawn and they wander
/// from there.
/// </summary>
[Title( "Village Frogs" ), Category( "Void Mage" ), Icon( "pets" )]
public sealed class VillageFrogs : Component
{
	public const string FrogModel = "characters/monsters/frog/frog.vmdl";
	/// <summary>The graph the close, animated frogs run (the same one monster_frog.prefab carries).</summary>
	public const string FrogAnimGraph = "animgraphs/monster_frog.vanmgrph";

	/// <summary>How far from the player a frog still bothers to hop (units). The user's number.</summary>
	[ConVar( "village_frog_range" )] public static float HopRange { get; set; } = 2000f;
	/// <summary>How many frogs may hop at once: the closest ones inside the range win, everything else sits.</summary>
	[ConVar( "village_frog_hop_max" )] public static int HopMax { get; set; } = 60;
	/// <summary>How far out frogs are still drawn (units); past it they are skipped entirely.</summary>
	[ConVar( "village_frog_draw_range" )] public static float DrawRange { get; set; } = 6000f;
	/// <summary>
	/// How close a frog has to be before it gets a real body: the collider that can be hit AND the skinned renderer
	/// that plays the jump clip. Raised from 400 when the bodies gained their animation, because a bind-pose frog
	/// sliding along its arc is obvious well past that.
	/// </summary>
	[ConVar( "village_frog_body_range" )] public static float BodyRange { get; set; } = 900f;
	/// <summary>How many hittable bodies exist at once (pooled, handed to the closest frogs).</summary>
	[ConVar( "village_frog_body_max" )] public static int BodyMax { get; set; } = 24;
	/// <summary>Ground a frog covers in one hop (units), as in the dungeon.</summary>
	[ConVar( "village_frog_hop_distance" )] public static float HopDistance { get; set; } = 65f;
	/// <summary>
	/// How long one hop commits the frog (seconds): the WHOLE jump clip at playback 1.0, crouch and settle included,
	/// which is 0.875 s (tools/out/monsters/frog_clips.json). The body only travels inside the clip's measured airborne
	/// window (<see cref="HopLiftTime"/>..<see cref="HopLandTime"/>), exactly as the dungeon frog does
	/// (SkeletonNpc.TickHopTravel): it used to start moving on frame 0, sliding through the crouch before the legs
	/// had pushed (user, 2026-09-19). Change this and the clip no longer lines up, so leave it.
	/// </summary>
	[ConVar( "village_frog_hop_seconds" )] public static float HopSeconds { get; set; } = 0.875f;
	/// <summary>Seconds into the clip that the feet leave and meet the ground (`air_start` / `air_end` in the measurement).</summary>
	public const float HopLiftTime = 0.375f;
	public const float HopLandTime = 0.625f;
	/// <summary>How high the arc goes (units) over the airborne window.</summary>
	[ConVar( "village_frog_hop_lift" )] public static float HopLift { get; set; } = 9f;
	/// <summary>Stillness between hops (seconds).</summary>
	[ConVar( "village_frog_rest_min" )] public static float RestMin { get; set; } = 0.8f;
	[ConVar( "village_frog_rest_max" )] public static float RestMax { get; set; } = 4f;
	/// <summary>How far from the release point the first scatter spreads a frog (units); bigger crowds spread wider.</summary>
	[ConVar( "village_frog_scatter" )] public static float Scatter { get; set; } = 260f;
	/// <summary>Print what the swarm is doing once a second.</summary>
	[ConVar( "village_frog_debug" )] public static bool Debug { get; set; } = false;

	/// <summary>The village's swarm (there is only ever one).</summary>
	public static VillageFrogs Instance { get; private set; }

	private struct Frog
	{
		public Vector3 Rest;        // where it sits on the ground
		public Vector3 From, To;    // the hop in progress
		public float HopStart;      // Time.Now when the hop began (negative: not hopping)
		public float NextHopAt;
		public float Yaw;
		public int HopId;           // ++ on every hop: what the animated body watches to fire its jump clip
		public byte Blocked;        // hops refused in a row; enough of them means it is wedged in something
	}

	/// <summary>Hops refused in a row before a frog is treated as wedged and put back with the others.</summary>
	private const int StuckHops = 6;
	/// <summary>Clear air a frog needs above its landing spot, or it is standing inside something.</summary>
	private const float Headroom = 26f;
	/// <summary>Tries at finding a spot that is not inside a wall before a frog is simply dropped where the player is.</summary>
	private const int ScatterTries = 8;

	// what the scatter and the headroom checks look through: everything that is not the village itself
	private static readonly string[] SpotIgnore = { "player", "npc", "enemy", "ally", "critter", "debris", "trigger", "ragdoll", "viewmodel", "posesource", "spellfire" };

	private readonly List<Frog> _frogs = new();
	private Model _model;
	private Vector3 _home;            // where the swarm was let out: where a wedged frog is put back
	private AnimationGraph _graph;
	private readonly HashSet<int> _animated = new();   // frogs drawn by their own renderer, not by the instanced pass
	private FrogSceneObject _so;
	private Transform[] _draw = Array.Empty<Transform>();
	private int _drawCount;
	private readonly List<(int Index, float Distance)> _near = new();
	private readonly List<FrogBody> _bodies = new();
	private GameObject _bodyRoot;
	private float _nextReport;

	/// <summary>How many frogs are in the village right now (the saved collection).</summary>
	public int Count => _frogs.Count;

	// ------------------------------------------------------------------ the swarm object

	/// <summary>
	/// The village's frog swarm, made on demand. RunManager calls this when it puts the player in the village; it
	/// also loads the saved population the first time.
	/// </summary>
	public static VillageFrogs EnsureOn( Scene scene )
	{
		if ( !scene.IsValid() ) return null;
		if ( Instance.IsValid() ) return Instance;
		var existing = scene.GetAllComponents<VillageFrogs>();
		foreach ( var f in existing ) { Instance = f; return f; }
		var go = new GameObject( true, "village_frogs" )
		{
			Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Hidden,
		};
		return Instance = go.Components.Create<VillageFrogs>();
	}

	/// <summary>
	/// Frogs carried home are let out here: they land in a scatter around `at` (the point the player arrived at) and
	/// wander off from there. Also adds them to the save, which is the collection.
	/// </summary>
	public static void Release( Scene scene, int count, Vector3 at )
	{
		if ( count <= 0 ) return;
		var swarm = EnsureOn( scene );
		if ( !swarm.IsValid() ) return;
		swarm.EnsureLoaded();
		swarm.Add( count, at );
		VoidMage.Run.RunSave.Data.Frogs = swarm.Count;
		VoidMage.Run.RunSave.Save();
		Log.Info( $"[Frog] {count} released into the village; {swarm.Count} living there now" );
	}

	private bool _loaded;

	/// <summary>The saved population, scattered around the village spawn the first time the village is up.</summary>
	public void EnsureLoaded()
	{
		if ( _loaded ) return;
		_loaded = true;
		int saved = Math.Max( 0, VoidMage.Run.RunSave.Data.Frogs );
		if ( saved <= 0 ) return;
		var home = VoidMage.Run.RunManager.Instance.IsValid() ? VoidMage.Run.RunManager.Instance.VillageSpawnPoint : WorldPosition;
		Add( saved, home );

		Log.Info( $"[Frog] {saved} frogs live in the village" );
	}

	private void Add( int count, Vector3 at )
	{
		_home = at;
		// a bigger crowd needs more room, or a thousand frogs would stack in one heap: the disc grows with the
		// square root of the number so the density stays roughly constant
		float radius = Scatter * MathF.Max( 1f, MathF.Sqrt( count / 24f ) );
		for ( int i = 0; i < count; i++ )
		{
			_frogs.Add( new Frog
			{
				Rest = FindSpot( at, radius ),
				HopStart = -1f,
				NextHopAt = Time.Now + Game.Random.Float( 0f, RestMax ),
				Yaw = Game.Random.Float( 0f, 360f ),
			} );
		}
	}

	/// <summary>
	/// A spot to put a frog on, around `at`. The old version picked a point in the disc and traced straight down,
	/// which happily posted a frog inside a house or a haystack — and once inside, every hop is refused and it sits
	/// there for good (user, 2026-09-18). Now the walk from the release point has to be clear, the landing needs
	/// clear air above it, and a spot that fails both a few times falls back to where the player is standing.
	/// </summary>
	private Vector3 FindSpot( Vector3 at, float radius )
	{
		for ( int i = 0; i < ScatterTries; i++ )
		{
			var offset = Vector3.Random.WithZ( 0f );
			offset = offset.IsNearZeroLength ? Vector3.Forward : offset.Normal;
			var want = at + offset * Game.Random.Float( 20f, radius );
			// nothing may be scattered THROUGH a wall: walk out from the release point and stop short of whatever blocks it
			var from = at + Vector3.Up * 30f;
			var tr = Scene.Trace.Ray( from, want + Vector3.Up * 30f ).Radius( 8f ).WithoutTags( SpotIgnore ).Run();
			var target = tr.Hit ? tr.EndPosition - offset * 24f : want + Vector3.Up * 30f;
			var spot = Ground( target, at );
			if ( HasHeadroom( spot ) ) return spot;
		}
		return Ground( at, at );
	}

	/// <summary>
	/// Clear air above a spot: a frog standing inside a crate, a wall or a roof has something right on top of it.
	/// The sweep starts well clear of the ground the spot sits on -- a sphere that begins overlapping the floor
	/// "hits" at once, and that refused every hop in the village (every frog turned on the spot and never moved).
	/// </summary>
	private bool HasHeadroom( Vector3 spot )
	{
		const float radius = 4f;
		var tr = Scene.Trace.Ray( spot + Vector3.Up * (radius + 6f), spot + Vector3.Up * Headroom ).Radius( radius ).WithoutTags( SpotIgnore ).Run();
		return !tr.Hit && !tr.StartedSolid;
	}

	/// <summary>The ground under a spot: traced down from well above it. Falls back to the release height.</summary>
	private Vector3 Ground( Vector3 spot, Vector3 fallbackAt )
	{
		var tr = Scene.Trace.Ray( spot + Vector3.Up * 200f, spot - Vector3.Up * 400f )
			.WithoutTags( SpotIgnore )
			.Run();
		return tr.Hit ? tr.HitPosition : fallbackAt;
	}

	// ------------------------------------------------------------------ life

	protected override void OnEnabled()
	{
		Instance = this;
		_model ??= Model.Load( FrogModel );
		_graph ??= AnimationGraph.Load( FrogAnimGraph );
		EnsureSceneObject();
	}

	protected override void OnDisabled()
	{
		_so?.Delete();
		_so = null;
		ClearBodies();
		if ( Instance == this ) Instance = null;
	}

	private void EnsureSceneObject()
	{
		if ( _so.IsValid() ) return;
		if ( !Scene.IsValid() || Scene.SceneWorld is null ) return;
		_so = new FrogSceneObject( Scene.SceneWorld, this );
		_so.Transform = global::Transform.Zero;
	}

	protected override void OnUpdate()
	{
		// the swarm only lives in the village: in a dungeon run nothing hops, nothing is hittable and nothing is drawn
		// (the draw list is emptied, or the last village frame's frogs would keep rendering out there)
		if ( VoidMage.Run.RunManager.InDungeon ) { _drawCount = 0; ClearBodies(); return; }
		EnsureLoaded();
		EnsureSceneObject();
		if ( _frogs.Count == 0 ) { _drawCount = 0; ClearBodies(); return; }

		var eye = ViewPoint;
		TickHops( eye );
		TickBodies( eye );
		BuildDrawList( eye );

		if ( Debug && Time.Now >= _nextReport )
		{
			_nextReport = Time.Now + 1f;
			Log.Info( $"[Frog] {_frogs.Count} in the village, {_drawCount} drawn, {_near.Count} near, {_bodies.Count} hittable" );
		}
	}

	/// <summary>Where the player is looking from: the scene camera, or the origin before there is one.</summary>
	private Vector3 ViewPoint => Scene.Camera.IsValid() ? Scene.Camera.WorldPosition : WorldPosition;

	// ------------------------------------------------------------------ hopping

	/// <summary>
	/// The closest frogs inside the range hop; everything else holds still. Only the shortlist is simulated, so the
	/// cost of the whole village is one distance check per frog plus real work for at most <see cref="HopMax"/>.
	/// </summary>
	private void TickHops( Vector3 eye )
	{
		_near.Clear();
		float range = MathF.Max( 0f, HopRange );
		float rangeSq = range * range;
		for ( int i = 0; i < _frogs.Count; i++ )
		{
			var frog = _frogs[i];
			// a frog in the air always finishes its hop, however far away the player wandered
			bool hopping = frog.HopStart >= 0f;
			float distSq = frog.Rest.DistanceSquared( eye );
			if ( !hopping && distSq > rangeSq ) continue;
			_near.Add( (i, distSq) );
		}
		_near.Sort( static ( a, b ) => a.Distance.CompareTo( b.Distance ) );

		int budget = Math.Max( 0, HopMax );
		float now = Time.Now;
		for ( int n = 0; n < _near.Count; n++ )
		{
			int index = _near[n].Index;
			var frog = _frogs[index];
			if ( frog.HopStart >= 0f )
			{
				float t = (now - frog.HopStart) / MathF.Max( 0.05f, HopSeconds );
				if ( t >= 1f )
				{
					frog.Rest = frog.To;
					frog.HopStart = -1f;
					frog.NextHopAt = now + Game.Random.Float( RestMin, MathF.Max( RestMin, RestMax ) );
				}
				_frogs[index] = frog;
				continue;
			}
			// past the budget it sits: the shortlist is sorted, so these are the ones furthest away
			if ( n >= budget || now < frog.NextHopAt ) continue;
			StartHop( index );
		}
	}

	/// <summary>One hop: a random direction, the landing spot traced onto the ground, and no hop at all off a ledge.</summary>
	private void StartHop( int index )
	{
		var frog = _frogs[index];
		float yaw = frog.Yaw + Game.Random.Float( -70f, 70f );
		var dir = Rotation.FromYaw( yaw ).Forward;
		var want = frog.Rest + dir * HopDistance;
		var landing = Ground( want, frog.Rest );
		// a wall, a fence or a drop: turn away instead of hopping into it
		if ( MathF.Abs( landing.z - frog.Rest.z ) > 40f || landing.Distance( frog.Rest ) > HopDistance * 1.6f || !HasHeadroom( landing ) )
		{
			frog.Yaw = yaw + 180f;
			frog.NextHopAt = Time.Now + Game.Random.Float( RestMin, RestMax );
			// every direction refused, over and over, means it is wedged inside something (it spawned in a wall, or
			// hopped into a gap it cannot leave). Put it back out with the others rather than leave it stuck forever.
			if ( ++frog.Blocked >= StuckHops )
			{
				frog.Blocked = 0;
				frog.Rest = FindSpot( _home, Scatter );
				frog.HopStart = -1f;
				if ( Debug ) Log.Info( $"[Frog] one was wedged and has been put back near {_home}" );
			}
			_frogs[index] = frog;
			return;
		}
		frog.Blocked = 0;
		frog.From = frog.Rest;
		frog.To = landing;
		frog.Yaw = yaw;
		frog.HopStart = Time.Now;
		frog.HopId++;
		_frogs[index] = frog;
	}

	/// <summary>
	/// Where a frog is drawn this frame: its rest spot, or the arc of the hop it is in the middle of. The arc only
	/// runs between the clip's lift and land times; before that the frog is crouching on its spot and after it, it
	/// is settling on the landing, so the travel and the legs agree (the dungeon frog's rule, SkeletonNpc.TickHopTravel).
	/// </summary>
	private Transform TransformOf( in Frog frog )
	{
		var rotation = Rotation.FromYaw( frog.Yaw );
		if ( frog.HopStart < 0f ) return new Transform( frog.Rest, rotation );
		float elapsed = Time.Now - frog.HopStart;
		// 0 until the feet leave, 1 from the moment they land: the fraction of the AIRBORNE window, not of the clip
		float a = Math.Clamp( (elapsed - HopLiftTime) / MathF.Max( 0.05f, HopLandTime - HopLiftTime ), 0f, 1f );
		var flat = Vector3.Lerp( frog.From, frog.To, a );
		// a plain parabola: up over the middle of the flight, back down on landing
		flat.z += HopLift * 4f * a * (1f - a);
		// nose up on the way out, down on the way in
		return new Transform( flat, rotation * Rotation.FromPitch( MathF.Sin( a * MathF.PI ) * -18f * MathF.Sign( 0.5f - a ) ) );
	}

	// ------------------------------------------------------------------ drawing

	private void BuildDrawList( Vector3 eye )
	{
		float draw = MathF.Max( 0f, DrawRange );
		float drawSq = draw * draw;
		if ( _draw.Length < _frogs.Count ) Array.Resize( ref _draw, Math.Max( 64, _frogs.Count ) );
		int count = 0;
		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );
		for ( int i = 0; i < _frogs.Count; i++ )
		{
			var frog = _frogs[i];
			if ( frog.Rest.DistanceSquared( eye ) > drawSq ) continue;
			// the close ones are drawn by their own skinned renderer (TickBodies): drawing them here as well would
			// put a bind-pose frog inside the animated one
			if ( _animated.Contains( i ) ) continue;
			var tx = TransformOf( frog );
			_draw[count++] = tx;
			mins = Vector3.Min( mins, tx.Position );
			maxs = Vector3.Max( maxs, tx.Position );
		}
		_drawCount = count;
		if ( !_so.IsValid() ) return;
		// the custom object is culled by its own bounds, so they have to hold every frog drawn (plus a frog's size)
		_so.Bounds = count == 0 ? new BBox( WorldPosition, 16f ) : new BBox( mins - 32f, maxs + 32f );
	}

	/// <summary>
	/// Called from the render pass: every frog in one instanced draw. The model is drawn in its bind pose (an
	/// instanced draw carries no skinning), which for a frog sitting on the grass is exactly the pose wanted; the
	/// hop is the transform's own arc.
	/// </summary>
	private void Render()
	{
		if ( _drawCount <= 0 || _model is null || _model.IsError ) return;
		Graphics.DrawModelInstanced( _model, _draw.AsSpan( 0, _drawCount ), _so.Attributes );
	}

	private sealed class FrogSceneObject : SceneCustomObject
	{
		private readonly VillageFrogs _owner;

		public FrogSceneObject( SceneWorld world, VillageFrogs owner ) : base( world )
		{
			_owner = owner;
			Flags.IsOpaque = true;
			Flags.IsTranslucent = false;
			Flags.WantsPrePass = true;
			// a thousand shadow casters is exactly the cost this whole design exists to avoid
			Flags.CastShadows = false;
		}

		public override void RenderSceneObject() => _owner.Render();
	}

	// ------------------------------------------------------------------ killing one

	/// <summary>
	/// The frogs close enough to swing at get a real body for as long as they stay close: a small collider and a
	/// hittable, pooled and handed round as the player walks. Everything further away cannot be hit — which is what
	/// keeps a thousand frogs free.
	/// </summary>
	private void TickBodies( Vector3 eye )
	{
		int want = Math.Clamp( BodyMax, 0, _near.Count );
		float bodyRangeSq = BodyRange * BodyRange;
		// _near is sorted by distance, so the first entries inside the body range are the ones to give a body to
		int give = 0;
		while ( give < want && _near[give].Distance <= bodyRangeSq ) give++;

		if ( give == 0 ) { ClearBodies(); return; }
		if ( !_bodyRoot.IsValid() )
		{
			_bodyRoot = new GameObject( true, "village_frog_bodies" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
			_bodyRoot.SetParent( GameObject, true );
		}
		while ( _bodies.Count < give )
		{
			var go = new GameObject( true, "frog" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
			go.SetParent( _bodyRoot, true );
			// tagged like any other creature so the sword's trace and the spells find it, plus `critter`, which
			// ProjectSettings/Collision.config pairs with `player` (and npc, ragdoll, debris) as Ignore: the shape
			// is still there for every query that wants to KILL a frog, and gone for everything that would push it
			// around or be stopped by it (user, 2026-09-18)
			go.Tags.Add( "npc" );
			go.Tags.Add( "enemy" );
			go.Tags.Add( "critter" );
			var collider = go.Components.Create<BoxCollider>();
			collider.Scale = new Vector3( 16f, 16f, 14f );
			collider.Center = new Vector3( 0f, 0f, 7f );
			// solid, not a trigger: the sword's FindInPhysics sphere and the spells' traces both want a real shape;
			// the collision rule above, not a trigger flag, is what keeps the player from bumping into it
			//
			// The close ones also get a REAL renderer on the frog's animgraph, because an instanced draw carries no
			// skinning: the whole swarm is drawn in its bind pose, so a village frog slid along its arc with its legs
			// folded under it (user: "it's moving but it's not really playing its animation", 2026-09-18). These few
			// play the pack's jump clip properly and are left out of the instanced draw; everything further away is
			// still one draw call of bind-pose frogs, which is the point of the whole design.
			var renderer = go.Components.Create<SkinnedModelRenderer>();
			renderer.Model = _model;
			renderer.AnimationGraph = _graph;
			// a frog is tiny and there are a lot of them: the swarm has never cast shadows and these must match
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			var body = go.Components.Create<FrogBody>();
			body.Owner = this;
			body.Renderer = renderer;
			_bodies.Add( body );
		}
		while ( _bodies.Count > give )
		{
			var last = _bodies[^1];
			_bodies.RemoveAt( _bodies.Count - 1 );
			if ( last.IsValid() ) last.GameObject.Destroy();
		}
		_animated.Clear();
		for ( int i = 0; i < give; i++ )
		{
			var body = _bodies[i];
			if ( !body.IsValid() ) continue;
			int index = _near[i].Index;
			var frog = _frogs[index];
			if ( body.Index != index ) body.Rebind();
			body.Index = index;
			// the whole transform now, not just the position: a skinned frog has to face the way it is hopping
			body.WorldTransform = TransformOf( frog );
			body.Drive( frog.HopId, frog.HopStart >= 0f );
			_animated.Add( index );
		}
	}

	private void ClearBodies()
	{
		foreach ( var body in _bodies ) if ( body.IsValid() ) body.GameObject.Destroy();
		_bodies.Clear();
		_animated.Clear();
		if ( _bodyRoot.IsValid() ) { _bodyRoot.Destroy(); _bodyRoot = null; }
	}

	/// <summary>
	/// The frog rig's corpse welds: the four IK feet ride the shin they sit on instead of the torso (MonsterCorpse).
	/// The same four strings monster_frog.prefab carries as SkeletonBody.CorpseWelds, written there by
	/// tools/gen_monster_prefabs.py from tools/out/monsters/frog_ragdoll.json. Copied rather than read off the prefab
	/// at runtime; if the frog's ragdoll is ever regenerated, copy them again from the prefab.
	/// </summary>
	private static readonly string[] CorpseWelds = { "BackFoot_L:BackLowLeg_L", "BackFoot_R:BackLowLeg_R", "FrontFoot_L:FrontLowLeg_L", "FrontFoot_R:FrontLowLeg_R" };

	/// <summary>
	/// A frog was hit: it ragdolls off the blow like a dungeon frog, and the collection is one smaller, for good.
	/// Only a frog with a body can be hit, and a body carries a live skinned renderer -- which is exactly what the
	/// corpse system builds its ragdoll from, so the corpse is spawned off it BEFORE the body pool is torn down.
	/// </summary>
	public void Kill( int index, Vector3 push, Vector3 hitPosition, SkinnedModelRenderer source )
	{
		if ( index < 0 || index >= _frogs.Count ) return;
		var at = TransformOf( _frogs[index] ).Position + Vector3.Up * 6f;
		var dir = push.IsNearZeroLength ? Vector3.Up : push.Normal;
		_frogs.RemoveAt( index );
		// the ragdoll, from the animated body's own renderer and pose (it used to just vanish in a blood burst, user 2026-09-19)
		if ( source.IsValid() ) VoidMage.Npc.SkeletonCorpse.Spawn( source, push, hitPosition.IsNearZeroLength ? at : hitPosition, CorpseWelds );
		// the bodies point at indices that just moved: hand them out again next frame
		ClearBodies();
		_near.Clear();
		BloodVfx.Burst( Scene, at, dir );
		VoidMage.Run.RunSave.Data.Frogs = _frogs.Count;
		VoidMage.Run.RunSave.Save();
		if ( Debug ) Log.Info( $"[Frog] one killed at home; {_frogs.Count} left" );
	}

	/// <summary>
	/// One frog's hittable, ANIMATED body, lent to whichever frog is closest to the player this frame: the collider
	/// the sword and the spells find, and the skinned renderer that actually plays the jump clip.
	/// </summary>
	[Title( "Village Frog Body" ), Category( "Void Mage" ), Icon( "pets" )]
	public sealed class FrogBody : Component, DamageRouter.IHittable
	{
		public VillageFrogs Owner { get; set; }
		public int Index { get; set; } = -1;
		public SkinnedModelRenderer Renderer { get; set; }

		private int _seenHop = int.MinValue;
		private int _pulseFrames;

		/// <summary>
		/// Drive the graph for whichever frog this body is standing in for. `hop_trigger` is a pulse held a few frames
		/// so a blend frame cannot swallow it (the same trick SkeletonBody uses for the dungeon's hoppers), and `busy`
		/// has to stay true for the length of the hop or the graph's `busy == false` transition drops straight back to
		/// the idle before the jump has played.
		/// </summary>
		public void Drive( int hopId, bool hopping )
		{
			if ( !Renderer.IsValid() ) return;
			// a body handed to a different frog picks up that frog's hop without firing a spurious one
			if ( _seenHop == int.MinValue ) _seenHop = hopId;
			else if ( hopId != _seenHop ) { _seenHop = hopId; _pulseFrames = 3; }
			bool pulse = _pulseFrames > 0;
			if ( _pulseFrames > 0 ) _pulseFrames--;
			Renderer.Set( "hop_trigger", pulse );
			Renderer.Set( "busy", hopping );
		}

		/// <summary>A body about to be lent to another frog: forget whose hop it was watching.</summary>
		public void Rebind() => _seenHop = int.MinValue;

		void DamageRouter.IHittable.ReceiveHit( in DamageRouter.Hit hit )
		{
			// a frog is 12 hp and nothing in the player's hands does less: one hit is one dead frog
			if ( Owner.IsValid() ) Owner.Kill( Index, hit.Push, hit.Position, Renderer );
		}
	}

	/// <summary>Dev: `village_frogs 50` puts frogs in the village (and saves them), `village_frogs 0` empties it.</summary>
	[ConCmd( "village_frogs" )]
	public static void SetCommand( int count = 25 )
	{
		var swarm = Instance;
		if ( !swarm.IsValid() ) { Log.Info( "[Frog] no village swarm (are you in the village?)" ); return; }
		swarm.EnsureLoaded();
		swarm._frogs.Clear();
		swarm.ClearBodies();
		if ( count > 0 )
		{
			var home = VoidMage.Run.RunManager.Instance.IsValid() ? VoidMage.Run.RunManager.Instance.VillageSpawnPoint : swarm.WorldPosition;
			swarm.Add( count, home );
		}
		VoidMage.Run.RunSave.Data.Frogs = swarm.Count;
		VoidMage.Run.RunSave.Save();
		Log.Info( $"[Frog] village population set to {swarm.Count}" );
	}
}
