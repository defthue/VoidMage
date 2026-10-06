using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Npc;

/// <summary>
/// A slain skeleton's body, ragdolled for a few seconds. heden's NpcCorpse, ported.
///
/// Client-local by necessity, not by preference: ModelPhysics.CreatePhysics() returns early on a proxy, so a
/// client physically cannot ragdoll the host-owned skeleton in place. The corpse is therefore a separate,
/// never-networked GameObject that each peer builds for itself from its own copy of the synced Dead state (and the
/// synced death push and contact point, so every peer launches it the same way). Zero bandwidth; the corpses
/// differing slightly between clients is invisible.
///
/// The model carries only capsules (tools/gen_skeleton_ragdoll.py); the joints are built here from the bone
/// hierarchy, with frames aligned to each bone's own axis (heden: AttachmentMode.Auto folded Blender-exported
/// rigs "like crumpled paper").
/// </summary>
public sealed class SkeletonCorpse : Component
{
	/// <summary>Hard ceiling on concurrent ragdolls; the oldest is retired to make room.</summary>
	[ConVar( "skeleton_corpse_max" )] public static int MaxCorpses { get; set; } = 12;
	/// <summary>Past this from the camera a death skips the ragdoll (the body just disappears).</summary>
	[ConVar( "skeleton_corpse_distance" )] public static float MaxDistance { get; set; } = 2500f;
	[ConVar( "skeleton_corpse_seconds" )] public static float LifeSeconds { get; set; } = 8f;
	[ConVar( "skeleton_corpse_debug" )] public static bool DebugLogging { get; set; } = false;
	/// <summary>Impulse per unit of ragdoll mass: the killing blow's speed. Half the fun is the body visibly flying.</summary>
	[ConVar( "skeleton_corpse_speed" )] public static float ImpulseSpeed { get; set; } = 200f;
	/// <summary>Speed below which a body falls asleep (only matters with AllowSleep on).</summary>
	[ConVar( "skeleton_corpse_sleep" )] public static float BodySleepThreshold { get; set; } = 1f;
	/// <summary>Off: the switch that keeps a landed corpse limp (a sleeping ragdoll locks solid the instant it lands).</summary>
	[ConVar( "skeleton_corpse_autosleep" )] public static bool AllowSleep { get; set; } = false;
	[ConVar( "skeleton_corpse_damping" )] public static float BodyAngularDamping { get; set; } = 0.15f;
	/// <summary>Scales every joint's friction; lower is floppier.</summary>
	[ConVar( "skeleton_corpse_friction" )] public static float JointFrictionScale { get; set; } = 1f;
	/// <summary>How hard walking into a corpse kicks it, as a fraction of the player's own speed. 0 disables.</summary>
	[ConVar( "skeleton_corpse_shove" )] public static float ShoveStrength { get; set; } = 0.7f;

	/// <summary>Used only when a model reports zero total mass. Roughly a skeleton.</summary>
	private const float FallbackMass = 25f;
	private const float ShoveReachPadding = 12f;
	/// <summary>Collision tag the vmdl shapes carry; stamped on the bone GameObjects too so the collision matrix applies.</summary>
	public const string RagdollTag = "ragdoll";

	private static readonly List<SkeletonCorpse> _live = new();
	private static bool _warnedNoPhysics;
	private static bool _warnedNoJoints;

	private TimeUntil _expire;

	/// <summary>The corpse's own renderer (the death camera reads its head bone).</summary>
	public SkinnedModelRenderer Renderer { get; private set; }
	public ModelPhysics Physics { get; private set; }
	/// <summary>A pinned corpse (the player's, with a camera riding it) is never evicted by the cap; it still expires.</summary>
	public bool Pinned { get; private set; }
	/// <summary>Copied from the dying body's SkeletonBody.Bleeds: a sword swing on this ragdoll throws blood, not bone sparks.</summary>
	public bool Bleeds { get; private set; }

