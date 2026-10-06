"""Write Assets/animgraphs/boss_hand.vanmgrph: the graph both Bone Colossus hands play (boss/hand_r.vmdl and
boss/hand_l.vmdl share the rig and the clip names, tools/rig_hell_hand.py).

One state machine on the float `hand_pose`:
    0 idle   `twitch` looping (the sculpt's own claw with a few degrees of drift)
    1 open   2 fist   3 point   4 grab   5 splay     six-frame static poses, held at their last frame
Every state can go to every other the moment the parameter says so, blended over BLEND seconds, so the code
sets `hand_pose` per attack phase (fist for a slam, splay for a sweep, grab at rest) and the fingers ease
there. The whole-hand motion (hover, slam, sweep) is the GameObject's transform, driven by Code/Boss.

    python tools/gen_boss_hand_animgraph.py
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from gen_fp_arms_animgraph import sequence, root, transition, state, state_machine, param_float  # noqa: E402
from gen_skeleton_animgraph import cond_float  # noqa: E402

OUT = os.path.join(ROOT, "Assets", "animgraphs", "boss_hand.vanmgrph")
MODEL = "boss/hand_r.vmdl"

POSES = ["idle", "open", "fist", "point", "grab", "splay"]
CLIPS = {"idle": ("twitch", True), "open": ("open", False), "fist": ("fist", False),
         "point": ("point", False), "grab": ("grab", False), "splay": ("splay", False)}
BLEND = 0.3

# ids: band 2700000xxx
P_POSE = 2700000001
N_ROOT, N_SM = 2700000010, 2700000011
N_SEQ = {p: 2700000020 + k for k, p in enumerate(POSES)}
S_POSE = {p: 2700000040 + k for k, p in enumerate(POSES)}


def main():
    nodes = []
    for k, p in enumerate(POSES):
        clip, loop = CLIPS[p]
        nodes.append((N_SEQ[p], sequence(N_SEQ[p], p, clip, loop, 1.0, (-640, -256 + 96 * k))))
    states = []
    for k, p in enumerate(POSES):
        exits = [transition([cond_float(P_POSE, j)], S_POSE[q], BLEND) for j, q in enumerate(POSES) if q != p]
        states.append(state(S_POSE[p], p, N_SEQ[p], exits, p == "idle", (-128, -256 + 96 * k)))
    nodes.append((N_SM, state_machine(N_SM, "hand", states, (128, 0))))
    nodes.append((N_ROOT, root(N_ROOT, N_SM, (384, 0))))
    nodes.sort(key=lambda n: n[0])
    params = param_float(P_POSE, "hand_pose", 0.0, float(len(POSES) - 1))

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
    print("wrote", OUT, "states", len(states))


if __name__ == "__main__":
    main()
