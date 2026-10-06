using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Traps;

namespace VoidMage.Dungeon;

/// <summary>
/// The trap half of the builder: spawns the decoration's TrapPlacements as local, non-networked objects in
/// placement order (the index every peer relays by), hands each its world boxes, and keeps skeleton spawns
/// off trap cells. Moving pieces (spears, blade) are not Static: a cached static shadow would stay behind.
/// </summary>
public sealed partial class DungeonBuilder
{
	/// <summary>Place traps (the decoration decides where; count and kinds below).</summary>
	[Property, Group( "Traps" )] public bool Traps { get; set; } = true;
	/// <summary>Traps per floor, rolled between these; per-floor tables come later.</summary>
	[Property, Group( "Traps" ), Range( 0, 16 )] public int TrapsMin { get; set; } = 6;
	[Property, Group( "Traps" ), Range( 0, 16 )] public int TrapsMax { get; set; } = 8;

	private const string GrateModel = "dungeonpack/sm_prop_trap_spears_grate_01.vmdl";
	private const string SpearModel = "dungeonpack/sm_prop_trap_spears_01.vmdl";
	private const string BladeModel = "dungeonpack/sm_prop_trap_swing_blade_01.vmdl";
	private const string BladeHousingModel = "dungeonpack/sm_prop_trap_swing_blade_housing_01.vmdl";
	/// <summary>The flame and acid heads are the one trap piece the kit themes (Realms has its own dwarf heads, with
	/// a separate jaw piece spawned at the same transform); everything else about a trap stays DungeonPack.</summary>
	private string FlameHeadModel => _kit.FlameHeadModel;

	/// <summary>The grate model's footprint, metres (2.48 x 2.47): four of them at full size overhang a corridor's inner width and sit in the walls.</summary>
	private const float GrateSize = 2.48f;
	/// <summary>Clear space (units) between the outer grates and each wall face.</summary>
	private const float GrateMargin = 4f;
	/// <summary>The grate is 0.35 m thick; halved it is a low grid on the floor, not a step (user: "quite thick", "annoying step up").</summary>
	private const float GrateHeightScale = 0.5f;
	/// <summary>Metres the spear tips reach above the floor when up.</summary>
	private const float SpikeRise = 1.6f;
	/// <summary>Gap (units) between the two blades of a pair; unscaled, the pair spans 4.3 m of the 4.46 m corridor.</summary>
	private const float BladeGap = 1f;
	/// <summary>Metres up the wall the flame head hangs: head height, so a crouching player passes under the jet.</summary>
	private float FlameHeadHeight => _kit.FlameHeadHeight;
	/// <summary>The jet's reach (units) from the wall face: nearly the corridor's inner width, short of the far wall.</summary>
	private const float JetLength = 172f;
	/// <summary>Half the jet's width (units) either side of the nozzle.</summary>
	private const float JetHalfWidth = 40f;
	/// <summary>The jet's height band above the floor (units): standing bodies (72) reach it, ducked ones (36) pass under.</summary>
	private const float JetLow = 46f, JetHigh = 78f;
	/// <summary>Radius (units) of the soot mark under the jet.</summary>
	private const float ScorchRadius = 50f;

	private TrapBudget TrapBudgetFor()
	{
		if ( !Traps ) return new TrapBudget( 0, 0, Array.Empty<TrapKind>() );
		var kinds = TrapBudget.ForDepth( Depth ).Kinds;
		return new TrapBudget( Math.Min( TrapsMin, TrapsMax ), Math.Max( TrapsMin, TrapsMax ), kinds );
	}

	/// <summary>The world box over one cell: floor surface (a little under, for the feet) to 1.1 m up.</summary>
	private BBox CellBox( int x, int z, int l )
	{
		float top = _floorTop.TryGetValue( (x, z, l), out var t ) ? t : 0f;
		var a = WorldTransform.PointToWorld( W( x, z, l ) + Vector3.Up * (top - 10f) );
		var b = WorldTransform.PointToWorld( W( x + 1, z + 1, l ) + Vector3.Up * (top + 110f) );
		return new BBox( Vector3.Min( a, b ), Vector3.Max( a, b ) );
	}

