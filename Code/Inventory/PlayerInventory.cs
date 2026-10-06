using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.Run;
using VoidMage.Spells;

namespace VoidMage.Inventory;

/// <summary>What taking a scroll did: whether it went on the staff, whether it was the first time ever (the seal broke), and the spell it pushed off.</summary>
public readonly record struct TakeResult( bool Ok, bool FirstTime, Scroll Dropped );

/// <summary>
/// The player's scrolls, spells and skins, the Isaac way (user, 2026-09-20; it was a satchel, a library and four
/// sockets moved about in a HUD, and the user found that loop "hard to grasp").
///
/// A scroll is TAKEN where it lies, with E, and that is that: an enchantment goes on the staff for the rest of the run
/// (any number of different ones, three copies of each at most, after which it leaves the drop pool) and can never be
/// taken off; a spell scroll replaces the staff's one spell, and the old spell drops at the player's feet so the
/// swap can be undone. The first time a scroll is ever taken it is sealed and unknown; once it has been on the
/// staff it is KNOWN for good (the same list as the index) and every later copy drops open, name and all, so taking
/// it is a choice. Spells taken are also unlocked for good: an unlocked spell can be chosen as the STARTING spell in
/// the village.
///
/// A run's scrolls end with the run, like its boons: a death or a walk home strips the staff back to the starting
/// spell, except that a DEATH lets the player keep one enchantment of their choosing (one copy, picked on the death
/// screen; user, 2026-09-22). Scrolls bought in the village go straight on the staff and ride into the next run.
/// Unlocked spells, known scrolls and skins are saved the moment they happen and are never lost. Owner-local; only the
/// skin choice is synced.
/// </summary>
[Title( "Player Inventory" ), Category( "Void Mage" ), Icon( "inventory_2" )]
public sealed class PlayerInventory : Component
{
	/// <summary>How often a spell scroll offers a spell the player has never had, while the run has not shown one yet (otherwise a known spell they are not holding).</summary>
	[ConVar( "scroll_new_spell_chance" )] public static float NewSpellChance { get; set; } = 0.75f;
	/// <summary>The same, once the run has already shown a never-had spell: still likely, so a small book is not the same known spell floor after floor.</summary>
	[ConVar( "scroll_new_spell_chance_after" )] public static float NewSpellChanceAfter { get; set; } = 0.4f;

	[Sync] public string SwordSkin { get; set; } = WeaponSkins.DefaultSword;
	[Sync] public string StaffSkin { get; set; } = WeaponSkins.DefaultStaff;

	public static PlayerInventory Local { get; private set; }

	/// <summary>Bumped on every change: the HUD's rebuild hash.</summary>
	public int Version { get; private set; }

	private readonly HashSet<string> _unlocked = new() { SpellIds.Fireball };
	private readonly HashSet<string> _discovered = new();
	private readonly HashSet<string> _skins = new();
	/// <summary>Kills of monsters carrying a weapon not owned yet, by skin id: at <see cref="SkinKillsNeeded"/> it is unlocked.</summary>
	private readonly Dictionary<string, int> _skinKills = new();

	/// <summary>How many of its carriers have to die before a carried weapon is the player's (user, 2026-09-21: 10, it was the first kill).</summary>
	[ConVar( "skin_kills_needed" )] public static int SkinKillsNeeded { get; set; } = 10;
	/// <summary>Unlocked or discovered this session and not looked at in the HUD yet (scroll keys and "skin:id"); not saved.</summary>
	private readonly HashSet<string> _unseen = new();
	/// <summary>The enchantments on the staff in the order they were first taken (the HUD's row).</summary>
	private readonly List<ModifierId> _order = new();
	private StaffLoadout _staff;
	private bool _loaded;
	private bool _newSpellOffered;

	public StaffLoadout Staff => _staff;
	/// <summary>The save has been read: the owned skins are known (the shop waits for this before it rolls its weapon).</summary>
	public bool IsLoaded => _loaded;
	/// <summary>The spell a run starts with: any unlocked spell, chosen in the village.</summary>
	public string StartingSpell { get; private set; } = SpellIds.Fireball;

