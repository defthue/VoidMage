using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;

namespace VoidMage.Spells;

/// <summary>
/// The staff, owner side: right mouse casts the <see cref="StaffLoadout"/>'s one spell (1 and 2 are the potion quick
/// slots, PotionDrinker). Every spell runs the same arms gesture (heden's cast layer through ClipPoseSource:
/// `channel_hold` up raises and holds, down throws, an abort pulse bails) and the same seven-stage <see cref="CastPipeline"/>.
/// There is no cast time any more (user, 2026-09-20: "remove cast time to make it more fun"): the button throws the
/// spell as soon as the arm is up, and held it throws again every follow-through, so mana is what paces the staff.
/// Charging lives on as Gluttony's effect: with that scroll a HOLD swells the spell and the release throws it.
/// `spell_instant 0` brings the old hold-to-charge rhythm back for comparison. What differs per delivery is what
/// the throw makes: a projectile or orb leaves the staff, an instant spell resolves at once, a beam burns a fixed window.
///
/// Presentation on every peer, from the synced charge state: the orb grows at the gem with the charge hum on it,
/// a release pops a launch flash and plays the cast, a cancel puffs, a refused cast clicks.
/// </summary>
[Title( "Spell Caster" ), Category( "Void Mage" ), Icon( "local_fire_department" )]
public sealed partial class SpellCaster : Component
{
	[Property] public string CastAction { get; set; } = "attack2";
	/// <summary>No cast time: the arm comes up and the spell leaves. 0 = the old rhythm (hold to charge, let go to fire).</summary>
	[ConVar( "spell_instant" )] public static bool Instant { get; set; } = true;
	/// <summary>
	/// How long the arm gets to come up before an instant cast leaves the staff: the length of the raise clip at the
	/// graph's speed (Attack6begin, 0.683 s at x4; tools/gen_fp_arms_animgraph.py BEGIN_SPEED). The graph always plays
	/// the raise out before the throw, so the bolt leaving at this moment IS the throw (user, 2026-09-20: at 0.08 the
	/// staff "immediately goes into the animation after firing").
	/// </summary>
	[ConVar( "spell_raise_seconds" )] public static float RaiseSeconds { get; set; } = 0.17f;
	/// <summary>With Gluttony: a press shorter than this is a tap (a plain cast), longer is a hold (the swell counts from here).</summary>
	[ConVar( "spell_tap_seconds" )] public static float TapSeconds { get; set; } = 0.2f;
	[Property] public float AimDistance { get; set; } = 4000f;
	/// <summary>How much of the cast shows over the sword clip: 1 is heden's full gesture, lower keeps more of the shield arm and softens it.</summary>
	[Property, Range( 0f, 1f )] public float CastWeight { get; set; } = 1f;

	[Sync] public bool IsCharging { get; set; }
	[Sync] public float ChargeStart { get; set; }
	/// <summary>The charge this cast needs (the spell's, stretched by Overcharge), so proxies grow the orb on the right clock.</summary>
	[Sync] public float ChargeSeconds { get; set; } = 0.3f;
	/// <summary>The spell being charged or fired, for every peer's look and sounds.</summary>
	[Sync] public string CastingSpellId { get; set; } = "";
	/// <summary>Bumped on every release that fired, so proxies can tell a throw from a cancel.</summary>
	[Sync] public int CastCount { get; set; }
	/// <summary>Bumped when a cast was refused (cannot pay): the HUD pulses, a click plays.</summary>
	[Sync] public int DenyCount { get; set; }
	/// <summary>A beam is burning (the staff stays raised).</summary>
	[Sync] public bool IsFiring { get; set; }

	/// <summary>The local player's caster.</summary>
	public static SpellCaster Local { get; private set; }

	/// <summary>Kindling: a sword hit has primed the staff, the next paid cast is discounted (the Cost stage spends it).</summary>
	public bool Primed { get; set; }
	/// <summary>Overcharge: how far the staff has built toward its next overcharged cast, 0..1 (1 = the next cast is the one).</summary>
	public float OverchargeBuild01 { get; private set; }
	/// <summary>The Edge: the blade carries the last spell until this time.</summary>
	[Sync] public float EdgeUntil { get; set; }
	[Sync] public string EdgeSpellId { get; set; } = "";
	public bool EdgeActive => Time.Now < EdgeUntil && !string.IsNullOrEmpty( EdgeSpellId );
	public float LastDeniedAt { get; private set; } = float.NegativeInfinity;

