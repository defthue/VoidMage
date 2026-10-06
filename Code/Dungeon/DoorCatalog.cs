using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>
/// Which door leaf hangs in which doorway, measured from the FBX rather than guessed
/// (tools/fbx_pivots.py over the vertices: opening = the widest vertex gap at mid height,
/// head = the lowest geometry that spans it). Everything here is in FBX centimetres and
/// converted with the vmdl import_scale, so the numbers can be checked against the meshes.
///
/// The fit is exact because these are Synty's own pairings. DungeonBuilder rolls one index
/// 0..4 and places DoorFrameModels[i] (the wall with the hole) plus DoorInnerFrameModels[i]
/// (the jambs and lintel) — and it is the INNER frame that sets the real opening: door_frame_02
/// narrows a 270 cm wall hole to 223.5 cm. The evidence, per index:
///   0  door_frame_01        opening 132.5, lintel 218.2 flat   -> door_03/04/05 (217-218.4, flat) and door_bars_01 (217.0)
///   1  door_frame_round_01  opening 128.4, arch crown ~245     -> door_01/02 (245.6, arched: the silhouette peaks mid-leaf)
///   2  door_frame_02        opening 223.5, lintel 265.0 flat   -> doordouble_flat_01+02 (265.1, flat) — exact
///   3  door_frame_02        same frame, so the same pair
///   4  door_frame_round_02  opening 263.7, arch crown ~270     -> doordouble_round_01+02 (271, arched)
/// door_01/02 are 27 cm too tall for the flat frame and must never land on index 0.
/// sm_prop_door_metal_* is deliberately unused.
/// </summary>
public static class DoorCatalog
{
	private const string P = "dungeonpack/";
	/// <summary>The import_scale in every one of these vmdls: FBX centimetres to s&amp;box units.</summary>
	public const float CM = 0.3937f;

	/// <summary>
	/// One leaf. <paramref name="WidthCm"/> is the hinge-to-far-edge reach for a hinge-pivoted mesh and the
	/// full width for a centre-pivoted one. <paramref name="Extends"/> says which way the body runs from the
	/// mesh origin (+1 = local +Y, -1 = local -Y), which is what decides whether the hinge object needs a
	/// 180 degree base yaw to point the leaf at the doorway centre.
	/// </summary>
	public sealed record DoorLeaf( string Model, float WidthCm, int Extends, bool CentrePivot )
	{
		public float Width => WidthCm * CM;

		/// <summary>
		/// The hinge's resting yaw for a leaf hung on jamb <paramref name="side"/> (-1 min-Y, +1 max-Y).
		///
		/// A hinge-pivoted mesh runs one way from its origin, so it gets turned round when that is not the way
		/// it has to reach. A centre-pivoted one never does: doordouble_flat_01/02 are already Synty's mirrored
		/// left/right pair, and a 180 degree yaw is a rotation, not a mirror — it would un-mirror the second
		/// leaf, showing its back face and putting its hinge stile at the middle of the doorway next to the
		/// first leaf's latch. Those two only need placing in their own half, which is what Offset does.
		/// </summary>
		public float BaseYaw( int side ) => CentrePivot ? 0f : (Extends == -side ? 0f : 180f);

		/// <summary>How far along the hinge's own +Y the mesh sits so its hinge edge lands on the hinge line.</summary>
		public float Offset( int side ) => CentrePivot ? -side * Width * 0.5f : 0f;
	}

	/// <summary>The clear opening of one inner frame and every leaf set that fits it. <paramref name="Boards"/> is
	/// where breakable plank panels go when the doorway is boarded up instead (the gaol): metres along the
	/// opening from its centre and up from the floor, one panel each; null = this doorway is never boarded.</summary>
	public sealed record DoorFit( float OpeningCm, float CentreCm, DoorLeaf[][] Options, (float dy, float dz)[] Boards = null )
	{
		/// <summary>Half the clear opening, in units.</summary>
		public float HalfOpening => OpeningCm * CM * 0.5f;
		/// <summary>The opening's centre along the frame's local Y, in units (the frames are not quite symmetric).</summary>
		public float Centre => CentreCm * CM;
	}

	// Measured pivots. Contrary to the "base" label in tools/DungeonPack_index.md, most leaves already have
	// the hinge baked in at FBX x = 0; only the flat double pair is centred on its origin.
	//   door_01..05, door_bars_01     x ~0 .. +129   hinge at origin, body +x (the -2..-5 tail is hinge hardware)
	//   doordouble_round_01           x ~0 .. +133   hinge at origin, body +x  -> the min-side leaf
	//   doordouble_round_02           x -132 .. ~0   hinge at origin, body -x  -> the max-side leaf
	//   doordouble_flat_01/02         x -57..+57     centre-pivoted
	// The round pair's arch confirms the handedness: _01 rises from 223 at its hinge to 271 at its far edge and
	// _02 mirrors it, so hinging each at its own jamb puts both crowns at the doorway centre, which is the arch.