	/// <summary>In the village of a real run scene.</summary>
	public static bool AtHome => RunManager.Instance.IsValid() && !RunManager.InDungeon;
	/// <summary>The starting spell can be changed: at home, or in a test scene without a run.</summary>
	public static bool CanChooseSpell => !RunManager.Instance.IsValid() || !RunManager.InDungeon;

	/// <summary>A weapon skin just became the player's (a kill, a crate): the banner (UI.UnlockNotice) shows it.</summary>
	public static event Action<WeaponSkin> SkinFound;
	/// <summary>An enchantment just went on the staff. `firstTime`: its seal broke, the player did not know what it was.</summary>
	public static event Action<Scroll, bool> ScrollTaken;
	/// <summary>An enchantment was just bought in the shop: the same banner.</summary>
	public static event Action<Scroll> ScrollBought;
	/// <summary>A spell was unlocked for good (the first time its scroll was taken): the same banner.</summary>
	public static event Action<SpellDefinition> SpellLearned;
	/// <summary>The staff's spell was swapped for a known one.</summary>
	public static event Action<SpellDefinition> SpellEquipped;

	protected override void OnStart()
	{
		if ( IsProxy ) return;
		Local = this;
		_staff = Components.Get<StaffLoadout>( FindMode.EverythingInSelfAndDescendants )
			?? Components.Get<StaffLoadout>( FindMode.EverythingInSelfAndAncestors );
		Load();
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
	}

	/// <summary>The player object carries one; the run's spawner and the HUD call this so a prefab without it still gets one.</summary>
	public static PlayerInventory EnsureOn( GameObject player )
	{
		if ( !player.IsValid() ) return null;
		return player.Components.Get<PlayerInventory>( FindMode.EverythingInSelfAndDescendants ) ?? player.Components.Create<PlayerInventory>();
	}

	// ------------------------------------------------------------------ queries

	public bool IsUnlocked( string spellId ) => !string.IsNullOrEmpty( spellId ) && _unlocked.Contains( spellId );
	public int UnlockedCount => SpellDatabase.All.Count( s => IsUnlocked( s.Id ) );
	public IEnumerable<SpellDefinition> LockedSpells => SpellDatabase.All.Where( s => !IsUnlocked( s.Id ) );

	/// <summary>Known: an unlocked spell, or an enchantment that has been on the staff at least once. A known scroll drops open.</summary>
	public bool IsDiscovered( Scroll s ) => s.IsSpell ? IsUnlocked( s.SpellId ) : !s.IsEmpty && _discovered.Contains( s.Opened.Key );
	public int DiscoveredCount( bool spells ) => Scroll.All.Count( s => s.IsSpell == spells && IsDiscovered( s ) );
	public bool HasSkin( string id ) => WeaponSkins.Get( id ) is { } skin && (skin.Starter || _skins.Contains( id ));
	public int SkinCount( HandSide hand ) => WeaponSkins.For( hand ).Count( s => HasSkin( s.Id ) );
	/// <summary>Carriers of this weapon killed so far (it stops counting once the weapon is owned).</summary>
	public int SkinKills( string id ) => id is not null && _skinKills.TryGetValue( id, out var n ) ? n : 0;

	public bool IsUnseen( string key ) => _unseen.Contains( key );
	public bool AnyUnseen( bool skins ) => _unseen.Any( k => k.StartsWith( "skin:", StringComparison.Ordinal ) == skins );
	public void MarkSeen( string key )
	{
		if ( _unseen.Remove( key ) ) Version++;
	}

	public ModifierStacks Stacks => _staff.IsValid() ? _staff.Stacks : ModifierStacks.Empty;
	/// <summary>Copies of an enchantment on the staff.</summary>
	public int Copies( ModifierId id ) => Stacks[id];