	public float Charge01 => IsCharging ? ((Time.Now - ChargeStart) / MathF.Max( ChargeSeconds, 0.01f )).Clamp( 0f, 1f ) : 0f;
	/// <summary>A real hold is on (Gluttony, or the old rhythm): the charge look and hum show. An instant cast's raise never does.</summary>
	public bool ShowsCharge => IsCharging && ChargeSeconds > RaiseSeconds + 0.1f && Time.Now - ChargeStart > RaiseSeconds;
	public bool Ready => IsCharging && Charge01 >= 0.999f;
	public bool InFirstPerson => _controller.IsValid() && !_controller.ThirdPerson;
	/// <summary>Charging, burning a beam or still in the throw's follow-through: the cast layer owns the staff arm (a potion has to wait).</summary>
	public bool Busy => _state != State.Idle;
	public SpellDefinition CastingSpell => SpellDatabase.Get( CastingSpellId ) ?? Loadout?.ActiveSpell ?? SpellDatabase.Get( SpellIds.Fireball );
	public SpellDefinition ActiveSpell => Loadout?.ActiveSpell;
	public StaffLoadout Loadout => _loadout.IsValid() ? _loadout : null;
	/// <summary>Kindling would discount the next cast right now (a sword hit primed the staff).</summary>
	public bool KindlingActive => Primed && Loadout is { } l && l.HasModifier( ModifierId.Kindling );

	/// <summary>
	/// The player's own object, what traces ignore. Never GameObject.Root: the dungeon builder parents the player
	/// under the dungeon, so Root is the dungeon and ignoring it ignores every wall.
	/// </summary>
	public GameObject PlayerRoot => _controller.IsValid() ? _controller.GameObject : GameObject;

	private enum State { Idle, Charging, Firing, Casting }

	private PlayerController _controller;
	private ClipPoseSource _source;
	private FirstPersonArms _arms;
	private HandPropFollower _staffFollower;
	private StaffLoadout _loadout;
	private PlayerMana _mana;
	private PlayerHealth _health;
	private PlayerShards _shards;

	private State _state = State.Idle;
	private float _castEnd;
	private float _nextCast;
	/// <summary>When the burning beam's window closes.</summary>
	private float _fireEnd;
	private SpellDefinition _current;
	private readonly List<SpellBeam> _beams = new();
	/// <summary>Free casts waiting for their beat: echoes (from the Emission stage), a wisp's or the hail's miniature.</summary>
	private readonly List<(CastContext ctx, float fireAt, bool echo)> _scheduled = new();
	private bool _swelling;
	private int _overchargeCount;
	private float _nextRetort;
	private float _edgeDamage;
	private SpellHitInfo _edgeInfo;

	protected override void OnStart()
	{
		if ( !IsProxy ) Local = this;
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
	}

	protected override void OnDisabled()
	{
		DisablePresentation();
	}

	protected override void OnUpdate()
	{
		if ( !_controller.IsValid() )
			_controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
		if ( !_source.IsValid() )
			_source = ClipPoseSource.Find( this );
		if ( !_arms.IsValid() )
			_arms = Components.Get<FirstPersonArms>( FindMode.EverythingInSelfAndAncestors );
		if ( !_loadout.IsValid() )
			_loadout = Components.Get<StaffLoadout>( FindMode.EverythingInSelfAndAncestors );
		if ( !_mana.IsValid() )
			_mana = Components.Get<PlayerMana>( FindMode.EverythingInSelfAndAncestors );
		if ( !_health.IsValid() )
			_health = Components.Get<PlayerHealth>( FindMode.EverythingInSelfAndAncestors );
		if ( !_shards.IsValid() )
			_shards = Components.Get<PlayerShards>( FindMode.EverythingInSelfAndAncestors ) ?? PlayerRoot?.Components.Get<PlayerShards>( FindMode.EverythingInSelfAndDescendants );
		if ( !_staffFollower.IsValid() )
		{
			foreach ( var f in Components.GetAll<HandPropFollower>( FindMode.EverythingInSelfAndAncestors ) )
				if ( f.Hand == HandSide.Right ) { _staffFollower = f; break; }
		}

		if ( !IsProxy )
		{
			UpdateInput();
			TickScheduled();
			LatticeField.Tick( this );
		}
		FamiliarWisps.Sync( this );
		UpdatePresentation();
	}

