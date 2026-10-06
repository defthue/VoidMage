using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VoidMage.Dungeon;

/// <summary>Stand-in for s&box's global Log so the generator compiles outside the engine.</summary>
public static class Log
{
	public static void Warning( string s ) => Console.WriteLine( "WARN " + s );
	public static void Info( string s ) => Console.WriteLine( s );
}

static class Program
{
	static int Main( string[] args )
	{
		// 4th arg: the theme by name (stone, mossy, realms). A word that is not one is an error, never a silent
		// fall-through to stone: a typo would otherwise produce a clean stone report labelled as something else.
		if ( !ParseTheme( args.Length > 3 ? args[3] : "stone", out var theme ) )
		{
			Console.WriteLine( $"unknown theme '{args[3]}': expected {string.Join( "|", Enum.GetNames<DungeonTheme>().Select( n => n.ToLowerInvariant() ) )}" );
			return 2;
		}
		if ( args.Length > 0 && args[0] == "audit" )
			return Audit( args.Length > 1 ? int.Parse( args[1] ) : 12, args.Length > 2 ? double.Parse( args[2], System.Globalization.CultureInfo.InvariantCulture ) : 1.5, theme );
		if ( args.Length > 0 && args[0] == "ramp" )
			return Ramp( args.Length > 1 ? int.Parse( args[1] ) : 20 );
		if ( args.Length > 0 && args[0] == "dump" )
			return Dump( args.Length > 1 ? int.Parse( args[1] ) : 2, args.Length > 2 ? args[2] : "layouts.json" );
		int seeds = args.Length > 0 ? int.Parse( args[0] ) : 8;
		double size = args.Length > 1 ? double.Parse( args[1], System.Globalization.CultureInfo.InvariantCulture ) : 1.5;
		int depth = args.Length > 2 ? int.Parse( args[2] ) : 1;
		int failures = 0;
		failures += CheckAssets();
		int secrets = 0, total = 0;
		foreach ( var a in new[] { Archetype.Warren, Archetype.Pit, Archetype.Cathedral } )
		{
			for ( int seed = 1; seed <= seeds; seed++ )
			{
				var sw = Stopwatch.StartNew();
				try
				{
					var lay = DungeonGenerator.Generate( a, seed, size, depth, theme: theme );
					// determinism: the same seed must produce the same dungeon and the same decoration
					var again = DungeonGenerator.Generate( a, seed, size, depth, theme: theme );
					bool same = lay.Grid.Cells.Count == again.Grid.Cells.Count && lay.Edges.Count == again.Edges.Count
						&& lay.Grid.SortedKeys().SequenceEqual( again.Grid.SortedKeys() )
						&& lay.Breaches.SequenceEqual( again.Breaches )
						&& lay.Decor.Signature() == again.Decor.Signature();
					// decoration must not change the layout: the undecorated grid is identical (minus the secret room)
					var bare = DungeonGenerator.Generate( a, seed, size, depth, decorate: false, theme: theme );
					var secretCells = lay.Grid.Tiers.Where( t => t.Tag == TierTag.Secret ).SelectMany( t => t.Cells.Select( c => (c.x, c.z, t.Level) ) ).ToHashSet();
					// The breach list has to match too. Breaches roll their chance LAST, after every filter, so
					// the secret room's own walls (which the Secret filter drops) never consume a random number
					// and the decorated and bare runs stay in step. This is the assertion that protects that.
					bool stable = bare.Grid.SortedKeys().SequenceEqual( lay.Grid.SortedKeys().Where( k => !secretCells.Contains( k ) ) )
						&& bare.Breaches.SequenceEqual( lay.Breaches );
					var problems = Check( lay );
					Console.WriteLine( $"{sw.ElapsedMilliseconds,5} ms  {(same ? "det" : "NONDET")} {(stable ? "stable" : "LAYOUT-CHANGED")}  " + lay.Stats() );
					Console.WriteLine( $"           decor: " + lay.Decor.Stats() );
					Console.WriteLine( $"           potions: {lay.Decor.Props.Count( p => p.Prop == DecorCatalog.PotionHealId )} heal, {lay.Decor.Props.Count( p => p.Prop == DecorCatalog.PotionManaId )} mana"
						+ $" (secret room {lay.Decor.Props.Count( p => (p.Prop == DecorCatalog.PotionHealId || p.Prop == DecorCatalog.PotionManaId) && lay.Decor.Roles.TryGetValue( p.TierId, out var r ) && r == DecorRole.Secret )}, {lay.Decor.Props.Count( p => p.Inside )} in chests, {lay.Decor.Props.Count( p => !p.Inside && (p.Prop == DecorCatalog.PotionHealId || p.Prop == DecorCatalog.PotionManaId) )} on tables)"
						+ $"; alchemy tables {AlchemyTables( lay.Decor )}"
						+ $"; chests {lay.Decor.Props.Count( p => p.Prop.Contains( "chest" ) )} (secret room {lay.Decor.Props.Count( p => p.Prop.Contains( "chest" ) && lay.Decor.Roles.TryGetValue( p.TierId, out var r ) && r == DecorRole.Secret )})" );
					Console.WriteLine( $"           traps: " + TrapSummary( lay ) );
				// branching: how many tiers of each kind end in a single link (the layout's dead ends)
				var live = lay.Grid.Tiers.Where( t => t.Cells.Count > 0 && t.Tag != TierTag.Secret ).ToList();
				var deadByKind = string.Join( " ", live.GroupBy( t => t.Kind ).OrderBy( g => g.Key )
					.Select( g => $"{g.Key}:{g.Count( t => Layout.NLinks( lay.Grid, t ) <= 1 )}/{g.Count()}" ) );
				Console.WriteLine( $"           dead ends by kind (dead/total): {deadByKind}; spurs {live.Count( t => t.Spur )}" );
					foreach ( var p in problems ) Console.WriteLine( "           PROBLEM " + p );
					if ( !same || !stable || problems.Count > 0 ) failures++;
					total++;
					if ( lay.SecretWall.HasValue ) secrets++;
				}
				catch ( Exception e )
				{
					failures++;
					Console.WriteLine( $"FAIL {a} seed {seed}: {e}" );
				}
			}
		}
		Console.WriteLine( $"secret rooms on {secrets}/{total} floors" );
		// props per room by role, over the last layout of each archetype: is a Library a library?
		foreach ( var a in new[] { Archetype.Warren, Archetype.Pit, Archetype.Cathedral } )
		{
			var lay = DungeonGenerator.Generate( a, 1, size, depth, theme: theme );
			var perRole = lay.Decor.Props.GroupBy( p => lay.Decor.Roles.TryGetValue( p.TierId, out var r ) ? r : DecorRole.None )
				.OrderBy( g => g.Key )
				.Select( g => $"{g.Key}:{(double)g.Count() / Math.Max( 1, lay.Decor.Roles.Count( kv => kv.Value == g.Key ) ):F0}/room" );
			Console.WriteLine( $"{a} seed 1 props per room: " + string.Join( "  ", perRole ) );
			foreach ( var role in new[] { DecorRole.Library, DecorRole.Barracks, DecorRole.Crypt, DecorRole.Armory, DecorRole.MessHall } )
			{
				var id = lay.Decor.Roles.Where( kv => kv.Value == role ).Select( kv => kv.Key ).FirstOrDefault();
				if ( id == 0 ) continue;
				var t = lay.Grid.Tiers.First( t => t.Id == id );
				var items = lay.Decor.Props.Where( p => p.TierId == id ).GroupBy( p => p.Prop ).OrderByDescending( g => g.Count() ).Select( g => $"{g.Key}x{g.Count()}" );
				Console.WriteLine( $"  first {role} ({t.Cells.Count} cells): " + string.Join( " ", items ) );
			}
		}
		Console.WriteLine( failures == 0 ? "ALL OK" : $"{failures} FAILURES" );
		return failures == 0 ? 0 : 1;
	}

