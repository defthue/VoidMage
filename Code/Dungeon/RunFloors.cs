namespace VoidMage.Dungeon;

/// <summary>
/// The run's floor table: the four biomes, ONE floor each, then the Bone Colossus (user, 2026-09-22: back to four
/// floors from the eight of 2026-09-13, "1 floor per biome", with the scrolls a floor raised from 4 to 6 so a run
/// still builds a staff; nothing else changed). Shrunk on 2026-09-20 for the Isaac-style loop ("much smaller so the
/// experience becomes more a quick hack and slash"), then the same day the head count was TRIPLED ("many enemies is
/// what makes it difficult") and the floors were let out again just far enough to hold it: 14 to 24 rooms, standing
/// room for about twice the bodies the floor carries (gentest ramp prints SHORT when a floor cannot hold its count).
/// Each floor comes in three SHAPES, one per grammar archetype (user, 2026-09-22: "randomize it whether it picks
/// pit warren or cathedral"); the floor's seed picks one, so the fixed first run is still fixed. A shape carries its
/// own size and rooms factor, because those were tuned per archetype: the eight measured rows of the old table are
/// kept as they were and the four shapes that table never had are sized by their neighbours. A floor is one dense
/// fight after another, and the run's scrolls come round quickly. Engine-free so tools/gentest can generate every
/// shape and prove each comes out with an exit tier and its portal. RunManager reads it; nothing else should.
/// </summary>
public static class RunFloors
{
	/// <summary>One way a floor can be laid out: the archetype with the size and rooms factor that hold the floor's count.</summary>
	public readonly record struct Shape( Archetype Archetype, float Size, float Rooms );

	/// <param name="Enemies">Head count the floor carries (DungeonBuilder.TargetSkeletons; critters not counted):
	/// 84 on the first floor to 192 on the last, on the SMALL floors (user, 2026-09-20, after a day at a third of
	/// that: "triple the enemy count, many enemies is what makes it difficult... the deeper you go the more enemys
	/// and the stronger they are"). The first floor is right as it is (from the second floor on the count was raised
	/// a third on 2026-09-21: "with a couple enchantments the second floor is too easy, needs more enemies later floors").
	/// With four floors the same 84-to-192 ramp is walked in bigger steps; raised 20% across the board on 2026-09-24
	/// (user: "increase amount of monsters per floor with about 20%"), 101 to 230. Every floor past the first its bodies also
	/// have 12% more health and hit 6% harder (SkeletonSpawner.SetFloor). The spread rules are unchanged
	/// (DungeonBuilder.SpawnClearCells, CorridorSkeletonChance and the per-tier spacing); gentest ramp prints SHORT
	/// when a shape cannot hold its floor's count.</param>
	public readonly record struct Floor( DungeonTheme Theme, int Enemies, Shape[] Shapes );

	/// <summary>The floor as the builder takes it: the biome and count with the shape the seed picked.</summary>
	public readonly record struct Row( DungeonTheme Theme, Archetype Archetype, float Size, float Rooms, int Enemies );

	public static readonly Floor[] Table =
	{
		new( DungeonTheme.Stone, 101, new Shape[]
		{
			new( Archetype.Warren,    0.6f,  0.20f ),
			new( Archetype.Pit,       0.6f,  0.20f ),
			new( Archetype.Cathedral, 0.55f, 0.25f ),
		} ),
		new( DungeonTheme.Mossy, 156, new Shape[]
		{
			new( Archetype.Warren,    0.9f,  0.35f ),
			new( Archetype.Pit,       0.7f,  0.30f ),
			new( Archetype.Cathedral, 0.7f,  0.35f ),
		} ),
		new( DungeonTheme.Realms, 187, new Shape[]
		{
			new( Archetype.Warren,    1.2f,  0.60f ),   // 1.05/0.45 was SHORT at 187 (tightest seed stood 156)
			new( Archetype.Pit,       0.9f,  0.45f ),
			new( Archetype.Cathedral, 0.8f,  0.50f ),
		} ),
		new( DungeonTheme.Gaol, 230, new Shape[]
		{
			new( Archetype.Warren,    1.35f, 0.70f ),   // 1.2/0.55 was SHORT at 230 (tightest seed stood 201)
			new( Archetype.Pit,       1.0f,  0.75f ),   // 1.0/0.4 was SHORT at 230 (tightest seed stood 222); the pit's ring only steps at size 1.3, its spurs scale with rooms
			new( Archetype.Cathedral, 0.9f,  0.55f ),
		} ),
	};

	/// <summary>The last dungeon floor (4); the boss is the floor after it.</summary>
	public static int LastFloor => Table.Length;
	public static int BossFloor => Table.Length + 1;

	public static Floor At( int floor ) => Table[System.Math.Clamp( floor, 1, LastFloor ) - 1];

	/// <summary>The floor laid out in the shape its seed picks (the builder's seed, so a floor's shape and its layout go together).</summary>
	public static Row For( int floor, int seed )
	{
		var f = At( floor );
		int pick = (int)((uint)DungeonRng.Mix( seed, 0x5A4E ) % (uint)f.Shapes.Length);
		return With( f, f.Shapes[pick] );
	}

	public static Row With( Floor f, Shape s ) => new( f.Theme, s.Archetype, s.Size, s.Rooms, f.Enemies );

	public static string Name( int floor )
	{
		if ( floor >= BossFloor ) return "the Colossus";
		if ( floor < 1 ) return "the village";
		return At( floor ).Theme switch
		{
			DungeonTheme.Mossy => "the overgrown halls",
			DungeonTheme.Realms => "the forge-hold",
			DungeonTheme.Gaol => "the gaol",
			_ => "the old dungeon",
		};
	}
}