	// ------------------------------------------------------------------ owner: input

	private void UpdateInput()
	{
		if ( !_source.IsValid() ) return;

		if ( VoidMage.UI.SpellLoadout.IsOpen || VoidMage.UI.WeaponTuner.IsOpen || VoidMage.UI.ShopkeeperChat.IsOpen || VoidMage.Dev.DevPanel.Blocking || VoidMage.Run.RunUi.Blocking || PotionDrinker.IsDrinking || (_health.IsValid() && _health.IsDead) )
		{
			// the dev panel or a run card has the mouse, a potion is being drunk, or the player is dead: nothing here
			// reads input, and a held cast lets go
			if ( _state == State.Charging ) Cancel();
			else if ( _state == State.Firing ) EndBeam();
			return;
		}

		switch ( _state )
		{
			case State.Idle:
				// held, the staff throws again every follow-through; only a fresh press complains about an empty pool
				if ( Input.Down( CastAction ) && Time.Now >= _nextCast ) TryStartCharge( Input.Pressed( CastAction ) );
				break;

			case State.Charging:
			{
				var held = Time.Now - ChargeStart;
				if ( _current is null ) { Cancel(); break; }
				if ( _swelling )
				{
					// Gluttony: a tap is a plain cast, a hold swells until the button lets go (or the hold cap throws it)
					var raised = held >= RaiseSeconds;
					if ( raised && (!Input.Down( CastAction ) || held > ChargeSeconds + _current.MaxHoldSeconds) )
						Fire( held <= TapSeconds ? 0f : ((held - TapSeconds) / MathF.Max( 0.05f, ChargeSeconds - TapSeconds )).Clamp( 0f, 1f ) );
					break;
				}
				if ( Instant || _current.AutoRelease )
				{
					if ( Charge01 >= 0.999f ) Fire( 0f );
					break;
				}
				// the old rhythm (spell_instant 0): the hold cap counts from full charge
				if ( held > ChargeSeconds + _current.MaxHoldSeconds ) { Cancel(); break; }
				if ( !Input.Down( CastAction ) )
				{
					if ( Charge01 >= 0.999f ) Fire( 0f );
					else Cancel();
				}
				break;
			}

			case State.Firing:
				// a burst, not a hose: the beam was paid for in full at the release and burns its own window out
				if ( _current is null || Time.Now >= _fireEnd ) EndBeam();
				break;

			case State.Casting:
				if ( Time.Now >= _castEnd )
				{
					_source.ActionTarget = 0f;
					_state = State.Idle;
				}
				break;
		}
	}

	/// <summary>The button is down: is there a spell, can it be paid? Then the arm comes up. `complain`: a fresh press (a held repeat that cannot pay just waits).</summary>
	private void TryStartCharge( bool complain )
	{
		var stacks = Loadout?.Stacks ?? ModifierStacks.Empty;
		// The Wild Page: the staff decides
		var spell = stacks.Has( ModifierId.WildPage ) ? RandomKnownSpell( null ) ?? ActiveSpell : ActiveSpell;
		if ( spell is null ) return;
		var preview = ResolvedCast.Preview( spell, stacks, _shards?.Carried ?? 0, KindlingActive, _health.IsValid() ? 1f - _health.Health01 : 0f );
		var affordable = preview.HealthCost > 0f
			? _health.IsValid() && _health.Health - preview.HealthCost >= 1f
			: _mana.IsValid() && _mana.CanAfford( preview.ManaCost );
		if ( !affordable )
		{
			if ( complain ) Deny();
			else _nextCast = Time.Now + 0.1f;
			return;
		}

		_current = spell;
		_swelling = stacks.Has( ModifierId.Gluttony );
		ChargeSeconds = _swelling ? ModifierTables.GluttonyHoldSeconds
			: Instant ? MathF.Max( 0.02f, RaiseSeconds )
			: spell.ChargeSeconds * SpellBoons.TimeScale;
		CastingSpellId = spell.Id;
		IsCharging = true;
		ChargeStart = Time.Now;
		_state = State.Charging;
		_source.ChannelHold = true;          // graph: Passthrough -> ChannelBegin -> ChannelLoop
		_source.ActionTarget = CastWeight;
	}

