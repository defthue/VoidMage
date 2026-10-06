using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Dungeon;

/// <summary>
/// The theme half of the builder: re-skinning the whole dungeon kit to the mossy atlas without touching a
/// single asset. Every DungeonPack model carries TWO material slots — its own (shaders/dungeonshader.vmat, or
/// shaders/woodshader.vmat for the wooden props) and shaders/dungeonoutline.vmat, the toon outline hull baked
/// into the same mesh. A whole-model <see cref="ModelRenderer.MaterialOverride"/> would replace both and take
/// the outline with it, so the swap is done PER SLOT through ModelRenderer.Materials, matching on the authored
/// material's ResourcePath and leaving every other slot alone. Same idea as SkeletonBody.FlashMaterials.
///
/// Both mossy vmats are hand-maintained copies of their stone originals with only the atlas changed
/// (Synty's 03_B: identical UV layout, recoloured green), so this costs no vmdl regeneration at all.
/// </summary>
public sealed partial class DungeonBuilder
{
	/// <summary>Stone is the dungeon as built (floors 1-5); Mossy is the same dungeon overgrown (6-10); Realms is the
	/// dwarven forge-hold from the Dungeon Realms pack (11+). Nothing derives this from Depth — pick it here.</summary>
	[Property, Group( "Decoration" )] public DungeonTheme Theme { get; set; } = DungeonTheme.Stone;

	/// <summary>Mossy only: how thickly the greenery grows. 1 is the tuned default, 0 turns the pass off.</summary>
	[Property, Group( "Decoration" ), Range( 0f, 2f )] public float Greenery { get; set; } = 1f;

	/// <summary>Mossy only: how thickly the root mats creep over the floors, wall feet and ceilings. Its own dial
	/// because it is the one that carries the look and the one that costs the renderers — a few hundred a floor.</summary>
	[Property, Group( "Decoration" ), Range( 0f, 2f )] public float Roots { get; set; } = 1f;

	/// <summary>Authored material -> its twin for this kit (DungeonKit.SkinPairs; empty for stone and for Realms, whose
	/// models carry their own materials). Order matters only for the log.</summary>
	private (string stone, string mossy)[] SkinPairs => _kit.SkinPairs;

	/// <summary>Resolved ResourcePath of a stone material -> the mossy Material to put in its place. Null until built.</summary>
	private Dictionary<string, Material> _skin;
	private int _skinnedRenderers, _skinnedSlots, _skinNoSlots;
	private bool _skinFailed;

	private void ResetSkin()
	{
		_skin = null;
		_skinnedRenderers = 0;
		_skinnedSlots = 0;
		_skinNoSlots = 0;
		_skinFailed = false;
	}

	private static string SkinKey( Material m ) => m?.ResourcePath?.ToLowerInvariant() ?? "";

	/// <summary>
	/// Built once per Generate. A mossy vmat that will not load (never compiled, or renamed) is the one failure
	/// that has to be loud: without the guard every SetOverride would be handed null, which CLEARS the override
	/// instead of setting it, and the floor would come out stone with nothing in the log to explain it.
	/// </summary>
	private Dictionary<string, Material> Skin
	{
		get
		{
			if ( _skin != null || _skinFailed ) return _skin;
			var map = new Dictionary<string, Material>();
			foreach ( var (stonePath, mossyPath) in SkinPairs )
			{
				var stone = Material.Load( stonePath );
				var mossy = Material.Load( mossyPath );
				if ( stone == null || mossy == null )
				{
					Log.Warning( $"[Dungeon] theme {_kit.Id}: could not load {(stone == null ? stonePath : mossyPath)}; the floor keeps its authored materials" );
					continue;
				}
				map[SkinKey( stone )] = mossy;
			}
			if ( map.Count == 0 ) { _skinFailed = true; return null; }
			return _skin = map;
		}
	}

	/// <summary>
	/// Re-skin one renderer's dungeon slots. Called from Spawn the moment the model is assigned, and that is
	/// safe: ModelRenderer answers the accessor's material count straight out of Model.Materials.Length, with
	/// no scene round-trip and nothing deferred to the next frame (checked against the engine assembly). So a
	/// count of zero here means the MODEL failed to load, which is worth counting and reporting rather than
	/// retrying.
	/// </summary>
	private void ApplySkin( ModelRenderer renderer )
	{
		if ( SkinPairs.Length == 0 || !renderer.IsValid() ) return;
		var skin = Skin;
		if ( skin == null ) return;
		var slots = renderer.Materials;
		if ( slots == null || slots.Count == 0 ) { _skinNoSlots++; return; }
		int hits = 0;
		for ( int i = 0; i < slots.Count; i++ )
		{
			if ( !skin.TryGetValue( SkinKey( slots.GetOriginal( i ) ), out var mossy ) ) continue;
			slots.SetOverride( i, mossy );
			hits++;
		}
		if ( hits == 0 ) return;
		_skinnedRenderers++;
		_skinnedSlots += hits;
	}

	private void LogSkin()
	{
		if ( SkinPairs.Length == 0 ) return;
		if ( _skinnedRenderers == 0 )
		{
			Log.Warning( $"[Dungeon] theme {_kit.Id}: nothing was re-skinned. Are {SkinPairs[0].mossy} and its atlas compiled?" );
			return;
		}
		var tail = _skinNoSlots > 0 ? $", {_skinNoSlots} renderers had no material table (model failed to load?)" : "";
		Log.Info( $"[Dungeon] theme {_kit.Id}: {_skinnedRenderers} renderers re-skinned, {_skinnedSlots} slots{tail}" );
	}
}
