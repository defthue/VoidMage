using Sandbox;

namespace VoidMage.Spells;

/// <summary>
/// Where a cast leaves the staff and where it goes: the gem, swept from the eye so it is never inside a wall,
/// aimed at what the crosshair covers so the bolt converges on it (the fireball's original arithmetic, shared by
/// every delivery now).
/// </summary>
public static class AimSolver
{
	/// <summary>Tags no spell trace ever hits: the caster's own bits, other spell effects, triggers, debris, allies and corpses.</summary>
	public static readonly string[] IgnoreTags = { "player", "viewmodel", "posesource", "spellfire", "trigger", "ally", VoidMage.Combat.Debris.Tag };

	public readonly record struct Aim( Vector3 Origin, Vector3 Direction, Vector3 Target );

	/// <param name="eye">The camera / eye position.</param>
	/// <param name="staffTip">The gem, or null to fire from the eye.</param>
	/// <param name="radius">The projectile's radius (the muzzle sweep and the back-fire guard use it).</param>
	public static Aim Solve( Scene scene, GameObject casterRoot, Vector3 eye, Vector3? staffTip, float radius, float aimDistance )
	{
		var origin = staffTip ?? eye;

		// The staff's gem can be inside a wall when the player hugs it: the muzzle is swept from the eye and stops
		// on this side of whatever is in the way (heden pushes its muzzle out of the caster's capsule for the same reason).
		if ( staffTip.HasValue )
		{
			var muzzle = scene.Trace.Ray( eye, origin ).Radius( radius ).WithoutTags( IgnoreTags ).IgnoreGameObjectHierarchy( casterRoot ).Run();
			if ( muzzle.Hit ) origin = muzzle.StartedSolid ? eye : muzzle.EndPosition;
		}

		// Aim at what the crosshair covers: the camera's centre ray, so the bolt from the staff converges on it. A
		// target behind the muzzle (crosshair on a wall closer than the gem) would fire backward; then the bolt goes
		// straight down the view instead.
		var camera = scene.Camera;
		var forward = camera.IsValid() ? camera.WorldRotation.Forward : Vector3.Forward;
		var direction = forward;
		var target = origin + forward * aimDistance;
		if ( camera.IsValid() )
		{
			var aim = scene.Trace.Ray( camera.WorldPosition, camera.WorldPosition + forward * aimDistance ).WithoutTags( IgnoreTags ).IgnoreGameObjectHierarchy( casterRoot ).Run();
			target = aim.Hit ? aim.HitPosition : aim.EndPosition;
			var toTarget = target - origin;
			if ( Vector3.Dot( toTarget, forward ) > radius * 2f ) direction = toTarget.Normal;
		}
		if ( direction.IsNearlyZero() ) direction = Vector3.Forward;
		return new Aim( origin, direction, target );
	}
}