	private void Deny()
	{
		DenyCount++;
		LastDeniedAt = Time.Now;
		_nextCast = Time.Now + 0.25f;
	}

	private void Cancel()
	{
		IsCharging = false;
		_source.ChannelHold = false;
		_source.AbortCount++;                // graph: straight back to Passthrough, no throw
		_source.ActionTarget = 0f;
		_state = State.Idle;
		_nextCast = Time.Now + 0.2f;
	}

	/// <summary>`chargePower`: how far Gluttony's hold got, 0 for a tap and for every staff without it.</summary>
	private void Fire( float chargePower )
	{
		var spell = _current;
		IsCharging = false;
		var aim = SolveAim( spell );
		var ctx = BuildContext( spell, SpellHitFlags.None, aim.Origin, aim.Direction, aim.Target );
		ctx.ChargePower = chargePower;

		// Overcharge: the staff counts its casts; the Nth one is the monster
		var every = ModifierTables.OverchargeEvery[ctx.Stack( ModifierId.Overcharge )];
		if ( every > 0 && _overchargeCount + 1 >= every ) ctx.Overcharged = true;

		// a new cast from the button lets the last cast's orbiting bolts go (its echoes and copies share its serial)
		CastSerial++;
		if ( !CastPipeline.Run( ctx ) )
		{
			CastSerial--;
			// could not pay after all (the pool drained while charging): a refusal, not a throw
			Deny();
			Cancel();
			return;
		}

		CastCount++;
		if ( every > 0 ) _overchargeCount = ctx.Overcharged ? 0 : _overchargeCount + 1;
		else _overchargeCount = 0;
		OverchargeBuild01 = every > 1 ? _overchargeCount / (float)(every - 1) : every == 1 ? 1f : 0f;

		// The Edge: the blade carries this spell for a few seconds
		var edge = ctx.Stack( ModifierId.Edge );
		if ( edge > 0 )
		{
			EdgeUntil = Time.Now + ModifierTables.EdgeSeconds;
			EdgeSpellId = spell.Id;
			// the staff's resting numbers: a hold, an overcharge or the last drop is that one cast's, not three seconds of sword hits (it carried 430 a swing at the peak, audit 2026-09-22)
			var resting = DamageStage.Multiplier( ctx.Stacks, ctx.Shards, _health.IsValid() ? 1f - _health.Health01 : 0f, 0f, false, false, false );
			_edgeDamage = spell.Damage * resting * ModifierTables.EdgePct[edge];
			_edgeInfo = ctx.ToHitInfo().With( SpellHitFlags.Proc | SpellHitFlags.Blade, 1 );
		}

		var castSeconds = ResolvedCast.CastSecondsOf( spell, ctx.Stacks );
		if ( spell.Delivery == SpellDelivery.Beam )
		{
			// the staff stays raised (ChannelLoop) while the beam burns; the throw comes when its window closes
			_fireEnd = Time.Now + MathF.Max( 0.2f, spell.BeamSeconds * ModifierTables.SlothBeamWindowMul[ctx.Stack( ModifierId.Sloth )] );
			IsFiring = true;
			_state = State.Firing;
			_castEnd = _fireEnd + castSeconds;
			return;
		}
		_source.ChannelHold = false;         // graph: -> ChannelEnd (the throw)
		_castEnd = Time.Now + castSeconds;
		_nextCast = _castEnd;
		_state = State.Casting;
	}

	private void EndBeam()
	{
		foreach ( var beam in _beams ) if ( beam.IsValid() ) beam.End();
		_beams.Clear();
		IsFiring = false;
		_source.ChannelHold = false;         // the throw
		var castSeconds = _current is null ? 0.5f : ResolvedCast.CastSecondsOf( _current, Loadout?.Stacks ?? ModifierStacks.Empty );
		_castEnd = Time.Now + castSeconds;
		_nextCast = _castEnd;
		_state = State.Casting;
	}

