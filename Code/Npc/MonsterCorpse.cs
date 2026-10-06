using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Npc;

/// <summary>
/// Drives the bones of a MONSTER corpse that own no physics body, so they ride the limb they belong to
/// instead of freezing to the corpse's root.
///
/// WHY THIS EXISTS. ModelPhysics.PositionRendererBonesFromPhysics calls SetBoneOverride for the bones that own
/// a body and for no others, and never clears an override; SkeletonCorpse.SeedPose stamps EVERY bone once at
/// death. So a body-less bone keeps its death pose relative to the corpse GameObject forever -- and that
/// object is itself re-driven from the root body every tick. It does not follow its parent.
///
/// A skeleton survives that because Synty weights its whole hand to `hand_l`: the finger, twist and socket
/// bones carry almost no skin, so freezing them is invisible. These packs are the opposite - half the rat's
/// vertices sit on bones a 16-body budget cannot reach - so the frozen bones were the corpse tearing apart.
///
/// The fix is to do for the animals what the rig already does for a skeleton's fingers: every frame, after
/// ModelPhysics has written its bodies, walk the bones parents-first and place each unowned one at its
/// parent's transform composed with its own BIND-POSE offset. It comes out rigid, which is exactly right for
/// a leg tip, and it is what lets one body cover a whole three-bone spider leg.
///
/// THIS IS MONSTER-ONLY, BY CONSTRUCTION. SkeletonCorpse adds it only when the dying body passed a weld map,
/// which only the generated monster prefabs do. A skeleton corpse never has this component, so it cannot run
/// a line of this file.
///
/// The bug that broke the skeletons when this logic briefly lived in SkeletonCorpse: it cached the corpse's
/// WorldTransform once at spawn. ModelPhysics rewrites that transform from the root body every tick, so every
/// bone got stamped in a stale space and smeared away. It is re-read every frame here, and never cached.
/// </summary>
public sealed class MonsterCorpse : Component
{
	[ConVar( "monster_corpse_debug" )] public static bool DebugLogging { get; set; } = false;

	/// <summary>
	/// A bone name the way the ENGINE spells it. Blender names mirrored bones `FrontLeg.L`; s&amp;box's
	/// compiled skeleton writes `FrontLeg_L`, and every lookup that misses is silent.
	/// </summary>
	private static string Spell( string bone ) => bone?.Replace( '.', '_' );

	private SkinnedModelRenderer _renderer;
	private ModelPhysics _physics;

	/// <summary>Bones with a body, in the renderer's index space: read from physics, never written here.</summary>
	private readonly HashSet<int> _driven = new();
	/// <summary>Every body-less bone, ordered so a bone's driver is always placed before it.</summary>
	private (BoneCollection.Bone Bone, int Driver, Transform Offset)[] _followers = Array.Empty<(BoneCollection.Bone, int, Transform)>();
	private Transform[] _world;

