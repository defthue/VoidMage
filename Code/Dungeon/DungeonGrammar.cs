using System;
using System.Collections.Generic;
using System.Linq;
using static VoidMage.Dungeon.Blocks;

namespace VoidMage.Dungeon;

public enum Archetype { Warren, Pit, Cathedral }

public enum BeatKind { Hallway, Room, Hall, Band, Tower }

/// <summary>One step of a spine: what to place, how far up/down it sits from the previous beat.</summary>
public sealed class Beat
{
	public BeatKind Kind;
	public int Delta;               // level change to reach this beat (negative = down)
	public (int w, int d)? Size;    // null for the kind's default sizes
	public TierTag Tag;

	public Beat( BeatKind kind, int delta = 0, (int w, int d)? size = null, TierTag tag = TierTag.None )
	{
		Kind = kind; Delta = delta; Size = size; Tag = tag;
	}
}

/// <summary>
/// Path-grammar layout (dungeon_genv2.py). Each archetype emits a SPINE: beats from spawn (top) to
/// exit (bottom), each attached to the previous one through a doorway or a stair run, so the spine is
/// walkable. Short SPURS hang off spine tiers: loot, shrine, ambush, dead ends. Tiers that merely touch
/// get a solid wall; only intended links get a doorway. Everything is enclosed: walls and ceilings.
/// </summary>
public sealed class Layout
{
	public readonly Grid G = new();
	public (int lo, int hi) XR, ZR;
	public HashSet<(int x, int z)> Avoid;
	/// <summary>Intended doorways between two tiers (ordered id pair).</summary>
	public readonly HashSet<(int a, int b)> Links = new();
	public readonly List<Tier> Spine = new();
	public readonly List<(Tier a, Tier b)> StairLinks = new();
	/// <summary>Doorway edges chosen by WallOffUnlinked: the two cells the opening joins.</summary>
	public readonly List<((int x, int z, int l) a, (int x, int z, int l) b)> Doors = new();

	public Layout( (int lo, int hi) xr, (int lo, int hi) zr, HashSet<(int x, int z)> avoid = null )
	{
		XR = xr; ZR = zr; Avoid = avoid ?? new HashSet<(int, int)>();
	}

	public static (int, int) LinkKey( Tier a, Tier b ) => (Math.Min( a.Id, b.Id ), Math.Max( a.Id, b.Id ));

	public bool Inside( IEnumerable<(int x, int z)> cells )
		=> cells.All( c => XR.lo <= c.x && c.x <= XR.hi && ZR.lo <= c.z && c.z <= ZR.hi && !Avoid.Contains( c ) );

	private static readonly Dictionary<BeatKind, (int, int)[]> Sizes = new()
	{
		[BeatKind.Hallway] = new[] { (1, 3), (1, 4), (1, 5), (1, 6), (2, 5) },
		[BeatKind.Room] = new[] { (2, 2), (2, 3), (3, 2), (3, 3) },
		[BeatKind.Hall] = new[] { (4, 3), (3, 4), (4, 4), (5, 4) },
		[BeatKind.Band] = new[] { (4, 3), (5, 3), (6, 3), (5, 4) },
	};

	private static TierKind KindOf( BeatKind k ) => k switch
	{
		BeatKind.Hallway => TierKind.Corridor,
		BeatKind.Hall or BeatKind.Band => TierKind.Hall,
		_ => TierKind.Room,
	};

