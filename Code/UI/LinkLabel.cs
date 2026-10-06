using Sandbox.UI;

namespace VoidMage.UI;

/// <summary>
/// A web link a whole button can be. Game code cannot start a process and the engine has no OpenUrl, but a rich
/// Label does open an http(s) anchor in the user's own browser when the anchor is clicked (Label.OnClick: it looks
/// at the node under the mouse and shell-opens its href). On its own that makes only the glyphs clickable, so this
/// label is the button's text, takes no mouse itself, and Open() plays the click at the middle of its one anchor.
/// </summary>
public sealed class LinkLabel : Label
{
	private bool _opening;

	public LinkLabel()
	{
		IsRich = true;
	}

	public void SetLink( string url, string text )
	{
		Text = $"<a href=\"{url}\">{text}</a>";
	}

	/// <summary>Call from a real click on the button around this label.</summary>
	public void Open()
	{
		// the click made here bubbles to the button as an onclick of its own
		if ( _opening ) return;
		_opening = true;
		try
		{
			var e = new MousePanelEvent( "onclick", this, "mouseleft" ) { LocalPosition = Box.Rect.Size * 0.5f };
			OnMouseMove( e );
			OnClick( e );
		}
		finally
		{
			_opening = false;
		}
	}
}