	/// <summary>
	/// Set up from the corpse's own renderer and physics.
	/// <paramref name="welds"/> is "bone:target" for the bones the rig parents to the ARMATURE ROOT -- the
	/// rat's and spider's feet, which are IK effectors in these packs. Following their parent would ride the
	/// torso, so the generator names the limb bone they sit on instead (tools/gen_monster_ragdoll.py).
	/// </summary>
	public void Bind( SkinnedModelRenderer renderer, ModelPhysics physics, string[] welds )
	{
		_renderer = renderer;
		_physics = physics;
		var bones = renderer?.Model?.Bones?.AllBones;
		if ( bones is null ) return;

		foreach ( var body in physics.Bodies )
			if ( body.Component.IsValid() ) _driven.Add( body.Bone );

		// THE CHECK THAT WOULD HAVE SAVED HOURS. ModelPhysics.CreateParts calls bones.GetBone(part.BoneName)
		// and `continue`s when it resolves to nothing, with no warning at all - and s&box spells Blender's
		// `FrontLeg.L` as `FrontLeg_L`, so every mirrored bone's body was silently missing. The spider ran its
		// whole corpse on 4 of 16, the rat on 9 of 16. Parts vs bodies is the number that shows it.
		var parts = renderer.Model.Physics?.Parts?.Count ?? 0;
		if ( parts != physics.Bodies.Count )
			Log.Warning( $"[MonsterCorpse] {renderer.Model.Name}: the model carries {parts} physics parts but the engine " +
				$"built only {physics.Bodies.Count} bodies - the rest name bones this model does not have " +
				$"(s&box writes `A.L` as `A_L`), and those limbs will be dead." );

		var overrides = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		if ( welds is not null )
		{
			foreach ( var entry in welds )
			{
				var split = entry?.IndexOf( ':' ) ?? -1;
				// sanitised the same way, so a prefab generated before that was understood cannot half-work
				if ( split > 0 ) overrides[Spell( entry[..split] )] = Spell( entry[(split + 1)..] );
			}
		}

		// Which bone each follower rides: its parent, or the weld target when the rig parented it to the root.
		var driverOf = new Dictionary<int, BoneCollection.Bone>();
		foreach ( var bone in bones )
		{
			if ( bone is null || _driven.Contains( bone.Index ) ) continue;
			var driver = bone.Parent;
			if ( overrides.TryGetValue( Spell( bone.Name ), out var target ) )
			{
				var named = renderer.Model.Bones.GetBone( target );
				if ( named is null )
					Log.Warning( $"[MonsterCorpse] {renderer.Model.Name}: weld target {target} is not a bone of this model" );
				else driver = named;
			}
			if ( driver is not null ) driverOf[bone.Index] = driver;
		}

		// Order so a follower is always placed after whatever drives it: a spider's foot welds to Leg3, which
		// itself has no body and rides Leg2. Placed out of order it would lag its own chain by a frame.
		var placed = new HashSet<int>( _driven );
		var ordered = new List<(BoneCollection.Bone, int, Transform)>();
		var pending = new List<BoneCollection.Bone>();
		foreach ( var bone in bones )
			if ( bone is not null && driverOf.ContainsKey( bone.Index ) ) pending.Add( bone );

		var guard = pending.Count + 1;
		while ( pending.Count > 0 && guard-- > 0 )
		{
			var progressed = false;
			for ( var i = pending.Count - 1; i >= 0; i-- )
			{
				var bone = pending[i];
				var driver = driverOf[bone.Index];
				if ( !placed.Contains( driver.Index ) ) continue;
				// bind-pose offset from the driver, constant for the life of the corpse
				var offset = driver.LocalTransform.ToLocal( bone.LocalTransform );
				ordered.Add( (bone, driver.Index, offset) );
				placed.Add( bone.Index );
				pending.RemoveAt( i );
				progressed = true;
			}
			if ( !progressed ) break;   // a bone whose driver is itself unreachable: leave it seeded
		}

		_followers = ordered.ToArray();
		// Seeded with each bone's BIND transform rather than left at default(Transform), which is a zero scale
		// and an all-zero (invalid) quaternion: one of those reaching SetBoneTransform is a NaN bone.
		_world = new Transform[bones.Count];
		foreach ( var bone in bones )
			if ( bone is not null && bone.Index >= 0 && bone.Index < _world.Length ) _world[bone.Index] = bone.LocalTransform;

		if ( DebugLogging )
			Log.Info( $"[MonsterCorpse] {renderer.Model.Name}: {parts} parts, {_driven.Count} bodies, {_followers.Length} welded" +
				(pending.Count > 0 ? $", UNREACHABLE: {string.Join( ", ", pending.ConvertAll( b => b.Name ) )}" : "") );
	}

	protected override void OnPreRender()
	{
		// OnPreRender runs after every OnUpdate, so ModelPhysics has already written this frame's bodies.
		if ( _followers.Length == 0 || !_renderer.IsValid() || !_physics.IsValid() ) return;

		// RE-READ, NEVER CACHE: ModelPhysics re-drives this transform from the root body every tick, and
		// caching it at spawn is precisely what smeared the bones the last time this was tried.
		var toLocal = _renderer.WorldTransform;

		foreach ( var body in _physics.Bodies )
		{
			var rb = body.Component;
			if ( rb.IsValid() && body.Bone >= 0 && body.Bone < _world.Length ) _world[body.Bone] = rb.WorldTransform;
		}

		foreach ( var (bone, driver, offset) in _followers )
		{
			var world = _world[driver].ToWorld( offset );
			_world[bone.Index] = world;
			_renderer.SetBoneTransform( bone, toLocal.ToLocal( world ) );
		}
	}
}
