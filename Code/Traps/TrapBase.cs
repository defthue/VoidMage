using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;
using VoidMage.Combat;
using VoidMage.Dungeon;
using VoidMage.Npc;

namespace VoidMage.Traps;

public enum TrapState { Armed, WindUp, Fire, Reset }

/// <summary>
/// One trap of the current floor. Built deterministically on every peer from the decoration's TrapPlacement
/// (never networked), registered by index in <see cref="TrapNet"/>; triggers are decided on the host and relayed
/// as "armed now", every peer then runs the same wind-up on its own clock. Hits are judged where the health
/// lives: the local player's client judges hits on itself against the trap as it sees it (no host echo lag, a
/// blade never hits you where it is not), the host judges hits on skeletons. Detection is by world boxes over
/// grid cells (pressure, proximity, the spike floor) or a swept shape test (the blade); no trigger colliders.
/// Freeze is the hook for the ice spell: a frozen trap's clock stops, nothing moves, nothing hits.
/// </summary>
public abstract class TrapBase : Component
{
	public const string Tag = "trap";

	public int Index { get; internal set; } = -1;
	public TrapPlacement Placement { get; set; }
	public TrapKind Kind => Placement.Kind;
	public TrapTrigger Trigger => TrapCatalog.Defs[Kind].Trigger;
	public TrapState State { get; private set; } = TrapState.Armed;
	/// <summary>World boxes (one per grid cell) whose occupants set the trap off. Empty for a cycling trap.</summary>
	public BBox[] TriggerBoxes { get; set; } = Array.Empty<BBox>();
	/// <summary>World boxes the trap hurts.</summary>
	public BBox[] DamageBoxes { get; set; } = Array.Empty<BBox>();

	public float FrozenUntil { get; private set; } = float.NegativeInfinity;
	public bool Frozen => Time.Now < FrozenUntil;
	/// <summary>Total seconds this trap has spent frozen: subtracted from the clock a cycling trap runs on.</summary>
	public float FrozenSeconds { get; private set; }

	/// <summary>The ice hook: the trap's clock stops for this long (extends, never shortens, a running freeze).</summary>
	public void Freeze( float seconds ) => FrozenUntil = MathF.Max( FrozenUntil, Time.Now + seconds );

	/// <summary>Seconds between the trigger and the hit; 0 for a cycling trap.</summary>
	public abstract float WindupSeconds { get; }

	private float _stateAt;
	protected float StateAge => Time.Now - _stateAt;
	/// <summary>Whoever was hit by the current firing: one hit per body per fire (a flame clears it per tick).</summary>
	protected readonly HashSet<Guid> HitThisFire = new();

	/// <summary>The centre of the first damage box: what "near a player" is measured from.</summary>
	public Vector3 Centre => DamageBoxes.Length > 0 ? DamageBoxes[0].Center : WorldPosition;

	/// <summary>Every peer, from TrapNet.ArmRpc. Idempotent: only an Armed trap starts a wind-up.</summary>
	internal void Arm()
	{
		if ( State != TrapState.Armed || Frozen ) return;
		Enter( TrapState.WindUp );
		OnArmed();
	}

	protected virtual void OnArmed() { }

	/// <summary>The charge-up of a pressure trap: one clip the length of the longest wind-up, played once as it arms.</summary>
	public const string SoundEngage = "sounds/trapengage.sound";

	/// <summary>True when the local listener is within TrapNet.ActiveRadius: OnArmed has no `near` of its own.</summary>
	protected bool Audible( Vector3 at ) => Sound.Listener.Position.DistanceSquared( at ) <= TrapNet.ActiveRadius * TrapNet.ActiveRadius;

	/// <summary>One frame from TrapSystem. A frozen trap holds its state timer and moves nothing.</summary>
	internal void Step( float now, float dt, TrapSystem.Targets targets, bool near )
	{
		if ( Frozen )
		{
			_stateAt += dt;
			FrozenSeconds += dt;
			return;
		}
		Tick( now, dt, targets, near );
	}

