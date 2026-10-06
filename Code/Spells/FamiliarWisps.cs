using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Spells;

/// <summary>
/// The Familiar's wisps: one small core of the staff's spell per copy of the scroll, circling the caster at shoulder
/// height. Built on every peer from the synced <see cref="StaffLoadout"/>, never networked: where wisp `i` is at
/// time `t` is a pure function (<see cref="PositionOf"/>), which is also where the caster throws its miniature from.
/// </summary>
[Title( "Familiar Wisps" ), Category( "Void Mage" ), Icon( "bubble_chart" )]
public sealed class FamiliarWisps : Component
{
	public const float Radius = 42f;
	public const float DegreesPerSecond = 80f;

	private readonly List<FireballVfx.Rig> _rigs = new();
	private string _builtFor = "";
	private GameObject _root;
	private SpellCaster _caster;

	/// <summary>Every peer, every frame, from the caster: the component exists only while the staff carries the scroll.</summary>
	public static void Sync( SpellCaster caster )
	{
		var wisps = ModifierTables.FamiliarWisps[caster.Loadout?.Stack( ModifierId.Familiar ) ?? 0];
		var existing = caster.Components.Get<FamiliarWisps>();
		if ( wisps <= 0 )
		{
			if ( existing.IsValid() ) existing.Destroy();
			return;
		}
		if ( !existing.IsValid() )
		{
			existing = caster.Components.Create<FamiliarWisps>();
			existing.Flags |= ComponentFlags.NotSaved;
			existing._caster = caster;
		}
	}

	/// <summary>Where wisp `i` of `count` is: a slow ring round the chest, each bobbing on its own beat.</summary>
	public static Vector3 PositionOf( GameObject playerRoot, int i, int count, float time )
	{
		if ( !playerRoot.IsValid() ) return Vector3.Zero;
		var controller = playerRoot.Components.Get<PlayerController>();
		var centre = controller.IsValid() ? controller.EyePosition - Vector3.Up * 6f : playerRoot.WorldPosition + Vector3.Up * 58f;
		var yaw = time * DegreesPerSecond + 360f * i / Math.Max( 1, count );
		return centre + Rotation.FromYaw( yaw ).Forward * Radius + Vector3.Up * (MathF.Sin( time * 2.1f + i * 1.7f ) * 5f);
	}

	protected override void OnUpdate()
	{
		if ( !_caster.IsValid() ) { Destroy(); return; }
		var spell = _caster.ActiveSpell;
		var count = ModifierTables.FamiliarWisps[_caster.Loadout?.Stack( ModifierId.Familiar ) ?? 0];
		var key = $"{spell?.Id}:{count}";
		if ( key != _builtFor ) Rebuild( spell, count, key );
		for ( var i = 0; i < _rigs.Count; i++ )
		{
			var rig = _rigs[i];
			if ( rig?.Root is null || !rig.Root.IsValid() ) continue;
			rig.Root.WorldPosition = PositionOf( _caster.PlayerRoot, i, _rigs.Count, Time.Now );
			FireballVfx.Drive( rig );
		}
	}

	private void Rebuild( SpellDefinition spell, int count, string key )
	{
		Clear();
		_builtFor = key;
		if ( spell is null || count <= 0 ) return;
		_root = new GameObject( true, "familiar wisps" );
		_root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		for ( var i = 0; i < count; i++ )
		{
			var rig = FireballVfx.BuildCore( _root, $"wisp {i}", false, spell.Palette );
			FireballVfx.SetScale( rig, 0.16f );
			FireballVfx.SetSpread( rig, 0.16f );
			rig.LightBrightness = 0.25f;
			_rigs.Add( rig );
		}
	}

	private void Clear()
	{
		foreach ( var rig in _rigs ) FireballVfx.Destroy( rig );
		_rigs.Clear();
		if ( _root.IsValid() ) _root.Destroy();
		_root = null;
	}

	protected override void OnDestroy() => Clear();
	protected override void OnDisabled() => Clear();
	protected override void OnEnabled() => _builtFor = "";
}