	/// <summary>
	/// Decoration audit over many floors: per role, how full and how recognisable the rooms come out; per
	/// prop and set piece, how often it is actually used. Prints, never fails.
	/// </summary>
	/// <summary>"with potion/all" for the tables in alchemy rooms (DecorPotions guarantees one on each).</summary>
	static string AlchemyTables( DecorResult d )
	{
		int all = 0, with = 0;
		foreach ( var t in d.Props )
		{
			if ( !d.Roles.TryGetValue( t.TierId, out var role ) || role != DecorRole.Alchemy ) continue;
			if ( !d.Kit.Props.TryGetValue( t.Prop, out var def ) || def.Top <= 0f || !def.Id.Contains( "table" ) || t.Anchor != AnchorKind.Floor ) continue;
			all++;
			float reach = Math.Max( def.Width, def.Depth ) / 2f / 5f;
			if ( d.Props.Any( p => (p.Prop == DecorCatalog.PotionHealId || p.Prop == DecorCatalog.PotionManaId) && p.TierId == t.TierId
				&& Math.Abs( p.GX - t.GX ) <= reach && Math.Abs( p.GZ - t.GZ ) <= reach && p.Height > 0.1f ) ) with++;
		}
		return $"{with}/{all} with a potion";
	}

	static int Audit( int seeds, double size, DungeonTheme theme = DungeonTheme.Stone )
	{
		var roles = Enum.GetValues<DecorRole>().Where( r => r != DecorRole.None ).ToList();
		var roomsByRole = roles.ToDictionary( r => r, _ => new System.Collections.Generic.List<(int cells, int props, int sig, int litter, bool set, bool ceiling, bool lit, bool centreSet, System.Collections.Generic.Dictionary<string, int> byProp)>() );
		var propTotal = new System.Collections.Generic.Dictionary<string, int>();
		var propFloors = new System.Collections.Generic.Dictionary<string, int>();
		var setTotal = new System.Collections.Generic.Dictionary<string, int>();
		var swapTotal = new System.Collections.Generic.Dictionary<string, int>();
		int floors = 0, corridorCells = 0, corridorProps = 0, corridorTorches = 0, roleRooms = 0, tiny = 0, litTotal = 0;
		int exitWith = 0, exitElsewhere = 0;
		var exitProp = DungeonKit.For( theme ).ExitProp;
		var torchByPriority = new Dictionary<int, int>();
		foreach ( var depth in new[] { 1, 4 } )
			foreach ( var a in new[] { Archetype.Warren, Archetype.Pit, Archetype.Cathedral } )
				for ( int seed = 1; seed <= seeds; seed++ )
				{
					var lay = DungeonGenerator.Generate( a, seed, size, depth, theme: theme );
					var d = lay.Decor;
					floors++;
					litTotal += d.LitCount;
					foreach ( var p in d.Props.Where( p => p.Prop == "torch" ) ) torchByPriority[p.Priority] = torchByPriority.GetValueOrDefault( p.Priority ) + 1;
					foreach ( var g in d.Props.GroupBy( p => p.Prop ) )
					{
						propTotal[g.Key] = propTotal.GetValueOrDefault( g.Key ) + g.Count();
						propFloors[g.Key] = propFloors.GetValueOrDefault( g.Key ) + 1;
					}
					foreach ( var (tier, set) in d.SetsUsed ) setTotal[set] = setTotal.GetValueOrDefault( set ) + 1;
					// a double-faced kit's secret tell may stand on the far side's transform with nothing on this face
					foreach ( var w in d.WallSwaps ) { var m = w.Model ?? w.BackModel; swapTotal[m] = swapTotal.GetValueOrDefault( m ) + 1; }
					var byTier = d.Props.GroupBy( p => p.TierId ).ToDictionary( g => g.Key, g => g.ToList() );
					var setTiers = d.SetsUsed.Select( s => s.tierId ).ToHashSet();
					if ( exitProp != null )
					{
						// the kit's exit prop stands in the exit room and nowhere else
						var exitTier = lay.Find( TierTag.Exit );
						if ( exitTier != null && d.Props.Any( p => p.Prop == exitProp && p.TierId == exitTier.Id ) ) exitWith++;
						exitElsewhere += d.Props.Count( p => p.Prop == exitProp && (exitTier == null || p.TierId != exitTier.Id) );
					}
					foreach ( var t in lay.Grid.Tiers.Where( t => t.Cells.Count > 0 ) )
					{
						var props = byTier.TryGetValue( t.Id, out var l ) ? l : new System.Collections.Generic.List<PropPlacement>();
						var role = d.Roles.TryGetValue( t.Id, out var r ) ? r : DecorRole.None;
						if ( role == DecorRole.None )
						{
							if ( t.Kind == TierKind.Corridor ) { corridorCells += t.Cells.Count; corridorProps += props.Count; corridorTorches += props.Count( p => p.Prop == "torch" ); }
							continue;
						}
						roleRooms++;
						var table = DecorCatalog.Tables[role];
						var sigIds = table.Signature?.Entries.Select( e => e.Id ).ToHashSet() ?? new System.Collections.Generic.HashSet<string>();
						int sig = props.Count( p => sigIds.Contains( p.Prop ) );
						int litter = props.Count( p => d.Kit.Props.TryGetValue( p.Prop, out var pd ) && pd.Litter );
						bool centreSet = table.Entries.TryGetValue( SlotKind.Centre, out var ce ) && ce.Any( e => e.IsSet );
						if ( props.Count < 4 ) tiny++;
						roomsByRole[role].Add( (t.Cells.Count, props.Count, sig, litter, setTiers.Contains( t.Id ), props.Any( p => p.Anchor == AnchorKind.CeilingHung ), props.Any( p => p.Lit ), centreSet,
							props.GroupBy( p => p.Prop ).ToDictionary( g => g.Key, g => g.Count() )) );
					}
				}
		Console.WriteLine( $"audit: {floors} floors ({seeds} seeds x 3 archetypes x depths 1 and 4), {roleRooms} dressed rooms, {tiny} with fewer than 4 props" );
		Console.WriteLine( $"corridors: {corridorCells} cells, {(double)corridorProps / corridorCells:F2} props/cell, {(double)corridorTorches / corridorCells:F2} torches/cell" );
		Console.WriteLine( "torches per floor by priority (0 stair, 1 door/room key, 2 corridor, 3 room, 9 burnt out): " + string.Join( " ", torchByPriority.OrderBy( kv => kv.Key ).Select( kv => $"{kv.Key}:{(double)kv.Value / floors:F1}" ) ) + $"; lit lights per floor {(double)litTotal / floors:F1}" );
		Console.WriteLine();
		Console.WriteLine( "role         rooms  cells(min/med/max)  props/room  props/cell  sig avg  sig<min  set%  ceil%  lit%  litter/cell" );
		foreach ( var role in roles )
		{
			var rs = roomsByRole[role];
			if ( rs.Count == 0 ) { Console.WriteLine( $"{role,-12} never rolled" ); continue; }
			var table = DecorCatalog.Tables[role];
			var cells = rs.Select( x => x.cells ).OrderBy( c => c ).ToList();
			int min = table.Signature?.Min ?? 0;
			double sigAvg = rs.Average( x => x.sig );
			double below = table.Signature == null ? 0 : 100.0 * rs.Count( x => x.sig < Math.Max( min, (int)Math.Round( table.Signature.PerCell * x.cells ) ) ) / rs.Count;
			double setPct = rs.Any( x => x.centreSet ) ? 100.0 * rs.Count( x => x.set ) / rs.Count( x => x.centreSet ) : -1;
			double ceilPct = table.Entries.ContainsKey( SlotKind.Ceiling ) ? 100.0 * rs.Count( x => x.ceiling ) / rs.Count : -1;
			Console.WriteLine( $"{role,-12} {rs.Count,5}  {cells[0],3}/{cells[cells.Count / 2],3}/{cells[^1],3}          {rs.Average( x => x.props ),6:F1}     {rs.Average( x => (double)x.props / x.cells ),5:F2}    {sigAvg,5:F1}   {below,4:F0}%  {(setPct < 0 ? "  -" : $"{setPct,3:F0}"),4}  {(ceilPct < 0 ? "  -" : $"{ceilPct,3:F0}"),4}  {100.0 * rs.Count( x => x.lit ) / rs.Count,3:F0}   {rs.Average( x => (double)x.litter / x.cells ),5:F2}" );
		}
		Console.WriteLine();
		foreach ( var role in roles )
		{
			var rs = roomsByRole[role];
			if ( rs.Count == 0 ) continue;
			var agg = new System.Collections.Generic.Dictionary<string, int>();
			foreach ( var r in rs ) foreach ( var kv in r.byProp ) agg[kv.Key] = agg.GetValueOrDefault( kv.Key ) + kv.Value;
			Console.WriteLine( $"{role,-12} " + string.Join( " ", agg.OrderByDescending( kv => kv.Value ).Take( 12 ).Select( kv => $"{kv.Key}:{(double)kv.Value / rs.Count:F1}" ) ) );
		}
		Console.WriteLine();
		Console.WriteLine( "set pieces per 100 floors: " + string.Join( " ", DungeonKit.For( theme ).Sets.Keys.OrderBy( k => k ).Select( k => $"{k}:{100.0 * setTotal.GetValueOrDefault( k ) / floors:F0}" ) ) );
		Console.WriteLine( "wall swaps per 100 floors: " + string.Join( " ", swapTotal.OrderByDescending( kv => kv.Value ).Select( kv => $"{System.IO.Path.GetFileNameWithoutExtension( kv.Key )}:{100.0 * kv.Value / floors:F0}" ) ) );
		Console.WriteLine();
		// the root mats get their own line: there are several hundred of them a floor and they would swamp
		// the column the rest of the greenery is read in
		var rootIds = DecorGreenery.RootIds.ToHashSet();
		var greenIds = DecorGreenery.AllEntries.Select( e => e.Id ).Where( id => id != "" && !rootIds.Contains( id ) ).Distinct().OrderBy( id => id ).ToList();
		Console.WriteLine( "greenery per floor: " + string.Join( "  ", greenIds.Select( id => $"{id}:{(double)propTotal.GetValueOrDefault( id ) / floors:F1}" ) ) );
		Console.WriteLine( "root mats per floor: " + string.Join( "  ", DecorGreenery.RootIds.Select( id => $"{id}:{(double)propTotal.GetValueOrDefault( id ) / floors:F1}" ) )
			+ $"  (total {DecorGreenery.RootIds.Sum( id => propTotal.GetValueOrDefault( id ) ) / (double)floors:F0}/floor)" );
		// the kit's wall sigils are a handful per floor and would never make either prop-use list
		if ( DungeonKit.For( theme ).SigilProp is { } sigil )
			Console.WriteLine( $"sigils per floor: {sigil}:{(double)propTotal.GetValueOrDefault( sigil ) / floors:F1} on {(double)propFloors.GetValueOrDefault( sigil ) / floors * 100:F0}% of floors" );
		if ( DungeonKit.For( theme ).WallSwaps.TryGetValue( "bars", out var bars ) )
			Console.WriteLine( $"bars walls per floor: {(double)swapTotal.GetValueOrDefault( bars.Model ) / floors:F1}" );
		if ( DungeonKit.For( theme ).HallwayCeiling is { } hung )
			Console.WriteLine( "corridor ceilings per floor: " + string.Join( " ", hung.Where( e => e.Id != "" ).Select( e => e.Id ).Distinct().Select( id => $"{id}:{(double)propTotal.GetValueOrDefault( id ) / floors:F1}" ) ) );
		if ( exitProp != null )
			Console.WriteLine( $"exit prop {exitProp}: in the exit room on {exitWith} of {floors} floors, {exitElsewhere} elsewhere" );
		Console.WriteLine();
		var usable = DungeonKit.For( theme ).Props.Values.Where( p => !p.Breakable ).OrderBy( p => propTotal.GetValueOrDefault( p.Id ) ).ToList();
		Console.WriteLine( "prop use per floor (lowest 30): " + string.Join( " ", usable.Take( 30 ).Select( p => $"{p.Id}:{(double)propTotal.GetValueOrDefault( p.Id ) / floors:F1}" ) ) );
		Console.WriteLine( "prop use per floor (highest 15): " + string.Join( " ", usable.AsEnumerable().Reverse().Take( 15 ).Select( p => $"{p.Id}:{(double)propTotal.GetValueOrDefault( p.Id ) / floors:F1}" ) ) );
		return 0;
	}

