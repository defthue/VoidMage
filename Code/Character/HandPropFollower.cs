using System.Linq;
using Sandbox;

namespace VoidMage.Character;

public enum HandSide { Left, Right }

/// <summary>What a follower carries: the hand's weapon, or the potion bottle of a drink.</summary>
public enum HandPropRole { Weapon, Potion }

/// <summary>
/// A prop everyone else sees in a hand (the sword, the staff, a potion being drunk): an unparented model
/// placed from the body's hand joints every frame after every bone writer has run (CharacterBoneRig calls
/// Follow last). Bone objects would not do: they are built from the animation before the rig writes. Reads
/// the grip settings of the matching FirstPersonArms prop when one is present, so a grip tuned in first
/// person is right in third person too.
///
/// A drink swaps what the right hand holds on every peer, off the drinker's synced state: the weapon
/// follower hides for the whole clip and the potion follower shows the bottle until the flick throws it.
/// </summary>
[Title( "Hand Prop Follower" ), Category( "Void Mage" ), Icon( "gavel" )]
public sealed class HandPropFollower : Component
{
	[Property] public HandSide Hand { get; set; } = HandSide.Left;
	/// <summary>Weapon: the hand's sword or staff. Potion: the bottle, only while a drink holds one.</summary>
	[Property] public HandPropRole Role { get; set; } = HandPropRole.Weapon;
	[Property] public Model PropModel { get; set; }
	[Property] public Vector3 GripOffset { get; set; } = new( 1.6f, 1.0f, 3.5f );
	[Property] public Angles RotationOffset { get; set; }
	[Property] public bool FlipPalm { get; set; }

	/// <summary>This frame's prop transform in the world while it shows (the staff tip for spells hangs off it, the bottle's throw leaves from it).</summary>
	public Transform? PropWorld { get; private set; }

	private ClipPoseSource _source;
	private FirstPersonArms _arms;
	private PotionDrinker _drinker;
	private GameObject _prop;
	private ModelRenderer _renderer;
	private readonly BoneCache _bones = new();

	protected override void OnDisabled()
	{
		if ( _prop.IsValid() ) _prop.Destroy();
		_prop = null;
		_renderer = null;
		PropWorld = null;
	}

	/// <summary>This hand's grip joint names (fixed strings: the lookups run every frame the props draw).</summary>
	private string[] Joints => FirstPersonArms.GripJoints( Hand );

	/// <summary>Called by CharacterBoneRig every OnPreRender after every bone writer.</summary>
	public void Follow( CharacterBoneRig rig )
	{
		if ( !_source.IsValid() )
			_source = ClipPoseSource.Find( this );
		if ( !_arms.IsValid() )
			_arms = Components.Get<FirstPersonArms>( FindMode.EverythingInSelfAndAncestors );
		if ( !_drinker.IsValid() )
			_drinker = Components.Get<PotionDrinker>( FindMode.EverythingInSelfAndAncestors );

		// A drink owns the right hand on every peer (the state is synced, so proxies swap too)
		var drinking = _drinker.IsValid() && _drinker.DrinkKind != 0;
		var model = ModelFor( drinking );

		// The arms graph always has a pose, so the props show whenever the arms are driven at all.
		var body = rig.Body;
		var visible = model is not null && !model.IsError && _source.IsValid() && _source.Renderer.IsValid() && body.IsValid() && body.Model is not null;
		var j = Joints;
		if ( visible
			&& Joint( rig, body.Model, j[0], out var hand ) && Joint( rig, body.Model, j[1], out var index )
			&& Joint( rig, body.Model, j[2], out var pinky ) && Joint( rig, body.Model, j[3], out var middle )
			&& Joint( rig, body.Model, j[4], out var thumb )
			&& EnsureProp( model ) )
		{
			var settings = Settings();
			var grip = SwordGrip.Compute( hand, index, pinky, middle, thumb, settings.Offset, settings.Rotation, settings.FlipPalm );

			if ( !_prop.Enabled ) _prop.Enabled = true;
			_prop.WorldTransform = settings.Seat( grip );
			PropWorld = grip;

			// The owner's body is shadows-only in first person; the props go with it.
			if ( _renderer.RenderType != body.RenderType )
				_renderer.RenderType = body.RenderType;
			return;
		}

		PropWorld = null;
		if ( _prop.IsValid() && _prop.Enabled ) _prop.Enabled = false;
	}

	/// <summary>What this follower should be holding right now, or null for nothing.</summary>
	private Model ModelFor( bool drinking )
	{
		if ( Role == HandPropRole.Potion )
			return _drinker.IsValid() && _drinker.BottleInHand ? _drinker.BottleModel : null;
		// the drinking hand puts its weapon away for the whole clip
		if ( drinking && Hand == HandSide.Right ) return null;
		return PropModel ?? (_arms.IsValid() ? _arms.ModelFor( Hand ) : null);
	}

	private FirstPersonArms.GripSettings Settings()
	{
		if ( !_arms.IsValid() ) return new FirstPersonArms.GripSettings( GripOffset, RotationOffset, FlipPalm );
		return Role == HandPropRole.Potion ? _arms.PotionGripFor( _drinker.IsValid() ? _drinker.BottleModel : null ) : _arms.GripFor( Hand );
	}

	private bool Joint( CharacterBoneRig rig, Model model, string name, out Vector3 world )
	{
		world = default;
		var bone = _bones.Get( model, name );
		if ( bone is null || !rig.TryGetPose( bone, out var tx ) ) return false;
		world = tx.Position;
		return true;
	}

	private bool EnsureProp( Model model )
	{
		if ( !_prop.IsValid() )
		{
			_prop = new GameObject( false, $"hand_prop_{Role.ToString().ToLowerInvariant()}_{Hand.ToString().ToLowerInvariant()}" )
			{
				Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked,
			};
			_renderer = _prop.Components.Create<ModelRenderer>();
		}
		// the bottle changes with the potion being drunk
		if ( _renderer.IsValid() && _renderer.Model != model ) _renderer.Model = model;
		return _renderer.IsValid();
	}
}
