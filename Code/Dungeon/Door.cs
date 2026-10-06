using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Dungeon;

public enum DoorState { Closed, Opening, Open, Closing }

/// <summary>
/// One door hanging in a doorway: one leaf, or two for the double frames. The component sits on a root at
/// the inner frame's transform, so its local frame is the frame's — +X points into cell a, +Y runs along
/// the wall, and the opening's jambs sit at Centre +/- HalfOpening on Y.
///
/// Each leaf hangs off a "hinge" child at its jamb whose yaw IS the swing: closed is the hinge's base yaw
/// and open is 90 degrees off it, in whichever direction points away from whoever walked up. The leaf mesh
/// is a child of that, offset only for the centre-pivoted flat pair (every other leaf has its hinge baked
/// in at the mesh origin).
///
/// The colliders start disabled and DoorNet.SetSolid turns them on once the navmesh has baked: the bake
/// runs off the whole physics world (DungeonBuilder.BuildNavMeshAsync -> nav.GenerateTiles), so a closed
/// door that is solid at that moment cuts the navmesh at the doorway and strands every skeleton behind it.
/// </summary>
public sealed class Door : Component
{
	/// <summary>A leaf and the hinge it swings on. Side is -1 for the min-Y jamb and +1 for the max-Y jamb.</summary>
	public sealed class Leaf
	{
		public GameObject Hinge;
		public Collider Collider;
		public float BaseYaw;
		public int Side;
	}

	public int Index { get; set; }
	public List<Leaf> Leaves { get; } = new();
	public DoorState State { get; private set; } = DoorState.Closed;

	/// <summary>Half the clear opening along local Y, and where that opening is centred (the frames are not symmetric).</summary>
	public float HalfOpening { get; set; }
	public float OpeningCentre { get; set; }

	/// <summary>0 closed, 1 open. Eased into the hinge yaw.</summary>
	private float _t;
	/// <summary>+1 swings into cell a (local +X), -1 into cell b. Chosen away from whoever opened it.</summary>
	private int _dir = 1;
	private float _clearSince = float.MaxValue;
	private bool _solid;

	protected override void OnAwake() => SetSolid( false );

	/// <summary>Turn the leaf colliders on (after the navmesh bake) or off.</summary>
	public void SetSolid( bool solid )
	{
		_solid = solid;
		ApplyColliders();
	}

	/// <summary>Solid only when the door is fully shut: a leaf mid-swing must never sweep a body along with it.</summary>
	private void ApplyColliders()
	{
		bool on = _solid && State == DoorState.Closed;
		foreach ( var l in Leaves )
			if ( l.Collider.IsValid() ) l.Collider.Enabled = on;
	}

	/// <summary>Where the door is in the world, at the middle of the opening.</summary>
	public Vector3 Centre => WorldTransform.PointToWorld( new Vector3( 0f, OpeningCentre, 0f ) );

	/// <summary>
	/// Is <paramref name="world"/> in front of this doorway? A plain radius would open a door every time
	/// someone walked past it along the wall, so the test is a box: within `reach` of the wall plane,
	/// roughly within the opening across it, and on this floor.
	/// </summary>
	public bool InFront( Vector3 world, float reach, float margin )
	{
		var l = WorldTransform.PointToLocal( world );
		if ( l.z < -60f || l.z > 120f ) return false;               // a body one level up is not at this door
		if ( MathF.Abs( l.x ) > reach ) return false;
		return MathF.Abs( l.y - OpeningCentre ) <= HalfOpening + margin;
	}

	/// <summary>Standing in the opening itself: the door must not shut on them.</summary>
	public bool InDoorway( Vector3 world )
	{
		var l = WorldTransform.PointToLocal( world );
		if ( l.z < -60f || l.z > 120f ) return false;
		return MathF.Abs( l.x ) <= DoorNet.DoorwayDepth && MathF.Abs( l.y - OpeningCentre ) <= HalfOpening;
	}

	/// <summary>Open, swinging away from <paramref name="from"/> so a leaf never comes at whoever triggered it.</summary>
	public void Open( Vector3 from )
	{
		if ( State == DoorState.Closed )
		{
			// local +X is cell a: someone on that side gets the door swung into cell b, and the other way round
			_dir = WorldTransform.PointToLocal( from ).x > 0f ? -1 : 1;
			Audio.GameAudio.Play( DoorNet.OpenSound, Centre );
		}
		if ( State == DoorState.Open || State == DoorState.Opening ) return;
		State = DoorState.Opening;
		ApplyColliders();
	}

	public void Close()
	{
		if ( State == DoorState.Closed || State == DoorState.Closing ) return;
		State = DoorState.Closing;
		Audio.GameAudio.Play( DoorNet.CloseSound, Centre );
	}

	/// <summary>
	/// Driven by DoorSystem rather than OnUpdate so every door ticks in one pass with one shared body scan.
	/// `near` is false for a door nobody is close to, which skips the swing entirely once it has settled.
	/// </summary>
	public void Step( float dt, bool wanted, float now )
	{
		if ( wanted )
		{
			_clearSince = float.MaxValue;
		}
		else if ( State == DoorState.Open || State == DoorState.Opening )
		{
			if ( _clearSince > now ) _clearSince = now;
			else if ( now - _clearSince >= DoorNet.CloseDelay ) Close();
		}

		float target = State is DoorState.Opening or DoorState.Open ? 1f : 0f;
		if ( _t != target )
		{
			float step = dt / MathF.Max( 0.05f, DoorNet.SwingTime );
			_t = target > _t ? MathF.Min( target, _t + step ) : MathF.Max( target, _t - step );
			ApplySwing();
		}

		if ( State == DoorState.Opening && _t >= 1f ) State = DoorState.Open;
		else if ( State == DoorState.Closing && _t <= 0f ) { State = DoorState.Closed; ApplyColliders(); }
	}

	private void ApplySwing()
	{
		// ease in and out so the leaf starts and settles softly instead of snapping to a stop
		float e = _t * _t * (3f - 2f * _t);
		foreach ( var l in Leaves )
		{
			if ( !l.Hinge.IsValid() ) continue;
			l.Hinge.LocalRotation = Rotation.FromYaw( l.BaseYaw + l.Side * _dir * 90f * e );
		}
	}

	public string DebugLabel() => $"#{Index} {State} {_t:0.00} dir {(_dir > 0 ? "+X" : "-X")}{(_solid ? "" : " (not solid yet)")}";
}