	private GameObject NewTrapRoot( string name, Vector3 localPos, float yaw )
	{
		var go = new GameObject( GameObject, true, name ) { Flags = GameObjectFlags.NotSaved };
		go.LocalPosition = localPos;
		go.LocalRotation = Rotation.FromYaw( yaw );
		go.Tags.Add( TrapBase.Tag );
		return go;
	}

	private void BuildTraps( DungeonLayout layout )
	{
		TrapNet.Clear();
		var decor = layout.Decor;
		if ( decor == null || decor.Traps.Count == 0 ) return;
		var counts = new Dictionary<TrapKind, int>();
		foreach ( var p in decor.Traps )
		{
			TrapBase trap = p.Kind switch
			{
				TrapKind.SpikeFloor => BuildSpikeFloor( p ),
				TrapKind.SwingBlade => BuildSwingBlade( p ),
				TrapKind.FlameHead => BuildFlameHead( p ),
				TrapKind.ArrowWall => BuildArrowWall( p ),
				TrapKind.SawRail => BuildSawRail( p ),
				TrapKind.AcidHead => BuildAcidHead( p ),
				TrapKind.WallSlab => BuildWallSlab( p ),
				_ => null,
			};
			if ( trap is null )
			{
				Log.Warning( $"[Dungeon] no builder for trap kind {p.Kind}; the index order is now off" );
				continue;
			}
			trap.Placement = p;
			// cell boxes by default; a builder that shaped its own volume (the flame jet) keeps it
			if ( trap.TriggerBoxes.Length == 0 ) trap.TriggerBoxes = DecorTraps.TriggerCells( p ).Select( c => CellBox( c.x, c.z, p.Level ) ).ToArray();
			if ( trap.DamageBoxes.Length == 0 ) trap.DamageBoxes = DecorTraps.DamageCells( p ).Select( c => CellBox( c.x, c.z, p.Level ) ).ToArray();
			int index = TrapNet.Register( trap );
			if ( index != p.Index ) Log.Warning( $"[Dungeon] trap {p.Name} registered as #{index}, placement says #{p.Index}" );
			counts[p.Kind] = counts.TryGetValue( p.Kind, out var n ) ? n + 1 : 1;
		}
		var blade = LoadModel( BladeModel ).Bounds;
		Log.Info( $"[Dungeon] {TrapNet.Count} traps: {string.Join( " ", counts.OrderBy( kv => kv.Key ).Select( kv => $"{kv.Key}x{kv.Value}" ) )}; blade bounds {blade.Mins:0}..{blade.Maxs:0}" );
	}

	/// <summary>Four grates at the cell's quarters (a raised grid, the tell) and four spear blocks under them, hidden until they fire.</summary>
	private TrapBase BuildSpikeFloor( TrapPlacement p )
	{
		float surface = _floorTop.TryGetValue( (p.X, p.Z, p.Level), out var t ) ? t : 0f;
		float yaw = Decor.YawOf( p.Facing );
		var root = NewTrapRoot( p.Name, W( p.X + 0.5f, p.Z + 0.5f, p.Level ) + Vector3.Up * surface, yaw );
		var trap = root.Components.Create<TrapSpikeFloor>();
		var spear = LoadModel( SpearModel ).Bounds;
		var grates = new List<GameObject>();
		var spears = new List<GameObject>();
		// two grates side by side must fit between the wall faces: scale them to the inner width less a margin,
		// and sit each one a quarter of that span off the cell centre, on top of the tile (not sunk), half height
		float inner = Cell - 2f * WallHalf - 2f * GrateMargin;
		float grateScale = inner / (2f * GrateSize * M);
		float q = inner / 4f / Cell;   // offset from the cell centre, in cell units
		var grateScaleVec = new Vector3( grateScale, grateScale, GrateHeightScale );
		foreach ( var dx in new[] { -q, q } )
			foreach ( var dz in new[] { -q, q } )
			{
				float gx = p.X + 0.5f + dx, gz = p.Z + 0.5f + dz;
				// the grates rattle during the wind-up, so they are not Static either
				grates.Add( Spawn( GrateModel, W( gx, gz, p.Level ) + Vector3.Up * (surface + FloorClearance), yaw, $"{p.Name} grate", ColliderKind.Box, grateScaleVec, isStatic: false, themed: false ) );
				var s = Spawn( SpearModel, W( gx, gz, p.Level ) + Vector3.Up * surface, yaw, $"{p.Name} spear", ColliderKind.None, isStatic: false, themed: false );
				s.SetParent( root, true );
				spears.Add( s );
			}
		trap.Grates = grates.ToArray();
		trap.Spears = spears.ToArray();
		// base pivot: the tips are at Maxs.z. At rest they sit 2 u under the floor surface; up, SpikeRise above it.
		trap.RestZ = -(spear.Maxs.z + 2f);
		trap.RaisedZ = -(spear.Maxs.z - SpikeRise * M);
		return trap;
	}