	/// <summary>The enchantments on the staff, in the order they were first taken.</summary>
	public IReadOnlyList<ModifierId> RunScrolls
	{
		get
		{
			var stacks = Stacks;
			// the staff is the truth (a dev command, a reset): the order only ever follows it
			_order.RemoveAll( id => stacks[id] <= 0 );
			foreach ( var def in ModifierDatabase.All )
				if ( stacks[def.Id] > 0 && !_order.Contains( def.Id ) ) _order.Add( def.Id );
			return _order;
		}
	}

	/// <summary>Whether E would do anything: an enchantment under three copies, a spell that is not the one on the staff.</summary>
	public bool CanTake( Scroll s )
	{
		s = s.Opened;
		if ( !s.IsValid || !_staff.IsValid() ) return false;
		if ( s.IsSpell ) return _staff.SpellId != s.SpellId;
		return Stacks[s.Modifier] < ModifierStacks.MaxCopies;
	}

	// ------------------------------------------------------------------ taking

	/// <summary>
	/// E on a scroll in the world. An enchantment goes on the staff for the rest of the run. A spell replaces the
	/// staff's spell (and is unlocked for good the first time); the spell it replaced comes back as `Dropped`, for
	/// the pickup to leave at the player's feet.
	/// </summary>
	public TakeResult Take( Scroll s )
	{
		s = s.Opened;
		if ( IsProxy || !CanTake( s ) ) return default;
		var first = !IsDiscovered( s );
		if ( s.IsSpell )
		{
			var old = Scroll.ForSpell( _staff.SpellId );
			_staff.SetSpell( s.SpellId );
			if ( first ) Unlock( s.SpellId );
			else SpellEquipped?.Invoke( s.Spell );
			Changed();
			return new TakeResult( true, first, old );
		}

		if ( !_staff.AddModifier( s.Modifier ) ) return default;
		if ( !_order.Contains( s.Modifier ) ) _order.Add( s.Modifier );
		Discover( s );
		Changed();
		ScrollTaken?.Invoke( s, first );
		return new TakeResult( true, first, Scroll.Empty );
	}

	/// <summary>An enchantment bought off the shop's table: straight onto the staff, where it rides into the next run. False (nothing taken) at three copies.</summary>
	public bool Buy( Scroll s )
	{
		s = s.Opened;
		if ( IsProxy || !s.IsModifier || !CanTake( s ) ) return false;
		if ( !_staff.AddModifier( s.Modifier ) ) return false;
		if ( !_order.Contains( s.Modifier ) ) _order.Add( s.Modifier );
		Discover( s );
		Changed();
		ScrollBought?.Invoke( s );
		return true;
	}

	/// <summary>The village: the spell the next run starts with. Only an unlocked spell, only where it may be chosen.</summary>
	public bool ChooseStartingSpell( string spellId )
	{
		if ( IsProxy || !IsUnlocked( spellId ) || !CanChooseSpell || !_staff.IsValid() ) return false;
		StartingSpell = spellId;
		_staff.SetSpell( spellId );
		Changed();
		return true;
	}

	// ------------------------------------------------------------------ what drops

	/// <summary>A random enchantment the staff can still take (nothing at three copies), or empty when the whole pool is capped.</summary>
	public Scroll RollEnchantment()
	{
		var stacks = Stacks;
		var pool = Scroll.AllModifiers.Where( s => stacks[s.Modifier] < ModifierStacks.MaxCopies ).ToList();
		return pool.Count == 0 ? Scroll.Empty : pool[Game.Random.Next( pool.Count )];
	}

	/// <summary>
	/// A spell scroll for this player: by preference one they have never had (less so once the run has shown one,
	/// so the book fills over a few runs, not one), otherwise a spell they know but are not holding. Empty when
	/// there is nothing to offer: the caller drops an enchantment instead.
	/// </summary>
	public Scroll RollSpell()
	{
		var held = _staff.IsValid() ? _staff.SpellId : "";
		var known = SpellDatabase.All.Where( s => IsUnlocked( s.Id ) && s.Id != held ).ToList();
		var fresh = LockedSpells.Where( s => s.Id != held ).ToList();
		var chance = _newSpellOffered ? NewSpellChanceAfter : NewSpellChance;
		if ( fresh.Count > 0 && (known.Count == 0 || Game.Random.Float( 0f, 1f ) < chance) )
		{
			_newSpellOffered = true;
			return Scroll.ForSpell( fresh[Game.Random.Next( fresh.Count )].Id );
		}
		return known.Count == 0 ? Scroll.Empty : Scroll.ForSpell( known[Game.Random.Next( known.Count )].Id );
	}