	/// <summary>
	/// Place `beat` adjacent to tier `src` (doorway) or via a stair run / tower (delta != 0),
	/// preferring `heading`. Returns the new tier or null.
	/// </summary>
	public Tier Attach( Tier src, Beat beat, Dir heading, DungeonRng rng, int tries = 60 )
	{
		var g = G;
		var headings = new List<Dir>();
		for ( int i = 0; i < 6; i++ ) headings.Add( heading );
		for ( int i = 0; i < 2; i++ ) headings.AddRange( heading.Turn() );
		headings.Add( heading.Opp() );
		var srcSorted = src.Sorted();
		for ( int t = 0; t < tries; t++ )
		{
			var d = rng.Choice( headings );
			int dx = d.Dx(), dz = d.Dz();
			var edge = srcSorted.Where( c => !src.Cells.Contains( (c.x + dx, c.z + dz) ) ).ToList();
			if ( edge.Count == 0 ) continue;
			var (ex, ez) = rng.Choice( edge );
			int delta = beat.Delta;
			int nl = src.Level + delta;
			if ( nl < 0 || nl > Grid.MaxLevel ) continue;
			var plan = new List<PlanStep>();
			HashSet<(int x, int z)> cells;
			Dir nd;
			if ( Math.Abs( delta ) == 4 )
			{
				var p = rng.Choice( d.Turn() );
				int low = delta > 0 ? src.Level : src.Level - 4;
				plan = PlanTower( g, (ex, ez), low, d, p );
				if ( plan == null ) continue;
				nd = rng.Choice( new[] { d.Opp(), p.Opp() } );
				var (w, dp) = rng.Choice( Sizes[BeatKind.Room] );
				cells = TierFootprintAhead( ex, ez, nd, w, dp, rng );   // includes the entry column
			}
			else
			{
				int ax, az;
				if ( delta == 0 )
				{
					ax = ex + dx; az = ez + dz; nd = d;
				}
				else if ( delta > 0 )
				{
					var res = TryStairRun( g, ex, ez, src.Level, d, delta, rng );
					if ( res == null ) continue;
					plan = res.Value.plan; var a = res.Value.arrival; ax = a.X; az = a.Z; nl = a.Level; nd = a.D;
				}
				else
				{
					var res = PlanDescend( g, ex, ez, src.Level, d, -delta, rng );
					if ( res == null ) continue;
					plan = res.Value.plan; var a = res.Value.arrival; ax = a.X; az = a.Z; nl = a.Level; nd = a.D;
				}
				var (w, dp) = beat.Size ?? rng.Choice( Sizes[beat.Kind == BeatKind.Tower ? BeatKind.Room : beat.Kind] );
				cells = TierFootprintAhead( ax, az, nd, w, dp, rng, sideShift: w > 1 );
			}
			if ( !Inside( cells ) ) continue;
			if ( !cells.All( c => g.Free( c.x, c.z, nl, CellKind.Floor ) ) ) continue;
			if ( plan.Count > 0 && cells.Overlaps( PlanCols( plan ) ) ) continue;
			CommitPlan( g, plan );
			var tier = TryPlaceTier( g, cells, nl, KindOf( beat.Kind ), beat.Tag );
			if ( tier == null ) continue;
			tier.Heading = nd;
			if ( delta == 0 ) Links.Add( LinkKey( src, tier ) );
			else StairLinks.Add( (src, tier) );
			return tier;
		}
		return null;
	}

	/// <summary>Place the first tier, then attach beats in order. headingFn(i, prevTier) -> heading.</summary>
	public bool RunSpine( List<Beat> beats, HashSet<(int x, int z)> firstCells, int firstLevel, Func<int, Tier, Dir> headingFn, DungeonRng rng )
	{
		var t = TryPlaceTier( G, firstCells, firstLevel, TierKind.Room );
		if ( t == null ) return false;
		t.Heading = headingFn( 0, null );
		Spine.Add( t );
		for ( int i = 0; i < beats.Count; i++ )
		{
			var h = headingFn( i + 1, t );
			var nt = Attach( t, beats[i], h, rng );
			if ( nt == null ) return false;
			Spine.Add( nt ); t = nt;
		}
		return true;
	}

	/// <summary>Share of hallway spurs left as a bare dead-end passage (the odd one is fine; every one was too many).</summary>
	public const double BareHallwayChance = 0.15;

