using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Boss;

public enum BossPartKind { Skull, HandL, HandR }

/// <summary>
/// One hittable piece of the Bone Colossus: the skull or a hand. Its own health (the skull's under a first layer,
/// <see cref="GuardHealth"/>, whose loss is phase 2; no posture or staggers: the user cut them 2026-09-23), a set of
/// oriented boxes that follow the model (cranium, horns, jaw on the jaw child; forearm, palm, one per finger:
/// tools/gen_boss_shapes.py, slightly bigger than the bone) the sword and the spells judge against by closest
/// point, and the client-to-host hit door every enemy uses (<see cref="ReceiveHit"/> forwards to the host,
/// <see cref="ApplyHitHost"/> is the one place damage lands). The boxes are colliders tagged `boss`, which the
/// collision rules make ignore the player, NPCs and debris: they exist to be hit, they never shove anything.
/// The GameObject is a network root the host moves; the brain (on the skull) decides everything else.
/// </summary>
[Title( "Boss Part" ), Category( "Void Mage" ), Icon( "skull" )]
public sealed class BossPart : Component, ICombatant, DamageRouter.IHittable
{
	[Property] public BossPartKind Kind { get; set; }

	[Property, Group( "Vitals" )] public float MaxHealth { get; set; } = 250f;
	/// <summary>
	/// The skull's first health layer (0 for a hand): every hit on the skull drains this before its health, and it is
	/// struck off whole when both hands are destroyed. Its loss is phase 2. The shipped numbers are the `boss_hp_*`
	/// convars (BossTuning); the brain writes them onto the parts at the wake-up.
	/// </summary>
	[Property, Group( "Vitals" )] public float GuardHealth { get; set; }

	/// <summary>The hit shapes, one per line: `name|parent|cx,cy,cz|qx,qy,qz,qw|sx,sy,sz` in model units (parent = a child GameObject's name, e.g. `jaw`, or empty). Empty: one box from BoxCenter/BoxSize or the model bounds.</summary>
	[Property, Group( "Body" )] public List<string> ShapeSpecs { get; set; } = new();
	/// <summary>The fallback single box in local units; a zero size takes the model's bounds.</summary>
	[Property, Group( "Body" )] public Vector3 BoxCenter { get; set; }
	[Property, Group( "Body" )] public Vector3 BoxSize { get; set; }
	/// <summary>
	/// A hand's frame fix: the rotation that turns the model so its palm normal points +x and its fingers +z. The
	/// brain composes `LookAt(palmToward, fingersToward) * StanceRotation` for every hand pose, so the two different
	/// sculpts (R lies flat, L claws up) present the same way. Generated from the rig json by tools/gen_boss_prefabs.py.
	/// </summary>
	[Property, Group( "Body" )] public Rotation StanceRotation { get; set; } = Rotation.Identity;
	/// <summary>The point the brain moves, in model units: a hand's palm centre (the origin is at the wrist end), zero for the skull.</summary>
	[Property, Group( "Body" )] public Vector3 Pivot { get; set; }

	[Property, Group( "Feel" )] public string HitSound { get; set; } = "sounds/swordhitstone.sound";
	[Property, Group( "Feel" )] public float FlashSeconds { get; set; } = 0.12f;
	/// <summary>The bone's own material (base slot; the outline slot stays): ivory with a hellfire rim.</summary>
	[Property, Group( "Feel" )] public string BoneMaterial { get; set; } = "shaders/bossbone.vmat";

