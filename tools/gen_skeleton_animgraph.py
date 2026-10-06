"""Write Assets/animgraphs/skeleton_slave.vanmgrph: the graph of the skeleton NPC (skeleton_slave_01.vmdl).

One state machine, the way heden's goblin/boss graphs are built (Code/Npc/SkeletonBody.cs drives it):

    loco        = 2D blend on (move_x, move_y): idle at the centre, the 6 walk cycles at radius 0.5, the 6 jog
                  cycles at radius 1, `run` (forward only) at y 1.5 (no FL/FR clips in the pack: those corners
                  blend F with L/R)
    guard_loco  = the same layout with guard_loop / guard_walk_* / guard_jog_* (no guard run)
    guard_in / guard_out = gaurd_start (pack typo) / guard_end, one-shots between the two
    attacks     = bool `attack_trigger` + float `attack_kind` equality on the transition (no int params in any
                  shipped graph): 1 attack_d_01, 2 attack_d_02, 3 charge (charge_attack_start = the telegraph ->
                  charge_attack_to_attack = the overhead chop; the pack's charge_attack_end has no strike, it is
                  the un-brace clip), 4 guard_attack_parry
    hurts       = `hurt_trigger` + `hurt_kind`: 0 light_hurt_f, 1 _b, 2 _l, 3 _r, 4 guard_light_hit,
                  5 guard_heavy_hit (the guard broken: shield knocked aside, ~1 s exposed)
    dead        = `dead` + `death_kind` 0..2 -> die_01/02/03, no way out
    busy        = bool, true while the host is in its Attack or Hurt state: attack and hurt states also leave
                  when it drops, so the code cuts recoveries short (the sword clips carry 1.2 s of recovery).
Every non-dead state can be interrupted by a hurt or a death, and every state that is not itself an attack
accepts the attack trigger (guard_in/out and the hurts included: a pulse that lands there must not be lost, or
the host hits on its timer with no clip). Attacks and hurts return to loco or guard_loco by the `guard` bool.
Triggers are plain bools the code holds for two frames (heden's proven pattern; no state has a self attack
transition, so the hold cannot double-fire).

Emitters are shared with gen_fp_arms_animgraph.py; this adds the 2D blend node and float comparisons.
Regenerate the vmdl first (gen_skeleton_npc_vmdl.py names this graph).

    python tools/gen_skeleton_animgraph.py
"""
import os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from gen_fp_arms_animgraph import (NONE, conn, node, sequence, root, cond_param, cond_finished, transition,  # noqa: E402
                                   state, state_machine, param_bool, param_float)

OUT = os.path.join(ROOT, "Assets", "animgraphs", "skeleton_slave.vanmgrph")
MODEL = "characters/skeletonslave/skeleton_slave_01.vmdl"

# Playback speeds (pacing knobs; SkeletonNpc's attack specs and HurtSeconds are in seconds AT these speeds).
# Hurts read slow at 1.0 (0.97 s clips); the sword attacks are 1.5 s clips, a quick hack and slash wants ~1.3.
HURT_SPEED = 1.4
GUARD_HIT_SPEED = 1.4
GUARD_BREAK_SPEED = 1.15
ATTACK_SPEED = 1.15
CHARGE_SPEED = 1.15
PARRY_SPEED = 1.15
DEATH_SPEED = 1.0
GUARD_IO_SPEED = 1.5