	// ------------------------------------------------------------------ knowledge

	public void Unlock( string spellId )
	{
		if ( !SpellDatabase.IsValid( spellId ) || !_unlocked.Add( spellId ) ) return;
		_unseen.Add( Scroll.ForSpell( spellId ).Key );
		Version++;
		Log.Info( $"[Inventory] spell unlocked: {SpellDatabase.Get( spellId ).Name}" );
		SaveDiscoveries();
		if ( !IsProxy ) SpellLearned?.Invoke( SpellDatabase.Get( spellId ) );
	}

	public void Discover( Scroll s )
	{
		s = s.Opened;
		if ( !s.IsModifier || !_discovered.Add( s.Key ) ) return;
		_unseen.Add( s.Key );
		Version++;
		SaveDiscoveries();
	}

	/// <summary>A weapon skin is the player's for good (a kill, a crate, the shop): saved the moment it happens.</summary>
	public void DiscoverSkin( string id )
	{
		if ( WeaponSkins.Get( id ) is not { } skin || HasSkin( id ) ) return;
		_skins.Add( id );
		_unseen.Add( "skin:" + id );
		Version++;
		Log.Info( $"[Inventory] weapon unlocked: {skin.Name}" );
		SaveDiscoveries();
		if ( !IsProxy ) SkinFound?.Invoke( skin );
	}

	/// <summary>Owner: one more carrier of this weapon is dead. The tenth (SkinKillsNeeded) makes the weapon the player's; every one is saved.</summary>
	public void CountSkinKill( string id )
	{
		if ( IsProxy || WeaponSkins.Get( id ) is null || HasSkin( id ) ) return;
		int kills = SkinKills( id ) + 1;
		_skinKills[id] = kills;
		if ( kills >= Math.Max( 1, SkinKillsNeeded ) ) { DiscoverSkin( id ); return; }
		Version++;
		SaveDiscoveries();
	}

	/// <summary>
	/// Host, on a kill: whoever swung is one kill nearer the weapon its victim carried (user, 2026-09-17; ten kills
	/// since 2026-09-21). Skins are owner-local (each player's own save), so this goes to the killer's client; solo,
	/// the host is that client.
	/// </summary>
	public static void UnlockSkinFor( GameObject player, string id )
	{
		if ( !player.IsValid() || string.IsNullOrEmpty( id ) ) return;
		var inv = player.Components.Get<PlayerInventory>( FindMode.EverythingInSelfAndDescendants )
			?? player.Components.Get<PlayerInventory>( FindMode.EverythingInSelfAndAncestors );
		// HasSkin is not checked here: on the host a remote player's skins are unknown, and CountSkinKill decides anyway
		if ( !inv.IsValid() ) return;
		if ( !inv.IsProxy ) inv.CountSkinKill( id );   // solo, or the host killed it itself: no round trip
		else inv.UnlockSkinRpc( id );
	}

	[Rpc.Owner]
	private void UnlockSkinRpc( string id ) => CountSkinKill( id );

	public bool EquipSkin( string id )
	{
		if ( IsProxy || !HasSkin( id ) ) return false;
		var skin = WeaponSkins.Get( id );
		if ( skin.IsSword ) SwordSkin = id; else StaffSkin = id;
		Version++;
		SaveDiscoveries();
		return true;
	}

	public string SkinFor( HandSide hand ) => hand == HandSide.Left ? SwordSkin : StaffSkin;

	/// <summary>Dev (the weapon tuner): hold any skin, owned or not, without saving the choice.</summary>
	public void PreviewSkin( string id )
	{
		if ( IsProxy || WeaponSkins.Get( id ) is not { } skin ) return;
		if ( skin.IsSword ) SwordSkin = id; else StaffSkin = id;
		Version++;
	}

