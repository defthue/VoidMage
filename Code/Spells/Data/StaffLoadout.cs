using System;
using Sandbox;

namespace VoidMage.Spells;

/// <summary>
/// The staff: ONE spell and every enchantment picked up this run (user, 2026-09-20: the Isaac model; it was two spell
/// slots and four modifier slots moved about in a HUD). A scroll taken is on the staff for the rest of the run, there
/// is no taking it off; a fourth copy never happens because a scroll at three copies leaves the drop pool.
/// Owner-authoritative like the rest of the player, every field synced so peers can show what a caster is throwing
/// (and how many wisps circle them). What is ON the staff is PlayerInventory's business; the setters stay
/// unrestricted for the spell_* test commands.
/// </summary>
[Title( "Staff Loadout" ), Category( "Void Mage" ), Icon( "auto_fix_high" )]
public sealed class StaffLoadout : Component
{
	[Sync] public string SpellId { get; set; } = SpellIds.Fireball;
	/// <summary><see cref="ModifierStacks"/>.Lo / .Hi.</summary>
	[Sync] public long ModsLo { get; set; }
	[Sync] public long ModsHi { get; set; }

	/// <summary>The local player's staff, for the HUD and console.</summary>
	public static StaffLoadout Local { get; private set; }

	public SpellDefinition ActiveSpell => SpellDatabase.Get( SpellId ) ?? SpellDatabase.Get( SpellIds.Fireball );
	public ModifierStacks Stacks => new( ModsLo, ModsHi );
	public bool HasModifier( ModifierId id ) => Stacks.Has( id );
	public int Stack( ModifierId id ) => Stacks[id];

	protected override void OnStart()
	{
		if ( !IsProxy ) Local = this;
		_health = Components.Get<VoidMage.Character.PlayerHealth>( FindMode.EverythingInSelfAndDescendants );
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
	}

	private VoidMage.Character.PlayerHealth _health;

	/// <summary>Owner: Wrath's cut on the cap follows the staff (polled: two longs are cheap and there is no change hook).</summary>
	protected override void OnUpdate()
	{
		if ( IsProxy || !_health.IsValid() ) return;
		_health.SetMaxHealthScale( ModifierTables.WrathMaxHealthMul[Stacks[ModifierId.Wrath]] );
	}

	// ------------------------------------------------------------------ owner

	public void SetSpell( string id )
	{
		if ( IsProxy ) return;
		SpellId = SpellDatabase.IsValid( id ) ? id : SpellIds.Fireball;
	}

	/// <summary>One more copy of a scroll. False when it already sits at three.</summary>
	public bool AddModifier( ModifierId id )
	{
		if ( IsProxy || id == ModifierId.None ) return false;
		var stacks = Stacks;
		if ( stacks[id] >= ModifierStacks.MaxCopies ) return false;
		SetStacks( stacks.Add( id ) );
		return true;
	}

	public void SetModifier( ModifierId id, int copies )
	{
		if ( IsProxy ) return;
		SetStacks( Stacks.With( id, copies ) );
	}

	public void SetStacks( ModifierStacks stacks )
	{
		if ( IsProxy ) return;
		ModsLo = stacks.Lo;
		ModsHi = stacks.Hi;
	}

	public void ClearModifiers() => SetStacks( ModifierStacks.Empty );

	public string Describe() => $"{ActiveSpell?.Name ?? "-"} | scrolls: {Stacks}";
}