# ---------------------------------------------------------------- ids (band 2400000xxx, unique)
P_MX, P_MY, P_GUARD, P_ATK, P_AKIND, P_HURT, P_HKIND, P_DEAD, P_DKIND = range(2400000001, 2400000010)
N_ROOT, N_SM, N_LOCO, N_GUARD_LOCO, N_GUARD_IN, N_GUARD_OUT = range(2400000010, 2400000016)
P_BUSY = 2400000016
N_ATTACK = {1: 2400000020, 2: 2400000021, 4: 2400000022}       # kind -> sequence node
N_CHARGE_START, N_CHARGE_HIT = 2400000023, 2400000024
N_HURT = [2400000030 + k for k in range(6)]
N_DEATH = [2400000040 + k for k in range(3)]
S_LOCO, S_GUARD_IN, S_GUARD_LOCO, S_GUARD_OUT = range(2400000050, 2400000054)
S_ATTACK = {1: 2400000060, 2: 2400000061, 4: 2400000062}
S_CHARGE_START, S_CHARGE_HIT = 2400000063, 2400000064
S_HURT = [2400000070 + k for k in range(6)]
S_DEATH = [2400000080 + k for k in range(3)]

ATTACK_CLIPS = {1: ("attack_d_01", ATTACK_SPEED), 2: ("attack_d_02", ATTACK_SPEED), 4: ("guard_attack_parry", PARRY_SPEED)}
HURT_CLIPS = [("light_hurt_f", HURT_SPEED), ("light_hurt_b", HURT_SPEED), ("light_hurt_l", HURT_SPEED),
              ("light_hurt_r", HURT_SPEED), ("guard_light_hit", GUARD_HIT_SPEED), ("guard_heavy_hit", GUARD_BREAK_SPEED)]
DEATH_CLIPS = ["die_01", "die_02", "die_03"]

# (x right, y forward) like heden's goblin blend; walk at 0.5, jog at 1.0, run at 1.5 (forward only, plain loco).
# Diagonals at 0.7 * radius. Radii are speed tiers only; the code maps measured u/s onto them (SkeletonBody).
DIRS = [("f", (0, 1)), ("b", (0, -1)), ("l", (-1, 0)), ("r", (1, 0)), ("bl", (-0.7, -0.7)), ("br", (0.7, -0.7))]
RUN_RADIUS = 1.5

BLEND_LOCO = 0.2
BLEND_GUARD = 0.15
BLEND_ATTACK_IN, BLEND_ATTACK_OUT = 0.08, 0.2
BLEND_HURT_IN, BLEND_HURT_OUT = 0.05, 0.2
BLEND_DEAD = 0.1


def loco_items(prefix, centre, run=None):
    items = [(centre, (0.0, 0.0))]
    for gait, radius in (("walk", 0.5), ("jog", 1.0)):
        for d, (x, y) in DIRS:
            items.append((f"{prefix}{gait}_{d}", (x * radius, y * radius)))
    if run:
        items.append((run, (0.0, RUN_RADIUS)))
    return items


def blend2d(i, name, param_x, param_y, items, pos, speed=1.0):
    """`speed` scales the whole node's playback: the monsters' in-place walk cycles are played faster than
    authored so a creature the size of a rat can still travel at a usable speed without sliding."""
    body = "".join(f'''						{{
							_class = "CBlend2DItem"
							m_tagSpans = [  ]
							m_sequenceName = "{clip}"
							m_blendValue = [ {x:.3f}, {y:.3f} ]
						}},
''' for clip, (x, y) in items)
    return node(i, f'''					_class = "CBlend2DAnimNode"
					m_sName = "{name}"
					m_vecPosition = [ {pos[0]:.1f}, {pos[1]:.1f} ]
					m_nNodeID =
					{{
						m_id = {i}
					}}
					m_sNote = ""
					m_items =
					[
{body}					]
					m_blendSourceX = "Parameter"
					m_paramX =
					{{
						m_id = {param_x}
					}}
					m_blendSourceY = "Parameter"
					m_paramY =
					{{
						m_id = {param_y}
					}}
					m_bLoop = true
					m_bLockBlendOnReset = false
					m_bLockWhenWaning = false
					m_playbackSpeed = {speed:.4f}
					m_damping =
					{{
						_class = "CAnimInputDamping"
						m_speedFunction = "NoDamping"
						m_fSpeedScale = 1.0
						m_fMinSpeed = 10.0
						m_fMaxTension = 1000.0
						m_fTension = 100.0
						m_fDamping = 5.0
					}}''')


