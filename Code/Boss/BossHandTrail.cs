using System;
using Sandbox;

namespace VoidMage.Boss;

/// <summary>
/// A huge hand flying into a strike: the fly-by woosh (the user's clip: wind tearing past), started by the brain on
/// every peer <see cref="Lead"/> seconds before the hand's strike stage begins (the clip's wind builds up, so it
/// peaks as the hand passes), once per move. The sound RIDES THE HAND while it plays, so it passes the listener
/// as the hand does. Nothing else: no smoke (smoke belongs to impacts), nothing networked.
/// </summary>
[Title( "Boss Hand Trail" ), Category( "Void Mage" ), Icon( "air" )]
public sealed class BossHandTrail : Component
{
	/// <summary>How long before a hand's strike stage its fly-by woosh starts, seconds.</summary>
	[ConVar( "boss_woosh_lead", ConVarFlags.Saved )] public static float Lead { get; set; } = 0.25f;

	private BossPart _part;
	private SoundHandle _woosh;

	protected override void OnStart()
	{
		_part = Components.Get<BossPart>();
	}

	/// <summary>Start the fly-by on this hand (a running one is cut: one woosh per hand at a time).</summary>
	public void Woosh()
	{
		if ( !_part.IsValid() || _part.Dead ) return;
		_woosh?.Stop();
		_woosh = Audio.GameAudio.Play( "sounds/flybywoosh.sound", _part.PivotWorld );
	}

	protected override void OnUpdate()
	{
		// the woosh flies with the hand
		if ( !_woosh.IsValid() ) return;
		if ( _woosh.IsPlaying ) _woosh.Position = _part.IsValid() ? _part.PivotWorld : WorldPosition;
		else _woosh = null;
	}

	protected override void OnDestroy()
	{
		_woosh?.Stop();
		_woosh = null;
	}
}
