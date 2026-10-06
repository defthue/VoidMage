using System;
using System.Collections.Generic;
using System.Linq;
using VoidMage.Spells;

namespace VoidMage.Inventory;

/// <summary>
/// One scroll: a spell or a staff enchantment (modifier). The identity is a string key, "spell:fireball" or
/// "mod:hound" (the enum NAME, never the ordinal: the save must survive modifiers being added or reordered). The
/// "sealed:" prefix is from the satchel days and only survives for the shop's old saves and the closed-scroll icon;
/// whether a scroll in the world lies sealed is what the PLAYER knows (PlayerInventory.IsDiscovered), not the key.
/// </summary>
public readonly record struct Scroll( string Key )
{
	public const string SpellPrefix = "spell:";
	public const string ModPrefix = "mod:";
	public const string SealedPrefix = "sealed:";

	public static readonly Scroll Empty = default;

	public bool IsEmpty => string.IsNullOrEmpty( Key );
	/// <summary>Not opened yet: the HUD shows the closed scroll and a click opens it.</summary>
	public bool Sealed => !IsEmpty && Key.StartsWith( SealedPrefix, StringComparison.Ordinal );
	/// <summary>The key without the seal.</summary>
	public string Inner => Sealed ? Key.Substring( SealedPrefix.Length ) : Key;

	public bool IsSpell => !IsEmpty && Inner.StartsWith( SpellPrefix, StringComparison.Ordinal );
	public bool IsModifier => !IsEmpty && Inner.StartsWith( ModPrefix, StringComparison.Ordinal );

	public string SpellId => IsSpell ? Inner.Substring( SpellPrefix.Length ) : "";
	public SpellDefinition Spell => IsSpell ? SpellDatabase.Get( SpellId ) : null;

	public ModifierId Modifier => IsModifier && Enum.TryParse<ModifierId>( Inner.Substring( ModPrefix.Length ), true, out var id ) ? id : ModifierId.None;
	public ModifierDefinition ModifierDef => IsModifier ? ModifierDatabase.Get( Modifier ) : null;

	/// <summary>A key that names something that exists (a save written before a cut can hold dead keys).</summary>
	public bool IsValid => Spell is not null || ModifierDef is not null;

	public Scroll Opened => Sealed ? new Scroll( Inner ) : this;
	public Scroll AsSealed => IsEmpty || Sealed ? this : new Scroll( SealedPrefix + Key );

	public static Scroll ForSpell( string spellId ) => SpellDatabase.IsValid( spellId ) ? new Scroll( SpellPrefix + spellId ) : Empty;
	public static Scroll ForModifier( ModifierId id ) => id == ModifierId.None ? Empty : new Scroll( ModPrefix + id.ToString().ToLowerInvariant() );

	/// <summary>A saved key back into a scroll; empty when it no longer names anything.</summary>
	public static Scroll FromKey( string key )
	{
		if ( string.IsNullOrEmpty( key ) ) return Empty;
		var s = new Scroll( key.Trim().ToLowerInvariant() );
		// normalise the modifier spelling so equal scrolls compare equal
		var inner = s.IsModifier ? ForModifier( s.Modifier ) : s.IsValid ? s.Opened : Empty;
		return inner.IsEmpty ? Empty : s.Sealed ? inner.AsSealed : inner;
	}

	/// <summary>By spell or modifier name, prefix allowed ("hound", "ice", "the orbit").</summary>
	public static Scroll Find( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return Empty;
		if ( SpellDatabase.Find( text ) is { } spell ) return ForSpell( spell.Id );
		if ( ModifierDatabase.TryParse( text, out var id ) && id != ModifierId.None ) return ForModifier( id );
		return Empty;
	}

	public string Name => Spell?.Name ?? ModifierDef?.Name ?? "";
	public string KindLabel => IsSpell ? "Spell" : IsModifier ? "Enchantment" : "";

	public override string ToString() => IsEmpty ? "(empty)" : Key;

	// ------------------------------------------------------------------ the catalogue

	/// <summary>Every scroll in catalogue order: the spells, then the enchantments. The index and the library sort by it.</summary>
	public static readonly Scroll[] All = SpellDatabase.All.Select( s => ForSpell( s.Id ) )
		.Concat( ModifierDatabase.All.Select( m => ForModifier( m.Id ) ) ).ToArray();

	public static readonly Scroll[] AllModifiers = All.Where( s => s.IsModifier ).ToArray();

	private static readonly Dictionary<string, int> _order = All.Select( ( s, i ) => (s.Key, i) ).ToDictionary( p => p.Key, p => p.i );

	/// <summary>Position in <see cref="All"/> (a sealed scroll sorts with its contents); unknown keys sort last.</summary>
	public int CatalogueOrder => !IsEmpty && _order.TryGetValue( Inner, out var i ) ? i : int.MaxValue;
}

