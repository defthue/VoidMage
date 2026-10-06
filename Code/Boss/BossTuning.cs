using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox;

namespace VoidMage.Boss;

/// <summary>
/// The tuning loop: every feel/timing/pose number of the boss is a saved convar, this prints and resets them
/// (`boss_tune`, `boss_tune reset`) and holds the pose ones (`boss_skull_pitch`, `boss_hand_l_pyr`, `boss_hand_r_pyr`)
/// plus `boss_slowmo`. The user tunes in-game with instant feedback and pastes the dump back to be baked as defaults.
/// Saved values outlive new defaults, so <see cref="Version"/> stamps the shipped set: when it moves, the saved
/// values are reset once on the next boss (two speed passes went unseen because the old saved numbers stayed).
/// </summary>
public static class BossTuning
{
	/// <summary>Bump when the shipped defaults change: the saved convars are reset to them once.</summary>
	public const int Version = 20;
	[ConVar( "boss_tune_version", ConVarFlags.Saved )] public static int SavedVersion { get; set; }

	/// <summary>The skull's nose-down tilt in the stance, degrees (the model's face points a little up along +x).</summary>
	[ConVar( "boss_skull_pitch", ConVarFlags.Saved )] public static float SkullPitch { get; set; } = 45f;
	/// <summary>A pitch/yaw/roll tweak on a hand's frame fix, degrees in the pose's own frame ("p y r").</summary>
	[ConVar( "boss_hand_l_pyr", ConVarFlags.Saved )] public static string HandLPyr { get; set; } = "0 0 0";
	[ConVar( "boss_hand_r_pyr", ConVarFlags.Saved )] public static string HandRPyr { get; set; } = "0 0 0";
	/// <summary>How fast the stance spot circles the player at most, degrees per second (it wanders back and forth).</summary>
	[ConVar( "boss_orbit_rate", ConVarFlags.Saved )] public static float OrbitRate { get; set; } = 35f;
	/// <summary>How fast the stance spot drifts to where it wants to be, u/s (the parts chase it with the idle dynamics).</summary>
	[ConVar( "boss_stance_speed", ConVarFlags.Saved )] public static float StanceSpeed { get; set; } = 900f;
	/// <summary>The jaw model's shift from where the prefab puts it, skull-local units "x y z" (face +x, up +z).</summary>
	[ConVar( "boss_jaw_offset", ConVarFlags.Saved )] public static string JawOffset { get; set; } = "40 0 -40";
	/// <summary>The jaw's closed tilt about its hinge, degrees: negative lifts the chin (the closing direction), positive drops it.</summary>
	[ConVar( "boss_jaw_tilt", ConVarFlags.Saved )] public static float JawTilt { get; set; } = -10f;

	private static string _cachedL, _cachedR, _cachedJaw;
	private static Rotation _tweakL = Rotation.Identity, _tweakR = Rotation.Identity;
	private static Vector3 _jawOffset;

	public static Vector3 JawOffsetVector
	{
		get
		{
			if ( _cachedJaw != JawOffset ) { _cachedJaw = JawOffset; _jawOffset = ParseVector( JawOffset ); }
			return _jawOffset;
		}
	}

