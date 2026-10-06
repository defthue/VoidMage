using System;
using Sandbox;

namespace VoidMage.Spells;

/// <summary>
/// The Void Lance's (the laser's) delivery: one <see cref="SpellBeam"/> per ray (Multiplication fans them). Every ray burns a
/// window and ends itself: a cast's own (the spell's BeamSeconds), a free copy's shorter one. The caster still holds
/// the rays so it can cut them short when the dev panel takes the mouse.
/// </summary>
public static class BeamEmitter
{
	public static void Emit( CastContext ctx ) => Emit( ctx, false );

	/// <param name="isReturn">The Return's ray back down the path: it never fires another.</param>
	public static void Emit( CastContext ctx, bool isReturn )
	{
		var count = Math.Max( 1, ctx.Count );
		var window = MathF.Max( 0.2f, ctx.BeamWindowSeconds > 0f ? ctx.BeamWindowSeconds : ctx.Spell.BeamSeconds )
			* ModifierTables.SlothBeamWindowMul[ctx.Stack( ModifierId.Sloth )];
		for ( var i = 0; i < count; i++ )
		{
			var go = new GameObject( true, $"beam {ctx.Spell.Id}" ) { WorldPosition = ctx.Origin, WorldRotation = Rotation.LookAt( ctx.Direction ) };
			go.Flags |= GameObjectFlags.NotSaved;
			go.Tags.Add( "spellfire" );
			var beam = go.Components.Create<SpellBeam>();
			beam.SpellId = ctx.Spell.Id;
			beam.Origin = ctx.Origin;
			beam.Direction = ProjectileEmitter.FanDirection( ctx.Direction, i, count, ctx.FanDegrees );
			beam.Scale = ctx.Spell.VfxScale * ctx.SizeScale;
			beam.Bounces = ctx.Flight.Bounces;
			// WallPierces stays 0: The Needle is bodies only (user, 2026-09-22)
			// the way home strikes everything it crosses, like a returning bolt
			beam.BodyPierces = isReturn ? 99 : ModifierTables.NeedleBodies[ctx.Stack( ModifierId.Needle )];
			beam.IgnoreBodyId = ctx.IgnoreBody.IsValid() ? ctx.IgnoreBody.Id : Guid.Empty;
			beam.FixedOrigin = ctx.FixedOrigin;
			beam.IsReturn = isReturn;
			beam.Active = true;
			beam.Context = ctx;
			beam.ForkIndex = i;
			beam.WindowSeconds = window;
			beam.Steers = !ctx.IsFree;
			go.NetworkSpawn();
			if ( !ctx.IsFree ) ctx.Caster?.RegisterBeam( beam );
		}
	}
}
