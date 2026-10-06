using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// Puts the arms' pose onto the visible character: the spine_03 subtree (chest, clavicles, arms, hands,
/// fingers) is read from ClipPoseSource's hidden renderer (sword clip with the spell cast layered in by
/// the arms graph) and re-rooted on the body's own animated chest, so the legs and pelvis keep the
/// locomotion graph and the arms swing the sword and cast.
/// Runs inside CharacterBoneRig's OnPreRender, before the head look, which composes on the result.
/// The neck and head keep the body's animation by default: the clips' head motion is camera framing.
/// </summary>
[Title( "Upper Body Pose Copy" ), Category( "Void Mage" ), Icon( "accessibility_new" )]
public sealed class UpperBodyPoseCopy : Component
{
	/// <summary>How much the chest itself takes the clip's orientation (0 keeps the locomotion torso, arms re-rooted on it).</summary>
	[Property, Range( 0f, 1f )] public float ChestBlend { get; set; } = 1f;
	[Property] public bool CopyHead { get; set; } = false;

	public bool Active { get; private set; }

	private ClipPoseSource _source;

	private Model _bodyModel;
	private readonly List<BoneCollection.Bone> _bodyChain = new();
	private readonly HashSet<string> _neckNames = new();

	private Model _srcModel;
	private readonly Dictionary<string, BoneCollection.Bone> _srcByName = new();
	private BoneCollection.Bone _srcChest;

	private readonly Dictionary<int, Transform> _written = new();

	/// <summary>The world transform this writer gave a body bone this frame, if it wrote it.</summary>
	public bool TryGetWritten( BoneCollection.Bone bodyBone, out Transform world )
	{
		if ( Active && bodyBone is not null && _written.TryGetValue( bodyBone.Index, out world ) ) return true;
		world = default;
		return false;
	}

	/// <summary>Called by CharacterBoneRig every OnPreRender, before the head look.</summary>
	public void ApplyBones( CharacterBoneRig rig )
	{
		Active = false;
		_written.Clear();

		if ( !_source.IsValid() )
			_source = ClipPoseSource.Find( this );
		if ( !_source.IsValid() ) return;

		var body = rig.Body;
		var src = _source.Renderer;
		if ( !body.IsValid() || !src.IsValid() ) return;

		var bodyModel = body.Model;
		var srcModel = src.Model;
		if ( bodyModel is null || srcModel is null ) return;

		var bodySceneModel = body.SceneModel;
		var srcSceneModel = src.SceneModel;
		if ( !bodySceneModel.IsValid() || !srcSceneModel.IsValid() ) return;

		CacheBody( bodyModel );
		CacheSource( srcModel );
		if ( _bodyChain.Count == 0 || _srcChest is null ) return;

		var bodyChestBone = _bodyChain[0];
		if ( !body.TryGetBoneTransformAnimation( bodyChestBone, out var bodyChestWorld ) ) return;
		if ( !src.TryGetBoneTransformAnimation( _srcChest, out var srcChestWorld ) ) return;

		// Everything in each model's own space, so where the hidden source sits in the world is irrelevant.
		var bodyFrame = bodySceneModel.Transform;
		var srcFrame = srcSceneModel.Transform;
		var bodyChest = bodyFrame.ToLocal( bodyChestWorld );
		var srcChest = srcFrame.ToLocal( srcChestWorld );

		var outChest = new Transform( bodyChest.Position, Rotation.Slerp( bodyChest.Rotation, srcChest.Rotation, ChestBlend ) );

		foreach ( var bone in _bodyChain )
		{
			Transform rel;
			if ( !CopyHead && _neckNames.Contains( bone.Name ) )
			{
				if ( !body.TryGetBoneTransformAnimation( bone, out var world ) ) continue;
				rel = bodyChest.ToLocal( bodyFrame.ToLocal( world ) );
			}
			else
			{
				if ( !_srcByName.TryGetValue( bone.Name, out var srcBone ) ) continue;
				if ( !src.TryGetBoneTransformAnimation( srcBone, out var world ) ) continue;
				rel = srcChest.ToLocal( srcFrame.ToLocal( world ) );
			}

			var outLocal = outChest.ToWorld( rel );
			rig.SetBone( bone, outLocal );
			_written[bone.Index] = bodyFrame.ToWorld( outLocal );
		}

		Active = true;
	}

	private void CacheBody( Model model )
	{
		if ( ReferenceEquals( model, _bodyModel ) && _bodyChain.Count > 0 ) return;

		_bodyModel = model;
		BoneSubtree.Collect( model, model.Bones.GetBone( "spine_03" ), _bodyChain );

		_neckNames.Clear();
		var neck = new List<BoneCollection.Bone>();
		BoneSubtree.Collect( model, model.Bones.GetBone( "neck_01" ), neck );
		foreach ( var bone in neck ) _neckNames.Add( bone.Name );
	}

	private void CacheSource( Model model )
	{
		if ( ReferenceEquals( model, _srcModel ) && _srcByName.Count > 0 ) return;

		_srcModel = model;
		_srcByName.Clear();
		foreach ( var bone in model.Bones.AllBones )
			_srcByName[bone.Name] = bone;
		_srcByName.TryGetValue( "spine_03", out _srcChest );
	}
}
