using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Combat;

/// <summary>
/// A corpse the dungeon builder placed as scenery (a Gaol prisoner slumped against the wall, a burnt body, a
/// Realms dead dwarf, a limb): the sword should meet flesh there, not stone. Most of them spawn without a collider
/// (you walk over a body on the floor), so the world sweep never finds them; instead they register here, the way
/// breakables do, and the swing judges them against the nearest point of their box. No hit points, no physics,
/// no networking: every peer builds the same list from the seed and the striker broadcasts the effect.
/// </summary>
[Title( "Flesh Prop" ), Category( "Void Mage" ), Icon( "bloodtype" )]
public sealed class FleshProp : Component
{
	private ModelRenderer _renderer;
	private BBox _bounds;
	private bool _boundsValid;

	/// <summary>The model's world box (asset data, so it is right even before the first frame draws).</summary>
	public BBox WorldBounds
	{
		get
		{
			if ( !_boundsValid )
			{
				_renderer ??= Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants );
				var model = _renderer?.Model;
				_bounds = model is null || model.IsError ? BBox.FromPositionAndSize( WorldPosition, 40f ) : model.Bounds.Transform( WorldTransform );
				_boundsValid = true;
			}
			return _bounds;
		}
	}

	public Vector3 ClosestPoint( Vector3 p )
	{
		var b = WorldBounds;
		return new Vector3( p.x.Clamp( b.Mins.x, b.Maxs.x ), p.y.Clamp( b.Mins.y, b.Maxs.y ), p.z.Clamp( b.Mins.z, b.Maxs.z ) );
	}
}

/// <summary>The flesh props of the current dungeon (cleared on a rebuild, like BreakableNet).</summary>
public static class FleshNet
{
	private static readonly List<FleshProp> _props = new();

	public static int Count => _props.Count;

	public static void Clear() => _props.Clear();

	public static void Register( FleshProp prop )
	{
		if ( prop.IsValid() ) _props.Add( prop );
	}

	/// <summary>Every flesh prop whose box comes within `radius` of `at`.</summary>
	public static IEnumerable<FleshProp> Near( Vector3 at, float radius )
	{
		var r2 = radius * radius;
		foreach ( var p in _props )
		{
			if ( !p.IsValid() ) continue;
			if ( p.ClosestPoint( at ).DistanceSquared( at ) <= r2 ) yield return p;
		}
	}
}