	/// <summary>
	/// Short branches off spine tiers. A hallway spur is (almost) never left dangling: it has to end in a
	/// room or it is taken out again, so the dead ends that remain are rooms (loot, shrine), not corridors
	/// to nowhere. CloseLoops then gives any spur that happens to touch another tier at its level a doorway
	/// into it.
	/// </summary>
	public void AddSpurs( DungeonRng rng, (int min, int max) perTier, int depth = 2 )
	{
		var kinds = new List<BeatKind> { BeatKind.Room, BeatKind.Hallway, BeatKind.Hall };
		var kindW = new List<double> { 5, 3, 1 };
		var deltas = new List<int> { 0, 1, -1, 2, -2 };
		var deltaW = new List<double> { 5, 2, 2, 1, 1 };
		// The exit hosts nothing (user, 2026-10-06, the layout viewer showed a loot room hanging off the exit: "loot
		// rooms should have always been found before it"). The exit is the end of the floor, and the mini-boss arena will
		// sit on the spine right before it, so every pocket hangs off a beat the player passes on the way.
		foreach ( var t in Spine.Take( Spine.Count - 1 ).ToList() )
		{
			int n = rng.RandInt( perTier.min, perTier.max );
			// the exit's share of pockets goes to the two beats before it, so the floor keeps its room for enemies
			// (gentest ramp went SHORT on the Gaol Cathedral without this)
			if ( Spine.Count > 2 && (t == Spine[^2] || t == Spine[^3]) ) n += rng.RandInt( 0, perTier.max );
			for ( int i = 0; i < n; i++ )
			{
				var kind = rng.Choices( kinds, kindW );
				int delta = rng.Choices( deltas, deltaW );
				var heading = rng.Choice( new[] { t.Heading.Turn()[0], t.Heading.Turn()[1], t.Heading.Opp() } );
				Tier nt;
				if ( kind == BeatKind.Hallway && rng.NextDouble() < BareHallwayChance )
				{
					nt = Attach( t, new Beat( BeatKind.Hallway, delta ), heading, rng, tries: 25 );
					if ( nt != null ) nt.Spur = true;
				}
				else if ( kind == BeatKind.Hallway )
				{
					nt = AttachHallwayToRoom( t, heading, delta, rng );
				}
				else
				{
					nt = Attach( t, new Beat( kind, delta ), heading, rng, tries: 25 );
					if ( nt != null )
					{
						nt.Spur = true;
						if ( depth > 1 && rng.NextDouble() < 0.7 )
						{
							int d2 = rng.Choice( new[] { 0, 0, 1, -1 } );
							if ( rng.NextDouble() < 0.4 ) AttachHallwayToRoom( nt, nt.Heading, d2, rng );
							else
							{
								var nt2 = Attach( nt, new Beat( BeatKind.Room, d2 ), nt.Heading, rng, tries: 20 );
								if ( nt2 != null ) nt2.Spur = true;
							}
						}
					}
				}
				if ( nt == null ) break;
			}
		}
		CloseLoops( rng );
		// A floor always has a few dead-end pockets, however small its rooms factor (the floor-1 Pit at 0.2 rolled no
		// spurs at all, so it had no loot room and no shrine): rooms off the spine's middle beats, added AFTER the loops
		// close so they stay dead ends, until there are MinPockets of them.
		if ( Spine.Count > 3 )
		{
			var hosts = Spine.Skip( 1 ).Take( Spine.Count - 2 ).ToList();
			rng.Shuffle( hosts );
			foreach ( var t in hosts )
			{
				if ( G.Tiers.Count( x => x.Spur && x.Kind != TierKind.Corridor && LinkCount( x ) <= 1 ) >= MinPockets ) break;
				var heading = rng.Choice( new[] { t.Heading.Turn()[0], t.Heading.Turn()[1] } );
				var nt = Attach( t, new Beat( BeatKind.Room, rng.Choice( new[] { 0, 0, 1, -1 } ) ), heading, rng, tries: 25 );
				if ( nt != null ) nt.Spur = true;
			}
		}
	}

	/// <summary>Fewest pocket rooms on a floor: two loot rooms and a shrine need somewhere to be.</summary>
	public const int MinPockets = 3;

	/// <summary>
	/// A flat hallway off `src` that ends in a room; the room takes the level change (a flat hallway leaves
	/// no stairs behind if it has to go). Returns the room, or null with the hallway removed again.
	/// </summary>
	private Tier AttachHallwayToRoom( Tier src, Dir heading, int roomDelta, DungeonRng rng )
	{
		var hall = Attach( src, new Beat( BeatKind.Hallway ), heading, rng, tries: 25 );
		if ( hall == null ) return null;
		hall.Spur = true;
		var room = Attach( hall, new Beat( BeatKind.Room, roomDelta ), hall.Heading, rng, tries: 25 );
		if ( room == null && roomDelta != 0 ) room = Attach( hall, new Beat( BeatKind.Room ), hall.Heading, rng, tries: 15 );
		if ( room == null )
		{
			RemoveTier( hall );
			return null;
		}
		room.Spur = true;
		return room;
	}

	/// <summary>Takes a flat tier out again: its cells, its claims and its links. Never for a tier a stair run leads to.</summary>
	private void RemoveTier( Tier t )
	{
		foreach ( var c in t.Cells ) G.RemoveFloor( c.x, c.z, t.Level );
		G.Tiers.Remove( t );
		Links.RemoveWhere( k => k.a == t.Id || k.b == t.Id );
		StairLinks.RemoveAll( s => s.a == t || s.b == t );
	}

