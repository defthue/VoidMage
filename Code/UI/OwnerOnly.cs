using Sandbox;

namespace VoidMage.UI;

/// <summary>Turns this object off on every client that does not own it: the HUD belongs to the local player only.</summary>
[Title( "Owner Only" ), Category( "Void Mage" ), Icon( "person" )]
public sealed class OwnerOnly : Component
{
	protected override void OnStart()
	{
		if ( IsProxy ) GameObject.Enabled = false;
	}
}
