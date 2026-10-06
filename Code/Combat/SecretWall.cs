using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Combat;

/// <summary>
/// The way into the secret room. Every floor has one "tell": the reserved sm_env_wall_05 piece, a wall with a
/// doorway shape merely INSET into it, standing where a corridor backs onto the doorless secret room
/// (VoidMage.Dungeon.DecorSecret). A spell that lands on it — a direct hit or a blast beside it — collapses the
/// inset panel: both faces of the segment go away, the pre-placed sm_env_wall_07 pieces (the same wall with the
/// doorway as a real opening) light up in their place, brick chunks blast out of the hole into the corridor and a
/// little rubble is left lying in the new threshold. The sword cannot do it; magic is what opens secrets.
///
/// One hit, whatever the spell's damage. Like the breakables the wall is not a networked object — every peer built
/// it from the same seed — so <see cref="SecretWallNet"/> relays the break by index and a late joiner asks the host
/// for the state on registration.
/// </summary>
[Title( "Secret Wall" ), Category( "Void Mage" ), Icon( "meeting_room" )]
public sealed class SecretWall : Component, DamageRouter.IHittable
{
	// The opening the arch piece has, in the wall's own frame (units): the spawned piece runs along its own +Y with
	// the front toward +X, so fbx x -> local Y (along), fbx y -> local Z (up), fbx z -> local X (through the wall).
	// Per kit, set by DungeonBuilder.SetUpSecretWall from DungeonKit BEFORE the wall registers (a late joiner may
	// break it on registration). The defaults are DungeonPack's: SM_Env_Wall_07's opening is x 110..394 cm of the
	// 5.18 m piece and runs from the floor to the arch at ~420 cm; SM_Env_Wall_05 has no opening at all.
	public float OpenAlongMin { get; set; } = 43.3f;    // 110 cm
	public float OpenAlongMax { get; set; } = 155.1f;   // 394 cm
	public float OpenTop { get; set; } = 165.4f;        // 420 cm
	/// <summary>The wall's own front face (sm_env_wall_05 reaches 26.4 cm forward of its pivot plane).</summary>
	public float FaceForward { get; set; } = 10.4f;

	/// <summary>How deep in front of the wall the brick burst is born. The pieces must NOT spawn inside the arch
	/// mesh they are bursting out of, and Debris fans each piece away from its box centre: a box that is a thin
	/// slab IN the wall plane throws them sideways along the corridor instead of out of the hole.</summary>
	[ConVar( "secret_wall_burst_depth" )] public static float BurstDepth { get; set; } = 40f;

	/// <summary>Brick chunks thrown out of the collapsing doorway.</summary>
	[ConVar( "secret_wall_bricks" )] public static int BrickCount { get; set; } = 17;
	/// <summary>The pack's bricks are 60-79 cm blocks: shrunk a little to read as rubble out of a 2.8 m opening.</summary>
	[ConVar( "secret_wall_brick_scale" )] public static float BrickScale { get; set; } = 0.7f;
	/// <summary>Static rubble left lying in the new threshold, so the doorway still reads as smashed once the
	/// physics debris has been evicted.</summary>
	[ConVar( "secret_wall_rubble" )] public static int RubbleCount { get; set; } = 3;

	/// <summary>The crack of the masonry giving way. There is no stone-crumble sound in the pack yet; this is the
	/// sword's stone impact, and the bricks' own thuds carry the rest of it.</summary>
	public const string SoundBreak = "sounds/swordhitstone.sound";

	/// <summary>The masonry left lying in the threshold; DungeonPack's bricks by default, the kit's own otherwise.</summary>
	public string[] RubbleModels { get; set; } =
	{
		"dungeonpack/sm_env_brick_rubble_01.vmdl", "dungeonpack/sm_env_brick_rubble_02.vmdl",
		"dungeonpack/sm_env_brick_rubble_03.vmdl", "dungeonpack/sm_env_brick_rubble_04.vmdl",
		"dungeonpack/sm_env_brick_rubble_05.vmdl", "dungeonpack/sm_env_brick_rubble_06.vmdl",
	};

	private static BreakableKits.Kit _kit;
	private static bool _warnedNoBricks;

