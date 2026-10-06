using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Npc;

/// <summary>
/// The skeleton as every peer sees it: turns <see cref="SkeletonNpc"/>'s synced state into the graph's
/// parameters (animgraphs/skeleton_slave.vanmgrph). Locomotion is not told, it is measured: velocity from the
/// transform's own deltas, in the body's frame, on the radial blend (walk at 0.5, jog at 1, run at 1.5 forward),
/// the way heden's NpcBody does so proxies and host read identically. Attack, hurt and death pulses come from
/// synced counters (seeded on first sight so a late joiner does not replay history); the attack pulse is held two
/// frames so a blend frame cannot swallow it. `busy` mirrors the host's attack/hurt state so the graph leaves a
/// one-shot the moment the code does.
/// Feedback: hitstop freezes playback; a real hit swaps the body's materials white for a few frames; a blocked
/// hit flashes ONLY the shield's material slot blue (heden's boss block cue) with a clang.
/// </summary>
[Title( "Skeleton Body" ), Category( "Void Mage" ), Icon( "directions_walk" )]
public sealed class SkeletonBody : Component
{
	/// <summary>Speed tiers of the loco blend, the clips' own root travel (see SkeletonNpc).</summary>
	[Property] public float WalkSpeed { get; set; } = 59f;
	[Property] public float JogSpeed { get; set; } = 159f;
	[Property] public float RunSpeed { get; set; } = 243f;
	[Property] public float BlendDamping { get; set; } = 12f;
	/// <summary>White flash on a real hit: a blink, not a glow (0.15 read as too long in play).</summary>
	[Property] public float FlashSeconds { get; set; } = 0.06f;
	/// <summary>The victim half of the hitstop, ~5 frames; the attacker's arms freeze a frame later so the blade "sticks".</summary>
	[Property] public float HurtHitstop { get; set; } = 0.09f;
	[Property] public Material FlashMaterial { get; set; }
	/// <summary>Blue flash on the shield when it blocks; only slots whose authored material is in ShieldMaterials.</summary>
	[Property] public float ShieldFlashSeconds { get; set; } = 0.12f;
	[Property] public float BlockHitstop { get; set; } = 0.08f;
	[Property] public Material ShieldFlashMaterial { get; set; }
	[Property] public List<Material> ShieldMaterials { get; set; } = new();
	/// <summary>This enemy's shield is wood; a future enemy with an iron shield sets its own event here.</summary>
	[Property] public string BlockSound { get; set; } = "sounds/woodenshieldblock.sound";
	/// <summary>Played once at the death spot when the ragdoll takes over (a random bone-break take).</summary>
	[Property] public string DeathSound { get; set; } = "sounds/bonebreak.sound";
	/// <summary>Played on every peer the moment an attack starts (a random take): the audible telegraph.</summary>
	[Property] public string AttackSound { get; set; } = "sounds/skeletonattack.sound";
	/// <summary>Footsteps by travelled distance (heden's accumulator): the citizen stone/wood steps, pitched up and quiet, until bone steps exist.</summary>
	[Property, Group( "Footsteps" )] public bool Footsteps { get; set; } = true;
	[Property, Group( "Footsteps" )] public float StepVolume { get; set; } = 0.45f;
	[Property, Group( "Footsteps" )] public float StepPitch { get; set; } = 1.15f;
	/// <summary>Step length at walk and at run speed (the clips: 85 cm per step walking, 185 cm running).</summary>
	[Property, Group( "Footsteps" )] public float StrideAtWalk { get; set; } = 33f;
	[Property, Group( "Footsteps" )] public float StrideAtRun { get; set; } = 73f;

	private float _stepAccumulator;
	private float _lastStepAt;

