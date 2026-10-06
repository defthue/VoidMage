using System;
using System.Linq;
using Sandbox;
using VoidMage.Combat;
using VoidMage.Run;

namespace VoidMage.Character;

/// <summary>
/// The potions, owner side: E picks up the potion the player looks at (PotionPickup), 1 drinks a healing potion
/// and 2 a mana potion. A drink is the UAL Drink clip on the first-person arms (FirstPersonArms.TryStartDrink
/// owns the clip and the bottle prop): the player can move at no more than crouch speed for its length
/// (CharacterLocomotionAnimator.DriveSpeeds), any hit cancels it, the heal lands on <see cref="HealFrame"/> when the hand leaves the
/// mouth (the potion is only consumed then, so a hit before it keeps the bottle), and on <see cref="ThrowFrame"/>
/// the flick lets the bottle go for real: a ThrownBottle with the hand's own speed that shatters on the first
/// wall or floor. A cancel between the two drops the bottle out of the hand instead.
/// </summary>
[Title( "Potion Drinker" ), Category( "Void Mage" ), Icon( "local_drink" )]
public sealed class PotionDrinker : Component
{
	/// <summary>The bottles in the drinking hand and the thrown empties: the pack's flasks with the cork off (the user's own Blender copies).</summary>
	public const string HealModel = "dungeonpack/sm_item_potion_07_uncorked.vmdl";
	public const string ManaModel = "dungeonpack/sm_item_potion_06_uncorked.vmdl";
	/// <summary>The corked originals: the pickups on the tables (DecorCatalog), and the reference the hand grip is tuned against.</summary>
	public const string HealCorkedModel = "dungeonpack/sm_item_potion_07.vmdl";
	public const string ManaCorkedModel = "dungeonpack/sm_item_potion_06.vmdl";

	private static Model _healCorked, _manaCorked;

	/// <summary>
	/// The model a bottle's grip is measured from: the corked original. Taking the cork off lowers the model's top, which
	/// moves its bounds centre down (about half an inch) — and the grip is taken from that centre — so the uncorked bottle
	/// is placed by the corked one's centre and sits in the hand exactly where the grips were tuned.
	/// </summary>
	public static Model GripReference( Model bottle )
	{
		var path = bottle?.ResourcePath ?? "";
		if ( path.Contains( "potion_07_uncorked", StringComparison.OrdinalIgnoreCase ) ) return _healCorked ??= Model.Load( HealCorkedModel );
		if ( path.Contains( "potion_06_uncorked", StringComparison.OrdinalIgnoreCase ) ) return _manaCorked ??= Model.Load( ManaCorkedModel );
		return bottle;
	}

	[ConVar( "potion_heal_amount" )] public static float HealAmount { get; set; } = 40f;
	[ConVar( "potion_mana_amount" )] public static float ManaAmount { get; set; } = 50f;
	/// <summary>Clip frame (30 fps, 1 = first key) on which the drink takes effect: the hand starts down from the mouth (measured on the contact sheet: ~57).</summary>
	[ConVar( "potion_heal_frame" )] public static int HealFrame { get; set; } = 56;
	/// <summary>Clip frame on which the flick releases the bottle (measured: the arm whips out over 60-63).</summary>
	[ConVar( "potion_throw_frame" )] public static int ThrowFrame { get; set; } = 61;
	/// <summary>Multiplier on the hand's measured speed at the release.</summary>
	[ConVar( "potion_throw_scale" )] public static float ThrowScale { get; set; } = 1f;
	/// <summary>Slowest a thrown bottle leaves (u/s): under it the throw goes out to the right of the view at this speed.</summary>
	[ConVar( "potion_throw_min_speed" )] public static float ThrowMinSpeed { get; set; } = 350f;
	/// <summary>Sound at the moment the potion takes effect (empty for none; the user supplies a gulp later).</summary>
	[ConVar( "potion_drink_sound" )] public static string DrinkSound { get; set; } = "";
	/// <summary>The glugging while the bottle is at the lips (user's clip, 2026-09-20): starts with the drink, cut off when a hit interrupts it.</summary>
	[ConVar( "potion_glug_sound" )] public static string GlugSound { get; set; } = "sounds/gluglugglug.sound";
	private SoundHandle _glug;
	/// <summary>
	/// How long the bottle is at the lips, press to swallow: the length of the glug clip (user, 2026-09-20: "make drinking
	/// of potion duration as long as the glugglugglug sound"; Gluglugglug.mp3 is 1.39 s). The drink clip reaches its
	/// swallow on HealFrame (1.87 s at its own speed), so it plays faster by the ratio. Alchemist's drink speed rides
	/// on top, and pitches the glug up with it so the two still end together.
	/// </summary>
	[ConVar( "potion_drink_seconds" )] public static float DrinkSeconds { get; set; } = 1.39f;
	public static float DrinkSpeedBoon => VoidMage.Boons.PlayerBoons.LocalScale( VoidMage.Boons.BoonStat.DrinkSpeed, 0.3f );
	/// <summary>The drink clip's playback rate (FirstPersonArms.OneShotRate).</summary>
	public static float DrinkRate => MathF.Max( 0.2f, HealFrame / ClipFps / MathF.Max( 0.3f, DrinkSeconds ) ) * DrinkSpeedBoon;
	/// <summary>Sound on a pickup (empty for none).</summary>
	[ConVar( "potion_pickup_sound" )] public static string PickupSound { get; set; } = VoidMage.Audio.GameAudio.ReceiveItemSound;

