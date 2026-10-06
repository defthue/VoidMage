using System;
using System.Collections.Generic;
using System.Linq;

namespace VoidMage.Character;

/// <summary>
/// The regions a player dresses independently, in wardrobe order. Torso carries both arms and hands, Legs
/// the hips, Shoes both feet. Palette is the colour set every sorcerer piece samples, carried as a
/// material group rather than a model. Hair is hidden (but kept) while a hat is worn.
/// </summary>
public enum AppearanceSlot
{
	Hat,
	Hair,
	FacialHair,
	Torso,
	Legs,
	Shoes,
	ShoulderLeft,
	ShoulderRight,
	HipLeft,
	HipRight,
	Palette
}

public sealed class AppearanceOption
{
	public string Id { get; init; } = "";
	public string Name { get; init; } = "";
	public AppearanceSlot Slot { get; init; }
	/// <summary>The bone-merged part models (a torso option is the torso plus both arms). Empty for the bare option and for palettes.</summary>
	public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();
	/// <summary>The BodyParts groups this option covers, so the skin under it is not drawn; empty for attachments.</summary>
	public IReadOnlyList<string> Hides { get; init; } = Array.Empty<string>();
	/// <summary>Palette options only: the vmdl material group to select (null = the default palette).</summary>
	public string MaterialGroup { get; init; }

	/// <summary>The bare option: nothing rendered for this slot (bald, no shirt, barefoot).</summary>
	public bool IsNone => Models.Count == 0 && MaterialGroup is null;
}

/// <summary>
/// The regions of the base body that clothing can hide. human_base.vmdl carries the skeleton, the
/// animgraph and the head; every other region is its own part model, bone-merged onto the body like
/// hair, so hiding a region is disabling its renderers. The head is never hidden.
/// </summary>
public static class BodyParts
{
	public const string Torso = "torso";
	public const string UpperArms = "upper_arms";
	public const string Forearms = "forearms";
	public const string Hands = "hands";
	public const string Hips = "hips";
	public const string Shins = "shins";
	public const string Feet = "feet";
	public const string Hair = "hair";

	public static readonly string[] BodyGroups = { Torso, UpperArms, Forearms, Hands, Hips, Shins, Feet };

	public static IReadOnlyList<string> ModelsFor( string group )
	{
		return group switch
		{
			Torso => new[] { "charactertoon/human_torso.vmdl" },
			UpperArms => new[] { "charactertoon/human_arm_upper_l.vmdl", "charactertoon/human_arm_upper_r.vmdl" },
			Forearms => new[] { "charactertoon/human_arm_lower_l.vmdl", "charactertoon/human_arm_lower_r.vmdl" },
			Hands => new[] { "charactertoon/human_hand_l.vmdl", "charactertoon/human_hand_r.vmdl" },
			Hips => new[] { "charactertoon/human_hips.vmdl" },
			Shins => new[] { "charactertoon/human_leg_l.vmdl", "charactertoon/human_leg_r.vmdl" },
			Feet => new[] { "charactertoon/human_foot_l.vmdl", "charactertoon/human_foot_r.vmdl" },
			_ => Array.Empty<string>()
		};
	}
}

/// <summary>
/// Every look a player can wear: the wardrobe's catalogue. The base human hairs from Assets/CharacterToon
/// plus every Fantasy Sorcerers piece (SorcererCatalogue, generated from the pack manifest), each skinned
/// to the human armature with only the bones it weights (bone merge matches by name).
/// </summary>
public static class AppearanceOptions
{
	public static class Ids
	{
		public const string Hair00 = "hair_00";
		public const string Hair01 = "hair_01";
		public const string PaletteDefault = "palette_default";
	}

	/// <summary>The renderers a colour can be applied to, named by their GameObject in the prefab.</summary>
	public static class TintParts
	{
		public const string Hair = "hair";
		public const string Eyes = "eyes";
		public const string Brows = "brows";

		public static readonly string[] All = { Hair, Eyes, Brows };

		/// <summary>These parts render the colour outright (CharacterColor.vmat replaces the texture with the renderer Tint).</summary>
		public static string DefaultTint( string part )
		{
			return part switch
			{
				Hair => "0.35,0.2,0.1",
				Brows => "0.25,0.14,0.08",
				Eyes => "0.25,0.45,0.7",
				_ => AppearanceData.NoTint
			};
		}
	}

	/// <summary>Hair swatches the wardrobe offers for the base hairs (the sorcerer hairs take their colour from the palette).</summary>
	public static readonly (string Name, string Tint)[] HairTints =
	{
		("Black", "0.08,0.07,0.07"), ("Dark brown", "0.2,0.12,0.07"), ("Brown", "0.35,0.2,0.1"), ("Chestnut", "0.45,0.22,0.12"),
		("Auburn", "0.5,0.18,0.08"), ("Ginger", "0.7,0.32,0.1"), ("Blonde", "0.8,0.62,0.3"), ("Platinum", "0.85,0.8,0.65"),
		("Grey", "0.5,0.5,0.5"), ("White", "0.9,0.9,0.88"), ("Violet", "0.4,0.2,0.55"), ("Teal", "0.15,0.45,0.45"),
	};