	private void TickFootsteps( float dt, float speed, bool oneShot )
	{
		if ( !Footsteps || oneShot || speed < 15f ) { _stepAccumulator = 0f; return; }
		_stepAccumulator += speed * dt;
		var stride = speed.Remap( WalkSpeed, RunSpeed, StrideAtWalk, StrideAtRun ).Clamp( StrideAtWalk, StrideAtRun );
		if ( _stepAccumulator < stride || Time.Now - _lastStepAt < 0.15f ) return;
		_stepAccumulator -= stride;
		_lastStepAt = Time.Now;
		var surface = FootstepSurfaceResolver.Resolve( Scene, WorldPosition );
		FootstepSurfaceResolver.Play( FootstepSurfaceResolver.StepSound( surface ), WorldPosition, StepVolume, StepPitch );
	}
	/// <summary>The bone the shield hangs on: where a block's sparks appear.</summary>
	[Property] public string ShieldBone { get; set; } = "shield_l";
	/// <summary>Whether this model has the `sword` / `shield` body groups at all. Off for the monsters, whose vmdls have none.</summary>
	[Property] public bool WeaponBodyGroups { get; set; } = true;
	/// <summary>
	/// Flesh, not bone: the sword throws blood and leaves a splat on it (HitVfx.FleshHit) instead of the white
	/// bone sparks, on the live body and on its ragdoll. True on the Fortress corpse and every Quaternius monster,
	/// false on every skeleton and the knight. Prefab-authored, so every peer reads it without a sync.
	/// </summary>
	[Property] public bool Bleeds { get; set; } = false;
	/// <summary>
	/// MONSTERS ONLY. False on every skeleton, which is the switch that keeps MonsterCorpse off a skeleton
	/// corpse entirely: a skeleton's body-less bones are fingers and sockets carrying almost no skin, so they
	/// are better left exactly as they are.
	/// </summary>
	[Property] public bool WeldCorpseBones { get; set; } = false;
	/// <summary>
	/// Generated by tools/gen_monster_ragdoll.py: "bone:target" for the bones these packs parent to the
	/// armature root (the rat's and spider's feet), which must ride their leg rather than the torso. Every
	/// other body-less bone just follows its parent and needs no entry.
	/// </summary>
	[Property] public string[] CorpseWelds { get; set; } = System.Array.Empty<string>();
	/// <summary>Death by ragdoll (SkeletonCorpse). Off: the graph's `dead` state plays a death clip and the body stays until the host removes it (DeathLingerSeconds).</summary>
	[Property] public bool Ragdoll { get; set; } = true;
	/// <summary>A flier: the renderer (on a child object) floats this high above the navmesh position, bobbing HoverBob units at HoverBobHz.</summary>
	[Property, Group( "Hover" )] public float HoverHeight { get; set; } = 0f;
	[Property, Group( "Hover" )] public float HoverBob { get; set; } = 4f;
	[Property, Group( "Hover" )] public float HoverBobHz { get; set; } = 0.7f;
	private float _bobPhase = -1f;
	private bool _deathShown;

	public const float VelocityHoldSeconds = 0.25f;
	public const float BlendSampleEpsilon = 0.005f;
	public const float RunRadius = 1.5f;
	/// <summary>Every material that only shields wear (prep_skeleton_npc.py SHIELD_MATERIALS): the slots the block flash targets.</summary>
	public static readonly string[] ShieldMaterialPaths = { "shaders/dungeonshader_shield.vmat", "shaders/shieldironshader.vmat", "shaders/shieldwoodshader.vmat", "shaders/realmsshadershield.vmat" };
	public const string SwordBodyGroup = "sword";
	public const string ShieldBodyGroup = "shield";

	private SkeletonNpc _npc;
	private SkinnedModelRenderer _renderer;
	private Vector3 _prevPosition;
	private float _measureAge;
	private bool _hasMeasurement;
	private Vector3 _measuredVelocity;
	private float _moveX, _moveY;
	/// <summary>What the locomotion blend is being fed and what it measured (skeleton_speeds): 1.5 on Y is the run clip, 0.5 a walk.</summary>
	public float BlendX => _moveX;
	public float BlendY => _moveY;
	public float MeasuredSpeed => _measuredVelocity.Length;
	private int _seenAttack = -1, _seenHurt = -1, _seenFlinch = -1, _seenHop = -1;
	private int _attackPulseFrames, _hopPulseFrames;
	private float _hitstopUntil;
	private float _hitstopAt = float.PositiveInfinity;
	private float _hitstopPending;
	private float _flashUntil;
	private bool _flashApplied;
	private bool _flashPerSlot;
	private bool _dormantApplied;
	private int _swordApplied = -1, _shieldApplied = -1;
	private bool _wasAlive, _corpseSpawned;
	private readonly List<int> _flashedSlots = new();
	private Material _flashFallback, _shieldFlashFallback;
	private bool _shieldMaterialsLoaded;

