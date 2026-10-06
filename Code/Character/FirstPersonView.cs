using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// For the local player in first person: the body (and every bone-merged piece) renders shadows only
/// so it never clips the camera. What the player sees of themselves is FirstPersonArms, a separate
/// arms-only rig placed on the camera. Third person (the PlayerController's view toggle) shows the
/// full body again. Dead, the body's renderers are DISABLED: the corpse (PlayerHealth) is what shows, and a
/// shadows-only body would still throw a standing shadow next to it. (ShadowRenderType.Off is "no shadows", not
/// "not drawn": switching to it left the body standing at the death spot beside its own ragdoll.) Only the
/// renderers that were on are switched off, so a clothing region CharacterAppearance hid stays hidden on respawn.
/// </summary>
[Title( "First Person View" ), Category( "Void Mage" ), Icon( "visibility" )]
public sealed class FirstPersonView : Component
{
	private PlayerController _controller;
	private PlayerHealth _health;
	private SkinnedModelRenderer _body;
	private bool _firstPersonApplied;
	private bool _deadApplied;
	private float _nextScan;
	private readonly System.Collections.Generic.List<SkinnedModelRenderer> _hiddenDead = new();

	protected override void OnStart()
	{
		if ( IsProxy ) return;

		// The editor rewrites the prefab on its own save, so don't trust MouseLock to survive in it.
		Components.GetOrCreate<UI.MouseLock>();
		// the close, wall-aware third-person camera lives in code for the same reason
		Components.GetOrCreate<ThirdPersonCamera>();
		// and the camera that rides the ragdoll when the player dies
		Components.GetOrCreate<DeathCamera>();
	}

	/// <summary>
	/// The clone's first frame: a player spawned mid-frame (the menu's arrival) rendered its third-person body once,
	/// from inside the head, before the first OnUpdate could hide it (a flash in the middle of the screen as the arms
	/// came up). The hide runs the moment the component is enabled, before anything is drawn.
	/// </summary>
	protected override void OnEnabled() => Refresh();

	protected override void OnUpdate() => Refresh();

	private void Refresh()
	{
		if ( IsProxy ) return;

		if ( !_controller.IsValid() )
			_controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
		if ( !_health.IsValid() )
			_health = Components.Get<PlayerHealth>( FindMode.EverythingInSelfAndAncestors );

		if ( !_body.IsValid() )
		{
			foreach ( var r in Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			{
				if ( r.BoneMergeTarget is not null ) continue;
				if ( r.GameObject.Tags.Has( "viewmodel" ) || r.GameObject.Tags.Has( "posesource" ) ) continue;
				_body = r; break;
			}
		}
		if ( !_body.IsValid() ) return;

		var firstPerson = _controller.IsValid() && !_controller.ThirdPerson;
		var dead = _health.IsValid() && _health.IsDead;

		// Newly dressed pieces need the shadows-only treatment too, so rescan while in first person. A death or a
		// respawn applies the same frame (a standing body clipping the camera for half a second reads as a glitch).
		if ( firstPerson == _firstPersonApplied && dead == _deadApplied && Time.Now < _nextScan ) return;
		_firstPersonApplied = firstPerson;
		_deadApplied = dead;
		_nextScan = Time.Now + 0.5f;

		if ( dead )
		{
			HideDead( _body, _hiddenDead );
			return;
		}
		ShowDead( _hiddenDead );
		var type = firstPerson ? ModelRenderer.ShadowRenderType.ShadowsOnly : ModelRenderer.ShadowRenderType.On;
		foreach ( var r in _body.GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( r.GameObject.Tags.Has( "viewmodel" ) || r.GameObject.Tags.Has( "posesource" ) ) continue;
			r.RenderType = type;
		}
	}

	/// <summary>Switch off every renderer of the body that is on (the arms' pose source and the viewmodel are not the body), remembering which.</summary>
	internal static void HideDead( SkinnedModelRenderer body, System.Collections.Generic.List<SkinnedModelRenderer> hidden )
	{
		foreach ( var r in body.GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( !r.Enabled || r.GameObject.Tags.Has( "viewmodel" ) || r.GameObject.Tags.Has( "posesource" ) ) continue;
			r.Enabled = false;
			hidden.Add( r );
		}
	}

	/// <summary>Back on: only what HideDead switched off.</summary>
	internal static void ShowDead( System.Collections.Generic.List<SkinnedModelRenderer> hidden )
	{
		foreach ( var r in hidden ) if ( r.IsValid() ) r.Enabled = true;
		hidden.Clear();
	}
}