	/// <summary>Frame of the 26-frame pickup reach (30 fps, 1 = first key) on which the potion vanishes from the table: halfway,
	/// where the hand closes on it (user: it went the instant E was pressed, before the hand got there).</summary>
	[ConVar( "potion_pickup_hide_frame" )] public static int PickupHideFrame { get; set; } = 0;

	private const float ClipFps = 30f;

	/// <summary>The potion the reach is on its way to, and when the hand gets there.</summary>
	private PotionPickup _pendingTake;
	private float _takeAt;

	/// <summary>The frog the reach is on its way to, on the same schedule as a potion (it is already in the bag).</summary>
	private VoidMage.Npc.CritterPickup _pendingFrog;
	private float _catchAt;

	/// <summary>A drink is running on the local player: the input gates of the caster and the arms read this.</summary>
	public static bool IsDrinking { get; private set; }
	public static PotionDrinker Local { get; private set; }

	/// <summary>
	/// What is being drunk, for every peer: 0 nothing, 1 healing, 2 mana. Synced because the third-person hands are
	/// everyone's view of this player — the weapon follower puts the staff away for the whole clip on the strength
	/// of it, and a proxy has no PotionDrinker input of its own.
	/// </summary>
	[Sync] public int DrinkKind { get; set; }
	/// <summary>The bottle is still in the hand (false the moment the flick throws it).</summary>
	[Sync] public bool BottleInHand { get; set; }

	/// <summary>The bottle model for <see cref="DrinkKind"/>, or null when nothing is being drunk.</summary>
	public Model BottleModel => DrinkKind switch
	{
		1 => _healBottle ??= Model.Load( HealModel ),
		2 => _manaBottle ??= Model.Load( ManaModel ),
		_ => null,
	};

	/// <summary>The kind being drunk right now.</summary>
	public PlayerPotions.Kind Kind { get; private set; }
	/// <summary>When a quick slot was pressed with nothing to drink (the hotbar flashes it).</summary>
	public float LastDeniedAt { get; private set; } = float.NegativeInfinity;
	public PlayerPotions.Kind LastDeniedKind { get; private set; }
	/// <summary>When a pickup was refused for a full slot (the prompt flashes).</summary>
	public float LastRefusedAt { get; private set; } = float.NegativeInfinity;

	private PlayerController _controller;
	private FirstPersonArms _arms;
	private PlayerHealth _health;
	private PlayerMana _mana;
	private PlayerPotions _potions;
	private ClipPoseSource _source;
	private Spells.SpellCaster _caster;
	private HandPropFollower _bottleFollower;
	private PlayerFrogs _frogs;

	/// <summary>How long a quick slot press waits for the arms to come free (a heavy swing's cooldown is 0.75 s).</summary>
	[ConVar( "potion_buffer_seconds" )] public static float BufferSeconds { get; set; } = 1f;
	private bool _queued;
	/// <summary>A quick slot press is waiting for the local player's arms: the swing in progress does not chain over it (FirstPersonArms).</summary>
	public static bool DrinkQueued => Local.IsValid() && Local._queued && Time.Now - Local._queuedAt <= BufferSeconds;
	private PlayerPotions.Kind _queuedKind;
	private float _queuedAt;

	private bool _drinking;
	private bool _healed;
	private bool _thrown;

