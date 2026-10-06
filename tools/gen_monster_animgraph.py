"""Write Assets/animgraphs/monster_<kind>.vanmgrph, one per Quaternius monster (tools/monster_kinds.py).

Same parameter names as the skeleton and Tormented Soul graphs, because Code/Npc/SkeletonBody.cs drives all
of them: move_x / move_y / guard / busy / attack_kind / attack_trigger / hurt_kind / hurt_trigger / dead.
Every parameter is declared even where the monster has no clip for it, so SkeletonBody's Set() always has a
target (the soul graph does the same with `guard`).

    python tools/gen_monster_animgraph.py [--kind bat]

Three things these packs force that the skeleton graph never had to deal with:

* **No monster has a directional clip set.** There is one walk (or one flying, or one hop) and that is all.
  So the 2D blend puts THE SAME clip at every point of the ring: the creature always turns to face where it
  is going, so it only ever samples near (0, +y), and the ring exists purely to give the blend node a real
  2D hull to triangulate. A degenerate collinear item list is not something to find out about in the editor.
* **Only the bat and the dragon have a hit clip.** Everything else gets no hurt states at all, and
  SkeletonNpc.CanStagger is off for them, so a hit is a flash and a flinch and never a state with no
  animation behind it.
* **No monster uses its death clip** (the user's call): they all ragdoll, so there is no `dead` state.
  The `dead` parameter still exists for SkeletonBody.
* **A hopper's locomotion is a one-shot, not a loop.** The frog travels by jumping, and a blend2d item loops
  on its own clock, so the clip's phase can never line up with the travel — it skated along with a jump
  playing over the top. A kind with `hop` in monster_kinds.py gets `idle` at every point of the loco ring and
  a `hop` STATE on the jump clip, entered on `hop_trigger` (a state entry resets the clip to frame 0, which
  is the whole point). `hop_trigger` is declared in every monster's graph so `SkeletonBody.Set()` always has
  a target, exactly as `guard` is.

The locomotion node's playback speed is the kind's `loco.playback`: these clips animate in place, so the
body's travel speed is the clip's own stance-foot speed times that (see gen_monster_prefabs.py).
"""
import argparse, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from gen_fp_arms_animgraph import (sequence, root, cond_param, cond_finished, transition,  # noqa: E402
                                   state, state_machine, param_bool, param_float)
from gen_skeleton_animgraph import blend2d, cond_float  # noqa: E402
import monster_kinds  # noqa: E402

ATTACK_SPEED = 1.0      # these clips are already brisk (0.67-1.67 s); the skeleton needed 1.15 to feel right
HURT_SPEED = 1.25
RUN_RADIUS = 1.5
# the ring the single move clip is repeated around: enough points for a real hull, in the order f/b/l/r
RING = ((0.0, 1.0), (0.0, -1.0), (-1.0, 0.0), (1.0, 0.0))

BLEND_LOCO = 0.2
BLEND_ATTACK_IN, BLEND_ATTACK_OUT = 0.08, 0.2
BLEND_HURT_IN, BLEND_HURT_OUT = 0.05, 0.2
BLEND_HOP_IN, BLEND_HOP_OUT = 0.05, 0.15

# id band 2600000xxx, unique inside each graph (each monster gets its own file, so they may repeat)
P_MX, P_MY, P_GUARD, P_ATK, P_AKIND, P_HURT, P_HKIND, P_DEAD, P_BUSY = range(2600000001, 2600000010)
N_ROOT, N_SM, N_LOCO = 2600000010, 2600000011, 2600000012
P_HOP, N_HOP = 2600000013, 2600000014
# every kind SkeletonNpc.Kind can emit: 1 SwordA, 2 SwordB, 3 Charge, 4 ShieldBash
ATTACK_KINDS = (1, 2, 3, 4)
N_ATTACK = {k: 2600000019 + k for k in ATTACK_KINDS}
N_HURT = [2600000030 + k for k in range(4)]
S_LOCO = 2600000050
S_HOP = 2600000051
S_ATTACK = {k: 2600000059 + k for k in ATTACK_KINDS}
S_HURT = [2600000070 + k for k in range(4)]


def loco_items(loco):
    """idle at the centre, the move clip around the ring at every radius, the fast clip at the outer ones."""
    items = []
    idle = loco.get("idle")
    move, fast = loco["move"], loco.get("fast")
    items.append((idle or move, (0.0, 0.0)))
    radii = (0.5, 1.0, RUN_RADIUS) if not fast else (0.5,)
    for r in radii:
        for x, y in RING:
            items.append((move, (x * r, y * r)))
    if fast:
        for r in (1.0, RUN_RADIUS):
            for x, y in RING:
                items.append((fast, (x * r, y * r)))
    return items


def attack_clips(attacks):
    """{attack_kind: clip} TOTAL over every kind SkeletonNpc.Kind can emit — 1 SwordA, 2 SwordB, 3 Charge,
    4 ShieldBash — repeating the first clip for the ones this monster has no animation of its own for.

    This has to be total, not `range(len(attacks))`. `SkeletonNpc.Decide` picks between SwordA and SwordB on
    a flat 50/50 with no check that a second clip exists, and damage is dealt on a wall clock that never
    consults the graph: a kind with no state changed nothing, so the five one-attack monsters hit for full
    damage with no animation under it half the time they attacked, sound and all. Charge and ShieldBash are
    dormant only because the monster brains roll 0 for them, so they are covered here too.
    gen_soul_animgraph.py has always done this (its `4 unused -> attack_b_03_a`); this generator did not."""
    return {kind: attacks[kind - 1] if kind <= len(attacks) else attacks[0] for kind in ATTACK_KINDS} if attacks else {}


