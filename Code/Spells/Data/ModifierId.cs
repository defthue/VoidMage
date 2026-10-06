namespace VoidMage.Spells;

/// <summary>
/// The staff's enchantments (MODIFIER_spec.md minus the cuts, plus the Isaac-style pool of 2026-09-20). The ordinal is
/// the wire identity: <see cref="ModifierStacks"/> packs two bits per modifier by this order, 31 to a long. The save
/// only ever holds enum NAMES (Scroll keys), so entries can be appended freely; cutting or reordering only breaks a
/// build talking to an older one.
/// </summary>
public enum ModifierId : byte
{
	None = 0,
	Multiplication,
	Ricochet,
	Overcharge,
	Kindling,
	Hollow,
	Avarice,
	Orbit,
	Amber,
	Wake,
	Conduit,
	Turncoat,
	Echo,
	/// <summary>Was Duality (fired the other slot's spell) until the staff went down to one spell.</summary>
	Stranger,
	Gluttony,
	Hound,
	Wrath,

	// ---- the 2026-09-20 pool
	Needle,
	Fracture,
	Familiar,
	Return,
	Retort,
	Lattice,
	Anvil,
	Mirror,
	Hail,
	Pyre,
	Harvest,
	Fuse,
	Ram,
	Edge,
	Sloth,
	Horizon,
	Hummingbird,
	LastDrop,
	WildPage,
	BrandEmbers,
	BrandFrost,
	BrandStorm,
}

/// <summary>The seven stages every cast resolves through, in order. A modifier registers into one or two.</summary>
public enum CastStage
{
	/// <summary>What you pay, in what currency.</summary>
	Cost,
	/// <summary>Whether a cast happens, and how many times.</summary>
	Trigger,
	/// <summary>How many projectiles, of what, in what pattern.</summary>
	Emission,
	/// <summary>Final damage per projectile.</summary>
	Damage,
	/// <summary>Behaviour while travelling.</summary>
	Flight,
	/// <summary>What happens where it lands.</summary>
	Impact,
	/// <summary>What happens when a victim dies.</summary>
	Death,
}
