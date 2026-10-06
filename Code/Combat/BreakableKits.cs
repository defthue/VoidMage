using System;
using System.Collections.Generic;

namespace VoidMage.Combat;

/// <summary>
/// What a breakable model turns into. The piece lists come from tools/gen_breakable_kits.py (the generated half of
/// this class): crates and barrels are cut out of their own mesh and spawn at the prop's transform, so the break is
/// seamless; loose extras (the pack's planks, a broken barrel's bent staves and rings, the vase shards) spawn at
/// random spots inside the prop. Rings are rationed (a barrel throws 1-3 of its hoops and ring bits, not all of
/// them), a vase leaves its broken base standing, and a crate that has no in-place pieces (the one-shell wide
/// crate) breaks into planks only.
/// </summary>
public static partial class BreakableKits
{
	/// <summary>One debris model, its bounding size in units (0 when unknown: read the model's bounds) and its kind
	/// (ring pieces are rationed per break; planks and shards are the loose extras).</summary>
	public readonly record struct Piece( string Model, float X, float Y, float Z, string Kind = "part" )
	{
		public float Volume => MathF.Max( X, 1f ) * MathF.Max( Y, 1f ) * MathF.Max( Z, 1f );
		public bool IsRing => Kind == "ring";
	}

	/// <summary>What a break sounds and looks like. Stone is the secret wall's masonry; no prop kit uses it.</summary>
	public enum Stuff { Wood, Clay, Stone }

	/// <summary>
	/// InPlace pieces are authored at the prop origin (spawn at the prop's transform, largest first); LooseMin..LooseMax
	/// of the non-ring Loose ones are added at jittered spots inside the prop's bounds at LooseScale; of every ring
	/// piece (in place or loose) only RingMin..RingMax spawn. Remnant is a model left where the prop stood (the vase
	/// base), null when nothing stays.
	/// </summary>
	public sealed record Kit( Stuff Stuff, Piece[] InPlace, Piece[] Loose, int LooseMin, int LooseMax, int RingMin, int RingMax, float LooseScale, string Remnant, int Hits = 1 );

	/// <summary>Light sword hits the big furniture (tables, beds, bookcases, benches, coffins, the rack) takes; a heavy or a fireball breaks it in one.</summary>
	[ConVar( "break_furniture_hits" )] public static int FurnitureHits { get; set; } = 2;

	/// <summary>The pack's planks are 2.4 m long: scaled to about a crate's width when they fly out of one.</summary>
	[ConVar( "debris_plank_scale" )] public static float PlankScale { get; set; } = 0.5f;

	private static readonly Dictionary<string, Kit> _cache = new();

	/// <summary>The kit for a model path (dungeonpack/sm_prop_crate_wood_02.vmdl); false for anything that does not break.</summary>
	public static bool TryGet( string model, out Kit kit )
	{
		if ( string.IsNullOrEmpty( model ) ) { kit = null; return false; }
		if ( _cache.TryGetValue( model, out kit ) ) return kit != null;
		kit = Build( model );
		_cache[model] = kit;
		return kit != null;
	}

	/// <summary>A thrown potion bottle: nothing in place, a few of the vase shards scaled down to glass bits, no remnant.</summary>
	public static Kit GlassBottle( int min, int max, float scale )
	{
		if ( !Loose.TryGetValue( "vase_shards", out var shards ) || shards.Length == 0 ) return null;
		min = Math.Max( 0, min );
		max = Math.Max( min, max );
		if ( _glass is null || _glass.LooseMin != min || _glass.LooseMax != max || MathF.Abs( _glass.LooseScale - scale ) > 0.001f )
			_glass = new Kit( Stuff.Clay, Array.Empty<Piece>(), shards, min, max, 0, 0, scale, null );
		return _glass;
	}

	private static Kit _glass;

