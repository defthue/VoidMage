using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The local player's first-person arms: a second skinned renderer carrying only the character's
/// arm and hand meshes (charactertoon/human_fp_arms.vmdl) running the arms animgraph. It is placed
/// every frame in PostCameraSetup, the same hook the engine's own weapon view models use, so the rig
/// follows the final camera view with no frame of lag: the clip's animated head bone is pinned to the
/// eye, and the whole rig turns with the view. Nothing writes bones: the graph output is the pose, and
/// both props are placed from the animated hand joints after the rig is placed, so they sit on the
/// hands the player sees.
///
/// The clips are mirrored: sword in the LEFT hand, the pack's shield fist on the right holds the staff.
/// This component picks the sword clip (state machine below) and hands it to ClipPoseSource, the synced
/// truth that also drives this renderer; the spell cast rides in the same graph (SpellCaster). The body
/// keeps the third-person graph for everyone else (FirstPersonView makes it shadows-only for the owner).
/// </summary>
[Title( "First Person Arms" ), Category( "Void Mage" ), Icon( "front_hand" )]
public sealed class FirstPersonArms : Component, ICameraModifier, PlayerController.IEvents
{
	public readonly record struct GripSettings( Vector3 Offset, Angles Rotation, bool FlipPalm )
	{
		/// <summary>A skin's own seat on top of the grip: turned about the model's shaft, end over end, and slid along it.</summary>
		public float Spin { get; init; }
		public bool Flip { get; init; }
		public float Shift { get; init; }
		/// <summary>The prop's own scale (a tuned import scale not baked into the player copy yet); 0 reads as 1.</summary>
		public float Scale { get; init; }

		/// <summary>The prop's transform for a grip transform (the grip itself is what the tip, the muzzle and the slash measure from).</summary>
		public Transform Seat( Transform grip )
		{
			float scale = Scale > 0f ? Scale : 1f;
			if ( Spin == 0f && !Flip && Shift == 0f && scale == 1f ) return grip;
			var rotation = grip.Rotation * global::Rotation.FromYaw( Spin ) * global::Rotation.FromRoll( Flip ? 180f : 0f );
			return new Transform( grip.Position + rotation.Up * Shift, rotation, scale );
		}
	}

	public const string DefaultSword = "dungeonpack/sm_wep_ornate_sword_02.vmdl";
	public const string DefaultStaff = "dungeonpack/sm_wep_staff_gem_01.vmdl";

	[Property] public Model ArmsModel { get; set; }

	[Property, Group( "Sword (left hand)" )] public Model SwordModel { get; set; }
	/// <summary>Handle middle relative to the hand joint, in the fist's frame: x along the fingers, y into the palm, z along the blade (inches).</summary>
	[Property, Group( "Sword (left hand)" )] public Vector3 SwordGripOffset { get; set; } = new( 1.6f, 1.0f, 3.5f );
	[Property, Group( "Sword (left hand)" )] public Angles SwordRotationOffset { get; set; }
	[Property, Group( "Sword (left hand)" )] public bool SwordFlipPalm { get; set; }

	[Property, Group( "Staff (right hand)" )] public Model StaffModel { get; set; }
	/// <summary>The staff pivots where a hand holds it, so z stays near 0.</summary>
	[Property, Group( "Staff (right hand)" )] public Vector3 StaffGripOffset { get; set; } = new( 1.6f, 1.0f, 0f );
	[Property, Group( "Staff (right hand)" )] public Angles StaffRotationOffset { get; set; }
	[Property, Group( "Staff (right hand)" )] public bool StaffFlipPalm { get; set; }
	/// <summary>Distance from the grip to the gem along the shaft (inches): where spells leave from.</summary>
	[Property, Group( "Staff (right hand)" )] public float StaffTipLength { get; set; } = 50f;

	/// <summary>
	/// The potion bottle while drinking (the staff hides). Measured off the model's OWN centre, because the pack's
	/// two flasks do not share a pivot (potion 07 is 20 in tall pivoted at its base, potion 06 is 16 in pivoted in
	/// the middle): z is how far above that centre the fist grips, so 0 holds either bottle by its middle.
	/// </summary>
	[Property, Group( "Potion (right hand)" )] public Vector3 PotionGripOffset { get; set; } = new( 5.1f, 5.2f, -4.5f );
	[Property, Group( "Potion (right hand)" )] public Angles PotionRotationOffset { get; set; } = new( 0f, 0f, 10f );
	[Property, Group( "Potion (right hand)" )] public bool PotionFlipPalm { get; set; }
	/// <summary>The mana flask (potion 06) has its own grip, since the two bottles are different shapes; PotionGripOffset is the healing one (potion 07).</summary>
	[Property, Group( "Potion (right hand)" )] public Vector3 ManaGripOffset { get; set; } = new( 4.1f, 2.7f, -4.5f );
	[Property, Group( "Potion (right hand)" )] public Angles ManaRotationOffset { get; set; } = new( 10f, 0f, 0f );
	/// <summary>
	/// The drink clip brings the hand to the character's mouth, and in first person the mouth is where the camera
	/// is: the hand passed through the head and came out behind it. While the drink plays the whole rig is pushed
	/// by however much the drinking hand needs to keep this far IN FRONT of the eye (inches). Continuous by
	/// construction: the push is zero while the arm is down, so nothing pops.
	/// </summary>
	[Property, Group( "Potion (right hand)" )] public float DrinkHandForward { get; set; } = 5f;
	/// <summary>And no higher than this above the eye (inches): the rig sinks the rest of the way.</summary>
	[Property, Group( "Potion (right hand)" )] public float DrinkHandCeiling { get; set; } = 4f;
	/// <summary>Cap on that push, so a strange frame cannot fling the arms across the screen.</summary>
	[Property, Group( "Potion (right hand)" )] public float DrinkPushMax { get; set; } = 26f;