	/// <summary>One unfrozen frame. `near` = a player is within TrapNet.ActiveRadius, so sounds and effects are worth playing.</summary>
	protected abstract void Tick( float now, float dt, TrapSystem.Targets targets, bool near );

	protected void Enter( TrapState state )
	{
		State = state;
		_stateAt = Time.Now;
		HitThisFire.Clear();
	}

	/// <summary>
	/// A standing skeleton's height (units). Kept as the DEFAULT of <see cref="SkeletonNpc.BodyHeight"/>, not
	/// as the value the body tests use: every NPC used to be measured as 70 units tall whatever it was, so a
	/// 22-unit rat counted as reaching into the flame head's jet band (46-78 above the floor) that it should
	/// walk straight under. The tests read each body's own height now; skeletons are unaffected because theirs
	/// is this number.
	/// </summary>
	public const float SkeletonHeight = 70f;

	/// <summary>A body standing at `feet`, `height` tall, reaches into the box: feet inside its footprint and the body's span overlapping its height band.</summary>
	public static bool Touches( in BBox b, Vector3 feet, float height )
		=> feet.x >= b.Mins.x && feet.x <= b.Maxs.x && feet.y >= b.Mins.y && feet.y <= b.Maxs.y
		&& feet.z <= b.Maxs.z && feet.z + height >= b.Mins.z;

	public static bool Inside( BBox[] boxes, Vector3 feet, float height )
	{
		foreach ( var b in boxes ) if ( Touches( b, feet, height ) ) return true;
		return false;
	}

	/// <summary>A hit on a player: only that player's own client applies it (owner-authoritative health).</summary>
	protected bool HitPlayer( PlayerHealth p, float damage, Vector3 push, Vector3 from, string kind )
	{
		if ( !p.IsValid() || p.IsProxy || p.IsDead ) return false;
		p.TakeHit( damage, push, from, kind, null );
		return true;
	}

	/// <summary>A hit on a skeleton: the host applies it (host-authoritative enemies). Push is a unit direction.</summary>
	protected bool HitSkeleton( SkeletonNpc n, float damage, Vector3 push, Vector3 from, string kind )
	{
		if ( !Networking.IsHost || !n.IsValid() || n.IsDead ) return false;
		return DamageRouter.Apply( n.GameObject, new DamageRouter.Hit { Damage = damage, Position = from, Push = push, Attacker = GameObject, Kind = kind } );
	}

	/// <summary>Everyone standing in the damage boxes takes the hit, once per firing.</summary>
	protected void HitInside( TrapSystem.Targets targets, float damage, string kind, Func<Vector3, Vector3> playerPush, Func<Vector3, Vector3> skeletonPush )
	{
		foreach ( var p in targets.Players )
		{
			if ( !p.IsValid() ) continue;
			var feet = p.FeetPosition;
			if ( !Inside( DamageBoxes, feet, p.BodyHeight ) || HitThisFire.Contains( p.GameObject.Id ) ) continue;
			if ( HitPlayer( p, damage, playerPush( feet ), feet + Vector3.Up * (p.BodyHeight * 0.6f), kind ) ) HitThisFire.Add( p.GameObject.Id );
		}
		foreach ( var n in targets.Skeletons )
		{
			if ( !n.IsValid() ) continue;
			var feet = n.WorldPosition;
			if ( !Inside( DamageBoxes, feet, n.BodyHeight ) || HitThisFire.Contains( n.GameObject.Id ) ) continue;
			if ( HitSkeleton( n, damage, skeletonPush( feet ), feet + Vector3.Up * 40f, kind ) ) HitThisFire.Add( n.GameObject.Id );
		}
	}

	public virtual string DebugLabel() => $"#{Index} {Kind} {State}{(Frozen ? " FROZEN" : "")}";
}