	private static Kit Build( string model )
	{
		if ( InPlace.TryGetValue( model, out var pieces ) )
		{
			if ( model.Contains( "barrel", StringComparison.Ordinal ) )
			{
				// its own staves, lids and uprights, 1-3 rings (1-2 on the damaged barrel) out of the hoops and the broken
				// barrel's ring bits, plus 1-2 of that barrel's bent staves
				var bits = Loose.TryGetValue( "barrel_bits", out var b ) ? b : Array.Empty<Piece>();
				var damaged = model.Contains( "broken", StringComparison.Ordinal );
				return new Kit( Stuff.Wood, pieces, bits, 1, 2, 1, damaged ? 2 : 3, 1f, null );
			}
			if ( model.Contains( "crate", StringComparison.Ordinal ) )
			{
				// a crate: its beams and boards plus a few of the pack's planks; the one-shell crate is planks only
				var planks = Loose.TryGetValue( "planks", out var p ) ? p : Array.Empty<Piece>();
				var planksOnly = pieces.Length == 0;
				return new Kit( Stuff.Wood, pieces, planks, planksOnly ? 8 : 3, planksOnly ? 10 : 5, 0, 0, PlankScale, null );
			}
			// furniture: its own parts; stools and lids go in one hit, the rest take FurnitureHits. A bookcase's books
			// mostly merged into its shelves under the tool's piece cap, so it throws loose books on top (3 singles to
			// 1 pile in the list, so most of what flies is single books).
			// the gaol's boarded-up doorways come down a panel per hit as well
			// CHESTS go in one hit too (user, 2026-09-17): they are the floor's loot, and hitting every one of them
			// twice was "very annoying" — the shudder is a tell for furniture, not for something you came to open.
			var oneHit = model.Contains( "stool", StringComparison.Ordinal ) || model.Contains( "lid", StringComparison.Ordinal )
				|| model.Contains( "boarded", StringComparison.Ordinal ) || model.Contains( "chest", StringComparison.Ordinal );
			var hits = oneHit ? 1 : Math.Max( 1, FurnitureHits );
			if ( model.Contains( "bookcase", StringComparison.Ordinal ) )
			{
				var books = Loose.TryGetValue( "books", out var bk ) ? bk : Array.Empty<Piece>();
				return new Kit( Stuff.Wood, pieces, books, 5, 9, 0, 0, 1f, null, hits );
			}
			return new Kit( Stuff.Wood, pieces, Array.Empty<Piece>(), 0, 0, 0, 0, 1f, null, hits );
		}
		if ( model.StartsWith( "dungeonpack/sm_prop_vase_", StringComparison.Ordinal )
			&& !model.Contains( "broken", StringComparison.Ordinal )
			&& !model.Contains( "shard", StringComparison.Ordinal )
			&& !model.Contains( "group", StringComparison.Ordinal ) )
		{
			var shards = Loose.TryGetValue( "vase_shards", out var s ) ? s : Array.Empty<Piece>();
			return new Kit( Stuff.Clay, Array.Empty<Piece>(), shards, 5, 8, 0, 0, 1f, "dungeonpack/sm_prop_vase_broken_01.vmdl" );
		}
		// The forge-hold's pots (Dungeon Realms) join the vase rotation: solid single meshes with nothing to cut
		// in place, so they need no Blender kit at all — the same clay shards fly and the same broken base stays
		// (user: "they're quite similar to vases, leave a broken vase behind too").
		if ( model.StartsWith( "dungeonrealms/sm_prop_camp_pot_", StringComparison.Ordinal ) )
		{
			var shards = Loose.TryGetValue( "vase_shards", out var s ) ? s : Array.Empty<Piece>();
			return new Kit( Stuff.Clay, Array.Empty<Piece>(), shards, 5, 8, 0, 0, 1f, "dungeonpack/sm_prop_vase_broken_01.vmdl" );
		}
		// the gaol's urn is pottery too (user: it breaks like the rest and leaves the broken vase behind)
		if ( model == "darkfortress/sm_prop_urn_02.vmdl" )
		{
			var shards = Loose.TryGetValue( "vase_shards", out var s ) ? s : Array.Empty<Piece>();
			return new Kit( Stuff.Clay, Array.Empty<Piece>(), shards, 5, 8, 0, 0, 1f, "dungeonpack/sm_prop_vase_broken_01.vmdl" );
		}
		return null;
	}
}