	/// <summary>
	/// Two blades side by side across the hallway, each under its own housing flush with the ceiling, swinging along
	/// the hallway. One yawed pivot root at the cell's ceiling centre (+X down the hallway = the swing plane), a
	/// "swing" child per blade offset along local Y (across the hallway) that pitches, the unscaled blade mesh under
	/// each. The blade's width lies along local Y, so the pair covers the corridor.
	/// </summary>
	private TrapBase BuildSwingBlade( TrapPlacement p )
	{
		float yaw = Decor.YawOf( p.Facing );
		var rot = Rotation.FromYaw( yaw );
		var pivot = W( p.X + 0.5f, p.Z + 0.5f, p.Level + 2 ) - Vector3.Up * (CeilingDrop + 0.11f * M);
		var b = LoadModel( BladeModel ).Bounds;
		// three blades abreast (user, 2026-09-20: "one extra in the middle"). The pair already filled the corridor, so
		// each of the three is two thirds as wide: squeezed across its own width only, its length and its reach kept.
		const float squeeze = 2f / 3f;
		float halfWidth = b.Size.y * 0.5f * squeeze;
		float side = 2f * halfWidth + BladeGap;
		var root = NewTrapRoot( p.Name, pivot, yaw );
		var swings = new List<GameObject>();
		var blades = new List<GameObject>();
		foreach ( var offset in new[] { -side, 0f, side } )
		{
			var at = pivot + rot.Left * offset;
			var housing = Spawn( BladeHousingModel, at, yaw, $"{p.Name} housing", ColliderKind.None, themed: false );
			if ( housing.IsValid() ) housing.LocalScale = housing.LocalScale * new Vector3( 1f, squeeze, 1f );
			var swing = new GameObject( root, true, "swing" ) { Flags = GameObjectFlags.NotSaved };
			swing.LocalPosition = new Vector3( 0f, offset, 0f );
			swing.LocalRotation = Rotation.Identity;
			// the crescent cuts edge-first: the mesh's wide axis turned into the swing plane (user: "the blade is sideways")
			var blade = Spawn( BladeModel, at, yaw + 90f, $"{p.Name} blade", ColliderKind.None, isStatic: false, themed: false );
			blade.LocalScale = blade.LocalScale * new Vector3( 1f, squeeze, 1f );   // the mesh's own wide axis, whichever way it was turned
			blade.SetParent( swing, true );
			swings.Add( swing );
			blades.Add( blade );
		}
		var trap = root.Components.Create<TrapSwingBlade>();
		trap.Swings = swings.ToArray();
		trap.Blades = blades.ToArray();
		trap.PhaseOffset = p.Index * 0.37f;
		trap.HalfThick = b.Size.x * 0.5f;
		trap.HalfWidth = halfWidth;
		trap.Length = -b.Mins.z;
		return trap;
	}