def cond_float(param, value, op=0):
    """op 0 == equal, 2 greater, 4 less (heden's graphs)."""
    return f'''								{{
									_class = "CParameterAnimCondition"
									m_comparisonOp = {op}
									m_paramID =
									{{
										m_id = {param}
									}}
									m_comparisonValue =
									{{
										m_nType = 4
										m_data = {value:.1f}
									}}
								}},
'''


def interrupts():
    """Hurt transitions, in priority order, shared by every state (death is a ragdoll, not a clip: SkeletonCorpse)."""
    return [transition([cond_param(P_HURT, True), cond_float(P_HKIND, k)], S_HURT[k], BLEND_HURT_IN) for k in range(len(HURT_CLIPS))]


def attacks():
    out = []
    for kind in (1, 2, 4):
        out.append(transition([cond_param(P_ATK, True), cond_float(P_AKIND, kind)], S_ATTACK[kind], BLEND_ATTACK_IN))
    out.append(transition([cond_param(P_ATK, True), cond_float(P_AKIND, 3)], S_CHARGE_START, BLEND_ATTACK_IN))
    return out


def back_to_loco(blend):
    """Leave a one-shot when the clip ends OR when the host says it is no longer busy (recovery cut short)."""
    out = []
    for cond in (cond_finished(), cond_param(P_BUSY, False)):
        out.append(transition([cond, cond_param(P_GUARD, False)], S_LOCO, blend))
        out.append(transition([cond, cond_param(P_GUARD, True)], S_GUARD_LOCO, blend))
    return out


