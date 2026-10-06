using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Run;

public enum PortalKind
{
	/// <summary>The village portal: walking in starts a run.</summary>
	Enter,
	/// <summary>A floor's exit portal: walking in opens the home-or-continue menu.</summary>
	Exit,
}

/// <summary>
/// A portal the run loop watches. Deliberately not a physics trigger: RunManager tests the local player's feet
/// against every portal each frame (a flat cylinder, because tiers stack 98 units apart and a sphere would fire
/// from the floor above), so a portal is any model with this component and nothing else to set up. The exit
/// portals are the kits' exit props, marked by DungeonBuilder; the village one the user places by hand.
/// </summary>
[Title( "Run Portal" ), Category( "Void Mage" ), Icon( "door_sliding" )]
public sealed class RunPortal : Component
{
	[Property] public PortalKind Kind { get; set; } = PortalKind.Enter;
	/// <summary>Horizontal reach of the trigger from the portal's pivot, units.</summary>
	[Property, Range( 30f, 300f )] public float Radius { get; set; } = 90f;
	/// <summary>Vertical reach either way from the pivot, units: less than one grid level (98) so the floor above never triggers it.</summary>
	[Property, Range( 20f, 200f )] public float HalfHeight { get; set; } = 70f;

	/// <summary>Fires within this many units have their flame, smoke and sparks dragged into the portal (FireVfxRig). 0 = none.</summary>
	[Property, Range( 0f, 800f )] public float SuctionRange { get; set; } = 320f;

	public static readonly List<RunPortal> All = new();

	protected override void OnEnabled()
	{
		if ( !All.Contains( this ) ) All.Add( this );
		// every portal gets its living look at runtime (exit props are spawned by the builder, the village one is hand placed)
		if ( !Scene.IsEditor ) Components.GetOrCreate<PortalVfx>();
	}
	protected override void OnDisabled() => All.Remove( this );

	public bool Contains( Vector3 feet )
	{
		var d = feet - WorldPosition;
		if ( System.MathF.Abs( d.z ) > HalfHeight ) return false;
		return d.WithZ( 0f ).LengthSquared <= Radius * Radius;
	}
}