	// ------------------------------------------------------------------ grip tuning

	/// <summary>Clip frame the tuning pose holds on: the flask at the mouth (the hold runs about 14-56).</summary>
	[ConVar( "potion_tune_frame" )] public static int TuneFrame { get; set; } = 34;

	/// <summary>
	/// Tuning the bottle's place in the hand: the drink clip runs to the hold and freezes there with the bottle in
	/// hand, nothing is drunk or thrown and no potion is needed. The PotionTuner panel nudges the grip live; the
	/// values only last for this play session, so its Print button logs them for the prefab.
	/// </summary>
	public bool Tuning { get; private set; }

	/// <summary>`potion_tune` starts or stops grip tuning with the healing bottle, `potion_tune mana` with the mana one.</summary>
	[ConCmd( "potion_tune" )]
	public static void TuneCommand( string bottle = "heal" )
	{
		var d = Local;
		if ( !d.IsValid() ) { Log.Info( "[Potion] no local player" ); return; }
		if ( d.Tuning ) { d.StopTuning(); return; }
		d.StartTuning( bottle.StartsWith( "m", StringComparison.OrdinalIgnoreCase ) ? PlayerPotions.Kind.Mana : PlayerPotions.Kind.Heal );
	}

	public void StartTuning( PlayerPotions.Kind kind )
	{
		if ( IsProxy || Tuning || _drinking || !_arms.IsValid() ) return;
		if ( !_arms.TryStartDrink( BottleFor( kind ) ) )
		{
			Log.Info( "[Potion] tuning needs the arms free: stand still on the ground, not mid-swing" );
			return;
		}
		Tuning = true;
		Kind = kind;
		_drinking = true;
		_healed = true;    // nothing to swallow
		_thrown = true;    // nothing to throw
		_startedAt = Time.Now;
		IsDrinking = true;
		DrinkKind = kind == PlayerPotions.Kind.Heal ? 1 : 2;
		SetBottleInHand( true );
		SetControls( false );
		VoidMage.UI.MouseLock.Release = true;
		Log.Info( $"[Potion] tuning the {(kind == PlayerPotions.Kind.Heal ? "healing" : "mana")} bottle: the pose freezes on frame {TuneFrame}. V front view (third person), Q switches the mouse between the panel and looking around" );
	}

	public void StopTuning()
	{
		if ( !Tuning ) return;
		PrintGrip();
		Tuning = false;
		if ( _source.IsValid() ) _source.Frozen = false;
		VoidMage.UI.MouseLock.Release = VoidMage.UI.SpellLoadout.IsOpen;
		_arms?.CancelDrink();
		Finish();
	}

	/// <summary>Tuning: switch the bottle in the hand between healing and mana.</summary>
	public void TuneBottle( PlayerPotions.Kind kind )
	{
		if ( !Tuning || !_arms.IsValid() ) return;
		Kind = kind;
		DrinkKind = kind == PlayerPotions.Kind.Heal ? 1 : 2;
		_arms.SwapBottle( BottleFor( kind ) );
	}

	/// <summary>Logs the tuned grips as they go into the player prefab's First Person Arms.</summary>
	public void PrintGrip()
	{
		if ( !_arms.IsValid() ) return;
		static string V( Vector3 v ) => $"{v.x:0.##},{v.y:0.##},{v.z:0.##}";
		static string A( Angles a ) => $"{a.pitch:0.#},{a.yaw:0.#},{a.roll:0.#}";
		Log.Info( $"[Potion] grips for the player prefab: \"PotionGripOffset\": \"{V( _arms.PotionGripOffset )}\", \"PotionRotationOffset\": \"{A( _arms.PotionRotationOffset )}\", \"ManaGripOffset\": \"{V( _arms.ManaGripOffset )}\", \"ManaRotationOffset\": \"{A( _arms.ManaRotationOffset )}\"" );
	}

	private Model BottleFor( PlayerPotions.Kind kind )
		=> kind == PlayerPotions.Kind.Heal ? (_healBottle ??= Model.Load( HealModel )) : (_manaBottle ??= Model.Load( ManaModel ));
	private float _startedAt;
	private Model _healBottle, _manaBottle;
	/// <summary>Last frame's bottle transform and the speed it is travelling at (u/s), from whichever view holds it.</summary>
	private Transform? _bottleAt;
	private Vector3? _bottlePrev;
	private Vector3 _bottleVelocity;

