using System;
using System.Linq;
using Sandbox;
using VoidMage.Boons;
using VoidMage.Combat;
using VoidMage.Dungeon;
using VoidMage.Npc;

namespace VoidMage.Character;

/// <summary>
/// The player's health and what a hit feels like: a shove on the body, a red flash on the screen, a camera
/// punch, the hit sound. Owner-authoritative like the rest of the player. Death cuts the controls on the spot,
/// ragdolls the body on every peer (SkeletonCorpse, the skeletons' own corpse, dressed with the body's pieces)
/// and puts the owner's camera on the ragdoll's eyes (DeathCamera); after RespawnSeconds the player respawns
/// on the dungeon's spawn tier with full health and the corpse is left to expire.
/// </summary>
[Title( "Player Health" ), Category( "Void Mage" ), Icon( "favorite" )]
public sealed class PlayerHealth : Component, DamageRouter.IHittable, ICameraModifier, ICombatant
{
	// ICombatant: players are always on the Ally side
	public Team Team => Team.Ally;
	public bool CanBeTargeted => !IsDead;

	[Property] public float MaxHealth { get; set; } = 100f;
	/// <summary>How long the ragdoll plays before the respawn teleport.</summary>
	[Property] public float RespawnSeconds { get; set; } = 4f;
	/// <summary>Camera kick per hit, degrees of pitch (up) and roll, decaying over PunchSeconds.</summary>
	[Property] public float PunchPitch { get; set; } = 5f;
	[Property] public float PunchRoll { get; set; } = 3.5f;
	[Property] public float PunchSeconds { get; set; } = 0.35f;
	[Property] public float FlashSeconds { get; set; } = 0.45f;
	/// <summary>Seconds of no damage after a hit.</summary>
	[Property] public float InvulnerableSeconds { get; set; } = 0.25f;
	/// <summary>heden's red body flash: every renderer of the character (first-person arms included) swaps to this material.</summary>
	[Property] public Material BodyFlashMaterial { get; set; }
	[Property] public float BodyFlashSeconds { get; set; } = 0.15f;
	/// <summary>Played on every peer when an enemy lands on this player (a random take).</summary>
	[Property] public string HitSound { get; set; } = "sounds/playerhit.sound";

	[Sync] public float Health { get; set; } = 100f;
	[Sync] public int HitCount { get; set; }
	/// <summary>Wrath's cut on the cap (0.4 at three copies): the effective max is MaxHealth times this. Owner-written from the staff.</summary>
	[Sync] public float MaxHealthScale { get; set; } = 1f;

	public bool IsDead => Health <= 0f;
	/// <summary>The base max plus the run's boons (Ironblood, Berserker's cost), times Wrath's cut; never under 10.</summary>
	public float EffectiveMaxHealth => MathF.Max( 10f, (MaxHealth + (RunBoons?.Get( BoonStat.MaxHealth ) ?? 0f)) * MathF.Max( 0.05f, MaxHealthScale ) );

	private PlayerBoons _boons;
	/// <summary>The run's boons on this player (on the controller root RunManager spawned), or null in a test scene.</summary>
	private PlayerBoons RunBoons
	{
		get
		{
			if ( !_boons.IsValid() ) _boons = PlayerBoons.Find( _controller.IsValid() ? _controller.GameObject : GameObject );
			return _boons;
		}
	}
	public float Health01 => EffectiveMaxHealth > 0f ? Math.Clamp( Health / EffectiveMaxHealth, 0f, 1f ) : 0f;
	/// <summary>1 right after a hit, fading to 0: drives the HUD vignette.</summary>
	public float HitFlash01 => FlashSeconds > 0f ? Math.Clamp( 1f - (Time.Now - _lastHitAt) / FlashSeconds, 0f, 1f ) : 0f;
	public float LastHitAt => _lastHitAt;

	/// <summary>The local player's, for HUD panels.</summary>
	public static PlayerHealth Local { get; private set; }

	/// <summary>Dev (the director): nothing takes health off the local player; hits still flash, shove and sound.</summary>
	[ConVar( "player_god" )] public static bool God { get; set; }