	/// <summary>
	/// The head on the wall beside the door frame at head height, its jet straight out from the wall across the
	/// doorway cell (the way in crosses it; crouch to pass under); the jet box is the trap's trigger and damage
	/// volume; a pilot flame (a small FireLight the fire system rigs and gates); the fire breath rig; permanent
	/// soot on the tile under the jet as the floor tell.
	/// </summary>
	private TrapBase BuildFlameHead( TrapPlacement p )
	{
		var d = p.WallD;
		var rot = Rotation.FromYaw( Decor.YawOf( d.Opp() ) );   // +X into the room, like every wall prop
		var model = LoadModel( FlameHeadModel );
		// a head modelled facing some other way is turned at spawn, and every measurement is taken on the turned box
		float hy = _kit.FlameHeadYaw;
		var bounds = hy == 0f ? model.Bounds : model.Bounds.Rotate( Rotation.FromYaw( hy ) );
		float surface = _floorTop.TryGetValue( (p.X, p.Z, p.Level), out var t ) ? t : 0f;
		var edgeMid = W( p.X + 0.5f, p.Z + 0.5f, p.Level ) + new Vector3( d.Dx(), d.Dz(), 0f ) * (Cell / 2f);
		float push = WallHalf - bounds.Mins.x;
		// slide along the wall toward the door frame (the doorway is on the Facing side of this cell)
		var toDoor = new Vector3( p.Facing.Dx(), p.Facing.Dz(), 0f );
		float s = Vector3.Dot( rot.Left, toDoor ) > 0f ? 0.28f : -0.28f;
		var pos = edgeMid + rot.Forward * push + rot.Left * (s * Cell) + Vector3.Up * (FlameHeadHeight * M + surface);
		Spawn( FlameHeadModel, pos, rot.Yaw() + hy, $"{p.Name} head", ColliderKind.None, themed: false );
		if ( _kit.FlameHeadMouth != null ) Spawn( _kit.FlameHeadMouth, pos, rot.Yaw() + hy, $"{p.Name} mouth", ColliderKind.None, themed: false );
		var root = NewTrapRoot( p.Name, pos, rot.Yaw() );
		var trap = root.Components.Create<TrapFlameHead>();

		var nozzle = pos + rot.Forward * bounds.Maxs.x;
		trap.Nozzle = WorldTransform.PointToWorld( nozzle );
		trap.Aim = WorldTransform.NormalToWorld( rot.Forward ).Normal;

		// the jet volume: from the wall face out along the normal, the nozzle's width either side, a head-height band
		var face = edgeMid + rot.Forward * WallHalf + rot.Left * (s * Cell);
		var c0 = WorldTransform.PointToWorld( face - rot.Left * JetHalfWidth + Vector3.Up * (surface + JetLow) );
		var c1 = WorldTransform.PointToWorld( face + rot.Forward * JetLength + rot.Left * JetHalfWidth + Vector3.Up * (surface + JetHigh) );
		var jetBox = new BBox( Vector3.Min( c0, c1 ), Vector3.Max( c0, c1 ) );
		trap.TriggerBoxes = new[] { jetBox };
		trap.DamageBoxes = new[] { jetBox };
		trap.Jet = TrapFlameVfx.Build( root, trap.Nozzle, trap.Aim );

		// the pilot flame: a candle-sized FireLight at the nozzle, no shadows (a flare must not stale a shadow cache)
		var lightGo = new GameObject( root, true, "light" ) { Flags = GameObjectFlags.NotSaved };
		lightGo.LocalPosition = new Vector3( bounds.Maxs.x + 3f, 0f, 0f );
		var colour = new Color( 1f, 0.6f, 0.15f ) * 1.2f;
		var light = lightGo.Components.Create<PointLight>();
		light.LightColor = colour;
		light.Radius = 120f;
		light.Shadows = false;
		var fire = lightGo.Components.Create<FireLight>();
		fire.Kind = LightKind.Candle;
		fire.FlameScale = 0.45f;
		fire.FlameOffset = Vector3.Zero;
		fire.BasePosition = lightGo.LocalPosition;
		fire.BaseSet = true;
		fire.WallFaceX = bounds.Mins.x;
		fire.BaseColor = colour;
		fire.BaseRadius = 120f;
		fire.Phase = (p.Index * 3.7f) % 10f;
		fire.Amount = 0.25f;
		trap.Pilot = fire;

		// the floor tell: soot that never fades under the middle of the jet, under the dungeon root so a rebuild sweeps it
		var soot = WorldTransform.PointToWorld( face + rot.Forward * (JetLength * 0.5f) + Vector3.Up * surface );
		VoidMage.Spells.GroundScorch.Place( soot, Vector3.Up, ScorchRadius, GameObject, permanent: true );
		return trap;
	}

