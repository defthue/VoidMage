using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// Dresses one character from its synced AppearanceData: the body's part models, one piece per wardrobe
/// slot, the chosen hair, the palette (colour set) and the hair/eye/brow tints. The host writes
/// AppearanceJson, every peer applies it here.
///
/// Every piece is its own SkinnedModelRenderer bone-merged onto the body, the same shape as the prefab's
/// hair/eyes/brows: CharacterBoneRig discovers them by BoneMergeTarget and keeps them glued through the
/// procedural bone writes. Hiding a region is disabling its holder GameObject. Appearance never touches
/// MaterialOverride (the hit flash owns it); the colour set is the vmdl material group, which yields to an
/// override and comes back on its own when the override clears.
/// </summary>
[Title( "Character Appearance" ), Category( "Void Mage" ), Icon( "face" )]
public sealed class CharacterAppearance : Component
{
	[Sync( SyncFlags.FromHost ), Change( nameof( OnAppearanceChanged ) )]
	public string AppearanceJson { get; set; } = "";

	// Only the tints are inspector-tunable; the starting outfit is AppearanceOptions.DefaultLook().
	[Property, Group( "Starting look" )] public Color HairTint { get; set; } = new( 0.35f, 0.2f, 0.1f );
	[Property, Group( "Starting look" )] public Color EyeTint { get; set; } = new( 0.25f, 0.45f, 0.7f );
	[Property, Group( "Starting look" )] public Color BrowTint { get; set; } = new( 0.25f, 0.14f, 0.08f );

	/// <summary>The last data applied, normalized; defaults until the host's first write arrives.</summary>
	public AppearanceData Current { get; private set; }

	private sealed class PieceSet
	{
		public GameObject Object;
		public string Key = "";
	}

	private SkinnedModelRenderer _body;
	private readonly Dictionary<string, PieceSet> _sets = new();
	private static readonly HashSet<string> _warnedModels = new();

	protected override void OnStart()
	{
		foreach ( var other in GameObject.Components.GetAll<CharacterAppearance>( FindMode.EverythingInSelf ) )
		{
			if ( other == this ) break;
			Log.Warning( $"[CharacterAppearance] Duplicate component on '{GameObject.Name}', disabling this copy" );
			Enabled = false;
			return;
		}

		// The host seeds the synced look from the defaults; clients wait for it.
		if ( string.IsNullOrEmpty( AppearanceJson ) && !IsProxy )
			AppearanceJson = StartingLook().ToJson();

		Apply();
	}

	protected override void OnDestroy()
	{
		foreach ( var set in _sets.Values )
			if ( set.Object.IsValid() ) set.Object.Destroy();
		_sets.Clear();
	}

	/// <summary>The default outfit with this prefab's tints.</summary>
	public AppearanceData StartingLook()
	{
		var data = AppearanceOptions.DefaultLook();
		data.Tints[AppearanceOptions.TintParts.Hair] = AppearanceData.FormatTint( HairTint );
		data.Tints[AppearanceOptions.TintParts.Eyes] = AppearanceData.FormatTint( EyeTint );
		data.Tints[AppearanceOptions.TintParts.Brows] = AppearanceData.FormatTint( BrowTint );
		data.Normalize();
		return data;
	}

	/// <summary>Host only: replace the look and push it to every peer.</summary>
	public void SetLook( AppearanceData data )
	{
		data.Normalize();
		AppearanceJson = data.ToJson();
		Apply();
	}

	/// <summary>A client asking for a look; the host normalizes and applies it (SetLook is host-only).</summary>
	[Rpc.Host]
	public void RequestLook( string json )
	{
		var data = AppearanceData.Parse( json );
		if ( data is null ) return;
		SetLook( data );
	}

	/// <summary>Puts the outfit saved by the wardrobe (RunSave) on a freshly spawned player; nothing saved, nothing changes.</summary>
	public static void ApplySaved( GameObject player )
	{
		if ( !player.IsValid() || !Networking.IsHost ) return;
		var json = VoidMage.Run.RunSave.Data.Appearance;
		if ( string.IsNullOrEmpty( json ) ) return;
		var look = AppearanceData.Parse( json );
		if ( look is null ) return;
		player.Components.Get<CharacterAppearance>( FindMode.EverythingInSelfAndDescendants )?.SetLook( look );
	}

	/// <summary>From the owner's side: apply directly when hosting, else ask the host.</summary>
	public void ChangeLook( AppearanceData data )
	{
		if ( Networking.IsHost ) SetLook( data );
		else RequestLook( data.ToJson() );
	}

	private void OnAppearanceChanged( string oldValue, string newValue ) => Apply();

	public void Apply()
	{
		var data = AppearanceData.Parse( AppearanceJson );
		if ( data is null )
		{
			data = new AppearanceData();
			data.Normalize();
		}

		Current = data;

		if ( !ResolveBody() ) return;

		// Every piece hides the skin under it.
		var bodyHides = new HashSet<string>();
		foreach ( var slot in AppearanceOptions.ModelSlots )
		{
			if ( slot == AppearanceSlot.Hair ) continue;
			var option = AppearanceOptions.Get( data.OptionFor( slot ) );
			var parts = option is null || option.IsNone ? Array.Empty<string>() : option.Models;
			var shown = parts.Count > 0;
			EnsureSet( $"piece_{slot.ToString().ToLowerInvariant()}", parts, shown );

			if ( shown ) bodyHides.UnionWith( option.Hides );
		}

		foreach ( var group in BodyParts.BodyGroups )
			EnsureSet( $"body_{group}", BodyParts.ModelsFor( group ), !bodyHides.Contains( group ) );

		ApplyHair( data );
		ApplyPalette( data );
		ApplyTints( data );
	}