	/// <summary>Where the rig's head sits relative to the eye, in view space (x forward, y left, z up).</summary>
	[Property] public Vector3 ViewOffset { get; set; }
	[Property] public Angles ViewAngleOffset { get; set; }

	/// <summary>Renders the arms in the view model layer (own depth range, never clips into walls).</summary>
	[Property] public bool ViewModelLayer { get; set; } = true;
	/// <summary>Camera near plane while the arms show (inches). The scene's 10 cuts the upper arms off; 0 leaves the camera alone.</summary>
	[Property] public float NearClip { get; set; } = 2f;

	/// <summary>Seconds after a quick swing before the next attack press counts (presses inside it are dropped).</summary>
	[Property, Group( "Attacks" )] public float LightAttackCooldown { get; set; } = 0.33f;
	/// <summary>
	/// Seconds after a heavy swing (Shift+attack) before the next attack press counts. The heavy clips only
	/// cut on frames 18-22 (0.6-0.73 s, measured), so anything shorter lets a second press cancel the swing
	/// before its blade, whoosh and hit ever arrive.
	/// </summary>
	[Property, Group( "Attacks" )] public float HeavyAttackCooldown { get; set; } = 0.75f;

	/// <summary>This frame's staff transform in the world while the rig shows, for the charge orb and the muzzle.</summary>
	public Transform? StaffWorld { get; private set; }
	public Vector3? StaffTipWorld => StaffWorld is { } t ? t.Position + t.Rotation.Up * TipLength : null;
	/// <summary>Grip to gem for the staff in hand: the equipped skin's own length, or StaffTipLength.</summary>
	public float TipLength
	{
		get
		{
			var skin = SkinFor( HandSide.Right );
			var tune = skin is null ? null : VoidMage.Inventory.WeaponTuning.Get( skin.Id );
			return tune?.TipLength ?? (skin?.TipLength ?? StaffTipLength) * (tune?.SizeRatio ?? 1f);
		}
	}

	/// <summary>Re-apply the view model layer flag to the arms and every prop (after ViewModelLayer changed at runtime).</summary>
	public void RefreshRenderFlags()
	{
		_flagsApplied = false;
		_potionFlagsApplied = false;
	}

	/// <summary>The skin in a hand right now (null without an inventory): what the weapon tuner edits.</summary>
	public VoidMage.Inventory.WeaponSkin SkinInHand( HandSide hand ) => SkinFor( hand );
	/// <summary>This frame's sword transform in the world while the rig shows: origin at the guard, blade along Up (the slash ribbon samples it).</summary>
	public Transform? SwordWorld { get; private set; }
	public bool RigVisible => _rig.IsValid() && _rig.Enabled;

	/// <summary>The drink clip is playing (PotionDrinker owns the effect and the throw; this owns the clip and the bottle prop).</summary>
	public bool IsDrinking => _state == State.Drink;
	/// <summary>Whether the bottle prop shows in the drinking hand (off once it is thrown).</summary>
	public bool BottleVisible { get; set; }
	/// <summary>This frame's bottle transform in the world while it shows (origin at the bottle's base, up along its neck).</summary>
	public Transform? PotionWorld { get; private set; }