	// ------------------------------------------------------------------ the second four

	private const string ArrowHoleModel = "dungeonpack/sm_prop_trap_wallarrow_hole_01.vmdl";
	private const string ArrowHoleModel2 = "dungeonpack/sm_prop_trap_wallarrow_hole_02.vmdl";
	private const string ArrowModel = "dungeonpack/sm_item_arrow_01.vmdl";
	private const string SawModel = "dungeonpack/sm_prop_trap_saw_01.vmdl";
	private const string SawRailModel = "dungeonpack/sm_prop_trap_saw_rail_01.vmdl";
	private string AcidHeadModel => _kit.AcidHeadModel;
	private const string WallSlabModel = "dungeonpack/sm_prop_trap_wallslab_01.vmdl";

	/// <summary>Arrow holes: two rows (metres up) in two columns (cells along the wall).</summary>
	private static readonly float[] ArrowRows = { 1.1f, 1.55f };
	private static readonly float[] ArrowColumns = { -0.15f, 0.15f };
	/// <summary>The volley's height band above the floor: standing bodies reach it, ducked ones pass under.</summary>
	private const float ArrowLow = 40f, ArrowHigh = 72f;
	/// <summary>Units the volley's lane reaches past the outer hole columns: a body's half width, since hits test the feet point.</summary>
	private const float ArrowBodyHalf = 16f;
	/// <summary>Metres up the wall the acid head hangs, and its pool's radius (units).</summary>
	private float AcidHeadHeight => _kit.AcidHeadHeight;
	private const float AcidPoolRadius = 70f;
	/// <summary>Units the slab's plate stands proud of its wall at rest.</summary>
	private const float SlabProud = 8f;

	/// <summary>The frame of wall face `d` of a cell: a rotation facing into the cell (+X = the wall's normal), the edge's midpoint on the grid plane, and the floor surface.</summary>
	private (Rotation rot, Vector3 edgeMid, float surface) WallFrame( int x, int z, int l, Dir d )
	{
		var rot = Rotation.FromYaw( Decor.YawOf( d.Opp() ) );
		var edgeMid = W( x + 0.5f, z + 0.5f, l ) + new Vector3( d.Dx(), d.Dz(), 0f ) * (Cell / 2f);
		float surface = _floorTop.TryGetValue( (x, z, l), out var t ) ? t : 0f;
		return (rot, edgeMid, surface);
	}

	/// <summary>A cell's world box between two heights above its floor surface.</summary>
	private BBox CellBand( int x, int z, int l, float low, float high )
	{
		float top = _floorTop.TryGetValue( (x, z, l), out var t ) ? t : 0f;
		var a = WorldTransform.PointToWorld( W( x, z, l ) + Vector3.Up * (top + low) );
		var b = WorldTransform.PointToWorld( W( x + 1, z + 1, l ) + Vector3.Up * (top + high) );
		return new BBox( Vector3.Min( a, b ), Vector3.Max( a, b ) );
	}

