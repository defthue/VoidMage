using Sandbox;

namespace VoidMage.Combat;

/// <summary>What killed something: the blow's attacker, its kind and damage and, for spells, the cast's context. Read on the host in the frame of the death.</summary>
public readonly record struct KillContext( GameObject Attacker, string Kind, float Damage, Vector3 Push, SpellHitInfo Spell )
{
	public static readonly KillContext None = new( null, "", 0f, Vector3.Zero, SpellHitInfo.None );

	public static KillContext From( in DamageRouter.Hit hit ) => new( hit.Attacker, hit.Kind ?? "", hit.Damage, hit.Push, hit.Spell );

	public bool IsSpell => Spell.IsSpell || DamageRouter.IsSpell( Kind );
}