	/// <summary>Intended connections of a tier: doorways plus stair runs (the grid's neighbour graph has no walls yet at this stage).</summary>
	private int LinkCount( Tier t )
		=> Links.Count( k => k.a == t.Id || k.b == t.Id ) + StairLinks.Count( s => s.a == t || s.b == t );

	/// <summary>
	/// A spur with a single link that touches another tier at its own level gets a doorway into it: every
	/// dead-end corridor that can become a through-corridor does, and a share of the dead-end rooms become
	/// pass-through rooms (the rest stay dead ends for loot and shrines). Spawn and exit are never joined
	/// this way, so a spur can not shortcut the spine's two ends.
	/// </summary>
	public void CloseLoops( DungeonRng rng, double roomChance = 0.35 )
	{
		var g = G;
		var ends = new HashSet<Tier> { Spine[0], Spine[^1] };
		foreach ( var t in g.Tiers.Where( t => t.Spur ).OrderBy( t => t.Id ).ToList() )
		{
			if ( LinkCount( t ) > 1 ) continue;
			if ( t.Kind != TierKind.Corridor && rng.NextDouble() >= roomChance ) continue;
			var touching = new List<Tier>();
			foreach ( var c in t.Sorted() )
				foreach ( var d in Dirs.All )
				{
					if ( !g.Cells.TryGetValue( (c.x + d.Dx(), c.z + d.Dz(), t.Level), out var nc ) ) continue;
					if ( nc.Kind != CellKind.Floor || nc.Tier == null || nc.Tier == t ) continue;
					if ( ends.Contains( nc.Tier ) || Links.Contains( LinkKey( t, nc.Tier ) ) ) continue;
					if ( !touching.Contains( nc.Tier ) ) touching.Add( nc.Tier );
				}
			if ( touching.Count == 0 ) continue;
			Links.Add( LinkKey( t, rng.Choice( touching ) ) );
		}
	}

	/// <summary>Where two tiers touch: a doorway if linked, solid wall otherwise.</summary>
	public void WallOffUnlinked( DungeonRng rng )
	{
		var g = G;
		var order = new List<(int, int)>();
		var shared = new Dictionary<(int, int), List<((int x, int z, int l), (int x, int z, int l))>>();
		foreach ( var key in g.SortedKeys() )
		{
			var c = g.Cells[key];
			if ( c.Kind != CellKind.Floor || c.Tier == null ) continue;
			foreach ( var (dx, dz) in new[] { (1, 0), (0, 1) } )
			{
				var n = (key.x + dx, key.z + dz, key.l);
				if ( g.Cells.TryGetValue( n, out var nc ) && nc.Kind == CellKind.Floor && nc.Tier != null && nc.Tier != c.Tier )
				{
					var pair = LinkKey( c.Tier, nc.Tier );
					if ( !shared.TryGetValue( pair, out var list ) ) { list = new(); shared[pair] = list; order.Add( pair ); }
					list.Add( (key, n) );
				}
			}
		}
		Doors.Clear();
		foreach ( var pair in order )
		{
			var edges = shared[pair];
			if ( Links.Contains( pair ) )
			{
				var door = rng.Choice( edges );
				Doors.Add( door );
				foreach ( var e in edges )
					if ( e != door ) g.IWalls.Add( Grid.Edge( e.Item1, e.Item2 ) );
			}
			else
			{
				foreach ( var e in edges ) g.IWalls.Add( Grid.Edge( e.Item1, e.Item2 ) );
			}
		}
	}

	/// <summary>Enclosed tiers get a ceiling two levels up (wall height) unless something already sits there.</summary>
	public List<(int x, int z, int l)> Ceilings()
	{
		var outList = new List<(int, int, int)>();
		foreach ( var key in G.SortedKeys() )
		{
			var (x, z, l) = key;
			if ( G.Cells[key].Kind == CellKind.Stair ) continue;
			if ( G.Cells.ContainsKey( (x, z, l + 2) ) || G.Cells.ContainsKey( (x, z, l + 1) ) ) continue;
			outList.Add( (x, z, l + 2) );
		}
		return outList;
	}

