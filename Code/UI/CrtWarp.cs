using System.Linq;
using Sandbox;
using Sandbox.UI;
using VoidMage.Rendering;

namespace VoidMage.UI;

/// <summary>
/// Where the player is really pointing on a HUD the CRT tube has bent. With RetroFilter's Over HUD = Curvature the
/// finished frame, interface included, is warped after the UI is drawn: the screen pixel at uv shows the UI point
/// Curve(uv) (voidmage_retro.shader, the same two lines as here). The engine hit-tests the unbent layout, so under a
/// bent HUD its hover and click land beside what the player sees, by more toward the edges. A panel that wants to
/// be clicked where it is SEEN tests its own rects against Seen().
/// </summary>
public static class CrtWarp
{
	private static RetroFilter _retro;

	/// <summary>The tube's curvature acting on the HUD right now; 0 when the HUD is drawn flat.</summary>
	public static float HudCurvature( Scene scene )
	{
		if ( !_retro.IsValid() || _retro.Scene != scene ) _retro = scene?.GetAllComponents<RetroFilter>().FirstOrDefault();
		if ( !_retro.IsValid() || !_retro.Enabled || !_retro.Crt || _retro.OverHud != RetroHudCoverage.Curvature ) return 0f;
		return _retro.Curvature;
	}

	/// <summary>The shader's Curve(): output uv to the source uv it shows.</summary>
	public static Vector2 Curve( Vector2 uv, float curvature )
	{
		var p = uv * 2f - 1f;
		var q = new Vector2( p.x * (1f + curvature * p.y * p.y), p.y * (1f + curvature * p.x * p.x) );
		return q * 0.5f + 0.5f;
	}

	/// <summary>The point of `root` (a full-screen panel), in the same screen pixels as Panel.Box.Rect, that the tube shows under the cursor.</summary>
	public static Vector2 Seen( Scene scene, Panel root )
	{
		var rect = root.Box.Rect;
		var mouse = root.MousePosition;
		var k = HudCurvature( scene );
		if ( k <= 0f || rect.Width <= 0f || rect.Height <= 0f ) return rect.Position + mouse;
		var uv = Curve( new Vector2( mouse.x / rect.Width, mouse.y / rect.Height ), k );
		return rect.Position + new Vector2( uv.x * rect.Width, uv.y * rect.Height );
	}
}