	public GripSettings GripFor( HandSide hand )
	{
		var grip = hand == HandSide.Left
			? new GripSettings( SwordGripOffset, SwordRotationOffset, SwordFlipPalm )
			: new GripSettings( StaffGripOffset, StaffRotationOffset, StaffFlipPalm );
		// a skin brings its own turn, flip and slide, and the in-game tuning (weapon_tune) sits on top of everything
		var skin = SkinFor( hand );
		if ( skin is null ) return grip;
		var tune = VoidMage.Inventory.WeaponTuning.Get( skin.Id );
		if ( tune is null ) return skin.Starter ? grip : grip with { Spin = skin.Spin, Flip = skin.Flip, Shift = skin.Shift };
		var r = tune.Rotation;
		return grip with
		{
			Offset = grip.Offset + tune.Offset,
			Rotation = new Angles( grip.Rotation.pitch + r.pitch, grip.Rotation.yaw + r.yaw, grip.Rotation.roll + r.roll ),
			Spin = tune.Spin ?? skin.Spin,
			Flip = tune.Flip ?? skin.Flip,
			Shift = tune.Shift ?? skin.Shift * tune.SizeRatio,
			Scale = tune.PropScale,
		};
	}
	/// <summary>The bottle's grip, shared with the third-person follower so one offset serves both. The model's own centre is taken out, so a bottle pivoted at its base and one pivoted in the middle sit in the fist alike.</summary>
	public GripSettings PotionGripFor( Model bottle )
	{
		var mana = IsManaBottle( bottle );
		var offset = mana ? ManaGripOffset : PotionGripOffset;
		var reference = PotionDrinker.GripReference( bottle );
		if ( reference is not null && !reference.IsError ) offset.z -= reference.Bounds.Center.z;
		return new GripSettings( offset, mana ? ManaRotationOffset : PotionRotationOffset, PotionFlipPalm );
	}

