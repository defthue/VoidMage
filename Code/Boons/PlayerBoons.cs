using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Boons;

/// <summary>
/// The boons and pacts the player picked this run. The owned ids are synced, so any peer (the host rolling a
/// sword crit, dropping shards for a kill) computes the same totals from them. Same-stat lines add up. Everything
/// is cleared when the run ends (RunManager.GoHome, death or return). RunManager adds this to the player it spawns.
/// </summary>
[Title( "Player Boons" ), Category( "Void Mage" ), Icon( "auto_awesome" )]
public sealed class PlayerBoons : Component
{
	/// <summary>Owned ids in pick order, comma separated.</summary>
	[Sync] public string OwnedIds { get; set; } = "";

	public static PlayerBoons Local { get; private set; }

	/// <summary>Damage taken can be cut by at most this much, however the reductions stack.</summary>
	public const float MaxDamageReduction = 0.5f;
	/// <summary>Life steal of both kinds together heals at most this per second.</summary>
	public const float MaxLifeStealPerSecond = 5f;

	/// <summary>The last grant, for the Tab panel's pop-in.</summary>
	public string LastGrantedId { get; private set; }
	public float LastGrantAt { get; private set; } = float.NegativeInfinity;

	private string _cachedFor;
	private readonly List<BoonDefinition> _owned = new();
	private readonly Dictionary<BoonStat, float> _totals = new();

	protected override void OnStart()
	{
		if ( !IsProxy ) Local = this;
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
	}

	public IReadOnlyList<BoonDefinition> Owned { get { Refresh(); return _owned; } }

	public bool Owns( string id ) { Refresh(); return _owned.Any( b => string.Equals( b.Id, id, StringComparison.OrdinalIgnoreCase ) ); }

	/// <summary>The summed value of a stat (0 when nothing owned touches it).</summary>
	public float Get( BoonStat stat ) { Refresh(); return _totals.TryGetValue( stat, out var v ) ? v : 0f; }

	public bool Has( BoonStat flag ) => Get( flag ) != 0f;

	/// <summary>A multiplier from a percent stat: 1 + total, never below `floor`.</summary>
	public float Scale( BoonStat stat, float floor = 0.1f ) => MathF.Max( floor, 1f + Get( stat ) );

	/// <summary>The local player's value of a stat; 0 without a player.</summary>
	public static float LocalGet( BoonStat stat ) => Local.IsValid() ? Local.Get( stat ) : 0f;
	public static float LocalScale( BoonStat stat, float floor = 0.1f ) => Local.IsValid() ? Local.Scale( stat, floor ) : 1f;

	/// <summary>The boons on an attacker root (a player's controller object), or null for anything else.</summary>
	public static PlayerBoons Find( GameObject attackerRoot )
	{
		if ( !attackerRoot.IsValid() ) return null;
		return attackerRoot.Components.Get<PlayerBoons>( FindMode.EverythingInSelfAndDescendants )
			?? attackerRoot.Components.Get<PlayerBoons>( FindMode.EverythingInSelfAndAncestors );
	}

	/// <summary>The multiplier on damage the player takes: the reductions are capped at MaxDamageReduction, the costs are not.</summary>
	public float DamageTakenScale( bool trap )
	{
		var total = Get( BoonStat.DamageTaken );
		if ( trap ) total += Get( BoonStat.TrapDamage );
		return MathF.Max( 1f - MaxDamageReduction, 1f + total );
	}

