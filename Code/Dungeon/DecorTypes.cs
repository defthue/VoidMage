using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Dungeon;

/// <summary>What a room is dressed as. Corridors, spawn and exit are None: the global pass dresses them.</summary>
public enum DecorRole { None, Library, Armory, Crypt, MessHall, Shrine, Storage, Torture, Throne, Alchemy, Barracks, Secret }

public enum SlotKind { Wall, WallSwap, Corner, Centre, EdgeStrip, FloorSwap, Ceiling, Pillar, Scatter, Litter }

/// <summary>
/// How a prop attaches. Floor: centred pivot, base at the floor. WallFlush: back face against the wall.
/// WallMounted: pivot on the wall face at MountHeight. CeilingHung: pivot (at the prop's top) on the
/// ceiling, MountHeight metres below it. PillarMounted: on a face of an interior pillar.
/// </summary>
public enum AnchorKind { Floor, WallFlush, WallMounted, CeilingHung, PillarMounted }

/// <summary>Three things can share one wall segment: a prop standing in front of it, one mounted at eye height, one hung high.</summary>
public enum WallLayer { Band, Mount, High }

public enum LightKind { None, Torch, Candle, Brazier, Chandelier, Glow }

/// <summary>
/// One catalog entry. Extents are metres, measured from the FBX (tools/fbx_pivots.py): Width along the
/// wall / across the facing, Depth away from the wall / along the facing, Height up. Attach is an
/// optional companion mesh per variant (chest lid, candle flame) spawned with the same transform.
/// Overlay props (rugs, moss) claim no floor space. Foot names a prop placed in front of this one
/// (a chest at the foot of a bed). ExteriorOnly props sink into the wall and need nothing behind it.
/// Top is the height of a surface small items can sit on (tables, crates, coffin lids). Breakable props
/// (crates, barrels, vases) are placed by their own system, never by the static room fill. RoleOnly
/// restricts a prop to one room role wherever it is referenced (tops, companions, sets included).
/// Sink is metres the pivot is pushed below the floor surface (pebbles bedding into the flagstones);
/// TopSink the same when the prop sits on another prop's Top (candles melting into a table). Hanger
/// names a chain hung from the ceiling above a ceiling prop, which then hangs from the chain's end.
/// Nudge is metres along the prop's own forward axis applied at spawn (a hook that is off the pivot).
/// MaxHeight caps how tall the spawned model may be, in metres: the builder measures the variant's real
/// bounds and shrinks it uniformly if it is taller (the pack's trees are up to 8.2 m and would grow
/// through a 5 m ceiling; its "giant" mushrooms are waist-high trees we want knee-high). 0 = uncapped.
/// Bury is how much of the geometry BELOW the pivot stays underground, as a fraction: 1 is the pack's
/// half-bedded look (pebbles and bone piles are modelled sunk into the ground on purpose, and the moss
/// blanket's buried 80 cm is exactly what leaves a low lump), 0 stands the whole model on the floor.
/// It is a fraction of the model's own depth rather than an absolute drop because one def's variants
/// differ: sm_env_mushroom_small_01 sits on its pivot while _04 hangs 40 cm below it, and a single
/// number in metres would float one of them in the air. Only the Floor and WallFlush anchors read it.
/// Tilt pitches the model at spawn, in degrees, for the flat pieces that have to lie on a wall or a
/// ceiling rather than the floor (the root mats). MULTIPLES OF 90 ONLY — the builder re-derives the
/// prop's bounds through BBox.Rotate, which returns an axis-aligned box and so is exact only on
/// axis-aligned turns. Turn is the same about the up axis, for a model whose long side runs along its
/// local X where the catalog wants it along Y (a Realms weapon rack modelled end-on): Width and Depth are
/// given AFTER the turn, the builder rotates the bounds by it for the flush push, and the mesh is spawned
/// turned. Multiples of 90 only, for the same reason. Scale is a uniform size multiplier applied at spawn
/// (the Realms weapon rack at 1.5): Width, Depth, Height and Top are the FINAL, post-scale numbers, because
/// the decorator reserves space from them and never multiplies. Never combine it with MaxHeight, which is a
/// cap applied after and would cancel it.
/// </summary>
public sealed record PropDef(
	string Id, string[] Models, AnchorKind Anchor, float Width, float Depth, float Height, bool Blocks,
	float MountHeight = 0, string[] Attach = null, LightKind Light = LightKind.None, string[] Decay = null,
	float Embed = 0, float Lift = 0, bool ExteriorOnly = false, bool Overlay = false, string Foot = null, float Top = 0,
	bool Breakable = false, DecorRole RoleOnly = DecorRole.None, float Sink = 0, float TopSink = 0, string Hanger = null,
	float Nudge = 0, float MaxHeight = 0, float Bury = 1, float Tilt = 0, float Turn = 0, float Scale = 1 )
{
	/// <summary>Needs the whole 5 m wall segment rather than half of it.</summary>
	public bool Wide => Width > 2.4f;
	/// <summary>Occupies a whole cell (big tables, altars) rather than a quarter of one.</summary>
	public bool Tile => Width > 2.4f && Depth > 1.3f || Width > 2.6f;
	/// <summary>Narrow but reaching past the middle of the cell (beds, coffins): takes a half-cell column against its wall, so two fit side by side.</summary>
	public bool Deep => !Tile && Depth > 1.3f;
	/// <summary>Small, non-blocking debris that may lie on the walking line.</summary>
	public bool Litter => !Blocks && Width <= 1.5f && Depth <= 1.5f && Height <= 0.5f;
}