	static bool ParseTheme( string word, out DungeonTheme theme ) => Enum.TryParse( word, ignoreCase: true, out theme );

	/// <summary>
	/// The run's floor table (RunFloors), every row over `seeds` seeds: each floor must generate (no seed may run
	/// the generator out of attempts), have an exit tier, and carry exactly one exit portal prop in that tier; the
	/// invariants of Check() hold too. Prints how big each floor comes out, so the ramp can be read as numbers.
	/// </summary>
	/// <summary>
	/// Every floor of the run table (each shape, `seeds` seeds) as one JSON file for tools/layout_viewer.html: tiers
	/// with their cells, tags, spine order and decor role, the stairs, landings and doorways. Layout review happens
	/// in 3D (user, 2026-10-06: "i find it hard to picture what multiple 2d images look like in 3d"), so this dumps
	/// the grid and the page draws it.
	/// </summary>
	static int Dump( int seeds, string path )
	{
		var floors = new List<object>();
		for ( int floor = 1; floor <= RunFloors.LastFloor; floor++ )
		foreach ( var shape in RunFloors.At( floor ).Shapes )
		{
			var row = RunFloors.With( RunFloors.At( floor ), shape );
			for ( int seed = 1; seed <= seeds; seed++ )
			{
				DungeonLayout lay;
				try { lay = DungeonGenerator.Generate( row.Archetype, seed, row.Size, floor, theme: row.Theme, rooms: row.Rooms ); }
				catch ( Exception e ) { Console.WriteLine( $"FAIL floor {floor} {row.Archetype} seed {seed}: {e.Message}" ); continue; }
				var spineIndex = new Dictionary<int, int>();
				for ( int i = 0; i < lay.Layout.Spine.Count; i++ ) spineIndex[lay.Layout.Spine[i].Id] = i;
				var tiers = lay.Grid.Tiers.Where( t => t.Cells.Count > 0 ).Select( t => new
				{
					id = t.Id, kind = t.Kind.ToString(), tag = t.Tag.ToString(), level = t.Level, spur = t.Spur,
					spine = spineIndex.TryGetValue( t.Id, out var si ) ? si : -1,
					role = lay.Decor != null && lay.Decor.Roles.TryGetValue( t.Id, out var r ) ? r.ToString() : "None",
					cells = t.Sorted().Select( c => new[] { c.x, c.z } ).ToList(),
				} ).ToList();
				var stairs = lay.Grid.Stairs.Select( s => new object[] { s.x, s.z, s.l, s.d.ToString() } ).ToList();
				var landings = lay.Grid.Cells.Where( kv => kv.Value.Kind == CellKind.Landing && kv.Value.Tier == null ).Select( kv => new[] { kv.Key.x, kv.Key.z, kv.Key.l } ).ToList();
				var doors = lay.Doors.Select( d => new[] { d.a.x, d.a.z, d.a.l, d.b.x, d.b.z, d.b.l } ).ToList();
				var chests = lay.Decor == null ? new List<int[]>() : lay.Decor.Props.Where( p => p.Prop.Contains( "chest" ) ).Select( p => new[] { (int)Math.Round( p.GX ), (int)Math.Round( p.GZ ), p.TierId } ).ToList();
				floors.Add( new
				{
					name = $"Floor {floor} {row.Theme} {row.Archetype} seed {seed}", floor, theme = row.Theme.ToString(), archetype = row.Archetype.ToString(),
					seed, size = row.Size, rooms = row.Rooms, enemies = row.Enemies, stats = lay.Stats(), tiers, stairs, landings, doors, chests,
				} );
				Console.WriteLine( lay.Stats() );
			}
		}
		var json = System.Text.Json.JsonSerializer.Serialize( new { floors } );
		System.IO.File.WriteAllText( path, json );
		Console.WriteLine( $"wrote {floors.Count} floors to {path} ({json.Length / 1024} KB)" );
		return 0;
	}