	protected override void OnStart()
	{
		if ( !IsProxy ) Local = this;
	}

	/// <summary>
	/// Everything the potions need on a spawned player, whatever the prefab carries: the counts, this component and
	/// the two HUD panels on the hud's ScreenPanel (the way Wardrobe.EnsureOn works). Called by every player spawner.
	/// </summary>
	public static void EnsureOn( GameObject player )
	{
		if ( !player.IsValid() ) return;
		player.Components.GetOrCreate<PlayerPotions>();
		player.Components.GetOrCreate<PlayerFrogs>();
		player.Components.GetOrCreate<PotionDrinker>();
		player.Components.GetOrCreate<PotionPour>();
		// the third-person bottle: its own follower beside the sword's and the staff's (CharacterBoneRig drives them all)
		if ( !player.Components.GetAll<HandPropFollower>( FindMode.EverythingInSelfAndDescendants ).Any( f => f.Role == HandPropRole.Potion ) )
		{
			var follower = player.Components.Create<HandPropFollower>();
			follower.Role = HandPropRole.Potion;
			follower.Hand = HandSide.Right;
		}
		var screen = player.Components.Get<ScreenPanel>( FindMode.EverythingInSelfAndDescendants );
		if ( !screen.IsValid() ) return;
		if ( screen.GameObject.Components.Get<UI.PotionHotbar>( FindMode.EverythingInSelf ) is null ) screen.GameObject.Components.Create<UI.PotionHotbar>();
		if ( screen.GameObject.Components.Get<UI.PickupPrompt>( FindMode.EverythingInSelf ) is null ) screen.GameObject.Components.Create<UI.PickupPrompt>();
		if ( screen.GameObject.Components.Get<UI.UnlockNotice>( FindMode.EverythingInSelf ) is null ) screen.GameObject.Components.Create<UI.UnlockNotice>();
		if ( screen.GameObject.Components.Get<UI.ScrollReveal>( FindMode.EverythingInSelf ) is null ) screen.GameObject.Components.Create<UI.ScrollReveal>();
		if ( screen.GameObject.Components.Get<UI.StaffHud>( FindMode.EverythingInSelf ) is null ) screen.GameObject.Components.Create<UI.StaffHud>();
		if ( screen.GameObject.Components.Get<UI.ShopkeeperChat>( FindMode.EverythingInSelf ) is null ) screen.GameObject.Components.Create<UI.ShopkeeperChat>();
		if ( screen.GameObject.Components.Get<UI.PotionTuner>( FindMode.EverythingInSelf ) is null ) screen.GameObject.Components.Create<UI.PotionTuner>();
		if ( screen.GameObject.Components.Get<UI.WeaponTuner>( FindMode.EverythingInSelf ) is null ) screen.GameObject.Components.Create<UI.WeaponTuner>();
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
		if ( Local is null ) IsDrinking = false;
	}