	private static readonly Dictionary<string, AppearanceOption> _options = new();
	private static readonly Dictionary<AppearanceSlot, List<AppearanceOption>> _bySlot = new();

	public static IReadOnlyDictionary<string, AppearanceOption> All => _options;

	/// <summary>The slots that carry models, in wardrobe order (Palette is separate).</summary>
	public static readonly AppearanceSlot[] ModelSlots =
		Enum.GetValues<AppearanceSlot>().Where( s => s != AppearanceSlot.Palette ).ToArray();

	/// <summary>Synty's six matched sorcerer outfits (back pieces left out).</summary>
	public static IReadOnlyList<SorcererCatalogue.Preset> Presets => SorcererCatalogue.Presets;

	static AppearanceOptions()
	{
		foreach ( var slot in Enum.GetValues<AppearanceSlot>() )
			_bySlot[slot] = new List<AppearanceOption>();

		// Bare: first in every list, and therefore what Normalize falls back to.
		foreach ( var slot in ModelSlots )
			Option( NoneId( slot ), "None", slot, Array.Empty<string>(), Array.Empty<string>() );

		Option( Ids.Hair01, "Hair 1", AppearanceSlot.Hair, new[] { "charactertoon/human_hair.vmdl" }, Array.Empty<string>() );
		for ( int i = 2; i <= 12; i++ )
			Option( $"hair_{i:00}", $"Hair {i}", AppearanceSlot.Hair, new[] { $"charactertoon/hair{i}.vmdl" }, Array.Empty<string>() );

		foreach ( var row in SorcererCatalogue.Rows )
			Option( row.Id, row.Name, row.Slot, row.Models, row.Hides );

		Palette( Ids.PaletteDefault, "Default", null );
		for ( int i = 1; i <= 6; i++ )
			Palette( $"palette_{i:00}", $"Colours {i}", $"palette_{i:00}" );
	}

	public static string NoneId( AppearanceSlot slot ) => slot.ToString().ToLowerInvariant() + "_00";

	private static void Option( string id, string name, AppearanceSlot slot, IReadOnlyList<string> models, IReadOnlyList<string> hides )
	{
		var option = new AppearanceOption { Id = id, Name = name, Slot = slot, Models = models, Hides = hides };
		_options[id] = option;
		_bySlot[slot].Add( option );
	}

	private static void Palette( string id, string name, string group )
	{
		var option = new AppearanceOption { Id = id, Name = name, Slot = AppearanceSlot.Palette, MaterialGroup = group };
		_options[id] = option;
		_bySlot[AppearanceSlot.Palette].Add( option );
	}

	public static AppearanceOption Get( string id ) => id is null ? null : _options.GetValueOrDefault( id );

	public static IReadOnlyList<AppearanceOption> For( AppearanceSlot slot ) => _bySlot[slot];

	public static AppearanceOption DefaultFor( AppearanceSlot slot )
	{
		var list = _bySlot[slot];
		return list.Count > 0 ? list[0] : null;
	}

	public static bool IsValid( AppearanceSlot slot, string id )
	{
		var option = Get( id );
		return option is not null && option.Slot == slot;
	}

	public static string DisplayName( AppearanceSlot slot )
	{
		return slot switch
		{
			AppearanceSlot.FacialHair => "Beard",
			AppearanceSlot.ShoulderLeft => "Left Shoulder",
			AppearanceSlot.ShoulderRight => "Right Shoulder",
			AppearanceSlot.HipLeft => "Left Hip",
			AppearanceSlot.HipRight => "Right Hip",
			AppearanceSlot.Palette => "Colours",
			_ => slot.ToString()
		};
	}

	/// <summary>A new character: Sorcerer preset 1 with base hair 1 under the hat, the default palette and tints.</summary>
	public static AppearanceData DefaultLook()
	{
		var data = new AppearanceData();
		data.Options[nameof( AppearanceSlot.Hair )] = Ids.Hair01;
		if ( Presets.Count > 0 ) WearPreset( data, Presets[0] );
		data.Options[nameof( AppearanceSlot.Palette )] = Ids.PaletteDefault;
		data.Normalize();
		return data;
	}

	/// <summary>The bare body: nothing worn in any slot, hair and tints kept from `from` (the wardrobe's Strip).</summary>
	public static AppearanceData BareLook( AppearanceData from )
	{
		var data = from?.Clone() ?? new AppearanceData();
		foreach ( var slot in ModelSlots )
			if ( slot != AppearanceSlot.Hair ) data.Options[slot.ToString()] = NoneId( slot );
		data.Options[nameof( AppearanceSlot.Palette )] = Ids.PaletteDefault;
		data.Normalize();
		return data;
	}

	/// <summary>
	/// Puts a preset on: every model slot takes the preset's option or goes bare. Hair is the exception: a
	/// preset without hair keeps the current one (it sits under the preset's hat anyway). Colours are untouched.
	/// </summary>
	public static void WearPreset( AppearanceData data, SorcererCatalogue.Preset preset )
	{
		foreach ( var slot in ModelSlots )
		{
			if ( preset.Options.TryGetValue( slot, out var id ) ) data.Options[slot.ToString()] = id;
			else if ( slot != AppearanceSlot.Hair ) data.Options[slot.ToString()] = NoneId( slot );
		}
	}
}