/// <summary>What makes a room recognisable: the prop(s) placed first, up to PerCell x cells (at least Min) of them.</summary>
public sealed record SignatureDef( FillEntry[] Entries, double PerCell, int Min );

/// <summary>An item of a set piece: prop id, offset forward (U) and right (V) of the anchor in cell units, yaw relative to the facing, H metres up (table tops).</summary>
public readonly record struct SetItem( string Prop, float U, float V, float Yaw, double Chance = 1, float H = 0 );

/// <summary>
/// An authored group with a footprint of W (across) x Dp (along the facing) cells around an anchor block.
/// FloorTile is stamped under every block cell; WallBehind asks for a wall on the far side of the block.
/// </summary>
public sealed record SetPiece( string Id, int W, int Dp, SetItem[] Items, string FloorTile = null, bool WallBehind = false );

/// <summary>One weighted option in a fill table. Id "" leaves the slot empty; IsSet names a set piece.</summary>
public readonly record struct FillEntry( string Id, double Weight, bool IsSet = false );

/// <summary>
/// A role's shopping list: entries per slot kind, the fraction of slots used per kind, the roll weight
/// and how depth shifts it (negative: shallow, positive: deep), an optional whole-room floor tile set,
/// and the most light-carrying props the room may hold.
/// </summary>
public sealed record FillTable(
	DecorRole Role, Dictionary<SlotKind, FillEntry[]> Entries, Dictionary<SlotKind, double> Density,
	double Weight, double DepthBias, string[] FloorTiles = null, int MaxLights = 99, SignatureDef Signature = null );

/// <summary>
/// One prop to spawn. Floor anchors carry the resolved position (GX, GZ in cell units). Wall anchors carry
/// the cell, its edge and S, the offset along the wall in cell units; the builder pushes the prop flush
/// using the model bounds. PillarMounted uses X,Z as the vertex and Edge as the face direction.
/// Height is metres (mount height, lift, or drop below the ceiling). Lit is decided by the light budget.
/// Scale is a size multiplier rolled by the placing pass (greenery grows to different sizes); the builder
/// multiplies it by whatever PropDef.MaxHeight forces. 0 is read as 1, so a default-constructed placement
/// is still full size.
/// </summary>
public readonly record struct PropPlacement(
	string Prop, string Model, string Attach, AnchorKind Anchor, int X, int Z, int Level, Dir Edge,
	float GX, float GZ, float S, float Height, float Yaw, bool Blocks, LightKind Light, int Priority, bool Lit,
	int TierId, float Embed, float Scale = 1f, bool Inside = false )
{
	/// <remarks>Inside: shut inside the prop placed at the same spot (a potion in a chest): hidden until that prop breaks.</remarks>
	public string Name => $"prop {Prop} {X},{Z},{Level} {Edge}";
}

/// <summary>
/// Replace the wall piece of one merged segment. Insert is spawned with the same transform (window bars,
/// rock plug). BackModel, when set, is a single-sided wall for the far side (the secret room case).
/// Centred flags mark models whose pivot is in the middle of the 5 m rather than at the end. Recessed
/// pieces reach into the rock behind the wall and get a floor and a ceiling there.
/// </summary>
public readonly record struct WallSwap( WallSegment Seg, string Model, string Insert, string BackModel, bool Centred, bool InsertCentred, bool MeshCollider, bool Recessed = false, float Setback = 0 );