	/// <summary>
	/// Which spine beat each tier hangs off: a spine tier is its own beat, a spur takes the beat of the nearest spine
	/// tier through the intended links. Placement rules read a pocket's position along the floor from this.
	/// </summary>
	private Dictionary<int, int> RootBeats()
	{
		var root = new Dictionary<int, int>();
		var q = new Queue<Tier>();
		for ( int i = 0; i < Spine.Count; i++ ) { root[Spine[i].Id] = i; q.Enqueue( Spine[i] ); }
		var byId = G.Tiers.ToDictionary( t => t.Id );
		var adj = new Dictionary<int, List<int>>();
		void Add( int a, int b ) { if ( !adj.TryGetValue( a, out var l ) ) adj[a] = l = new List<int>(); l.Add( b ); }
		foreach ( var k in Links.OrderBy( k => k ) ) { Add( k.a, k.b ); Add( k.b, k.a ); }
		foreach ( var s in StairLinks ) { Add( s.a.Id, s.b.Id ); Add( s.b.Id, s.a.Id ); }
		while ( q.Count > 0 )
		{
			var t = q.Dequeue();
			if ( !adj.TryGetValue( t.Id, out var ns ) ) continue;
			foreach ( var n in ns.OrderBy( n => n ) )
			{
				if ( root.ContainsKey( n ) || !byId.TryGetValue( n, out var nt ) ) continue;
				root[n] = root[t.Id];
				q.Enqueue( nt );
			}
		}
		return root;
	}

	/// <summary>
	/// Spawn at the top of the spine, exit at its end. The typed pockets go by WHERE they hang off the spine (user,
	/// 2026-10-06: placements that make sense without becoming readable): a loot room in each half of the floor, never
	/// off the spawn or the beat after it, never past the exit (the beat right before the exit may host one); the
	/// shrine past the midpoint, where mana actually runs low, off a beat the loot does not use; the ambush mid-spine
	/// and never on the beat right before the exit, so the arena is not two fights. It used to put the loot in the two
	/// dead ends FARTHEST from spawn, which is how a loot room ended up hanging off the exit.
	/// </summary>
	public void TagSpecial( DungeonRng rng )
	{
		var g = G;
		int n = Spine.Count;
		Spine[0].Tag = TierTag.Spawn; Spine[^1].Tag = TierTag.Exit;
		var root = RootBeats();
		int Beat( Tier t ) => root.TryGetValue( t.Id, out var b ) ? b : -1;
		var dead = g.Tiers.Where( t => t.Spur && t.Tag == TierTag.None && NLinks( g, t ) <= 1 ).ToList();
		rng.Shuffle( dead );
		// rooms first; a bare dead-end hallway only when nothing else is left
		dead = dead.Where( t => t.Kind != TierKind.Corridor ).Concat( dead.Where( t => t.Kind == TierKind.Corridor ) ).ToList();
		int last = n - 2;
		var loot = new List<Tier>();
		var early = dead.FirstOrDefault( t => Beat( t ) >= 2 && Beat( t ) <= n / 2 );
		var late = dead.FirstOrDefault( t => Beat( t ) > n / 2 && Beat( t ) <= last );
		if ( early != null ) loot.Add( early );
		if ( late != null ) loot.Add( late );
		foreach ( var t in dead.Where( t => !loot.Contains( t ) && Beat( t ) >= 2 && Beat( t ) <= last ) ) { if ( loot.Count >= 2 ) break; loot.Add( t ); }
		foreach ( var t in dead.Where( t => !loot.Contains( t ) && Beat( t ) >= 1 && Beat( t ) <= last ) ) { if ( loot.Count >= 2 ) break; loot.Add( t ); }
		// a small floor (the Pit at rooms 0.2 has almost no pockets) still needs its loot: any dead end, then any
		// spur room off the spine's middle, even a pass-through one
		foreach ( var t in dead.Where( t => !loot.Contains( t ) ) ) { if ( loot.Count >= 2 ) break; loot.Add( t ); }
		foreach ( var t in g.Tiers.Where( t => t.Spur && t.Tag == TierTag.None && t.Kind != TierKind.Corridor && !loot.Contains( t ) && Beat( t ) >= 1 && Beat( t ) <= last ).OrderBy( t => t.Id ) ) { if ( loot.Count >= 2 ) break; loot.Add( t ); }
		foreach ( var t in loot ) t.Tag = TierTag.Loot;
		var lootBeats = loot.Select( Beat ).ToHashSet();
		var shrine = dead.FirstOrDefault( t => t.Tag == TierTag.None && Beat( t ) > n / 2 && Beat( t ) <= last && !lootBeats.Contains( Beat( t ) ) )
			?? dead.FirstOrDefault( t => t.Tag == TierTag.None && Beat( t ) >= 1 && Beat( t ) <= last )
			?? dead.FirstOrDefault( t => t.Tag == TierTag.None );
		if ( shrine != null ) shrine.Tag = TierTag.Shrine;
		if ( !g.Tiers.Any( t => t.Tag == TierTag.Ambush ) )
		{
			var mid = Spine.Skip( 1 ).Take( Math.Max( 0, n - 3 ) ).Where( t => t.Kind != TierKind.Corridor ).ToList();
			if ( mid.Count > 0 ) mid[mid.Count / 2].Tag = TierTag.Ambush;
		}
	}