	/// <summary>
	/// Whether the ragdoll a swing met is flesh. The sweep returns one of the bone GameObjects ModelPhysics created;
	/// they hang under the corpse root, and if ever they do not, the live list still knows which corpse owns the body.
	/// </summary>
	public static bool BleedsAt( GameObject struck )
	{
		if ( !struck.IsValid() ) return false;
		var corpse = struck.Components.Get<SkeletonCorpse>( FindMode.EverythingInSelfAndAncestors );
		if ( corpse.IsValid() ) return corpse.Bleeds;
		foreach ( var c in _live )
		{
			if ( !c.IsValid() || !c.Bleeds || !c.Physics.IsValid() ) continue;
			foreach ( var body in c.Physics.Bodies )
				if ( body.Component.IsValid() && body.Component.GameObject == struck ) return true;
		}
		return false;
	}


	/// <summary>
	/// Ragdoll a dead skeleton from its live renderer. True when the caller should hide that renderer.
	/// <paramref name="welds"/> is monster-only and defaults to null, which is exactly today's behaviour: a
	/// skeleton passes nothing, gets no MonsterCorpse and runs this file unchanged. See MonsterCorpse.
	/// </summary>
	public static bool Spawn( SkinnedModelRenderer source, Vector3 impulseDir, Vector3 impulseOrigin, string[] welds = null )
	{
		SpawnCorpse( source, impulseDir, impulseOrigin, welds, null, "skeleton_corpse", false, out var hideSource );
		return hideSource;
	}

	/// <summary>
	/// The player's variant: returns the corpse (null when none was built: dedicated server, too far, no physics),
	/// recreates <paramref name="merged"/> (hair, eyes, brows, clothing: the renderers bone-merged onto the source)
	/// as bone-merged children of the corpse so the body dies dressed, and pins it against the cap.
	/// </summary>
	public static SkeletonCorpse SpawnCorpse( SkinnedModelRenderer source, Vector3 impulseDir, Vector3 impulseOrigin, string[] welds,
		IEnumerable<SkinnedModelRenderer> merged, string name, bool pinned )
		=> SpawnCorpse( source, impulseDir, impulseOrigin, welds, merged, name, pinned, out _ );

	private static SkeletonCorpse SpawnCorpse( SkinnedModelRenderer source, Vector3 impulseDir, Vector3 impulseOrigin, string[] welds,
		IEnumerable<SkinnedModelRenderer> merged, string name, bool pinned, out bool hideSource )
	{
		hideSource = false;
		if ( Application.IsDedicatedServer ) return null;
		if ( !source.IsValid() || source.Model is null ) return null;
		var scene = source.Scene;
		if ( scene is null ) return null;

		hideSource = true;
		var center = source.Bounds.Center;
		var camera = scene.Camera;
		if ( camera.IsValid() && camera.WorldPosition.Distance( center ) > MaxDistance ) return null;

		// CreatePhysics() bails SILENTLY on a model with no physics parts, which would leave a statue at the death spot
		var physics = source.Model.Physics;
		if ( physics is null || physics.Parts.Count == 0 )
		{
			if ( !_warnedNoPhysics )
			{
				_warnedNoPhysics = true;
				Log.Warning( $"[SkeletonCorpse] {source.Model.Name} has no physics parts: no ragdoll. Run tools/gen_skeleton_ragdoll.py and regenerate the vmdl." );
			}
			return null;
		}

		// Drop the entry here rather than leaving it to OnDestroy: GameObject.Destroy() only QUEUES
		// the delete, so _live.Count would not move until next frame and this loop would spin the
		// main thread forever on the corpse that takes us to the cap (the second Retire on the same
		// object early-outs on its _destroying flag). That was a hard editor freeze on the 12th body.
		// The oldest UNPINNED goes; when only pinned ones are left the cap slides (a handful of players at most).
		while ( _live.Count >= MaxCorpses && _live.Count > 0 )
		{
			var index = _live.FindIndex( c => !c.Pinned );
			if ( index < 0 ) break;
			var oldest = _live[index];
			_live.RemoveAt( index );
			oldest.Retire();
		}

		return Build( source, impulseDir, impulseOrigin, welds, merged, name, pinned );
	}