	// ------------------------------------------------------------------ the run (RunManager)

	/// <summary>Leaving the village: the at-home state is written (what was bought rides along), and the run may show one new spell.</summary>
	public void OnRunStart()
	{
		_newSpellOffered = false;
		SaveHome( force: true );
	}

	/// <summary>Home through a portal: the run is over and its scrolls with it, like its boons.</summary>
	public void OnReturnHome() => EndRun( ModifierId.None );

	/// <summary>
	/// Died in a run: the same, except for the ONE enchantment the death screen let the player keep (user, 2026-09-22:
	/// "select and keep 1 enchantment from that run... only 1 per run"). One copy of it stays on the staff and rides
	/// into the next run like a bought scroll would. The spells learned and the scrolls now known stay; they were
	/// saved as they happened.
	/// </summary>
	public void OnDeath( ModifierId keep ) => EndRun( keep );

	private void EndRun( ModifierId keep )
	{
		if ( IsProxy ) return;
		bool kept = keep != ModifierId.None && Stacks[keep] > 0;
		if ( _staff.IsValid() )
		{
			_staff.SetStacks( kept ? ModifierStacks.Empty.Add( keep ) : ModifierStacks.Empty );
			_staff.SetSpell( IsUnlocked( StartingSpell ) ? StartingSpell : SpellIds.Fireball );
		}
		_order.Clear();
		if ( kept ) _order.Add( keep );
		Version++;
		SaveHome( force: true );
		if ( kept ) Log.Info( $"[Inventory] kept {Scroll.ForModifier( keep ).Name} through the death" );
	}

	// ------------------------------------------------------------------ internals

	/// <summary>After every change: the HUD rebuilds, home saves.</summary>
	private void Changed()
	{
		Version++;
		SaveHome();
	}

	// ------------------------------------------------------------------ save

	private void Load()
	{
		if ( _loaded ) return;
		_loaded = true;
		RunSave.Load();
		var data = RunSave.Data.Inventory;
		if ( data is null )
		{
			// a new player: Fireball and nothing else (a test scene keeps its prefab's staff)
			if ( RunManager.Instance.IsValid() ) ApplyStaff( null );
			SaveHome( force: true );
			Version++;
			return;
		}

		foreach ( var id in data.UnlockedSpells ) if ( SpellDatabase.IsValid( id ) ) _unlocked.Add( id );
		foreach ( var key in data.Discovered ) if ( Scroll.FromKey( key ) is { IsModifier: true } s ) _discovered.Add( s.Opened.Key );
		foreach ( var id in data.Skins ) if ( WeaponSkins.Get( id ) is not null ) _skins.Add( id );
		foreach ( var (id, kills) in data.SkinKills ?? new() ) if ( WeaponSkins.Get( id ) is not null ) _skinKills[id] = kills;
		SwordSkin = HasSkin( data.SwordSkin ) ? data.SwordSkin : WeaponSkins.DefaultSword;
		StaffSkin = HasSkin( data.StaffSkin ) ? data.StaffSkin : WeaponSkins.DefaultStaff;
		StartingSpell = IsUnlocked( data.SpellA ) ? data.SpellA : SpellIds.Fireball;
		// a test scene keeps the staff its prefab was set up with
		if ( RunManager.Instance.IsValid() ) ApplyStaff( data );
		Version++;
		Log.Info( $"[Inventory] loaded: {UnlockedCount}/{SpellDatabase.All.Length} spells, starting with {StartingSpell}, {_discovered.Count}/{Scroll.AllModifiers.Length} enchantments known" );
	}

	/// <summary>The staff as it stood at home (null: a lone Fireball): the starting spell and whatever was bought since the last run.</summary>
	private void ApplyStaff( InventorySaveData data )
	{
		if ( !_staff.IsValid() ) return;
		_staff.SetSpell( StartingSpell );
		var stacks = ModifierStacks.Empty;
		_order.Clear();
		if ( data is not null )
		{
			foreach ( var key in data.Modifiers )
			{
				var id = Scroll.FromKey( key ).Modifier;
				if ( id == ModifierId.None ) continue;
				stacks = stacks.Add( id );
				if ( !_order.Contains( id ) ) _order.Add( id );
			}
		}
		_staff.SetStacks( stacks );
	}