	/// <summary>Where the body stands (the controller's root), for cell tests like the traps'.</summary>
	public Vector3 FeetPosition => _controller.IsValid() ? _controller.WorldPosition : WorldPosition;

	/// <summary>
	/// Moves the local player's controller root to `position` (feet), optionally facing `look`. The one teleport
	/// used by the run loop, the respawn and the boss arena: it clears the transform interpolation, because a hop
	/// of forty thousand units would otherwise lerp across the map for a frame, and it stops the body dead so the
	/// old floor's momentum does not carry into the new one. False without a local player.
	/// </summary>
	public static bool TeleportLocal( Vector3 position, Angles? look = null )
	{
		var health = Local;
		var c = health.IsValid() ? health.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors ) : null;
		var root = c.IsValid() ? c.GameObject : health.IsValid() ? health.GameObject : null;
		if ( !root.IsValid() ) return false;
		root.WorldPosition = position;
		root.Transform.ClearInterpolation();
		if ( c.IsValid() )
		{
			c.WishVelocity = Vector3.Zero;
			if ( c.Body.IsValid() ) c.Body.Velocity = Vector3.Zero;
			if ( look.HasValue ) c.EyeAngles = look.Value;
		}
		return true;
	}
	/// <summary>In something's grip right now (the body is placed by the holder every frame: nothing else may move it).</summary>
	public bool Held { get; private set; }

	/// <summary>
	/// Held by something (the Colossus's fist): movement off and the body stopped dead; the look stays. Released,
	/// the controls come back unless a run card owns the screen or the player is dead (the same rule as a drink's).
	/// </summary>
	public void Hold( bool held )
	{
		Held = held;
		if ( !_controller.IsValid() ) return;
		if ( !held )
		{
			if ( !VoidMage.Run.RunUi.Blocking && !IsDead ) _controller.UseInputControls = true;
			return;
		}
		_controller.UseInputControls = false;
		_controller.WishVelocity = Vector3.Zero;
		if ( _controller.Body.IsValid() ) _controller.Body.Velocity = Vector3.Zero;
	}

	/// <summary>Owner: shift the body by `delta` keeping its velocity (something solid it stood inside pushed it out).</summary>
	public void Nudge( Vector3 delta )
	{
		if ( IsProxy || !_controller.IsValid() || delta.IsNearZeroLength ) return;
		_controller.WorldPosition += delta;
	}

	/// <summary>
	/// Owner: environmental damage on a steady tick (standing in lava), past the per-attacker hit grace and without
	/// the push, the hitstop or the drink interrupt: the bar, the vignette and death, nothing else. `kind` should be
	/// a trap kind (`trap_lava`) so the trap boons and the trap test clamp apply.
	/// </summary>
	public void Scald( float damage, Vector3 from, string kind )
	{
		if ( IsProxy || IsDead ) return;
		bool trap = HitFeel.IsTrap( kind );
		var taken = trap ? HitFeel.AdjustTrap( damage ) : HitFeel.AdjustEnemy( damage );
		taken *= RunBoons?.DamageTakenScale( trap ) ?? 1f;
		if ( taken <= 0f || God ) return;
		_lastHitAt = Time.Now;
		Health = MathF.Max( 0f, Health - taken );
		VoidMage.UI.HitVignette.PushTaken( taken, trap );
		if ( Health <= 0f ) Die( Vector3.Up * 100f, from );
	}

	/// <summary>How tall the body stands right now (crouched or not), units: a flame jet at head height passes over a crouching player.</summary>
	public float BodyHeight => _controller.IsValid() ? (_controller.IsDucking ? _controller.DuckedHeight : _controller.BodyHeight) : 72f;

	/// <summary>Before the first-person arms (1000), so they ride the punched view.</summary>
	public int CameraOrder => 900;

	private PlayerController _controller;
	private float _lastHitAt = float.NegativeInfinity;
	private float _punchRoll;
	private float _diedAt;
	private bool _respawning;
	private float _bodyFlashUntil;
	private bool _bodyFlashApplied;
	private readonly System.Collections.Generic.Dictionary<Guid, float> _lastHitBy = new();
	private Material _bodyFlashFallback;
	private SkeletonCorpse _corpse;
	private DeathCamera _deathCamera;
	/// <summary>Proxies: whether the body is currently switched off (mirrors the synced IsDead).</summary>
	private bool _bodyHidden;
	private float _deadSeenAt;
	private readonly System.Collections.Generic.List<SkinnedModelRenderer> _hiddenDead = new();

	protected override void OnStart()
	{
		Health = EffectiveMaxHealth;
		_controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
		if ( !IsProxy ) Local = this;
	}

	protected override void OnDestroy()
	{
		if ( Local == this ) Local = null;
	}

	protected override void OnUpdate()
	{
		if ( _bodyFlashApplied && Time.Now >= _bodyFlashUntil ) SetBodyFlash( null );
		if ( IsProxy )
		{
			// State-driven, not RPC-driven, so a late joiner or a dropped ShowDeath still hides a dead body and a
			// respawn shows it again with nothing to send. The owner's body is FirstPersonView's. The hide waits a
			// beat for ShowDeath (which hides on its own once the corpse is built): the corpse copies its pose from
			// the live body, and a disabled renderer has none to copy.
			if ( IsDead != _bodyHidden )
			{
				if ( !IsDead ) { SetBodyVisible( true ); _deadSeenAt = 0f; }
				else
				{
					if ( _deadSeenAt <= 0f ) _deadSeenAt = Time.Now;
					if ( _corpse.IsValid() || Time.Now >= _deadSeenAt + 0.5f ) SetBodyVisible( false );
				}
			}
			return;
		}
		if ( !IsDead && Health < EffectiveMaxHealth )
		{
			// Mending, Vow of Emptiness: a slow tick toward the cap
			var regen = RunBoons?.Get( BoonStat.HealthRegen ) ?? 0f;
			if ( regen > 0f ) Health = MathF.Min( EffectiveMaxHealth, Health + regen * Time.Delta );
		}
		if ( !_respawning ) return;
		if ( Time.Now < _diedAt + RespawnSeconds ) return;
		Respawn();
	}

	// ------------------------------------------------------------------ red flash (heden's CharacterAnimator)

	/// <summary>Runs on every peer: the toon shader ignores Tint, so the flash is a material override on every renderer.</summary>
	private void FlashBody()
	{
		var material = BodyFlashMaterial ?? (_bodyFlashFallback ??= Material.Load( "materials/player_hit_flash.vmat" ));
		if ( material is null ) return;
		SetBodyFlash( material );
		_bodyFlashApplied = true;
		_bodyFlashUntil = Time.Now + BodyFlashSeconds;
	}

	private void SetBodyFlash( Material material )
	{
		var root = _controller.IsValid() ? _controller.GameObject : GameObject;
		foreach ( var renderer in root.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( !renderer.IsValid() || renderer.Tags.Has( "posesource" ) ) continue;
			renderer.MaterialOverride = material;
		}
		if ( material is null ) _bodyFlashApplied = false;
	}

	// ------------------------------------------------------------------ damage in

	/// <summary>Local damage sources (nothing hits the player from its own client yet, but the door is the same).</summary>
	public void ReceiveHit( in DamageRouter.Hit hit ) => TakeHit( hit.Damage, hit.Push, hit.Position, hit.Kind ?? "", hit.Attacker );

	/// <summary>ICombatant: the host landing damage on a player goes out as the broadcast the owner applies.</summary>
	public void ApplyHitHost( in DamageRouter.Hit hit )
	{
		if ( Networking.IsHost ) TakeHit( hit.Damage, hit.Push, hit.Position, hit.Kind ?? "", hit.Attacker );
	}

	/// <summary>Called by the host's skeletons; runs on every peer so all hear it, the owner applies it.</summary>
	[Rpc.Broadcast]
	public void TakeHit( float damage, Vector3 push, Vector3 from, string kind, GameObject attacker )
	{
		VoidMage.Audio.GameAudio.Play( HitSound, WorldPosition + Vector3.Up * 50f );
		if ( IsDead ) return;
		// the grace is per attacker (one skeleton cannot double-tap you), never shared: two skeletons landing
		// together both count
		var key = attacker.IsValid() ? attacker.Id : Guid.Empty;
		var grace = InvulnerableSeconds + (RunBoons?.Get( BoonStat.HitInvulnerability ) ?? 0f);
		if ( _lastHitBy.TryGetValue( key, out var last ) && Time.Now < last + grace ) return;
		_lastHitBy[key] = Time.Now;
		FlashBody();
		if ( IsProxy ) return;

		_lastHitAt = Time.Now;
		HitCount++;
		bool trap = HitFeel.IsTrap( kind );
		var taken = trap ? HitFeel.AdjustTrap( damage ) : HitFeel.AdjustEnemy( damage );
		taken *= RunBoons?.DamageTakenScale( trap ) ?? 1f;   // Ironblood, Juggernaut, Glass Blade, Dungeonwise
		if ( God ) taken = 0f;
		Health = MathF.Max( 0f, Health - taken );
		// what it cost, popping off the health bar: posted here rather than at the source so it is the number that
		// actually came off the bar, boons, test clamps and all (UI.HitVignette owns the bar and the popups)
		VoidMage.UI.HitVignette.PushTaken( taken, trap );
		_punchRoll = Game.Random.Next( 2 ) == 0 ? -PunchRoll : PunchRoll;
		HitFeel.TookHit();
		// the victim half of the hitstop: the arms freeze; the attacker freezes itself (SkeletonNpc.ShowHitLanded)
		var root = _controller.IsValid() ? _controller.GameObject : GameObject;
		root.Components.Get<FirstPersonArms>( FindMode.EverythingInSelfAndDescendants )?.Hitstop( HitFeel.TakenHitstopSeconds );
		// a potion being drunk is interrupted by any hit (the bottle is kept if nothing was swallowed yet)
		root.Components.Get<PotionDrinker>( FindMode.EverythingInSelfAndDescendants )?.OnHit();
		// The Retort: the staff answers a wound (a trap's is answered too; there is just nobody to hit)
		if ( Health > 0f ) root.Components.Get<VoidMage.Spells.SpellCaster>( FindMode.EverythingInSelfAndDescendants )?.OnHurt();

		if ( _controller.IsValid() && _controller.Body.IsValid() && !push.IsNearlyZero() )
			_controller.Body.Velocity += push.WithZ( MathF.Max( push.z, 0f ) ) + Vector3.Up * 40f;

		if ( Health <= 0f ) Die( push, from );
	}

	// ------------------------------------------------------------------ death

	/// <summary>Owner: the instant of death. Controls off, the killing push handed to the corpse, every peer told.</summary>
	private void Die( Vector3 push, Vector3 from )
	{
		_diedAt = Time.Now;
		_respawning = true;

		if ( _controller.IsValid() )
		{
			// movement off; look stays on so the third-person view can orbit the corpse
			_controller.UseInputControls = false;
			// the move mode keeps consuming WishVelocity every step with input off, so it has to be zeroed by hand;
			// the body stops dead here, the hit's shove (applied above) goes to the ragdoll instead
			_controller.WishVelocity = Vector3.Zero;
			if ( _controller.Body.IsValid() ) _controller.Body.Velocity = Vector3.Zero;
			// dying always cuts to third person: DeathCamera orbits the corpse (C still toggles back to the eyes);
			// Respawn puts the view back in first person
			_controller.ThirdPerson = true;
		}

		// the skeletons' death push: along the blow, lifted a little so the body goes over rather than along the floor
		var dir = push.IsNearZeroLength ? Vector3.Zero : (push.Normal + Vector3.Up * 0.35f).Normal;
		var origin = from.IsNearZeroLength ? WorldPosition + Vector3.Up * 50f : from;
		ShowDeath( dir, origin );
		// the run: an open exit menu closes, a transition in flight is dropped; the way home comes at Respawn
		var run = VoidMage.Run.RunManager.Instance;
		if ( run.IsValid() ) run.OnPlayerDied();
	}

	/// <summary>
	/// Every peer builds its own corpse from its own copy of the body (ModelPhysics refuses to build on a proxy, so
	/// the ragdoll is never networked, exactly like the skeletons'). The owner also captures where its eye sits in
	/// the head bone's frame right now and hands that to the death camera, so the view lands on the corpse's eyes.
	/// </summary>
	[Rpc.Broadcast]
	private void ShowDeath( Vector3 dir, Vector3 origin )
	{
		var body = BodyRenderer();
		if ( !body.IsValid() ) return;

		Transform? eyeLocal = null;
		var eyeNow = default( Transform );
		if ( !IsProxy && _controller.IsValid() )
		{
			var head = body.Model?.Bones?.GetBone( "head" );
			if ( head is not null && body.TryGetBoneTransform( head, out var headWorld ) )
			{
				eyeNow = new Transform( _controller.EyePosition, _controller.EyeAngles.ToRotation() );
				eyeLocal = headWorld.ToLocal( eyeNow );
			}
		}

		_corpse = SkeletonCorpse.SpawnCorpse( body, dir, origin, null, Pieces( body ), "player_corpse", pinned: true );

		if ( IsProxy )
		{
			SetBodyVisible( false );
			return;
		}
		if ( !_deathCamera.IsValid() ) _deathCamera = Components.Get<DeathCamera>( FindMode.EverythingInSelfAndAncestors );
		if ( _deathCamera.IsValid() && eyeLocal is { } local ) _deathCamera.Begin( _corpse, local, eyeNow );
	}

	/// <summary>The body: the one skinned renderer under the player that merges onto nothing (FirstPersonView's filter).</summary>
	private SkinnedModelRenderer BodyRenderer()
	{
		var root = _controller.IsValid() ? _controller.GameObject : GameObject;
		foreach ( var r in root.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( r.BoneMergeTarget is not null ) continue;
			if ( r.GameObject.Tags.Has( "viewmodel" ) || r.GameObject.Tags.Has( "posesource" ) ) continue;
			return r;
		}
		return null;
	}

	/// <summary>Hair, eyes, brows and the dressed clothing: every live renderer bone-merged onto the body. A piece the
	/// death hide switched off still counts (a proxy may hide the body before the ShowDeath RPC arrives).</summary>
	private System.Collections.Generic.List<SkinnedModelRenderer> Pieces( SkinnedModelRenderer body )
	{
		var pieces = new System.Collections.Generic.List<SkinnedModelRenderer>();
		foreach ( var r in body.GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( r == body || r.BoneMergeTarget != body ) continue;
			if ( (!r.Enabled && !_hiddenDead.Contains( r )) || !r.GameObject.Active ) continue;   // a hidden clothing region stays hidden
			pieces.Add( r );
		}
		return pieces;
	}

	/// <summary>Proxies only (the owner's FirstPersonView owns its body's render type): the body and its pieces on or off.
	/// Off means the renderers are DISABLED (a render type of Off only drops the shadows and leaves the body standing).</summary>
	private void SetBodyVisible( bool visible )
	{
		_bodyHidden = !visible;
		if ( visible ) { FirstPersonView.ShowDead( _hiddenDead ); return; }
		var body = BodyRenderer();
		if ( !body.IsValid() ) return;
		FirstPersonView.HideDead( body, _hiddenDead );
	}

	/// <summary>Dev: kill the local player outright (past the test-damage clamps), to watch the ragdoll and the death camera.</summary>
	[ConCmd( "player_kill" )]
	public static void KillLocal()
	{
		var player = Local;
		if ( !player.IsValid() || player.IsProxy || player.IsDead ) return;
		player._lastHitAt = Time.Now;
		player.Health = 0f;
		player.Die( Vector3.Zero, Vector3.Zero );
	}

	// ------------------------------------------------------------------ The Hollow

	/// <summary>Owner: a cast paid in blood. False (and nothing spent) when it would drop below 1 HP: at 1 HP a Hollow staff cannot cast.</summary>
	public bool TrySpendHealth( float amount )
	{
		if ( IsProxy || IsDead ) return false;
		if ( amount <= 0f ) return true;
		if ( Health - amount < 1f ) return false;
		Health -= amount;
		return true;
	}

	/// <summary>The host's reward for a hit or a kill (The Hollow's refund, the Siphon's leech): runs on every peer, only the owner applies it.</summary>
	[Rpc.Broadcast]
	public void Refund( float amount, string reason )
	{
		if ( IsProxy || IsDead || amount <= 0f ) return;
		Health = MathF.Min( EffectiveMaxHealth, Health + amount );
	}

	/// <summary>Owner: a healing potion. Capped at the effective max; nothing on a corpse.</summary>
	public void Heal( float amount )
	{
		if ( IsProxy || IsDead || amount <= 0f ) return;
		Health = MathF.Min( EffectiveMaxHealth, Health + amount );
	}

	/// <summary>Owner: back to the effective max (the start of a run). Nothing on a corpse.</summary>
	public void Refill()
	{
		if ( IsProxy || IsDead ) return;
		Health = EffectiveMaxHealth;
	}

	/// <summary>Owner: the cap shrank (a pact's cost, boons cleared at the end of a run): health comes down with it, never below 1.</summary>
	public void ClampToMax()
	{
		if ( IsProxy || IsDead ) return;
		Health = MathF.Max( 1f, MathF.Min( Health, EffectiveMaxHealth ) );
	}

	/// <summary>
	/// Owner: Wrath cuts the cap. Health is clamped down to the new max (never below 1) and never raised when the
	/// cap grows back: the copies you take off give you room, not health.
	/// </summary>
	public void SetMaxHealthScale( float scale )
	{
		if ( IsProxy ) return;
		scale = Math.Clamp( scale, 0.05f, 1f );
		if ( MathF.Abs( scale - MaxHealthScale ) < 0.001f ) return;
		MaxHealthScale = scale;
		if ( IsDead ) return;
		Health = MathF.Max( 1f, MathF.Min( Health, EffectiveMaxHealth ) );
	}

	/// <summary>The health on an attacker root (a player's controller object), or null for anything else.</summary>
	public static PlayerHealth Find( GameObject attackerRoot )
		=> attackerRoot.IsValid() ? attackerRoot.Components.Get<PlayerHealth>( FindMode.EverythingInSelfAndDescendants ) : null;

	private void Respawn()
	{
		_respawning = false;
		Health = EffectiveMaxHealth;
		// In the game a death ends the run: the RunManager takes the player home (and back under its own fade).
		// Test scenes without one keep the old respawn on the floor's spawn tier.
		var run = VoidMage.Run.RunManager.Instance;
		bool handled = run.IsValid() && run.OnPlayerRespawned();
		if ( !handled && !run.IsValid() )
		{
			var dungeon = Scene.GetAllComponents<DungeonBuilder>().FirstOrDefault();
			if ( dungeon.IsValid() && dungeon.PlayerSpawnWorld.HasValue ) TeleportLocal( dungeon.PlayerSpawnWorld.Value );
		}
		// controls back (the run's transition switches them off again itself), the view back on the controller;
		// the corpse stays where it fell until it expires
		if ( _controller.IsValid() )
		{
			_controller.UseInputControls = !handled;
			_controller.ThirdPerson = false;   // death switched to third person; a respawn is always back in first person
		}
		if ( _deathCamera.IsValid() ) _deathCamera.End();
		_corpse = null;
	}

	// ------------------------------------------------------------------ camera punch

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || camera != Scene.Camera ) return;
		var angles = view.Rotation.Angles();
		// the shake (taken hits) and the kick (dealt hits) run on real time so they play through the freeze frames
		var shake = HitFeel.ShakeAngles();
		var kick = HitFeel.KickAngles();
		angles.pitch += shake.pitch + kick.pitch;
		angles.yaw += shake.yaw + kick.yaw;
		angles.roll += shake.roll;
		view.Position += HitFeel.KickOffset();
		var t = (Time.Now - _lastHitAt) / MathF.Max( 0.01f, PunchSeconds );
		if ( t >= 0f && t < 1f )
		{
			// a sharp kick that rings back: one sine over the window, damped
			var k = MathF.Sin( t * MathF.PI * 2f ) * (1f - t);
			angles.pitch -= PunchPitch * k;
			angles.roll += _punchRoll * k;
		}
		view.Rotation = angles.ToRotation();
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view ) { }
}