	/// <summary>
	/// Four holes on the wall (two rows, two columns), old arrows stuck in the wall opposite when it is a wall, and the
	/// cell as the plate. The volley leaves the holes' mouths straight across; its damage band sits at chest and head
	/// height so a crouching body is under it.
	/// </summary>
	private TrapBase BuildArrowWall( TrapPlacement p )
	{
		var d = p.WallD;
		var (rot, edgeMid, surface) = WallFrame( p.X, p.Z, p.Level, d );
		var hole = LoadModel( ArrowHoleModel ).Bounds;
		float push = WallHalf - hole.Mins.x;
		var root = NewTrapRoot( p.Name, edgeMid + rot.Forward * WallHalf + Vector3.Up * surface, rot.Yaw() );
		var trap = root.Components.Create<TrapArrowWall>();
		var mouths = new List<Vector3>();
		int k = 0;
		foreach ( var h in ArrowRows )
			foreach ( var s in ArrowColumns )
			{
				var pos = edgeMid + rot.Forward * push + rot.Left * (s * Cell) + Vector3.Up * (h * M + surface);
				// the holes are masonry set INTO the wall, not a mechanism: they follow the wall's atlas or they
				// read as a stone patch on a green one. The mechanisms above and below keep the stone atlas.
				Spawn( k++ % 2 == 0 ? ArrowHoleModel : ArrowHoleModel2, pos, rot.Yaw(), $"{p.Name} hole", ColliderKind.None );
				mouths.Add( WorldTransform.PointToWorld( pos + rot.Forward * hole.Maxs.x ) );
			}
		float inner = Cell - 2f * WallHalf;
		trap.Holes = mouths.ToArray();
		trap.Aim = WorldTransform.NormalToWorld( rot.Forward ).Normal;
		trap.Range = inner - (push - WallHalf) - hole.Maxs.x;
		trap.ArrowModel = ArrowModel;
		trap.Dungeon = GameObject;
		trap.TriggerBoxes = new[] { CellBox( p.X, p.Z, p.Level ) };
		// the lane the arrows fly down, not the whole cell: the plate is 5 m wide, the volley barely 0.6 m
		float laneHalf = ArrowColumns.Max( MathF.Abs ) * Cell + ArrowBodyHalf;
		var laneA = WorldTransform.PointToWorld( edgeMid + rot.Left * laneHalf + Vector3.Up * (surface + ArrowLow) );
		var laneB = WorldTransform.PointToWorld( edgeMid + rot.Forward * Cell - rot.Left * laneHalf + Vector3.Up * (surface + ArrowHigh) );
		trap.DamageBoxes = new[] { new BBox( Vector3.Min( laneA, laneB ), Vector3.Max( laneA, laneB ) ) };

		// the second tell: arrows from volleys past, stuck in the wall opposite (their tips a hand's width in)
		var opposite = d.Opp();
		bool farIsWall = Layout.Edges.Any( e => e.Kind == EdgeKind.Wall && (e.X, e.Z, e.Level, e.D) == (p.X, p.Z, p.Level, opposite) );
		if ( farIsWall )
		{
			var arrow = LoadModel( ArrowModel ).Bounds;
			var farFace = edgeMid + rot.Forward * (Cell - WallHalf);
			var pattern = new[] { (s: -0.12f, h: 1.2f, pitch: -6f), (s: 0.06f, h: 0.95f, pitch: 4f), (s: 0.2f, h: 1.5f, pitch: -3f) };
			foreach ( var (s, h, pitch) in pattern )
			{
				var pos = farFace - rot.Forward * (arrow.Maxs.x - 11f) + rot.Left * (s * Cell) + Vector3.Up * (h * M + surface);
				var go = Spawn( ArrowModel, pos, rot.Yaw(), $"{p.Name} stuck arrow", ColliderKind.None, themed: false );
				go.LocalRotation = Rotation.FromYaw( rot.Yaw() ) * Rotation.FromPitch( pitch );
			}
		}
		return trap;
	}

	/// <summary>A rail piece per cell down the middle of the run and the disc riding it from end to end.</summary>
	private TrapBase BuildSawRail( TrapPlacement p )
	{
		var cells = DecorTraps.DamageCells( p );
		float yaw = Decor.YawOf( p.Facing );
		var rot = Rotation.FromYaw( yaw );
		var rail = LoadModel( SawRailModel ).Bounds;
		var saw = LoadModel( SawModel ).Bounds;
		float radius = saw.Maxs.z;
		Vector3 first = default, last = default;
		float surface = 0f;
		for ( int i = 0; i < cells.Count; i++ )
		{
			var (cx, cz) = cells[i];
			float top = _floorTop.TryGetValue( (cx, cz, p.Level), out var t ) ? t : 0f;
			var centre = W( cx + 0.5f, cz + 0.5f, p.Level ) + Vector3.Up * top;
			Spawn( SawRailModel, centre + Vector3.Up * (-rail.Mins.z + FloorClearance), yaw, $"{p.Name} rail", ColliderKind.None, themed: false );
			if ( i == 0 ) { first = centre; surface = top; }
			last = centre;
		}
		var root = NewTrapRoot( p.Name, first, yaw );
		var trap = root.Components.Create<TrapSawRail>();
		// the disc's centre rides just above the rail; it spins in the rail's vertical plane (thin across the hallway)
		float lift = radius + 3f;
		var start = first + Vector3.Up * lift + rot.Forward * (radius + 6f);
		var end = last + Vector3.Up * lift - rot.Forward * (radius + 6f);
		var disc = Spawn( SawModel, start, yaw, $"{p.Name} saw", ColliderKind.None, isStatic: false, themed: false );
		disc.SetParent( root, true );
		trap.Disc = disc;
		trap.Start = WorldTransform.PointToWorld( start );
		trap.Along = WorldTransform.NormalToWorld( rot.Forward ).Normal;
		trap.Travel = MathF.Max( 0f, (end - start).Length );
		trap.Radius = radius;
		trap.HalfThick = saw.Size.y * 0.5f;
		trap.PhaseOffset = p.Index * 0.61f;
		_ = surface;
		return trap;
	}