	public static int NLinks( Grid g, Tier t )
	{
		var links = new HashSet<string>();
		foreach ( var c in t.Cells )
			foreach ( var n in g.Neighbours( (c.x, c.z, t.Level) ) )
			{
				var nc = g.Cells[n];
				if ( nc.Tier != t ) links.Add( nc.Tier != null ? "t" + nc.Tier.Id : $"s{n.x},{n.z},{n.l}" );
			}
		return links.Count;
	}
}

/// <summary>The three floor styles as spines. Each returns a Layout or null (retry with a new sub-seed).</summary>
public static class Grammar
{
	/// <param name="size">Scales the layout's bounding box (sqrt: 1.5 is the tuned default).</param>
	/// <param name="rooms">Scales how MANY tiers a floor has, independently of the box: the Warren's spine gains or
	/// loses (hallway, room) pairs at its tail and every archetype rolls proportionally more or fewer spurs. 1 is
	/// the classic floor; the run ramps this from 0.6 on floor 1 to 1.6 on floor 12.</param>
	public static Layout Generate( Archetype a, DungeonRng rng, double size, double rooms = 1.0 ) => a switch
	{
		Archetype.Warren => Warren( rng, size, rooms ),
		Archetype.Pit => Pit( rng, size, rooms ),
		Archetype.Cathedral => Cathedral( rng, size, rooms ),
		_ => null,
	};

	/// <summary>How many (hallway, room) pairs the rooms factor adds (positive, 6 per whole unit) or drops (negative,
	/// 8 per unit: the small floors are the first three of a run and have to read as small).</summary>
	private static int PairDelta( double rooms ) => (int)Math.Round( (rooms - 1.0) * (rooms < 1.0 ? 8.0 : 6.0) );

	/// <summary>The spur roll scaled by the rooms factor: the upper bound scales, and below 1 a spine tier may get
	/// no spur at all (the spurs are most of a floor's tiers, so a small floor needs fewer of them, not just fewer beats).</summary>
	private static (int min, int max) Spurs( (int min, int max) perTier, double rooms )
	{
		int min = rooms < 1.0 ? 0 : perTier.min;
		return (min, Math.Max( min, (int)Math.Round( perTier.max * rooms ) ));
	}

