using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Traps;

/// <summary>
/// The spiky vines grown across a breached wall. Not a <see cref="TrapBase"/> — there is no trigger, no
/// wind-up and no cycle, it simply hurts whatever is standing in it — but it judges hits the same way the
/// traps do, which is the part that matters: the local player's own client applies damage to that player
/// (owner-authoritative health, so nobody is hurt by a body they only see as a proxy) and the host applies
/// damage to NPCs. Nothing is networked; every peer built the same breach from the same seed.
///
/// The damage kind is "trap_vine" so <see cref="HitFeel.IsTrap"/> claims it: environmental damage that is
/// not scaled by the enemy difficulty knobs and that a skeleton's raised guard cannot block. No sound of its
/// own either — <see cref="PlayerHealth.TakeHit"/> already plays the hit.
///
/// A per-body cooldown makes one crossing cost one prick; standing in the vines keeps costing.
/// </summary>
[Title( "Vine Curtain" ), Category( "Void Mage" ), Icon( "grass" )]
public sealed class VineCurtain : Component
{
	[ConVar( "vine_damage" )] public static float Damage { get; set; } = 5f;
	/// <summary>Seconds before the same body can be pricked again.</summary>
	[ConVar( "vine_interval" )] public static float Interval { get; set; } = 0.6f;
	/// <summary>Shove out of the vines, units/s. 0 by default: a scratch should not fight your movement.</summary>
	[ConVar( "vine_push" )] public static float Push { get; set; } = 0f;
	/// <summary>Whether skeletons and monsters bleed on the vines too. They path through breaches, so this is
	/// the knob to reach for if they start grinding themselves down on one.</summary>
	[ConVar( "vine_hurts_npcs" )] public static bool HurtsNpcs { get; set; } = true;

	public const string DamageKind = "trap_vine";

	public int Index { get; internal set; } = -1;

	/// <summary>
	/// The volume the spikes fill, in the component's OWN frame — not in world space. That is what makes the
	/// box tight enough to be fair: a vine trailing 3 m diagonally across a room has a world-aligned bounding
	/// box half of which is nowhere near the plant, and you would be pricked walking past it. Turned with the
	/// vine, the same box is the vine.
	///
	/// Scale is NOT part of the transform used to test it (see <see cref="ToLocal"/>), so a volume built from a
	/// model's bounds has to be scaled to match the renderer before it is set here.
	/// </summary>
	public BBox Hurt { get; set; }

	private readonly Dictionary<Guid, float> _nextHit = new();

	public Vector3 Centre => WorldTransform.PointToWorld( Hurt.Center );
	/// <summary>Where to draw it: the box is local, so it needs the object's place and facing to be seen.</summary>
	public Transform Frame => new( WorldPosition, WorldRotation );

	/// <summary>
	/// A world point in the vine's frame. Rotation and position only, deliberately: the prop's GameObject
	/// carries the size roll in its LocalScale, and dividing by that here would shrink the box back to the
	/// model's unscaled bounds.
	/// </summary>
	private Vector3 ToLocal( Vector3 world ) => WorldRotation.Inverse * (world - WorldPosition);

	/// <summary>
	/// One frame, from <see cref="VineSystem"/>. `targets` is the shared body scan.
	///
	/// The body is tested in the vine's own frame. Its HEIGHT is not rotated with it, which is correct only
	/// because every vine is placed with a yaw and no pitch — local Z is still world up — and that is the one
	/// thing to remember if a vine is ever tilted the way the root mats are.
	/// </summary>
	internal void Step( float now, TrapSystem.Targets targets )
	{
		foreach ( var p in targets.Players )
		{
			if ( !p.IsValid() || p.IsProxy || p.IsDead ) continue;
			var feet = p.FeetPosition;
			if ( !TrapBase.Touches( Hurt, ToLocal( feet ), p.BodyHeight ) || !Ready( p.GameObject.Id, now ) ) continue;
			p.TakeHit( Damage, Shove( feet ), feet + Vector3.Up * (p.BodyHeight * 0.6f), DamageKind, null );
		}
		if ( !HurtsNpcs || !Networking.IsHost ) return;
		foreach ( var n in targets.Skeletons )
		{
			if ( !n.IsValid() || n.IsDead ) continue;
			var feet = n.WorldPosition;
			if ( !TrapBase.Touches( Hurt, ToLocal( feet ), n.BodyHeight ) || !Ready( n.GameObject.Id, now ) ) continue;
			DamageRouter.Apply( n.GameObject, new DamageRouter.Hit
			{
				Damage = Damage,
				Position = feet + Vector3.Up * 40f,
				Push = Shove( feet ).Normal,
				Attacker = GameObject,
				Kind = DamageKind,
			} );
		}
	}

	private bool Ready( Guid id, float now )
	{
		if ( _nextHit.TryGetValue( id, out var at ) && now < at ) return false;
		_nextHit[id] = now + Interval;
		return true;
	}

	/// <summary>Back out the way you came: the curtain's own object faces into one of the two rooms, so which
	/// side of the wall plane the body is on says which way to shove it.</summary>
	private Vector3 Shove( Vector3 feet )
	{
		if ( Push <= 0f ) return Vector3.Zero;
		float side = ToLocal( feet ).x >= 0f ? 1f : -1f;
		return WorldRotation.Forward * (side * Push);
	}

	/// <summary>Drop bodies that have not been near for a while, so the table cannot grow all run.</summary>
	internal void Prune( float now )
	{
		if ( _nextHit.Count < 16 ) return;
		var stale = new List<Guid>();
		foreach ( var kv in _nextHit ) if ( now > kv.Value + 30f ) stale.Add( kv.Key );
		foreach ( var id in stale ) _nextHit.Remove( id );
	}
}

/// <summary>
/// The vine curtains of the current floor. A plain registry, not a relay: unlike a trap there is no state to
/// keep in step between peers, so nothing here is networked. Registered by the builder and cleared with the
/// rest of the dungeon (DungeonBuilder.Clear), the TrapNet pattern minus the RPCs.
/// </summary>
public static class VineNet
{
	/// <summary>Draw the hurting volumes.</summary>
	[ConVar( "vine_debug" )] public static bool Debug { get; set; } = false;

	private static readonly List<VineCurtain> _curtains = new();

	public static IReadOnlyList<VineCurtain> All => _curtains;
	public static int Count => _curtains.Count;

	public static void Clear() => _curtains.Clear();

	public static int Register( VineCurtain curtain )
	{
		curtain.Index = _curtains.Count;
		_curtains.Add( curtain );
		return curtain.Index;
	}
}