	protected override void OnDisabled()
	{
		if ( Tuning ) { Tuning = false; if ( _source.IsValid() ) _source.Frozen = false; }
		if ( _drinking ) Finish();
		// already in the bag: never leave its bottle standing on the table, nor its frog hopping about
		if ( _pendingTake is not null ) FinishTake();
		if ( _pendingFrog.IsValid() ) FinishCatch();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( !_controller.IsValid() ) _controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
		if ( !_arms.IsValid() ) _arms = Components.Get<FirstPersonArms>( FindMode.EverythingInSelfAndDescendants );
		if ( !_health.IsValid() ) _health = Components.Get<PlayerHealth>( FindMode.EverythingInSelfAndDescendants );
		if ( !_mana.IsValid() ) _mana = Components.Get<PlayerMana>( FindMode.EverythingInSelfAndDescendants );
		if ( !_potions.IsValid() ) _potions = Components.Get<PlayerPotions>( FindMode.EverythingInSelfAndDescendants );
		if ( !_source.IsValid() ) _source = ClipPoseSource.Find( this );
		if ( !_caster.IsValid() ) _caster = Components.Get<Spells.SpellCaster>( FindMode.EverythingInSelfAndDescendants );
		if ( !_bottleFollower.IsValid() )
			_bottleFollower = Components.GetAll<HandPropFollower>( FindMode.EverythingInSelfAndAncestors ).FirstOrDefault( f => f.Role == HandPropRole.Potion );

		TrackBottle();
		// before every early return below: a potion already reached for goes on schedule whatever opens or happens next
		if ( _pendingTake is not null && Time.Now >= _takeAt ) FinishTake();
		if ( _pendingFrog.IsValid() && Time.Now >= _catchAt ) FinishCatch();

		var dead = _health.IsValid() && _health.IsDead;
		var blocked = VoidMage.UI.SpellLoadout.IsOpen || VoidMage.UI.ShopkeeperChat.IsOpen || VoidMage.Dev.DevPanel.Blocking || RunUi.Blocking || dead;

		if ( Tuning )
		{
			if ( dead || RunUi.Blocking || !_arms.IsValid() || !_arms.IsDrinking ) { StopTuning(); return; }
			if ( _source.IsValid() && !_source.Frozen && Frame >= TuneFrame ) _source.Frozen = true;
			// Q hands the mouse back to looking around (and back to the panel): the caster ignores Q while a drink runs
			if ( Input.Pressed( "Menu" ) ) VoidMage.UI.MouseLock.Release = !VoidMage.UI.MouseLock.Release;
			return;
		}

		if ( _drinking )
		{
			if ( blocked ) { Cancel(); return; }
			TickDrink();
			return;
		}

		if ( blocked ) { _queued = false; PotionPickup.ClearFocus(); VoidMage.Npc.CritterPickup.ClearFocus(); VoidMage.Village.VillageShop.ClearFocus(); VoidMage.Village.Shopkeeper.ClearFocus(); return; }

		TickPickup();
		if ( Input.Pressed( "Slot1" ) ) QueueDrink( PlayerPotions.Kind.Heal );
		else if ( Input.Pressed( "Slot2" ) ) QueueDrink( PlayerPotions.Kind.Mana );
		// a press the arms could not take yet (mid-swing, mid-air, a cast's follow-through) is tried again every frame
		if ( _queued && Time.Now - _queuedAt > BufferSeconds ) _queued = false;
		if ( _queued && TryDrink( _queuedKind ) ) _queued = false;
	}

	/// <summary>
	/// A quick slot press is remembered rather than dropped (user, 2026-09-21: "when i attack and click 1 it doesnt
	/// drink"): the drink starts on the first frame the arms are free, as long as that is within <see cref="BufferSeconds"/>.
	/// An empty slot still flashes at the press.
	/// </summary>
	private void QueueDrink( PlayerPotions.Kind kind )
	{
		if ( !_potions.IsValid() ) return;
		if ( _potions.Count( kind ) <= 0 ) { _queued = false; LastDeniedAt = Time.Now; LastDeniedKind = kind; return; }
		_queued = true;
		_queuedKind = kind;
		_queuedAt = Time.Now;
	}

	/// <summary>
	/// Where the bottle is and how fast it is going, from whichever view is holding it: the first-person rig places
	/// it in PostCameraSetup, the third-person follower in OnPreRender, and both are a frame old by the time this
	/// runs — which is what the throw wants anyway, since it reads them on the frame the flick fires.
	/// </summary>
	private void TrackBottle()
	{
		var at = _arms.IsValid() ? _arms.PotionWorld : null;
		at ??= _bottleFollower.IsValid() ? _bottleFollower.PropWorld : null;
		_bottleAt = at;
		if ( at is not { } t )
		{
			_bottlePrev = null;
			_bottleVelocity = Vector3.Zero;
			return;
		}
		var dt = MathF.Max( Time.Delta, 0.001f );
		var instant = _bottlePrev.HasValue ? (t.Position - _bottlePrev.Value) / dt : Vector3.Zero;
		// a short average, so one hitched frame cannot fling the bottle across the room
		_bottleVelocity = _bottlePrev.HasValue ? Vector3.Lerp( _bottleVelocity, instant, 0.5f ) : Vector3.Zero;
		_bottlePrev = t.Position;
	}

	// ------------------------------------------------------------------ pickup (E)

