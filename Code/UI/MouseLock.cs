using Sandbox;

namespace VoidMage.UI;

/// <summary>
/// Keeps the cursor locked to the game window. Mouse.Visibility defaults to Auto, which hands the
/// cursor back the moment the game view thinks UI wants it — in the editor that means a click frees
/// the cursor and the look angles stop at the edge of the screen until you click again. Hidden locks
/// the mouse to the game, so the look input is a raw delta with no screen bounds. Escape still works:
/// the pause menu runs outside the scene and brings its own cursor.
///
/// Set <see cref="Release"/> from a menu or inventory panel when a real cursor is needed.
/// </summary>
[Title( "Mouse Lock" ), Category( "Void Mage" ), Icon( "mouse" )]
public sealed class MouseLock : Component
{
	/// <summary>Set true while a UI panel needs a usable cursor.</summary>
	public static bool Release { get; set; }

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		Mouse.Visibility = Release ? MouseVisibility.Visible : MouseVisibility.Hidden;
	}

	protected override void OnDisabled()
	{
		Mouse.Visibility = MouseVisibility.Auto;
	}
}