	private static readonly DoorLeaf Door01 = new( P + "sm_env_door_01.vmdl", 128.7f, +1, false );
	private static readonly DoorLeaf Door02 = new( P + "sm_env_door_02.vmdl", 128.7f, +1, false );
	private static readonly DoorLeaf Door03 = new( P + "sm_env_door_03.vmdl", 128.3f, +1, false );
	private static readonly DoorLeaf Door04 = new( P + "sm_env_door_04.vmdl", 128.9f, +1, false );
	private static readonly DoorLeaf Door05 = new( P + "sm_env_door_05.vmdl", 129.7f, +1, false );
	private static readonly DoorLeaf DoorBars = new( P + "sm_env_door_bars_01.vmdl", 132.2f, +1, false );
	private static readonly DoorLeaf FlatMin = new( P + "sm_env_doordouble_flat_01.vmdl", 114.5f, +1, true );
	private static readonly DoorLeaf FlatMax = new( P + "sm_env_doordouble_flat_02.vmdl", 112.4f, +1, true );
	private static readonly DoorLeaf RoundMin = new( P + "sm_env_doordouble_round_01.vmdl", 133.3f, +1, false );
	private static readonly DoorLeaf RoundMax = new( P + "sm_env_doordouble_round_02.vmdl", 132.1f, -1, false );

	/// <summary>Indexed by DungeonBuilder's doorway roll. A null entry would mean "never hang a door here".</summary>
	public static readonly DoorFit[] Fits =
	{
		// 0  sm_env_wall_doorframe_01 + sm_env_door_frame_01: flat lintel at 218.2, so only the 218-tall leaves
		new( 132.5f, -1.2f, new[] { new[] { Door03 }, new[] { Door04 }, new[] { Door05 }, new[] { DoorBars } } ),
		// 1  sm_env_wall_doorframe_round_01 + sm_env_door_frame_round_01: arched to ~245, so the 245.6-tall arched leaves
		new( 128.4f, -0.5f, new[] { new[] { Door01 }, new[] { Door02 } } ),
		// 2  sm_env_wall_doorframe_double_01 + sm_env_door_frame_02: flat lintel at 265.0
		new( 223.5f, 4.0f, new[] { new[] { FlatMin, FlatMax } } ),
		// 3  sm_env_wall_doorframe_double_02 + the same sm_env_door_frame_02, so the same pair
		new( 223.5f, 4.0f, new[] { new[] { FlatMin, FlatMax } } ),
		// 4  sm_env_wall_doorframe_double_round_01 + sm_env_door_frame_round_02: arched to ~270
		new( 263.7f, -1.5f, new[] { new[] { RoundMin, RoundMax } } ),
	};

	// ---- Dungeon Realms (the forge-hold). Openings measured 2026-09-11 by a triangle scan of the frame FBX (the
	// method reproduces door_frame_01's 132.5 above to 0.3 %). Every Realms frame is a fused 5 m wall piece whose
	// hole is centred at 250 cm, and DungeonBuilder anchors the door rig at the segment's middle, so Centre is 0.
	// The leaves hinge at y ~ 0 and run +Y like door_01, 33-40 cm thick (the walls are 66-78). A double frame
	// ships ONE leaf: the max-side copy is the same mesh turned 180 by BaseYaw, so its other face shows there.
	//   Single_01 / Single_02   opening 180.3, flat head 246.7   -> either single leaf (176.1 / 180.0 reach, 246 tall)
	//   Double_01               opening 261.6, flat lintel 295   -> Door_01 x2 (127.8 each: 6 cm of play)
	//   Double_02               opening 265.8, arched 257-326    -> Door_02 x2 (141.4 each: 8.5 cm over per side, Outset buries it)
	//   Archway_02              open arch, no leaf
	private const string RP = "dungeonrealms/";
	private static readonly DoorLeaf RSingle01 = new( RP + "sm_env_dwarf_wall_doorframe_single_01_door.vmdl", 176.1f, +1, false );
	private static readonly DoorLeaf RSingle02 = new( RP + "sm_env_dwarf_wall_doorframe_single_02_door.vmdl", 180.0f, +1, false );
	private static readonly DoorLeaf RDouble01 = new( RP + "sm_env_dwarf_wall_doorframe_double_01_door_01.vmdl", 127.8f, +1, false );
	private static readonly DoorLeaf RDouble02 = new( RP + "sm_env_dwarf_wall_doorframe_double_02_door_02.vmdl", 141.4f, +1, false );

