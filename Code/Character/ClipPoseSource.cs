using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The networked "what are this character's arms doing" state, and the hidden renderer on every peer
/// that turns it into a pose. The arms model (charactertoon/human_fp_arms.vmdl) carries an animgraph
/// (animgraphs/human_fp_arms.vanmgrph, tools/gen_fp_arms_animgraph.py) built like heden's: a gait
/// selector under a Direct Playback node for the sword clips, and heden's fireball state machine
/// (Attack6 begin/loop/end on `channel_hold`) masked onto the staff arm and faded by `action_weight`.
///
/// The owner decides (FirstPersonArms picks the sword clip, SpellCaster the cast); everything is
/// [Sync]. <see cref="Drive"/> applies the whole state to any renderer of that model, so the hidden
/// renderer here and the owner's first-person arms are always the same pose. UpperBodyPoseCopy copies
/// this renderer's chest and arms onto the visible body, which is how everyone else sees both.
/// Late joiners seek one-shots to the right frame through DirectPlayback.StartTime.
/// </summary>
[Title( "Clip Pose Source" ), Category( "Void Mage" ), Icon( "sync" )]
public sealed class ClipPoseSource : Component
{
	/// <summary>Model carrying the clips and the graph; the arms model by default.</summary>
	[Property] public Model ClipModel { get; set; }
	/// <summary>heden's action-layer damping: how fast the cast fades in and out (per second).</summary>
	[Property] public float ActionDamping { get; set; } = 15f;

	// The looping clips live in the graph's gait selector, in this order (gen_fp_arms_animgraph.py).
	public static readonly string[] GaitClips =
	{
		"anim_idle_breathing", "anim_walk", "anim_run", "anim_sprint_loop", "anim_jump_loop",
	};
	public static int GaitIndex( string clip ) => Array.IndexOf( GaitClips, clip );

	// One-shot sword clip through the Direct Playback node.
	[Sync] public string Clip { get; set; } = "";
	[Sync] public float StartTime { get; set; }
	[Sync] public int Version { get; set; }
	/// <summary>Playback speed of the one-shot (attack speed and drink speed boons). The gait loops always run at 1.</summary>
	[Sync] public float Rate { get; set; } = 1f;
	// Looping base clip.
	[Sync] public int Gait { get; set; }
	// The cast: hold up = raise and hold, hold down = throw, abort count change = bail out.
	[Sync] public bool ChannelHold { get; set; }
	[Sync] public int AbortCount { get; set; }
	/// <summary>What the cast layer fades toward (0, or the caster's CastWeight).</summary>
	[Sync] public float ActionTarget { get; set; }

	public SkinnedModelRenderer Renderer { get; private set; }
	public bool HasClip => !string.IsNullOrEmpty( Clip );
	/// <summary>Hold the animation still where it is (potion grip tuning): the hidden pose source and the first-person rig both stop.</summary>
	public bool Frozen { get; set; }
	/// <summary>The smoothed cast layer weight this frame.</summary>
	public float ActionWeight { get; private set; }

	/// <summary>The one-shot has run its length (never true in the first frames after it started).</summary>
	public bool IsFinished
	{
		get
		{
			if ( !HasClip ) return true;
			if ( Time.Now - StartTime < 0.05f ) return false;
			var dp = Playback( Renderer );
			if ( dp is null ) return false;
			if ( string.IsNullOrEmpty( dp.Name ) ) return true;
			var duration = dp.Duration;
			return duration > 0f && dp.Time >= duration - 0.001f;
		}
	}

	public float ClipTime => Playback( Renderer )?.Time ?? 0f;

	/// <summary>
	/// Seconds of the one-shot's own timeline since it started (wall time scaled by Rate). Every frame-timed effect
	/// (the swipe, the slash crescent, the head sway, the drink's swallow) reads this, so a faster swing keeps its
	/// hit, its trail and its sound on the same frames.
	/// </summary>
	public float Elapsed => (Time.Now - StartTime) * Rate;

	/// <summary>The rate a renderer of this state plays at right now: the one-shot's while one runs, 1 for the gait.</summary>
	public float RendererRate => HasClip ? Rate : 1f;

	private GameObject _holder;
	private bool _flagsApplied;
	private readonly Dictionary<SkinnedModelRenderer, int> _appliedVersion = new();
	private readonly List<SkinnedModelRenderer> _stale = new();
	private int _seenAbort;
	private bool _abortPulse;
	private bool _abortSeeded;

	// heden keeps the layer weight off exact 0 and 1: the blend node would otherwise stop evaluating
	// (and resetting) the cast state machine while it is fully out.
	private const float WeightFloor = 0.005f;
	private const float WeightCeiling = 0.995f;