	/// <summary>The bricks the doorway comes apart into: loose only, nothing authored in place.</summary>
	private static BreakableKits.Kit Kit
	{
		get
		{
			if ( !BreakableKits.Loose.TryGetValue( "wall_bricks", out var loose ) || loose.Length == 0 )
			{
				if ( !_warnedNoBricks ) { _warnedNoBricks = true; Log.Warning( "[SecretWall] no wall_bricks debris: re-run tools/gen_breakable_kits.py. The wall still opens, silently." ); }
				loose = Array.Empty<BreakableKits.Piece>();
			}
			if ( _kit is null || _kit.LooseMax != BrickCount || _kit.LooseScale != BrickScale )
				_kit = new BreakableKits.Kit( BreakableKits.Stuff.Stone, Array.Empty<BreakableKits.Piece>(), loose,
					Math.Max( 1, BrickCount - 4 ), Math.Max( 1, BrickCount ), 0, 0, BrickScale, null );
			return _kit;
		}
	}

	public int Index { get; internal set; } = -1;
	/// <summary>The plain single-sided wall on the far side of the same segment; it seals the room too. Null for a
	/// kit whose tell is finished on both faces (Realms): one piece, one arch.</summary>
	public GameObject Back { get; set; }
	/// <summary>The opened-doorway pieces standing in the same plane(s), renderers off until the break.</summary>
	public GameObject Arch { get; set; }
	public GameObject ArchBack { get; set; }
	/// <summary>Seeded from the layout so every peer leaves the same rubble in the same places.</summary>
	public int Seed { get; set; }
	public bool Broken { get; private set; }

	/// <summary>
	/// The doorway opening in world space, pushed out in FRONT of the wall face: where the bricks are born and
	/// where the rubble lands. In front, not in the wall plane, for two reasons — a piece born in the plane spawns
	/// inside the arch mesh it is meant to be bursting out of, and Debris fans each piece away from the box centre,
	/// so a box that is a thin slab in the wall would throw the bricks sideways along the corridor.
	/// Transformed as a box (all eight corners), not as two corners: the wall is yaw-rotated.
	/// </summary>
	public BBox OpeningWorld =>
		new BBox( new Vector3( FaceForward, OpenAlongMin, 0f ), new Vector3( FaceForward + BurstDepth, OpenAlongMax, OpenTop ) )
			.Transform( WorldTransform );

	public Vector3 ClosestPoint( Vector3 p )
	{
		var b = OpeningWorld;
		return new Vector3( p.x.Clamp( b.Mins.x, b.Maxs.x ), p.y.Clamp( b.Mins.y, b.Maxs.y ), p.z.Clamp( b.Mins.z, b.Maxs.z ) );
	}

	/// <summary>A blow landed on the wall itself. Only magic opens it: the sword, the traps and the skeletons bounce.</summary>
	public void ReceiveHit( in DamageRouter.Hit hit )
	{
		if ( !DamageRouter.IsSpell( hit.Kind ) ) return;
		Break( hit.Position, hit.Push, localAttacker: true );
	}

	/// <summary>Open it here and now, then tell the host so it reaches everyone else.</summary>
	public void Break( Vector3 position, Vector3 push, bool localAttacker )
	{
		if ( Broken ) return;
		BreakLocal( position, push, localAttacker, silent: false );
		SecretWallNet.Report( Index, position, push );
	}

	/// <summary>Idempotent: the echo of a predicted break is a no-op. `silent` is a late joiner catching up.</summary>
	internal void BreakLocal( Vector3 position, Vector3 push, bool localAttacker, bool silent )
	{
		if ( Broken ) return;
		Broken = true;

		var opening = OpeningWorld;
		var centre = opening.Center;
		// out of the hole, not along the corridor: the wall's own forward is the way into the host corridor
		var outward = WorldRotation.Forward;
		if ( outward.IsNearZeroLength ) outward = (centre - position).WithZ( 0f ).Normal;

		Show( GameObject, false );
		Show( Back, false );
		Show( Arch, true );
		Show( ArchBack, true );

		var rng = new Random( Seed );
		SpawnRubble( rng, opening );

		if ( !silent )
		{
			VoidMage.Audio.GameAudio.Play( SoundBreak, centre );
			BreakVfx.Stone( Scene, centre, opening.Size.Length * 0.5f, outward, localAttacker );
			// the dungeon root, not this object: its renderer is the thing that just went away
			var parent = GameObject.Parent.IsValid() ? GameObject.Parent : GameObject;
			Debris.Burst( Scene, WorldTransform, opening, Kit, outward, heavy: true, BreakableProp.SoundThud, parent );
		}

		_ = RetileNav( opening );
	}