	/// <summary>The beam emitter hands its rays over so the button can end them.</summary>
	public void RegisterBeam( SpellBeam beam )
	{
		if ( beam.IsValid() ) _beams.Add( beam );
	}

	// ------------------------------------------------------------------ owner: casts from elsewhere

	/// <summary>Counts the casts thrown from the button. An orbiting bolt remembers the one it was born under and flies off when it changes.</summary>
	public int CastSerial { get; private set; }

	/// <summary>The Echo: the cast repeats `repeats` times, a beat apart, free and fading, from where it was thrown.</summary>
	public void ScheduleEchoes( CastContext ctx, int repeats, float beat )
	{
		for ( var k = 1; k <= repeats; k++ )
		{
			var clone = ctx.Clone();
			clone.Flags |= SpellHitFlags.Echo;
			clone.SourceScale *= MathF.Pow( ModifierTables.EchoFalloff, k );   // each repeat weaker than the one before
			clone.BeamWindowSeconds = 1f;
			// a repeat keeps the hold it was thrown with, but the Nth cast and the cast that emptied the pool are moments
			// of the one cast (three free x4 echoes off one last drop, audit 2026-09-22)
			clone.Overcharged = false;
			clone.LastDrop = false;
			_scheduled.Add( (clone, Time.Now + beat * k, true) );
		}
	}

	private void TickScheduled()
	{
		if ( _scheduled.Count == 0 ) return;
		for ( var i = _scheduled.Count - 1; i >= 0; i-- )
		{
			var (ctx, fireAt, echo) = _scheduled[i];
			if ( Time.Now < fireAt ) continue;
			_scheduled.RemoveAt( i );
			CastPipeline.RunFrom( ctx, CastStage.Emission );
			if ( echo ) ShowEcho( ctx.Origin, ctx.Spell.Id );
			else ShowProc( ctx.Origin, ctx.Spell.Id );
		}
	}

	/// <summary>
	/// A free miniature of `parent` from somewhere else: a proc at depth 1, so it composes with every flight and
	/// impact scroll but is never multiplied, echoed or the source of another proc. A moment of the parent (a swell,
	/// an overcharge, the last drop) stays the parent's.
	/// </summary>
	private CastContext Miniature( CastContext parent, Vector3 origin, Vector3 direction, float sourceScale, float sizeScale )
	{
		var mini = parent.Clone();
		mini.Flags |= SpellHitFlags.Proc;
		mini.Depth = 1;
		mini.Origin = origin;
		mini.Direction = direction.IsNearZeroLength ? parent.Direction : direction.Normal;
		mini.AimPoint = origin + mini.Direction * 300f;
		mini.FixedOrigin = true;
		mini.SourceScale = sourceScale;
		mini.SizeScale = sizeScale;
		mini.ChargePower = 0f;
		mini.Overcharged = false;
		mini.LastDrop = false;
		mini.BeamWindowSeconds = 0.5f;
		return mini;
	}

	/// <summary>The Familiar: every wisp throws a small copy of the cast at the hostile nearest to it (along the aim when there is none).</summary>
	public void CastFamiliars( CastContext ctx )
	{
		var wisps = ModifierTables.FamiliarWisps[ctx.Stack( ModifierId.Familiar )];
		for ( var i = 0; i < wisps; i++ )
		{
			var at = FamiliarWisps.PositionOf( PlayerRoot, i, wisps, Time.Now );
			var target = NearestHostile( at, ModifierTables.FamiliarReach, needLos: true );
			var direction = target is null ? ctx.Direction : target.AimPoint - at;
			_scheduled.Add( (Miniature( ctx, at, direction, ModifierTables.FamiliarDamage, ModifierTables.FamiliarSize ), Time.Now + 0.06f * (i + 1), false) );
		}
	}

