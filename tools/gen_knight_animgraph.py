"""Write Assets/animgraphs/undead_knight.vanmgrph: the Undead Knight's graph (undead_knight.vmdl).

The skeleton graph's layout (gen_skeleton_animgraph.py: loco / guard_loco radial blends, guard_in / guard_out,
six hurt states, `busy`) with the attacks replaced by ALL THIRTEEN `attack_*` clips of the Sword & Shield pack, one
state each, `attack_kind` 1..13 in ATTACK_CLIPS order -- the same order gen_knight_prefab.py lists the attack table
in, so kind N in the prefab is state N here (the prefab generator asserts it). Every attack state can flow straight
into any other on a new pulse (a combo: SkeletonNpc.TryCombo pulses the next kind the frame the first ends), which
the skeleton graph never needed.

Attacks play at 1.0 (the user's "don't speed clips up"; a big deliberate knight): SkeletonNpc's attack table seconds
are measured at 1.0 (tools probe, 2026-09-12). The hurt states are the shield's: 4 guard_light_hit (the shield
holds), 5 guard_heavy_hit (the guard broken by a heavy, and the posture stagger). 0-3 exist for completeness.
Death is a ragdoll (SkeletonCorpse), no clip.

    python tools/gen_knight_animgraph.py
"""
import os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from gen_fp_arms_animgraph import (sequence, root, cond_param, cond_finished, transition,  # noqa: E402
                                   state, state_machine, param_bool, param_float)
from gen_skeleton_animgraph import loco_items, blend2d, cond_float, RUN_RADIUS  # noqa: E402

OUT = os.path.join(ROOT, "Assets", "animgraphs", "undead_knight.vanmgrph")
MODEL = "characters/undeadknight/undead_knight.vmdl"

ATTACK_SPEED = 1.0
HURT_SPEED = 1.4
GUARD_HIT_SPEED = 1.4
GUARD_BREAK_SPEED = 1.0     # the break / stagger clip at its authored pace
GUARD_IO_SPEED = 1.5

# kind N = ATTACK_CLIPS[N-1]; gen_knight_prefab.py's table is in this order
ATTACK_CLIPS = ["attack_a_01", "attack_a_02", "attack_a_03",
                "attack_b_01", "attack_b_02", "attack_b_03_a", "attack_b_03_b", "attack_b_03_c", "attack_b_03_d",
                "attack_c_02", "attack_c_03_a",
                "attack_d_01", "attack_d_02"]
HURT_CLIPS = [("light_hurt_f", HURT_SPEED), ("light_hurt_b", HURT_SPEED), ("light_hurt_l", HURT_SPEED),
              ("light_hurt_r", HURT_SPEED), ("guard_light_hit", GUARD_HIT_SPEED), ("guard_heavy_hit", GUARD_BREAK_SPEED)]

# ---------------------------------------------------------------- ids (band 2700000xxx, unique)
P_MX, P_MY, P_GUARD, P_ATK, P_AKIND, P_HURT, P_HKIND, P_DEAD, P_DKIND = range(2700000001, 2700000010)
N_ROOT, N_SM, N_LOCO, N_GUARD_LOCO, N_GUARD_IN, N_GUARD_OUT = range(2700000010, 2700000016)
P_BUSY = 2700000016
KINDS = list(range(1, len(ATTACK_CLIPS) + 1))
N_ATTACK = {k: 2700000100 + k for k in KINDS}
S_ATTACK = {k: 2700000200 + k for k in KINDS}
N_HURT = [2700000030 + k for k in range(len(HURT_CLIPS))]
S_LOCO, S_GUARD_IN, S_GUARD_LOCO, S_GUARD_OUT = range(2700000050, 2700000054)
S_HURT = [2700000070 + k for k in range(len(HURT_CLIPS))]

BLEND_LOCO = 0.2
BLEND_GUARD = 0.15
BLEND_ATTACK_IN, BLEND_ATTACK_OUT = 0.08, 0.2
BLEND_HURT_IN, BLEND_HURT_OUT = 0.05, 0.2


def interrupts():
    return [transition([cond_param(P_HURT, True), cond_float(P_HKIND, k)], S_HURT[k], BLEND_HURT_IN) for k in range(len(HURT_CLIPS))]


def attacks(exclude=None):
    return [transition([cond_param(P_ATK, True), cond_float(P_AKIND, k)], S_ATTACK[k], BLEND_ATTACK_IN) for k in KINDS if k != exclude]


def back_to_loco(blend):
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
    ]
    for k in KINDS:
        nodes.append((N_ATTACK[k], sequence(N_ATTACK[k], f"attack_{k}", ATTACK_CLIPS[k - 1], False, ATTACK_SPEED, (-640, 64 + 64 * k))))
    for k, (clip, speed) in enumerate(HURT_CLIPS):
        nodes.append((N_HURT[k], sequence(N_HURT[k], f"hurt_{k}", clip, False, speed, (-960, 64 + 64 * k))))

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
    ]
    for k in KINDS:
        # a combo: the next attack's pulse lands while this state plays (a different kind, so the held pulse of
        # this one can never re-enter it)
        states.append(state(S_ATTACK[k], f"attack_{k}", N_ATTACK[k], interrupts() + attacks(exclude=k) + back_to_loco(BLEND_ATTACK_OUT),
                            False, (0, 64 + 64 * k)))
    for k in range(len(HURT_CLIPS)):
        states.append(state(S_HURT[k], f"hurt_{k}", N_HURT[k], interrupts() + attacks() + back_to_loco(BLEND_HURT_OUT), False, (512, 128 + 64 * k)))

    nodes.append((N_SM, state_machine(N_SM, "Knight", states, (-96, 0))))
    nodes.append((N_ROOT, root(N_ROOT, N_SM, (256, 0))))
    nodes.sort(key=lambda n: n[0])

    params = "".join([
        param_float(P_MX, "move_x", -1.0, 1.0),
        param_float(P_MY, "move_y", -RUN_RADIUS, RUN_RADIUS),
        param_bool(P_GUARD, "guard"),
        param_bool(P_BUSY, "busy"),
        param_bool(P_ATK, "attack_trigger"),
        param_float(P_AKIND, "attack_kind", 0.0, float(len(ATTACK_CLIPS))),
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
    print("wrote", OUT, "nodes", len(nodes), "states", len(states), "attack kinds", len(KINDS))


if __name__ == "__main__":
    main()