	/// <summary>Renderer and collider on and off together; the arch's pieces keep their mesh colliders either way
	/// (see DungeonBuilder.PlaceWallSwap: cooking them here would hitch on the break frame).</summary>
	private static void Show( GameObject go, bool on )
	{
		if ( !go.IsValid() ) return;
		foreach ( var r in go.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelf ) ) r.Enabled = on;
		foreach ( var c in go.Components.GetAll<Collider>( FindMode.EverythingInSelf ) ) c.Enabled = on;
	}

	/// <summary>A few chunks of masonry left lying in the threshold, on the floor, out of the walking centre.</summary>
	private void SpawnRubble( Random rng, BBox opening )
	{
		var parent = GameObject.Parent.IsValid() ? GameObject.Parent : GameObject;
		var n = RubbleModels == null || RubbleModels.Length == 0 ? 0 : Math.Clamp( RubbleCount, 0, 6 );
		for ( var i = 0; i < n; i++ )
		{
			var model = Debris.LoadModel( RubbleModels[rng.Next( RubbleModels.Length )] );
			if ( model is null || model.IsError ) continue;
			// along the opening but pushed off centre, and only just in front of the wall plane
			var along = (float)(rng.NextDouble() * 0.8 + 0.1);
			var side = i % 2 == 0 ? along : 1f - along;
			var local = new Vector3(
				FaceForward + (float)rng.NextDouble() * BurstDepth * 0.5f,
				OpenAlongMin + (OpenAlongMax - OpenAlongMin) * side,
				0f );
			var go = new GameObject( parent, true, $"{GameObject.Name} rubble {i}" );
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			go.WorldRotation = Rotation.FromYaw( (float)rng.NextDouble() * 360f );
			go.WorldPosition = WorldTransform.PointToWorld( local ).WithZ( opening.Mins.z - model.Bounds.Mins.z );
			go.Components.Create<ModelRenderer>().Model = model;
		}
	}

	/// <summary>
	/// The navmesh was tiled once at generate time with this wall solid, and nothing re-tiles it. Without this a
	/// skeleton chasing the player into the opened room walks into the doorway and steers at the wall that is no
	/// longer there. Local to each peer: the navmesh is not networked.
	/// </summary>
	private async System.Threading.Tasks.Task RetileNav( BBox opening )
	{
		var nav = Scene?.NavMesh;
		if ( nav is null || !nav.IsEnabled ) return;
		await GameTask.DelaySeconds( 0.05f );   // let the disabled colliders leave the physics world
		if ( !Scene.IsValid() || !GameObject.IsValid() ) return;
		// the doorway plus a room's worth either side, so the tiles that meet at the opening are rebuilt too
		var pad = new Vector3( 220f, 220f, 120f );
		var box = new BBox( opening.Mins - pad, opening.Maxs + pad );
		try { await nav.GenerateTiles( Scene.PhysicsWorld, box ); }
		catch ( Exception e ) { Log.Warning( $"[SecretWall] navmesh retile failed: {e.Message}" ); }
	}
}

/// <summary>
/// The secret walls of the current dungeon by index and the break relay. Static RPCs because the walls are not
/// networked: every peer built the same list in the same order from the seed (the BreakableNet / TrapNet pattern).
/// Unlike a trap's state a break is permanent, so a peer that registers late asks the host whether it already
/// happened and catches up silently.
/// </summary>
public static class SecretWallNet
{
	/// <summary>A blast this close to the doorway opens it, matching the fireball's reach for crates.</summary>
	[ConVar( "secret_wall_splash_radius" )] public static float SplashRadius { get; set; } = 90f;

	private static readonly List<SecretWall> _walls = new();
	private static readonly HashSet<int> _brokenEarly = new();

	public static int Count => _walls.Count;