	protected override void OnAwake()
	{
		_npc = Components.Get<SkeletonNpc>();
		_renderer = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		_visualYaw = _npc.IsValid() ? _npc.BodyYaw : WorldRotation.Yaw();
	}

	private float _visualYaw;

	/// <summary>
	/// Turn the MODEL to the host's <see cref="SkeletonNpc.BodyYaw"/>, on every peer.
	///
	/// The facing cannot live on the root any more: NavMeshAgent answers any write to its own GameObject's
	/// transform by clearing the crowd agent's path, so turning the root every frame left the skeleton without a
	/// path to run along. The model hangs on a child ("visual") instead, and because a child is not networked the
	/// yaw travels as a synced number and is applied here. The host already rate-limits it in
	/// <see cref="SkeletonNpc"/>, so it is used as-is; a proxy eases into it to hide the sync rate.
	/// </summary>
	private void TurnModel( float dt )
	{
		var target = _npc.BodyYaw;
		if ( IsProxy )
		{
			var delta = target - _visualYaw;
			delta = (delta % 360f + 540f) % 360f - 180f;   // shortest way round
			_visualYaw += delta * (1f - MathF.Exp( -14f * dt ));
		}
		else _visualYaw = target;
		// only ever the model's own object; falling back to the root would reintroduce the path wipe, so a prefab
		// whose renderer is still on the root simply does not turn rather than breaking its pathing
		var go = _renderer.GameObject;
		if ( go != GameObject ) go.WorldRotation = Rotation.FromYaw( _visualYaw );
	}

