using System.Linq;
using Sandbox;

namespace VoidMage.Village;

/// <summary>
/// The village's day and night in one session (the main menu is a sunny afternoon; Play breaks the storm and it
/// is night from then on). Three things make the look, and this flips all three at once, under the menu's flash:
///  - the materials: the scene-wide "VoidNight" attribute, which the 14 outdoor vmats answer through the toon
///    core's Toon Night keys (tools/village_night_vmats.py writes both presets into them);
///  - the sun's colour;
///  - the sky, which is only the camera's background colour (the scene has no skybox).
/// The NIGHT sun and sky are whatever the scene file holds (the user tuned those in the editor), read once before
/// the first change; the day values are the properties here. The rain and the fog are RunManager's (SetNight).
/// Lives on the RunManager's object; one per client, nothing networked.
/// </summary>
[Title( "Village Sky" ), Category( "Void Mage" ), Icon( "wb_twilight" )]
public sealed class VillageSky : Component
{
	public const string NightAttribute = "VoidNight";

	[Property] public Color DaySun { get; set; } = new( 1f, 0.95f, 0.84f );
	/// <summary>The camera's background at day: the whole sky.</summary>
	[Property] public Color DaySky { get; set; } = new( 0.47f, 0.68f, 0.9f );

	/// <summary>The village is in its stormy night. False on the main menu, true from Play on.</summary>
	public static bool Night { get; private set; }

	private DirectionalLight _sun;
	private Color _nightSun;
	private Color _nightSky;
	private bool _captured;

	public static VillageSky EnsureOn( GameObject go ) => go.Components.GetOrCreate<VillageSky>();

	/// <summary>Call at home only: on a floor the sun is switched off with the rest of the village and cannot be found.</summary>
	public void Set( bool night )
	{
		var camera = Scene.Camera;
		if ( !_captured )
		{
			_sun = Scene.GetAllComponents<DirectionalLight>().FirstOrDefault( l => l.IsValid() );
			if ( _sun.IsValid() ) _nightSun = _sun.LightColor;
			if ( camera.IsValid() ) _nightSky = camera.BackgroundColor;
			_captured = true;
		}
		Night = night;
		Scene.RenderAttributes.Set( NightAttribute, night ? 1f : 0f );
		if ( _sun.IsValid() ) _sun.LightColor = night ? _nightSun : DaySun;
		if ( camera.IsValid() ) camera.BackgroundColor = night ? _nightSky : DaySky;
	}

	protected override void OnDestroy()
	{
		Night = false;
	}
}