	/// <summary>The Hail: small copies of the cast drop from the ceiling onto hostiles near the caster, one after another.</summary>
	public void CastHail( CastContext ctx )
	{
		var bolts = ModifierTables.HailBolts[ctx.Stack( ModifierId.Hail )];
		if ( bolts <= 0 ) return;
		var near = new List<ICombatant>();
		foreach ( var npc in CombatTargets.Bodies( Team.Hostile ) )
			if ( npc.CanBeTargeted && npc.AimPoint.DistanceSquared( WorldPosition ) < ModifierTables.HailReach * ModifierTables.HailReach ) near.Add( npc );
		if ( near.Count == 0 ) return;
		for ( var i = 0; i < bolts; i++ )
		{
			var victim = near[Game.Random.Next( near.Count )];
			var chest = victim.AimPoint;
			// from under the ceiling above it (or as high as the hail reaches in a hall without one)
			var up = Scene.Trace.Ray( chest, chest + Vector3.Up * 420f ).WithoutTags( AimSolver.IgnoreTags ).WithoutTags( "npc", "enemy", "ragdoll" ).Run();
			var top = (up.Hit ? up.HitPosition : up.EndPosition) - Vector3.Up * 14f;
			if ( top.z - chest.z < 40f ) continue;
			var jitter = new Vector3( Game.Random.Float( -12f, 12f ), Game.Random.Float( -12f, 12f ), 0f );
			_scheduled.Add( (Miniature( ctx, top + jitter, Vector3.Down, ModifierTables.HailDamage, ModifierTables.HailSize ), Time.Now + ModifierTables.HailBeatSeconds * (i + 1), false) );
		}
	}

	/// <summary>The Retort: the owner was just hurt. A ring of small copies of the spell bursts out at chest height.</summary>
	public void OnHurt()
	{
		if ( IsProxy || Time.Now < _nextRetort ) return;
		var loadout = Loadout;
		var bolts = ModifierTables.RetortBolts[loadout?.Stack( ModifierId.Retort ) ?? 0];
		var spell = ActiveSpell;
		if ( bolts <= 0 || spell is null ) return;
		_nextRetort = Time.Now + ModifierTables.RetortCooldown;
		var origin = (_controller.IsValid() ? _controller.EyePosition : WorldPosition + Vector3.Up * 60f) - Vector3.Up * 18f;
		var parent = BuildContext( spell, SpellHitFlags.Proc, origin, Vector3.Forward, origin + Vector3.Forward * 300f );
		// lightning forks on its own: a few resolves round the compass are a ring of it
		if ( spell.Delivery == SpellDelivery.Instant ) bolts = Math.Max( 2, bolts / 4 );
		var start = Game.Random.Float( 0f, 360f );
		for ( var i = 0; i < bolts; i++ )
		{
			var direction = Rotation.FromYaw( start + 360f * i / bolts ).Forward;
			CastPipeline.RunFrom( Miniature( parent, origin + direction * 14f, direction, ModifierTables.RetortDamage, ModifierTables.RetortSize ), CastStage.Emission );
		}
		ShowProc( origin, spell.Id );
	}

	/// <summary>A random spell the player has learned, other than `exceptId`; any other spell when the book is that thin.</summary>
	public SpellDefinition RandomKnownSpell( string exceptId )
	{
		var inv = VoidMage.Inventory.PlayerInventory.Local;
		var pool = new List<SpellDefinition>();
		foreach ( var s in SpellDatabase.All )
			if ( s.Id != exceptId && (inv is null || inv.IsUnlocked( s.Id )) ) pool.Add( s );
		if ( pool.Count == 0 )
			foreach ( var s in SpellDatabase.All ) if ( s.Id != exceptId ) pool.Add( s );
		return pool.Count == 0 ? null : pool[Game.Random.Next( pool.Count )];
	}

	private ICombatant NearestHostile( Vector3 from, float reach, bool needLos )
	{
		ICombatant best = null;
		var bestD2 = reach * reach;
		foreach ( var npc in CombatTargets.Bodies( Team.Hostile ) )
		{
			if ( !npc.CanBeTargeted ) continue;
			var d2 = npc.AimPoint.DistanceSquared( from );
			if ( d2 >= bestD2 ) continue;
			if ( needLos && Scene.Trace.Ray( from, npc.AimPoint ).WithoutTags( SpellArea.LosIgnore ).WithoutTags( "spellfire" ).Run().Hit ) continue;
			best = npc; bestD2 = d2;
		}
		return best;
	}

