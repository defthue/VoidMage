using Sandbox;

namespace VoidMage.Spells;

/// <summary>
/// A spell's five colours, the only thing the placeholder visuals need to tell one spell from another: the
/// white-hot heart, the body of the element, its embers, its smoke, and the streak it draws in flight.
/// The values are tuning placeholders; the bespoke VFX pass keys on the spell id and keeps these as a base.
/// </summary>
public sealed record SpellPalette( Color Hot, Color Core, Color Ember, Color Smoke, Color Trail )
{
	/// <summary>The fireball as shipped (FireballVfx's statics).</summary>
	public static readonly SpellPalette Fire = new(
		new Color( 1f, 0.95f, 0.75f ),
		new Color( 1f, 0.55f, 0.15f ),
		new Color( 1f, 0.8f, 0.3f ),
		new Color( 0.36f, 0.30f, 0.44f ),
		new Color( 1f, 0.45f, 0.1f ) );

	public static readonly SpellPalette Ice = new(
		new Color( 0.92f, 0.98f, 1f ),
		new Color( 0.55f, 0.78f, 1f ),
		new Color( 0.8f, 0.92f, 1f ),
		new Color( 0.42f, 0.48f, 0.62f ),
		new Color( 0.7f, 0.85f, 1f ) );

	public static readonly SpellPalette Lightning = new(
		new Color( 1f, 1f, 1f ),
		new Color( 0.72f, 0.55f, 1f ),
		new Color( 0.9f, 0.8f, 1f ),
		new Color( 0.3f, 0.26f, 0.42f ),
		new Color( 0.85f, 0.7f, 1f ) );

	/// <summary>The Void Lance (it was the red Piercing Laser until 2026-09-21): the Void Orb's family, a shade bluer, its smoke near-black.</summary>
	public static readonly SpellPalette Laser = new(
		new Color( 0.9f, 0.78f, 1f ),
		new Color( 0.56f, 0.2f, 1f ),
		new Color( 0.7f, 0.46f, 1f ),
		new Color( 0.05f, 0.02f, 0.09f ),
		new Color( 0.5f, 0.16f, 0.92f ) );

	public static readonly SpellPalette Void = new(
		new Color( 0.85f, 0.6f, 1f ),
		new Color( 0.5f, 0.12f, 0.9f ),
		new Color( 0.6f, 0.3f, 1f ),
		new Color( 0.12f, 0.08f, 0.18f ),
		new Color( 0.4f, 0.1f, 0.7f ) );

	/// <summary>Bees: amber, honey, black smoke.</summary>
	public static readonly SpellPalette Swarm = new(
		new Color( 1f, 0.95f, 0.7f ),
		new Color( 1f, 0.72f, 0.1f ),
		new Color( 1f, 0.85f, 0.35f ),
		new Color( 0.1f, 0.09f, 0.06f ),
		new Color( 1f, 0.8f, 0.25f ) );

	/// <summary>Necrotic: the necromancer's green. Acid-pale heart, sickly witchfire body, yellow-green motes, green-black vapour.</summary>
	public static readonly SpellPalette Necrotic = new(
		new Color( 0.82f, 1f, 0.72f ),
		new Color( 0.24f, 0.88f, 0.36f ),
		new Color( 0.6f, 1f, 0.35f ),
		new Color( 0.06f, 0.14f, 0.08f ),
		new Color( 0.3f, 0.95f, 0.42f ) );

	/// <summary>CSS colour for the HUD.</summary>
	public static string Css( Color c ) => $"rgb({(int)(c.r * 255f)},{(int)(c.g * 255f)},{(int)(c.b * 255f)})";
}