	protected override void OnUpdate()
	{
		if ( !_renderer.IsValid() || !_npc.IsValid() ) return;

		// asleep (SkeletonSystem): no renderer at all, so no animgraph, no bones, no draw; on waking the graph
		// restarts in idle and the counters below are untouched, so nothing pulses spuriously
		if ( _npc.Dormant != _dormantApplied )
		{
			_dormantApplied = _npc.Dormant;
			_renderer.Enabled = !_npc.Dormant;
			if ( !_npc.Dormant ) { _prevPosition = WorldPosition; _hasMeasurement = false; _measuredVelocity = Vector3.Zero; }
		}
		if ( _npc.Dormant ) return;
		// belt and braces (user, 2026-09-21: killed by a Tormented Soul that "was invisible"; no cause found by reading):
		// an awake body that is not dead is drawn, whatever switched its renderer off
		if ( _npc.State != SkeletonState.Dead && !_renderer.Enabled )
		{
			Log.Warning( $"[Skeleton] {_npc.KindName} was awake with its renderer off at {WorldPosition:0}: switched back on" );
			_renderer.Enabled = true;
		}

		// which sword and shield: the vmdl's `sword` / `shield` body groups hold every option of this kind; set before
		// the death branch so the corpse (which copies BodyGroups) carries the right ones
		if ( WeaponBodyGroups && _swordApplied != _npc.SwordIndex )
		{
			_swordApplied = _npc.SwordIndex;
			_renderer.SetBodyGroup( SwordBodyGroup, _npc.SwordIndex );
		}
		if ( WeaponBodyGroups && _shieldApplied != _npc.ShieldIndex )
		{
			_shieldApplied = _npc.ShieldIndex;
			_renderer.SetBodyGroup( ShieldBodyGroup, _npc.ShieldIndex );
		}

		var dt = Time.Delta;
		MeasureMotion( dt );
		TurnModel( dt );

		// a flier hovers: the visual body rides above the agent's floor position with a slow bob (each instance its own phase)
		if ( HoverHeight > 0f && _renderer.GameObject != GameObject )
		{
			if ( _bobPhase < 0f ) _bobPhase = Game.Random.Float( 0f, 10f );
			var bob = _npc.State == SkeletonState.Dead ? 0f : MathF.Sin( (Time.Now + _bobPhase) * MathF.Tau * HoverBobHz ) * HoverBob;
			_renderer.GameObject.LocalPosition = Vector3.Up * (HoverHeight + bob);
		}

		// death: the ragdoll takes over on every peer; a skeleton first seen already dead has nothing to seed from.
		// Without a ragdoll the graph plays its death clip and the body simply stays until the host removes it.
		if ( _npc.State == SkeletonState.Dead && !Ragdoll )
		{
			if ( !_deathShown )
			{
				_deathShown = true;
				if ( _wasAlive && !string.IsNullOrEmpty( DeathSound ) ) VoidMage.Audio.GameAudio.Play( DeathSound, WorldPosition + Vector3.Up * 50f );
				ClearSlotOverrides();
				_renderer.MaterialOverride = null;
				_renderer.PlaybackRate = 1f;
				_renderer.Set( "busy", false );
				_renderer.Set( "dead", true );
			}
			return;
		}
		if ( _npc.State == SkeletonState.Dead )
		{
			if ( !_corpseSpawned )
			{
				_corpseSpawned = true;
				if ( _wasAlive )
				{
					// one of the bone-break takes (the .sound picks at random), from the body, on every peer; a
					// crumbling minion (DeathKind 1) goes quietly, an exploding one (2) has its own burst
					if ( !string.IsNullOrEmpty( DeathSound ) && _npc.DeathKind == 0 ) VoidMage.Audio.GameAudio.Play( DeathSound, WorldPosition + Vector3.Up * 50f );
					// flesh comes apart: the killing blow's burst on every peer (a crumbling/exploding minion keeps its own)
					if ( Bleeds && _npc.DeathKind == 0 ) VoidMage.Combat.BloodVfx.Burst( Scene, WorldPosition + Vector3.Up * 40f, _npc.DeathPush );
					_tint = null;
					_renderer.MaterialOverride = null;
					SkeletonCorpse.Spawn( _renderer, _npc.DeathKind == 0 ? _npc.DeathPush : Vector3.Zero, _npc.DeathHitPosition,
						WeldCorpseBones ? (CorpseWelds ?? System.Array.Empty<string>()) : null );
				}
				ClearSlotOverrides();
				_renderer.MaterialOverride = null;
				_renderer.Enabled = false;
			}
			return;
		}
		_wasAlive = true;
		var oneShot = _npc.Busy;

		// locomotion blend from measured velocity, held at zero while a one-shot owns the body
		var speed = _measuredVelocity.Length;
		var targetX = 0f; var targetY = 0f;
		if ( !oneShot && speed > 8f )
		{
			var unit = _measuredVelocity / speed;
			var rot = _renderer.WorldRotation;   // the model's own object: the root no longer carries the facing
			var fwd = Vector3.Dot( rot.Forward, unit );
			var right = -Vector3.Dot( rot.Left, unit );
			var radius = speed <= WalkSpeed ? 0.5f * (speed / WalkSpeed)
				: speed <= JogSpeed ? 0.5f + 0.5f * (speed - WalkSpeed) / MathF.Max( 1f, JogSpeed - WalkSpeed )
				: 1f + 0.5f * MathF.Min( 1f, (speed - JogSpeed) / MathF.Max( 1f, RunSpeed - JogSpeed ) );
			// THE PACK HAS NO FORWARD-DIAGONAL CLIPS. gen_skeleton_animgraph.py lays out f/b/l/r/bl/br at walk 0.5
			// and jog 1.0 plus a single `run` at (0,1.5) -- there is no walk_fl/fr, no jog_fl/fr and no off-axis
			// run, because those clips do not exist. So jog_f (0,1) is INTERIOR to the blend hull and the hull's
			// whole forward edge is the chord run(0,1.5) -> jog_r(1,0), i.e. 1.5*|x| + y <= 1.5. Any forward
			// diagonal lands outside it and the node projects it onto that chord: the run gets blended with a
			// PURE SIDEWAYS JOG at the wrong cadence. At 30 degrees off it was 67 % run + 33 % jog_r (171 u/s of
			// footwork against 243 of travel); at 60 degrees, 27 % + 73 % (134 against 243). That is the
			// "struggling to walk while moving fast", and no amount of fixing the movement could reach it.
			// So forward travel COLLAPSES onto the forward axis as the heading error shrinks, and only lateral and
			// backward diagonals keep theirs -- l, r, bl and br all exist at both gaits.
			if ( fwd > 0f )
			{
				var forwardness = MathX.Remap( fwd, 0.2f, 0.55f, 0f, 1f ).Clamp( 0f, 1f );
				right = MathX.Lerp( right, 0f, forwardness );
				fwd = MathX.Lerp( fwd, 1f, forwardness );
				var n = MathF.Sqrt( right * right + fwd * fwd );
				if ( n > 1e-4f ) { right /= n; fwd /= n; }
			}
			// guard_loco has no `run` item at all, so a guarding body must never ask for the run ring.
			if ( _npc.Guard ) radius = MathF.Min( radius, 1f );
			targetX = right * radius;   // x = right
			targetY = fwd * radius;
		}
		var blend = 1f - MathF.Exp( -BlendDamping * dt );
		_moveX += (targetX - _moveX) * blend;
		_moveY += (targetY - _moveY) * blend;

		// pulses: attack held two frames, hurt exactly one, after the counter moves
		var pulseHurt = false;
		if ( _seenAttack < 0 ) _seenAttack = _npc.AttackCount;
		else if ( _npc.AttackCount != _seenAttack )
		{
			_seenAttack = _npc.AttackCount;
			_attackPulseFrames = 3;
			// a frozen graph (hitstop, PlaybackRate 0) never sees a trigger: an attack ends the freeze, always
			_hitstopUntil = 0f; _hitstopAt = float.PositiveInfinity;
			if ( !string.IsNullOrEmpty( AttackSound ) ) VoidMage.Audio.GameAudio.Play( AttackSound, WorldPosition + Vector3.Up * 55f );
		}
		TickFootsteps( dt, speed, oneShot );
		var pulseAttack = _attackPulseFrames > 0;
		if ( _attackPulseFrames > 0 ) _attackPulseFrames--;

		if ( _seenHurt < 0 ) _seenHurt = _npc.HurtCount;
		else if ( _npc.HurtCount != _seenHurt )
		{
			_seenHurt = _npc.HurtCount;
			pulseHurt = true;
			// the shield took it (held or broken): the blue shield cue, never the white body flash
			if ( _npc.HurtKind >= SkeletonNpc.HurtKindGuardHit ) Blocked();
			else { Flash(); _hitstopPending = HurtHitstop; }
			_hitstopAt = Time.Now;   // applied next frame so the graph sees the pulse first
		}
		// a hop is a one-shot like an attack: the same seed-then-pulse, held a few frames so a blend frame
		// cannot swallow it (a hopper's whole locomotion is this trigger)
		if ( _seenHop < 0 ) _seenHop = _npc.HopCount;
		else if ( _npc.HopCount != _seenHop )
		{
			_seenHop = _npc.HopCount;
			_hopPulseFrames = 3;
		}
		var pulseHop = _hopPulseFrames > 0;
		if ( _hopPulseFrames > 0 ) _hopPulseFrames--;

		if ( _seenFlinch < 0 ) _seenFlinch = _npc.FlinchCount;
		else if ( _npc.FlinchCount != _seenFlinch )
		{
			_seenFlinch = _npc.FlinchCount;
			Flash();
			Hitstop( HurtHitstop * 0.5f );
		}

		_renderer.Set( "move_x", OffBlendSample( _moveX, 1f ) );
		_renderer.Set( "move_y", OffBlendSample( _moveY, RunRadius ) );
		_renderer.Set( "guard", _npc.Guard );
		_renderer.Set( "busy", _npc.Busy );
		_renderer.Set( "attack_kind", (float)_npc.AttackKind );
		_renderer.Set( "attack_trigger", pulseAttack );
		_renderer.Set( "hurt_kind", (float)_npc.HurtKind );
		_renderer.Set( "hurt_trigger", pulseHurt );
		// only a hopper's graph declares this one; nothing else should be writing a parameter it has no slot for
		if ( _npc.Hops ) _renderer.Set( "hop_trigger", pulseHop );

		if ( Time.Now > _hitstopAt && !pulseHurt )
		{
			_hitstopAt = float.PositiveInfinity;
			Hitstop( _hitstopPending );
		}
		_renderer.PlaybackRate = Time.Now < _hitstopUntil ? 0f : _npc.InCombatPace ? _npc.Pace : 1f;
		UpdateFlash();
		UpdateTint();
	}

