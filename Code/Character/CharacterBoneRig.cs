using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The one place the character's bones get written procedurally, and the reason hair, eyes, brows,
/// first-person hands and clothing stay glued to the body through every animation.
///
/// The engine's frame is Update -> UpdateBones (the body animates, then every bone-merged attachment
/// copies its bones by name) -> OnPreRender -> render. A bone read in OnUpdate is LAST frame's pose,
/// and SetBoneTransform is a persistent override stamped on during the NEXT UpdateBones. So the rig
/// runs the writers itself, in one OnPreRender, on this frame's animation, and every write goes
/// through SetBone, which writes the FINAL world-space render bone (SceneModel.SetBoneWorldTransform)
/// to the body and to every attachment that has a bone of that name.
/// </summary>
public sealed class CharacterBoneRig : Component
{
	[Property] public SkinnedModelRenderer Body { get; set; }

	private HeadLookController _headLook;
	private UpperBodyPoseCopy _poseCopy;
	private readonly List<HandPropFollower> _props = new();
	private float _nextPropScan;

	private readonly List<SkinnedModelRenderer> _attachments = new();
	private readonly Dictionary<SkinnedModelRenderer, BoneCache> _attachmentBones = new();
	private readonly List<SkinnedModelRenderer> _stale = new();

	private bool _clearedLegacyOverrides;

	// The attachment list is rebuilt on demand, not per frame. Spawners (CharacterAppearance, the
	// first-person hands) call NotifyAttachmentsChanged; the slow rescan is a safety net.
	private bool _attachmentsDirty = true;
	private float _nextAttachmentScan;
	private const float AttachmentRescanSeconds = 1f;

	public void InvalidateAttachments() => _attachmentsDirty = true;

	/// <summary>Call after adding, removing, enabling or disabling a bone-merged renderer anywhere under a character.</summary>
	public static void NotifyAttachmentsChanged( GameObject near )
	{
		if ( !near.IsValid() ) return;
		near.Components.Get<CharacterBoneRig>( FindMode.EverythingInSelfAndAncestors )?.InvalidateAttachments();
	}

	protected override void OnEnabled()
	{
		_attachmentsDirty = true;
	}

	protected override void OnDisabled()
	{
		_clearedLegacyOverrides = false;
	}

	protected override void OnPreRender()
	{
		if ( !Body.IsValid() )
		{
			foreach ( var renderer in Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			{
				if ( renderer.BoneMergeTarget is not null ) continue;
				if ( renderer.GameObject.Tags.Has( "viewmodel" ) || renderer.GameObject.Tags.Has( "posesource" ) ) continue;
				Body = renderer;
				break;
			}
		}

		if ( !Body.IsValid() ) return;

		if ( _attachmentsDirty || Time.Now >= _nextAttachmentScan )
			RefreshAttachments();

		if ( !_clearedLegacyOverrides )
		{
			_clearedLegacyOverrides = true;
			ClearAll();
		}

		if ( !_headLook.IsValid() )
			_headLook = Components.Get<HeadLookController>( FindMode.EverythingInSelfAndDescendants );
		if ( !_poseCopy.IsValid() )
			_poseCopy = Components.Get<UpperBodyPoseCopy>( FindMode.EverythingInSelfAndDescendants );
		if ( Time.Now >= _nextPropScan )
		{
			_nextPropScan = Time.Now + 1f;
			_props.Clear();
			_props.AddRange( Components.GetAll<HandPropFollower>( FindMode.EverythingInSelfAndDescendants ) );
		}

		// Fixed order: the arms pose (sword clip with the cast layered in by the arms graph) first, the
		// head look composes on it, the hand props read the result.
		if ( _poseCopy.IsValid() && _poseCopy.Enabled )
			_poseCopy.ApplyBones( this );

		if ( _headLook.IsValid() && _headLook.Enabled )
			_headLook.ApplyBones( this );

		foreach ( var prop in _props )
			if ( prop.IsValid() && prop.Enabled ) prop.Follow( this );

		// the potion stream hangs between the bottle just placed and the mouth
		if ( !_pour.IsValid() && Time.Now >= _nextPourScan )
		{
			_nextPourScan = Time.Now + 1f;
			_pour = Components.Get<PotionPour>( FindMode.EverythingInSelfAndDescendants );
		}
		if ( _pour.IsValid() && _pour.Enabled ) _pour.Build( this );
	}

	private PotionPour _pour;
	private float _nextPourScan;

	/// <summary>
	/// This frame's pose of a body bone in world space: what the upper body copy gave that bone, else
	/// the raw animation. Downstream writers and followers read through this so they compose.
	/// </summary>
	public bool TryGetPose( BoneCollection.Bone bodyBone, out Transform world )
	{
		if ( _poseCopy.IsValid() && _poseCopy.TryGetWritten( bodyBone, out world ) ) return true;

		if ( Body.IsValid() && bodyBone is not null )
			return Body.TryGetBoneTransformAnimation( bodyBone, out world );

		world = default;
		return false;
	}

	/// <summary>
	/// Writes this frame's final render bone, given in the body's SceneModel-local space, to the body
	/// and to every attachment carrying a bone of the same name.
	/// </summary>
	public void SetBone( BoneCollection.Bone bodyBone, Transform local )
	{
		if ( bodyBone is null || !Body.IsValid() ) return;

		var bodyModel = Body.SceneModel;
		if ( !bodyModel.IsValid() ) return;

		var world = bodyModel.Transform.ToWorld( local );
		bodyModel.SetBoneWorldTransform( bodyBone.Index, world );

		foreach ( var attachment in _attachments )
		{
			if ( !attachment.IsValid() || !attachment.Active || attachment.Model is null )
			{
				_attachmentsDirty = true;
				continue;
			}

			var attachmentModel = attachment.SceneModel;
			if ( !attachmentModel.IsValid() ) continue;

			if ( !_attachmentBones.TryGetValue( attachment, out var cache ) )
			{
				cache = new BoneCache();
				_attachmentBones[attachment] = cache;
			}

			var bone = cache.Get( attachment.Model, bodyBone.Name );
			if ( bone is not null )
				attachmentModel.SetBoneWorldTransform( bone.Index, world );
		}
	}

	private void RefreshAttachments()
	{
		_attachmentsDirty = false;
		_nextAttachmentScan = Time.Now + AttachmentRescanSeconds;
		_attachments.Clear();

		foreach ( var renderer in Body.GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EnabledInSelfAndDescendants ) )
		{
			if ( renderer == Body ) continue;
			if ( renderer.BoneMergeTarget != Body ) continue;
			_attachments.Add( renderer );
		}

		if ( _attachmentBones.Count == 0 ) return;

		_stale.Clear();
		foreach ( var key in _attachmentBones.Keys )
			if ( !key.IsValid() ) _stale.Add( key );

		foreach ( var key in _stale )
			_attachmentBones.Remove( key );
	}

	private void ClearAll()
	{
		if ( Body.IsValid() )
			Body.ClearPhysicsBones();

		foreach ( var attachment in _attachments )
			if ( attachment.IsValid() ) attachment.ClearPhysicsBones();
	}
}