	/// <summary>Write the at-home state: only in the village of a run scene (a test scene never touches the save's staff).</summary>
	private void SaveHome( bool force = false )
	{
		if ( IsProxy || !_staff.IsValid() || !RunManager.Instance.IsValid() ) return;
		if ( !force && RunManager.InDungeon ) return;
		var data = RunSave.Data.Inventory ??= new InventorySaveData();
		data.SpellA = StartingSpell ?? SpellIds.Fireball;
		data.SpellB = "";
		var stacks = _staff.Stacks;
		data.Modifiers = RunScrolls.SelectMany( id => Enumerable.Repeat( Scroll.ForModifier( id ).Key, stacks[id] ) ).ToList();
		// the satchel and the library are gone (2026-09-20): an old save's are dropped here
		data.Satchel = new();
		data.Library = new();
		WriteDiscoveries( data );
		RunSave.Save();
	}

	private void SaveDiscoveries()
	{
		if ( IsProxy || !_loaded ) return;
		var data = RunSave.Data.Inventory;
		if ( data is null )
		{
			// nothing at home yet: in a run scene that is the whole state, in a test scene only what was found
			if ( RunManager.Instance.IsValid() && !RunManager.InDungeon ) { SaveHome( force: true ); return; }
			data = RunSave.Data.Inventory = new InventorySaveData { SpellA = SpellIds.Fireball };
		}
		WriteDiscoveries( data );
		RunSave.Save();
	}

	private void WriteDiscoveries( InventorySaveData data )
	{
		data.UnlockedSpells = SpellDatabase.All.Where( s => IsUnlocked( s.Id ) ).Select( s => s.Id ).ToList();
		data.Discovered = _discovered.OrderBy( k => Scroll.FromKey( k ).CatalogueOrder ).ToList();
		data.Skins = _skins.ToList();
		data.SkinKills = _skinKills.Where( k => !_skins.Contains( k.Key ) ).ToDictionary( k => k.Key, k => k.Value );
		data.SwordSkin = SwordSkin ?? "";
		data.StaffSkin = StaffSkin ?? "";
	}

	// ------------------------------------------------------------------ dev

	private static PlayerInventory Dev()
	{
		var inv = Local;
		if ( !inv.IsValid() && StaffLoadout.Local.IsValid() ) inv = EnsureOn( StaffLoadout.Local.GameObject );
		if ( !inv.IsValid() ) Log.Warning( "[Inventory] no local player" );
		return inv;
	}

	/// <summary>
	/// `inv_give hound` / `inv_give random 3` / `inv_give spell` / `inv_give all`: scrolls straight onto the staff, as
	/// if taken. "random" is an enchantment the staff can still take, "spell" a spell scroll as a floor would roll it,
	/// "all" one copy of every enchantment.
	/// </summary>
	[ConCmd( "inv_give" )]
	public static void GiveCommand( string name = "random", int count = 1 )
	{
		var inv = Dev(); if ( !inv.IsValid() ) return;
		if ( name == "all" )
		{
			foreach ( var m in Scroll.AllModifiers ) inv.Take( m );
			Log.Info( $"[Inventory] staff: {inv._staff?.Describe()}" );
			return;
		}
		for ( int i = 0; i < Math.Max( 1, count ); i++ )
		{
			var s = name == "random" ? inv.RollEnchantment() : name == "spell" ? inv.RollSpell() : Scroll.Find( name );
			if ( s.IsEmpty ) { Log.Warning( $"[Inventory] no scroll called '{name}' (or nothing left to give)" ); return; }
			var took = inv.Take( s );
			Log.Info( $"[Inventory] {s.Name}: {(took.Ok ? (took.FirstTime ? "taken, the seal broke" : "taken") : "refused (three copies, or the spell already held)")}" );
		}
	}