	static int Ramp( int seeds )
	{
		int failures = 0;
		Console.WriteLine( $"ramp: {RunFloors.Table.Length} floors x {RunFloors.Table[0].Shapes.Length} shapes x {seeds} seeds" );
		Console.WriteLine( "floor theme   archetype  size rooms | tiers(min/avg/max) rooms  cells(avg)  spine  attempts(max)  exit+portal" );
		for ( int floor = 1; floor <= RunFloors.LastFloor; floor++ )
		foreach ( var shape in RunFloors.At( floor ).Shapes )
		{
			var row = RunFloors.With( RunFloors.At( floor ), shape );
			var exitProp = DungeonKit.For( row.Theme ).ExitProp;
			int minT = int.MaxValue, maxT = 0, sumT = 0, sumRooms = 0, sumCells = 0, sumSpine = 0, maxAttempt = 0, portals = 0, exits = 0, problems = 0, noChest = 0, sumChests = 0, sumRoom = 0, minRoom = int.MaxValue;
			for ( int seed = 1; seed <= seeds; seed++ )
			{
				try
				{
					var lay = DungeonGenerator.Generate( row.Archetype, seed, row.Size, floor, theme: row.Theme, rooms: row.Rooms );
					var live = lay.Grid.Tiers.Where( t => t.Cells.Count > 0 && t.Tag != TierTag.Secret ).ToList();
					minT = Math.Min( minT, live.Count ); maxT = Math.Max( maxT, live.Count ); sumT += live.Count;
					sumRooms += live.Count( t => t.Kind != TierKind.Corridor );
					sumCells += lay.Grid.Cells.Count;
					sumSpine += lay.Layout.Spine.Count;
					maxAttempt = Math.Max( maxAttempt, lay.Attempt );
					var exit = lay.Find( TierTag.Exit );
					if ( exit != null ) exits++;
					int n = exitProp == null ? 0 : lay.Decor.Props.Count( p => p.Prop == exitProp && exit != null && p.TierId == exit.Id );
					if ( n == 1 ) portals++;
					// no chest outside the secret room (DecorChests): counted so a regression shows
					var secretIds = lay.Grid.Tiers.Where( t => t.Tag == TierTag.Secret ).Select( t => t.Id ).ToHashSet();
					int chests = lay.Decor.Props.Count( p => p.Anchor == AnchorKind.Floor && p.Prop.Contains( "chest", StringComparison.Ordinal ) && !secretIds.Contains( p.TierId ) );
					sumChests += chests;
					int room = EnemyRoom( lay );
					sumRoom += room; minRoom = Math.Min( minRoom, room );
					if ( chests == 0 ) noChest++;
					var probs = Check( lay );
					if ( probs.Count > 0 ) { problems++; foreach ( var p in probs ) Console.WriteLine( $"           floor {floor} seed {seed} PROBLEM {p}" ); }
				}
				catch ( Exception e )
				{
					failures++;
					Console.WriteLine( $"FAIL floor {floor} seed {seed}: {e.Message}" );
				}
			}
			int ok = seeds;
			Console.WriteLine( $"{floor,5} {row.Theme,-7} {row.Archetype,-10} {row.Size,4:F1} {row.Rooms,5:F1} | {minT,3}/{(double)sumT / ok,4:F1}/{maxT,-3}          {(double)sumRooms / ok,5:F1}  {(double)sumCells / ok,8:F0}   {(double)sumSpine / ok,5:F1}  {maxAttempt,8}        {exits}/{portals}" );
			// the builder stands a portal itself when the decor pass found no room, so a missing portal is a warning; a missing EXIT is a failure
			if ( exits < seeds ) { failures++; Console.WriteLine( $"           floor {floor}: {seeds - exits} seeds without an exit tier" ); }
			if ( portals < exits ) Console.WriteLine( $"           WARN floor {floor}: {exits - portals} seeds where the decor pass placed no exit portal (the builder's fallback covers them)" );
			Console.WriteLine( $"           chests outside the secret room: {(double)sumChests / ok:F1} avg, {noChest} seeds with none" );
			Console.WriteLine( $"           enemies {row.Enemies}: room for {(double)sumRoom / ok:F0} avg, {minRoom} on the tightest seed{(minRoom < row.Enemies ? "   <-- SHORT" : "")}" );
			// chests live in the secret room only since 2026-09-20 (the floor's scrolls are on monsters): one left outside is the bug now
			if ( sumChests > 0 ) { failures++; Console.WriteLine( $"           floor {floor}: {sumChests} chests left outside the secret room" ); }
			if ( problems > 0 ) failures++;
		}
		Console.WriteLine( failures == 0 ? "RAMP OK" : $"RAMP {failures} FAILURES" );
		return failures == 0 ? 0 : 1;
	}