	// ------------------------------------------------------------------ feel

	/// <summary>Freeze the animation briefly (victim and attacker halves of melee hitstop).</summary>
	public void Hitstop( float seconds )
	{
		_hitstopUntil = MathF.Max( _hitstopUntil, Time.Now + seconds );
	}

	/// <summary>White flash over the whole body: a hit that did damage.</summary>
	public void Flash()
	{
		if ( !_renderer.IsValid() ) return;
		var material = FlashMaterial ?? (_flashFallback ??= Material.Load( "materials/hit_flash.vmat" ));
		if ( material is null ) return;
		ClearSlotOverrides();
		_renderer.MaterialOverride = material;
		_flashApplied = true;
		_flashPerSlot = false;
		_flashUntil = Time.Now + FlashSeconds;
	}

	/// <summary>The shield took it: blue flash on the shield's slots only, a longer hitstop, a clang, sparks off the shield. No white.</summary>
	private void Blocked()
	{
		_hitstopPending = BlockHitstop;
		// the sparks at the shield itself (its bone), sheared off sideways and down; the blade's own travel is not
		// known here, so the side is a coin flip, which reads fine for a shield swatting a blade aside
		var at = _renderer.IsValid() && _renderer.TryGetBoneTransform( ShieldBone, out var shieldBone ) ? shieldBone.Position : WorldPosition + WorldRotation.Forward * 20f + Vector3.Up * 45f;
		var side = (Game.Random.Next( 2 ) == 0 ? 1f : -1f) * WorldRotation.Left;
		VoidMage.Combat.HitVfx.ShieldHit( Scene, at, (side + WorldRotation.Forward * 0.5f).Normal, _npc.IsValid() && _npc.HurtKind == SkeletonNpc.HurtKindBlocked );
		if ( !string.IsNullOrEmpty( BlockSound ) ) VoidMage.Audio.GameAudio.Play( BlockSound, WorldPosition + Vector3.Up * 50f );
		if ( ShieldMaterials.Count == 0 && !_shieldMaterialsLoaded )
		{
			_shieldMaterialsLoaded = true;
			foreach ( var path in ShieldMaterialPaths )
			{
				var shield = Material.Load( path );
				if ( shield is not null ) ShieldMaterials.Add( shield );
			}
		}
		var flash = ShieldFlashMaterial ?? (_shieldFlashFallback ??= Material.Load( "materials/shield_block_flash.vmat" ));
		FlashMaterials( ShieldMaterials, ShieldFlashSeconds, flash );
	}