	/// <summary>`inv_drop hound` / `inv_drop random` / `inv_drop spell`: the scroll in the world in front of the player, to pick up with E.</summary>
	[ConCmd( "inv_drop" )]
	public static void DropCommand( string name = "random" )
	{
		var inv = Dev(); if ( !inv.IsValid() ) return;
		var s = name == "random" ? inv.RollEnchantment() : name == "spell" ? inv.RollSpell() : Scroll.Find( name );
		if ( s.IsEmpty ) { Log.Warning( $"[Inventory] no scroll called '{name}'" ); return; }
		var at = inv.WorldPosition + inv.WorldRotation.Forward.WithZ( 0f ).Normal * 70f + Vector3.Up * 30f;
		ScrollPickup.Spawn( inv.Scene, at, s, null, Vector3.Zero );
	}

	/// <summary>`inv_clear`: every enchantment off the staff (the run's scrolls, not what is known).</summary>
	[ConCmd( "inv_clear" )]
	public static void ClearCommand()
	{
		var inv = Dev(); if ( !inv.IsValid() || !inv._staff.IsValid() ) return;
		inv._staff.ClearModifiers();
		inv._order.Clear();
		inv.Changed();
	}

	/// <summary>`inv_skin crystal` / `inv_skin all`: find weapon skins.</summary>
	[ConCmd( "inv_skin" )]
	public static void SkinCommand( string name = "all" )
	{
		var inv = Dev(); if ( !inv.IsValid() ) return;
		var skins = name == "all" ? WeaponSkins.All : new[] { WeaponSkins.Find( name ) }.Where( s => s is not null ).ToArray();
		if ( skins.Length == 0 ) { Log.Warning( $"[Inventory] no skin called '{name}'" ); return; }
		foreach ( var skin in skins ) inv.DiscoverSkin( skin.Id );
	}

	/// <summary>`inv_unlock_all`: every spell unlocked, every enchantment known, every skin found (nothing put on the staff).</summary>
	[ConCmd( "inv_unlock_all" )]
	public static void UnlockAllCommand()
	{
		var inv = Dev(); if ( !inv.IsValid() ) return;
		foreach ( var spell in SpellDatabase.All ) inv.Unlock( spell.Id );
		foreach ( var s in Scroll.AllModifiers ) inv.Discover( s );
		foreach ( var skin in WeaponSkins.All ) inv.DiscoverSkin( skin.Id );
	}

	/// <summary>`inv_reset`: forget the inventory save (a new player's lone Fireball, nothing known).</summary>
	[ConCmd( "inv_reset" )]
	public static void ResetCommand()
	{
		var inv = Dev(); if ( !inv.IsValid() ) return;
		RunSave.Data.Inventory = null;
		inv._unlocked.Clear();
		inv._unlocked.Add( SpellIds.Fireball );
		inv._discovered.Clear();
		inv._skins.Clear();
		inv._skinKills.Clear();
		inv._unseen.Clear();
		inv._order.Clear();
		inv.StartingSpell = SpellIds.Fireball;
		inv.SwordSkin = WeaponSkins.DefaultSword;
		inv.StaffSkin = WeaponSkins.DefaultStaff;
		inv.ApplyStaff( null );
		inv.Version++;
		inv.SaveHome( force: true );
		RunSave.Save();
		Log.Info( "[Inventory] reset" );
	}

	[ConCmd( "inv_list" )]
	public static void ListCommand()
	{
		var inv = Dev(); if ( !inv.IsValid() ) return;
		Log.Info( $"[Inventory] staff: {inv._staff?.Describe()}" );
		Log.Info( $"[Inventory] spells: {string.Join( ", ", SpellDatabase.All.Where( s => inv.IsUnlocked( s.Id ) ).Select( s => s.Name ) )} (starting with {inv.StartingSpell})" );
		Log.Info( $"[Inventory] enchantments known {inv._discovered.Count}/{Scroll.AllModifiers.Length}, skins {inv._skins.Count + WeaponSkins.All.Count( s => s.Starter )}/{WeaponSkins.All.Length}" );
	}
}
