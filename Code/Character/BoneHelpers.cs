using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// Collects a bone and every bone under it. Walks Parent links against the whole bone list instead
/// of needing a Children API, so it survives a model swap; callers cache the result per model.
/// </summary>
public static class BoneSubtree
{
	public static void Collect( Model model, BoneCollection.Bone root, List<BoneCollection.Bone> into )
	{
		into.Clear();
		if ( model is null || root is null ) return;

		into.Add( root );

		foreach ( var bone in model.Bones.AllBones )
		{
			for ( var parent = bone.Parent; parent is not null; parent = parent.Parent )
			{
				if ( parent != root ) continue;

				into.Add( bone );
				break;
			}
		}
	}
}

/// <summary>
/// A per-component cache of bone handles resolved by name. Model.Bones.GetBone is a name lookup the
/// engine performs from scratch on every call. Keyed on the model reference, so swapping the model
/// invalidates the whole cache. Missing bones cache as null too.
/// </summary>
public sealed class BoneCache
{
	private Model _model;
	private readonly Dictionary<string, BoneCollection.Bone> _bones = new();

	public BoneCollection.Bone Get( Model model, string name )
	{
		if ( model is null || string.IsNullOrEmpty( name ) ) return null;

		if ( !ReferenceEquals( model, _model ) )
		{
			_model = model;
			_bones.Clear();
		}

		if ( _bones.TryGetValue( name, out var bone ) ) return bone;

		bone = model.Bones.GetBone( name );
		_bones[name] = bone;
		return bone;
	}
}
