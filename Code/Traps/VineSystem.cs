using Sandbox;
using VoidMage.Dungeon;

namespace VoidMage.Traps;

/// <summary>
/// Drives every vine curtain from one place each frame, the TrapSystem model: one cached rescan of the
/// bodies on the shared half-second cadence, then each curtain's own step. Costs nothing in a scene with no
/// breaches, which is every stone floor. `vine_debug 1` draws the volumes.
/// </summary>
public sealed class VineSystem : GameObjectSystem
{
	private readonly TrapSystem.Targets _targets = new();
	private float _nextScan = float.NegativeInfinity;

	public VineSystem( Scene scene ) : base( scene )
	{
		// same stage and order as the traps: after movement, so feet are where the player actually ended up
		Listen( Stage.FinishUpdate, 2, Tick, "Vines" );
	}

	private void Tick()
	{
		if ( Scene.IsEditor ) return;
		var curtains = VineNet.All;
		if ( curtains.Count == 0 ) return;
		float now = Time.Now;
		if ( now >= _nextScan )
		{
			_nextScan = now + BodyScan.Interval;
			BodyScan.Rescan( Scene, _targets.Players, _targets.Skeletons );
		}
		foreach ( var curtain in curtains )
		{
			// Clear() destroys the dungeon's children but a stale entry can outlive it by a frame
			if ( !curtain.IsValid() ) continue;
			curtain.Step( now, _targets );
			curtain.Prune( now );
			// the box is in the vine's own frame, so it has to be drawn in it too or it lands axis-aligned at the
			// scene origin
			if ( VineNet.Debug ) Scene.DebugOverlay.Box( curtain.Hurt, Color.Green.WithAlpha( 0.5f ), transform: curtain.Frame );
		}
	}
}
