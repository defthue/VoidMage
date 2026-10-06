using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// Where a sword sits in a fist, built from the hand's own joints instead of the prop bone's axes:
/// the blade runs along the knuckle line toward the thumb, its flat faces the palm, the handle's middle
/// sits a little in front of the hand joint on the palm side. The same math places the first-person sword
/// and the third-person one, so one tuned offset serves both. Sword models are expected with the blade
/// along +Z, the crossguard along X and the origin at the guard (the DungeonPack sm_wep_* swords).
/// </summary>
public static class SwordGrip
{
	/// <summary>
	/// hand, index, pinky, middle, thumb: world (or any one consistent space) positions of hand_r,
	/// index_01_r, pinky_01_r, middle_01_r, thumb_01_r. gripOffset is in the fist's frame:
	/// x along the fingers, y toward the palm, z along the blade. extra is a final local rotation trim.
	/// </summary>
	public static Transform Compute( Vector3 hand, Vector3 index, Vector3 pinky, Vector3 middle, Vector3 thumb,
		Vector3 gripOffset, Angles extra, bool flipPalm )
	{
		var fingers = (middle - hand).Normal;
		var knuckles = (index - pinky).Normal;

		// Palm normal: perpendicular to the finger direction and the knuckle line, on the thumb's side.
		var palm = Vector3.Cross( fingers, knuckles ).Normal;
		if ( Vector3.Dot( palm, thumb - hand ) < 0f ) palm = -palm;
		if ( flipPalm ) palm = -palm;

		// Blade: the knuckle line made perpendicular to the palm normal (the thumb side of the fist).
		var blade = (knuckles - palm * Vector3.Dot( knuckles, palm )).Normal;
		// Crossguard/edge axis completes a right-handed frame whose Y is the palm normal.
		var edge = Vector3.Cross( palm, blade ).Normal;

		var rotation = Rotation.LookAt( edge, blade );
		var position = hand + fingers * gripOffset.x + palm * gripOffset.y + blade * gripOffset.z;

		return new Transform( position, rotation * extra.ToRotation() );
	}
}
