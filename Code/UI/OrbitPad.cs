using System;
using Sandbox;
using Sandbox.UI;

namespace VoidMage.UI;

/// <summary>
/// A full-screen pad under the weapon tuner's window while free look is on: dragging on it orbits the camera (the tuner
/// reads :active and Mouse.Delta, the wardrobe preview's way) and the wheel zooms. Razor has no onmousewheel event, so
/// the wheel comes through this virtual.
/// </summary>
public sealed class OrbitPad : Panel
{
	/// <summary>Called with the wheel delta: positive is scrolling down.</summary>
	public Action<float> Wheel { get; set; }

	public override void OnMouseWheel( Vector2 value )
	{
		Wheel?.Invoke( value.y );
	}

	// :active must survive the mouse moving, so this is not a drag target
	public override bool WantsDrag => false;
}
