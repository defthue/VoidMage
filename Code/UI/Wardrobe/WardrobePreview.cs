using System;
using Sandbox;
using Sandbox.UI;

namespace VoidMage.UI;

/// <summary>
/// The wardrobe's preview picture. A plain panel whose background is the stage's render target, with the
/// one thing razor cannot express: the mouse wheel (there is no onmousewheel event; the wheel is a virtual
/// on Panel that otherwise scrolls the parent).
/// </summary>
public sealed class WardrobePreview : Panel
{
	/// <summary>Called with the wheel delta: positive is scrolling down.</summary>
	public Action<float> Wheel { get; set; }

	public override void OnMouseWheel( Vector2 value )
	{
		Wheel?.Invoke( value.y );
	}

	// The figure turns while this panel holds :active (see Wardrobe.OnUpdate). Declaring it a drag target
	// would make the engine drop :active as soon as the mouse moves, so it must stay false.
	public override bool WantsDrag => false;
}