	private void TickPickup()
	{
		if ( !_controller.IsValid() || !_potions.IsValid() ) { PotionPickup.ClearFocus(); VoidMage.Run.ScrollPickup.ClearFocus(); VoidMage.Run.SkinPickup.ClearFocus(); VoidMage.Npc.CritterPickup.ClearFocus(); VoidMage.Dungeon.ManaShrine.ClearFocus(); VoidMage.Village.VillageShop.ClearFocus(); VoidMage.Village.Shopkeeper.ClearFocus(); return; }
		var eye = _controller.EyePosition;
		var rot = _controller.EyeTransform.Rotation;
		// the shop's table first (village only, where nothing else is lying about): E buys what the crosshair is on
		var forSale = VoidMage.Village.VillageShop.UpdateFocus( Scene, eye, rot.Forward, _controller.GameObject );
		if ( forSale.IsValid() )
		{
			VoidMage.Run.SkinPickup.ClearFocus();
			VoidMage.Run.ScrollPickup.ClearFocus();
			VoidMage.Npc.CritterPickup.ClearFocus();
			VoidMage.Dungeon.ManaShrine.ClearFocus();
			PotionPickup.ClearFocus();
			VoidMage.Village.Shopkeeper.ClearFocus();
			if ( !Input.Pressed( "Use" ) ) return;
			if ( !VoidMage.Village.VillageShop.Instance.TryBuy( forSale ) ) return;
			if ( _arms.IsValid() && !(_caster.IsValid() && _caster.Busy) ) _arms.TryStartPickup();
			return;
		}
		// the shopkeeper behind it: whatever on the table is under the crosshair wins, he is what is left to look at
		var keeper = VoidMage.Village.Shopkeeper.UpdateFocus( Scene, eye, rot.Forward, _controller.GameObject );
		if ( keeper.IsValid() )
		{
			VoidMage.Run.SkinPickup.ClearFocus();
			VoidMage.Run.ScrollPickup.ClearFocus();
			VoidMage.Npc.CritterPickup.ClearFocus();
			VoidMage.Dungeon.ManaShrine.ClearFocus();
			PotionPickup.ClearFocus();
			if ( Input.Pressed( "Use" ) ) keeper.Talk();
			return;
		}
		// rarest first, so one E can never take two things: a weapon (1% out of a crate), then a scroll, then a potion
		var weapon = VoidMage.Run.SkinPickup.UpdateFocus( Scene, eye, rot.Forward, _controller.GameObject );
		if ( weapon.IsValid() )
		{
			PotionPickup.ClearFocus();
			VoidMage.Run.ScrollPickup.ClearFocus();
			if ( !Input.Pressed( "Use" ) ) return;
			if ( _arms.IsValid() && !(_caster.IsValid() && _caster.Busy) ) _arms.TryStartPickup();
			weapon.Take();
			return;
		}
		// a scroll in view wins over a potion: it is the rarer find, and one E should never take both
		var scroll = VoidMage.Run.ScrollPickup.UpdateFocus( Scene, eye, rot.Forward, _controller.GameObject );
		if ( scroll.IsValid() )
		{
			PotionPickup.ClearFocus();
			if ( !Input.Pressed( "Use" ) ) return;
			// the staff hand reaches for it like a potion (only for the look: the scroll is taken at once)
			if ( _arms.IsValid() && !(_caster.IsValid() && _caster.Busy) ) _arms.TryStartPickup();
			scroll.Take();
			return;
		}
		// a live frog in view is caught before a potion behind it: it is the thing that moves, and it is what the
		// player is aiming at when they walk up to one
		var frog = VoidMage.Npc.CritterPickup.UpdateFocus( Scene, eye, rot.Forward, _controller.GameObject );
		if ( frog.IsValid() )
		{
			PotionPickup.ClearFocus();
			VoidMage.Dungeon.ManaShrine.ClearFocus();
			if ( !Input.Pressed( "Use" ) ) return;
			_frogs ??= Components.Get<PlayerFrogs>( FindMode.EverythingInSelfAndAncestors );
			// counted into the bag at the press like a potion; only the BODY waits for the hand
			_frogs?.Add();
			// the frog leaves the world AT THE PRESS, like a potion (user, 2026-09-20: waiting for the hand to close,
			// 8 frames into the reach, "thats confusing, just pick them up right away"); the reach still plays.
			// `potion_pickup_hide_frame 9` brings the wait back
			bool frogReach = _arms.IsValid() && !(_caster.IsValid() && _caster.Busy) && _arms.TryStartPickup();
			if ( _pendingFrog.IsValid() ) FinishCatch();
			frog.Reserve();   // out of focus and out of the pick list at once, so E cannot catch it twice
			_pendingFrog = frog;
			_catchAt = frogReach ? Time.Now + MathF.Max( 0, PickupHideFrame - 1 ) / ClipFps : Time.Now;
			if ( !frogReach || _catchAt <= Time.Now ) FinishCatch();
			return;
		}
		// the shrine: E fills the staff, free and as often as you like. Last of the E things, so a frog sitting on
		// the altar or a potion in front of it is still what the press takes.
		var shrine = VoidMage.Dungeon.ManaShrine.UpdateFocus( Scene, eye, rot.Forward, _controller.GameObject );
		if ( shrine.IsValid() )
		{
			PotionPickup.ClearFocus();
			if ( !Input.Pressed( "Use" ) ) return;
			if ( _arms.IsValid() && !(_caster.IsValid() && _caster.Busy) ) _arms.TryStartPickup();
			shrine.Use();
			return;
		}
		var focused = PotionPickup.UpdateFocus( Scene, eye, rot.Forward, _controller.GameObject );
		if ( !Input.Pressed( "Use" ) ) return;
		if ( !focused.IsValid() )
		{
			// E with nothing in focus: with the pour debug on, say what the nearest potion looked like
			if ( PotionPour.DebugDraw ) Log.Info( $"[Potion] E: nothing in focus. {PotionPickup.Describe( Scene, eye, rot.Forward, _controller.GameObject )}" );
			return;
		}

		if ( !_potions.TryAdd( focused.Kind ) )
		{
			LastRefusedAt = Time.Now;
			return;
		}
		// the staff hand reaches for it, so E is not an empty press (never over a cast holding that arm); the potion stays
		// on the table until the hand closes on it halfway through the reach, and goes at once when there is no reach
		if ( _pendingTake is not null ) FinishTake();
		_pendingTake = focused;
		focused.Reserve();
		bool reach = _arms.IsValid() && !(_caster.IsValid() && _caster.Busy) && _arms.TryStartPickup();
		_takeAt = reach ? Time.Now + MathF.Max( 0, PickupHideFrame - 1 ) / ClipFps : Time.Now;
		if ( !reach || _takeAt <= Time.Now ) FinishTake();
	}