	/// <summary>Long rectangle. Spine snakes lengthways: hallways and rooms, a few one-level stairs, one stair tower dropping 4 levels.</summary>
	public static Layout Warren( DungeonRng rng, double size, double rooms = 1.0 )
	{
		int sx = (int)(22 * Math.Sqrt( size )), sz = (int)(10 * Math.Sqrt( size ));
		var L = new Layout( (0, sx), (MathUtil.FloorDiv( -sz, 2 ), MathUtil.FloorDiv( sz, 2 )) );
		var beats = new List<Beat>
		{
			new( BeatKind.Hallway ), new( BeatKind.Room ), new( BeatKind.Hallway, -1 ), new( BeatKind.Room ), new( BeatKind.Hallway ),
			new( BeatKind.Hall, tag: TierTag.Ambush ), new( BeatKind.Hallway, -1 ), new( BeatKind.Room ), new( BeatKind.Hallway ), new( BeatKind.Room, -1 ),
			new( BeatKind.Hallway ), new( BeatKind.Hall ), new( BeatKind.Hallway ),
			new( BeatKind.Tower, -4 ),
			new( BeatKind.Hallway ), new( BeatKind.Room ), new( BeatKind.Hallway ), new( BeatKind.Room ), new( BeatKind.Hallway, -1 ),
			new( BeatKind.Hall ), new( BeatKind.Hallway ), new( BeatKind.Room ), new( BeatKind.Hallway ), new( BeatKind.Room ),
		};
		// The tail after the tower is the flexible part: a small floor drops pairs from the end (the tower and one
		// pair after it always stay, so the spine still has its drop), a big floor gains (hallway, room) pairs.
		// The exit is simply whatever tier ends the spine (TagSpecial), so a shorter tail moves it, nothing more.
		int pairs = PairDelta( rooms );
		int towerAt = beats.FindIndex( b => b.Kind == BeatKind.Tower );
		for ( ; pairs < 0 && beats.Count >= towerAt + 1 + 2 + 2; pairs++ ) beats.RemoveRange( beats.Count - 2, 2 );
		for ( ; pairs > 0; pairs-- ) { beats.Add( new Beat( BeatKind.Hallway, rng.NextDouble() < 0.3 ? -1 : 0 ) ); beats.Add( new Beat( BeatKind.Room ) ); }
		// snake: east across the box, flip when we hit the far end
		var h = Dir.E;
		Dir Heading( int i, Tier prev )
		{
			if ( prev != null )
			{
				int far = prev.Cells.Max( c => c.x ), near = prev.Cells.Min( c => c.x );
				if ( h == Dir.E && far >= sx - 3 ) h = Dir.W;
				if ( h == Dir.W && near <= 2 ) h = Dir.E;
			}
			return h;
		}
		if ( !L.RunSpine( beats, Rect( 0, -1, 3, 3 ), 8, Heading, rng ) ) return null;
		L.AddSpurs( rng, Spurs( (1, 3), rooms ), depth: 2 );
		return L;
	}

	/// <summary>
	/// Square around a central void. Spine circles the void clockwise, dropping 2 levels at each corner:
	/// bands facing the void, rooms outside. The last beat is the pit floor at level 0.
	/// </summary>
	public static Layout Pit( DungeonRng rng, double size, double rooms = 1.0 )
	{
		int v = Math.Max( 3, (int)Math.Round( 4 * Math.Sqrt( size ) ) );
		int r = Math.Max( 4, (int)Math.Round( 5 * Math.Sqrt( size ) ) );
		var L = new Layout( (-r - 1, v + r), (-r - 1, v + r), Rect( 0, 0, v, v ) );
		var order = new[] { Dir.E, Dir.S, Dir.W, Dir.N };
		var beats = new List<Beat>();
		for ( int side = 0; side < 4; side++ )
		{
			beats.AddRange( new[] { new Beat( BeatKind.Band ), new Beat( BeatKind.Room ), new Beat( BeatKind.Band ), new Beat( BeatKind.Hallway ) } );
			if ( side < 3 ) beats.Add( new Beat( BeatKind.Hallway, -2 ) );
		}
		beats.Add( new Beat( BeatKind.Hallway, -2 ) );
		beats.Add( new Beat( BeatKind.Hall, size: (v, v) ) );
		Dir Heading( int i, Tier prev ) => order[Math.Min( 3, i / 5 )];
		if ( !L.RunSpine( beats, Rect( -r, v, 3, 3 ), 8, Heading, rng ) ) return null;
		// the ring around the void is the archetype; only the spurs scale with the rooms factor
		L.AddSpurs( rng, Spurs( (1, 2), rooms ), depth: 2 );
		return L;
	}

