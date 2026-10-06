using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Traps;

namespace VoidMage.Dungeon;

/// <summary>
/// The breach half of the builder: dressing the holes DungeonBreach opened. The wall is already gone from
/// the grid, so nothing was spawned in that plane and there is no WallSegment to key off — the pivot is
/// rebuilt from the breach record with the same EdgePivot/W maths BuildWalls uses.
///
/// A breach is NOT a doorway and nothing from the doorway kit goes near it: a ragged stub of masonry at each
/// end of the 5 m run, spiky vines filling the gap between them, and rubble on the threshold. The stubs are
/// the only things with a collider; you push through the vines, and they take a bite out of you for it.
/// </summary>
public sealed partial class DungeonBuilder
{
	/// <summary>Mossy only: walls to bring down per floor, rolled between these. Only a handful of walls on a
	/// floor are ever eligible (the two rooms have to touch WITHOUT already sharing a doorway), so a floor can
	/// come up short of the minimum. The log line says how many were eligible when it does.</summary>
	[Property, Group( "Decoration" ), Range( 0, 12 )] public int BreachesMin { get; set; } = 2;
	[Property, Group( "Decoration" ), Range( 0, 12 )] public int BreachesMax { get; set; } = 4;

	/// <summary>The mirrored pair of ragged wall stubs. _01 runs along its local +Y from its pivot, _02 along -Y.</summary>
	private const string BreachStubStart = "dungeonpack/sm_env_wall_broken_edge_01.vmdl";
	private const string BreachStubEnd = "dungeonpack/sm_env_wall_broken_edge_02.vmdl";
	/// <summary>Full-length hanging vine: 5.03 m below its pivot, 0.41 m above it, and reaching 1.385 m along its
	/// own +X with 0.34 m behind (tools/fbx_shape.py). The sheet of it hangs in the model's XZ plane.</summary>
	private const string BreachVineModel = "dungeonpack/sm_env_vine_spikes_03.vmdl";
	/// <summary>Metres the vine reaches above its own pivot: hang it that far under the slab or it pokes through.</summary>
	private const float BreachVineTop = 0.41f;
	/// <summary>Metres it reaches along its own +X, which the quarter turn lays along the wall.</summary>
	private const float BreachVineReach = 1.385f;
	/// <summary>The model is drawn to hang flat against a wall, so the pair is turned a quarter each way into the
	/// wall plane rather than left facing out of it (user, 2026-09-11), and cut to 80%.</summary>
	private const float BreachVineScale = 0.8f;
	/// <summary>Metres of daylight left between the two strands' inner tips: enough to read as a way through.</summary>
	private const float BreachVineOpening = 0.3f;
	/// <summary>Metres the model's sheet sits off its own pivot across the wall plane; corrected out so the
	/// strand straddles the wall rather than leaning out of one face of it.</summary>
	private const float BreachVineSide = 0.086f;

	private static readonly string[] BreachRubble =
	{
		"dungeonpack/sm_env_brick_rubble_01.vmdl", "dungeonpack/sm_env_brick_rubble_02.vmdl",
		"dungeonpack/sm_env_brick_rubble_03.vmdl", "dungeonpack/sm_env_brick_rubble_04.vmdl",
		"dungeonpack/sm_env_brick_rubble_05.vmdl", "dungeonpack/sm_env_brick_rubble_06.vmdl",
	};
	private static readonly string[] BreachGroundVines =
	{
		"dungeonpack/sm_env_vine_spikes_01.vmdl", "dungeonpack/sm_env_vine_spikes_02.vmdl",
	};

	/// <summary>Half-depth of the hurting volume, metres either side of the wall plane: you are "in the vines"
	/// while you are pushing through them, and at a walk that is one prick per crossing.</summary>
	private const float VineBoxHalfDepth = 0.55f;

	private void BuildBreaches( DungeonLayout layout )
	{
		if ( layout.Breaches.Count == 0 ) return;
		foreach ( var b in layout.Breaches )
		{
			var (px, pz, yaw) = EdgePivot( b.D );
			var rot = Rotation.FromYaw( yaw );
			var pivot = W( b.X + px, b.Z + pz, b.Level );
			var along = rot.Left;               // the wall pieces' local +Y: the direction a segment runs
			string where = $"{b.X},{b.Z},{b.Level} {b.D}";
			float endLen = DungeonBreach.SegmentM - b.Offset - b.Gap;

			// the two stubs, each squashed along the wall so the hole between them comes out the rolled width
			if ( b.Offset >= 0.5f )
				Spawn( BreachStubStart, pivot, yaw, $"breachstub {where} a", ColliderKind.Box,
					new Vector3( 1f, b.Scale1, 1f ) );
			if ( endLen >= 0.5f )
				Spawn( BreachStubEnd, pivot + along * Cell, yaw, $"breachstub {where} b", ColliderKind.Box,
					new Vector3( 1f, b.Scale2, 1f ) );

			var rng = new Random( b.Seed );
			BreachVines( b, pivot, rot, along, yaw, where, rng );
			BreachThreshold( b, pivot, rot, along, where, rng );
			BuildVineCurtain( b, pivot, rot, along, where );
		}
		// the floor vines register their own hurt volumes later, in BuildProps, so this counts breaches only
		Log.Info( $"[Dungeon] {layout.Breaches.Count} breached walls" );
	}