	/// <summary>Forget the previous dungeon's walls (a rebuild registers a fresh list).</summary>
	public static void Clear()
	{
		_walls.Clear();
		_brokenEarly.Clear();
	}

	public static int Register( SecretWall wall )
	{
		wall.Index = _walls.Count;
		_walls.Add( wall );
		// a break that arrived while this peer was still building, or one that happened before it joined at all
		if ( _brokenEarly.Contains( wall.Index ) ) wall.BreakLocal( wall.WorldPosition, Vector3.Zero, false, silent: true );
		else if ( Networking.IsActive && !Networking.IsHost ) RequestState( wall.Index );
		return wall.Index;
	}

	private static SecretWall Get( int index ) => index >= 0 && index < _walls.Count && _walls[index].IsValid() ? _walls[index] : null;

	/// <summary>Every secret wall of the floor opened (the director's button).</summary>
	public static void BreakAll()
	{
		foreach ( var w in _walls )
			if ( w.IsValid() && !w.Broken ) w.Break( w.OpeningWorld.Center, w.WorldRotation.Forward, localAttacker: true );
	}

	public static void Report( int index, Vector3 position, Vector3 push )
	{
		if ( index < 0 ) return;
		if ( !Networking.IsActive ) return;   // single player: the local break is the whole story
		if ( Networking.IsHost ) BreakRpc( index, position, push, false );
		else RequestBreak( index, position, push );
	}

	[Rpc.Host]
	private static void RequestBreak( int index, Vector3 position, Vector3 push ) => BreakRpc( index, position, push, false );

	[Rpc.Broadcast]
	private static void BreakRpc( int index, Vector3 position, Vector3 push, bool silent )
	{
		var w = Get( index );
		if ( w is null ) { _brokenEarly.Add( index ); return; }   // still building: catch up at Register
		w.BreakLocal( position, push, localAttacker: false, silent );
	}

	/// <summary>A peer that just built its dungeon asks whether the wall is already open.</summary>
	[Rpc.Host]
	private static void RequestState( int index )
	{
		var w = Get( index );
		if ( w is not null && w.Broken ) BreakRpc( index, w.WorldPosition, Vector3.Zero, true );
	}

	/// <summary>A spell went off near the wall: opens it if the doorway is inside `radius`. Returns how many opened.</summary>
	public static int Blast( Vector3 at, float radius, bool localAttacker = true )
	{
		var n = 0;
		var r2 = radius * radius;
		foreach ( var w in _walls )
		{
			if ( !w.IsValid() || w.Broken ) continue;
			var closest = w.ClosestPoint( at );
			if ( closest.DistanceSquared( at ) > r2 ) continue;
			var push = (w.WorldRotation.Forward).WithZ( 0f );
			w.Break( closest, push.IsNearZeroLength ? Vector3.Forward : push.Normal, localAttacker );
			n++;
		}
		return n;
	}

	/// <summary>secret_wall_break [index]: open the secret wall without hunting for it. No index = the first.</summary>
	[ConCmd( "secret_wall_break" )]
	public static void BreakCommand( string index = "" )
	{
		if ( _walls.Count == 0 ) { Log.Info( "[SecretWall] none in this dungeon" ); return; }
		if ( !int.TryParse( index, out var i ) ) i = 0;
		var w = Get( i );
		if ( w is null ) { Log.Info( $"[SecretWall] no wall #{i}" ); return; }
		if ( w.Broken ) { Log.Info( $"[SecretWall] #{i} is already open" ); return; }
		w.Break( w.OpeningWorld.Center, w.WorldRotation.Forward, localAttacker: true );
		Log.Info( $"[SecretWall] opened #{i} at {w.WorldPosition:0}" );
	}

	/// <summary>secret_wall_list: where the tell is and whether it is open.</summary>
	[ConCmd( "secret_wall_list" )]
	public static void ListCommand()
	{
		if ( _walls.Count == 0 ) { Log.Info( "[SecretWall] none" ); return; }
		foreach ( var w in _walls )
			Log.Info( w.IsValid() ? $"[SecretWall] #{w.Index} {w.GameObject.Name} at {w.WorldPosition:0} {(w.Broken ? "OPEN" : "sealed")}" : "[SecretWall] (destroyed)" );
	}
}