	[Sync( SyncFlags.FromHost )] public float Health { get; set; }
	/// <summary>What is left of the first layer (the skull only); 0 once it has broken.</summary>
	[Sync( SyncFlags.FromHost )] public float Guard { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Dead { get; set; }
	/// <summary>Pulse: every landed hit, for the flash on every peer.</summary>
	[Sync( SyncFlags.FromHost )] public int HurtCount { get; set; }
	/// <summary>A hand's finger pose (animgraphs/boss_hand.vanmgrph `hand_pose`): 0 idle, 1 open, 2 fist, 3 point, 4 grab, 5 splay.</summary>
	[Sync( SyncFlags.FromHost )] public int Pose { get; set; }

	public float Health01 => MaxHealth > 0f ? Math.Clamp( Health / MaxHealth, 0f, 1f ) : 0f;
	public float Guard01 => GuardHealth > 0f ? Math.Clamp( Guard / GuardHealth, 0f, 1f ) : 0f;
	/// <summary>Is the first layer still up (the skull before phase 2)?</summary>
	public bool Guarded => Guard > 0f;

	// ICombatant: always the enemy's side; hunted and hittable once the boss is awake
	public Team Team => Team.Hostile;
	public bool IsDead => Dead;
	public bool CanBeTargeted => !Dead && (Brain?.Awake ?? true);
	public float BodyHeight => WorldBounds.Size.z;

	public BossBrain Brain => BossBrain.Current;

	/// <summary>One hit shape: an oriented box on its own GameObject (the collider), with the model-space definition for the geometry maths.</summary>
	private sealed class Shape
	{
		public string Name;
		public GameObject Go;
		public BoxCollider Box;
		/// <summary>Model-space centre, rotation and size (the part's frame, ignoring the jaw's swing).</summary>
		public Vector3 Center;
		public Rotation Rot;
		public Vector3 Size;
		/// <summary>A sphere instead of a box when set (model units; Size is then the diameter on every axis).</summary>
		public float Radius;
		/// <summary>The animated bone this box rides (a finger bone, the palm), or null for a box fixed to the part / a child.</summary>
		public string Bone;
		/// <summary>The box relative to its bone's bind pose (model space), once known.</summary>
		public Transform? LocalToBone;
		/// <summary>A surface an attack strikes with (the palm, fingers and wrist): what the player's body is judged against. The forearm, cranium and horns are not.</summary>
		public bool Strike;
	}

	private readonly List<Shape> _shapes = new();
	private ModelRenderer _renderer;
	private int _seenHurt;
	private float _flashUntil;
	private Material _flash;
	private Material _bone;
	private static readonly string[] PartTags = { "enemy", "npc", "boss" };

	protected override void OnAwake()
	{
		_renderer = Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		foreach ( var t in PartTags ) if ( !GameObject.Tags.Has( t ) ) GameObject.Tags.Add( t );
		BuildShapes();
		if ( Networking.IsHost && Health <= 0f && !Dead ) { Health = MaxHealth; Guard = GuardHealth; }
		ApplyBoneMaterial();
		if ( Kind != BossPartKind.Skull && !Components.Get<BossHandTrail>().IsValid() ) Components.Create<BossHandTrail>();
	}

	private void BuildShapes()
	{
		_shapes.Clear();
		foreach ( var spec in ShapeSpecs )
		{
			var s = ParseShape( spec );
			if ( s is null ) { Log.Warning( $"[Boss] bad shape spec on {Kind}: {spec}" ); continue; }
			var parent = GameObject;
			string bone = null;
			if ( s.Value.parent.StartsWith( "bone:" ) ) bone = s.Value.parent[5..];
			else if ( !string.IsNullOrEmpty( s.Value.parent ) )
				foreach ( var c in GameObject.Children ) if ( c.Name == s.Value.parent ) { parent = c; break; }
			var shape = MakeShape( parent, s.Value.name, s.Value.center, s.Value.rot, s.Value.size, s.Value.radius );
			shape.Bone = bone;
			shape.Strike = Kind != BossPartKind.Skull && s.Value.name != "forearm";
			_shapes.Add( shape );
		}
		if ( _shapes.Count == 0 )
		{
			// the fallback: one box, from the properties or the model
			if ( BoxSize.IsNearZeroLength && _renderer?.Model is not null )
			{
				BoxCenter = _renderer.Model.Bounds.Center;
				BoxSize = _renderer.Model.Bounds.Size;
			}
			_shapes.Add( MakeShape( GameObject, "body", BoxCenter, Rotation.Identity, BoxSize, 0f ) );
		}
	}

	private Shape MakeShape( GameObject parent, string name, Vector3 center, Rotation rot, Vector3 size, float radius )
	{
		var go = new GameObject( true, "hit_" + name ) { Parent = parent };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.LocalPosition = center;
		go.LocalRotation = rot;
		go.LocalScale = Vector3.One;
		foreach ( var t in PartTags ) go.Tags.Add( t );
		if ( radius > 0f )
		{
			var sphere = go.Components.Create<SphereCollider>();
			sphere.Center = Vector3.Zero;
			sphere.Radius = radius;
			sphere.Static = false;
			return new Shape { Name = name, Go = go, Center = center, Rot = rot, Size = new Vector3( radius * 2f ), Radius = radius };
		}
		var box = go.Components.Create<BoxCollider>();
		box.Center = Vector3.Zero;
		box.Scale = size;
		box.Static = false;
		return new Shape { Name = name, Go = go, Box = box, Center = center, Rot = rot, Size = size };
	}

	/// <summary>`name|parent|cx,cy,cz|qx,qy,qz,qw|sx,sy,sz` is a box; a single number in the size field (`...|r`) is a sphere of that radius.</summary>
	private static (string name, string parent, Vector3 center, Rotation rot, Vector3 size, float radius)? ParseShape( string spec )
	{
		var f = spec.Split( '|' );
		if ( f.Length < 5 ) return null;
		float[] N( string s ) => Array.ConvertAll( s.Split( ',' ), x => float.Parse( x, CultureInfo.InvariantCulture ) );
		try
		{
			var c = N( f[2] ); var q = N( f[3] ); var s = N( f[4] );
			if ( c.Length < 3 || q.Length < 4 ) return null;
			if ( s.Length == 1 ) return (f[0], f[1], new Vector3( c[0], c[1], c[2] ), new Rotation( q[0], q[1], q[2], q[3] ), new Vector3( s[0] * 2f ), s[0]);
			if ( s.Length < 3 ) return null;
			return (f[0], f[1], new Vector3( c[0], c[1], c[2] ), new Rotation( q[0], q[1], q[2], q[3] ), new Vector3( s[0], s[1], s[2] ), 0f);
		}
		catch ( FormatException ) { return null; }
	}

	/// <summary>Is a world point inside this shape, each face (or the sphere's surface) pulled in by `shrink`?</summary>
	private static bool ShapeContains( Shape s, Vector3 world, float shrink )
	{
		var tr = s.Go.WorldTransform;
		if ( s.Radius > 0f )
		{
			var r = s.Radius * MathF.Max( 0.01f, tr.Scale.x ) - shrink;
			return r > 0f && tr.Position.DistanceSquared( world ) <= r * r;
		}
		var l = tr.PointToLocal( world );
		var h = s.Size * 0.5f - new Vector3( shrink );
		if ( h.x <= 0f || h.y <= 0f || h.z <= 0f ) return false;
		return MathF.Abs( l.x ) <= h.x && MathF.Abs( l.y ) <= h.y && MathF.Abs( l.z ) <= h.z;
	}

	/// <summary>Every peer: the finger and palm boxes ride their animated bones (the fist and the flat hand carry their own hit shapes).</summary>
	private void FollowBones()
	{
		if ( _renderer is not SkinnedModelRenderer skinned || skinned.Model is null ) return;
		foreach ( var sh in _shapes )
		{
			if ( sh.Bone is null || !sh.Go.IsValid() ) continue;
			if ( sh.LocalToBone is null )
			{
				var bone = skinned.Model.Bones.GetBone( sh.Bone );
				if ( bone is null ) { sh.Bone = null; continue; }
				sh.LocalToBone = bone.LocalTransform.ToLocal( new Transform( sh.Center, sh.Rot, 1f ) );
			}
			if ( skinned.TryGetBoneTransform( sh.Bone, out var world ) ) sh.Go.WorldTransform = world.ToWorld( sh.LocalToBone.Value );
		}
	}

	/// <summary>Every renderer of the part (the skull's jaw child included) on the bone material, base slot only.</summary>
	private void ApplyBoneMaterial()
	{
		if ( string.IsNullOrEmpty( BoneMaterial ) ) return;
		_bone ??= Material.Load( BoneMaterial );
		foreach ( var r in Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			HellArena.SwapBaseMaterial( r, _bone );
	}

	protected override void OnEnabled() => BossNet.Register( this );
	protected override void OnDisabled() => BossNet.Unregister( this );

	// ------------------------------------------------------------------ geometry

	/// <summary>Every hit shape as (world transform at its centre, size in local units, is a strike surface, sphere radius in local units or 0 for a box): for drawing.</summary>
	public IEnumerable<(Transform World, Vector3 Size, bool Strike, float Radius)> Shapes
	{
		get
		{
			foreach ( var s in _shapes ) if ( s.Go.IsValid() ) yield return (s.Go.WorldTransform, s.Size, s.Strike, s.Radius);
		}
	}

	/// <summary>Is a world point inside any strike surface (palm, fingers, wrist; each shape pulled in by `forgive` on every side)?</summary>
	public bool InStrikeShapes( Vector3 world, float forgive )
	{
		foreach ( var s in _shapes )
			if ( s.Strike && s.Go.IsValid() && ShapeContains( s, world, forgive ) ) return true;
		return false;
	}

	/// <summary>The union of the shapes' world boxes.</summary>
	public BBox WorldBounds
	{
		get
		{
			var first = true;
			var b = default( BBox );
			foreach ( var s in _shapes )
			{
				if ( !s.Go.IsValid() ) continue;
				var tr = s.Go.WorldTransform;
				var wb = s.Radius > 0f
					? BBox.FromPositionAndSize( tr.Position, s.Radius * 2f * MathF.Max( 0.01f, tr.Scale.x ) )
					: new BBox( -s.Size * 0.5f, s.Size * 0.5f ).Transform( tr );
				b = first ? wb : b.AddBBox( wb );
				first = false;
			}
			return first ? BBox.FromPositionAndSize( WorldPosition, 10f ) : b;
		}
	}

	/// <summary>The first shape's centre (cranium, forearm): where a bolt homes.</summary>
	public Vector3 AimPoint => _shapes.Count > 0 && _shapes[0].Go.IsValid() ? _shapes[0].Go.WorldPosition : WorldPosition;

	/// <summary>The nearest point of any shape to `world`: what a blade or a bolt is judged against.</summary>
	public Vector3 ClosestPoint( Vector3 world )
	{
		var best = WorldPosition;
		var bestD = float.MaxValue;
		foreach ( var s in _shapes )
		{
			if ( !s.Go.IsValid() ) continue;
			var w = ClosestOnShape( s, world );
			var d = w.DistanceSquared( world );
			if ( d < bestD ) { bestD = d; best = w; }
		}
		return best;
	}

	/// <summary>
	/// The nearest point of any shape to the segment a..b (a blade's line), and the segment's point it is nearest to:
	/// what a swing is judged against, so a hand beside you counts by the part of it you swing AT, not by the point
	/// nearest your face (the palm top under your feet). Convex shape against a segment: alternating projection.
	/// </summary>
	public Vector3 ClosestPointToSegment( Vector3 a, Vector3 b, out Vector3 onSegment )
	{
		var best = WorldPosition;
		var bestOn = a;
		var bestD = float.MaxValue;
		foreach ( var s in _shapes )
		{
			if ( !s.Go.IsValid() ) continue;
			var p = (a + b) * 0.5f;
			var w = p;
			for ( var i = 0; i < 6; i++ )
			{
				w = ClosestOnShape( s, p );
				p = ClosestOnSegment( a, b, w );
			}
			var d = w.DistanceSquared( p );
			if ( d < bestD ) { bestD = d; best = w; bestOn = p; }
		}
		onSegment = bestOn;
		return best;
	}

	private static Vector3 ClosestOnSegment( Vector3 a, Vector3 b, Vector3 p )
	{
		var ab = b - a;
		var len2 = ab.LengthSquared;
		if ( len2 < 0.0001f ) return a;
		var t = Math.Clamp( Vector3.Dot( p - a, ab ) / len2, 0f, 1f );
		return a + ab * t;
	}

	/// <summary>The nearest point of one shape to a world point (the point itself when inside).</summary>
	private static Vector3 ClosestOnShape( Shape s, Vector3 world )
	{
		var tr = s.Go.WorldTransform;
		if ( s.Radius > 0f )
		{
			var r = s.Radius * MathF.Max( 0.01f, tr.Scale.x );
			var to = world - tr.Position;
			return to.Length <= r ? world : tr.Position + to.Normal * r;
		}
		var l = tr.PointToLocal( world );
		var h = s.Size * 0.5f;
		var c = new Vector3( Math.Clamp( l.x, -h.x, h.x ), Math.Clamp( l.y, -h.y, h.y ), Math.Clamp( l.z, -h.z, h.z ) );
		return tr.PointToWorld( c );
	}

	/// <summary>
	/// The HORIZONTAL displacement that takes a sphere (`world`, `radius`) out of every shape it is inside, shape by
	/// shape, each shape first pulled in by `shrink` (the boxes are padded for the sword): a box along the least
	/// penetrated of its local axes that run mostly level, a sphere radially and flat. Zero when clear. The player is
	/// kept out of the hands with this: the collision rules let the parts pass through bodies, so a resting hand
	/// could be walked into and the camera went black inside it. Level only: a vertical push lifted the player into
	/// a hand nothing let them stand on, and pinned them there.
	/// </summary>
	public Vector3 Resolve( Vector3 world, float radius, float shrink )
	{
		var total = Vector3.Zero;
		foreach ( var s in _shapes )
		{
			if ( !s.Go.IsValid() ) continue;
			var tr = s.Go.WorldTransform;
			var scale = MathF.Max( 0.01f, tr.Scale.x );
			var p = world + total;
			if ( s.Radius > 0f )
			{
				var r = s.Radius * scale - shrink + radius;
				var d = p - tr.Position;
				if ( r <= 0f || d.Length >= r ) continue;
				var flat = d.WithZ( 0f );
				var len = flat.Length;
				total += (len < 0.01f ? Vector3.Forward : flat / len) * (r - len);
				continue;
			}
			var l = tr.PointToLocal( p );
			var h = s.Size * 0.5f + new Vector3( (radius - shrink) / scale );
			if ( h.x <= 0f || h.y <= 0f || h.z <= 0f ) continue;
			if ( MathF.Abs( l.x ) >= h.x || MathF.Abs( l.y ) >= h.y || MathF.Abs( l.z ) >= h.z ) continue;
			// the level axes: a local axis whose world direction runs mostly flat
			var best = float.MaxValue;
			var push = Vector3.Zero;
			for ( var axis = 0; axis < 3; axis++ )
			{
				var dir = tr.Rotation * (axis == 0 ? Vector3.Forward : axis == 1 ? Vector3.Left : Vector3.Up);
				var flat = dir.WithZ( 0f );
				if ( flat.Length < 0.5f ) continue;
				var li = axis == 0 ? l.x : axis == 1 ? l.y : l.z;
				var hi = axis == 0 ? h.x : axis == 1 ? h.y : h.z;
				var pen = (hi - MathF.Abs( li )) * scale / flat.Length;   // along the flat direction it takes a little more to clear
				if ( pen >= best ) continue;
				best = pen;
				push = flat.Normal * MathF.Sign( li == 0f ? 1f : li ) * pen;
			}
			if ( best == float.MaxValue )
			{
				// a box standing on end with every axis steep: out of it the way the point sits, flat
				var d = (p - tr.Position).WithZ( 0f );
				push = (d.IsNearZeroLength ? Vector3.Forward : d.Normal) * (MathF.Max( h.x, h.y ) * scale);
			}
			total += push;
		}
		return total;
	}

	/// <summary>
	/// The lowest point of the HAND's hit shapes (palm and fingers) as they stand right now (pose and rotation
	/// included) relative to the pivot's height: negative = below the pivot. The forearm and the wrist box riding it
	/// are left out: they swing on the arm spring, and under a fingers-up palm the forearm hangs straight down, so
	/// counting its wrist box floated every floor-level pass (the sweep, the palm strike, the grab) a wrist box over
	/// the floor and over the player's head. The floor contact is the palm's heel.
	/// </summary>
	public float LiveBottomFromPivot()
	{
		var min = float.MaxValue;
		foreach ( var sh in _shapes )
		{
			if ( sh.Name is "forearm" or "wrist" || !sh.Go.IsValid() ) continue;
			var tr = sh.Go.WorldTransform;
			var scale = MathF.Max( 0.01f, tr.Scale.x );
			float bottom;
			if ( sh.Radius > 0f ) bottom = tr.Position.z - sh.Radius * scale;
			else
			{
				var h = sh.Size * 0.5f * scale;
				var r = tr.Rotation;
				var ext = MathF.Abs( (r * Vector3.Forward).z ) * h.x + MathF.Abs( (r * Vector3.Left).z ) * h.y + MathF.Abs( (r * Vector3.Up).z ) * h.z;
				bottom = tr.Position.z - ext;
			}
			min = MathF.Min( min, bottom );
		}
		return min == float.MaxValue ? 0f : min - PivotWorld.z;
	}

	/// <summary>The lowest point of ONE named shape (the palm) as it stands right now, relative to the pivot's height; the hand's lowest point when there is no such shape. A pass judged by the palm alone is planned from this: a thumb or a finger hanging under the palm's edge must not lift the palm.</summary>
	public float ShapeBottomFromPivot( string name )
	{
		foreach ( var sh in _shapes )
		{
			if ( sh.Name != name || !sh.Go.IsValid() ) continue;
			var tr = sh.Go.WorldTransform;
			var scale = MathF.Max( 0.01f, tr.Scale.x );
			if ( sh.Radius > 0f ) return tr.Position.z - sh.Radius * scale - PivotWorld.z;
			var h = sh.Size * 0.5f * scale;
			var r = tr.Rotation;
			var ext = MathF.Abs( (r * Vector3.Forward).z ) * h.x + MathF.Abs( (r * Vector3.Left).z ) * h.y + MathF.Abs( (r * Vector3.Up).z ) * h.z;
			return tr.Position.z - ext - PivotWorld.z;
		}
		return LiveBottomFromPivot();
	}

	/// <summary>Is a world point inside the named shape (pulled in by `forgive` on every side)? False when there is no such shape.</summary>
	public bool InShape( string name, Vector3 world, float forgive )
	{
		foreach ( var s in _shapes )
			if ( s.Name == name && s.Go.IsValid() ) return ShapeContains( s, world, forgive );
		return false;
	}

	/// <summary>The highest point of the hand's own hit shapes (palm and fingers, as posed and turned right now) relative to the pivot's height: the top of a fist, where a held head sticks out.</summary>
	public float LiveTopFromPivot()
	{
		var max = float.MinValue;
		foreach ( var sh in _shapes )
		{
			if ( sh.Name is "forearm" or "wrist" || !sh.Go.IsValid() ) continue;
			var tr = sh.Go.WorldTransform;
			var scale = MathF.Max( 0.01f, tr.Scale.x );
			float top;
			if ( sh.Radius > 0f ) top = tr.Position.z + sh.Radius * scale;
			else
			{
				var h = sh.Size * 0.5f * scale;
				var r = tr.Rotation;
				var ext = MathF.Abs( (r * Vector3.Forward).z ) * h.x + MathF.Abs( (r * Vector3.Left).z ) * h.y + MathF.Abs( (r * Vector3.Up).z ) * h.z;
				top = tr.Position.z + ext;
			}
			max = MathF.Max( max, top );
		}
		return max == float.MinValue ? 0f : max - PivotWorld.z;
	}

	/// <summary>Is a world point inside any shape?</summary>
	public bool Contains( Vector3 world )
	{
		foreach ( var s in _shapes )
			if ( s.Go.IsValid() && ShapeContains( s, world, 0f ) ) return true;
		return false;
	}

	/// <summary>Is a world point inside a box given in the part's local (model) units?</summary>
	public bool ContainsLocal( Vector3 world, BBox local )
	{
		var l = WorldTransform.PointToLocal( world );
		return l.x >= local.Mins.x && l.x <= local.Maxs.x && l.y >= local.Mins.y && l.y <= local.Maxs.y && l.z >= local.Mins.z && l.z <= local.Maxs.z;
	}

	/// <summary>The height of the part's lowest point relative to its origin, for a world rotation: origin z = ground - this puts it on the ground. The forearm is left out: it swings on its own and keeps itself off the floor (BossArm).</summary>
	public float BottomOffset( Rotation rot )
	{
		var s = MathF.Max( 0.01f, WorldScale.x );
		var min = float.MaxValue;
		foreach ( var sh in _shapes )
		{
			if ( sh.Name == "forearm" ) continue;
			var r = rot * sh.Rot;
			var h = sh.Size * 0.5f * s;
			var ext = sh.Radius > 0f ? sh.Radius * s
				: MathF.Abs( (r * Vector3.Forward).z ) * h.x + MathF.Abs( (r * Vector3.Left).z ) * h.y + MathF.Abs( (r * Vector3.Up).z ) * h.z;
			min = MathF.Min( min, (rot * (sh.Center * s)).z - ext );
		}
		return min == float.MaxValue ? 0f : min;
	}

	/// <summary>The lowest point relative to the PIVOT for a rotation (the brain moves the pivot, not the origin).</summary>
	public float BottomOffsetFromPivot( Rotation rot ) => BottomOffset( rot ) - (rot * (Pivot * MathF.Max( 0.01f, WorldScale.x ))).z;

	/// <summary>The pivot's world position.</summary>
	public Vector3 PivotWorld => WorldTransform.PointToWorld( Pivot );

	/// <summary>The origin that puts the pivot at `pivotWorld` for a rotation.</summary>
	public Vector3 OriginFor( Vector3 pivotWorld, Rotation rot ) => pivotWorld - rot * (Pivot * MathF.Max( 0.01f, WorldScale.x ));

	/// <summary>The closest point, pulled down to the arena floor for a walker that wants to stand next to it.</summary>
	public Vector3 FloorContact( Vector3 from )
	{
		var p = ClosestPoint( from );
		return HellArena.Built ? p.WithZ( HellArena.FloorZ ) : p;
	}

	// ------------------------------------------------------------------ hits in

	public void ReceiveHit( in DamageRouter.Hit hit )
	{
		if ( Networking.IsHost ) { ApplyHitHost( hit ); return; }
		var s = hit.Spell;
		RequestHit( hit.Damage, hit.Position, hit.Push, hit.Kind ?? "", hit.Attacker, s.SpellId ?? "", s.Mods, s.Mods2, s.Depth, s.CastId, (int)s.Flags );
	}

	[Rpc.Host]
	private void RequestHit( float damage, Vector3 position, Vector3 push, string kind, GameObject attacker, string spellId, long mods, long mods2, int depth, int castId, int flags )
	{
		ApplyHitHost( new DamageRouter.Hit
		{
			Damage = damage, Position = position, Push = push, Attacker = attacker, Kind = kind,
			Spell = new SpellHitInfo { SpellId = spellId, Mods = mods, Mods2 = mods2, Depth = depth, CastId = castId, Flags = (SpellHitFlags)flags },
		} );
	}

	/// <summary>
	/// Host only: the one door. The brain's exposure multiplier (a skull resting after its own move), the crit
	/// multiplier, death. The skull's first layer takes every hit before its health, and the hit that
	/// breaks it is spent on it (nothing carries over: the break is a clean beat).
	/// </summary>
	public void ApplyHitHost( in DamageRouter.Hit hit )
	{
		if ( !Networking.IsHost || Dead ) return;
		var brain = Brain;
		if ( brain.IsValid() && !brain.Awake ) brain.Wake();   // a blade in a sleeping skull is a wake-up call
		var exposure = brain.IsValid() ? brain.DamageFactor( this ) : 1f;
		var (crit, critMultiplier) = DamageRouter.RollCrit( hit );
		var applied = hit.Damage * exposure * critMultiplier;
		if ( Guard > 0f )
		{
			Guard = MathF.Max( 0f, Guard - applied );
			if ( Guard <= 0f ) Brain?.OnGuardBroken( this );
		}
		else Health = MathF.Max( 0f, Health - applied );
		HurtCount++;
		ShowDamage( (int)MathF.Round( applied ), hit.Position, crit, DamageRouter.IsMagic( hit.Kind ) );
		Spells.SpellHostTuning.OnDamageDealtTo( Kind.ToString(), hit, applied );
		if ( Health <= 0f ) DieHost( hit.Push );
	}

	/// <summary>Host: dead outright (the `boss_kill` command).</summary>
	public void KillHost()
	{
		if ( !Networking.IsHost || Dead ) return;
		Health = 0f;
		Guard = 0f;
		DieHost( Vector3.Zero );
	}

	/// <summary>Host: the first layer struck off whole (both hands destroyed, or `boss_phase 2`); the brain hears of it once.</summary>
	public void BreakGuardHost()
	{
		if ( !Networking.IsHost || Dead || Guard <= 0f ) return;
		Guard = 0f;
		Brain?.OnGuardBroken( this );
	}

	/// <summary>Host, at the wake-up: the shipped vitals (the `boss_hp_*` convars) written over the prefab's, the pools full.</summary>
	public void SetVitalsHost( float maxHealth, float guard )
	{
		if ( !Networking.IsHost || Dead ) return;
		if ( maxHealth > 0f ) MaxHealth = maxHealth;
		GuardHealth = MathF.Max( 0f, guard );
		Health = MaxHealth;
		Guard = GuardHealth;
	}

	private void DieHost( Vector3 push )
	{
		Dead = true;
		// a hand destroyed in the fight bursts on the spot; a bone dying in the skull's death scene has already burst with it
		var burst = Kind != BossPartKind.Skull && Brain is not { Phase: BossPhase.Dead };
		Brain?.OnPartDied( this );
		ShowDeath( burst );
	}

	// ------------------------------------------------------------------ presentation, every peer

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	private void ShowDamage( int damage, Vector3 position, bool crit, bool magic )
	{
		UI.DamageNumbers.Push( damage, position, crit, magic );
		// the bone's hit sound is the SWORD's: a spell landing brings its own impact sound
		if ( !magic ) Audio.GameAudio.Play( HitSound, position );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void ShowDeath( bool burst )
	{
		// no longer hittable. A hand destroyed in the fight BURSTS (the skull's own burst, chips and rings) and is
		// gone; the skull plays its death scene from the brain (the rise, then the burst) and hides there.
		foreach ( var s in _shapes ) if ( s.Box.IsValid() ) s.Box.Enabled = false;
		if ( !burst ) { Combat.HitFeel.Shake( 3f, 0.6f ); return; }
		Burst();
		Combat.HitFeel.Shake( 6f, 0.8f );
	}

	/// <summary>Every peer: the bone flies apart where it is and its models go dark.</summary>
	public void Burst()
	{
		var b = WorldBounds;
		BossVfx.Burst( Scene, b.Center, MathF.Max( 120f, b.Size.Length * 0.4f ) );
		foreach ( var r in Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) ) r.Enabled = false;
	}

	protected override void OnUpdate()
	{
		if ( HurtCount != _seenHurt )
		{
			_seenHurt = HurtCount;
			_flashUntil = Time.Now + FlashSeconds;
			_flash ??= Material.Load( "materials/hit_flash.vmat" );
			// every renderer of the part: the skull's jaw and a hand's forearm are children with their own, and flashed with it
			foreach ( var r in Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) ) r.MaterialOverride = _flash;
		}
		else if ( _flashUntil > 0f && Time.Now >= _flashUntil )
		{
			_flashUntil = 0f;
			foreach ( var r in Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) ) r.MaterialOverride = null;
			ApplyBoneMaterial();   // the whole-renderer flash may have cleared the per-slot bone material
		}
		if ( _renderer is SkinnedModelRenderer skinned ) skinned.Set( "hand_pose", (float)Pose );
		FollowBones();
	}
}