	private void FinishTake()
	{
		var p = _pendingTake;
		_pendingTake = null;
		if ( !p.IsValid() ) return;
		if ( !string.IsNullOrEmpty( PickupSound ) ) VoidMage.Audio.GameAudio.Play( PickupSound, p.Centre );
		p.Taken();
	}

	/// <summary>The hand closed on the frog: off the floor it goes. It was counted into the bag at the press.</summary>
	private void FinishCatch()
	{
		var f = _pendingFrog;
		_pendingFrog = null;
		if ( !f.IsValid() ) return;
		if ( !string.IsNullOrEmpty( PickupSound ) ) VoidMage.Audio.GameAudio.Play( PickupSound, f.Centre );
		f.Catch();
	}

	// ------------------------------------------------------------------ drink (1 / 2)

	private bool TryDrink( PlayerPotions.Kind kind )
	{
		if ( !_potions.IsValid() || !_arms.IsValid() ) return false;
		if ( _potions.Count( kind ) <= 0 ) { LastDeniedAt = Time.Now; LastDeniedKind = kind; return true; }
		// the cast layer owns the staff arm until its follow-through is done; a swing or a jump is the arms' own gate
		if ( _caster.IsValid() && _caster.Busy ) return false;
		if ( !_arms.TryStartDrink( BottleFor( kind ) ) ) return false;

		Kind = kind;
		_drinking = true;
		_healed = false;
		_thrown = false;
		_startedAt = Time.Now;
		IsDrinking = true;
		_glug?.Stop( 0f );
		_glug = string.IsNullOrEmpty( GlugSound ) || !_controller.IsValid() ? null : VoidMage.Audio.GameAudio.PlayLoop( GlugSound, _controller.EyePosition );
		if ( _glug is not null ) _glug.Pitch = DrinkSpeedBoon;
		DrinkKind = kind == PlayerPotions.Kind.Heal ? 1 : 2;
		SetBottleInHand( true );
		// no longer rooted (user): the player keeps walking, capped at crouch speed while IsDrinking
		// (CharacterLocomotionAnimator.DriveSpeeds)
		return true;
	}

	/// <summary>The clip's frame right now (1 = the first key), off the synced clip clock like SwordSlash.</summary>
	private int Frame
	{
		get
		{
			var t = _source.IsValid() ? _source.Elapsed : Time.Now - _startedAt;
			return (int)MathF.Floor( t * ClipFps ) + 1;
		}
	}