	private static Vector3 ParseVector( string xyz )
	{
		var parts = (xyz ?? "").Split( new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries );
		float F( int i ) => i < parts.Length && float.TryParse( parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : 0f;
		return new Vector3( F( 0 ), F( 1 ), F( 2 ) );
	}

	public static Rotation HandTweak( BossPartKind kind )
	{
		if ( kind == BossPartKind.HandL )
		{
			if ( _cachedL != HandLPyr ) { _cachedL = HandLPyr; _tweakL = Parse( HandLPyr ); }
			return _tweakL;
		}
		if ( _cachedR != HandRPyr ) { _cachedR = HandRPyr; _tweakR = Parse( HandRPyr ); }
		return _tweakR;
	}

	private static Rotation Parse( string pyr )
	{
		var parts = (pyr ?? "").Split( new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries );
		float F( int i ) => i < parts.Length && float.TryParse( parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : 0f;
		return Rotation.From( F( 0 ), F( 1 ), F( 2 ) );
	}

	/// <summary>Every tunable, with its shipped default (the reset values; keep in step with the attributes).</summary>
	public static readonly (string Name, string Default)[] All =
	{
		("boss_skull_pitch", "45"), ("boss_hand_l_pyr", "0 0 0"), ("boss_hand_r_pyr", "0 0 0"), ("boss_orbit_rate", "35"), ("boss_stance_speed", "900"),
		("boss_jaw_offset", "40 0 -40"), ("boss_jaw_tilt", "-10"), ("boss_hit_forgive", "20"), ("boss_tell_shake", "0.95"), ("boss_tell_windup", "180"), ("boss_floor_step", "100"),
		("boss_woosh_lead", "0.25"), ("boss_ring_alpha", "0.55"),
		("boss_gap_1", "0.7"), ("boss_gap_2", "0.4"), ("boss_turn_rate", "320"), ("boss_overlap", "True"),
		("boss_arena_radius", "1900"), ("boss_hug_distance", "260"), ("boss_hug_seconds", "1.5"), ("boss_repo_seconds", "1"), ("boss_return_lift", "150"),
		("boss_form_spread", "380"), ("boss_form_forward", "120"), ("boss_form_height", "-50"),
		("boss_form_speed", "4500"), ("boss_form_accel", "40000"), ("boss_form_arrive", "0.06"), ("boss_form_trail_arrive", "0.14"),
		("boss_idle_speed", "2200"), ("boss_idle_accel", "6000"), ("boss_idle_drag", "0.8"), ("boss_idle_arrive", "0.22"),
		("boss_approach_speed", "2200"), ("boss_approach_accel", "6000"), ("boss_approach_drag", "0.6"), ("boss_approach_arrive", "0.4"),
		("boss_tell_speed", "400"), ("boss_tell_accel", "1400"), ("boss_tell_drag", "1.5"), ("boss_tell_arrive", "0.5"), ("boss_turn_damping", "0.7"),
		("boss_track_speed", "900"), ("boss_track_accel", "4000"), ("boss_track_drag", "1"), ("boss_track_arrive", "0.25"),
		("boss_strike_speed", "5200"), ("boss_strike_accel", "22000"), ("boss_strike_drag", "0"), ("boss_strike_arrive", "0.08"),
		("boss_settle_drag", "8"),
		("boss_recover_speed", "2600"), ("boss_recover_accel", "6000"), ("boss_recover_drag", "1"), ("boss_recover_arrive", "0.3"),
		("boss_lean", "1.2"), ("boss_lean_max", "18"), ("boss_bounce", "0.05"), ("boss_turn_idle", "360"), ("boss_turn_strike", "1000"),
		("boss_sway_amp", "40"), ("boss_sway_hz", "0.12"), ("boss_palm_leads", "0.6"), ("boss_arc_height", "300"),
		("boss_arm_natural", "1"), ("boss_arm_shoulder", "0.6"), ("boss_arm_gravity", "0"), ("boss_arm_trail", "0.3"),
		("boss_arm_stiffness", "45"), ("boss_arm_damping", "9"), ("boss_arm_max_bend", "30"),
		("boss_arm_shoulder_side", "260"), ("boss_arm_shoulder_up", "40"),
		("boss_slam_approach", "0.4"), ("boss_slam_tell", "1.2"), ("boss_slam_lock", "0.5"), ("boss_slam_strike", "0.36"), ("boss_slam_rest", "1.6"), ("boss_slam_recover", "0.6"),
		("boss_slam_damage", "25"), ("boss_slam_height", "620"), ("boss_slam_palm_height", "25"), ("boss_hammer_palm_height", "80"), ("boss_ring_speed", "720"), ("boss_ring_max", "1100"),
		("boss_sweep_approach", "0.4"), ("boss_sweep_tell", "1.2"), ("boss_sweep_lock", "0.6"), ("boss_sweep_strike", "0.7"), ("boss_sweep_rest", "1.2"), ("boss_sweep_recover", "0.6"),
		("boss_sweep_damage", "15"), ("boss_sweep_reach", "700"), ("boss_sweep_start_height", "0"),
		("boss_palm_approach", "0.4"), ("boss_palm_tell", "1.2"), ("boss_palm_lock", "0.6"), ("boss_palm_strike", "0.5"), ("boss_palm_rest", "1.2"), ("boss_palm_recover", "0.6"),
		("boss_palm_damage", "18"), ("boss_palm_distance", "600"), ("boss_palm_through", "160"), ("boss_palm_knockback", "900"), ("boss_palm_knockup", "200"), ("boss_palm_start_height", "0"),
		("boss_bite_approach", "0.5"), ("boss_bite_tell", "0.6"), ("boss_bite_lock", "0.5"), ("boss_bite_strike", "1.2"), ("boss_bite_rest", "0.7"), ("boss_bite_recover", "1.1"),
		("boss_bite_damage", "35"), ("boss_bite_distance", "850"), ("boss_bite_through", "700"), ("boss_bite_mouth_height", "40"), ("boss_bite_climb", "1"), ("boss_skull_low_clearance", "-60"),
		("boss_shake_approach", "0.5"), ("boss_shake_tell", "0.8"), ("boss_shake_lock", "0.2"), ("boss_shake_strike", "1.2"), ("boss_shake_rest", "1.2"), ("boss_shake_recover", "0.7"),
		("boss_shake_bolts", "4"), ("boss_bolt_gap", "0.28"), ("boss_bolt_flight", "1.3"), ("boss_bolt_arc", "260"), ("boss_bolt_radius", "55"), ("boss_bolt_damage", "14"),
		("boss_shake_exposed", "2"), ("boss_shake_distance", "650"),
		("boss_clap_approach", "0.5"), ("boss_clap_tell", "1.3"), ("boss_clap_lock", "0.5"), ("boss_clap_strike", "0.4"), ("boss_clap_rest", "1.7"), ("boss_clap_recover", "0.6"),
		("boss_clap_damage", "40"), ("boss_clap_reach", "850"), ("boss_clap_gap", "45"),
		("boss_clap_wave", "900"), ("boss_clap_wave_seconds", "0.5"),
		("boss_ram_approach", "0.5"), ("boss_ram_tell", "0.8"), ("boss_ram_lock", "0.4"), ("boss_ram_strike", "1.1"), ("boss_ram_rest", "1.6"), ("boss_ram_recover", "1.3"),
		("boss_ram_damage", "40"), ("boss_ram_distance", "1300"), ("boss_ram_through", "1500"), ("boss_ram_crash_radius", "1300"), ("boss_ram_radius", "190"), ("boss_ram_exposed", "1.5"),
		("boss_rake_approach", "0.4"), ("boss_rake_tell", "1.2"), ("boss_rake_lock", "0.6"), ("boss_rake_strike", "0.7"), ("boss_rake_rest", "1.4"), ("boss_rake_recover", "0.8"),
		("boss_rake_damage", "22"), ("boss_rake_distance", "650"), ("boss_rake_through", "400"),
		("boss_march_approach", "1.2"), ("boss_march_tell", "1.1"), ("boss_march_lock", "0.5"), ("boss_march_strike", "2.2"), ("boss_march_rest", "1.5"), ("boss_march_recover", "0.9"),
		("boss_march_damage", "22"), ("boss_march_steps", "5"), ("boss_march_spacing", "320"), ("boss_march_hang", "320"), ("boss_march_palm_height", "25"), ("boss_march_head_back", "650"),
		("boss_rain_approach", "0.5"), ("boss_rain_tell", "0.8"), ("boss_rain_lock", "0.3"), ("boss_rain_strike", "2"), ("boss_rain_rest", "1.6"), ("boss_rain_recover", "1"),
		("boss_rain_bolts", "12"), ("boss_rain_radius", "900"), ("boss_rain_rise", "350"), ("boss_rain_exposed", "1.5"),
		("boss_grab_approach", "0.4"), ("boss_grab_tell", "1.2"), ("boss_grab_lock", "0.6"), ("boss_grab_strike", "0.5"), ("boss_grab_rest", "1.4"), ("boss_grab_recover", "1.9"),
		("boss_grab_damage", "30"), ("boss_grab_distance", "600"), ("boss_grab_through", "220"), ("boss_grab_hold_height", "420"), ("boss_grab_throw", "650"), ("boss_grab_height", "24"), ("boss_grab_head", "14"),
		("boss_summon_approach", "0.5"), ("boss_summon_tell", "1"), ("boss_summon_lock", "0.3"), ("boss_summon_strike", "1.4"), ("boss_summon_rest", "1.5"), ("boss_summon_recover", "1"),
		("boss_summon_cap", "10"), ("boss_summon_radius", "700"), ("boss_summon_cooldown", "18"), ("boss_summon_exposed", "1.5"),
		("boss_floor_sink", "8"), ("boss_death_rise", "420"), ("boss_laugh_min", "30"), ("boss_laugh_max", "60"),
		("boss_hp_hand", "600"), ("boss_hp_guard", "1350"), ("boss_hp_skull", "2150"), ("boss_wake_distance", "1800"),
		("boss_lava_damage", "2"), ("boss_lava_tick", "0.2"), ("boss_body_pushout", "True"),
	};

	/// <summary>Host, when a boss stands up: saved tunables from an older shipped set are reset to the current defaults, once.</summary>
	public static void EnsureFresh()
	{
		if ( SavedVersion == Version ) return;
		foreach ( var (name, def) in All ) ConsoleSystem.SetValue( name, def );
		SavedVersion = Version;
		Log.Info( $"[Boss] tunables reset to the shipped defaults (tuning v{Version}); `boss_tune` shows them" );
	}

	/// <summary>`boss_tune [reset]`: prints every boss tunable as `name value` (paste it back to be baked), or resets them all to the shipped defaults.</summary>
	[ConCmd( "boss_tune" )]
	public static void TuneCommand( string arg = "" )
	{
		if ( arg is "reset" )
		{
			foreach ( var (name, def) in All ) ConsoleSystem.SetValue( name, def );
			SavedVersion = Version;
			Log.Info( $"[Boss] {All.Length} tunables reset" );
			return;
		}
		var lines = new List<string>();
		foreach ( var (name, def) in All )
		{
			var v = ConsoleSystem.GetValue( name, def ) ?? def;
			lines.Add( v == def ? $"{name} {v}" : $"{name} {v}   (default {def})" );
		}
		Log.Info( "[Boss] tune:\n" + string.Join( "\n", lines ) );
	}

	/// <summary>`boss_slowmo [scale]`: host, the scene's time scale (0.3 to watch a strike; 1 back to normal). The director owns the clock now; this sets its speed.</summary>
	[ConCmd( "boss_slowmo" )]
	public static void SlowmoCommand( float scale = 0.3f )
	{
		if ( !Networking.IsHost ) { Log.Warning( "[Boss] host only" ); return; }
		Dev.DevDirector.SetSpeed( Math.Clamp( scale, 0.05f, 2f ) );
	}
}
