namespace VoidMage.Dungeon;

/// <summary>
/// How a floor is dressed. The generation grammar is identical either way — same spines, same spurs, same
/// seeds. Stone is the dungeon as it was built; Mossy is the same dungeon gone to seed: the green atlas,
/// greenery over the floors, walls and ceilings, and walls that have fallen in (see <see cref="DungeonBreach"/>).
///
/// Realms is a different pack altogether: the dwarven forge-hold of floors 11+, built from POLYGON Dungeon
/// Realms through <see cref="DungeonKit"/>, which holds everything about a theme that the builder reads.
///
/// Gaol is the plague-fortress prison built from POLYGON Dark Fantasy and Dark Fortress together: vaulted
/// cells, iron-bar walls, boarded-up dead ends, corpses and torture machines everywhere (DungeonKit.Gaol.cs).
///
/// Nothing picks this automatically. Which floor of a run gets which theme is a later decision; for now it is
/// a property on the builder, independent of Depth.
/// </summary>
public enum DungeonTheme { Stone, Mossy, Realms, Gaol }