	private void TickDrink()
	{
		if ( !_arms.IsValid() || !_arms.IsDrinking )
		{
			// the clip ran out (or the arms dropped it): whatever was still in the hand is gone with the clip
			Finish();
			return;
		}
		var frame = Frame;
		if ( !_healed && frame >= HealFrame ) Swallow();
		if ( !_thrown && frame >= ThrowFrame ) Throw( drop: false );
	}

	/// <summary>The moment the potion takes effect: consumed now, so a hit before this keeps it.</summary>
	private void Swallow()
	{
		_healed = true;
		if ( !_potions.IsValid() || !_potions.TryTake( Kind ) ) return;
		// Alchemist doubles it, Bloodthirst's cost halves it
		var strength = VoidMage.Boons.PlayerBoons.LocalScale( VoidMage.Boons.BoonStat.PotionStrength, 0.1f );
		if ( Kind == PlayerPotions.Kind.Heal ) _health?.Heal( HealAmount * strength );
		else _mana?.Restore( ManaAmount * strength );
		if ( !string.IsNullOrEmpty( DrinkSound ) && _controller.IsValid() ) VoidMage.Audio.GameAudio.Play( DrinkSound, _controller.EyePosition );
	}

	/// <summary>The bottle leaves the hand: the flick throws it, a cancel after the swallow lets it drop.</summary>
	private void Throw( bool drop )
	{
		_thrown = true;
		var world = _bottleAt;
		SetBottleInHand( false );
		if ( world is not { } at || !_controller.IsValid() )
		{
			// no bottle transform this frame (the hands are not drawn at all): nothing to throw, and saying so beats
			// a potion that quietly evaporates
			Log.Info( "[Potion] the flick had no bottle in hand to throw (no first-person rig and no hand follower)" );
			return;
		}

		var velocity = _bottleVelocity * MathF.Max( 0f, ThrowScale );
		if ( !drop && velocity.Length < ThrowMinSpeed )
		{
			// the hand barely moved this frame (a frame hitch, a pause): the flick still goes out to the right and a little up
			var rot = _controller.EyeTransform.Rotation;
			var dir = (rot.Forward * 0.45f - rot.Left * 1f + Vector3.Up * 0.25f).Normal;
			velocity = dir * ThrowMinSpeed;
		}
		// a dropped bottle keeps only a little of the hand's motion
		if ( drop ) velocity = velocity.ClampLength( 120f );

		var model = Kind == PlayerPotions.Kind.Heal ? _healBottle : _manaBottle;
		var parent = Scene.GetAllComponents<Dungeon.DungeonBuilder>().FirstOrDefault()?.GameObject;
		ThrownBottle.Throw( Scene, model, at, velocity, Kind, parent, _controller.GameObject );
	}

	/// <summary>A hit landed on the player: the drink stops (PlayerHealth.TakeHit).</summary>
	public void OnHit()
	{
		if ( Tuning ) StopTuning();
		else if ( _drinking ) Cancel();
	}

	private void Cancel()
	{
		if ( !_drinking ) return;
		if ( _healed && !_thrown ) Throw( drop: true );
		_glug?.Stop( 0.1f );
		_glug = null;
		_arms?.CancelDrink();
		Finish();
	}

	private void Finish()
	{
		_drinking = false;
		IsDrinking = false;
		DrinkKind = 0;
		SetBottleInHand( false );
		SetControls( true );
	}

	/// <summary>The bottle shows in the hand: the first-person prop and (through the synced flag) everyone's third-person one.</summary>
	private void SetBottleInHand( bool show )
	{
		BottleInHand = show;
		if ( _arms.IsValid() ) _arms.BottleVisible = show;
	}

	/// <summary>Rooted for the drink: the same cut a run card makes (RunManager.SetControls), the look untouched. Never re-enabled under a card or on a corpse.</summary>
	private void SetControls( bool on )
	{
		if ( !_controller.IsValid() ) return;
		if ( on )
		{
			var dead = _health.IsValid() && _health.IsDead;
			if ( !RunUi.Blocking && !dead ) _controller.UseInputControls = true;
			return;
		}
		_controller.UseInputControls = false;
		_controller.WishVelocity = Vector3.Zero;
		// stop dead on the spot; gravity keeps its say
		if ( _controller.Body.IsValid() ) _controller.Body.Velocity = _controller.Body.Velocity.WithX( 0f ).WithY( 0f );
	}
}