	/// <summary>
	/// How many enemies a floor can stand, by DungeonBuilder.SpawnSkeletonsAsync's own rules: EnemiesPerCell (3) a cell, no spawn,
	/// secret or exit tier, nothing within SpawnClearCells (3) of the spawn or ExitClearCells (3) of the exit unless it
	/// is the ambush, no cell under a blocking floor prop or a trap. The engine's sweep check rejects a few more, so
	/// this is the ceiling, not a promise. The three numbers mirror the builder's defaults.
	/// </summary>
	static int EnemyRoom( DungeonLayout lay )
	{
		const int spawnClear = 3, exitClear = 3, perCell = 3;
		var (sx, sz) = lay.Find( TierTag.Spawn )?.Centroid() ?? (double.NaN, double.NaN);
		var (ex, ez) = lay.Find( TierTag.Exit )?.Centroid() ?? (double.NaN, double.NaN);
		var occupied = new HashSet<(int x, int z, int l)>();
		foreach ( var p in lay.Decor.Props )
			if ( p.Blocks && p.Anchor == AnchorKind.Floor ) occupied.Add( (p.X, p.Z, p.Level) );
		foreach ( var c in lay.Decor.TrapCells ) occupied.Add( c );
		int room = 0;
		foreach ( var t in lay.Grid.Tiers )
		{
			if ( t.Cells.Count == 0 || t.Tag is TierTag.Spawn or TierTag.Secret or TierTag.Exit ) continue;
			var (cx, cz) = t.Centroid();
			if ( !double.IsNaN( sx ) && Math.Abs( cx - sx ) + Math.Abs( cz - sz ) < spawnClear && t.Tag != TierTag.Ambush ) continue;
			if ( !double.IsNaN( ex ) && Math.Abs( cx - ex ) + Math.Abs( cz - ez ) < exitClear && t.Tag != TierTag.Ambush ) continue;
			room += t.Cells.Count( c => !occupied.Contains( (c.x, c.z, t.Level) ) ) * perCell;
		}
		return room;
	}

	/// <summary>Runtime model path prefix -> the asset folder its vmdls live in. A prefix not listed here is a missing model.</summary>
	static readonly Dictionary<string, string> PackDirs = new()
	{
		["dungeonpack/"] = "DungeonPack",
		["dungeonrealms/"] = "DungeonRealms",
		["darkfantasy/"] = "DarkFantasy",
		["darkfortress/"] = "DarkFortress",
	};