def build(name):
    kind = monster_kinds.kind(name)
    loco, attacks, hop = kind["loco"], list(kind["attacks"]), kind["hop"]
    hurts = 4 if kind["stagger"] else 0
    hurt_clip = "hit"
    clip_of = attack_clips(attacks)

    nodes = [(N_LOCO, blend2d(N_LOCO, "loco", P_MX, P_MY, loco_items(loco), (-640, -256), loco["playback"]))]
    if hop:
        nodes.append((N_HOP, sequence(N_HOP, "hop", hop["clip"], False, loco["playback"], (-640, -96))))
    for i, clip in clip_of.items():
        nodes.append((N_ATTACK[i], sequence(N_ATTACK[i], "attack_%d" % i, clip, False, ATTACK_SPEED, (-640, 128 * i))))
    for k in range(hurts):
        nodes.append((N_HURT[k], sequence(N_HURT[k], "hurt_%d" % k, hurt_clip, False, HURT_SPEED, (-640, 384 + 96 * k))))

    # every state can be interrupted by a hurt, and by an attack: a pulse that lands mid-hurt used to be
    # dropped and the host then hit on its timer with no clip behind it (the skeleton graph's lesson)
    def interrupts():
        return [transition([cond_param(P_HURT, True), cond_float(P_HKIND, k)], S_HURT[k], BLEND_HURT_IN)
                for k in range(hurts)]

    def attack_entries():
        return [transition([cond_param(P_ATK, True), cond_float(P_AKIND, i)], S_ATTACK[i], BLEND_ATTACK_IN)
                for i in clip_of]

    def back_to_loco(blend):
        # `busy` is the host letting go: it cuts a clip's long recovery the moment the code has moved on
        return [transition([cond_finished()], S_LOCO, blend), transition([cond_param(P_BUSY, False)], S_LOCO, blend)]

    def hop_entry():
        return [transition([cond_param(P_HOP, True)], S_HOP, BLEND_HOP_IN)] if hop else []

    states = [state(S_LOCO, "loco", N_LOCO, hop_entry() + attack_entries() + interrupts(), True, (-256, -256))]
    if hop:
        states.append(state(S_HOP, "hop", N_HOP, back_to_loco(BLEND_HOP_OUT) + attack_entries() + interrupts(),
                            False, (-256, -96)))
    for i in clip_of:
        others = [t for j, t in zip(clip_of, attack_entries()) if j != i]
        states.append(state(S_ATTACK[i], "attack_%d" % i, N_ATTACK[i],
                            back_to_loco(BLEND_ATTACK_OUT) + others + interrupts(), False, (-256, 128 * i)))
    for k in range(hurts):
        others = [t for j, t in enumerate(interrupts()) if j != k]
        states.append(state(S_HURT[k], "hurt_%d" % k, N_HURT[k],
                            back_to_loco(BLEND_HURT_OUT) + attack_entries() + others + hop_entry(), False,
                            (-256, 384 + 96 * k)))

    nodes.append((N_SM, state_machine(N_SM, "monster", states, (0, 0))))
    nodes.append((N_ROOT, root(N_ROOT, N_SM, (256, 0))))
    nodes.sort(key=lambda n: n[0])
    params = "".join([
        param_float(P_MX, "move_x", -RUN_RADIUS, RUN_RADIUS), param_float(P_MY, "move_y", -RUN_RADIUS, RUN_RADIUS),
        param_bool(P_GUARD, "guard"), param_bool(P_BUSY, "busy"), param_bool(P_DEAD, "dead"),
        param_bool(P_ATK, "attack_trigger"), param_float(P_AKIND, "attack_kind", 0.0, 4.0),
        param_bool(P_HURT, "hurt_trigger"), param_float(P_HKIND, "hurt_kind", 0.0, 5.0),
        # declared even where nothing hops, so SkeletonBody's Set() always has a target
        param_bool(P_HOP, "hop_trigger"),
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
		"{kind["vmdl_rel"]}",
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
    os.makedirs(os.path.dirname(kind["animgraph"]), exist_ok=True)
    with open(kind["animgraph"], "w", newline="\n") as f:
        f.write(out)
    print("wrote %-38s loco %-14s %2d items, playback %.2f, attacks %-18s hurts %d, hop %-5s states %d" % (
        os.path.relpath(kind["animgraph"], ROOT),
        loco["move"] + ("+" + loco["fast"] if loco.get("fast") else ""),
        len(loco_items(loco)), loco["playback"], ",".join(attacks) or "none", hurts,
        hop["clip"] if hop else "no", len(states)))


def main():
    ap = argparse.ArgumentParser()
    monster_kinds.add_kind_arg(ap)
    args = ap.parse_args()
    for name in monster_kinds.selected(args.kind):
        build(name)


if __name__ == "__main__":
    main()
