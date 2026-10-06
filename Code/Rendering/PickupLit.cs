using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Rendering;

/// <summary>
/// The pickups' own lighting (user, 2026-09-17: "its own shader ... so players actually see it"). A potion or a weapon
/// lying in an unlit corner of the dungeon was as good as invisible, so every visible slot of a pickup's model is
/// overridden with the same material at a raised ambient floor (heden_toon's `g_flAmbientFloor`, the minimum light on
/// the shadow side): `shaders/pickuplit/<name>.vmat`, written by tools/gen_lit_pickup_vmats.py. Nothing else about the
/// material changes, so the thing still looks like itself, just never black.
///
/// Outline slots are left alone (the outline is unlit anyway, and the focus outline swaps that slot).
///
/// The names are listed here rather than probed with Material.Load: a vmat that does not exist loads as the error
/// material and would render solid purple. This list must match SOURCES in the generator.
/// </summary>
public static class PickupLit
{
	/// <summary>The lit twins that exist on disk, by the source material's file name (lower case, no extension).</summary>
	private static readonly string[] Names =
	{
		"dungeonshader", "realmsshader", "fantasykingdomshader", "darkfantasyshader", "darkfortressshader01b",
		"bladeshader", "ironshader", "woodhandleshader", "brassshader", "gemshader", "staffshader", "staffgem",
	};

	private static readonly Dictionary<string, Material> _cache = new();

	/// <summary>The lit twin of a material path, or null when there is none for it (an outline, a shader we never generated).</summary>
	public static Material For( string resourcePath )
	{
		var name = System.IO.Path.GetFileNameWithoutExtension( resourcePath ?? "" ).ToLowerInvariant();
		if ( name.Length == 0 || Array.IndexOf( Names, name ) < 0 ) return null;
		if ( !_cache.TryGetValue( name, out var material ) )
		{
			material = Material.Load( $"shaders/pickuplit/{name}.vmat" );
			if ( material is null ) Log.Warning( $"[Pickup] shaders/pickuplit/{name}.vmat is missing: run tools/gen_lit_pickup_vmats.py" );
			_cache[name] = material;
		}
		return material;
	}

	/// <summary>
	/// Every slot of `renderer` whose material has a lit twin is overridden with it. Returns false until the model's
	/// scene object exists (the material list is empty before that), so callers keep asking for a frame or two - the
	/// same dance PotionPickup does for its outline.
	/// </summary>
	public static bool Apply( ModelRenderer renderer )
	{
		if ( !renderer.IsValid() || renderer.Model is null ) return false;
		var materials = renderer.Materials;
		if ( materials.Count == 0 ) return false;
		for ( int i = 0; i < materials.Count; i++ )
		{
			var original = materials.GetOriginal( i );
			var lit = For( original?.ResourcePath ?? original?.Name );
			if ( lit is not null ) materials.SetOverride( i, lit );
		}
		return true;
	}
}
