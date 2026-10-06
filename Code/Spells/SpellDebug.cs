using System;
using System.Globalization;
using System.Linq;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Spells;

/// <summary>
/// Console access to the local player's staff while the inventory does not exist: equip any spell, slot any
/// modifier (duplicates allowed), print the loadout and the resolved numbers. Everything here acts on the LOCAL
/// player's owner-authoritative components, so there is no host guard.
///
///   spell_set &lt;spell&gt;            spell_mod &lt;enchantment&gt; [copies 0..3]      spell_clear
///   spell_loadout                 spell_shards &lt;n&gt;                      spell_list
/// </summary>
public static class SpellDebug
{
	/// <summary>Log the resolved pipeline (cost, damage, count, flight) on every cast.</summary>
	[ConVar( "spell_debug" )] public static bool Verbose { get; set; }

	[ConCmd( "spell_set" )]
	public static void SetCommand( string spell = "" )
	{
		var loadout = StaffLoadout.Local;
		if ( !loadout.IsValid() ) { Log.Info( "[Spell] no local staff" ); return; }
		var def = SpellDatabase.Find( spell );
		if ( def is null ) { Log.Info( $"[Spell] unknown spell '{spell}'; one of {string.Join( ", ", SpellDatabase.All.Select( s => s.Id ) )}" ); return; }
		loadout.SetSpell( def.Id );
		Log.Info( $"[Spell] {loadout.Describe()}" );
	}

	/// <summary>`spell_mod hound` adds a copy; `spell_mod hound 3` sets the copies; `spell_mod hound 0` takes it off (the dev's privilege: a player never can).</summary>
	[ConCmd( "spell_mod" )]
	public static void ModCommand( string enchantment = "", int copies = -1 )
	{
		var loadout = StaffLoadout.Local;
		if ( !loadout.IsValid() ) { Log.Info( "[Spell] no local staff" ); return; }
		if ( !ModifierDatabase.TryParse( enchantment, out var id ) || id == ModifierId.None )
		{
			Log.Info( $"[Spell] unknown enchantment '{enchantment}'; one of {string.Join( ", ", ModifierDatabase.All.Select( m => m.Id.ToString().ToLowerInvariant() ) )}" );
			return;
		}
		if ( copies < 0 ) loadout.AddModifier( id );
		else loadout.SetModifier( id, copies );
		Log.Info( $"[Spell] {loadout.Describe()}" );
	}

	[ConCmd( "spell_clear" )]
	public static void ClearCommand()
	{
		var loadout = StaffLoadout.Local;
		if ( !loadout.IsValid() ) { Log.Info( "[Spell] no local staff" ); return; }
		loadout.ClearModifiers();
		Log.Info( $"[Spell] {loadout.Describe()}" );
	}

	/// <summary>Sets the shards carried, to try Avarice at a given purse (it fed on gold until 2026-09-19; this was `spell_gold`).</summary>
	[ConCmd( "spell_shards" )]
	public static void ShardsCommand( string amount = "" )
	{
		var shards = PlayerShards.Local;
		if ( !shards.IsValid() ) { Log.Info( "[Spell] no local shard purse" ); return; }
		if ( int.TryParse( amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n ) ) shards.Carried = Math.Max( 0, n );
		Log.Info( $"[Spell] shards carried {shards.Carried}" );
	}

	[ConCmd( "spell_list" )]
	public static void ListCommand()
	{
		foreach ( var s in SpellDatabase.All )
			Log.Info( $"[Spell] {s.Id,-16} {s.Name,-16} {s.Delivery,-10} mana {s.ManaCost,4:0}  dmg {s.Damage,4:0}  cast {s.CastDurationSeconds:0.00}s" );
		foreach ( var m in ModifierDatabase.All )
			Log.Info( $"[Spell] {m.Id.ToString().ToLowerInvariant(),-16} {m.Name,-16} {m.StageTag,-14} {(m.Black ? "[black] " : "")}{m.Banner}" );
	}