	/// <summary>The body is the one skinned renderer that merges onto nothing; every piece points at it.</summary>
	private bool ResolveBody()
	{
		if ( _body.IsValid() ) return true;

		foreach ( var renderer in Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( renderer.BoneMergeTarget is not null ) continue;
			// The first-person arms and the hidden sword pose source are skeletons of their own, not the body.
			if ( renderer.GameObject.Tags.Has( "viewmodel" ) || renderer.GameObject.Tags.Has( "posesource" ) ) continue;
			_body = renderer;
			return true;
		}
		return false;
	}

	public SkinnedModelRenderer Body => ResolveBody() ? _body : null;

	/// <summary>One holder GameObject per piece set under the body, one renderer per part.</summary>
	private void EnsureSet( string name, IReadOnlyList<string> parts, bool enabled )
	{
		var key = string.Join( "|", parts );

		if ( !_sets.TryGetValue( name, out var set ) )
		{
			set = new PieceSet();
			_sets[name] = set;
		}

		if ( set.Key != key || (parts.Count > 0 && !set.Object.IsValid()) )
		{
			if ( set.Object.IsValid() )
				set.Object.DestroyImmediate();

			set.Object = null;
			set.Key = key;

			if ( parts.Count > 0 )
			{
				var holder = new GameObject( true, name ) { Parent = _body.GameObject };

				foreach ( var path in parts )
				{
					var model = LoadModel( path );
					if ( model is null ) continue;

					var renderer = holder.Components.Create<SkinnedModelRenderer>();
					renderer.Model = model;
					renderer.BoneMergeTarget = _body;
					renderer.RenderType = _body.RenderType;
				}

				set.Object = holder;
			}

			CharacterBoneRig.NotifyAttachmentsChanged( _body.GameObject );
		}

		if ( set.Object.IsValid() && set.Object.Enabled != enabled )
		{
			set.Object.Enabled = enabled;
			CharacterBoneRig.NotifyAttachmentsChanged( _body.GameObject );
		}
	}

	private void ApplyHair( AppearanceData data )
	{
		var hairObject = FindPartObject( AppearanceOptions.TintParts.Hair );
		if ( !hairObject.IsValid() ) return;

		var hair = hairObject.Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelf );
		var option = AppearanceOptions.Get( data.OptionFor( AppearanceSlot.Hair ) );
		var visible = true;
		if ( hair.IsValid() && option is not null && !option.IsNone )
		{
			var model = LoadModel( option.Models[0] );
			if ( model is not null && hair.Model != model )
				hair.Model = model;
		}

		// "None" is bald: the renderer stays but the object is off. A hat hides the hair too, but the choice
		// is kept, so taking the hat off brings the same hair back.
		if ( option is not null && option.IsNone )
			visible = false;
		if ( AppearanceOptions.Get( data.OptionFor( AppearanceSlot.Hat ) ) is { IsNone: false } )
			visible = false;

		if ( hairObject.Enabled != visible )
		{
			hairObject.Enabled = visible;
			CharacterBoneRig.NotifyAttachmentsChanged( hairObject );
		}
	}

	/// <summary>The colour set: the same material group on every renderer under the body. Models without groups ignore it.</summary>
	private void ApplyPalette( AppearanceData data )
	{
		var option = AppearanceOptions.Get( data.OptionFor( AppearanceSlot.Palette ) );
		var group = option?.MaterialGroup;
		foreach ( var renderer in _body.GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( renderer.MaterialGroup != group ) renderer.MaterialGroup = group;
		}
	}

	private void ApplyTints( AppearanceData data )
	{
		foreach ( var part in AppearanceOptions.TintParts.All )
		{
			var renderer = FindPart( part );
			if ( !renderer.IsValid() ) continue;

			if ( !AppearanceData.TryParseTint( data.TintFor( part ), out var r, out var g, out var b ) )
				r = g = b = 1f;

			renderer.Tint = new Color( r, g, b, 1f );
		}

		// a beard is hair too: same flat-colour material, same tint
		if ( _sets.TryGetValue( $"piece_{nameof( AppearanceSlot.FacialHair ).ToLowerInvariant()}", out var beard ) && beard.Object.IsValid() )
		{
			if ( !AppearanceData.TryParseTint( data.TintFor( AppearanceOptions.TintParts.Hair ), out var r, out var g, out var b ) )
				r = g = b = 1f;
			foreach ( var renderer in beard.Object.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelf ) )
				renderer.Tint = new Color( r, g, b, 1f );
		}
	}

	private GameObject FindPartObject( string name )
	{
		if ( !_body.IsValid() ) return null;
		return _body.GameObject.Children.FirstOrDefault( c => c.IsValid() && string.Equals( c.Name, name, StringComparison.OrdinalIgnoreCase ) );
	}

	private SkinnedModelRenderer FindPart( string name )
	{
		return FindPartObject( name )?.Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelf );
	}

	private static Model LoadModel( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;

		var model = Model.Load( path );
		if ( model is not null && !model.IsError ) return model;

		if ( _warnedModels.Add( path ) )
			Log.Warning( $"[CharacterAppearance] Missing model '{path}', piece not rendered" );

		return null;
	}
}