public readonly record struct FloorSwap( int X, int Z, int Level, string Model );

/// <summary>A full wall segment between this tier's cell (X,Z, wall on side D) and another room's cell (FX,FZ, side FD) at the same level.</summary>
public readonly record struct SharedWall( WallSegment Seg, int X, int Z, Dir D, int FX, int FZ, Dir FD );

/// <summary>A placement slot in cell/edge coordinates. Corner slots carry both wall dirs (D north/south, D2 east/west).</summary>
public readonly record struct Slot( SlotKind Kind, int X, int Z, int Level, Dir D, Dir D2, int TierId, bool NearDoor, bool HasPillar );

/// <summary>Contiguous wall slots along one straight wall line, ordered along the line (the doc's edge_strip).</summary>
public sealed class WallRun
{
	public int TierId;
	public Dir D;
	public List<Slot> Slots = new();
}

/// <summary>Everything the decoration stage produced for one floor.</summary>
public sealed class DecorResult
{
	/// <summary>The fabric and catalog this floor is dressed from: every placement pass reads its props, wall swaps and
	/// banners through here rather than from DecorCatalog directly, so a Realms floor gets Realms models.</summary>
	public DungeonKit Kit = DungeonKit.Stone;
	public Dictionary<int, DecorRole> Roles = new();
	public List<PropPlacement> Props = new();
	public List<WallSwap> WallSwaps = new();
	public List<FloorSwap> FloorSwaps = new();
	public HashSet<(int x, int z, int l)> Walk = new();
	public HashSet<(int x, int z, int l)> Approach = new();
	public int BannerVariant;
	public (int x, int z, int l, Dir d)? SecretWall;
	public int SlotCount;
	public int Depth;
	/// <summary>Every set piece stamped on this floor and the tier it landed in (for the audit).</summary>
	public List<(int tierId, string set)> SetsUsed = new();
	/// <summary>The traps, in spawn order (a trap's Index is its position here on every peer).</summary>
	public List<TrapPlacement> Traps = new();
	/// <summary>Every cell a trap hurts: no room floor tile, blocking prop or skeleton spawn goes there.</summary>
	public HashSet<(int x, int z, int l)> TrapCells = new();

	public int LitCount => Props.Count( p => p.Lit );

	public string Stats()
	{
		var roles = string.Join( " ", Roles.Where( kv => kv.Value != DecorRole.None ).GroupBy( kv => kv.Value )
			.OrderBy( g => g.Key ).Select( g => $"{g.Key}x{g.Count()}" ) );
		var lights = Props.Count( p => p.Light != LightKind.None );
		return $"depth {Depth}: {SlotCount} slots, {Props.Count} props ({Props.Count( p => p.Blocks )} blocking), " +
			$"{lights} light props ({LitCount} lit), {WallSwaps.Count} wall swaps, {FloorSwaps.Count} floor swaps, " +
			$"walk {Walk.Count} approach {Approach.Count}, {Traps.Count} traps, banner {BannerVariant + 1}" +
			(SecretWall.HasValue ? $", secret wall {SecretWall.Value.x},{SecretWall.Value.z},{SecretWall.Value.l} {SecretWall.Value.d}" : ", no secret") +
			$" | {roles}";
	}

	/// <summary>A stable string of every placement, for the determinism check.</summary>
	public string Signature()
	{
		// plain string ops only: keeps the engine-side compile free of anything outside s&box's whitelist
		var parts = new List<string>( Props.Count + WallSwaps.Count + FloorSwaps.Count );
		foreach ( var p in Props )
			parts.Add( $"{p.Model}:{p.X},{p.Z},{p.Level},{(int)(p.GX * 1000)},{(int)(p.GZ * 1000)},{(int)p.Yaw},{(int)(p.Scale * 1000)},{p.Lit}" );
		foreach ( var w in WallSwaps ) parts.Add( $"{w.Model}:{w.Seg}" );
		foreach ( var f in FloorSwaps ) parts.Add( f.ToString() );
		foreach ( var t in Traps ) parts.Add( $"trap:{t.Kind}:{t.Index}:{t.X},{t.Z},{t.Level},{t.Facing},{t.WallX},{t.WallZ},{t.WallD},{t.Variant},{t.Extent}" );
		return string.Join( ";", parts );
	}
}