	/// <summary>Prints the staff and the numbers the active spell resolves to; toggles the dev panel when it exists.</summary>
	[ConCmd( "spell_loadout" )]
	public static void LoadoutCommand()
	{
		var loadout = StaffLoadout.Local;
		if ( !loadout.IsValid() ) { Log.Info( "[Spell] no local staff" ); return; }
		Log.Info( $"[Spell] {loadout.Describe()}" );
		var mana = PlayerMana.Local;
		if ( mana.IsValid() ) Log.Info( $"[Spell] mana {mana.Mana:0}/{mana.EffectiveMaxMana:0}{(PlayerMana.Infinite ? " (infinite)" : "")}, shards {PlayerShards.Local?.Carried ?? 0}" );
		var spell = loadout.ActiveSpell;
		if ( spell is null ) { Log.Info( "[Spell] no spell equipped" ); return; }
		var health = PlayerHealth.Local;
		Log.Info( $"[Spell] {ResolvedCast.Preview( spell, loadout.Stacks, PlayerShards.Local?.Carried ?? 0, SpellCaster.Local?.KindlingActive ?? false, health.IsValid() ? 1f - health.Health01 : 0f )}" );
		VoidMage.UI.SpellLoadout.Toggle();
	}

	// ------------------------------------------------------------------ VFX iteration (no mana, no cast)

	private static Vector3? CrosshairHit( out Vector3 normal )
	{
		normal = Vector3.Up;
		var scene = Game.ActiveScene;
		var camera = scene?.Camera;
		if ( !camera.IsValid() ) return null;
		var tr = scene.Trace.Ray( camera.WorldPosition, camera.WorldPosition + camera.WorldRotation.Forward * 4000f ).WithoutTags( AimSolver.IgnoreTags ).Run();
		if ( tr.Hit ) normal = tr.Normal;
		return tr.Hit ? tr.HitPosition : tr.EndPosition;
	}

	/// <summary>spell_burst [spell] [world|body|fizzle]: the spell's burst at the crosshair.</summary>
	[ConCmd( "spell_burst" )]
	public static void BurstCommand( string spell = "fireball", string kind = "world" )
	{
		var def = SpellDatabase.Find( spell ) ?? SpellDatabase.Get( SpellIds.Fireball );
		var at = CrosshairHit( out var normal );
		if ( at is null ) return;
		var k = kind.StartsWith( "b" ) ? ImpactKind.Body : kind.StartsWith( "f" ) ? ImpactKind.Fizzle : ImpactKind.World;
		SpellVfx.Burst( Game.ActiveScene, at.Value, normal, def, def.VfxScale, k );
	}

	/// <summary>spell_arc [spell]: an arc from the staff to the crosshair.</summary>
	[ConCmd( "spell_arc" )]
	public static void ArcCommand( string spell = "chain_lightning" )
	{
		var def = SpellDatabase.Find( spell ) ?? SpellDatabase.Get( SpellIds.ChainLightning );
		var at = CrosshairHit( out _ );
		var caster = SpellCaster.Local;
		if ( at is null || !caster.IsValid() ) return;
		var from = caster.StaffTip() ?? caster.WorldPosition + Vector3.Up * 60f;
		SpellVfx.ChainArc( Game.ActiveScene, def, new[] { from, at.Value }, def.VfxScale );
	}

	/// <summary>spell_fx [model] [add|alpha]: an FX mesh 120 u ahead for a size check (vfx/models/&lt;model&gt;.vmdl), gone after 6 s.</summary>
	[ConCmd( "spell_fx" )]
	public static void FxCommand( string model = "fx_crystal_02", string blend = "alpha" )
	{
		var scene = Game.ActiveScene;
		var camera = scene?.Camera;
		if ( !camera.IsValid() ) return;
		var root = new GameObject( true, "fx_check" );
		root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		root.WorldPosition = camera.WorldPosition + camera.WorldRotation.Forward * 120f;
		root.Components.Create<VfxLifetime>().Seconds = 6f;
		var r = StyleVfx.AddMesh( root, model, model, SpellPalette.Ice.Core, 1f, blend.StartsWith( "ad" ) );
		Log.Info( r is null ? $"[Spell] vfx/models/{model}.vmdl did not load (compiled in the editor yet?)" : $"[Spell] {model}: bounds {r.Model.Bounds.Size:0} u" );
	}

	private static int ParseSpellSlot( string slot ) => (slot ?? "").Trim().ToLowerInvariant() switch
	{
		"a" or "1" or "0" => 0,
		"b" or "2" => 1,
		_ => -1,
	};
}