	/// <summary>Indexed by the Realms kit's DoorFrameModels roll.</summary>
	public static readonly DoorFit[] RealmsFits =
	{
		new( 180.3f, 0f, new[] { new[] { RSingle01 }, new[] { RSingle02 } } ),
		new( 180.3f, 0f, new[] { new[] { RSingle01 }, new[] { RSingle02 } } ),
		new( 261.6f, 0.4f, new[] { new[] { RDouble01, RDouble01 } } ),
		new( 265.8f, 0f, new[] { new[] { RDouble02, RDouble02 } } ),
		null,
	};

	// ---- The gaol (POLYGON Dark Fantasy + Dark Fortress). The doorways are composites of 2.5 m panels
	// (DungeonKit.Gaol.cs), so the hole is the panel's, measured by the same triangle scan (2026-09-11):
	//   DF Base_Wall_Door_01      hole 100 x 195 at the panel's 125 -> centred at 250 of the 5 m composite
	//   Fortress Wall_Door_Double_02  hole 200 x 245 at 125          -> centred
	//   Fortress Wall_Door_Prison_01  hole 100 x 195 at 125, and the panel sits at the START of the composite,
	//                                 so the opening is 125 cm off the segment middle: Centre -125
	// Leaves all hinge at y ~ 0 and run +Y: DF Base_Door_01 / Door_02 and Fortress Door_04/05/06 are 100 x 197,
	// Fortress Door_01 99 x 249 (a pair fills the double hole with 2 cm of play), DF Base_Door_Large_01 112 x 247
	// (a pair is 24 cm over, which Outset buries in the jambs). The boards are 119 x 118 cm plank panels centred
	// on their pivot: two stacked cover a single hole (the top one runs 24 cm into the lintel, hidden), four a double.
	private const string DP = "darkfantasy/";
	private const string FP = "darkfortress/";
	private static readonly DoorLeaf GDoor01 = new( DP + "sm_bld_base_door_01.vmdl", 100.0f, +1, false );
	private static readonly DoorLeaf GDoor02 = new( DP + "sm_bld_door_02.vmdl", 100.0f, +1, false );
	private static readonly DoorLeaf GDoor04 = new( FP + "sm_bld_door_04.vmdl", 100.0f, +1, false );
	private static readonly DoorLeaf GDoor05 = new( FP + "sm_bld_door_05.vmdl", 100.0f, +1, false );
	private static readonly DoorLeaf GDoor06 = new( FP + "sm_bld_door_06.vmdl", 100.0f, +1, false );
	private static readonly DoorLeaf GTall01 = new( FP + "sm_bld_door_01.vmdl", 99.0f, +1, false );
	private static readonly DoorLeaf GLarge01 = new( DP + "sm_bld_base_door_large_01.vmdl", 112.0f, +1, false );
	private static readonly (float dy, float dz)[] SingleBoards = { (0f, 0.60f), (0f, 1.60f) };
	private static readonly (float dy, float dz)[] DoubleBoards = { (-0.5f, 0.62f), (0.5f, 0.62f), (-0.5f, 1.78f), (0.5f, 1.78f) };

	/// <summary>Indexed by the Gaol kit's DoorFrameModels roll (single, single, double, prison gate, arch, ruin).</summary>
	public static readonly DoorFit[] GaolFits =
	{
		new( 100.0f, 0f, new[] { new[] { GDoor01 }, new[] { GDoor02 }, new[] { GDoor04 }, new[] { GDoor05 }, new[] { GDoor06 } }, SingleBoards ),
		new( 100.0f, 0f, new[] { new[] { GDoor01 }, new[] { GDoor02 }, new[] { GDoor04 }, new[] { GDoor05 }, new[] { GDoor06 } }, SingleBoards ),
		new( 200.0f, 0f, new[] { new[] { GTall01, GTall01 }, new[] { GLarge01, GLarge01 } }, DoubleBoards ),
		new( 100.0f, -125.0f, new[] { new[] { GDoor06 }, new[] { GDoor05 } } ),
		null,
		null,
	};

	/// <summary>
	/// How far outside the jamb line each hinge of a pair sits. The two leaves together are a little wider than
	/// the opening (flat 89.33 vs 88.00 units, round 104.48 vs 103.82), so hanging both flush with the jambs
	/// would have them overlap where they meet. Pushing each hinge out by half the excess makes them meet on the
	/// centre line instead, and buries the hinge under a centimetre into the jamb, which is where a hinge lives.
	/// </summary>
	public static float Outset( DoorFit fit, DoorLeaf[] leaves )
	{
		if ( leaves.Length < 2 ) return 0f;
		float sum = leaves.Sum( l => l.Width );
		return MathF.Max( 0f, (sum - fit.HalfOpening * 2f) * 0.5f );
	}

	/// <summary>Every model this catalog can emit, so tools/gentest can assert the vmdls exist.</summary>
	public static IEnumerable<string> AllModels() =>
		Fits.Where( f => f != null ).SelectMany( f => f.Options ).SelectMany( o => o ).Select( l => l.Model ).Distinct();
}