	/// <summary>
	/// A free miniature cast from somewhere other than the button (The Conduit from a sword hit): its own pipeline
	/// run at depth 1, so it composes with every stage but never procs again.
	/// </summary>
	public bool CastProc( SpellHitFlags flags, Vector3 origin, Vector3 direction, string spellId, float sourceScale, float sizeScale )
	{
		if ( IsProxy ) return false;
		var spell = SpellDatabase.Get( spellId );
		if ( spell is null ) return false;
		var ctx = BuildContext( spell, flags, origin, direction.IsNearlyZero() ? Vector3.Forward : direction.Normal, origin + direction * 300f );
		ctx.Depth = 1;
		ctx.SourceScale = sourceScale;
		ctx.SizeScale = sizeScale;
		ctx.BeamWindowSeconds = 0.6f;
		var ok = CastPipeline.Run( ctx );
		if ( ok ) ShowProc( origin, spell.Id );
		return ok;
	}

	/// <summary>
	/// The sword landed on a body. Kindling primes the staff, The Edge adds the carried spell's strike (one host
	/// request, statuses and brands included), The Conduit rolls for a free miniature at the struck body.
	/// </summary>
	public void OnSwordHit( Vector3 contact, GameObject target )
	{
		if ( IsProxy ) return;
		var loadout = Loadout;
		if ( loadout is null ) return;
		var stacks = loadout.Stacks;
		if ( stacks.Has( ModifierId.Kindling ) ) Primed = true;

		if ( stacks.Has( ModifierId.Edge ) && EdgeActive && target.IsValid() && _edgeDamage > 0f )
		{
			var push = _controller.IsValid() ? _controller.EyeTransform.Rotation.Forward.WithZ( 0f ).Normal : Vector3.Zero;
			SpellNet.Impact( contact, Vector3.Up, target, _edgeDamage, push, PlayerRoot, _edgeInfo, 0f, 0f, 0 );
			ShowEdge( contact, EdgeSpellId );
		}

		var conduit = stacks[ModifierId.Conduit];
		if ( conduit <= 0 || Game.Random.Float( 0f, 1f ) >= ModifierTables.ConduitChance[conduit] ) return;
		var spell = loadout.ActiveSpell;
		if ( spell is null ) return;
		var origin = StaffTip() ?? contact;
		// a walker is aimed at by its chest; a boss part by where the blade actually met it (its origin can be metres away)
		var at = target.IsValid() && target.Components.Get<Boss.BossPart>() is null ? target.WorldPosition + Vector3.Up * 40f : contact;
		var direction = at - origin;
		if ( direction.Length < 10f ) direction = _controller.IsValid() ? _controller.EyeTransform.Rotation.Forward : Vector3.Forward;
		CastProc( SpellHitFlags.Proc, origin, direction.Normal, spell.Id, ModifierTables.ConduitDamageScale, ModifierTables.ConduitSizeScale );
	}

	private AimSolver.Aim SolveAim( SpellDefinition spell )
	{
		var eye = _controller.IsValid() ? _controller.EyePosition : WorldPosition + Vector3.Up * 60f;
		return AimSolver.Solve( Scene, PlayerRoot, eye, StaffTip(), MathF.Max( 4f, spell.Radius ), AimDistance );
	}

	private CastContext BuildContext( SpellDefinition spell, SpellHitFlags flags, Vector3 origin, Vector3 direction, Vector3 aimPoint )
	{
		var loadout = Loadout;
		return new CastContext
		{
			Spell = spell,
			Stacks = loadout?.Stacks ?? ModifierStacks.Empty,
			Flags = flags,
			Depth = 0,
			CastId = CastContext.NewCastId(),
			Caster = this,
			CasterRoot = PlayerRoot,
			Scene = Scene,
			Mana = _mana,
			Health = _health,
			Shards = _shards?.Carried ?? 0,
			Origin = origin,
			Direction = direction,
			AimPoint = aimPoint,
		};
	}

	/// <summary>Where the staff's gem is: the first-person staff for the owner in first person, else the body's staff.</summary>
	public Vector3? StaffTip()
	{
		if ( !IsProxy && _arms.IsValid() && _arms.RigVisible && _arms.StaffTipWorld is { } fp ) return fp;
		if ( _staffFollower.IsValid() && _staffFollower.PropWorld is { } prop )
		{
			var length = _arms.IsValid() ? _arms.TipLength : 50f;
			return prop.Position + prop.Rotation.Up * length;
		}
		return null;
	}
}