	/// <summary>
	/// Every model any kit can emit — its fabric, every prop in its catalog, the traps and the door leaves — must
	/// exist as a compiled vmdl in the pack folder its path prefix names. Resolved by prefix on purpose: checking
	/// the bare file name in one folder would let a wrong-prefix path pass whenever a same-named stone file exists.
	/// Also every id a role table, set piece or swap table names must resolve in every kit.
	/// </summary>
	static int CheckAssets()
	{
		var assets = System.IO.Path.GetFullPath( System.IO.Path.Combine( AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets" ) );
		if ( !System.IO.Directory.Exists( System.IO.Path.Combine( assets, "DungeonPack" ) ) ) { Console.WriteLine( "assets: skipped (no " + assets + ")" ); return 0; }
		int missing = 0, total = 0;
		foreach ( var kit in DungeonKit.All )
		{
			foreach ( var id in DecorCatalog.MissingIds( kit ).Distinct() ) { Console.WriteLine( $"UNKNOWN PROP ID {id} ({kit.Id})" ); missing++; }
			foreach ( var id in DecorCatalog.LeakedBreakables( kit ).Distinct() ) { Console.WriteLine( $"BREAKABLE IN STATIC FILL {id} ({kit.Id})" ); missing++; }
			var models = kit.AllModels().Concat( TrapCatalog.AllModels() ).Concat( DoorCatalog.AllModels() ).Distinct().OrderBy( m => m ).ToList();
			total += models.Count;
			foreach ( var m in models )
			{
				var prefix = PackDirs.Keys.FirstOrDefault( p => m.StartsWith( p, StringComparison.Ordinal ) );
				var file = prefix == null ? null : System.IO.Path.Combine( assets, PackDirs[prefix], System.IO.Path.GetFileName( m ) );
				if ( file == null || !System.IO.File.Exists( file ) ) { Console.WriteLine( $"MISSING MODEL {m} ({kit.Id})" ); missing++; }
			}
		}
		Console.WriteLine( $"assets: {total} models over {DungeonKit.All.Length} kits, {missing} missing" );
		return missing;
	}

	/// <summary>Invariants: portals on the walking line, no blocking prop on it, torches only on walls, swaps only on exclusive segments.</summary>
	static System.Collections.Generic.List<string> Check( DungeonLayout lay )
	{
		var problems = new System.Collections.Generic.List<string>();
		var d = lay.Decor;
		var g = lay.Grid;
		var kit = d.Kit;
		if ( kit != DungeonKit.For( lay.Theme ) ) problems.Add( $"decorated with kit {kit.Id} on a {lay.Theme} floor" );
		// a stair with nothing at one end walks the player off the world
		foreach ( var s in g.DanglingStairs() ) problems.Add( $"stair {s.x},{s.z},{s.l} rising {s.d} leads nowhere" );
		var wallSet = lay.Edges.Where( e => e.Kind == EdgeKind.Wall ).Select( e => (e.X, e.Z, e.Level, e.D) ).ToHashSet();
		foreach ( var p in d.Props )
		{
			if ( p.Blocks && d.Walk.Contains( (p.X, p.Z, p.Level) ) && p.Anchor == AnchorKind.Floor && !IsSmallAgainstWall( p, kit ) )
				problems.Add( $"blocking {p.Prop} on the walking line at {p.X},{p.Z},{p.Level}" );
			if ( p.Anchor is AnchorKind.WallFlush or AnchorKind.WallMounted && !wallSet.Contains( (p.X, p.Z, p.Level, p.Edge) ) )
				problems.Add( $"{p.Prop} on a non-wall edge {p.X},{p.Z},{p.Level} {p.Edge}" );
			if ( p.Anchor != AnchorKind.PillarMounted && !g.Cells.ContainsKey( (p.X, p.Z, p.Level) ) )
				problems.Add( $"{p.Prop} outside the grid at {p.X},{p.Z},{p.Level}" );
		}
		// a see-through piece on a shared wall needs a face for the far room: a second copy, or a kit whose pieces are double-faced
		foreach ( var w in d.WallSwaps )
			if ( !w.Seg.Exclusive && w.BackModel == null && !kit.DoubleFaced ) problems.Add( $"swap {w.Model} on a shared segment {w.Seg}" );
		foreach ( var t in g.Tiers.Where( t => t.Cells.Count > 0 && t.Tag != TierTag.Secret ) )
			foreach ( var c in t.Cells )
				foreach ( var n in g.Neighbours( (c.x, c.z, t.Level) ) )
					if ( g.Cells[n].Tier != t && !d.Walk.Contains( (c.x, c.z, t.Level) ) )
						problems.Add( $"portal {c.x},{c.z},{t.Level} of tier {t.Id} not on the walking line" );
		if ( lay.SecretWall.HasValue && d.WallSwaps.Count( w => w.Model == kit.SecretWallModel || w.BackModel == kit.SecretWallModel ) != 1 )
			problems.Add( "secret room without exactly one secret wall swap" );
		// windows are two-sided too and must always have a walkable, non-secret cell on both sides
		foreach ( var w in d.WallSwaps.Where( w => w.Model == kit.WallSwaps["window"].Model ) )
		{
			var (bx, bz) = Walls.CellOf( w.Seg.Line, w.Seg.D.Opp() );
			var far = g.FloorAt( bx, bz, w.Seg.K );
			var near = g.FloorAt( w.Seg.CX, w.Seg.CZ, w.Seg.K );
			if ( far == null || near == null || far.Tier == null || near.Tier == null || far.Tier.Tag == TierTag.Secret || near.Tier.Tag == TierTag.Secret )
				problems.Add( $"window at {w.Seg} without a room on both sides" );
		}
		if ( lay.SecretWall.HasValue )
		{
			var sw = lay.SecretWall.Value;
			var back = (sw.x + sw.d.Dx(), sw.z + sw.d.Dz(), sw.l, sw.d.Opp());
			foreach ( var p in d.Props )
				if ( p.Anchor is AnchorKind.WallMounted or AnchorKind.WallFlush && ((p.X, p.Z, p.Level, p.Edge) == (sw.x, sw.z, sw.l, sw.d) || (p.X, p.Z, p.Level, p.Edge) == back) )
					problems.Add( $"{p.Prop} on the secret wall" );
		}
		problems.AddRange( CheckTraps( lay ) );
		problems.AddRange( CheckBreaches( lay ) );
		problems.AddRange( CheckRoots( lay ) );
		return problems.Distinct().ToList();
	}

	/// <summary>
	/// The root mats claim nothing in Occupancy and are allowed to tangle with each other, so nothing downstream
	/// would ever notice this layer putting a mat somewhere it must not go. What it must not do: lie across a
	/// trap (whose geometry at rest says nothing about where it sweeps), share a trap's wall face or a banner's,
	/// poke a high wall mat through the ceiling, hang off a slab that is not there, or jitter its pivot out of
	/// its own cell — FloorTopFor keys on the rounded-down position, so that one silently reads the neighbour's
	/// floor height.
	/// </summary>
	static System.Collections.Generic.List<string> CheckRoots( DungeonLayout lay )
	{
		var problems = new System.Collections.Generic.List<string>();
		var d = lay.Decor;
		if ( d == null ) return problems;
		var ids = DecorGreenery.RootIds.ToHashSet();
		var slabs = lay.Ceilings.ToHashSet();
		// every face the placement pass is supposed to keep off, rebuilt here independently of it
		var blocked = new System.Collections.Generic.HashSet<(int, int, int, Dir)>();
		foreach ( var t in d.Traps ) blocked.Add( (t.WallX, t.WallZ, t.Level, t.WallD) );
		foreach ( var w in d.WallSwaps )
		{
			blocked.Add( (w.Seg.CX, w.Seg.CZ, w.Seg.K, w.Seg.D) );
			var (fx, fz) = Walls.CellOf( w.Seg.Line, w.Seg.D.Opp() );
			blocked.Add( (fx, fz, w.Seg.K, w.Seg.D.Opp()) );
		}
		foreach ( var p in d.Props )
			if ( p.Anchor == AnchorKind.WallMounted && p.Prop != "torch" && !ids.Contains( p.Prop ) )
				blocked.Add( (p.X, p.Z, p.Level, p.Edge) );
		// caps rather than uniqueness: mats are allowed to share a slot now, but not without limit — a density
		// typo has to fail the harness rather than just move a number in the audit line
		var floors = new System.Collections.Generic.Dictionary<(int, int, int, int), int>();
		var faces = new System.Collections.Generic.Dictionary<(int, int, int, Dir), int>();
		var ceils = new System.Collections.Generic.Dictionary<(int, int, int), int>();
		int nFloor = 0, nWall = 0, nCeil = 0;
		foreach ( var p in d.Props )
		{
			if ( !ids.Contains( p.Prop ) ) continue;
			var who = $"{p.Prop} {p.X},{p.Z},{p.Level}";
			switch ( p.Anchor )
			{
				case AnchorKind.Floor:
				{
					nFloor++;
					if ( d.TrapCells.Contains( (p.X, p.Z, p.Level) ) ) problems.Add( $"{who}: on a trap cell" );
					if ( (int)Math.Floor( p.GX ) != p.X || (int)Math.Floor( p.GZ ) != p.Z )
						problems.Add( $"{who}: jittered out of its own cell to {p.GX:F2},{p.GZ:F2}" );
					var k = (p.X, p.Z, p.Level, Occupancy.QuarterOf( p.GX, p.GZ, p.X, p.Z ));
					if ( (floors[k] = floors.GetValueOrDefault( k ) + 1) > 2 ) problems.Add( $"{who}: {floors[k]} mats in one quarter" );
					break;
				}
				case AnchorKind.WallMounted:
				{
					nWall++;
					if ( d.TrapCells.Contains( (p.X, p.Z, p.Level) ) ) problems.Add( $"{who}: on a trap cell" );
					if ( blocked.Contains( (p.X, p.Z, p.Level, p.Edge) ) )
						problems.Add( $"{who}: on a face already carrying something, {p.Edge}" );
					// the mat rises 0.662 m per unit of scale above its mount; the slab is 4.74 m up
					float top = p.Height + 0.662f * p.Scale;
					if ( top > 4.75f ) problems.Add( $"{who}: reaches {top:F2} m, through the ceiling" );
					// anything in the high band needs a real slab, or it hangs above the visible ceiling
					if ( p.Height > 2f && !slabs.Contains( (p.X, p.Z, p.Level + 2) ) )
						problems.Add( $"{who}: high band on a cell with no ceiling slab" );
					// at most three: one in each half of the low band, plus one in the high band
					var k = (p.X, p.Z, p.Level, p.Edge);
					if ( (faces[k] = faces.GetValueOrDefault( k ) + 1) > 3 ) problems.Add( $"{who}: {faces[k]} mats on one wall face" );
					break;
				}
				case AnchorKind.CeilingHung:
				{
					nCeil++;
					if ( !slabs.Contains( (p.X, p.Z, p.Level + 2) ) ) problems.Add( $"{who}: hung where there is no ceiling slab" );
					if ( d.TrapCells.Contains( (p.X, p.Z, p.Level) ) ) problems.Add( $"{who}: over a trap cell" );
					// GX/GZ are pushed toward a walled edge; the slab test above keys on X,Z, so a pivot that
					// left its cell would be checking the wrong ceiling
					if ( (int)Math.Floor( p.GX ) != p.X || (int)Math.Floor( p.GZ ) != p.Z )
						problems.Add( $"{who}: pushed out of its own cell to {p.GX:F2},{p.GZ:F2}" );
					var k = (p.X, p.Z, p.Level);
					if ( (ceils[k] = ceils.GetValueOrDefault( k ) + 1) > 4 ) problems.Add( $"{who}: {ceils[k]} mats on one ceiling" );
					break;
				}
			}
		}
		// a band around the tuned targets, so a density regression fails here instead of only in the audit
		if ( lay.Theme == DungeonTheme.Mossy && lay.Decor.Depth > 0 )
		{
			// 150 until 2026-10-06: the exit stopped hosting spurs, and the smallest Mossy Warren seed came out at 148
			// mats on a slightly smaller floor (the density is unchanged, the floor is)
			if ( nFloor < 140 ) problems.Add( $"only {nFloor} floor mats on the whole floor" );
			if ( nWall < 100 ) problems.Add( $"only {nWall} wall mats on the whole floor" );
			if ( nCeil < 70 ) problems.Add( $"only {nCeil} ceiling mats on the whole floor" );
		}
		return problems;
	}

	/// <summary>
	/// Breach invariants: the wall really is gone from the geometry, the hole joins two real floor cells that
	/// the walking line now runs through, it never opens the secret room or a spawn-to-exit shortcut, no two
	/// breaches share a wall plane, and the stubs still add up to a 5 m segment. Stone floors have none.
	/// </summary>
	static System.Collections.Generic.List<string> CheckBreaches( DungeonLayout lay )
	{
		var problems = new System.Collections.Generic.List<string>();
		var g = lay.Grid;
		if ( lay.Theme != DungeonTheme.Mossy && lay.Breaches.Count > 0 )
			problems.Add( $"{lay.Breaches.Count} breaches on a {lay.Theme} floor" );
		// (no connectivity assertion: the secret room is deliberately walled off, so a decorated grid is never
		// Connected(). Breaches only ever REMOVE walls, so they cannot disconnect anything anyway.)
		var lines = new System.Collections.Generic.HashSet<(WallLine, int)>();
		var edgeLines = lay.Edges.Select( e => (Walls.LineOf( e.X, e.Z, e.D ), e.Level) ).ToHashSet();
		foreach ( var b in lay.Breaches )
		{
			string who = $"breach {b.X},{b.Z},{b.Level} {b.D}";
			if ( !lines.Add( (b.Line, b.Level) ) ) problems.Add( $"{who}: second breach on the same wall plane" );
			// the wall must be gone: any edge left on this line means BuildBreaches is dressing a solid wall
			if ( edgeLines.Contains( (b.Line, b.Level) ) ) problems.Add( $"{who}: the wall is still in the edge list" );
			var near = g.FloorAt( b.X, b.Z, b.Level );
			var far = g.FloorAt( b.Far.x, b.Far.z, b.Far.l );
			if ( near == null || far == null ) { problems.Add( $"{who}: not between two floor cells" ); continue; }
			if ( near.Tier == null || far.Tier == null || near.Tier == far.Tier ) problems.Add( $"{who}: not between two different tiers" );
			if ( near.Tier?.Tag == TierTag.Secret || far.Tier?.Tag == TierTag.Secret ) problems.Add( $"{who}: opens the secret room" );
			bool ends( Tier t ) => t != null && t.Tag is TierTag.Spawn or TierTag.Exit;
			if ( ends( near.Tier ) && ends( far.Tier ) ) problems.Add( $"{who}: shortcuts spawn to exit" );
			if ( !g.Neighbours( (b.X, b.Z, b.Level) ).Contains( b.Far ) ) problems.Add( $"{who}: the two cells are not joined" );
			if ( lay.Decor != null )
			{
				if ( !lay.Decor.Walk.Contains( (b.X, b.Z, b.Level) ) ) problems.Add( $"{who}: near cell off the walking line" );
				if ( !lay.Decor.Walk.Contains( b.Far ) ) problems.Add( $"{who}: far cell off the walking line" );
			}
			if ( b.Gap < 1.2f || b.Gap > 2.6f ) problems.Add( $"{who}: gap {b.Gap:F2} m out of range" );
			if ( b.Offset < 0.4f || b.Offset + b.Gap > DungeonBreach.SegmentM ) problems.Add( $"{who}: stubs do not fit the segment" );
		}
		return problems;
	}

	static bool IsSmallAgainstWall( PropPlacement p, DungeonKit kit ) => p.S != 0 || kit.Props.TryGetValue( p.Prop, out var def ) && def.Depth <= 1.0f && !def.Tile;

	/// <summary>Per-kind counts, how far from the spawn the first trap sits, and how many landed off the spine.</summary>
	static string TrapSummary( DungeonLayout lay )
	{
		var traps = lay.Decor.Traps;
		if ( traps.Count == 0 ) return "none";
		var kinds = string.Join( " ", traps.GroupBy( t => t.Kind ).OrderBy( g => g.Key ).Select( g => $"{g.Key}x{g.Count()}" ) );
		var first = traps.OrderBy( t => t.WalkDist ).First();
		return $"{traps.Count} ({kinds}), first {first.Kind} at {first.WalkDist} cells, spurs {traps.Count( t => t.SpineIndex < 0 )}";
	}

	/// <summary>
	/// Trap invariants: every hurt cell is a floor cell of the trap's own tier; no trap in the spawn, exit, shrine,
	/// loot or secret tiers; hallway kinds sit mid-run on a straight 1-wide corridor (never a doorway cell or a
	/// run end), a blade under a ceiling, a spike plate with nothing living below; a flame head on a doorway
	/// cell with another tier across it and its head on a real, non-secret wall; one trap per tier; spaced out;
	/// and nothing the decoration placed may stand on a trap cell or on the head's face.
	/// </summary>
	static System.Collections.Generic.List<string> CheckTraps( DungeonLayout lay )
	{
		var problems = new System.Collections.Generic.List<string>();
		var d = lay.Decor;
		var g = lay.Grid;
		var wallSet = lay.Edges.Where( e => e.Kind == EdgeKind.Wall ).Select( e => (e.X, e.Z, e.Level, e.D) ).ToHashSet();
		var ceilings = lay.Ceilings.ToHashSet();
		var tiers = g.Tiers.ToDictionary( t => t.Id );
		var seenTiers = new System.Collections.Generic.HashSet<int>();
		var budget = TrapBudget.ForDepth( d.Depth );
		for ( int i = 0; i < d.Traps.Count; i++ )
		{
			var t = d.Traps[i];
			string who = $"{t.Kind} at {t.X},{t.Z},{t.Level}";
			if ( t.Index != i ) problems.Add( $"{who} has index {t.Index}, expected {i}" );
			if ( !tiers.TryGetValue( t.TierId, out var tier ) ) { problems.Add( $"{who} in unknown tier {t.TierId}" ); continue; }
			if ( tier.Tag is TierTag.Spawn or TierTag.Exit or TierTag.Shrine or TierTag.Loot or TierTag.Secret ) problems.Add( $"{who} in a {tier.Tag} tier" );
			if ( !seenTiers.Add( t.TierId ) ) problems.Add( $"{who}: second trap in tier {t.TierId}" );
			foreach ( var (x, z) in DecorTraps.DamageCells( t ) )
			{
				var cell = g.FloorAt( x, z, t.Level );
				if ( cell == null || cell.Tier != tier ) problems.Add( $"{who} hurts {x},{z} outside its tier" );
				if ( !d.TrapCells.Contains( (x, z, t.Level) ) ) problems.Add( $"{who} hurts {x},{z} but the cell is not in TrapCells" );
			}
			var def = TrapCatalog.Defs[t.Kind];
			if ( def.Site == TrapSite.Hallway )
			{
				var cells = tier.Sorted();
				bool alongZ = cells.All( c => c.x == cells[0].x ), alongX = cells.All( c => c.z == cells[0].z );
				var run = alongZ ? cells.OrderBy( c => c.z ).ToList() : cells.OrderBy( c => c.x ).ToList();
				if ( tier.Kind != TierKind.Corridor || !(alongZ || alongX) || cells.Count < 3 ) problems.Add( $"{who} not on a straight 1-wide corridor" );
				else
				{
					if ( run[0] == (t.X, t.Z) || run[^1] == (t.X, t.Z) ) problems.Add( $"{who} on the end cell of its run" );
					if ( (alongZ ? Dir.N : Dir.E) != t.Facing ) problems.Add( $"{who} facing {t.Facing} on a run along {(alongZ ? "z" : "x")}" );
				}
				foreach ( var (x, z) in DecorTraps.DamageCells( t ) )
				{
					if ( t.Kind == TrapKind.SwingBlade && (x, z) != (t.X, t.Z) ) continue;   // the swing may reach the run's ends
					if ( cells.Count >= 2 && (run[0] == (x, z) || run[^1] == (x, z)) ) problems.Add( $"{who} covers the end cell {x},{z}" );
					foreach ( var n in g.Neighbours( (x, z, t.Level) ) )
						if ( g.Cells[n].Tier != tier ) problems.Add( $"{who} covers a doorway cell {x},{z}" );
				}
				if ( t.Kind == TrapKind.SwingBlade && !ceilings.Contains( (t.X, t.Z, t.Level + 2) ) ) problems.Add( $"{who} without a ceiling" );
				if ( t.Kind == TrapKind.SpikeFloor && g.Claims.Contains( (t.X, t.Z, t.Level - 1) ) ) problems.Add( $"{who} over a claimed cell below" );
				if ( t.Kind == TrapKind.SawRail && t.Extent < 2 ) problems.Add( $"{who} rail shorter than 2 cells" );
				if ( t.Kind is TrapKind.ArrowWall or TrapKind.WallSlab )
				{
					if ( !wallSet.Contains( (t.WallX, t.WallZ, t.Level, t.WallD) ) ) problems.Add( $"{who} on a non-wall face {t.WallD}" );
					if ( DecorSlots.IsSecretWall( lay, t.WallX, t.WallZ, t.Level, t.WallD ) ) problems.Add( $"{who} on the secret wall" );
					foreach ( var p in d.Props )
						if ( p.Anchor is AnchorKind.WallMounted or AnchorKind.WallFlush && (p.X, p.Z, p.Level, p.Edge) == (t.WallX, t.WallZ, t.Level, t.WallD) )
							problems.Add( $"{p.Prop} on the {t.Kind}'s wall face at {t.WallX},{t.WallZ},{t.Level} {t.WallD}" );
				}
				if ( t.Kind == TrapKind.WallSlab )
				{
					if ( !wallSet.Contains( (t.X, t.Z, t.Level, t.WallD.Opp()) ) ) problems.Add( $"{who} without a solid wall opposite" );
					for ( int k = 1; k <= DecorTraps.SlabRockCells; k++ )
					{
						int bx = t.X + t.WallD.Dx() * k, bz = t.Z + t.WallD.Dz() * k;
						if ( g.Claims.Contains( (bx, bz, t.Level) ) || g.Claims.Contains( (bx, bz, t.Level + 1) ) ) problems.Add( $"{who} shafts reach a claimed cell {bx},{bz}" );
					}
				}
			}
			else
			{
				var beyond = (t.X + t.Facing.Dx(), t.Z + t.Facing.Dz(), t.Level);
				if ( !g.Cells.TryGetValue( beyond, out var bc ) || bc.Kind != CellKind.Floor || bc.Tier == null || bc.Tier == tier ) problems.Add( $"{who} not on a doorway cell" );
				if ( !wallSet.Contains( (t.WallX, t.WallZ, t.Level, t.WallD) ) ) problems.Add( $"{who} head on a non-wall face {t.WallX},{t.WallZ} {t.WallD}" );
				if ( DecorSlots.IsSecretWall( lay, t.WallX, t.WallZ, t.Level, t.WallD ) ) problems.Add( $"{who} head on the secret wall" );
				foreach ( var p in d.Props )
					if ( p.Anchor is AnchorKind.WallMounted or AnchorKind.WallFlush && (p.X, p.Z, p.Level, p.Edge) == (t.WallX, t.WallZ, t.Level, t.WallD) )
						problems.Add( $"{p.Prop} on the {t.Kind}'s face at {t.WallX},{t.WallZ},{t.Level} {t.WallD}" );
			}
			for ( int j = 0; j < i; j++ )
				if ( Math.Abs( d.Traps[j].WalkDist - t.WalkDist ) < Math.Max( 1, budget.MinGapCells / 2 ) )
					problems.Add( $"{who} within {budget.MinGapCells / 2} cells of {d.Traps[j].Kind} at {d.Traps[j].X},{d.Traps[j].Z}" );
		}
		foreach ( var p in d.Props )
			if ( p.Blocks && p.Anchor == AnchorKind.Floor && d.TrapCells.Contains( (p.X, p.Z, p.Level) ) )
				problems.Add( $"blocking {p.Prop} on a trap cell at {p.X},{p.Z},{p.Level}" );
		if ( d.Traps.Count < budget.Min ) Console.WriteLine( $"           WARN only {d.Traps.Count} traps seated (budget {budget.Min}-{budget.Max})" );
		return problems;
	}

}