	private static SkeletonCorpse Build( SkinnedModelRenderer source, Vector3 impulseDir, Vector3 impulseOrigin, string[] welds,
		IEnumerable<SkinnedModelRenderer> merged, string name, bool pinned )
	{
		var model = source.Model;

		// Created DISABLED: ModelPhysics builds its bodies in OnEnabled, and the renderer assignment and the pose copy
		// have to happen before that runs.
		var go = new GameObject( false, name ) { WorldTransform = source.WorldTransform };
		go.Tags.Add( RagdollTag );

		var renderer = go.Components.Create<SkinnedModelRenderer>();
		renderer.Model = model;
		renderer.UseAnimGraph = false;
		renderer.Tint = source.Tint;
		renderer.MaterialGroup = source.MaterialGroup;
		renderer.BodyGroups = source.BodyGroups;

		SeedPose( source, renderer, model );

		var mp = go.Components.Create<ModelPhysics>();
		mp.Model = model;
		// Must be set before enable: CreateParts prefers Renderer.TryGetBoneTransform and silently falls back to the
		// vmdl BIND POSE if the renderer isn't there yet.
		mp.Renderer = renderer;
		mp.RigidbodyFlags = RigidbodyFlags.DisableCollisionSounds;
		// per-bone linear AND angular velocity: a skeleton killed mid-swing keeps the swing
		mp.CopyBonesFrom( source, true );

		go.Enabled = true;

		var jointCount = mp.Joints.Count;
		if ( jointCount == 0 ) jointCount = BuildJoints( mp, model );

		var corpse = go.Components.Create<SkeletonCorpse>();
		corpse._expire = LifeSeconds;
		corpse.Renderer = renderer;
		corpse.Physics = mp;
		corpse.Pinned = pinned;
		// the body's own flag (the renderer sits on the "visual" child, SkeletonBody on the root); the player has none
		corpse.Bleeds = source.Components.Get<SkeletonBody>( FindMode.EverythingInSelfAndAncestors )?.Bleeds ?? false;

		// MONSTERS ONLY. A skeleton passes no welds and never gets this component; its 30 body-less bones are
		// fingers and sockets carrying almost no skin, so leaving them seeded is invisible. See MonsterCorpse.
		if ( welds is not null )
			go.Components.Create<MonsterCorpse>().Bind( renderer, mp, welds );

		// The player's dressing: each piece is bone-merged onto the corpse renderer the way it was onto the body.
		// The engine bakes the physics bone overrides into the target's SceneModel and then merges the FINAL bones
		// into every merged child, so the pieces ride the ragdoll. Never MaterialOverride: the hit flash stays behind.
		if ( merged is not null )
		{
			foreach ( var piece in merged )
			{
				if ( !piece.IsValid() || piece.Model is null ) continue;
				var child = new GameObject( true, piece.GameObject.Name ) { Parent = go };
				var r = child.Components.Create<SkinnedModelRenderer>();
				r.Model = piece.Model;
				r.UseAnimGraph = false;
				r.Tint = piece.Tint;
				r.MaterialGroup = piece.MaterialGroup;
				r.BodyGroups = piece.BodyGroups;
				r.RenderType = ModelRenderer.ShadowRenderType.On;
				r.BoneMergeTarget = renderer;
			}
		}

		Settle( mp );
		Launch( mp, renderer, impulseDir, impulseOrigin );
		_live.Add( corpse );

		if ( !_warnedNoJoints && mp.Bodies.Count > 1 && jointCount == 0 )
		{
			_warnedNoJoints = true;
			Log.Warning( $"[SkeletonCorpse] {model.Name} has {mp.Bodies.Count} physics bodies but no joints: the corpse will come apart." );
		}
		if ( DebugLogging )
			Log.Info( $"[SkeletonCorpse] spawned {name} ({_live.Count}/{MaxCorpses}) bodies={mp.Bodies.Count} joints={jointCount} mass={mp.Mass:0.#} push {impulseDir:0.00} at {impulseOrigin:0}" );
		return corpse;
	}