	/// <summary>
	/// Ring of enclosed rooms and hallways around a reserved centre, descending from 8; the final stair
	/// drops into the great hall at level 0 which fills the centre, with balconies at level 2.
	/// </summary>
	public static Layout Cathedral( DungeonRng rng, double size, double rooms = 1.0 )
	{
		int hw = (int)(6 * Math.Sqrt( size )), hh = (int)(8 * Math.Sqrt( size ));
		int pad = 6;
		var L = new Layout( (-pad, hw + pad - 1), (-pad, hh + pad - 1), Rect( 0, 0, hw, hh ) );
		var order = new[] { Dir.E, Dir.S, Dir.W, Dir.N };
		var beats = new List<Beat>();
		for ( int side = 0; side < 4; side++ )
		{
			int d1 = side < 3 ? -1 : 0, d2 = side < 3 ? -1 : 0;
			beats.AddRange( new[] { new Beat( BeatKind.Hallway ), new Beat( BeatKind.Room ), new Beat( BeatKind.Hallway, d1 ), new Beat( BeatKind.Room, d2 ) } );
		}
		beats[3].Tag = TierTag.Ambush;
		Dir Heading( int i, Tier prev ) => order[Math.Min( 3, i / 4 )];
		if ( !L.RunSpine( beats, Rect( -3, hh, 3, 3 ), 8, Heading, rng ) ) return null;

		// great hall in the centre at level 0, entered by a stair from the last spine tier
		L.Avoid = new HashSet<(int, int)>();
		var g = L.G;
		var hall = TryPlaceTier( g, Rect( 0, 0, hw, hh ), 0, TierKind.Hall );
		if ( hall == null ) return null;
		hall.Heading = Dir.S;
		var last = L.Spine[^1];
		bool connected = false;
		var lastSorted = last.Sorted();
		for ( int i = 0; i < 300; i++ )
		{
			var (ex, ez) = rng.Choice( lastSorted );
			var d = rng.Choice( Dirs.All ); int dx = d.Dx(), dz = d.Dz();
			if ( last.Cells.Contains( (ex + dx, ez + dz) ) ) continue;
			var res = PlanDescend( g, ex, ez, last.Level, d, last.Level, rng );
			if ( res == null ) continue;
			var (plan, arr) = res.Value;
			if ( hall.Cells.Contains( (arr.X, arr.Z) ) )
			{
				CommitPlan( g, plan ); L.StairLinks.Add( (last, hall) ); connected = true; break;
			}
			if ( !g.Free( arr.X, arr.Z, 0, CellKind.Floor ) ) continue;
			var path = BridgeCorridor( g, (arr.X, arr.Z), 0, ( x, z ) => hall.Cells.Contains( (x, z) ), 8, rng );
			if ( path != null )
			{
				var corridor = new HashSet<(int, int)>( path ) { (arr.X, arr.Z) };
				// The path was found on the grid WITHOUT this stair run, and the short way back to the hall is
				// often straight under it. Turn that down before committing: a committed run whose corridor then
				// fails to place stays in the grid as a stair to nowhere.
				if ( corridor.Overlaps( PlanCols( plan ) ) ) continue;
				CommitPlan( g, plan );
				var hwT = TryPlaceTier( g, corridor, 0, TierKind.Corridor );
				if ( hwT == null ) continue;
				hwT.Heading = arr.D;
				L.Links.Add( Layout.LinkKey( hwT, hall ) ); L.StairLinks.Add( (last, hwT) );
				connected = true; break;
			}
		}
		if ( !connected ) return null;
		L.Spine.Add( hall );

		// balconies at level 2 over the hall's long edges, each reached by a stair from the hall
		var hallSorted = hall.Sorted();
		foreach ( var d in new[] { Dir.E, Dir.W } )
		{
			int col = d == Dir.E ? hw - 1 : 0;
			var cells = new HashSet<(int x, int z)>();
			for ( int z = 1; z < hh - 1; z++ ) cells.Add( (col, z) );
			if ( !cells.All( c => g.Free( c.x, c.z, 2, CellKind.Floor ) ) ) continue;
			var b = TryPlaceTier( g, cells, 2, TierKind.Corridor );
			if ( b == null ) continue;
			b.Heading = Dir.N;
			bool linked = false;
			for ( int i = 0; i < 100; i++ )
			{
				var (ex, ez) = rng.Choice( hallSorted );
				var hd = rng.Choice( new[] { Dir.N, Dir.S } );
				var res = TryStairRun( g, ex, ez, 0, hd, 2, rng );
				if ( res == null ) continue;
				var (plan, arr) = res.Value;
				if ( b.Cells.Contains( (arr.X, arr.Z) ) && !PlanCols( plan ).Overlaps( hall.Cells ) )
				{
					CommitPlan( g, plan ); L.StairLinks.Add( (hall, b) ); linked = true; break;
				}
			}
			if ( !linked )
			{
				foreach ( var c in b.Cells ) g.RemoveFloor( c.x, c.z, 2 );
				g.Tiers.Remove( b );
			}
		}
		AddInteriorWalls( g, hall, rng, 1 );
		L.AddSpurs( rng, Spurs( (1, 3), rooms ), depth: 2 );
		return L;
	}
}