	/// <summary>heden's NpcBody.FlashMaterials: ONLY the slots whose authored material is in `targets` are overridden.</summary>
	public void FlashMaterials( List<Material> targets, float seconds, Material flashWith )
	{
		if ( !_renderer.IsValid() || targets is null || targets.Count == 0 || flashWith is null ) return;
		var slots = _renderer.Materials;
		if ( slots is null || slots.Count == 0 ) return;
		if ( _flashApplied && !_flashPerSlot ) _renderer.MaterialOverride = null;
		ClearSlotOverrides();
		_flashUntil = Time.Now + seconds;
		_flashApplied = true;
		_flashPerSlot = true;
		for ( var i = 0; i < slots.Count; i++ )
		{
			if ( !MaterialMatches( slots.GetOriginal( i ), targets ) ) continue;
			slots.SetOverride( i, flashWith );
			_flashedSlots.Add( i );
		}
	}

	private void ClearSlotOverrides()
	{
		if ( _flashedSlots.Count == 0 ) return;
		var slots = _renderer.IsValid() ? _renderer.Materials : null;
		foreach ( var index in _flashedSlots )
			if ( slots is not null && index < slots.Count ) slots.SetOverride( index, null );
		_flashedSlots.Clear();
	}

	private static bool MaterialMatches( Material original, List<Material> list )
	{
		if ( original is null ) return false;
		foreach ( var entry in list )
		{
			if ( entry is null ) continue;
			if ( entry == original ) return true;
			if ( !string.IsNullOrEmpty( entry.ResourcePath ) && entry.ResourcePath == original.ResourcePath ) return true;
		}
		return false;
	}

