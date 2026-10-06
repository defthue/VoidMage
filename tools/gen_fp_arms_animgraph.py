"""Write Assets/animgraphs/human_fp_arms.vanmgrph: the animgraph of the first-person arms model.

Built the way heden's human_base graph plays the fireball, with the sword clips underneath:

    Root <- action_blend (CBlendAnimNode, float param `action_weight`)
              0.0 <- base
              1.0 <- cast_mask (CBoneMaskAnimNode, weight list `cast_arm`; in1 = base, in2 = cast SM)
    base = Direct Playback node (code plays any one-shot sword clip by name through
           SceneModel.DirectPlayback; when none plays it shows its input) <- gait_select
    gait_select = Selector on int param `gait`: idle / walk / run / sprint loop / jump loop (looping clips)
    cast SM = Passthrough (start, shows base) / ChannelBegin (Attack6begin) / ChannelLoop / ChannelEnd,
              driven by bool `channel_hold` (up: begin -> loop, down: end) and bool `channel_abort`
              (pulse: straight back), transitions and blend times copied from heden.

Node schemas are verbatim from heden's graph (sequence, blend, mask, state machine) and the shipped
citizen graph (int parameter, selector, direct playback). Regenerate the model first
(gen_fp_arms_vmdl.py: it declares the clips, the weight list and this graph).

    python tools/gen_fp_arms_animgraph.py
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "animgraphs", "human_fp_arms.vanmgrph")
NONE = 4294967295

# Ids: parameters, then nodes, then states. All unique, all in a range no editor-made id uses.
P_GAIT, P_ACTION, P_HOLD, P_ABORT = 2300000001, 2300000002, 2300000003, 2300000004
N_ROOT, N_BLEND, N_MASK, N_DIRECT, N_GAIT = 2300000010, 2300000011, 2300000012, 2300000013, 2300000014
N_GAIT_SEQ = [2300000020, 2300000021, 2300000022, 2300000023, 2300000024]
N_CAST_SM = 2300000030
N_BEGIN, N_LOOP, N_END = 2300000031, 2300000032, 2300000033
S_PASS, S_BEGIN, S_LOOP, S_END = 2300000040, 2300000041, 2300000042, 2300000043

# Order matters: FirstPersonArms sends the index (ClipPoseSource.GaitIndex).
GAIT_CLIPS = ["anim_idle_breathing", "anim_walk", "anim_run", "anim_sprint_loop", "anim_jump_loop"]
GAIT_BLEND = 0.2
# Attack6begin is 1.4 s at its authored pace; the charge is half a second, so the raise runs at 2x.
# The raise, Attack6begin, is 0.683 s long (measured 2026-09-20). Since the cast time went (spell_instant) every cast is
# raise -> throw with no hold between, so the raise plays at x4 (0.17 s) and ALWAYS plays out before the throw:
# SpellCaster lets the bolt go as it ends (spell_raise_seconds 0.17). It was x2, and a release cut straight to the throw.
BEGIN_SPEED = 4.0


def conn(node, output=NONE):
    return f'''{{
						m_nodeID =
						{{
							m_id = {node}
						}}
						m_outputID =
						{{
							m_id = {output}
						}}
					}}'''


def node(i, body):
    return f'''			{{
				key =
				{{
					m_id = {i}
				}}
				value =
				{{
{body}
				}}
			}},
'''


def sequence(i, name, clip, loop, speed=1.0, pos=(0, 0)):
    return node(i, f'''					_class = "CSequenceAnimNode"
					m_sName = "{name}"
					m_vecPosition = [ {pos[0]:.1f}, {pos[1]:.1f} ]
					m_nNodeID =
					{{
						m_id = {i}
					}}
					m_sNote = ""
					m_tagSpans = [  ]
					m_sequenceName = "{clip}"
					m_playbackSpeed = {speed}
					m_bLoop = {"true" if loop else "false"}''')


def selector(i, name, param, children, blend, pos):
    kids = "".join(f'''						{conn(c)},
''' for c in children)
    tags = "".join(f'''						{{
							m_id = {NONE}
						}},
''' for _ in children)
    names = "".join(f'''						"{k}",
''' for k in range(len(children)))
    return node(i, f'''					_class = "CSelectorAnimNode"
					m_sName = "{name}"
					m_vecPosition = [ {pos[0]:.1f}, {pos[1]:.1f} ]
					m_nNodeID =
					{{
						m_id = {i}
					}}
					m_sNote = ""
					m_children =
					[
{kids}					]
					m_tags =
					[
{tags}					]
					m_selectionSource = "SelectionSource_Int"
					m_boolParamID =
					{{
						m_id = {NONE}
					}}
					m_enumParamID =
					{{
						m_id = {NONE}
					}}
					m_intParamID =
					{{
						m_id = {param}
					}}
					m_intParamMinValue = 0
					m_intParamMaxValue = {len(children) - 1}
					m_blendDuration = {blend}
					m_tagBehavior = "SelectorTagBehavior_OffWhenFinished"
					m_bResetOnChange = false
					m_bSyncCyclesOnChange = true
					m_blendCurve =
					{{
						m_vControlPoint1 = [ 0.5, 0.0 ]
						m_vControlPoint2 = [ 0.5, 1.0 ]
					}}
					m_intParamNames =
					[
{names}					]''')


def direct_playback(i, name, input_node, pos):
    return node(i, f'''					_class = "CDirectPlaybackAnimNode"
					m_sName = "{name}"
					m_vecPosition = [ {pos[0]:.1f}, {pos[1]:.1f} ]
					m_nNodeID =
					{{
						m_id = {i}
					}}
					m_sNote = ""
					m_inputConnection =
					{conn(input_node)}
					m_bFinishEarly = true
					m_bResetOnFinish = false
					m_bStopSequenceOnFinish = false''')


def bone_mask(i, name, weightlist, in1, in2, pos):
    return node(i, f'''					_class = "CBoneMaskAnimNode"
					m_sName = "{name}"
					m_vecPosition = [ {pos[0]:.1f}, {pos[1]:.1f} ]
					m_nNodeID =
					{{
						m_id = {i}
					}}
					m_sNote = ""
					m_weightListName = "{weightlist}"
					m_inputConnection1 =
					{conn(in1)}
					m_inputConnection2 =
					{conn(in2)}
					m_blendSpace = "BlendSpace_Parent"
					m_bUseBlendScale = false
					m_blendValueSource = "Parameter"
					m_blendParameter =
					{{
						m_id = {NONE}
					}}
					m_timingBehavior = "UseChild1"
					m_flTimingBlend = 0.5
					m_flRootMotionBlend = 0.0
					m_footMotionTiming = "Child1"
					m_bResetChild1 = true
					m_bResetChild2 = true''')


def blend(i, name, param, child0, child1, pos):
    return node(i, f'''					_class = "CBlendAnimNode"
					m_sName = "{name}"
					m_vecPosition = [ {pos[0]:.1f}, {pos[1]:.1f} ]
					m_nNodeID =
					{{
						m_id = {i}
					}}
					m_sNote = ""
					m_children =
					[
						{{
							m_inputConnection =
							{conn(child0)}
							m_name = ""
							m_blendValue = 0.0
						}},
						{{
							m_inputConnection =
							{conn(child1)}
							m_name = ""
							m_blendValue = 1.0
						}},
					]
					m_blendValueSource = "Parameter"
					m_param =
					{{
						m_id = {param}
					}}
					m_blendKeyType = "BlendKey_UserValue"
					m_bLockBlendOnReset = false
					m_bSyncCycles = false
					m_bLoop = true
					m_bLockWhenWaning = false
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


def root(i, input_node, pos):
    return node(i, f'''					_class = "CRootAnimNode"
					m_sName = "Unnamed"
					m_vecPosition = [ {pos[0]:.1f}, {pos[1]:.1f} ]
					m_nNodeID =
					{{
						m_id = {i}
					}}
					m_sNote = ""
					m_inputConnection =
					{conn(input_node)}''')


# ---------------------------------------------------------------- state machine

def cond_param(param, value):
    return f'''								{{
									_class = "CParameterAnimCondition"
									m_comparisonOp = 0
									m_paramID =
									{{
										m_id = {param}
									}}
									m_comparisonValue =
									{{
										m_nType = 1
										m_data = {"true" if value else "false"}
									}}
								}},
'''


def cond_finished():
    return '''								{
									_class = "CFinishedCondition"
									m_comparisonOp = 0
									m_option = "FinishedConditionOption_OnFinished"
									m_bIsFinished = true
								},
'''


def transition(conditions, dest, duration):
    return f'''							{{
								_class = "CAnimStateTransition"
								m_conditions =
								[
{"".join(conditions)}								]
								m_blendDuration = {duration}
								m_destState =
								{{
									m_id = {dest}
								}}
								m_bReset = true
								m_resetCycleOption = "Beginning"
								m_flFixedCycleValue = 0.0
								m_bBlendCycle = false
								m_blendCurve =
								{{
									m_vControlPoint1 = [ 0.5, 0.0 ]
									m_vControlPoint2 = [ 0.5, 1.0 ]
								}}
								m_bForceFootPlant = false
								m_bDisabled = false
								m_bRandomTimeBetween = false
								m_flRandomTimeStart = 0.0
								m_flRandomTimeEnd = 0.0
							}},
'''


def state(sid, name, input_node, transitions, start, pos):
    return f'''						{{
							_class = "CAnimState"
							m_transitions =
							[
{"".join(transitions)}							]
							m_tags = [  ]
							m_tagBehaviors = [  ]
							m_name = "{name}"
							m_inputConnection =
							{conn(input_node)}
							m_stateID =
							{{
								m_id = {sid}
							}}
							m_position = [ {pos[0]:.1f}, {pos[1]:.1f} ]
							m_bIsStartState = {"true" if start else "false"}
							m_bIsEndtState = false
							m_bIsPassthrough = false
							m_bIsRootMotionExclusive = false
							m_bAlwaysEvaluate = false
						}},
'''


def state_machine(i, name, states, pos):
    return node(i, f'''					_class = "CStateMachineAnimNode"
					m_sName = "{name}"
					m_vecPosition = [ {pos[0]:.1f}, {pos[1]:.1f} ]
					m_nNodeID =
					{{
						m_id = {i}
					}}
					m_sNote = ""
					m_states =
					[
{"".join(states)}					]
					m_bBlockWaningTags = false
					m_bLockStateWhenWaning = false''')


# ---------------------------------------------------------------- parameters

def param_bool(i, name):
    return f'''		{{
			_class = "CBoolAnimParameter"
			m_name = "{name}"
			m_id =
			{{
				m_id = {i}
			}}
			m_previewButton = "ANIMPARAM_BUTTON_NONE"
			m_bUseMostRecentValue = false
			m_bAutoReset = false
			m_bDefaultValue = false
		}},
'''


def param_float(i, name, lo, hi):
    return f'''		{{
			_class = "CFloatAnimParameter"
			m_name = "{name}"
			m_id =
			{{
				m_id = {i}
			}}
			m_previewButton = "ANIMPARAM_BUTTON_NONE"
			m_bUseMostRecentValue = false
			m_bAutoReset = false
			m_fDefaultValue = 0.0
			m_fMinValue = {lo}
			m_fMaxValue = {hi}
		}},
'''


def param_int(i, name, lo, hi):
    return f'''		{{
			_class = "CIntAnimParameter"
			m_name = "{name}"
			m_id =
			{{
				m_id = {i}
			}}
			m_previewButton = "ANIMPARAM_BUTTON_NONE"
			m_bUseMostRecentValue = false
			m_bAutoReset = false
			m_defaultValue = 0
			m_minValue = {lo}
			m_maxValue = {hi}
		}},
'''


def main():
    nodes = []
    for k, clip in enumerate(GAIT_CLIPS):
        nodes.append((N_GAIT_SEQ[k], sequence(N_GAIT_SEQ[k], clip, clip, True, pos=(-640, -160 + 64 * k))))
    nodes.append((N_GAIT, selector(N_GAIT, "gait", P_GAIT, N_GAIT_SEQ, GAIT_BLEND, (-352, -96))))
    nodes.append((N_DIRECT, direct_playback(N_DIRECT, "sword clips (direct playback)", N_GAIT, (-96, -96))))

    nodes.append((N_BEGIN, sequence(N_BEGIN, "Attack6begin", "Attack6begin", False, BEGIN_SPEED, (-640, 256))))
    nodes.append((N_LOOP, sequence(N_LOOP, "Attack6loop", "Attack6loop", True, pos=(-640, 320))))
    nodes.append((N_END, sequence(N_END, "Attack6end", "Attack6end", False, pos=(-640, 384))))

    states = [
        state(S_PASS, "Passthrough", N_DIRECT, [
            transition([cond_param(P_HOLD, True)], S_BEGIN, 0.06),
        ], True, (160, -32)),
        state(S_BEGIN, "ChannelBegin", N_BEGIN, [
            transition([cond_finished(), cond_param(P_HOLD, True)], S_LOOP, 0.1),
            transition([cond_param(P_ABORT, True)], S_PASS, 0.15),
            transition([cond_finished(), cond_param(P_HOLD, False)], S_END, 0.06),
        ], False, (480, -80)),
        state(S_LOOP, "ChannelLoop", N_LOOP, [
            transition([cond_param(P_ABORT, True)], S_PASS, 0.1),
            transition([cond_param(P_HOLD, False)], S_END, 0.1),
        ], False, (480, 32)),
        state(S_END, "ChannelEnd", N_END, [
            transition([cond_param(P_HOLD, True)], S_BEGIN, 0.15),
            transition([cond_finished()], S_PASS, 0.25),
        ], False, (480, 160)),
    ]
    nodes.append((N_CAST_SM, state_machine(N_CAST_SM, "Cast State Machine", states, (-96, 256))))

    nodes.append((N_MASK, bone_mask(N_MASK, "cast_arm", "cast_arm", N_DIRECT, N_CAST_SM, (192, 96))))
    nodes.append((N_BLEND, blend(N_BLEND, "action_blend", P_ACTION, N_DIRECT, N_MASK, (448, -32))))
    nodes.append((N_ROOT, root(N_ROOT, N_BLEND, (704, -32))))

    nodes.sort(key=lambda n: n[0])
    params = "".join([
        param_int(P_GAIT, "gait", 0, len(GAIT_CLIPS) - 1),
        param_float(P_ACTION, "action_weight", 0.0, 1.0),
        param_bool(P_HOLD, "channel_hold"),
        param_bool(P_ABORT, "channel_abort"),
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
		"charactertoon/human_fp_arms.vmdl",
	]
	m_boneMergeModels = [  ]
	m_cameraSettings =
	{{
		m_flFov = 60.0
		m_sLockBoneName = "head"
		m_bLockCamera = false
		m_bViewModelCamera = false
	}}
}}
'''
    with open(OUT, "w", newline="\n") as f:
        f.write(out)
    print("wrote", OUT, "nodes", len(nodes))


if __name__ == "__main__":
    main()