	/// <summary>
	/// Copy every bone's pose across, not just the ones physics will drive: ModelPhysics only overrides bones that own
	/// a body (16 of 46); fingers, the neck, the weapon sockets would otherwise draw at bind pose.
	/// </summary>
	private static void SeedPose( SkinnedModelRenderer source, SkinnedModelRenderer target, Model model )
	{
		var bones = model.Bones?.AllBones;
		if ( bones is null ) return;
		var pose = source.GetBoneTransforms( world: true );
		if ( pose is null ) return;
		// SetBoneTransform passes straight through to SceneModel.SetBoneOverride: a transform LOCAL to the scene object
		var toLocal = target.WorldTransform;
		foreach ( var bone in bones )
		{
			if ( bone is null || bone.Index < 0 || bone.Index >= pose.Length ) continue;
			target.SetBoneTransform( bone, toLocal.ToLocal( pose[bone.Index] ) );
		}
	}

	/// <summary>How floppy each joint is, matched by bone-name prefix, first match wins (heden's numbers, Epic names).
	/// Swing is the half-angle of the cone the child may leave its rest direction by; twist is rotation about its length.</summary>
	private static readonly (string Prefix, float Swing, float Twist, float Friction)[] JointLimits =
	{
		( "spine", 40f, 25f, 0.4f ),
		( "neck", 45f, 25f, 0.2f ),
		( "head", 45f, 30f, 0.2f ),
		( "clavicle", 20f, 15f, 0.5f ),
		( "upperarm", 85f, 35f, 0.1f ),
		// elbows and knees are hinges, approximated as tight cones (a tumbling body never shows the difference)
		( "lowerarm", 90f, 25f, 0.15f ),
		( "calf", 90f, 20f, 0.15f ),
		( "hand", 70f, 40f, 0.3f ),
		( "thigh", 85f, 30f, 0.3f ),
		( "foot", 50f, 25f, 0.1f ),
		( "", 60f, 30f, 0.2f ),
	};

	/// <summary>
	/// Connect the physics bodies to each other: each body to its nearest ancestor that also owns a body, hosted on
	/// the CHILD so the joint anchors at the child bone's origin, frames aligned to the bone's own axis.
	/// </summary>
	private static int BuildJoints( ModelPhysics mp, Model model )
	{
		var bones = model.Bones?.AllBones;
		if ( bones is null ) return 0;

		var bodyByBone = new Dictionary<int, Rigidbody>();
		foreach ( var body in mp.Bodies )
			if ( body.Component.IsValid() ) bodyByBone[body.Bone] = body.Component;

		var built = 0;
		foreach ( var body in mp.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;
			if ( body.Bone < 0 || body.Bone >= bones.Count ) continue;

			var ancestor = bones[body.Bone].Parent;
			while ( ancestor is not null && !bodyByBone.ContainsKey( ancestor.Index ) ) ancestor = ancestor.Parent;
			if ( ancestor is null ) continue;
			if ( !bodyByBone.TryGetValue( ancestor.Index, out var parentBody ) ) continue;

			var (_, swing, twist, friction) = LimitsFor( bones[body.Bone].Name );

			var joint = rb.GameObject.Components.Create<BallJoint>();
			joint.Body = parentBody.GameObject;
			joint.EnableCollision = false;

			// A ball-socket's twist axis is its frame's X and the swing limit is the cone around it; aligning X with the
			// bone means the cone opens along the limb, which is what the limits above mean.
			var frame = new Transform( Vector3.Zero, Rotation.LookAt( BoneForwardLocal( bones, body.Bone ) ) );
			joint.Attachment = Joint.AttachmentMode.LocalFrames;
			joint.LocalFrame1 = frame;
			joint.LocalFrame2 = parentBody.WorldTransform.ToLocal( rb.WorldTransform.ToWorld( frame ) );
			joint.SwingLimitEnabled = true;
			joint.SwingLimit = new Vector2( 0f, swing );
			joint.TwistLimitEnabled = true;
			joint.TwistLimit = new Vector2( -twist, twist );
			joint.Friction = friction * JointFrictionScale;
			built++;
		}

		if ( DebugLogging && mp.Bodies.Count > 0 )
		{
			// expect roughly (0,1,0) on this Blender-exported rig; a wrong axis folds the corpse in on itself
			var sample = BoneForwardLocal( bones, mp.Bodies[0].Bone );
			Log.Info( $"[SkeletonCorpse] built {built} runtime joints for {model.Name}; bone-forward of {bones[mp.Bodies[0].Bone].Name} = {sample}" );
		}
		return built;
	}