/// <summary>
/// The placeholder presentation of a scroll: its rune code, its colour, its icon and the lines the HUD prints.
/// Everything a player reads about a scroll comes from here, so the full UI pass has one file to rewrite.
/// </summary>
public static class ScrollText
{
	/// <summary>
	/// The two-letter code written on each scroll (user: "Fireball would be FB"). Nobody reads Futhark, so these only
	/// have to be distinct as runes: C, K and Q share a rune, so do V/W, J/Y and X/Z. An enchantment's code is on its
	/// definition; the spells' are here.
	/// </summary>
	private static readonly Dictionary<string, string> SpellCodes = new()
	{
		[SpellIds.Fireball] = "FB", [SpellIds.IceShard] = "IS", [SpellIds.ChainLightning] = "CL", [SpellIds.Laser] = "VL",
		[SpellIds.VoidOrb] = "VO", [SpellIds.Swarm] = "SW", [SpellIds.SiphonBolt] = "SB",
	};

	/// <summary>Latin letter to Elder Futhark (the closest sound; Noto Sans Runic draws them).</summary>
	private static readonly Dictionary<char, string> Futhark = new()
	{
		['A'] = "ᚨ", ['B'] = "ᛒ", ['C'] = "ᚲ", ['D'] = "ᛞ", ['E'] = "ᛖ", ['F'] = "ᚠ", ['G'] = "ᚷ", ['H'] = "ᚺ", ['I'] = "ᛁ",
		['J'] = "ᛃ", ['K'] = "ᚲ", ['L'] = "ᛚ", ['M'] = "ᛗ", ['N'] = "ᚾ", ['O'] = "ᛟ", ['P'] = "ᛈ", ['Q'] = "ᚲ", ['R'] = "ᚱ",
		['S'] = "ᛊ", ['T'] = "ᛏ", ['U'] = "ᚢ", ['V'] = "ᚹ", ['W'] = "ᚹ", ['X'] = "ᛉ", ['Y'] = "ᛃ", ['Z'] = "ᛉ",
	};

	public static string Code( Scroll s )
	{
		if ( s.ModifierDef is { } mod ) return mod.Code;
		return s.IsSpell && SpellCodes.TryGetValue( s.SpellId, out var code ) ? code : "??";
	}

	/// <summary>The scroll's code in runes ("ᚠᛒ"). Empty for a sealed scroll: nothing is written on the outside.</summary>
	public static string Runes( Scroll s ) => s.IsEmpty || s.Sealed ? "" : ToRunes( Code( s ) );

	/// <summary>Any latin text in Futhark, letter by letter (spaces kept, anything else dropped).</summary>
	public static string ToRunes( string text )
	{
		var sb = new System.Text.StringBuilder();
		foreach ( var c in (text ?? "").ToUpperInvariant() )
		{
			if ( c == ' ' ) sb.Append( ' ' );
			else if ( Futhark.TryGetValue( c, out var r ) ) sb.Append( r );
		}
		return sb.ToString();
	}

	/// <summary>The icon: the closed or open Dungeon Realms scroll, red-sealed for enchantments, gold-sealed for spells (tools/render_scroll_icons.py).</summary>
	public static string IconPath( Scroll s ) => $"ui/scrolls/{(s.Sealed ? "closed" : "open")}_{(s.IsSpell ? "spell" : "mod")}.png";

	private static readonly Dictionary<string, Sandbox.Texture> _icons = new();

	public static Sandbox.Texture Icon( Scroll s )
	{
		if ( s.IsEmpty ) return null;
		var path = IconPath( s );
		if ( !_icons.TryGetValue( path, out var tex ) )
		{
			tex = Sandbox.Texture.Load( path );
			_icons[path] = tex;
		}
		return tex;
	}

	/// <summary>A spell writes in its palette's core colour; an enchantment in the colour of the first stage it works in.</summary>
	public static string Colour( Scroll s )
	{
		if ( s.Spell is { } spell ) return SpellPalette.Css( spell.Palette.Core );
		var stage = s.ModifierDef?.Stages.FirstOrDefault() ?? CastStage.Cost;
		return stage switch
		{
			CastStage.Cost => "#b8860b",
			CastStage.Trigger => "#7a4fc8",
			CastStage.Emission => "#c8641e",
			CastStage.Damage => "#b02a22",
			CastStage.Flight => "#1f8a80",
			CastStage.Impact => "#3f5fb0",
			CastStage.Death => "#4f7a3a",
			_ => "#4a3a2a",
		};
	}

