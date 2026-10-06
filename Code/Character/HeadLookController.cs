using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// Free look: the neck takes a share of the look and the head the rest, as absolute overrides on this
/// frame's animated pose. OnUpdate only smooths and syncs the look angles; the bones are written from
/// CharacterBoneRig's OnPreRender through ApplyBones, where the rig mirrors every write onto the
/// bone-merged attachments (hair, eyes, brows, hands, clothing).
/// </summary>
public sealed class HeadLookController : Component
{
	[Property] public float MaxYaw { get; set; } = 45f;
	[Property] public float MaxPitch { get; set; } = 15f;
	[Property] public float TurnRate { get; set; } = 8f;

	[Sync] private float SyncedYaw { get; set; }
	[Sync] private float SyncedPitch { get; set; }

	private float _yaw;
	private float _pitch;
	private CharacterBoneRig _rig;

	// The body's facing is the visual child's rotation (CharacterLocomotionAnimator turns it), not the
	// physics root's.
	private Rotation BodyRotation
	{
		get
		{
			if ( !_rig.IsValid() )
				_rig = Components.Get<CharacterBoneRig>( FindMode.EverythingInSelfAndDescendants );
			return _rig.IsValid() && _rig.Body.IsValid() ? _rig.Body.WorldRotation : WorldRotation;
		}
	}

	protected override void OnUpdate()
	{
		if ( IsProxy )
		{
			var proxyBlend = 1f - MathF.Exp( -15f * Time.Delta );
			_yaw = _yaw.LerpTo( SyncedYaw, proxyBlend );
			_pitch = _pitch.LerpTo( SyncedPitch, proxyBlend );
			return;
		}

		var targetLook = 0f;
		var targetPitch = 0f;

		var camera = Scene.Camera;
		if ( camera.IsValid() )
		{
			var local = camera.WorldRotation.Forward * BodyRotation.Inverse;
			var yaw = MathF.Atan2( local.y, local.x ).RadianToDegree();
			var pitch = (-MathF.Asin( local.z.Clamp( -1f, 1f ) )).RadianToDegree();

			if ( MathF.Abs( yaw ) <= 120f )
			{
				targetLook = yaw.Clamp( -MaxYaw, MaxYaw );
				targetPitch = pitch.Clamp( -MaxPitch, MaxPitch );
			}
		}

		var blend = 1f - MathF.Exp( -TurnRate * Time.Delta );
		_yaw = _yaw.LerpTo( targetLook, blend );
		_pitch = _pitch.LerpTo( targetPitch, blend );

		SyncedYaw = QuantizeAngle( _yaw );
		SyncedPitch = QuantizeAngle( _pitch );
	}

	private static float QuantizeAngle( float value )
	{
		if ( MathF.Abs( value ) < 0.1f ) return 0f;
		return MathF.Round( value * 2f ) / 2f;
	}

	private Model _cachedModel;
	private BoneCollection.Bone _neck;
	private BoneCollection.Bone _head;
	private readonly List<BoneCollection.Bone> _headSubtree = new();

	private void CacheBones( Model model )
	{
		if ( ReferenceEquals( model, _cachedModel ) ) return;

		_cachedModel = model;
		_neck = model.Bones.GetBone( "neck_01" );
		_head = model.Bones.GetBone( "head" );
		BoneSubtree.Collect( model, _head, _headSubtree );
	}

	/// <summary>Called by CharacterBoneRig every OnPreRender. Reads are this frame's animation pose.</summary>
	public void ApplyBones( CharacterBoneRig rig )
	{
		if ( MathF.Abs( _yaw ) < 0.1f && MathF.Abs( _pitch ) < 0.1f ) return;

		var renderer = rig.Body;
		if ( !renderer.IsValid() ) return;

		var model = renderer.Model;
		if ( model is null ) return;

		var sceneModel = renderer.SceneModel;
		if ( !sceneModel.IsValid() ) return;

		CacheBones( model );
		if ( _head is null ) return;

		// Reads go through the rig so the look composes on the sword pose copy when it wrote the chest.
		if ( !rig.TryGetPose( _head, out var headWorld ) ) return;

		var animFrame = sceneModel.Transform;
		var toModel = animFrame.Rotation.Inverse;
		var body = renderer.WorldRotation;

		var neckDelta = toModel * body * Rotation.From( _pitch * 0.3f, _yaw * 0.3f, 0f ) * body.Inverse * animFrame.Rotation;
		var fullDelta = toModel * body * Rotation.From( _pitch, _yaw, 0f ) * body.Inverse * animFrame.Rotation;

		var headLocal = animFrame.ToLocal( headWorld );
		var headPos = headLocal.Position;

		if ( _neck is not null && rig.TryGetPose( _neck, out var neckWorld ) )
		{
			var neckLocal = animFrame.ToLocal( neckWorld );
			rig.SetBone( _neck, neckLocal.WithRotation( neckDelta * neckLocal.Rotation ) );

			// The head orbits the neck pivot with the neck's share of the turn.
			headPos = neckLocal.Position + neckDelta * (headLocal.Position - neckLocal.Position);
		}

		// The head and everything under it, rigid about the (moved) head pivot.
		foreach ( var bone in _headSubtree )
		{
			if ( !rig.TryGetPose( bone, out var world ) ) continue;

			var local = animFrame.ToLocal( world );
			rig.SetBone( bone, local
				.WithPosition( headPos + fullDelta * (local.Position - headLocal.Position) )
				.WithRotation( fullDelta * local.Rotation ) );
		}
	}
}