	/// <summary>
	/// The curtain: one strand grown off each broken edge, drooping in toward the middle and not quite meeting.
	///
	/// The model is drawn as a sheet hanging FLAT against a wall, not as a free strand, so both are turned a
	/// quarter — one each way — to lay that sheet in the wall plane and anchor it to its own stub. Each is then
	/// placed by where its far tip should land rather than by its pivot, which is what leaves a consistent
	/// opening between them whatever width the hole rolled: their pivots end up back over the masonry.
	/// </summary>
	private void BreachVines( Breach b, Vector3 pivot, Rotation rot, Vector3 along, float yaw, string where, Random rng )
	{
		// the slab hangs CeilingDrop below the grid plane and the strand reaches BreachVineTop above its own
		// pivot, so this tucks the top of the vine just under the stone
		float hang = 2f * LevelH - CeilingDrop - BreachVineTop * BreachVineScale * M;
		float reach = BreachVineReach * BreachVineScale;
		float middle = b.Offset + b.Gap / 2f;
		var scale = new Vector3( BreachVineScale );
		for ( int i = 0; i < 2; i++ )
		{
			// -1 is the strand on the segment's near end, +1 the one on the far end
			int side = i == 0 ? -1 : 1;
			// a quarter turn each way puts the sheet in the wall plane with its reach pointing into the hole
			float turn = yaw + side * -90f;
			// the tip stops half the opening short of the middle, so the pivot sits back toward its own stub
			float alongM = middle + side * (BreachVineOpening / 2f + reach);
			var pos = pivot
				+ along * (alongM * M)
				+ rot.Forward * (side * BreachVineSide * BreachVineScale * M)
				+ Vector3.Up * hang;
			float jitter = (float)(rng.NextDouble() - 0.5) * 10f;
			Spawn( BreachVineModel, pos, turn + jitter, $"breachvine {where} {i}", ColliderKind.None, scale, themed: false );
		}
	}

	/// <summary>What the wall left behind: masonry and creeper across the threshold, both sides, no colliders.</summary>
	private void BreachThreshold( Breach b, Vector3 pivot, Rotation rot, Vector3 along, string where, Random rng )
	{
		float top = _floorTop.TryGetValue( (b.X, b.Z, b.Level), out var t ) ? t : 0f;
		int chunks = 2 + rng.Next( 3 );
		for ( int i = 0; i < chunks; i++ )
		{
			var model = BreachRubble[rng.Next( BreachRubble.Length )];
			var pos = ThresholdPoint( b, pivot, rot, along, rng, 1.1f )
				+ Vector3.Up * (top - LoadModel( model ).Bounds.Mins.z);
			Spawn( model, pos, (float)rng.NextDouble() * 360f, $"breachrubble {where} {i}", ColliderKind.None, themed: false );
		}
		// a creeper or two spilling out of the hole, so the hanging curtain reads as rooted in something
		int creepers = 1 + rng.Next( 2 );
		for ( int i = 0; i < creepers; i++ )
		{
			var model = BreachGroundVines[rng.Next( BreachGroundVines.Length )];
			var pos = ThresholdPoint( b, pivot, rot, along, rng, 0.5f ) + Vector3.Up * top;
			Spawn( model, pos, (float)rng.NextDouble() * 360f, $"breachcreeper {where} {i}", ColliderKind.None, themed: false );
		}
	}

	/// <summary>A point somewhere in the hole's mouth, `spread` metres either side of the wall plane.</summary>
	private static Vector3 ThresholdPoint( Breach b, Vector3 pivot, Rotation rot, Vector3 along, Random rng, float spread )
	{
		float alongM = b.Offset + b.Gap * (float)rng.NextDouble();
		float side = (float)(rng.NextDouble() - 0.5) * 2f * spread;
		return pivot + along * (alongM * M) + rot.Forward * (side * M);
	}

	/// <summary>
	/// The volume the vines hurt in: the width of the hole, a body's depth either side of the wall plane, and
	/// the full height of the opening. Built in the curtain's own frame — +X out of the wall, +Y along it — so
	/// it stays square to the hole whichever way the wall runs. The strands themselves are pulled back onto the
	/// stubs and leave a gap in the middle, but the whole mouth still costs you something to push through:
	/// what hurts is the hole, not the individual plants.
	/// </summary>
	private void BuildVineCurtain( Breach b, Vector3 pivot, Rotation rot, Vector3 along, string where )
	{
		float nearTop = _floorTop.TryGetValue( (b.X, b.Z, b.Level), out var t0 ) ? t0 : 0f;
		float farTop = _floorTop.TryGetValue( b.Far, out var t1 ) ? t1 : nearTop;
		float floor = MathF.Min( nearTop, farTop );

		var go = new GameObject( GameObject, true, $"breachvines {where}" ) { Flags = GameObjectFlags.NotSaved };
		go.LocalPosition = pivot;
		go.LocalRotation = rot;
		var curtain = go.Components.Create<VineCurtain>();
		curtain.Hurt = new BBox(
			new Vector3( -VineBoxHalfDepth * M, b.Offset * M, floor - 10f ),
			new Vector3( VineBoxHalfDepth * M, (b.Offset + b.Gap) * M, floor + 2f * LevelH ) );
		VineNet.Register( curtain );
	}
}