	public static string Title( Scroll s ) => s.Sealed ? (s.IsSpell ? "Sealed spell scroll" : "Sealed scroll") : s.Name;

	public static string Description( Scroll s )
	{
		if ( s.Sealed ) return s.IsSpell ? "A gold seal: a spell you have never held." : "A scroll you have never held.";
		if ( s.ModifierDef is { } mod ) return string.IsNullOrEmpty( mod.Description ) ? mod.Banner : mod.Description;
		if ( s.Spell is not { } spell ) return "";
		return spell.Id switch
		{
			SpellIds.Fireball => "A heavy bolt that bursts on impact and splashes nearby enemies.",
			SpellIds.IceShard => "A fast, cheap shard that slows what it hits.",
			SpellIds.ChainLightning => "Strikes instantly and leaps between enemies.",
			SpellIds.Laser => "A short burst beam that burns the first enemy in its line.",
			SpellIds.VoidOrb => "A slow orb that leaves a burning void where it lands.",
			SpellIds.Swarm => "Six bees that seek out enemies. Every sting counts as a hit.",
			SpellIds.SiphonBolt => $"A slow, short-ranged bolt. Every enemy it kills heals you for {SpellDatabase.Get( SpellIds.SiphonBolt )?.KillHeal ?? 0f:0}.",
			_ => "",
		};
	}

	/// <summary>The spell's base numbers, or what one, two and three copies of an enchantment do.</summary>
	public static IEnumerable<string> Details( Scroll s )
	{
		if ( s.Sealed ) yield break;
		if ( s.Spell is { } spell )
		{
			yield return $"{spell.Damage:0.#} damage{(spell.ProjectileCount > 1 ? $" × {spell.ProjectileCount}" : "")}";
			yield return $"{spell.ManaCost:0} mana";
			yield return $"a cast every {spell.CastDurationSeconds:0.##}s";
			yield break;
		}
		if ( !s.IsModifier ) yield break;
		for ( int n = 1; n <= 3; n++ )
			yield return $"×{n}  {StackLine( s.Modifier, n )}";
	}

