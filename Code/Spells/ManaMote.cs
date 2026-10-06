using System;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Spells;

/// <summary>
/// The Harvest's mote: a small blue core that leaves a corpse, hangs a beat and flies to the caster's chest. Built on
/// every peer by SpellNet.ManaMote and never networked; the peer that owns the caster puts the mana in the pool when
/// its own copy arrives.
/// </summary>
public sealed class ManaMote : Component
{
	public GameObject Target { get; set; }
	public float Mana { get; set; }

	private FireballVfx.Rig _rig;
	private float _born;
	private Vector3 _drift;

	protected override void OnStart()
	{
		_born = Time.Now;
		_drift = new Vector3( Game.Random.Float( -1f, 1f ), Game.Random.Float( -1f, 1f ), 1.4f ).Normal * 70f;
		_rig = FireballVfx.BuildCore( GameObject, "mote", false, SpellPalette.Ice );
		FireballVfx.SetScale( _rig, 0.14f );
		FireballVfx.SetSpread( _rig, 0.14f );
		if ( _rig is not null ) _rig.LightBrightness = 0.2f;
	}

	protected override void OnUpdate()
	{
		if ( !Target.IsValid() || Time.Now - _born > 4f ) { GameObject.Destroy(); return; }
		FireballVfx.Drive( _rig );
		var age = Time.Now - _born;
		if ( age < 0.3f )
		{
			// out of the body first, so it reads as something the corpse gave up
			WorldPosition += _drift * (1f - age / 0.3f) * Time.Delta;
			return;
		}
		var chest = Target.WorldPosition + Vector3.Up * 46f;
		var to = chest - WorldPosition;
		var speed = 500f + 1600f * (age - 0.3f);
		if ( to.Length <= speed * Time.Delta + 12f )
		{
			var mana = Target.Components.Get<PlayerMana>( FindMode.EverythingInSelfAndDescendants );
			if ( mana.IsValid() && !mana.IsProxy )
			{
				// a Hollow staff never spends mana: the harvest comes back as the blood it casts with
				var staff = Target.Components.Get<StaffLoadout>( FindMode.EverythingInSelfAndDescendants );
				var health = staff.IsValid() && staff.HasModifier( ModifierId.Hollow ) ? Target.Components.Get<PlayerHealth>( FindMode.EverythingInSelfAndDescendants ) : null;
				if ( health.IsValid() ) health.Heal( Mana * ModifierTables.HollowHealthPerMana );
				else mana.Restore( Mana );
			}
			GameObject.Destroy();
			return;
		}
		WorldPosition += to.Normal * speed * Time.Delta;
	}

	protected override void OnDestroy() => FireballVfx.Destroy( _rig );
}