	/// <summary>Whether a bottle model is the mana flask (potion 06); anything else takes the healing grip.</summary>
	public static bool IsManaBottle( Model bottle )
	{
		if ( bottle is null ) return false;
		var path = bottle.ResourcePath ?? bottle.Name ?? "";
		return path.Contains( "potion_06", StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>Tuning: move one bottle's grip by these amounts (inches along fingers / into palm / along the bottle, and degrees).</summary>
	public void NudgePotionGrip( bool mana, Vector3 move, Angles turn )
	{
		if ( mana )
		{
			ManaGripOffset += move;
			ManaRotationOffset = new Angles( ManaRotationOffset.pitch + turn.pitch, ManaRotationOffset.yaw + turn.yaw, ManaRotationOffset.roll + turn.roll );
		}
		else
		{
			PotionGripOffset += move;
			PotionRotationOffset = new Angles( PotionRotationOffset.pitch + turn.pitch, PotionRotationOffset.yaw + turn.yaw, PotionRotationOffset.roll + turn.roll );
		}
	}

	/// <summary>Tuning: put a different bottle in the drinking hand without restarting the clip.</summary>
	public void SwapBottle( Model bottle ) => SetBottle( bottle );

	public Model ModelFor( HandSide hand )
	{
		// a skin other than the starting weapon wins; the starter keeps the prefab's (hand-remapped) model
		var skin = SkinFor( hand );
		if ( skin is not null && !skin.Starter && VoidMage.Inventory.WeaponSkins.ModelOf( skin ) is { } skinned ) return skinned;
		return hand == HandSide.Left
			? SwordModel ?? Model.Load( DefaultSword )
			: StaffModel ?? Model.Load( DefaultStaff );
	}

	private VoidMage.Inventory.PlayerInventory _inventory;
	private float _nextInventoryLook;

	/// <summary>The equipped skin for a hand (synced, so proxies show it too); null without an inventory.</summary>
	private VoidMage.Inventory.WeaponSkin SkinFor( HandSide hand )
	{
		// test scenes have no inventory: look again now and then, not every frame
		if ( !_inventory.IsValid() && Time.Now >= _nextInventoryLook )
		{
			_nextInventoryLook = Time.Now + 1f;
			_inventory = Components.Get<VoidMage.Inventory.PlayerInventory>( FindMode.EverythingInSelfAndAncestors );
		}
		return _inventory.IsValid() ? VoidMage.Inventory.WeaponSkins.Get( _inventory.SkinFor( hand ) ) : null;
	}

	// Locomotion clip thresholds come from CharacterLocomotionAnimator's speeds (midpoints), so one
	// place owns the numbers; these are the fallbacks without it.
	private const float MovingThreshold = 15f;
	private float RunThreshold => _locomotion.IsValid() ? (_locomotion.WalkSpeed + _locomotion.RunSpeed) * 0.5f : 120f;
	// Sprint sits above the side sprint (Shift while strafing / backing up), which keeps the run arms.
	private float SprintThreshold => _locomotion.IsValid() ? _locomotion.SprintThreshold : 255f;

	// Clip names as listed in human_fp_arms.vmdl (the retargeted, mirrored pack). The looping ones are
	// the graph's gait selector (ClipPoseSource.GaitClips); the rest play as one-shots.
	public const string ClipSelect = "anim_select";
	public const string ClipIdle = "anim_idle_breathing";
	public const string ClipWalk = "anim_walk";
	public const string ClipRun = "anim_run";
	public const string ClipSprintIn = "anim_sprint_in";
	public const string ClipSprint = "anim_sprint_loop";
	public const string ClipSprintOut = "anim_sprint_out";
	public const string ClipJumpIn = "anim_jump_in";
	public const string ClipJump = "anim_jump_loop";
	public const string ClipJumpOut = "anim_jump_out";
	/// <summary>The UAL Drink take, right arm over the idle (tools/layer_clip.py): 68 frames, hand at the mouth by frame 3, flick at 60-63.</summary>
	public const string ClipDrink = "drink";
	/// <summary>The UAL PickUp_Table reach, mirrored onto the staff arm as a hand path over the idle (layer_clip.py --follow-hand): 26 frames, the staff stays in hand.</summary>
	public const string ClipPickup = "pickup_table";

	private static readonly string[] LightCombo =
	{
		"anim_attack_swing_right_01", "anim_attack_swing_left_01", "anim_attack_swing_down",
		"anim_attack_swing_right_02", "anim_attack_swing_left_02", "anim_attack_swing_right_03",
	};
	private static readonly string[] HeavyCombo =
	{
		"anim_attack_heavy_swing_right", "anim_attack_heavy_swing_left", "anim_attack_heavy_swing_down",
	};

	/// <summary>Which combo a clip belongs to and where in it ("light" 2/6), for logs that name a swing.</summary>
	public static bool TryDescribeAttack( string clip, out string set, out int index, out int count )
	{
		index = Array.IndexOf( LightCombo, clip );
		if ( index >= 0 ) { set = "light"; count = LightCombo.Length; return true; }
		index = Array.IndexOf( HeavyCombo, clip );
		if ( index >= 0 ) { set = "heavy"; count = HeavyCombo.Length; return true; }
		set = ""; count = 0;
		return false;
	}

	private enum State { Select, Locomotion, SprintIn, SprintOut, JumpIn, Jump, JumpOut, Attack, Drink, Pickup }

	private PlayerController _controller;
	private PlayerHealth _health;
	private ClipPoseSource _poseSource;
	private CharacterLocomotionAnimator _locomotion;
	private GameObject _rig;
	private SkinnedModelRenderer _renderer;
	private GameObject _sword;
	private GameObject _staff;
	private GameObject _potion;
	private Model _bottleModel;
	private bool _potionFlagsApplied;
	private Vector3 _headRest;
	private bool _flagsApplied;
	private readonly BoneCache _bones = new();
	private readonly Dictionary<string, Vector3> _joints = new();

	private State _state = State.Select;
	private string _current = "";
	private float _playedAt;
	private int _combo;
	private bool _sprinting;
	private bool _airborne;
	private bool _jumpQueued;
	private float _nextAttackAt;

	int ICameraModifier.CameraOrder => 1000;

	protected override void OnEnabled()
	{
		_state = State.Select;
		_current = "";
	}

	protected override void OnDisabled()
	{
		if ( _rig.IsValid() ) _rig.Destroy();
		_rig = null; _renderer = null; _sword = null; _staff = null; _potion = null;
		_flagsApplied = false;
		_potionFlagsApplied = false;
		StaffWorld = null;
		SwordWorld = null;
		PotionWorld = null;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		if ( !_controller.IsValid() )
			_controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
		if ( !_poseSource.IsValid() )
			_poseSource = ClipPoseSource.Find( this );
		if ( !_locomotion.IsValid() )
			_locomotion = Components.Get<CharacterLocomotionAnimator>( FindMode.EverythingInSelfAndAncestors );
		if ( !_health.IsValid() )
			_health = Components.Get<PlayerHealth>( FindMode.EverythingInSelfAndAncestors );

		// Dead: no attack input, no arms (the death camera rides the ragdoll); the rig comes back on respawn.
		var dead = _health.IsValid() && _health.IsDead;

		// The clip choice runs every frame whatever the view: it is what the body (and everyone else) sees.
		if ( _controller.IsValid() && !dead )
			UpdateState();

		var firstPerson = _controller.IsValid() && !_controller.ThirdPerson && !dead && !Hidden;
		if ( !firstPerson )
		{
			if ( _rig.IsValid() && _rig.Enabled ) _rig.Enabled = false;
			StaffWorld = null;
			SwordWorld = null;
			PotionWorld = null;
			return;
		}

		EnsureRig();
		if ( !_renderer.IsValid() ) return;

		if ( !_rig.Enabled ) _rig.Enabled = true;
		ApplyFlags();
		// the bottle prop comes and goes with each drink: its layer flags are applied whenever it is (re)made
		if ( !_potionFlagsApplied && _potion.IsValid() && _potion.Enabled ) _potionFlagsApplied = ApplyPropFlags( _potion );

		// The same state the hidden source plays, on this renderer (a re-shown rig seeks mid-clip).
		if ( _poseSource.IsValid() )
			_poseSource.Drive( _renderer );
		// hitstop freezes the arms for a moment; potion tuning freezes them on the hold for as long as it runs
		var frozen = _poseSource.IsValid() && _poseSource.Frozen;
		_renderer.PlaybackRate = frozen || Time.Now < _hitstopUntil ? 0f : _poseSource.IsValid() ? _poseSource.RendererRate : 1f;
	}

	// ------------------------------------------------------------------ rig

	private void EnsureRig()
	{
		if ( _rig.IsValid() ) return;

		var model = ArmsModel ?? Model.Load( "charactertoon/human_fp_arms.vmdl" );
		if ( model is null || model.IsError )
		{
			Log.Warning( "[FirstPersonArms] charactertoon/human_fp_arms.vmdl missing" );
			return;
		}

		_rig = new GameObject( true, "fp_arms" )
		{
			Parent = GameObject,
			Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute,
		};
		_rig.Tags.Add( "firstperson", "viewmodel" );

		_renderer = _rig.Components.Create<SkinnedModelRenderer>();
		_renderer.Model = model;
		_renderer.UseAnimGraph = true;
		_renderer.RenderType = ModelRenderer.ShadowRenderType.On;

		_headRest = RestPosition( model, "head" );

		// Both props are children of the rig and get their transform from the hand joints every frame.
		_sword = CreateProp( "fp_sword", ModelFor( HandSide.Left ) );
		_staff = CreateProp( "fp_staff", ModelFor( HandSide.Right ) );

		_flagsApplied = false;
	}

	private GameObject CreateProp( string name, Model model )
	{
		if ( model is null || model.IsError ) return null;
		var go = new GameObject( false, name ) { Parent = _rig };
		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = model;
		renderer.RenderType = ModelRenderer.ShadowRenderType.On;
		return go;
	}

	private void ApplyFlags()
	{
		if ( _flagsApplied ) return;

		var so = _renderer.SceneObject;
		if ( !so.IsValid() ) return;
		so.Flags.ViewModelLayer = ViewModelLayer;
		so.Flags.CastShadows = false;

		// not built yet: come back next frame (no array per call; this used to allocate every frame a prop lagged)
		if ( !ApplyPropFlags( _sword ) || !ApplyPropFlags( _staff ) ) return;

		_flagsApplied = true;
	}

	private bool ApplyPropFlags( GameObject prop )
	{
		if ( !prop.IsValid() ) return true;
		var propSo = prop.Components.Get<ModelRenderer>()?.SceneObject;
		if ( !propSo.IsValid() ) return false;
		propSo.Flags.ViewModelLayer = ViewModelLayer;
		propSo.Flags.CastShadows = false;
		return true;
	}

	/// <summary>A bone's rest position in model space, from the bind pose chain.</summary>
	private static Vector3 RestPosition( Model model, string boneName )
	{
		var bone = model.Bones.GetBone( boneName );
		if ( bone is null ) return Vector3.Zero;

		var chain = new List<BoneCollection.Bone>();
		for ( var b = bone; b is not null; b = b.Parent ) chain.Add( b );

		var tx = global::Transform.Zero;
		for ( var i = chain.Count - 1; i >= 0; i-- )
			tx = tx.ToWorld( chain[i].LocalTransform );

		return tx.Position;
	}

	// ------------------------------------------------------------------ placement

	private static readonly string[] JointNames =
	{
		"head",
		"hand_l", "index_01_l", "pinky_01_l", "middle_01_l", "thumb_01_l",
		"hand_r", "index_01_r", "pinky_01_r", "middle_01_r", "thumb_01_r",
	};

	/// <summary>The grip joints of one hand, in SwordGrip.Compute order: hand, index, pinky, middle, thumb. Fixed strings, so a lookup never builds one.</summary>
	internal static readonly string[] LeftGripJoints = { "hand_l", "index_01_l", "pinky_01_l", "middle_01_l", "thumb_01_l" };
	internal static readonly string[] RightGripJoints = { "hand_r", "index_01_r", "pinky_01_r", "middle_01_r", "thumb_01_r" };
	internal static string[] GripJoints( HandSide hand ) => hand == HandSide.Left ? LeftGripJoints : RightGripJoints;

	/// <summary>
	/// The weapon tuner's free look: the camera flies around the weapon, so the rig must stop following it. While set, the
	/// rig stays on the last eye view the player controller gave (recorded here, before the tuner moves the camera).
	/// </summary>
	public static bool HoldEye { get; set; }
	/// <summary>Dev (the director): the arms rig off, for a clean first-person plate.</summary>
	public static bool Hidden { get; set; }
	/// <summary>1 = the arms hang out of sight below the view, 0 = in place. The main menu raises them once the player has the camera.</summary>
	public static float IntroLower { get; set; }
	private const float IntroDrop = 55f;
	private Vector3 _eyePosition;
	private Rotation _eyeRotation;
	private bool _eyeKnown;

	/// <summary>Runs after the controller's view: the upper arms sit inside the default near plane, so it comes in.</summary>
	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || camera != Scene.Camera ) return;
		if ( !HoldEye || !_eyeKnown )
		{
			_eyePosition = view.Position;
			_eyeRotation = view.Rotation;
			_eyeKnown = true;
		}
		if ( NearClip <= 0f ) return;
		if ( _rig.IsValid() && _rig.Enabled )
			view.ZNear = NearClip;
	}