	/// <summary>One copy's, two copies' and three copies' numbers, spelled out: every figure says what it is a figure of.</summary>
	public static string StackLine( ModifierId id, int n ) => id switch
	{
		ModifierId.Multiplication => $"{ModifierTables.MultiplicationCount[n]} projectiles per cast",
		ModifierId.Mirror => n switch { 1 => "one more cast, behind you", 2 => "behind you and to both sides", _ => "all the way round, eight directions" },
		ModifierId.Stranger => $"the stranger's spell at {ModifierTables.StrangerDamage[n] * 100f:0}% damage",
		ModifierId.Hummingbird => $"casts {1f / ModifierTables.HummingbirdCastTimeMul[n]:0.#}x as fast · {ModifierTables.HummingbirdCostMul[n] * 100f:0}% mana · {ModifierTables.HummingbirdDamageMul[n] * 100f:0}% damage",
		ModifierId.WildPage => $"+{ModifierTables.WildPageDamagePct[n] * 100f:0}% damage, whatever comes out",
		ModifierId.Echo => $"{ModifierTables.EchoRepeats[n]} free repeat{(n > 1 ? "s" : "")} of every cast",
		ModifierId.Familiar => $"{ModifierTables.FamiliarWisps[n]} wisp{(n > 1 ? "s" : "")}, each at {ModifierTables.FamiliarDamage * 100f:0}% damage",
		ModifierId.Hail => $"{ModifierTables.HailBolts[n]} falling copies at {ModifierTables.HailDamage * 100f:0}% damage",
		ModifierId.Conduit => $"{ModifierTables.ConduitChance[n] * 100f:0}% chance on every sword hit",
		ModifierId.Retort => $"a ring of {ModifierTables.RetortBolts[n]} at {ModifierTables.RetortDamage * 100f:0}% damage",
		ModifierId.Edge => $"sword hits add {ModifierTables.EdgePct[n] * 100f:0}% of the spell for {ModifierTables.EdgeSeconds:0}s",
		ModifierId.Kindling => n >= 3 ? "the primed cast is free" : $"the primed cast costs {ModifierTables.KindlingDiscount[n] * 100f:0}% less",
		ModifierId.Hollow => n < 2
			? $"+{ModifierTables.HollowDamagePct[n] * 100f:0}% damage · kills give nothing back"
			: $"+{ModifierTables.HollowDamagePct[n] * 100f:0}% damage · each kill heals {SpellHostTuning.HollowRefund( n ):0}",
		ModifierId.LastDrop => $"the emptying cast hits for x{1f + ModifierTables.LastDropDamagePct[n]:0.#}",
		ModifierId.Overcharge => $"every {Ordinal( ModifierTables.OverchargeEvery[n] )} cast is overcharged, +{ModifierTables.OverchargeDamagePct * 100f:0}% damage",
		ModifierId.Gluttony => $"a full hold: x{1f + ModifierTables.GluttonyDamagePct[n]:0.#} damage · x{ModifierTables.GluttonySizeMul[n]:0.#} size · +{ModifierTables.GluttonyCostPct[n] * 100f:0}% mana",
		ModifierId.Avarice => $"up to +{ModifierTables.AvariceCoefficient[n] * ModifierTables.AvariceCapPct * 100f:0}% damage from carried shards",
		ModifierId.Wrath => $"−{(1f - ModifierTables.WrathMaxHealthMul[n]) * 100f:0}% max health · up to +{ModifierTables.WrathCoefficient[n] * 100f:0}% damage",
		ModifierId.Horizon => $"up to +{ModifierTables.HorizonPct[n] * 100f:0}% damage at the end of its reach",
		ModifierId.Ricochet => $"{ModifierTables.RicochetBounces[n]} bounces · chains leap {ModifierTables.RicochetExtraJumps[n]} more time{(n > 1 ? "s" : "")}",
		ModifierId.Hound => n == 1 ? "gentle homing" : n == 2 ? "strong homing" : "relentless homing",
		ModifierId.Needle => n == 1 ? "through 2 enemies · walls still stop it" : "through every enemy · walls still stop it",
		ModifierId.Return => n switch { 1 => $"the way back hits for {ModifierTables.ReturnDamage[1] * 100f:0}%", 2 => "the way back hits in full", _ => "back in full, then out again" },
		ModifierId.Orbit => $"{ModifierTables.OrbitRings[n]} ring{(n > 1 ? "s" : "")} around you",
		ModifierId.Amber => $"mines wait {ModifierTables.AmberMineSeconds[n]:0} seconds",
		ModifierId.Wake => $"trail stays {ModifierTables.WakeSeconds[n]:0} seconds, {(n == 1 ? "narrow" : n == 2 ? "wider" : "widest")}",
		ModifierId.Lattice => $"joins spells within {ModifierTables.LatticeRange[n] / 39.37f:0.#} m of each other",
		ModifierId.Anvil => $"bursts {ModifierTables.AnvilSplashRadius[n] / 39.37f:0.#} m wide · +{ModifierTables.AnvilDamagePct[n] * 100f:0}% damage",
		ModifierId.Sloth => $"+{ModifierTables.SlothDamagePct[n] * 100f:0}% damage · beams burn x{ModifierTables.SlothBeamWindowMul[n]:0.##} as long",
		ModifierId.Fracture => $"{ModifierTables.FractureShards[n]} pieces at {ModifierTables.FractureDamage * 100f:0}% damage",
		ModifierId.Fuse => n switch { 1 => "the charge hits again for half", 2 => "the charge hits again in full", _ => "the charge hits again for x1.5" },
		ModifierId.Ram => n == 1 ? "a shove · a wall hurts a little" : n == 2 ? "a hard shove · a wall hurts" : "thrown across the room · a wall hurts badly",
		ModifierId.BrandEmbers => $"burns for {SpellHostEffects.EmbersDps( n ):0} a second",
		ModifierId.BrandFrost => n == 1 ? "a chill" : n == 2 ? "a deep chill" : "nearly frozen",
		ModifierId.BrandStorm => $"{n} arc{(n > 1 ? "s" : "")} to enemies nearby",
		ModifierId.Turncoat => n < 2
			? "allies just crumble when their time is up"
			: $"allies explode for {SpellHostTuning.TurncoatBlastDamage( n ):0} damage when their time is up",
		ModifierId.Pyre => n == 1 ? "a small burst" : n == 2 ? "a burst" : "a big burst",
		ModifierId.Harvest => n == 1 ? "2 mana per kill" : n == 2 ? "3 mana per kill" : "5 mana per kill",
		_ => "",
	};

	private static string Ordinal( int n ) => n switch { 2 => "2nd", 3 => "3rd", _ => $"{n}th" };

	/// <summary>Roman numerals for the copies on the staff (the HUD's pips).</summary>
	public static string Tier( int copies ) => copies switch { 1 => "I", 2 => "II", 3 => "III", _ => "" };
}