	/// <summary>
	/// The head low on the wall beside the door, dripping onto the doorway cell; its effects rig aimed at the cell's
	/// floor centre; the cell as the plate; a green-eaten stain on the tile as the tell.
	/// </summary>
	private TrapBase BuildAcidHead( TrapPlacement p )
	{
		var d = p.WallD;
		var (rot, edgeMid, surface) = WallFrame( p.X, p.Z, p.Level, d );
		var model = LoadModel( AcidHeadModel );
		float hy = _kit.AcidHeadYaw;
		var bounds = hy == 0f ? model.Bounds : model.Bounds.Rotate( Rotation.FromYaw( hy ) );
		float push = WallHalf - bounds.Mins.x;
		var toDoor = new Vector3( p.Facing.Dx(), p.Facing.Dz(), 0f );
		float s = Vector3.Dot( rot.Left, toDoor ) > 0f ? 0.28f : -0.28f;
		var pos = edgeMid + rot.Forward * push + rot.Left * (s * Cell) + Vector3.Up * (AcidHeadHeight * M + surface);
		Spawn( AcidHeadModel, pos, rot.Yaw() + hy, $"{p.Name} head", ColliderKind.None, themed: false );
		if ( _kit.AcidHeadMouth != null ) Spawn( _kit.AcidHeadMouth, pos, rot.Yaw() + hy, $"{p.Name} mouth", ColliderKind.None, themed: false );
		var root = NewTrapRoot( p.Name, pos, rot.Yaw() );
		var trap = root.Components.Create<TrapAcidHead>();
		var nozzle = WorldTransform.PointToWorld( pos + rot.Forward * bounds.Maxs.x );
		var floor = WorldTransform.PointToWorld( W( p.X + 0.5f, p.Z + 0.5f, p.Level ) + Vector3.Up * surface );
		trap.Rig = TrapAcidVfx.Build( root, nozzle, floor, AcidPoolRadius );
		trap.PoolBox = CellBand( p.X, p.Z, p.Level, -10f, 30f );
		VoidMage.Spells.GroundScorch.Place( floor, Vector3.Up, ScorchRadius, GameObject, permanent: true, tint: TrapAcidVfx.AcidDark );
		return trap;
	}

	/// <summary>
	/// The slab set into its wall with the plate a hand's width proud and the shafts hidden in the rock behind; it
	/// travels the cell's inner width less that, so the plate meets the far wall. A damage volume, no collider.
	/// </summary>
	private TrapBase BuildWallSlab( TrapPlacement p )
	{
		var d = p.WallD;
		var (rot, edgeMid, surface) = WallFrame( p.X, p.Z, p.Level, d );
		var slab = LoadModel( WallSlabModel ).Bounds;
		var rest = edgeMid + rot.Forward * (WallHalf - slab.Maxs.x + SlabProud) + Vector3.Up * surface;
		var root = NewTrapRoot( p.Name, rest, rot.Yaw() );
		var mesh = Spawn( WallSlabModel, rest, rot.Yaw(), $"{p.Name} slab", ColliderKind.None, isStatic: false, themed: false );
		mesh.SetParent( root, true );
		var trap = root.Components.Create<TrapWallSlab>();
		trap.Slab = mesh;
		trap.Along = WorldTransform.NormalToWorld( rot.Forward ).Normal;
		trap.Travel = (Cell - 2f * WallHalf) - SlabProud - 1f;
		return trap;
	}
}