	public static ClipPoseSource Find( Component from )
		=> from.Components.Get<ClipPoseSource>( FindMode.EverythingInSelfAndAncestors );

	// ------------------------------------------------------------------ owner

	/// <summary>Owner only: play a one-shot sword clip now (crossfades from whatever plays).</summary>
	public void PlayOneShot( string clip, float rate = 1f )
	{
		if ( IsProxy ) return;
		Clip = clip;
		StartTime = Time.Now;
		Rate = MathF.Max( 0.05f, rate );
		Version++;
	}

	/// <summary>Owner only: the looping base clip; a running one-shot is cancelled so the gait shows.</summary>
	public void SetGait( int index )
	{
		if ( IsProxy ) return;
		Gait = Math.Clamp( index, 0, GaitClips.Length - 1 );
		if ( HasClip ) Stop();
	}

	/// <summary>Owner only: stop the one-shot (blends back to the gait).</summary>
	public void Stop()
	{
		if ( IsProxy || !HasClip ) return;
		Clip = "";
		Version++;
	}

	// ------------------------------------------------------------------ every peer

	protected override void OnDisabled()
	{
		if ( _holder.IsValid() ) _holder.Destroy();
		_holder = null;
		Renderer = null;
		_appliedVersion.Clear();
		_flagsApplied = false;
	}

	protected override void OnUpdate()
	{
		EnsureRenderer();

		// An abort is a one-frame pulse on every peer, raised from the synced counter.
		if ( !_abortSeeded ) { _abortSeeded = true; _seenAbort = AbortCount; }
		_abortPulse = _seenAbort != AbortCount;
		_seenAbort = AbortCount;

		var blend = 1f - MathF.Exp( -MathF.Max( 0f, ActionDamping ) * Time.Delta );
		ActionWeight = ActionWeight.LerpTo( ActionTarget.Clamp( 0f, 1f ), blend );

		if ( !Renderer.IsValid() ) return;
		Renderer.PlaybackRate = Frozen ? 0f : RendererRate;

		if ( !_flagsApplied )
		{
			var so = Renderer.SceneObject;
			if ( so.IsValid() )
			{
				so.Flags.CastShadows = false;
				_flagsApplied = true;
			}
		}

		Drive( Renderer );
	}

	/// <summary>Applies the whole state to a renderer of the arms model: graph parameters and the one-shot.</summary>
	public void Drive( SkinnedModelRenderer renderer )
	{
		if ( !renderer.IsValid() ) return;

		renderer.Set( "gait", Gait );
		renderer.Set( "channel_hold", ChannelHold );
		renderer.Set( "channel_abort", _abortPulse );
		renderer.Set( "action_weight", ActionWeight.Clamp( WeightFloor, WeightCeiling ) );

		var dp = Playback( renderer );
		if ( dp is null ) return;

		if ( _appliedVersion.TryGetValue( renderer, out var applied ) && applied == Version ) return;
		_appliedVersion[renderer] = Version;
		PruneApplied();

		if ( !HasClip )
		{
			dp.Cancel();
			return;
		}

		dp.Play( Clip );

		// A renderer picking the clip up late (a joiner, the first-person rig re-shown) seeks to the frame
		// everyone else is on; the owner's fresh play is left alone.
		if ( Time.Now - StartTime > 0.1f )
			dp.StartTime = StartTime;
	}

	private static AnimGraphDirectPlayback Playback( SkinnedModelRenderer renderer )
	{
		if ( !renderer.IsValid() || !renderer.SceneModel.IsValid() ) return null;
		return renderer.SceneModel.DirectPlayback;
	}

	private void PruneApplied()
	{
		_stale.Clear();
		foreach ( var key in _appliedVersion.Keys )
			if ( !key.IsValid() ) _stale.Add( key );
		foreach ( var key in _stale ) _appliedVersion.Remove( key );
	}

	// Sibling of the visual body, never under it: FirstPersonView and CharacterAppearance walk the body's
	// descendants and must not pick this up. Shadows-only with shadow casting off keeps the SceneModel
	// animating while nothing of it ever draws.
	private void EnsureRenderer()
	{
		if ( Renderer.IsValid() ) return;

		var model = ClipModel ?? Model.Load( "charactertoon/human_fp_arms.vmdl" );
		if ( model is null || model.IsError ) return;

		_holder = new GameObject( true, "pose_source" )
		{
			Parent = GameObject,
			Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked,
		};
		_holder.Tags.Add( "posesource" );

		Renderer = _holder.Components.Create<SkinnedModelRenderer>();
		Renderer.Model = model;
		Renderer.UseAnimGraph = true;
		Renderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		Renderer.PlaybackRate = 1f;
		_flagsApplied = false;
	}
}
