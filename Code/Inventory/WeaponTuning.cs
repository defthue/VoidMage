using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Sandbox;

namespace VoidMage.Inventory;

/// <summary>
/// One weapon skin's seat in the player's hand, as tuned in game (`weapon_tune`). Everything is on top of the prefab's
/// sword or staff grip. Null means "the skin table's own value"; set once the tuner touches it.
/// </summary>
public sealed class WeaponTune
{
	public string Id { get; set; } = "";
	/// <summary>Added to the hand's grip offset (inches, the fist's frame: along the fingers, into the palm, along the blade).</summary>
	public Vector3 Offset { get; set; }
	/// <summary>Added to the hand's rotation trim.</summary>
	public Angles Rotation { get; set; }
	public float? Spin { get; set; }
	public bool? Flip { get; set; }
	/// <summary>Along the shaft, world inches at this import scale.</summary>
	public float? Shift { get; set; }
	/// <summary>Grip to spell muzzle, world inches at this import scale.</summary>
	public float? TipLength { get; set; }
	/// <summary>The size the player copy should have (tools/gen_player_weapons.py bakes it into Assets/PlayerWeapons).</summary>
	public float ImportScale { get; set; } = WeaponTuning.AuthoredScale;
	/// <summary>The size the player copy was last baked at: until the tool runs again, the prop is scaled by the difference.</summary>
	public float CompiledScale { get; set; } = WeaponTuning.AuthoredScale;

	[JsonIgnore] public float PropScale => CompiledScale > 0f && ImportScale > 0f ? ImportScale / CompiledScale : 1f;
	/// <summary>How much bigger than the skin table was written for (its shifts and tips are at import_scale 0.2).</summary>
	[JsonIgnore] public float SizeRatio => ImportScale > 0f ? ImportScale / WeaponTuning.AuthoredScale : 1f;
}

public sealed class WeaponTuneFile
{
	public List<WeaponTune> Weapons { get; set; } = new();
}

/// <summary>
/// The weapon seats: the project's Assets/data/weapon_tuning.json (committed, written by tools/gen_player_weapons.py) with the
/// user's own save on top (voidmage/weapon_tuning.json in the s&box data folder, written by the tuner's Save). A skin with no
/// entry sits exactly as the skin table and the prefab say.
/// </summary>
public static class WeaponTuning
{
	/// <summary>The import scale the skin table's shifts and tips were written for (the starting sword and staff's).</summary>
	public const float AuthoredScale = 0.2f;
	public const string ProjectPath = "data/weapon_tuning.json";
	public const string UserPath = "voidmage/weapon_tuning.json";

	private static Dictionary<string, WeaponTune> _tunes;

	private static Dictionary<string, WeaponTune> Tunes
	{
		get
		{
			if ( _tunes is not null ) return _tunes;
			_tunes = new();
			Merge( FileSystem.Mounted, ProjectPath );
			Merge( FileSystem.Data, UserPath );
			return _tunes;
		}
	}

	private static void Merge( BaseFileSystem fs, string path )
	{
		try
		{
			if ( fs is null || !fs.FileExists( path ) ) return;
			var file = fs.ReadJson<WeaponTuneFile>( path );
			foreach ( var t in file?.Weapons ?? new() )
				if ( !string.IsNullOrEmpty( t.Id ) ) _tunes[t.Id] = t;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Weapons] could not read {path}: {e.Message}" );
		}
	}

	public static WeaponTune Get( string id ) => !string.IsNullOrEmpty( id ) && Tunes.TryGetValue( id, out var t ) ? t : null;

	/// <summary>The entry the tuner edits, made on first touch.</summary>
	public static WeaponTune Edit( string id )
	{
		if ( Get( id ) is { } t ) return t;
		t = new WeaponTune { Id = id };
		Tunes[id] = t;
		return t;
	}

	/// <summary>A new import scale; a tuned slide and tip keep their place on the model (they are world inches at the old size).</summary>
	public static void SetImportScale( string id, float value )
	{
		var t = Edit( id );
		float before = t.ImportScale;
		float after = MathF.Max( 0.01f, MathF.Round( value * 1000f ) / 1000f );
		if ( before <= 0f || after == before ) { t.ImportScale = after; return; }
		if ( t.Shift is { } shift ) t.Shift = shift * after / before;
		if ( t.TipLength is { } tip ) t.TipLength = tip * after / before;
		t.ImportScale = after;
	}

	/// <summary>Forget the tuning of one weapon (back to the skin table), keeping what was baked.</summary>
	public static void Reset( string id )
	{
		if ( Get( id ) is not { } t ) return;
		Tunes[id] = new WeaponTune { Id = id, ImportScale = t.CompiledScale, CompiledScale = t.CompiledScale };
	}

	public static void Save()
	{
		try
		{
			FileSystem.Data.CreateDirectory( "voidmage" );
			FileSystem.Data.WriteJson( UserPath, new WeaponTuneFile { Weapons = Tunes.Values.OrderBy( t => t.Id ).ToList() } );
			Log.Info( $"[Weapons] tuning saved ({Tunes.Count} weapons) to the data folder's {UserPath}. Run tools/gen_player_weapons.py (Blender) to bake import scales and copy it into the project." );
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"[Weapons] could not write {UserPath}: {e.Message}" );
		}
	}

	/// <summary>Dev: reload both files (after the bake tool ran).</summary>
	[ConCmd( "weapon_tune_reload" )]
	public static void ReloadCommand()
	{
		_tunes = null;
		Log.Info( $"[Weapons] tuning reloaded: {Tunes.Count} weapons" );
	}
}