	private void UpdateFlash()
	{
		if ( !_flashApplied || Time.Now < _flashUntil ) return;
		_flashApplied = false;
		if ( _flashPerSlot )
		{
			// the exact indices are released one by one: ClearMaterialOverrides alone left heden's bodies stuck white
			ClearSlotOverrides();
			_renderer.ClearMaterialOverrides();
			_renderer.MaterialOverride = _tint;
		}
		else
		{
			_renderer.MaterialOverride = _tint;
		}
	}

	// ------------------------------------------------------------------ the standing tint (a raised ally, a slow, a burn)

	private Material _tint;
	private int _tintKind = -1;
	private Material _allyTint, _slowTint, _burnTint;

	/// <summary>A whole-body override that stays until cleared; the hit flash paints over it and hands it back.</summary>
	public void SetTintOverride( Material material )
	{
		_tint = material;
		if ( !_renderer.IsValid() || _flashApplied ) return;
		_renderer.MaterialOverride = material;
	}

	/// <summary>Which standing tint the synced state asks for: raised beats burning beats slowed beats none.</summary>
	private void UpdateTint()
	{
		var kind = _npc.Raised && _npc.Team == VoidMage.Combat.Team.Ally ? 1 : _npc.BurnKind > 0 ? 2 : _npc.SlowAmount > 0.01f ? 3 : 0;
		if ( kind == _tintKind ) return;
		_tintKind = kind;
		var material = kind switch
		{
			1 => _allyTint ??= Material.Load( "materials/turncoat_tint.vmat" ),
			2 => _burnTint ??= Material.Load( "materials/burn_tint.vmat" ),
			3 => _slowTint ??= Material.Load( "materials/slow_tint.vmat" ),
			_ => null,
		};
		SetTintOverride( material );
	}

	// ------------------------------------------------------------------ measurement (heden's NpcBody)

	private void MeasureMotion( float dt )
	{
		var position = WorldPosition;
		if ( !_hasMeasurement )
		{
			_hasMeasurement = true;
			_prevPosition = position;
			_measureAge = 0f;
			return;
		}
		_measureAge += dt;
		if ( position != _prevPosition )
		{
			_measuredVelocity = ((position - _prevPosition) / MathF.Max( _measureAge, 1e-4f )).WithZ( 0f );
			_prevPosition = position;
			_measureAge = 0f;
		}
		else if ( _measureAge > VelocityHoldSeconds )
		{
			_measuredVelocity = Vector3.Zero;
		}
	}

	/// <summary>Keeps a blend input off an exact sample point (heden: landing on one snapped to the bind pose).</summary>
	private static float OffBlendSample( float value, float limit )
	{
		if ( value >= limit - BlendSampleEpsilon ) return limit - BlendSampleEpsilon;
		if ( value <= -limit + BlendSampleEpsilon ) return -limit + BlendSampleEpsilon;
		if ( MathF.Abs( value ) < BlendSampleEpsilon ) return BlendSampleEpsilon;
		return value;
	}
}