	private void Refresh()
	{
		var ids = OwnedIds ?? "";
		if ( _cachedFor == ids ) return;
		_cachedFor = ids;
		_owned.Clear();
		_totals.Clear();
		foreach ( var id in ids.Split( ',', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var def = BoonDatabase.Get( id );
			if ( def is null ) continue;
			_owned.Add( def );
			foreach ( var line in def.Lines )
				_totals[line.Stat] = (_totals.TryGetValue( line.Stat, out var v ) ? v : 0f) + (BoonText.IsFlag( line.Stat ) ? 1f : line.Value);
		}
	}

	// ------------------------------------------------------------------ owner

	/// <summary>Owner: take a card. The one-shot parts land here (a max-health gain also heals by that much).</summary>
	public bool Grant( string id )
	{
		if ( IsProxy ) return false;
		var def = BoonDatabase.Get( id );
		if ( def is null || Owns( def.Id ) ) return false;
		OwnedIds = string.IsNullOrEmpty( OwnedIds ) ? def.Id : $"{OwnedIds},{def.Id}";
		LastGrantedId = def.Id;
		LastGrantAt = Time.Now;

		var root = GameObject;
		var health = root.Components.Get<PlayerHealth>( FindMode.EverythingInSelfAndDescendants );
		var mana = root.Components.Get<PlayerMana>( FindMode.EverythingInSelfAndDescendants );
		var potions = root.Components.Get<PlayerPotions>( FindMode.EverythingInSelfAndDescendants );
		foreach ( var line in def.Lines )
		{
			switch ( line.Stat )
			{
				case BoonStat.MaxHealth when health.IsValid():
					if ( line.Value > 0f ) health.Heal( line.Value );
					else health.ClampToMax();
					break;
				case BoonStat.MaxMana when mana.IsValid() && line.Value > 0f:
					mana.Restore( line.Value );
					break;
				case BoonStat.HealthPotions when potions.IsValid():
					for ( int i = 0; i < (int)line.Value; i++ ) potions.TryAdd( PlayerPotions.Kind.Heal );
					break;
				// the card is already owned by now, so Capacity here is the widened one: the new slots come in full
				case BoonStat.FillPotions when potions.IsValid():
					potions.Fill();
					break;
				case BoonStat.NoPotions when potions.IsValid():
					potions.Discard();
					break;
				case BoonStat.SecretWallsOnMap:
					UI.DungeonMinimap.RefreshSecretMarkers();
					break;
			}
		}
		Log.Info( $"[Boons] took {def.Name}: {string.Join( ", ", def.Lines.Select( l => l.Text ) )}" );
		return true;
	}

	/// <summary>Owner: the run is over. Anything a boon stretched (potion capacity, max health) snaps back.</summary>
	public void Clear()
	{
		if ( IsProxy ) return;
		OwnedIds = "";
		LastGrantedId = null;
		var root = GameObject;
		root.Components.Get<PlayerHealth>( FindMode.EverythingInSelfAndDescendants )?.ClampToMax();
		root.Components.Get<PlayerPotions>( FindMode.EverythingInSelfAndDescendants )?.ClampToCapacity();
		UI.DungeonMinimap.RefreshSecretMarkers();
	}

	// ------------------------------------------------------------------ dev

	/// <summary>`boon_give honed_edge`: take a card without the screen.</summary>
	[ConCmd( "boon_give" )]
	public static void GiveCommand( string id )
	{
		if ( !Local.IsValid() ) { Log.Warning( "[Boons] no local player" ); return; }
		if ( BoonDatabase.Get( id ) is null ) { Log.Warning( $"[Boons] no boon '{id}'. Ids: {string.Join( " ", BoonDatabase.All.Select( b => b.Id ) )}" ); return; }
		if ( !Local.Grant( id ) ) Log.Info( $"[Boons] already own {id}" );
	}

	/// <summary>`boon_clear`: drop every boon.</summary>
	[ConCmd( "boon_clear" )]
	public static void ClearCommand() => Local?.Clear();

	/// <summary>`boon_list`: what is owned and the totals it adds up to.</summary>
	[ConCmd( "boon_list" )]
	public static void ListCommand()
	{
		if ( !Local.IsValid() ) { Log.Warning( "[Boons] no local player" ); return; }
		var owned = Local.Owned;
		Log.Info( $"[Boons] {owned.Count} owned: {(owned.Count == 0 ? "none" : string.Join( ", ", owned.Select( b => b.Name ) ))}" );
		for ( var stat = (BoonStat)0; stat <= BoonStat.SecretWallsOnMap; stat++ )
		{
			var v = Local.Get( stat );
			if ( v != 0f ) Log.Info( $"[Boons]   {BoonText.Format( stat, v )}" );
		}
	}
}