def main():
    nodes = [
        (N_LOCO, blend2d(N_LOCO, "loco", P_MX, P_MY, loco_items("", "idle", "run"), (-640, -256))),
        (N_GUARD_LOCO, blend2d(N_GUARD_LOCO, "guard_loco", P_MX, P_MY, loco_items("guard_", "guard_loop"), (-640, -160))),
        (N_GUARD_IN, sequence(N_GUARD_IN, "guard_in", "gaurd_start", False, GUARD_IO_SPEED, (-640, -64))),
        (N_GUARD_OUT, sequence(N_GUARD_OUT, "guard_out", "guard_end", False, GUARD_IO_SPEED, (-640, 0))),
        (N_CHARGE_START, sequence(N_CHARGE_START, "charge_start", "charge_attack_start", False, CHARGE_SPEED, (-640, 256))),
        (N_CHARGE_HIT, sequence(N_CHARGE_HIT, "charge_hit", "charge_attack_to_attack", False, CHARGE_SPEED, (-640, 320))),
    ]
    for kind, (clip, speed) in ATTACK_CLIPS.items():
        nodes.append((N_ATTACK[kind], sequence(N_ATTACK[kind], f"attack_{kind}", clip, False, speed, (-640, 64 + 64 * kind))))
    for k, (clip, speed) in enumerate(HURT_CLIPS):
        nodes.append((N_HURT[k], sequence(N_HURT[k], f"hurt_{k}", clip, False, speed, (-640, 416 + 64 * k))))

    states = [
        state(S_LOCO, "loco", N_LOCO, interrupts() + attacks() + [
            transition([cond_param(P_GUARD, True)], S_GUARD_IN, BLEND_GUARD),
        ], True, (0, -256)),
        state(S_GUARD_IN, "guard_in", N_GUARD_IN, interrupts() + attacks() + [
            transition([cond_finished()], S_GUARD_LOCO, BLEND_GUARD),
            transition([cond_param(P_GUARD, False)], S_GUARD_OUT, BLEND_GUARD),
        ], False, (256, -256)),
        state(S_GUARD_LOCO, "guard_loco", N_GUARD_LOCO, interrupts() + attacks() + [
            transition([cond_param(P_GUARD, False)], S_GUARD_OUT, BLEND_GUARD),
        ], False, (512, -256)),
        state(S_GUARD_OUT, "guard_out", N_GUARD_OUT, interrupts() + attacks() + [
            transition([cond_finished()], S_LOCO, BLEND_LOCO),
            transition([cond_param(P_GUARD, True)], S_GUARD_IN, BLEND_GUARD),
        ], False, (256, -128)),
        state(S_CHARGE_START, "charge_start", N_CHARGE_START, interrupts() + [
            transition([cond_finished()], S_CHARGE_HIT, 0.05),
        ] + back_to_loco(BLEND_ATTACK_OUT)[2:], False, (0, 128)),   # only the busy-false exits: finished goes to the chop
        state(S_CHARGE_HIT, "charge_hit", N_CHARGE_HIT, interrupts() + back_to_loco(BLEND_ATTACK_OUT), False, (256, 128)),
    ]
    for kind in (1, 2, 4):
        states.append(state(S_ATTACK[kind], f"attack_{kind}", N_ATTACK[kind], interrupts() + back_to_loco(BLEND_ATTACK_OUT),
                            False, (0, 192 + 64 * kind)))
    for k in range(len(HURT_CLIPS)):
        states.append(state(S_HURT[k], f"hurt_{k}", N_HURT[k], interrupts() + attacks() + back_to_loco(BLEND_HURT_OUT), False, (512, 128 + 64 * k)))

    nodes.append((N_SM, state_machine(N_SM, "Skeleton", states, (-96, 0))))
    nodes.append((N_ROOT, root(N_ROOT, N_SM, (256, 0))))
    nodes.sort(key=lambda n: n[0])

    params = "".join([
        param_float(P_MX, "move_x", -1.0, 1.0),
        param_float(P_MY, "move_y", -RUN_RADIUS, RUN_RADIUS),
        param_bool(P_GUARD, "guard"),
        param_bool(P_BUSY, "busy"),
        param_bool(P_ATK, "attack_trigger"),
        param_float(P_AKIND, "attack_kind", 0.0, 4.0),
        param_bool(P_HURT, "hurt_trigger"),
        param_float(P_HKIND, "hurt_kind", 0.0, float(len(HURT_CLIPS) - 1)),
    ])

    out = f'''<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:animgraph2:version{{0f7898b8-5471-45c4-9867-cd9c46bcfdb5}} -->
{{
	_class = "CAnimationGraph"
	m_nodeManager =
	{{
		_class = "CAnimNodeManager"
		m_nodes =
		[
{"".join(body for _, body in nodes)}		]
	}}
	m_pParameterList =
	{{
		_class = "CAnimParameterList"
		m_Parameters =
		[
{params}		]
	}}
	m_pTagManager =
	{{
		_class = "CAnimTagManager"
		m_tags = [  ]
	}}
	m_pMovementManager =
	{{
		_class = "CAnimMovementManager"
		m_MotorList =
		{{
			_class = "CAnimMotorList"
			m_motors = [  ]
		}}
		m_MovementSettings =
		{{
			_class = "CAnimMovementSettings"
			m_bShouldCalculateSlope = false
		}}
	}}
	m_pSettingsManager =
	{{
		_class = "CAnimGraphSettingsManager"
		m_settingsGroups =
		[
			{{
				_class = "CAnimGraphGeneralSettings"
				m_iGridSnap = 16
			}},
		]
	}}
	m_pActivityValuesList =
	{{
		_class = "CActivityValueList"
		m_activities = [  ]
	}}
	m_previewModels =
	[
		"{MODEL}",
	]
	m_boneMergeModels = [  ]
	m_cameraSettings =
	{{
		m_flFov = 60.0
		m_sLockBoneName = ""
		m_bLockCamera = false
		m_bViewModelCamera = false
	}}
}}
'''
    with open(OUT, "w", newline="\n") as f:
        f.write(out)
    print("wrote", OUT, "nodes", len(nodes), "states", len(states))


if __name__ == "__main__":
    main()