	/// <summary>Which way a bone points, in its own local space: derived from the skeleton, never assumed.</summary>
	private static Vector3 BoneForwardLocal( IReadOnlyList<BoneCollection.Bone> bones, int index )
	{
		var bone = bones[index];
		var here = bone.LocalTransform;
		var child = bone.Children is { Count: > 0 } ? bone.Children[0] : null;
		var target = child?.LocalTransform.Position ?? bone.Parent?.LocalTransform.Position;
		if ( target is null ) return Vector3.Forward;
		// terminal bones continue the direction their parent arrived from instead of reversing into it
		var dir = child is not null ? target.Value - here.Position : here.Position - target.Value;
		if ( dir.IsNearZeroLength ) return Vector3.Forward;
		return here.Rotation.Inverse * dir.Normal;
	}

	private static (string, float, float, float) LimitsFor( string boneName )
	{
		foreach ( var entry in JointLimits )
			if ( entry.Prefix.Length == 0 || boneName.StartsWith( entry.Prefix, StringComparison.OrdinalIgnoreCase ) ) return entry;
		return JointLimits[^1];
	}

	/// <summary>Give the bodies real masses if the model did not (a weightless ragdoll is stiff: friction beats gravity).</summary>
	private static void EnsureMass( ModelPhysics mp )
	{
		var total = 0f;
		foreach ( var body in mp.Bodies )
			if ( body.Component.IsValid() ) total += body.Component.Mass;
		if ( total >= 1f ) return;

		var volumes = new List<float>( mp.Bodies.Count );
		var volumeSum = 0f;
		foreach ( var body in mp.Bodies )
		{
			var size = body.Component.IsValid() ? body.Component.GetWorldBounds().Size : Vector3.Zero;
			var volume = MathF.Max( size.x * size.y * size.z, 1f );
			volumes.Add( volume );
			volumeSum += volume;
		}
		if ( volumeSum <= 0f ) return;
		for ( var i = 0; i < mp.Bodies.Count; i++ )
		{
			var rb = mp.Bodies[i].Component;
			if ( !rb.IsValid() ) continue;
			rb.MassOverride = MathF.Max( FallbackMass * (volumes[i] / volumeSum), 0.5f );
		}
		if ( DebugLogging ) Log.Info( $"[SkeletonCorpse] model reported no mass; distributed {FallbackMass} across {mp.Bodies.Count} bodies" );
	}

	private static void Settle( ModelPhysics mp )
	{
		EnsureMass( mp );
		foreach ( var body in mp.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;
			rb.SleepThreshold = BodySleepThreshold;
			rb.AngularDamping = BodyAngularDamping;
			if ( rb.PhysicsBody.IsValid() ) rb.PhysicsBody.AutoSleep = AllowSleep;
			// the bone GameObjects s&box creates do not inherit the corpse root's tags
			rb.GameObject.Tags.Add( RagdollTag );
		}
	}