	/// <summary>
	/// After the camera is final: the rig goes where the view is, head on the eye, and the props go
	/// onto the hands. The engine's own view models place themselves here; nothing lags the view.
	/// </summary>
	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view )
	{
		if ( IsProxy || camera != Scene.Camera ) return;
		if ( !_rig.IsValid() || !_rig.Enabled || !_renderer.IsValid() ) return;

		// This frame's bones were built on the rig's previous transform: read the joints into rig
		// space through that transform before the rig moves.
		var previous = _rig.WorldTransform;
		var model = _renderer.Model;
		_joints.Clear();
		foreach ( var name in JointNames )
		{
			var bone = _bones.Get( model, name );
			if ( bone is not null && _renderer.TryGetBoneTransform( bone, out var world ) )
				_joints[name] = previous.ToLocal( world ).Position;
		}

		var headLocal = _joints.TryGetValue( "head", out var head ) ? head : _headRest;
		// the eye the rig hangs from: the view, or the held one while the tuner flies the camera around
		var eyeRotation = HoldEye && _eyeKnown ? _eyeRotation : view.Rotation;
		var eyePosition = HoldEye && _eyeKnown ? _eyePosition : view.Position;
		var rotation = eyeRotation * ViewAngleOffset.ToRotation();
		var position = eyePosition + rotation * (ViewOffset + DrinkPush( headLocal ) - headLocal);
		if ( IntroLower > 0f ) position += rotation.Down * (IntroLower * IntroDrop);
		_rig.WorldTransform = new Transform( position, rotation );
		var now = _rig.WorldTransform;

		// a skin chosen in the Armory swaps the model in the hand
		RefreshPropModel( _sword, ModelFor( HandSide.Left ) );
		RefreshPropModel( _staff, ModelFor( HandSide.Right ) );

		PlaceProp( _sword, LeftGripJoints, GripFor( HandSide.Left ), out var swordLocal );
		SwordWorld = swordLocal is { } w ? now.ToWorld( w ) : null;

		// the drinking hand: the bottle instead of the staff (PlaceProp re-enables what it places, so the staff is
		// simply not placed while the drink runs; it comes back the frame the drink ends)
		Transform? staffLocal = null, potionLocal = null;
		if ( _state == State.Drink )
		{
			if ( _staff.IsValid() && _staff.Enabled ) _staff.Enabled = false;
			if ( BottleVisible ) PlaceProp( _potion, RightGripJoints, PotionGripFor( _bottleModel ), out potionLocal );
			else if ( _potion.IsValid() && _potion.Enabled ) _potion.Enabled = false;
		}
		else
		{
			PlaceProp( _staff, RightGripJoints, GripFor( HandSide.Right ), out staffLocal );
			if ( _potion.IsValid() && _potion.Enabled ) _potion.Enabled = false;
		}
		StaffWorld = staffLocal is { } s ? now.ToWorld( s ) : null;
		PotionWorld = potionLocal is { } p ? now.ToWorld( p ) : null;
	}

	/// <summary>The bottle for the next drink (the potion 06/07 model): made on first use, swapped after.</summary>
	private void SetBottle( Model model )
	{
		if ( model is null || model.IsError ) return;
		_bottleModel = model;
		if ( !_potion.IsValid() )
		{
			if ( !_rig.IsValid() ) return;
			_potion = CreateProp( "fp_potion", model );
			if ( _potion.IsValid() ) _potion.Enabled = false;
		}
		// EverythingInSelf: between drinks the prop is disabled, and a plain Get skips a disabled object's components,
		// which left the first bottle drunk in the first-person hand for every drink after it
		else if ( _potion.Components.Get<ModelRenderer>( FindMode.EverythingInSelf ) is { } r && r.Model != model )
		{
			r.Model = model;
		}
		_potionFlagsApplied = false;
	}

	/// <summary>
	/// How far to move the whole rig this frame so the drinking hand stays in front of (and not above) the eye.
	/// Rig space is the view's: x forward, y left, z up. Zero unless the drink clip is playing.
	/// </summary>
	private Vector3 DrinkPush( Vector3 headLocal )
	{
		if ( _state != State.Drink || !_joints.TryGetValue( "hand_r", out var hand ) ) return Vector3.Zero;
		var off = hand - headLocal;
		var push = Vector3.Zero;
		if ( off.x < DrinkHandForward ) push.x = DrinkHandForward - off.x;
		if ( off.z > DrinkHandCeiling ) push.z = DrinkHandCeiling - off.z;
		return push.ClampLength( MathF.Max( 0f, DrinkPushMax ) );
	}

	private void RefreshPropModel( GameObject prop, Model model )
	{
		if ( !prop.IsValid() || model is null || model.IsError ) return;
		var renderer = prop.Components.Get<ModelRenderer>( FindMode.EverythingInSelf );
		if ( !renderer.IsValid() || renderer.Model == model ) return;
		renderer.Model = model;
		// the new scene object needs the view model layer again
		_flagsApplied = false;
	}

	private void PlaceProp( GameObject prop, string[] joints, GripSettings grip, out Transform? local )
	{
		local = null;
		if ( !prop.IsValid() ) return;

		if ( !_joints.TryGetValue( joints[0], out var hand ) || !_joints.TryGetValue( joints[1], out var index )
			|| !_joints.TryGetValue( joints[2], out var pinky ) || !_joints.TryGetValue( joints[3], out var middle )
			|| !_joints.TryGetValue( joints[4], out var thumb ) )
		{
			if ( prop.Enabled ) prop.Enabled = false;
			return;
		}

		var tx = SwordGrip.Compute( hand, index, pinky, middle, thumb, grip.Offset, grip.Rotation, grip.FlipPalm );
		prop.LocalTransform = grip.Seat( tx );
		if ( !prop.Enabled ) prop.Enabled = true;
		local = tx;
	}

	// ------------------------------------------------------------------ animation

	/// <summary>Freeze the arms briefly: the attacker's half of melee hitstop (the victim freezes too).</summary>
	public void Hitstop( float seconds )
	{
		_hitstopUntil = MathF.Max( _hitstopUntil, Time.Now + seconds );
	}

	private float _hitstopUntil;

	/// <summary>Hands the clip to the synced source: looping clips select the gait, the rest play once.</summary>
	private void Play( string clip )
	{
		if ( _current == clip ) return;
		_current = clip;
		_playedAt = Time.Now;

		if ( !_poseSource.IsValid() ) return;
		var gait = ClipPoseSource.GaitIndex( clip );
		if ( gait >= 0 ) _poseSource.SetGait( gait );
		else _poseSource.PlayOneShot( clip, OneShotRate( clip ) );
	}

	/// <summary>Swings play at the run's attack speed (Bladestorm, Brute's cost), the drink at its drink speed (Alchemist).</summary>
	private static float OneShotRate( string clip )
	{
		if ( clip == ClipDrink ) return PotionDrinker.DrinkRate;   // the glug clip's length, times Alchemist
		if ( clip.StartsWith( "anim_attack_", StringComparison.Ordinal ) ) return AttackSpeedScale;
		return 1f;
	}

	private static float AttackSpeedScale => VoidMage.Boons.PlayerBoons.LocalScale( VoidMage.Boons.BoonStat.AttackSpeed, 0.3f );

	// A clip is never "finished" on the frame it was started: the graph only advances on the next
	// animation update, and a stale finished flag from the previous one-shot would cut the new clip.
	private bool Finished
	{
		get
		{
			if ( Time.Now - _playedAt < 0.05f ) return false;
			return !_poseSource.IsValid() || _poseSource.IsFinished;
		}
	}

	private void UpdateState()
	{
		var speed = _controller.Velocity.WithZ( 0f ).Length;
		var grounded = _controller.IsOnGround;
		// no swing while the staff is busy (charging, a beam burning, or the throw's follow-through): Void Lance's
		// second-long cast is not a free window for the blade (user, 2026-09-24)
		var casting = VoidMage.Spells.SpellCaster.Local is { } caster && caster.Busy;
		var attack = Input.Pressed( "attack1" ) && Time.Now >= _nextAttackAt && _state != State.Drink && !casting && !VoidMage.UI.SpellLoadout.IsOpen && !VoidMage.Run.RunUi.Blocking && !VoidMage.UI.WeaponTuner.IsOpen && !VoidMage.UI.ShopkeeperChat.IsOpen && !VoidMage.Dev.DevPanel.Blocking;
		var heavy = Input.Down( "run" );

		// a jump during a swing or a drink leaves the arms to it (the body jumps regardless): cutting the attack clip
		// with the jump-in lost the heavy swing's hit window and left the slash ribbon without its profile
		if ( _jumpQueued ) { _jumpQueued = false; _airborne = true; if ( _state is not (State.Attack or State.Drink) ) { _state = State.JumpIn; Play( ClipJumpIn ); } }
		else if ( _airborne && grounded && _state is State.Jump or State.JumpIn ) { _airborne = false; _state = State.JumpOut; Play( ClipJumpOut ); }
		else if ( _airborne && grounded && _state is State.Attack or State.Drink ) _airborne = false;
		else if ( !grounded && !_airborne && _state is State.Locomotion or State.SprintIn or State.SprintOut ) { _airborne = true; _state = State.Jump; Play( ClipJump ); }

		// the combo never resets: a pause in the fight picks up at the next swing in the cycle, not the first (user)

		switch ( _state )
		{
			case State.Select:
				if ( _current != ClipSelect ) { Play( ClipSelect ); break; }
				if ( Finished ) _state = State.Locomotion;
				break;

			case State.Attack:
				// a potion press waiting on this swing goes first: mashing attack must not keep chaining over it
				if ( attack && !PotionDrinker.DrinkQueued ) { StartAttack( heavy ); break; }
				// a swing that ends in the air hands over to the jump loop, so the landing still plays its settle
				if ( Finished ) { if ( _airborne && !grounded ) { _state = State.Jump; Play( ClipJump ); } else _state = State.Locomotion; }
				break;

			case State.Drink:
				// no attack, no sprint: the player is rooted (PotionDrinker holds the controls); the clip runs out or is cancelled
				if ( Finished ) { _state = State.Locomotion; BottleVisible = false; }
				break;

			case State.Pickup:
				// a short gesture, not rooted: a swing cuts it, otherwise the gait comes back when it runs out
				if ( attack ) { StartAttack( heavy ); break; }
				if ( Finished ) _state = State.Locomotion;
				break;

			case State.JumpIn:
				if ( attack ) { StartAttack( heavy ); break; }
				if ( Finished ) { _state = State.Jump; Play( ClipJump ); }
				break;

			case State.Jump:
				if ( attack ) { StartAttack( heavy ); break; }
				break;

			case State.JumpOut:
				if ( attack ) { StartAttack( heavy ); break; }
				if ( Finished ) _state = State.Locomotion;
				break;

			case State.SprintIn:
				if ( attack ) { StartAttack( heavy ); break; }
				if ( speed < SprintThreshold ) { _state = State.SprintOut; Play( ClipSprintOut ); }
				else if ( Finished ) { _state = State.Locomotion; Play( ClipSprint ); }
				break;

			case State.SprintOut:
				if ( attack ) { StartAttack( heavy ); break; }
				if ( speed >= SprintThreshold ) { _state = State.SprintIn; Play( ClipSprintIn ); }
				else if ( Finished ) _state = State.Locomotion;
				break;

			case State.Locomotion:
				if ( attack ) { StartAttack( heavy ); break; }

				var sprinting = speed >= SprintThreshold;
				if ( sprinting && !_sprinting ) { _sprinting = true; _state = State.SprintIn; Play( ClipSprintIn ); break; }
				if ( !sprinting && _sprinting ) { _sprinting = false; _state = State.SprintOut; Play( ClipSprintOut ); break; }

				if ( sprinting ) Play( ClipSprint );
				else if ( speed >= RunThreshold ) Play( ClipRun );
				else if ( speed >= MovingThreshold ) Play( ClipWalk );
				else Play( ClipIdle );
				break;
		}
	}

	private void StartAttack( bool heavy )
	{
		var set = heavy ? HeavyCombo : LightCombo;
		var clip = set[_combo % set.Length];
		_combo++;
		_nextAttackAt = Time.Now + (heavy ? HeavyAttackCooldown : LightAttackCooldown) / AttackSpeedScale;
		_state = State.Attack;
		_current = "";
		Play( clip );
	}

	/// <summary>
	/// Start the drink clip with `bottle` in the right hand. Only on the ground and never inside an attack cooldown,
	/// so the clip is never cut by the state machine's own transitions. PotionDrinker buffers the press and retries.
	/// </summary>
	public bool TryStartDrink( Model bottle )
	{
		// a pickup reach is only a gesture: drinking the potion just picked up cuts straight into the drink. A swing past
		// its cooldown (checked below) has landed its hit and may be chained into another swing, so the drink may cut
		// its recovery the same way; a landing is only a settle
		if ( IsProxy || _state is not (State.Locomotion or State.SprintIn or State.SprintOut or State.Pickup or State.Attack or State.JumpOut) ) return false;
		if ( !_controller.IsValid() || !_controller.IsOnGround ) return false;
		if ( Time.Now < _nextAttackAt || !_poseSource.IsValid() ) return false;
		if ( _health.IsValid() && _health.IsDead ) return false;
		SetBottle( bottle );
		BottleVisible = true;
		_state = State.Drink;
		_current = "";
		Play( ClipDrink );
		return true;
	}

	/// <summary>
	/// The staff hand's little reach when something is picked up (E). Only from a stand, walk or run, like the drink;
	/// the staff stays in the hand, and a swing or a jump simply takes over.
	/// </summary>
	public bool TryStartPickup()
	{
		if ( IsProxy || _state is not (State.Locomotion or State.SprintIn or State.SprintOut) ) return false;
		if ( !_poseSource.IsValid() || (_health.IsValid() && _health.IsDead) ) return false;
		_state = State.Pickup;
		_current = "";
		Play( ClipPickup );
		return true;
	}

	/// <summary>Stop the drink (a hit, a run card): the one-shot is dropped and the gait shows again next frame.</summary>
	public void CancelDrink()
	{
		if ( _state != State.Drink ) return;
		BottleVisible = false;
		if ( _poseSource.IsValid() ) _poseSource.Stop();
		_state = State.Locomotion;
	}

	/// <summary>The player's jump event, so the arms start the jump clip on the same frame.</summary>
	void PlayerController.IEvents.OnJumped()
	{
		if ( !IsProxy ) _jumpQueued = true;
	}
}
