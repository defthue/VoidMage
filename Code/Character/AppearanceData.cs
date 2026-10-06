using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// What a player chose in the customizer. Dictionaries rather than one property per choice so a new
/// slot or tintable part is a table row, not a schema change; Normalize makes any stored or
/// client-sent value safe to apply. Carried on the player object as a host-written [Sync] JSON string.
/// </summary>
public sealed class AppearanceData
{
	/// <summary>AppearanceSlot name -> AppearanceOption id.</summary>
	[JsonPropertyName( "options" )]
	public Dictionary<string, string> Options { get; set; } = new();

	/// <summary>TintParts name -> "r,g,b" in 0..1.</summary>
	[JsonPropertyName( "tints" )]
	public Dictionary<string, string> Tints { get; set; } = new();

	public const string NoTint = "1,1,1";

	public void Normalize()
	{
		Options ??= new Dictionary<string, string>();
		Tints ??= new Dictionary<string, string>();

		foreach ( var slot in Enum.GetValues<AppearanceSlot>() )
		{
			var key = slot.ToString();
			if ( Options.TryGetValue( key, out var id ) && AppearanceOptions.IsValid( slot, id ) ) continue;

			var fallback = AppearanceOptions.DefaultFor( slot );
			if ( fallback is null ) Options.Remove( key );
			else Options[key] = fallback.Id;
		}

		foreach ( var key in Options.Keys.ToList() )
		{
			if ( !Enum.TryParse<AppearanceSlot>( key, out _ ) )
				Options.Remove( key );
		}

		foreach ( var part in AppearanceOptions.TintParts.All )
		{
			if ( Tints.TryGetValue( part, out var tint ) && TryParseTint( tint, out var r, out var g, out var b ) )
				Tints[part] = FormatTint( r, g, b );
			else
				Tints[part] = AppearanceOptions.TintParts.DefaultTint( part );
		}

		foreach ( var key in Tints.Keys.ToList() )
		{
			if ( Array.IndexOf( AppearanceOptions.TintParts.All, key ) < 0 )
				Tints.Remove( key );
		}
	}

	public string OptionFor( AppearanceSlot slot )
	{
		return Options is not null && Options.TryGetValue( slot.ToString(), out var id ) ? id : AppearanceOptions.DefaultFor( slot )?.Id ?? "";
	}

	public string TintFor( string part )
	{
		return Tints is not null && Tints.TryGetValue( part, out var tint ) ? tint : AppearanceOptions.TintParts.DefaultTint( part );
	}

	public AppearanceData Clone()
	{
		return new AppearanceData
		{
			Options = new Dictionary<string, string>( Options ?? new Dictionary<string, string>() ),
			Tints = new Dictionary<string, string>( Tints ?? new Dictionary<string, string>() )
		};
	}

	/// <summary>Null on anything unparseable; an empty string is simply "not received yet".</summary>
	public static AppearanceData Parse( string json )
	{
		if ( string.IsNullOrEmpty( json ) ) return null;
		try
		{
			var data = Json.Deserialize<AppearanceData>( json );
			data?.Normalize();
			return data;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[AppearanceData] Unparseable appearance json: {e.Message}" );
			return null;
		}
	}

	public string ToJson() => Json.Serialize( this );

	/// <summary>Three invariant floats, clamped; the format is ours so nothing parses it differently later.</summary>
	public static bool TryParseTint( string text, out float r, out float g, out float b )
	{
		r = g = b = 1f;
		if ( string.IsNullOrEmpty( text ) ) return false;

		var parts = text.Split( ',' );
		if ( parts.Length != 3 ) return false;

		var values = new float[3];
		for ( var i = 0; i < 3; i++ )
		{
			if ( !float.TryParse( parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) ) return false;
			if ( !float.IsFinite( value ) ) return false;
			values[i] = Math.Clamp( value, 0f, 1f );
		}

		r = values[0]; g = values[1]; b = values[2];
		return true;
	}

	public static string FormatTint( float r, float g, float b )
	{
		return string.Create( CultureInfo.InvariantCulture, $"{Math.Clamp( r, 0f, 1f ):0.###},{Math.Clamp( g, 0f, 1f ):0.###},{Math.Clamp( b, 0f, 1f ):0.###}" );
	}

	public static string FormatTint( Color c ) => FormatTint( c.r, c.g, c.b );
}