	/// <summary>The killing blow: one impulse at the bone nearest the contact, along the blade's travel.</summary>
	private static void Launch( ModelPhysics mp, SkinnedModelRenderer renderer, Vector3 dir, Vector3 origin )
	{
		if ( dir.IsNearZeroLength ) return;
		var mass = mp.Mass;
		if ( mass <= 0f ) mass = FallbackMass;
		var force = mass * ImpulseSpeed;

		// the bone nearest the blow rather than the whole ragdoll: a single hit spins it instead of translating it
		var hitBone = renderer.GetClosestBone( origin );
		if ( hitBone is not null )
		{
			foreach ( var body in mp.Bodies )
			{
				if ( body.Bone != hitBone.Index || !body.Component.IsValid() ) continue;
				body.Component.ApplyImpulseAt( origin, dir * force );
				return;
			}
		}
		// the closest bone may own no body (a finger, a socket): the heaviest one, the torso
		Rigidbody heaviest = null;
		foreach ( var body in mp.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;
			if ( heaviest is null || rb.Mass > heaviest.Mass ) heaviest = rb;
		}
		heaviest?.ApplyImpulseAt( origin, dir * force );
	}

	/// <summary>A sword swing on the corpse (SwordSounds): shove the struck body along the blade.</summary>
	public static void Bat( GameObject struck, Vector3 at, Vector3 blade, bool heavy )
	{
		var rb = struck.IsValid() ? struck.Components.Get<Rigidbody>() : null;
		if ( !rb.IsValid() ) return;
		rb.ApplyImpulseAt( at, blade * MathF.Max( rb.Mass, 0.5f ) * ImpulseSpeed * (heavy ? 1.5f : 0.8f) );
	}

	protected override void OnUpdate()
	{
		if ( _expire ) Retire();
	}

	protected override void OnFixedUpdate()
	{
		ShoveFromPlayer();
	}

	/// <summary>
	/// Let the player kick corpses around without corpses affecting the player: the collision matrix makes ragdolls
	/// ignore the player, so the push is re-applied here by velocity matching (a body already moving away is left
	/// alone, so standing in a corpse cannot pump energy into it).
	/// </summary>
	private void ShoveFromPlayer()
	{
		if ( ShoveStrength <= 0f ) return;
		var player = PlayerHealth.Local;
		if ( !player.IsValid() ) return;
		var controller = player.Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
		if ( !controller.IsValid() ) return;

		var velocity = controller.Velocity.WithZ( 0f );
		var speed = velocity.Length;
		if ( speed < 8f ) return;

		var mp = Components.Get<ModelPhysics>();
		if ( !mp.IsValid() ) return;

		var feet = controller.WorldPosition;
		var reach = controller.BodyRadius + ShoveReachPadding;
		foreach ( var body in mp.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;
			var offset = rb.WorldPosition - feet;
			if ( offset.z < -ShoveReachPadding || offset.z > controller.BodyHeight ) continue;
			var flat = offset.WithZ( 0f );
			if ( flat.Length > reach ) continue;
			var dir = flat.IsNearZeroLength ? velocity.Normal : flat.Normal;
			dir = (dir + Vector3.Up * 0.15f).Normal;
			var desired = speed * ShoveStrength;
			var current = rb.Velocity.Dot( dir );
			if ( current >= desired ) continue;
			rb.ApplyImpulse( dir * (desired - current) * MathF.Max( rb.Mass, 0.5f ) );
		}
	}

	/// <summary>The single exit, for the lifetime timer and cap eviction alike.</summary>
	private void Retire()
	{
		VoidMage.Dev.WorldTape.Retire( GameObject, () => _live.Remove( this ) );
	}

	protected override void OnDestroy()
	{
		_live.Remove( this );
	}
}
