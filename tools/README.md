# DungeonPack asset generation

The Synty Dungeon Pack FBX files in `Assets/DungeonPack` are already "toonified" (heden format):
each file holds `SM_X` (base mesh) + `SM_X_Outline` (inflated hull for `heden_outline.shader`),
with material slots named `DungeonShader`, `DungeonFloor`, `DungeonWallTiles`, ... and
`FantasyKingdomOutline` for the hull.

Regenerate every `.vmdl` and the slot `.vmat`s in `Assets/Shaders`:

```
python tools/fbx_scan_all.py Assets/DungeonPack scan.json
python tools/gen_dungeon_assets.py scan.json
```

Scale rules (measured, see `fbx_bounds.py`): meshes are in cm -> `import_scale 0.3937`;
`SM_Env_Tiles_Texture_09/10` carry a 100x node scale -> `0.003937`; the raw `FX_*` meshes are
in metres -> `39.37`. Flame/particle and sewer-water meshes get no collision.

# Dungeon generator

`Code/Dungeon/` is the C# port of `dungeon_genv2.py` (path grammar: spine + spurs, all enclosed, doorways
not doors) on top of `dungeon_gen.py`'s grid and building blocks (`DungeonBlocks.cs`)
plus `DungeonBuilder`, the component that spawns the models. `Assets/scenes/minimal.scene` has a
Dungeon object with it: press Generate in the inspector, or play the scene (it builds on start and
spawns a first-person PlayerController on the spawn tier).

Run the generator headless across seeds (checks determinism, prints stats):

```
cd tools/gentest
dotnet run -c Release -- 10 1.5
```

Kit pivots/yaws are the tables at the bottom of `DungeonBuilder.cs`; measure with
`tools/fbx_pivots.py <fbx...>`. Mesh-local axes in s&box are (fbx.z, fbx.x, fbx.y), derived from
heden's Unity->s&box basis and the Synty demo placements (stairs vs. upper-floor tiles).

# Decoration

After the layout, `Decor.Run` (engine-free, `Code/Dungeon/Decor*.cs`) assigns a role to every room
(Library, Crypt, Shrine, ...), extracts placement slots from the geometry (walls, corners, centre,
ceilings, pillar faces), computes the walking line between every door and stair so props never block
it, fills the slots from the role's table in `DecorCatalog.cs`, and runs the global pass: torches at
stairs, doors and along corridors, hallway clutter, decay by depth, the light budget. One secret room
per floor sits behind a `sm_env_wall_05` wall (reserved for that: never add it to any other table).

Everything is data in `DecorCatalog.cs`: props with measured extents (metres, from `fbx_pivots.py`),
set pieces (prop offsets around an anchor cell, U forward / V right in cell units), and per-role fill
tables. The builder pushes wall props flush using the model bounds, so a new prop only needs its
extents and anchor kind. Skinned meshes (`skeleton_slave_*`, `skeleton_table_*`) have garbage bounds
and must stay out. `gentest` checks that every catalog model exists, that decoration is deterministic
and never changes the layout, and prints per-floor stats (`dotnet run -c Release -- 10 1.5 <depth>`).

# First-person sword animations (retarget)

The Fab "First Person sword and shield" clips are on the UE5 Manny skeleton (A-pose rest, spine_01-05,
neck_01/02, metacarpals). The Synty Sidekick rig in `Assets/CharacterToon` rests in a T-pose with
spine_01-03 and neck_01, so copying rotations by bone name leaves the arms 53-61 degrees off; s&box has no
retargeting of its own. `tools/retarget_fpp.py` re-authors each clip on the Synty skeleton headlessly:

```
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup --python tools/retarget_fpp.py -- ^
    --rig Assets/CharacterToon/human_torso.fbx --out Assets/Animations/SwordFP ^
    "C:\Users\skipp\Desktop\Speelboord Studios\Animation\FPP_sword_shield\Animations\*.FBX"
```

It poses the Synty rig into Manny's rest pose (palm plane matched), binds world-space rotation constraints
through helper bones on Manny, bakes and exports animation-only FBX (cm, Y-up, 30 fps, same layout as
`Assets/Animations/Magic`). Add `--report` to print per-frame limb angle differences against the source
(expect 0.0 on upper/forearm, a few degrees on hands). `--finger-curl` (default 0.65) scales every finger
joint's rotation after baking: Manny's grip is a tight fist around a thin handle (knuckles ~100°, middle joints
~70°) that folds the chunky Synty fingers through the palm; 0.65 gives ~78°/48°. The pack is right-handed;
`--mirror` (default on, `--no-mirror` to keep the original) swaps left and right after baking by mirroring each
bone's delta from rest across the X=0 plane, so the sword ends up in the LEFT hand and the shield fist on the
right holds the staff. In game neither prop is attached to a bone: both are placed from the hand joints
(`Code/Character/SwordGrip.cs`: blade along the knuckle line toward the thumb, flat facing the palm, handle
middle offset from the hand joint), tuned via First Person Arms > Sword grip / Staff grip.

`python tools/gen_fp_arms_vmdl.py` then writes `Assets/CharacterToon/human_fp_arms.vmdl`: the arm and hand
meshes on the full skeleton with every SwordFP clip plus heden's Attack6begin/loop/end as AnimFiles, the
`cast_arm` weight list, a `do_not_discard` BoneMarkup for all 87 bones (ModelDoc drops unskinned bones
otherwise, which loses `head`) and the graph hook. `python tools/gen_fp_arms_animgraph.py` writes that graph,
`Assets/animgraphs/human_fp_arms.vanmgrph` (KV3 text, same construction as heden's body graph): a gait
selector (int `gait`: idle/walk/run/sprint/jump loops) under a Direct Playback node that code feeds one-shot
sword clips by name (`SceneModel.DirectPlayback.Play`), and heden's cast state machine (Attack6 on bool
`channel_hold`, bool `channel_abort` bails) masked onto the staff arm by `cast_arm` and faded by float
`action_weight`. Regenerate the model first, then the graph.

In game `ClipPoseSource` is the synced state (one-shot clip + start time, gait, hold, abort count, action
target) and drives every renderer of the arms model with it: its own hidden one on every peer (the body's
`UpperBodyPoseCopy` copies that chest/arms through `CharacterBoneRig`, `HandPropFollower` carries the
third-person sword and staff) and the owner's `FirstPersonArms` rig, placed on the camera in
`PostCameraSetup` with the props on the animated hand joints. Nothing writes bones for the arms.

## The drink clip (Universal Animation Library)

The potion drink (`Code/Character/PotionDrinker.cs`) is the `Drink` take of Quaternius' Universal Animation
Library, which ships as one FBX of 120 takes (`Universal Animation Library[Source]\Unity\UAL1.fbx`, Epic bone
names, `Head` capitalised, no twist bones). The same retarget tool eats it with two extra flags: `--take Drink`
picks the action out of the multi-take file (Blender leaves the LAST take active otherwise) and `--name drink`
names the output; the head rename and the twist-bone skip are automatic:

```
blender.exe -b --factory-startup --python tools/retarget_fpp.py -- --rig Assets/CharacterToon/human_torso.fbx ^
    --out <scratch> --no-mirror --report --finger-curl 1.0 --take Drink --name drink "<UAL>\Unity\UAL1.fbx"
```

The take animates the RIGHT arm only and holds a T-pose everywhere else (checked on a contact sheet, see
below), so played whole it would fling the sword arm out sideways. `tools/layer_clip.py` lays the right-arm
chain of the retargeted take over the first-person idle (`anim_idle_breathing`, looped), trims the T-pose
lead-in and lead-out, and fixes the half of the retarget that rotation copying cannot:

```
blender.exe -b --factory-startup --python tools/layer_clip.py -- --base Assets/Animations/SwordFP/anim_idle_breathing.fbx ^
    --overlay <scratch>\drink.fbx --out Assets/Animations/SwordFP/drink.fbx --bones clavicle_r --start 9 --end 76 ^
    --hand-at "0.093,-0.04,-0.095" --hand-aim "0.013,-0.01,-0.102" --shoulder-drop 40 --wrist-in -30
```

Why the last two flags: copying joint angles puts the hand where the SOURCE's proportions put it, and the Synty
arm is longer against its own head, so the drink's hand sailed past the mouth and ended up 11 cm BEHIND the eyes,
beside the temple — the "his hand goes through his head" the first build had, in both views. `--hand-at` re-solves
the arm each frame (two-bone IK) so the hand sits at a point given in metres from the eye midpoint in the BODY's
forward/left/up — the body's, not the head's, because the head tilts back in the clip while neither view tilts
with it (third person keeps the body's own head, first person is the player's camera). `--elbow-down` (default 1)
swings the elbow under the shoulder-to-hand line so the arm does not read as a salute, and `--hand-aim` turns the
fist so what it holds points at the mouth, since the source's wrist never tips the vessel and the potion is a 50 cm
flask. All of it fades out between `--hand-near` and `--hand-far` metres of hand-to-head distance, so the arm
hanging at the side is exactly as animated. `--debug` prints the hand-to-eye distance before and after per frame.

The numbers are relative to the EYES, but the first-person camera is not at the eyes: FirstPersonArms pins the head
BONE to the camera, and the head bone sits 7.2 cm below and 7.7 cm behind the eye midpoint. The first values aimed
the flask at the mouth and the player saw it drunk just above the screen centre. The aim point above works out to
9 cm in front of the camera and 3 cm under its centre, and the fist height was picked by looking through the `eye`
view of `film_fbx_clip.py` (the flask comes up into the lower middle of the screen). In third person the flask now
meets the chin rather than the lips. That still read too high and back in the skull in game (the eye render
disagreed with what the player saw, so trust measurements against the head bone over that view): `--shoulder-drop 40`
then swings the whole arm down about the shoulder as one piece, the player's own suggestion. Measured against the
head bone (the camera) at the hold: the wrist went from 7 in ahead and level with the camera to 10 in ahead and
8 in below it. `--wrist-in` then turns the fist while the flask is at the mouth. Positive tips the grip's long
axis toward the mouth as the tool measures it; in game +15 read as the wrong way round (the tuned grip holds the flask
differently from that axis), so the drink ships with -30 (-15 first, then the player asked for 30).

Result: `Assets/Animations/SwordFP/drink.fbx`, 68 frames at 30 fps (hand at the mouth by frame 3, leaves it
around 57, the flick whips out over 60-63; `potion_heal_frame` / `potion_throw_frame` are the convars), picked
up by `gen_fp_arms_vmdl.py` like every SwordFP clip; the graph needs no change (one-shots play by name).

### The pickup reach (E)

`PickUp_Table` (UAL1.fbx, 26 frames) reaches with the LEFT arm from a hanging rest, with a slight lean; the right
arm does not move. It goes on the staff arm, so it is retargeted MIRRORED (the retarget default):

```
blender.exe -b --factory-startup --python tools/retarget_fpp.py -- --rig Assets/CharacterToon/human_torso.fbx ^
    --out <scratch> --finger-curl 1.0 --take PickUp_Table --name pickup_table "<UAL>\Unity\UAL1.fbx"
blender.exe -b --factory-startup --python tools/layer_clip.py -- --base Assets/Animations/SwordFP/anim_idle_breathing.fbx ^
    --overlay <scratch>\pickup_table.fbx --out Assets/Animations/SwordFP/pickup_table.fbx --bones clavicle_r ^
    --start 2 --end 27 --follow-hand "0.5,0.7,0.15"
```

Copying the joint angles does not work here: the staff hand is already held out in front, so the source's
reach from the hip swings the staff up to vertical through the view. (Blender exports frame 1 at 1/fps, so the
retargeted take runs 2-27.) `--follow-hand fwd,left,up` keeps the idle arm, fingers and WRIST (so the staff keeps its
angle) and re-solves the arm with IK so the hand moves the way the source hand moves, in the body's axes, scaled
per axis. The idle arm is nearly straight, so forward reach clamps; what ships measures, from the head: out
11 cm, then 11 cm forward, 13 cm in and 9 cm up, then back to the idle exactly.

`tools/film_fbx_clip.py` is how a frame gets picked: it renders every Nth frame of a clip FBX on the character
pieces from the side and the front (cameras oriented off the skeleton's own up/forward, since the pieces import
in the FBX's raw space) into one contact sheet PNG — read it, never guess a pose from a description:

```
blender.exe -b --factory-startup --python tools/film_fbx_clip.py -- --clip Assets/Animations/SwordFP/drink.fbx --out <scratch>\drink_sheet.png --step 3 ^
    --focus head --zoom 1.5 --prop Assets/DungeonPack/SM_Item_Potion_07.fbx --prop-grip "1.6,1.2,-10.1"
```

`--focus <bone>` and `--zoom` frame the part that matters, and `--prop` puts a prop FBX in the hand through the
game's own grip maths (SwordGrip.Compute, ported in `measure_sword_tip.py`) so the pose can be judged with the
thing the hand is holding. Two traps in that scene: the character pieces import at 1/100 (so a length in metres
means nothing until it is scaled by the rig's own spine, which is what the tool does) and the pieces' object
scale reads 0.0001, not the 0.01 the rig actually measures.

# Sword slash crescent (tip measurement)

`Code/Character/SwordSlash.cs` plays an authored crescent in view space at the moment of each attack's cut (the
way most games do it), not a trail on the blade: the pack's hand keys alternate fast and slow frames, so a
faithful blade trail is a jittery scribble. Each clip's row in its `Profiles` table is a start frame, an end
frame and three eye-relative points (x forward, y left, z up, inches): the circle through them is the swing's
pivot and reach, and a band just inside that radius sweeps from the first point to the last with a fading tail
(`materials/slash_ribbon.vmat` paints the profile across the band). `tools/measure_sword_tip.py` produces those
rows from the clips:

```
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup --python tools/measure_sword_tip.py -- ^
    --out tools/out/sword_tip.json "Assets/Animations/SwordFP/anim_attack_*.fbx"
```

Per clip it rebuilds the grip from the five left-hand joints exactly as `SwordGrip.Compute` does (`--grip` /
`--rot` default to the prefab's tuned sword grip, `--blade` 28 is the ornate sword's tip above the guard at its
0.2 import scale), follows the tip, and prints tip speed per frame, the peak, the window (contiguous frames above
`--threshold` = 25% of the 3-frame-averaged peak), the swept angle and the guard's travel direction, and then
the crescent: inside the window it drops the slow lead-in and tail (under 30% of the raw peak) and keeps the
final run of frames where the tip is in front of the eye (the windup pulls it behind the head), printing a
paste-ready `Profiles` row per clip with the first, half-way (by travel) and last points plus every in-front
frame for hand tuning. Eye space uses the model's fixed axes (Blender import: left +X, up +Z, forward -Y) and each
frame's own head, exactly how `FirstPersonArms` places the rig on the view. Measured 2026-09-06: the light swings
are fast from frame 0 to 7 (the visible cut is frames 4-7) and then nearly still; the heavies wind up for half a
second and cut over frames 18-22. In game every swing logs `[SwordSlash] <clip> (light 2/6) crescent f4-7 ...`, so
a swing can be named and its row tuned; with `DebugArc` on, the real tip's eye-space path is recorded during the
swing, drawn in magenta next to the fitted arc (cyan) and logged per frame, which is how the rows are checked
against the game. The crescent runs on the synced clip start time, never the playback clock (that clock still
holds the previous clip's time on the frame a swing starts).

Spell casts (`Code/Spells`): `SpellCaster` runs the fireball (RMB charge 0.3 s, release fires) by raising and
dropping `channel_hold` through `ClipPoseSource`, exactly as heden's `CharacterAnimator` drives its graph;
`CastWeight` below 1 softens the gesture against the shield arm. `FireballVfx` is heden's layered fireball
(SpellVfxRig: core, tail, fumes, embers, smoke trail per distance, ribbon, impact flash/fireburst/smoke/ring/
embers, GroundBreak scorch decal through `heden_ground_splat.shader`) at `VfxScale` 0.65 (heden 0.85) with Void
Mage flair: banded three-tone ramps, a ring of orbiting polygon shards, violet void smoke, a flickering light.
Textures are the Synty PolygonParticles sprites in `Assets/VFX`; smoke is drawn only with the hexagon
`circle_01`/`circle_02` sprites (no fumes/smoke PNGs). The bolt has a persistent body (two `SpriteRenderer`
billboards under the particles) and sheds its tail and embers per distance (`RateOverDistance`), so it stays a
solid streak at 1800 u/s. The charge orb is the same core grown by the charge (`SetSize`: sprites, spread and
population together). Impacts are typed (`ImpactKind`): World = full burst + scorch, Body (enemy/npc tag) = tighter
burst + hit spray along the normal, no scorch, body hit sound layered; Fizzle (range/lifetime) = quiet puffs, no sound.

Trace gotcha: `DungeonBuilder` parents the player under the dungeon root, so `GameObject.Root` on the player IS
the dungeon and `IgnoreGameObjectHierarchy( GameObject.Root )` ignores every wall. Traces ignore the
`PlayerController`'s object instead (`SpellCaster.PlayerRoot`, `SwordSounds`). The muzzle is swept from the eye
to the staff gem so a bolt cast nose-to-wall starts on the player's side. Sword attacks have a cooldown
(`FirstPersonArms`: 0.33 s light, 0.5 s heavy); presses inside it are dropped.

# Skeleton NPC (Sword & Shield pack)

The first enemy is Synty's Dungeon skeleton slave with the "Simple Sword & Shield" pack (178 clips at
`C:\Users\skipp\Desktop\Speelboord Studios\Animation\simpleswordshieldanim_fbx`, `SSH_*_v001.fbx` in nested
category folders). The pack is UE5 Manny again, but 60 fps, and every travelling clip carries its travel on
Manny's `root` (walk 170 cm, roll 412 cm, attack lunges 184 cm; root rotation is always 0). Manny's `root` is an
FBX Root node, so Blender turns it into the clip's armature OBJECT: root motion is the object transform.

Mesh source is the Unreal variant `dungeonpack\FBX\Characters\Unreal_Characters\SK_Chr_Skeleton_Slave_01.fbx`
(same mesh as `Character_Skeleton_Slave_01.fbx`, Epic bone names, 43 bones, thumb/index/middle only, no sockets).
Three scripts, run in this order:

```
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup --python tools/prep_skeleton_npc.py -- --kind slave
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup --python tools/retarget_fpp.py -- ^
    --rig Assets/Characters/SkeletonSlave/skeleton_slave_01.fbx --out Assets/Animations/SkeletonSS ^
    --no-mirror --root-motion --sockets weapon_r,shield_l --strip-prefix SSH_ --strip-suffix _v001 ^
    "C:/Users/skipp/Desktop/Speelboord Studios/Animation/simpleswordshieldanim_fbx/**/*.fbx"
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup --python tools/gen_skeleton_ragdoll.py -- --kind slave
python tools/gen_skeleton_npc_vmdl.py --kind slave
```

`--kind` is one of `tools/skeleton_kinds.py` (`slave`, `soldier_01`, `soldier_02`, `tormented_soul`, `realms_01..03`): the character FBX, the output
folder/model name, and the swords and shields it can hold. Every Dungeon skeleton character sits on the SAME
Epic-named rig (43 bones, identical rest transforms, checked with `fbx_bounds.py`), so the retarget runs ONCE
(against the slave) and the clips in `Assets/Animations/SkeletonSS` and the one animgraph are shared; prep, ragdoll
and vmdl run per kind. The soldiers (`Assets/Characters/SkeletonSoldier/skeleton_soldier_01/02`) carry
`SM_Wep_GreatSword_01` / `SM_Wep_Greatsword_Curved_01` and `SM_Wep_Shield_Heater_01` / `_02` (the user's re-exports
with split slots: greatswords 152 / 169 cm, grips derived per sword; the heater shields' `IronCrossguardPommelshader`
slot goes to `ShieldIronShader` so the block flash never lights a sword).

### Realms skeletons (a kind on another rig, re-skinned)

The Dungeon Realms pack's `Chr_Skeleton_01/02/03` (three skins of one character, the forge-hold's enemies) ship on
Synty's CLASSIC rig: 50 bones named `Hips`, `Shoulder_L`, `Elbow_L`, `Finger_01`... whose JOINT POSITIONS equal the
Epic rig's to 0.00 cm but whose rest orientations differ by 47-180 degrees (measured 2026-09-11 in Blender), and
the FBX names both hands' finger bones identically. A rename cannot carry the clips, so such a kind names a donor
(`rig_fbx` = the slave's Epic rig) and a `bone_map`, and `prep_skeleton_npc.py` re-skins: vertex groups are
renamed classic -> Epic (`CLASSIC_UNSIDED/SIDED/FINGERS`; a finger's side comes from its first `_L`/`_R` ancestor,
never the `.001` suffix; the 4th finger segments, `Jaw` and `Toes_*` merge into their parents), the body is
re-parented to the donor with its world matrix kept, the classic armature is deleted. It refuses if any 1:1 joint
is more than 0.5 cm from the donor's, because the skin binds in the donor's rest from then on. Everything after
that (sockets, weapons, outline, ragdoll, vmdl, clips, animgraph) is the shared rig. `base_material` names the
body slot (`RealmsShader`, the Realms atlas) instead of `DungeonShader`; `gen_skeleton_npc_vmdl.py` has the remap.

Weapons: `SM_Wep_Shield_Gravestone_01` (DungeonPack, one choice; its `DungeonShader` slot becomes
`DungeonShaderShield` like the chitin, so the block flash lights it) and FantasyKingdom `SM_Wep_Sword_03` /
`_04` (one atlas slot each, `FantasyKingdomShader`, at `FK_SWORD_SCALE` 0.7: 152 cm hero blades to the
greatswords' ~106 cm). Sword 04 came with heden's FantasyKingdom copy; sword 03 was toonified one-off with
`TOON_BASE_MAT=FantasyKingdomShader` + `toonify_adventure.py` on a folder holding just that file (the
FantasyKingdom source is already cm), plus a hand copy of `sm_wep_sword_04.vmdl` as its prop vmdl.

Prefabs `Assets/Prefabs/skeleton_realms_0N.prefab` come from `python tools/gen_skeleton_prefab.py --kind realms_0N
--from skeleton_soldier_01` (fresh GUIDs, name, KindName, model; everything else the soldier's: DamageScale 1.5,
GuardPoints 3, `soldier_*` brains via the `soldier_` prefix in `SkeletonSpawner.Kinds`). The dungeon rolls them
among `DungeonBuilder.StrongSkeletonKinds` under `SoldierChance` on every floor for now; `skeleton_spawn realms`
or `realms_02 1 0` for one.

### Undead Knight (heden's boss grammar as a strong enemy)

`Chr_Undead_Knight_01` (Dungeon Realms) is the same classic rig as the Realms skeletons (worst joint 0.04 cm off),
so `--kind undead_knight` re-skins it the same way, with the pack's own `SM_Wep_Sword_Medium_08` (native 151 cm) and
`SM_Wep_Shield_07` (107 x 126 cm, 47 cm deep: `seat=18` in `wep()` moves it out along its plate normal so the arm
sits at its back face instead of its centre). The pack ships him headless: `attachments=[attach(...Helmet_01, "head")]`
(toonified one-off with `toonify_realms.py` on a folder holding just that file) is parented to the head joint in the
character's own frame and rides the vmdl's `body` node. A Realms-atlas shield takes the `RealmsShaderShield`
identity (`Assets/Shaders/RealmsShaderShield.vmat`, a hand-kept twin of RealmsShader like `dungeonshader_shield.vmat`;
`SkeletonBody.ShieldMaterialPaths` lists it). `model_scale` in the kind (1.2) multiplies the vmdl's
`ModelModifier_ScaleAndMirror`, so render, ragdoll capsules and root motion grow together (units = cm x 0.4724).

Build: prep -> gen_skeleton_ragdoll -> gen_skeleton_npc_vmdl (as any kind), then
`python tools/gen_knight_animgraph.py` (`Assets/animgraphs/undead_knight.vanmgrph`: the skeleton layout with ALL 13
`attack_*` clips as states 1..13 that can flow into each other, attacks at 1.0) and
`python tools/gen_knight_prefab.py --force` (`Assets/Prefabs/undead_knight.prefab` from the soldier prefab: the
ATTACK TABLE measured with the Blender hand-speed probe, posture, the shield read, capsule/agent for the 1.2x body).
Brain `Assets/Npc/knight.brain` (one personality, breath 0.15-0.6 s, strafe 0.5, 400 hp). `skeleton_spawn knight`;
the dungeon rolls one per room at `KnightChance` 0.05 on every floor (floor gating is a later pass).

What "heden's boss" means in SkeletonNpc terms (all on the prefab, generic for any kind that fills `Attacks`):
- **the table** (`SkeletonNpc.Attacks`): one weighted roll over the entries whose cooldown is up and whose
  `SelectMin..SelectMax` (0 = StartRange) holds the player; last pick x0.35, the three before x0.7; nothing
  eligible = close in on the nearest start range and roll again (he never stands and waits). `Weight 0` entries are
  combo-only: `ComboInto` + `ComboChance` chain a_01 -> a_02, b_01 -> b_02, d_01 -> d_02, b_03_a -> the overhead.
  Leaps (`a_03`, `b_03_b`, `c_03_a`) need 140 u and carry hyper armor; `c_03_a` is the opener from 260 u.
  `HitTime2` gives `attack_c_02` its second strike. `RootMotionStandoff` 45 clamps a lunge so it plants AT the player.
- **posture** (`PostureMax` 260): no flinches (`CanStagger` off, `FlinchShoveScale` 0.15); every hit adds
  0.6 x damage, decays 6/s after 2 s; full = `guard_heavy_hit` then dazed for 3.5 s at 1.5x damage.
- **the shield read** (`ShieldReactChance` 0.9): the second frontal light inside 1.6 s snaps the shield up for 1.5 s
  (once per 3 s) when he is between attacks or in a recovery; lights bounce (`guard_light_hit`), a heavy on it
  BREAKS it (1.25x damage, +30 posture, open for `GuardBreakSeconds` 1.2), backstabs land in full, an untested
  shield drops when the hold ends. `GuardPoints` 99 so the old point rule never breaks it on lights.
Not ported: phase 2, the kick, void zones, the intro/eyes/exhausts (he is a strong enemy, not the boss). No spell yet.

- `prep_skeleton_npc.py` writes `Assets/Characters/SkeletonSlave/skeleton_slave_01.fbx`: the character with
  socket bones `weapon_r`, `weapon_l`, `shield_l` (placed by posing the rig into Manny's rest with
  `retarget_fpp.prepose` and applying Manny's socket-to-joint offset there), the Dungeon `SM_Wep_BrokenSword_01`,
  its unbroken re-model `SM_Wep_UNBrokenSword.fbx` (the user's Blender export; same handle, pommel at -13.8 cm on
  both, tip 60.7 vs 75.2, so it takes the broken sword's grip matrix verbatim) and `SM_Wep_Shield_Chitin_01`
  (plain cm; `SWORD_FBX`/`SHIELD_FBX` carry a per-file authoring scale, the Chr items would need 0.01) skinned
  100% to `weapon_r` / `shield_l`, and the outline shell (every face duplicated into a `DungeonOutline` slot; body
  material renamed `DungeonShader`). The swords were split in Blender into handle / blade / crossguard+pommel;
  `SWORD_MATERIALS` renames those slots to `WoodHandleShader` / `BladeShader` / `IronShader` (the first two are the
  players' sword vmats, `Assets/Shaders/IronShader.vmat` is BladeShader pulled darker and greyer where the players'
  sword has brass) and the user's `...01Outline` shells to `<mesh>_Outline`. The grip is
  derived from the fist (blade along the knuckle line toward the thumb, flat to the palm, grip point on the
  socket; shield plate, the vertex-heavy side of its flat axis, away from the palm, long axis along the
  forearm, spun `--shield-spin` degrees about the plate normal, -90 by the user's eye); `--sword-pos/--sword-rot`, `--shield-pos/--shield-rot` (cm, degrees,
  socket space) tweak it. Rerunning prep never needs a retarget rerun: the clips only animate the socket bones.
- `retarget_fpp.py` gained flags for this rig: `--root-motion` copies the clip object's location onto the
  target `root` bone (the pelvis bakes root-relative, verified: walk_f root 170 cm, pelvis < 7 cm),
  `--sockets` copies Manny's socket world ROTATION only (location rides the skeleton's own hand, so limb-length
  differences cannot float the sword), the palm plane falls back to index/middle when pinky is missing, output
  names drop the prefix/suffix, lowercase, `-` to `_` (`attack_a_01`, `guard_walk_start_bl`,
  `jump_in_move_loop_f`). The scene fps follows the clip (60), 178 clips took ~3 min in four parallel Blender
  processes (split the globs per category). `gaurd_start` keeps the pack's own typo.
- `gen_skeleton_npc_vmdl.py` writes the vmdl: RenderMeshFile nodes `body`, `sword_N`, `shield_N` from the one FBX
  (each node = one compiled render mesh) and the `sword` / `shield` body groups whose choices list THOSE NODE
  NAMES, last choice empty (`SkeletonBody` sets them from the synced `SkeletonNpc.SwordIndex` / `ShieldIndex`).
  A choice listing FBX object names matches nothing (the compiled model drops them), every mesh lands in every
  group and the skeleton draws all its weapons at once; Facepunch's own vmdls name the nodes the same way. Then an AnimFile per clip with `looping` from the name (`idle`, `*loop*`,
  the 6-way walk/jog cycles, `run`) and an `ExtractMotion` child (tx/ty on `root`) when `tools/fbx_anim.py`
  finds the clip's root travelling more than 1 cm (123 of 178), `default_root_bone_name = "root"`, a
  `do_not_discard` BoneMarkup for all 46 bones, the two Dungeon vmat remaps, scale 0.3937, no animgraph yet.
  `--filter <regex>` emits a subset for a quick first compile. Nothing in the project had proven ExtractMotion
  on a moving root before this (human_base's Synty clips have static roots): check `walk_f` stays in place.
- `gen_skeleton_animgraph.py` writes `Assets/animgraphs/skeleton_slave.vanmgrph` (id band 2400000xxx, emitters
  shared with the arms graph plus `CBlend2DAnimNode` and float comparisons): one state machine. `loco` and
  `guard_loco` are radial 2D blends on `move_x`/`move_y` (idle or guard_loop at the centre, the 6 walk cycles at
  radius 0.5, the 6 jog cycles at 1, and — in `loco` only — `run` at (0, 1.5)). **The pack has NO forward-diagonal
  clips** (`DIRS` is f/b/l/r/bl/br; there is no walk_fl/fr, no jog_fl/fr and no off-axis run), so FL/FR do NOT
  blend from F and L/R the way a radial layout implies: `jog_f` (0,1) is *interior* to the convex hull, whose whole
  forward edge is the chord `run(0,1.5) -> jog_r(1,0)`, i.e. `1.5*|x| + y <= 1.5`. Any forward-diagonal sample lands
  outside it and gets projected onto that chord, blending the run with a pure SIDEWAYS jog at the wrong cadence —
  67 % run + 33 % jog_r at 30 deg off, 27/73 at 60 deg, which is 134 u/s of footwork against 243 of travel. So
  `SkeletonBody` collapses forward travel onto the forward axis (`forwardness`, a remap of `fwd` over 0.2..0.55)
  and only lateral and backward diagonals keep theirs; `guard_loco` has no run item, so a guarding body is capped
  at radius 1. `guard_in`/`guard_out` are gaurd_start/guard_end;
  attacks are `attack_trigger` + float `attack_kind` (1 attack_d_01, 2 attack_d_02, 3 charge start->end, 4
  guard_attack_parry, the shield bash); hurts `hurt_trigger` + `hurt_kind` (0 f, 1 b, 2 l, 3 r, 4 guard_light_hit
  = the shield held, 5 guard_heavy_hit = the guard broken); `dead` +
  `death_kind` 0-2. Playback speeds are constants at the top (hurts 1.25 for pacing).

### Corpse, Dark Fantasy skeletons, armor tiers, blood (2026-09-12)

Five more kinds on the shared clips, all in `skeleton_kinds.py` (`corpse`, `df_skeleton`, `df_flesh`, `df_light`,
`df_heavy`), built with the same three Blender/Python steps as the Realms ones plus `gen_skeleton_prefab.py --kind`
(which now reads the kind's `prefab_from` and applies `prefab_npc` / `prefab_body` overrides on top of the clone):

    blender -b --factory-startup --python tools/prep_skeleton_npc.py -- --kind <K>
    blender -b --factory-startup --python tools/gen_skeleton_ragdoll.py -- --kind <K>
    python tools/gen_skeleton_npc_vmdl.py --kind <K>
    python tools/gen_skeleton_prefab.py --kind <K> [--force]

* **`corpse`** (Dark Fortress `SM_Chr_Corpse_01`, from the pack's single `Characters.fbx`: prep keeps `body_mesh`
  and drops the other 17 meshes). Classic rig with SIDED finger names (`Thumb_01_L`): `bone_map="synty_classic_sided"`
  (the same mapper, the suffix wins over the ancestor walk), 0.00 cm on all 43 joints. A slave WITHOUT a shield:
  slave swords, `shields=[]`, prefab from `skeleton` with `GuardPoints 0`, brains `corpse_aggressive/defensive`
  (the slave's numbers with every guard/bash chance 0 — that is what keeps `Guard` false; GuardPoints alone still
  swallows one hit). `SkeletonBody.Bleeds = true`: the sword bleeds it.
* **`df_skeleton` / `df_flesh` / `df_light` / `df_heavy`** (Dark Fantasy `SK_Chr_Skeleton_01 / _Flesh_01 /
  _LightArmor_01 / _HeavyArmor_01`). MEASURED 2026-09-12: this Epic-named rig IS the donor's — 0.000 cm on every
  shared joint except the donor's splayed left thumb (thumb_02_l 2.64, thumb_03_l 4.35 cm), rest orientations
  1-25 deg off (a rename would still bind wrong), `jaw` + 7 `ik_*` extras, no `root` bone. So they re-skin with the
  identity map `bone_map="epic"` (`jaw -> head`, `ik_*` dropped, must carry no skin) and `reskin_slack` (per-bone cm
  tolerance) for the thumb; NO retarget. The bodies all carry a skull; the light/heavy helmets are the pack's
  separate attachments (`Assets/DarkFantasy/SM_Chr_Attach_Skeleton_*Armor_Helmet_01.fbx`) seated at the head joint
  like the knight's (`attach(..., offset=(x, y, z))` cm nudges the seat if the in-engine look asks). Body slot
  `DarkFantasyShader` (+ `darkfortressshader` / `darkfantasyshader` rows in gen_skeleton_npc_vmdl.py REMAPS).
* **Tiers** (`DungeonBuilder.BaseSkeletonKinds / LightArmorKinds / HeavyArmorKinds`, `SoldierChance` then
  `HeavyShare` 0.4; everywhere mixed in until the floors are decided): base = slave, df_skeleton, df_flesh, corpse
  (slave stats); light = soldier_01, df_light, realms_01..03 (DamageScale 1.5, GuardPoints 3, `soldier_*` 110/130 hp);
  heavy = soldier_02, df_heavy (1.7, 4, `heavy_*` 140/160 hp — soldier_02.prefab edited by hand, df_heavy carries
  the numbers in its kind). `skeleton_spawn base|light|heavy|df|corpse|df_flesh ...` (SkeletonSpawner.Aliases).
* **Blood** (`Code/Combat/BloodVfx.cs`, v2 after the user rejected red sparks: "always normal sparks with each
  hit, but added blood, and gorey"): `HitVfx.FleshHit` = the bone hit unchanged + `BloodVfx.Spray`. A spray is
  three particle effects and some surface marks: *drops* (the engine's `blood_drop` sprite copied to
  `Assets/vfx/blood_drop.png`, faced along travel + motion blur, thrown with the blade, gravity 900, `Collision`
  on with `Bounce 0` + `DieOnCollisionChance 1`, `OnParticleDestroyed` queues the landing spot), *strings* (3-5
  faster drops with a `ParticleTrailRenderer` ribbon), *gibs* (`ParticleModelRenderer` over the Particle FX pack's
  `SM_GoreChunk_01..04`, imported by `gen_fx_assets.py` with the `"gore"` key -> `Assets/VFX/models/sm_gorechunk_0N.vmdl`
  on `shaders/GoreChunk.vmat` = heden_toon over a flat dark-red `vfx/blood_flat.png`; 1-2 on a light, 2-3 on a
  heavy, 5-7 on a kill; they collide, lie for `blood_gib_seconds` 8 and shrink away; their spin is a curve over
  life that hits 0 early so they stop rolling once down). *Marks*: the engine's four blood splatter colour maps
  (`Assets/vfx/blood_splatter_0N.png`, `tools/gen_blood_assets.py`) stamped as surface quads through
  `heden_ground_splat.shader` / `blood_splat_0N.vmat` via `GroundScorch.Place(..., material:)` (now per-material
  quads): a big one on the floor under the hit, one on the wall the swing points at, small ones where drops and
  chunks land (`BloodSpray` drains the queue on the main thread, 8 per spray); they stay for the run under
  `blood_marks_max` 150 (`BloodMarks`, oldest first) and die with the dungeon. Death of a bleeding kind =
  `BloodVfx.Burst` from `SkeletonBody` at the corpse spawn (every peer). Engine `Decal`s were ruled out: our
  `heden_toon.shader` never calls `Decals::Apply`, so they would be invisible on the dungeon. LESSON: a
  `ParticleSphereEmitter` throws at `Velocity 100` by default; v1's 6 s splat particle drifted 600 u ("floating
  away"); every static burst now sets `em.Velocity = 0`. Routing unchanged: `SwordSounds.StruckBody(..., flesh)` on
  a live body whose `SkeletonBody.Bleeds` is set (the corpse, the Dark Fantasy flesh skeleton, the Tormented Soul
  and all eight monsters; the bare skeletons and the knight do not), its ragdoll
  (`SkeletonCorpse.Bleeds`), and `StruckFlesh` for the corpse PROPS (`DungeonBuilder.IsFleshModel` -> `FleshProp`,
  judged like breakables, no collider). `SwordSounds.HitFlesh` is a .sound path for a wet take; none exists yet.
* Nothing of it seen in-engine yet: the DF skins on the shared clips, the helmets' seat, the corpse's no-shield
  fight, the blood drops and splat under the retro filter, the flesh props' box test on hanging corpses.

### The swordsman: Spirit Demon and Dark Lord on Synty's own Sword Animations (2026-09-12/13)

One enemy, two skins: Fantasy Rivals `SK_Character_SpiritDemon` and Dark Fantasy `SK_Chr_DarkLord_Male_01` (kinds
`spirit_demon` / `dark_lord`, `Assets/Characters/SpiritDemon/spirit_demon.vmdl`, `Assets/Characters/DarkLord/dark_lord.vmdl`,
prefabs `Assets/Prefabs/spirit_demon.prefab` / `dark_lord.prefab`, one brain `Assets/Npc/swordsman.brain`, one graph
`Assets/animgraphs/swordsman.vanmgrph`). The user wants them on Synty's **Sword Animations** pack
(`...\Animation\SwordAnimations\SwordAnimations\Polygon`, 118 clips: attack combos / flourish / stab / fencing / leaping,
block + parries, deaths, dodges + rolls, hit reacts + staggers, knockdown, stun, idles, draw/sheathe) instead of the Sword &
Shield clips, with a UNIQUE brain ("don't copy the knight's table") and NO new C# behaviour ("a unique set of animations is
enough": no dodge / block / riposte code); locomotion = the Sword & Shield walk / jog / run already on this rig (the pack
has none), death = ragdoll, tier between soldier and knight (200 hp, own spawn chance, alone or a pair). Both hold Dark
Fantasy `SM_Wep_Sword_01` (`DEMON_SWORDS`: 178.5 cm native brought to 107 cm with `DF_SWORD_SCALE` 0.6, the
FantasyKingdom-blade size the user picked before; `DarkFantasyShader` added to prep's SWORD_MATERIALS) -- the user wants
sword variety between the two later. The DF pack's `SM_Chr_Attach_DarkLord_Mask_01` / `_Cape_01` are `attach(...)` options.

    blender -b --factory-startup --python tools/retarget_synty.py -- --rig Assets/Characters/SkeletonSlave/skeleton_slave_01.fbx ^
        --out Assets/Animations/SkeletonSword --strip-prefix A_ --strip-suffix _Sword "<pack>/Polygon/**/*.fbx"   (once, ~10 min)
    blender -b --factory-startup --python tools/prep_skeleton_npc.py -- --kind spirit_demon          (and dark_lord)
    blender -b --factory-startup --python tools/gen_skeleton_ragdoll.py -- --kind spirit_demon       (and dark_lord)
    blender -b --factory-startup --python tools/measure_sword_clips.py                              (-> tools/out/sword_clips.json)
    python tools/gen_swordsman_animgraph.py
    python tools/gen_skeleton_npc_vmdl.py --kind spirit_demon                                         (and dark_lord)
    python tools/gen_swordsman_prefab.py --force                                                      (both prefabs + the brain)

* **The brain** (`gen_swordsman_prefab.py`, the truth; nothing hand-tuned). Ten attack states in `gen_swordsman_animgraph.ATTACK_CLIPS`
  order = the table's order, every one the `_rootmotion` variant so the lunges are real: light combo a -> b -> c (a weight 3,
  chains at 0.75 / 0.6, c is a thrust that lands at full extension), the fencing poke (weight 3, from 60 u out, 45 u lunge,
  can flow into the light chain), heavy combo a -> b -> c (weight 2, cooldown 4, hyper armor through the wind-ups, c steps
  in 51 u and shoves 340), the heavy stab (weight 2.5, cooldown 3, POINT BLANK only: SelectMax 60 -- measured, its tip only
  reaches 131 cm, so it is the answer to a player standing inside the cuts, not a long thrust), the flourish (weight 1.2,
  cooldown 7, two blade passes at 1.20 / 1.47 s, 80 deg arc), and the leaping strike (weight 1.5, cooldown 8, the sighting
  OPENER from 280 u, re-engages from 140-237 u only; 237 u of lunge clamped by `RootMotionStandoff` 40). Damage 12 / 14
  light, 19 / 21 heavy, 17 stab, 16 leap; brain cooldown 0.35-0.9 s, PreferredRange 85, strafe 0.45, back-off 0.15,
  Health 200, every guard chance 0 (no shield: `GuardPoints 0`). Posture 70 at 0.6 per damage (~115 damage) = the pack's
  KNOCKDOWN: hurt kind 5 is a three-state chain in the graph (`knockdown_begin_rootmotion` -> `knockdown_loop` ->
  `knockdown_end`, stepping on `finished`, all leaving on `busy == false`), `StaggerSeconds` 4.07 = the chain's length
  (the generator asserts it), 1.5x damage while down; `knockdown_loop` / `stun_loop` are NOT looping AnimFiles in the vmdl
  (`PLAY_ONCE`) so `finished` fires. Light hits = `hit_f/b/l/r_react` at 1.2 (heavy hits cannot get their own clip
  without code: `HurtKind` is direction-only). `SkeletonBody.BlockSound ""` on both prefabs, because kind 5 is a shield
  event to SkeletonBody (the sparks at the empty `shield_l` socket remain, cosmetic). Spawning: `SkeletonSpawner` rows
  `spirit_demon` / `dark_lord` (prefix `swordsman_`, brain `swordsman`), alias `swordsman`; `DungeonBuilder.SwordsmanChance`
  0.08 rolled after the knight, `count 1-2`, skin per body from `SwordsmanKinds`.
* **`measure_sword_clips.py`** (Blender): per clip the blade TIP (weapon_r + 93 cm along the knuckle line, prep's grip rule)
  relative to the root; hit = the tip's speed peak (3-frame averaged; `peaks` = every local maximum >= 40 % of the top,
  6 frames apart, so a row can pin `hit_peak` / `two_hit`), plus `max_reach_*` for thrusts (`extension=True` rows: the stab,
  the light finisher -- a thrust's tip moves along its own axis, so its speed peak undersells it, the monsters' bite rule).
  Reach = tip forward at the hit (HitRange = cm x 0.3937 + 4, clamped 50-110), lunge = root forward travel to the hit,
  Duration = last hit + 0.5 / 0.8 / 0.7 s (light / heavy / leap), longer than the clip if need be (the graph blends to the
  stance while the host still says busy = the recovery pause). Forward is calibrated from the leap's root travel.
* Clip sets: `clip_dirs` on a kind (`SWORDSMAN_CLIPS`: all of SkeletonSword + SkeletonSS `idle`, `walk_*`, `jog_*`, `run`),
  `gen_skeleton_npc_vmdl.py` lists several folders with a name filter each and refuses a duplicate name; the loco blend is
  the Sword & Shield radial with the pack's `idle_base` sword stance at the centre (a 60 fps and a 30 fps clip in one
  Blend2D is fine).

* **The clips are on Synty's classic ANIMATION rig**, not Manny: `Root/Hips/Spine_01/Shoulder_L/Elbow_L/Finger_01`, the right
  hand's fingers suffixed `_1`, plus `Prop_L`/`Prop_R` (10 cm off each hand), 52 bones, 30 fps, animation-only, no bind
  pose. Bone lengths equal the classic characters' and the donor's exactly (the joints coincide, as for every Synty rig so
  far). `tools/retarget_synty.py` renames the bones to their Epic twins on import (Blender rewrites the action paths) and
  runs retarget_fpp's helper-bone transfer onto the prepared slave rig, so the output (`Assets/Animations/SkeletonSword`)
  is on the shared donor rig and any skeleton kind can list it (`clip_dir` in skeleton_kinds.py; gen_skeleton_npc_vmdl.py
  takes the folder per kind). Every character re-skinned onto the donor (all of them) can play both clip sets.
* **A Synty animation-only FBX has no bind pose, so Blender's rest for it is the clip's FIRST FRAME** (the leaping
  attack's rest pelvis sits 1.4 m from the origin, hips yawed 17 deg). Two consequences, both fixed: (1) the pelvis
  helper must sit at the SOURCE pelvis (`bind(..., pelvis_at_source=True)`), or the whole body carries that offset into
  every frame (it did: 145.8 cm, constant, rotating with the pelvis); (2) retarget_fpp's aim-by-direction pre-pose leaves
  each bone's roll unmatched against such a rest (3-4 cm of joint error, a constant twist through torso and limbs). The
  exact pre-pose needs the animation rig's canonical rest IN THE SAME FRAME CONVENTION: the baselocomotion pack's
  `Polygon/Neutral/Additive/TPose/A_TPose_Neut.fbx` (`skeleton_kinds.POLYGON_TPOSE_CLIP`, all rotations zero, hips at
  the characters' 87.63 cm). Each donor bone is posed to `src_rest * tpose_rest^-1 * donor_rest`; verified 0.0 cm on
  root, pelvis and hand every frame, 0.0 deg on every limb, worst joint 1.8 cm at the donor's own splayed thumb tip.
* **A classic-rig CHARACTER file is NOT a valid reference for that**: Synty authored the character rig with X along the
  bone and post-rotations, the animation rig with Y along the bone and pre-rotations only, so their Blender bone frames
  differ by a per-bone constant even in the same pose (132 deg on the pelvis for a 5 deg change of direction, 7 deg on
  the head). Using `SM_Chr_DarkLord_Male_01.fbx` as the reference put every limb 90 deg off.
* Root motion: the `_RootMotion_` variants carry the travel on the `Root` BONE (a real LimbNode here; parry break 75 cm,
  dodge roll 4.35 m, leaping attack 7.35 m + 76 cm up), the plain variants keep Root at zero with the same hip motion,
  so root travel is copied bone-to-bone onto the donor's `root` and 38 clips get ExtractMotion (tx/ty; the vertical of
  `_rootmotionvertical` stays on the bone). Root rotation is never copied (always 0 in this pack).
* Sockets: `Prop_R`/`Prop_L` drive `weapon_r`/`weapon_l` through the same rest-delta helpers, so where Synty's prop sits
  on the hand as in the T-pose, the socket sits as prep placed it and prep's fist-derived grip still applies when the
  user picks weapons. `shield_l` has no source bone and keeps its rest seat on the forearm.
* prep changes: `slot_materials` (a body with several FBX material slots, mapped by FBX material name: the SpiritDemon's
  `_Ghost2` -> `FantasyRivalsGhost`, `lambert39` -> `FantasyRivalsShader`), `no_outline` (slots whose faces get no outline
  shell: the ghost slot is translucent, 246 of 2232 faces), and a hard error for a multi-slot body without the map. Both
  Unreal exports re-skinned with the identity `epic` map + the thumb slack, no joint refused.
* **The SpiritDemon's ghost slot** (2026-09-13, user: "all the white is supposed to be transparent"): its 246 ghost faces
  sit on ONE texel of `mystic_arms.png` (pale blue, alpha 0.075), and the editor had re-saved the vmdl with the ghost slot
  remapped onto the opaque body shader = flat white. `Assets/Shaders/heden_ghost.shader` (new): heden_foliage minus the
  cutout, alpha-BLENDED (`BlendEnable`, no depth write, `translucent`, two-sided, no Depth mode) at `g_flOpacity` 0.35 x a
  rim fade; the texture's alpha is deliberately not read (it would make him invisible). `FantasyRivalsGhost.vmat` moved to
  it; the vmdl regenerated with the ghost remap. If the editor rewrites the remap again, pick `shaders/fantasyrivalsghost.vmat`
  in the material list once.
* Nothing seen in-engine: the SkeletonSword clips compiling, the ghost shader compiling and its look, the fight itself
  (attack picks and combos, the stab at point blank, the leap opener landing at standoff, the knockdown chain stepping on
  `finished`, no shield clang on the break, the ragdoll with the sword riding the hand), the DarkLord's look.

## Tormented Soul (the flying ghost)

`SK_Chr_Tormented_Soul` is the skeleton rig minus the feet (39 bones, identical transforms), so it is a kind like the
others (`--kind tormented_soul`: dagger `SM_Wep_Dagger_07` on weapon_r, no shield, no ragdoll) and plays the same
retargeted clips. Its own graph, `tools/gen_soul_animgraph.py` -> `Assets/animgraphs/tormented_soul.vanmgrph`, uses
the user's picks: `jump_loop` hovering at the blend centre and the six `jump_in_move_loop_*` at radius 0.5 and 1
(a flier moves or hovers: no gaits), attacks 1 `attack_b_03_a`, 2 `attack_b_03_c`, 3 `attack_c_03_a` (the leap),
hurts `light_hurt_*`, a `dead` -> `die_01` state kept for a kind without a ragdoll (`SkeletonBody.Ragdoll` off; the
soul ragdolls: 10 capsules, `ragdoll_skip = PhysicsLegs` because its thigh/calf bones carry no mesh). Clips play at
1x (the user: quick enough); he is relentless through the brain instead (cooldown 0-0.15 s, durations cut where the
hand goes still). Measured at 60 fps: b_03_a strikes at 0.27 s (116 cm lunge), b_03_c at 0.48 s (209 cm), c_03_a at
0.52 s (290 cm, jumps to 133 cm).
`attack_b_03_c` had its head 36-51 deg off the neck the whole clip; `tools/fix_clip_bones.py --bones head --blend 0`
rewrote its head track to follow the neck (Blender 5: keys are overwritten frame by frame, there is no
Action.fcurves any more).

Every clip plays IN PLACE (`SkeletonNpc.ApplyRootMotion` off in `prefabs/tormented_soul.prefab`); the code flies the
body: `AttackSpec.LungeSpeed` moves it at the target until the hit frame, stopping 60 % of the reach short
(`TickLunge`), `MinRange` keeps the leap from starting too close, and `OpenerRange` fires the leap once per
sighting straight out of the approach (Idle -> Approach arms it). The renderer sits on a child object that
`SkeletonBody` floats `HoverHeight` 24 u above the agent with a slow bob; the capsule is raised to match. Speeds
220 / 290 / 340 (walk/jog/run; the skeletons run 243), brains `soul_aggressive` / `soul_defensive` (no guard, no
bash, cooldowns 0.45-0.9 / 0.8-1.4). Dungeon roll `SoulChance` 0.15 before `SoldierChance`; `skeleton_spawn soul`.

## Skeleton in the game (Code/Npc, Code/Combat)

Host-authoritative, heden's NPC pattern: `Assets/Prefabs/skeleton.prefab` = SkinnedModelRenderer + NavMeshAgent
(owns the position, UpdateRotation off) + CapsuleCollider (tags `enemy npc`) + `SkeletonNpc` (host truth: state,
health, brain, movement, attack timing, damage in, root motion of one-shots applied to the agent, wall-checked)
+ `SkeletonBody` (every peer: measures velocity from transform deltas into the blend, pulses attack/hurt/death
from synced counters, hitstop = PlaybackRate 0, hit flash = `materials/hit_flash.vmat` override). Brains are
`.brain` GameResources (`Assets/Npc/aggressive.brain`, `defensive.brain`: cooldowns, guard/shield-bash/charge/strafe
chances, preferred range, walk vs jog, health), rolled per skeleton with ±15 % jitter. Every skeleton that can
reach you swings on its own cooldown: an `AttackTokens` slot system used to let only two of them attack a target
at a time, and the ones it turned away fell through to the footwork rolls instead, circling at walking pace with a
direction that flipped every second or so — the slow jittery shuffle a pack did while a lone skeleton looked fine.
Footwork now only runs while the attack is on cooldown, and aggro is bounded in height by `AggroHeightBand` (160,
twice that before a chase gives up) so skeletons a floor above cannot lock on to a planar distance they can never
close. Movement destinations all come from `Toward` (a point on the straight line to the target, `standoff` short of
it) wrapped in `Lead` (at least 60 u ahead, so the agent runs instead of braking into an arrival). NEVER derive one
from the skeleton's own position: a `SlotPoint` that offset the standoff by 45 deg around the ring from wherever the
skeleton stood had no fixed point at all, curved harder the closer it got, and — moving with the agent — defeated
`MoveTo`'s throttle so the path was rebuilt every 0.15 s. Spread a pack with the agent's `Separation` (the prefabs
set 0.5), not with per-agent destinations. `CloseSpeed` is `RunSpeed`, always: closing distance is a run whatever the
brain prefers and whatever it carries, walking is footwork (circling, backing off) and never travel, and the shield
is never raised on the approach.

**THE MODEL HANGS ON A CHILD OBJECT ("visual"), AND THE ROOT'S TRANSFORM IS NEVER WRITTEN.** `NavMeshAgent`
subscribes to its own GameObject's `OnTransformChanged` and answers any write — a pure rotation included — with
`SetAgentPosition`, and the crowd's `SimulationAgent.SetPosition` runs `Path.Clear(); NeedsPath = true` with no
check that the position even changed (all decompiled from `Sandbox.Engine.dll`). `TurnToward` used to write the
root's `WorldRotation` every rendered frame, so every skeleton deleted and re-planned its path every frame and was
never actually navigating — the long-hunted "runs jittery then struggles to walk". The facing is now a host-owned
`_bodyYaw`, synced as `SkeletonNpc.BodyYaw` (a child object is not networked), and `SkeletonBody.TurnModel` applies
it to the model on every peer, easing it in on proxies. `Steer`'s wedge retry no longer cycles `_agent.Enabled`
either. Kinds (`SkeletonSpawner.Kinds`: `slave` = `skeleton.prefab`, `soldier_01/02` =
`skeleton_soldier_01/02.prefab`) are prefabs of the same components with a different model and `SkeletonNpc`
numbers: the soldiers have `DamageScale` 1.5, `GuardPoints` 3 and their own brains `soldier_aggressive` /
`soldier_defensive` (health 110 / 130, same personalities; `FindProfile` adds the kind's prefix and never mixes).
Orthogonal to the brain are the synced `SwordIndex` / `ShieldIndex`, the vmdl body-group choices (slave: broken 0 /
whole 1, the whole sword scales sword attacks by `SecondSwordDamageScale` 1.15; soldiers: greatsword 0 / curved 1,
heater 01 / 02, equal). The dungeon rolls kind (`SoldierChance` 0.25), brain, sword and shield independently;
`skeleton_spawn [slave|soldier_01|soldier_02|soldier] [aggressive|defensive] [sword] [shield]`, tokens in any order. The dungeon builds the navmesh itself after the floor exists
(`DungeonBuilder.BuildNavMeshAsync`): the scene's navmesh is enabled with deferred generation, and the ONE
thing a runtime-built world must do first is `nav.CustomBounds = true; nav.Bounds = <layout box>`: every tile
request (`GenerateTiles` and `RequestTilesGeneration` alike) is clamped to `NavMesh.Bounds`, which the scene
computes once at load from the static bodies present then, so a floor built in OnStart gets an empty box at the
origin and every request silently collapses onto one empty tile (the editor works because you enable the navmesh
after Generate; heden's worlds are static at load). Then `Scene.NavMesh.GenerateTiles` is awaited per 1024 u
chunk, nearest the spawn first, the spawn and an ambush room are probed and logged
(`[Dungeon] navmesh: N chunks ... spawn probe HIT/MISS`, one retry on a miss, re-probed each second) and
`NavMeshReady` is set. The NavMeshAgent owns the position (heden's pattern) with heden's stall ladder under it
(`SkeletonNpc.Steer`): no progress for 0.5 s -> the path is re-issued, three fruitless retries -> pinned to the
nearest navmesh point, agent cycled, and walked straight for a second (sphere sweep, wall slide, projected onto
the navmesh). Root motion (lunges, hurt shoves) is projected with `NavMesh.GetClosestPoint(next, 24)` too, so a
lunge stops at a wall instead of leaving the crowd agent off-mesh. Turning and steering run in OnUpdate, per
rendered frame: doing it in FixedUpdate strobes on the host (heden's ApplyLineDrive lesson).
`ProjectSettings/Collision.config` declares `npc`/`npc` = Ignore: the engine's `NavMeshGameSystem` sets each
agent's height from a sphere trace run `IgnoreDynamic()` + `WithCollisionRules(agent.Tags)` that takes the CLOSEST
hit up or down, and another skeleton's capsule (no rigidbody = static body) is a valid hit, so without the rule a
skeleton walking into another snapped onto its capsule and floated. `skeleton_navprobe` prints the navmesh bounds
and the physics bodies around you, regenerates that tile and probes it, for the next time the mesh is missing.

Speeds are the clips' own root travel, never faster (the feet would slide): walk_* 150 cm/s = 59 u/s, jog_* 405
cm/s = 159 u/s, `run` 617 cm/s = 243 u/s at the model's 0.3937 scale (`SkeletonNpc.WalkSpeed/JogSpeed/RunSpeed`;
`SkeletonBody` maps the measured velocity onto the blend radii 0.5 / 1.0 / 1.5, the run tier only straight ahead
and never while guarding). One rule closes every gap (`SkeletonNpc.CloseSpeed`): while there is ground between it
and the player it RUNS, whatever it carries and whatever its brain prefers; only inside `EngageRange` (90) does a
raised shield or a defensive brain drop it to a walk, and there the walk is footwork, not travel. It aims at a
standoff point short of the player (the crowd avoids agents, not the player's capsule). The player is on the same rule
(`CharacterLocomotionAnimator` walk 60 / run 120 / sprint 300, prefab and code): the FP arm clips bob at the
cadence of the third-person body clips (run 2.86, sprint 3.33 steps/s) whose stance-foot speed is Walk_F ~64,
Run_F ~115, Sprint_F ~300 u/s, measured with the Blender FK probe used for the skeleton (see git history of this
session's plan for the method: foot speed relative to the root while the foot is on the ground, calibrated on the
skeleton clips whose root travel is exact).
Prefab tags are comma separated (`"enemy,npc"`): a wrong string is rejected whole and a tagless skeleton hits its
own capsule in every line-of-sight trace and never wakes up (the spawner now checks and repairs the tags).
The dungeon populates the whole floor (`DungeonBuilder.SpawnSkeletonsAsync`, host, a tick after Build so the
colliders exist): `SkeletonsPerAmbush` in the ambush tier first, then every room/hall rolls `RoomSkeletonChance`
for a group of `RoomGroupMin..Max` and every corridor `CorridorSkeletonChance` for one, nothing within
`SpawnClearCells` (8) of the player spawn or in the secret room, capped at `MaxSkeletons` (40). A skeleton stands
on a cell the decoration kept clear (the walking line, then approach cells, then any cell without a blocking
floor prop) and the spot is sphere-swept down first: if the first thing under the cell centre is more than 12 u
above the floor level (a table, a crate) the cell is skipped and counted in the log line. `SkeletonSystem`
(GameObjectSystem) sleeps Idle skeletons further than 1900 u from every player and wakes them inside 1500 u
(synced `Dormant`: the host skips brain, steering and the crowd agent, every peer's `SkeletonBody` disables the
renderer, so a dormant skeleton costs one distance check); the idle line-of-sight trace runs 5x a second, not
per tick. `[Skeleton] N awake, M dormant` logs on change.
`skeleton_spawn [aggressive|defensive]` drops one in front of the player and reports whether the navmesh has
polygons there, `skeleton_navdraw 1` draws an in-game overlay (the engine's own `DrawMesh` only works from the
editor camera): a green/red sphere at the closest navmesh point to you, the built chunks, each skeleton's move
line and a label with state, brain, health, agent speed and FALLBACK when steering straight; `skeleton_debug 1`
logs decisions, hits and the root motion each clip actually moved, `skeleton_kill_all` clears them.

Damage goes through `DamageRouter.Apply(target, Hit)` -> the target's `IHittable` (`SkeletonNpc` forwards to the
host by `[Rpc.Host]`, `PlayerHealth` is owner-authoritative through a broadcast `TakeHit`). The sword in
`SwordSounds` is an arc: at the swipe moment every body (`enemy`/`npc` physics object from `Scene.FindInPhysics`)
inside `HitArcDegrees` (50° half-angle) of the view and within `Reach` (60) + the capsule radius is hit, each one
gated by a line-of-sight ray to its chest that ignores other bodies (a wall or pillar still shields it); one
hitstop and shake per swing however many it caught, the stone clang only when the arc caught nobody and the
view sweep met the dungeon. Every hit deals damage and shoves along the swing (right/left swings along the
player's right/left, overheads forward; `SwingRightMovesRight` flips the mirrored clips' meaning if the stagger
goes the wrong way); the fireball hooks `OnImpact` in `SpellCaster`. The skeleton picks the hurt clip whose
authored travel best matches the shove (hurt_r moves it to its left = the player's right when facing) and dies
with die_01 (falls back) only from a frontal shove.

Shield: "from the front" is judged from where the ATTACKER stands (±60° of the facing; the shove direction is
useless, a lateral slash shoves sideways). A frontal hit on a raised shield (past a 0.25 s raise grace) does NO
damage. The guard has `GuardPoints` (2): a light sword hit costs 1, a heavy or a fireball 2. While points remain
the shield holds: `HurtKind 4` = `guard_light_hit` (0.45 s, still guarding); the hit that spends the last point
breaks it: `HurtKind 5` = `guard_heavy_hit` (shield knocked aside, 1.0 s of the 1.16 s clip, open). Either way
the shield's own material slot flashes blue for 0.12 s (`materials/shield_block_flash.vmat`, heden's boss block
cue; the shield mesh carries `shaders/dungeonshader_shield.vmat`, a copy of the dungeon material with its own
identity, because per-slot overrides match by material and body/sword/shield otherwise share one), hitstop 0.08,
a clang. Real hits flash the whole body white 0.06 s. Hyper armor: the charge cannot be staggered from its chop
clip's start to the hit, the shield bash from its first frame to the hit (damage + flash only, `FlinchCount`); poise: 2 staggers
inside 2 s and hits stop staggering for 1.1 s while the skeleton swings back at once; coming out of any stagger
the next attack is allowed within 0.1-0.4 s regardless of the brain's cooldown, so a spammed skeleton still
gets its swings in. `PlayerHealth` adds the player's health, heden's red body flash (a material override on every
renderer of the character, the first-person arms included: `materials/player_hit_flash.vmat`, 0.15 s), a camera
punch (`ICameraModifier` before the arms), the HUD vignette + bar (`Code/UI/HitVignette.razor`), a shove on the
rigidbody, and a respawn on the spawn tier after 4 s.

Death (2026-09-11): the instant health hits 0 the owner switches the engine controller's `UseInputControls` off
(look stays on: the C third-person toggle keeps working and orbits the corpse), zeroes `WishVelocity` (the move
mode keeps consuming it with input off) and the body velocity, and broadcasts `ShowDeath(dir, origin)`. Every peer ragdolls its own copy of the body through
`SkeletonCorpse.SpawnCorpse` (the skeletons' corpse: `ModelPhysics` refuses to build on a proxy, so the ragdoll is
never networked), dressed by recreating every renderer bone-merged onto the body (hair, eyes, brows, clothing)
as bone-merged children of the corpse renderer, `pinned` so the 12-corpse cap never evicts it. The capsules are
`Assets/CharacterToon/prefabs/human_base_physicsshapelist.vmdl_prefab` from
`gen_skeleton_ragdoll.py --fbx Assets/CharacterToon/human_torso.fbx --out ...` (same Epic names, same cm, same
0.3937 scale as the skeletons; 16 bodies), referenced by a hand-added `PhysicsShapeList` in the hand-maintained
`human_base.vmdl`. The owner's view goes to `DeathCamera` (order 960, after the punch and the sway): the camera
sits between the corpse's `eye_l`/`eye_r` bones (the eyeballs; the controller's eye point is 8 cm behind them,
inside the skull, which read as "looking inside the head") facing the way the head faces (the view's rotation in
the head's frame, read off the live body at death), raw (you tumble with the body); `death_cam_nudge` (4 units,
the brows and hair fringe reach 2.8 past the eyes) and `death_cam_near` (2) keep the face out of the frame,
`death_cam_steady 1` smooths the rotation and drops roll. In third person the same component orbits the corpse's
pelvis (+`death_cam_orbit_lift` 16) with mouse look at `ThirdPersonCamera`'s distance and its wall trace. Body visibility is state-driven:
`FirstPersonView` writes `Off` while `IsDead` for the owner, proxies mirror the synced health in
`PlayerHealth.OnUpdate` (late joiners covered, no respawn RPC). While dead the arms rig hides and reads no attack,
`SpellCaster` cancels and reads nothing, the sway and the speed driver stand down, and the minimap holds its last
living view. `player_kill` kills the local player past the test-damage clamps (`combat_test_enemy_damage`
defaults to 1).

Attacks always animate: the graph's `guard_in`, `guard_out` and every hurt state accept the attack trigger too
(a pulse landing there used to be dropped: the host hit on its timer with no clip) and `SkeletonBody` holds the
trigger two frames; the `busy` bool (host in Attack or Hurt) is an exit from every one-shot so the code cuts the
clips' long recoveries. The charge is `charge_attack_start` (brace, steps back) -> `charge_attack_to_attack` (the
overhead chop); the pack's `charge_attack_end` has no strike at all (it un-braces). Timings measured in Blender
(sword socket speed/reach per frame; seconds at the graph's 1.15x; `SkeletonNpc.SwordA/SwordB/Charge/ShieldBash`):

| attack | clip(s) | hit | locked | start range | hit range (from the post-lunge root) |
|---|---|---|---|---|---|
| SwordA | attack_d_01 (lunging slash, frames 13-15; the overhead after it is a flourish) | 0.20 | 0.85 | 80 | 60 |
| SwordB | attack_d_02 (fast overhead chop, lunge 70 cm) | 0.20 | 0.80 | 70 | 60 |
| Charge | charge_attack_start + charge_attack_to_attack (lunge 132 cm) | 0.98 | 1.50 | 100 | 70, armor from 0.48 |
| ShieldBash | guard_attack_parry (the pack's name; a swing of the shield, an attack) | 0.30 | 0.75 | 55 | 50, armor throughout |

A hit also needs the player's feet within 72 u of the skeleton's height (no hits from the air).

Death is a ragdoll, heden's corpse setup ported (`Code/Npc/SkeletonCorpse.cs` = heden's NpcCorpse): the networked
body only carries `State Dead` + `DeathPush` (the killing blade's own travel, lifted 0.35 so bodies arc) +
`DeathHitPosition` for `DeathLingerSeconds` 0.3, and EVERY peer builds its own never-networked `skeleton_corpse`
from its live renderer, because `ModelPhysics.CreatePhysics()` returns early on a proxy. The corpse seeds every
bone from the last live frame (fingers, sword and shield keep their pose), creates `ModelPhysics` with the
renderer set BEFORE enabling (else bind pose), copies bone velocities, builds ball joints at runtime from the bone
hierarchy with frames along each bone's own axis, distributes a fallback mass by volume, keeps auto-sleep OFF (a
sleeping ragdoll locks solid the instant it lands), and applies one impulse (`mass * skeleton_corpse_speed` 200)
at the bone nearest the contact. The capsules come from `tools/gen_skeleton_ragdoll.py` (Blender headless ->
`Assets/Characters/SkeletonSlave/prefabs/skeleton_slave_01_physicsshapelist.vmdl_prefab`, heden's goblin format,
referenced by the vmdl's `PhysicsShapeList`): 16 bodies along each bone's measured rest axis. On this Epic-named
rig the bones run along X (mirrored to -X on the right side), not Y like heden's Synty rig; the script prints
the axis it read, never assumes one. `Collision.config`: `ragdoll` ignores npc, player and ragdoll (corpses
land on floors and nothing else); the player shoves corpses by velocity matching, the sword bats them
(`SkeletonCorpse.Bat`), skeletons see through them. Convars `skeleton_corpse_max` 12, `_distance` 2500,
`_seconds` 8, `_speed`, `_sleep`, `_autosleep`, `_damping`, `_friction`, `_shove`, `_debug` (prints bodies,
joints, mass, the bone-forward axis it derived and the push).

Hit VFX (`Code/Combat/HitVfx.cs`, static, on every peer through `SwordSounds.StruckBody/StruckWorld` broadcasts
and `SkeletonBody.Blocked`): one recipe on the fireball's sprite layers, a brief soft-spot flash (0.1 s,
16->34 u) and a few WHITE-GREY pixel sparks: hard `polygonparticles_circle_01` discs 2.6->1.2 u, `FaceVelocity` +
`Stretch` 0.12 into short bars, thrown along the swing and pulled down, fading in the air (no collision), alpha
blended, unlit, no bloom so they stay crisp under the filter. Bone hits 8 sparks along the swing; shield (spawned
at the `shield_l` bone on hurt kinds 4 and 5, heavier on the break) 10 off a coin-flip side; stone (the view sweep
met the dungeon) 6 off the normal. Heavies scale 1.4x and hitstop 0.09. Tried and REJECTED by the user
(2026-09-07): a point-light pop ("flashbanged"), the fireball's ring sprite, orange bullet-trail sparks, hexagon
bone chips and dust; sparks mean white-grey, never orange.
The local player's own hits also ripple the screen: `RetroFilter.TriggerRipple(world, scale)` -> the "Hit ripple"
feature projects the point each frame (`Camera.PointToScreenNormal`) and the retro shader's `ApplyShockRipple`
bends `srcUv` radially with `x*exp(-x*x)` around a ring growing at 1.6 screen heights/s for 0.25 s, peak 8 px.

Round two (2026-09-07): the contact flash is a hard four-point star (`polygonparticles_sparkle`, 30->8 u, 0.12 s)
over a flat white disc (14->3 u, 0.1 s), both alpha-blended white, no glow; sparks are pills (scale 1.4->0.7,
`Stretch` = ASPECT ratio 3, `FaceVelocity`; `hit_spark_rot` / `hit_spark_stretch` convars to tune live). Swing
direction everywhere (sparks, shove, sway) is the measured blade path: `SwordSlash.TryBladeDirection(clip, eye)`
= crescent End - Start in eye space (x fwd, y left, z up) turned into the world; the old left/right/forward rule
is only the fallback for clips without a crescent row. `SwordSway` (player prefab, camera order 950, owner only)
leans the camera against the swing during the wind-up (0.35 deg), whips it with the cut over the crescent frames
(1.6 deg, heavies 1.5x, roll 0.6 per degree of yaw), settles on a damped spring (k 380, damping 16); on connect a
counter-jolt against the swing (1.1 / 1.8 deg) and, for a landed heavy, +2.5 deg FOV for 0.15 s. Taken-hit shake
doubled (1.5 deg, 0.3 s) and the punch raised (pitch 5, roll 3.5).

Hit feel (`Code/Combat/HitFeel.cs`, static): freeze frames are per body, heden's hitstop (PlaybackRate 0 on
that renderer only, never the scene's time). Landing a hit: the skeleton freezes 0.09 s on its hurt pulse and the
arms freeze one frame LATER (0.09 light / 0.12 heavy) so the blade sticks; a directional camera kick against the
blade's travel (0.7° / 1.5 u recoil, heavy 1.2° / 2.5 u; an impulse with one overshoot, `HitFeel.Kick*`, applied
in `PlayerHealth.ModifyCamera`), a 1° FOV pop (1.5° heavy, `SwordSway`), the skeleton lurches `ImpactShove` 10 u
along the blade over 0.05 s before its hurt clip travels (half on a blocked hit), and `HitVfx.Ring`: one thin grey
ring opening 10 -> 46 u in the blade's plane at 28 % opacity (`hit_ring_alpha`, `hit_ring_rot` if it shows
edge-on) on body and shield hits. Taking a hit: the skeleton and your arms freeze 0.08 s, a symmetric shake, and a
chromatic aberration pulse from `RetroFilter.HitAberration` (8 source px, works at native resolution too) falling
off with the shake. `combat_test_damage N` makes every hit do N damage (0 = real).

Attack start ranges are DERIVED, never authored: `StartRange = HitRange + LungeBeforeHit * 0.75 - 4` (lunges
measured per clip: attack_d_01 37 u, attack_d_02 28 u, the charge 29 u before its chop, the bash 0), so a swing
only starts when a standing player will be hit (84 / 77 / 88 / 46 u). The shield still comes up on the way in
(the brain's `GuardApproachRange`, 120) but costs no speed to raise: the defensive walk belongs to the engage
loop's last 90 units. Raising it used to halve the approach speed, which read as the skeleton losing interest
and strolling the rest of the way (fixed 2026-09-09).

Driving the crowd agent has four rules learned the hard way, all of which look like "the skeleton walks slowly"
when broken. The first only shows with a GROUP: never send several agents to the same destination. Each skeleton
keeps a fixed angle of its own on the ring around its target (`SkeletonNpc.SlotPoint`, `_slotDegrees` +-45 deg
rolled with the brain), so a pack arrives as an arc; sharing one point made the solver spend its whole budget
pushing them out of each other while a lone skeleton ran in fine. The rest: judge stand-vs-close against a BAND and not a line (`_holdingGround`, close inside `PreferredRange`,
set off again past 1.4x) because `Agent.Stop()` throws the path away and per-tick churn never lets it accelerate;
keep `Stop()` idempotent for the same reason; and aim at a point at least `MinLead` (60 u) ahead (`SkeletonNpc.Lead`)
because an agent brakes as it nears its destination, so a standoff point ten units ahead is one it crawls to.
`skeleton_speeds` prints state / distance / asked / agent / real speed per awake skeleton to tell those apart.

# Sounds

`Assets/Sounds` holds heden's fireball set (`fireballcharge` loop, `fireballcastfast` on release,
`fireballimpact`) and sword set (`hitnothinglight`/`hitnothingheavy` whooshes, `lightabhit` body hit,
`swordhitstone`); `.sound` files reference the sibling `.mp3` as `sounds/<name>.vsnd`. `SpellCaster` plays the
cast sounds from the synced charge state on every peer (a `CastCount` bump tells a throw from a cancel);
`FireballProjectile.Explode` plays the impact. `SwordSounds` plays the whoosh at a per-kind delay from the
synced clip start and the owner sweeps for a hit (enemy/npc tag -> body hit, else stone). Footsteps
(`Footsteps.cs`: heden's distance accumulator, surface by name, landing thuds) use the base game's citizen
events, `sounds/footsteps/footstep-*.sound`, which s&box mounts from `core/`: nothing to copy.

Sound events (one `.sound` per event, `SelectionMode Random` over the takes, all in `Assets/Sounds`, written with
the same JSON as `HitNothingLight.sound`; files are referenced as `sounds/<lowercase name>.vsnd`):
`skeletonattack` (SkeletonBody, on the attack pulse, every peer), `bonebreak` (SkeletonBody, when the ragdoll takes
over), `playerhit` (PlayerHealth.TakeHit, the SwordBoss takes), `swordhittable` (SwordSounds, a swing that meets
wooden furniture: wood/table/bed/bench/stool/bookcase/crate/barrel/chest/coffin/plank/chair/door by object or
model name, "stone"/"metal"/"iron" win), `cracklingfire` (the five-minute clip), `ambiencedrip` / `ambienceknock` /
`ambiencehorror`. Skeleton footsteps: SkeletonBody's distance accumulator (stride 33 u walking to 73 u running, the
clips' step lengths) on the citizen stone/wood steps at 0.45 volume, pitch 1.15, until bone steps exist.
`Code/Audio/DungeonAmbience.cs` (a GameObjectSystem, local per machine, `ambience_enabled`, `ambience_debug`):
the three nearest torches/braziers/chandeliers within 700 u each get a crackle voice started at a random offset
into the clip (`SoundHandle.Time`), faded out when they drop out of the set, restarted when the clip ends; and the
unease cues at random intervals (drips 6-22 s close by, knocks 45-140 s and horror stings 100-300 s, both from
behind three times out of four, 250-750 u away, held off while a skeleton is engaged within 600 u, sometimes
repeated once a couple of seconds later). Random pitch and volume per play.

# Weapon materials

The sword (`SM_Wep_Ornate_Sword_02.fbx`) and staff (`SM_Wep_Staff_Gem_01.fbx`) were re-split in Blender into
material slots (`WoodHandleShader`, `BrassShader`, `BladeShader`, `GemShader`, plus the outline hull
`FantasyKingdomOutline` / `DungeonOutline`); their vmdls remap each slot by hand (`import_scale 0.2` on the
render mesh is hand-set too, so do not regenerate those two vmdls). Two shaders carry the looks, both on the
toon core and both usable in heden unchanged:

- `heden_gem.shader` = `heden_ice.shader` plus a Gem group. Every ice knob keeps its default, so heden's ore
  vmats can point at it with no change. New: `PatternObjectSpace` (patterns and the view ray in the mesh's
  frame, so a hand-held gem's inside only moves with the view), `Refraction` (fake IOR bend), `Edge*` (dark
  saturated rim), `Core*` (self-lit centre, >1 overbright for bloom), `Twinkle` (glint threshold wanders).
  `GemShader.vmat` (small sword gems) and `StaffGem.vmat` (big, alive, glowing).
- `heden_metal.shader` = `heden_toon.shader` plus `Reflect*` (the reflected view vector's world up quantised
  into sky/horizon/ground tints that multiply the albedo, noise-smudged), `Brush*` (object-space streaks),
  `Edge*` (view-only fresnel light after the shade), `Glint*` (heden_ice's light-driven catches).
  `BladeShader.vmat` (steel) and `BrassShader.vmat` (gold).

Both need `materials/water_noise_01.png` (copied from heden). Retro-filter rule for any new effect: big hard
bands with few tones; if a glint dithers into noise, raise its softness or lower its power.

# Fire

Every lit prop gets a `light` child with a PointLight and a `FireLight` (flicker amount, phase, base
colour/radius/position, and the flame recipe: kind, scale, offset, wall glow, smoke, sparks, tongues).
`FireLightSystem` drives them all each frame from one flicker curve, moves the light with the flame's
lean, and parks a code-built sprite flame (`FireVfxRig`) on the nearest 24 fires in play (spawn 1200,
keep 1500, matched to the retro fog) and on every fire in the editor (`FireLightSystem.EditorPreview`).
The flame sprites are generated: `python tools/gen_fire_sprites.py` writes `Assets/VFX/flame_*.png`
(teardrop tongue, lick, glow with stepped colour bands so they survive the dither). The candle flame
cards use the toon shader's runestone glow path (`g_flRuneEnable` in `DungeonFxFlame.vmat`) so the
system can push them overbright. Offsets per kind live in `DungeonBuilder.AddLight`; the FireLight
gizmo shows the flame base for tuning.

# Breakables (crates, barrels, vases)

```
"C:\Program Files\Blender Foundation\Blender 5.0lender.exe" -b --factory-startup --python tools/gen_breakable_kits.py -- [kit ...]
```

The Synty crates and barrels are ONE mesh object made of disconnected single-sided shells (a crate: frame cage, six
open 2 cm panel trays, six diagonal braces, corner blocks; a barrel: one stave surface, two lid discs, four
uprights, two hoops). The tool separates them by loose parts, solidifies every open part 3 cm inward (the inner
shell and rim inherit the outside UVs), cuts the stave shell into six wedges and every panel into two boards
(capped, cap UVs from a neighbour), rebuilds the crate frame as 12 clean box beams painted with the cage's own
atlas colour (cutting the solidified cage left its inner skin behind), drops the X braces, and gives each piece its
own outline hull (the piece pushed out a hair, faces flipped; Synty's `_Outline` only wrapped the outside). The
one-shell wide crate (05) gets no in-place pieces and breaks into planks only. Every piece carries a kind
(board, beam, stave, ring, lid, bit) that the game reads. Output: `Assets/Breakables/<kit>.fbx`, one
`<kit>_pNN.vmdl` per piece (`PhysicsHullFromRender`, single hull) and `Code/Combat/BreakableKits.Generated.cs`
(largest first). It re-reads its own FBX and checks the pieces' bounds match the source (node scale 1, cm, so
`import_scale 0.3937`). Extras that are not in place: `barrel_bits` (Barrel_Broken_02's bent staves and rings,
re-centred), `plank_01/02` and `vase_shard_01-04` (hull vmdls over the DungeonPack FBX). A subset run keeps the
other kits' data (`Assets/Breakables/kits.json`). Kits so far: crate_wood_02-05, barrel_01/03/broken_01.

In the game (`Code/Combat`): `BreakableProp` (one hit, `IHittable`) on every crate/barrel/vase the builder places
(`DecorBreakables.Run`, the last decor pass: corners then wall halves, crates stack 40%, a vase comes as a huddle
of 1-5 weighted low; Storage 10-16, Secret 4-7, rooms 2-6, corridors about one per five cells, halls +3, none in
the spawn room; catalog `crate` = wood 02-05, `barrel` = 01/03/broken_01, `vase` 01-09 leaving `vase_broken_01`
plus 5-8 shards). The props are seed-built on every peer, so they are not networked: `BreakableNet` numbers them
in spawn order and relays breaks by index with static RPCs; the swinger breaks its copy at once and reports, the
host tells the rest, a break is idempotent. The sword sweeps `BreakableNet.Near` (closest point of the box, LoS,
walls and other breakables block), a fireball breaks everything within `fireball_break_radius`; whatever stood on
a broken prop follows `break_chain_delay` later. `Debris` is heden's NodeDebris without the ore paths (Rigidbody
before collider, per-frame budget, farthest-first eviction at `debris_max`, terminal fall speed, landing brake,
thud with a scene-wide gap; `debris_life 0` keeps pieces for the run). Per break a kit throws all its in-place
non-ring pieces, 1-3 rings (1-2 for the damaged barrel) out of hoops and ring bits, and 1-2 bent staves or 3-5
planks (`debris_plank_scale` 0.5; the one-shell crate 8-10 planks). `BreakVfx` is the dust + chips in the HitVfx
style (`break_dust`, `break_dust_size`, `break_chips`, `break_dust_alpha`). Sounds:
`woodsmash.sound` (WoodSmash01-03) for crates and barrels, `vaseshatter.sound` (VaseShatter01-05) for vases,
`debristhud.sound` for landings. The `debris` collision tag ignores player, npc and ragdoll. Not done yet:
broken-set sync for late joiners, navmesh holes where crates stood, skeleton swings breaking props, metal/ornate
crates.

Wooden furniture (2026-09-07, user's list: tables, stools, bench, beds, bookcases, coffins with their lid, the
weapon rack; NOT chests, the vat, the ornate crate or the potion pole) uses the `furniture` rule: every loose
part is a piece, sheets become boards, anything over 1.5 m is segmented, a bulky carcass quartered, bits under
16 cm join a neighbour, at most 28 pieces. Any model with a kit becomes a `BreakableProp` (the catalog's
Breakable flag only means "placed by the breakables pass"). Big furniture takes `break_furniture_hits` (2)
light hits, shuddering (`break_shake`) and shedding chips in between; a heavy or a fireball breaks it at once;
stools and lids go in one. Hits relay as the resulting hit count, so a predicted hit and its echo are one hit.
Whatever small static prop stood on the top (candles, books, jars, a candle's flame card) is flung as debris on
a box collider and its object destroyed (the candle goes out); a coffin's lid is parented under the coffin and
bursts with it.

Wood material (2026-09-08): every wooden model renders on `Assets/Shaders/WoodShader.vmat` (the sword handle's
look under its own name: paper grain in object space at 0.3, ambient floor 0.05) instead of `DungeonShader.vmat`.
The list is `tools/wood_props.py` (`WOOD_MODELS`: the furniture, crates and barrels, planks and logs, door leaves,
timber; and `is_wood_kit` for the breakable kits, everything but vase shards and books). `python tools/wood_props.py`
rewrites the `dungeonshader.vmat` remap in `Assets/DungeonPack` and `Assets/Breakables` in place (`--check` reports
only); `gen_dungeon_assets.py` and `gen_breakable_kits.py` import the list so a regeneration keeps it. A non-wood
model with some other hand-set vmat (the chitin shield on woodhandleshader) is never touched.

# Traps

Seven kinds (`TrapKind`, `TrapCatalog`), built 2026-09-08. Spike floor = pressure (step on the grates, a rattle,
spears up through them, resets). Swing blade = cycling: THREE blades abreast across a hallway (user, 2026-09-20: "one extra in the middle"; each squeezed to two thirds of its width so three fit where the pair did, length and reach kept), hung
from the middle cell's ceiling, swinging along it in a real pendulum motion (integrated table, 85°, a hang knob for
extra dwell at the top), half a period apart. Flame head = proximity: a nozzle at head height beside a room's door
whose jet crosses the doorway cell; the jet box is trigger and damage volume, tested body-aware, so crouching passes
under it; a fire-breath particle jet. Arrow wall = pressure: four holes in one side wall of a hallway cell (old
arrows stuck in the wall opposite), clicks, then a volley of arrow models across the cell at chest/head height
(crouch under it). Saw rail = cycling: rail pieces down the middle of the bare interior cells of a run (needs two or
more), a disc riding a cosine between the ends, spinning with its travel; the corridor edges stay walkable. Acid head
(DISABLED 2026-09-20, user: "remove the acid trap replace all with firetraps"; its turn in the rotation is a second flame head)
= proximity: a nozzle low beside a room's door, dripping onto the doorway cell (a green-eaten stain); step on and it
sprays the cell (a splash) and leaves a bubbling pool that burns feet for six seconds. Wall slab = pressure: a slab on
shafts set into one hallway wall with solid rock behind (two cells) and a solid wall opposite; step in front and it
grinds, shoots across the cell and squashes everything in it against the far wall (a damage volume with a hard shove,
no collider), then creeps back. Everything hurts skeletons too, skeletons trigger plates, a fireball sets off every
plate within `trap_fireball_radius`, and every trap has `Freeze(seconds)` for the ice spell that does not exist yet.

Placement is the decoration's first pass (`Code/Dungeon/DecorTraps.cs`, run right after the roles, own RNG
stream) so a trap's cells are claimed before furniture, litter or breakables could cover a tell: hallway kinds on
the middle interior cell of a straight 1-wide corridor (never a run end or a doorway cell; the spike plate needs
nothing living below, the blade a ceiling), room kinds on the door the player arrives through (the portal cell,
head on a side wall face); never in the spawn, exit, shrine, loot or secret tiers; one per tier; spine first,
nearest the spawn first; `TrapBudget` (4-6 per floor, `DungeonBuilder` TrapsMin/TrapsMax, kinds rotation, gap of 6
walking cells halved on a second pass, at least 3 from the spawn, the first trap is always a hallway one). Traps
live in `DecorResult.Traps` (never in Props) with `TrapCells` for the floor tiles and skeleton spawns; gentest
checks them (`CheckTraps`) and prints a `traps:` line per floor.

Runtime (`Code/Traps`): the builder spawns each placement in order and `TrapNet` numbers them, the BreakableNet
pattern (nothing networked, static RPCs by index). The host decides triggers (`TrapSystem` tests feet against the
trap's world cell boxes) and broadcasts "arm N"; every peer runs the wind-up on its own clock. A player's own
client judges trap hits on itself (owner-authoritative health, no host echo), the host judges skeletons; the blade
sweeps three points up each body against its crescent in this and the previous frame, one hit per half period.
Trap damage kinds `trap_*` skip the `combat_test_enemy_damage` clamp (`combat_test_trap_damage`), never crit,
ignore shields; `trap_flame` reads as magic. Convars `trap_debug` (boxes + labels), `trap_active_radius`,
`trap_spike_*`, `trap_blade_*`, `trap_flame_*`, `trap_arrow_*`, `trap_saw_*`, `trap_acid_*`, `trap_slab_*`; commands
`trap_list`, `trap_fire [i]`, `trap_freeze [i] [s]` (no index = the nearest trap). Sounds are stand-ins from the
existing sets (the saw has none yet).

# Spells and staff modifiers (Code/Spells)

**The Isaac rework, 2026-09-20.** The user found the loop "hard to grasp" next to Binding of Isaac (pick a thing up,
see at once what it does), so the staff changed shape. What follows this block describes the machinery, which is
unchanged; these are the rules on top of it now:

- **One spell, any number of scrolls.** `StaffLoadout` is `SpellId` plus the packed stacks (`ModsLo`/`ModsHi`). No
  slots, no swapping, no taking a scroll off. `ModifierStacks` is TWO longs now (31 ids each, 2 bits an id): the
  pool is 38 and `SpellHitInfo` carries `Mods` + `Mods2` through every hit RPC.
- **No cast time.** RMB throws the spell as soon as the arm is up (`spell_raise_seconds` 0.17: the raise clip at the
  arms graph's x4, which now always plays out before the throw; `tools/gen_fp_arms_animgraph.py`) and, held, throws again
  every follow-through (`CastDurationSeconds`), so mana is the governor. `spell_instant 0` brings the old
  hold-to-charge rhythm back for comparison. Charging lives on as **Gluttony**: a tap casts, a hold swells the spell
  (`ChargePower` 0..1 through the Cost and Damage stages); the bespoke charge looks only show for a real hold
  (`SpellCaster.ShowsCharge`).
- **The pool** (`ModifierDatabase`, 38): each scroll has a `Banner` (the one line the pickup prints), a `Black` seal
  (it costs or REPLACES something: Hollow, Wrath, Orbit, Amber, Sloth, Hummingbird, Wild Page) and its own 2-rune
  `Code`. Reworked: Overcharge (every 4th/3rd/2nd cast is overcharged), Kindling (a sword hit primes the next cast),
  Duality -> The Stranger (a random learned spell alongside), Conduit (the one spell), Hound (beams lean, chains
  widen). New: Mirror, Hummingbird, Wild Page, Familiar, Hail, Retort, Edge, Last Drop, Horizon, Needle, Return,
  Lattice, Anvil, Sloth, Fracture, Fuse, Ram, Brand of Embers / Frost / Storm, Pyre, Harvest.
- **The balance rule.** PATTERN scrolls (Multiplication, Mirror) multiply each other. PROC scrolls (Familiar, Hail,
  Retort, Conduit, Edge, Fracture shards) inherit every flight and impact scroll but are flagged `Proc`/`Shard`
  (`CastContext.IsProcLike`): never multiplied, never echoed, never the source of another proc. What the HOST makes out
  of a hit (a Storm arc, a Fuse going off, a Pyre, a Ram's wall) is flagged `Secondary` and triggers nothing in
  `SpellHostEffects`, so one pyre never lights the next. `spell_bolt_cap` (64) fizzles a caster's oldest bolt.
- **Every scroll does something on every delivery.** The bolt side is `SpellProjectile` (pierce, the wall ghost, the
  return, the horizon's growth, `FractureShards`), the ray's is `SpellBeam` (wall pierces, the lean, the far-end
  damage, the end-point burst, the return ray, fixed origins for rays that do not leave the staff), the chain's is
  `ChainLightningCast` (no-LOS links, bursting links, the retrace, lingering links). The Lattice is `LatticeField`
  (owner side, 5 Hz, one unreliable broadcast for the lines).
- **Where they fire from.** Owner: `SpellCaster.CastFamiliars` / `CastHail` (Trigger stage, paid casts only),
  `OnHurt` (The Retort, from `PlayerHealth.TakeHit`), `OnSwordHit` (Kindling's primer, The Edge's strike, The
  Conduit's roll; `SwordSounds` calls it). Host: `SpellHostEffects.OnHit` (from `SpellHostTuning.ApplyStatus`: the
  brands, Fuse, Ram via `SkeletonNpc.RamShove`), `.OnKill` (from `SpellDeathStage`: Pyre, Harvest's `ManaMote`),
  `.Tick` (the fuses). `FamiliarWisps` draws the wisps on every peer from the synced stacks.
- Enemies get `npc_floor_health` (+12%) health and `npc_floor_damage` (+6%) damage per floor past the first
  (`SkeletonSpawner.SetFloor`).
- **Ticks count.** The laser and the void orb only ever deal Tick hits, so `SpellHostEffects.OnHit` runs for ticks
  too: they brand like any hit and lodge a Fuse, throw a Storm arc or Ram at most once per body per 0.6 s, weighed up
  to about a cast's worth; an orb's area counts as its touch. The laser breaks the props it crosses, bursts visibly
  where it ends with The Anvil, throws The Lattice's lightning from its end, and plays the impact sound where it
  strikes (`laservoidmage` is the cast sound at the staff). An orb (no longer taken through walls by The Needle, which is bodies only since 2026-09-22) drifts home
  with The Return, sheds small orbs with The Fracture and is paid by The Horizon where it comes to rest.

Built 2026-09-08 from MAGIC_concept.md / MODIFIER_spec.md: seven spells (`SpellDatabase`: fireball, ice shard,
chain lightning, laser, void orb, plus the user's Swarm (six homing, wobbling bees) and Siphon Bolt (slow, leeches
half its damage back as health)), a staff with two spell slots and four modifier slots (`StaffLoadout`, synced, on
the player prefab; the docs said three, four won the playtest), sixteen modifiers (`ModifierId`; The Well and
Contagion were cut; Gluttony, The Hound and Wrath added; stacks pack two bits per id into a long, tables in
`ModifierDefinition.cs`) resolving through the seven-stage `CastPipeline` on the caster's client. Damage
to enemies is always ONE `SpellNet.Impact` request to the host (direct victim + splash); persistent
areas (Void Orb zone, The Wake, Amber mines, laser burns) are `HazardField` hazards registered once and ticked by
the host (`SpellHostSystem`); statuses, kills, The Turncoat (raised skeletons on `Team.Ally`), The
Hollow's refund and the Siphon's leech (`SpellHostTuning.OnDamageDealt`, through `PlayerHealth.Refund`) run on the
host (`SkeletonStatus`, `SpellDeathStage`). Every hit carries a `SpellHitInfo` (spell id, packed stacks, proc depth,
cast id, lineage flags) through `DamageRouter.Hit.Spell`. Homing (a spell's own or The Hound's) and the bee wobble
live in `SpellProjectile` on the owner (targets picked from the synced skeleton state); Wrath's max-health cut is
`PlayerHealth.MaxHealthScale`, written by `StaffLoadout` from the slots, and its damage term reads the missing
fraction in `DamageStage` and `ResolvedCast.Preview`.

Input is one rhythm for every spell: hold RMB to charge, let go to fire. The laser is a burst, not a hose — the
release burns a fixed `BeamSeconds` window (paid for once, like any cast) while following the crosshair.

Looks: the bolt is `SpellVfx.BuildProjectile`, the charge is `Code/Spells/Vfx/ChargeVfx.cs`, one bespoke recipe per
`SpellBodyStyle` (fire gathers embers inward, ice cuts a shard that points down the shot, the chain is a caged
spark throwing arcs, the laser is a lit gem with a focus ring and an aim hairline, the void is a small singularity
with wisps falling in, the swarm is bees circling the hand, the siphon is a necromancer's green: a dark hollow
wrapped in cold witchfire inside turning sigil rings, drawing motes of life in). One palette per spell, so colour
alone tells them apart: orange, ice blue, violet, red, purple, amber, necrotic green. The caster owns
the charge rig's position AND rotation, so anything with a direction follows the aim. A rig knows its mesh children
(`Rig.Meshes`) and its moving parts (`Rig.Scaled`, `IRigScaled`), which is what makes them grow with the charge and
vanish the moment it is released — without that a crystal or a void sphere hangs at the gem after every cast.

Testing: RMB casts (hold to keep casting). The `spell_*` commands below set the staff directly,
bypassing the inventory. Console: `spell_set <spell>`, `spell_mod <scroll> [copies 0..3]`, `spell_clear`, `spell_loadout`, `spell_instant 0|1`,
`spell_gold <n>`, `spell_list`, `spell_infinite_mana 1`, `spell_debug 1` (logs the resolved cast), `spell_burst`,
`spell_arc`, `spell_fx <model>`; host tests `spell_test_raise`, `spell_test_status slow|burn`, `spell_test_area`,
`spell_test_turncoat`, `spell_test_wake`, `spell_test_leech [damage]`, `spell_test_wrath`, `spell_hazard_debug 1`,
`skeleton_teams`, `skeleton_spawn ... ally`. Host numbers are `spell_*` convars on `SpellHostTuning`.

## Inventory HUD, scrolls and loot

Rebuilt 2026-09-20 with the Isaac rework above: the satchel, the library, the four sockets, the brazier and the
drag-and-drop are gone. Tab opens one HUD (`Code/UI/SpellLoadout.razor`):

- **Staff**: your one spell, the spellbook (in the village a click on a learned spell makes it the spell runs START
  with), a board of sockets holding the scrolls taken this run in the order they were taken (three rows, growing;
  tier pips, a cold edge on black seals), the cast's numbers, the boons, and the details of whatever is hovered (all
  three tiers, the one you have lit). Its cards wear the STRETCHY kit frame (`.dfk dfk-panel`): the baked
  `df-card-*` frames are pictures of the old card sizes.
- **Index**: a scrolling window (`.index-scroll`, hover hit-tested through `Panel.ScrollOffset`).
  Every spell and enchantment; the ones never held show as `???`.
- **Armory**, **Wardrobe**, **Dev**.

The data lives in `Code/Inventory/`:

- `Scroll`: a scroll's identity plus `ScrollText`, which holds everything the player reads or sees about a scroll
  (2-letter code, runes, colour, icon, text, `StackLine` per tier).
- `PlayerInventory`: what is learned and known, `Take` / `Buy` / `ChooseStartingSpell`, `RollEnchantment` /
  `RollSpell`, saving and the run hooks.
- `WeaponSkins`: the skin table.

Rules:

- **E takes a scroll, for good.** An enchantment goes on the staff for the rest of the run; three copies is the cap,
  and a scroll at three leaves the drop pool. A spell scroll replaces the staff's spell, and the old spell is left
  lying in its place so the swap can be undone. Nothing is picked up automatically.
- **Spawns fill the floor** (`DungeonBuilder.RaiseToTarget`): every eligible tier gets a pack, then the rest of the head
  count goes one body at a time to whichever tier is emptiest for its size. It used to multiply only the groups that
  had rolled, which left rooms empty next to rooms of eight. Wanderers only walk to spots they can see from home.
- **Traps**: NPCs no longer set them off (`trap_npc_trigger` 0) but are still hurt by one that fires.
- **Sealed the first time, open after.** A scroll the player has never held lies closed and the prompt only says
  "Break the seal" (gold for a spell, "black seal" for a scroll that costs or replaces something). Once it has been
  on the staff it is KNOWN (`IsDiscovered`, the same list as the Index): every later copy lies open, the prompt names
  it with its one line, and taking it is a choice.
- **On screen.** `UI.StaffHud` (bottom right) shows the spell in an iron slot and every scroll taken, eight to a row,
  stacking upward; the slot's frame turns gold while Kindling is primed, violet when the next cast is overcharged. The
  spell's name no longer sits under the crosshair. A known scroll in the world stands up, faces the player and wears
  its runes on the paper (`ScrollPickup.ShowRunes`, the shop's trick).
- **The reveal.** `UI.ScrollReveal` opens the scroll on screen the moment it is taken (the inventory's old unwrap
  timeline, faster, never blocking): the whole show for a first time, a quick unroll for a known one.
- **A run's scrolls end with the run**, like its boons: a death or a walk home strips the staff back to the starting
  spell. A scroll bought in the village goes straight on the staff and rides into the next run.
- **A death keeps ONE** (user, 2026-09-22): the death card (under the black, after the runes) lays out the run's
  enchantments as tiles and waits for a click; one copy of the chosen one stays on the staff for the next run
  (`RunManager.OfferingKeep/KeepChoices/ChooseKeep`, `PlayerInventory.OnDeath( keep )`). Nothing on the staff = no
  card. An abandoned run and a won run keep nothing, as before.
- **Spells are learned for good** the first time their scroll is taken. A spell scroll offers a spell the player
  knows but is not holding; at most once a run it is a spell they have never had (`scroll_new_spell_chance`).
- Unlocks, what is known and skins are saved the moment they happen and are never lost.

Loot:

- **Scroll drops** (`Code/Run/ScrollDrops.cs`; the pre-chosen carriers with a scroll over their head lasted half a
  day). The floor owes `floor_scrolls` (5, raised from 3 when the run went to four floors on 2026-09-22) enchantments
  and `floor_spells` (1) spell scroll, 24 scrolls a run. When the floor is spawned
  each gets a KILL COUNT, spread with jitter between `floor_scroll_first` (6%) and `floor_scroll_last` (50%) of the
  floor's hostile bodies; the kill that reaches a count drops that scroll where the body fell (nearest walkable
  spot), whoever the kill was. Whatever is still owed when the player comes within `floor_scroll_exit_range` of the
  exit portal is laid out by it (`RunManager`), so a player who fights little still misses nothing.
- **No secret room, no chests** (`DungeonGenerator.SecretRooms` false; user: "we dont have enough usecases for it and we
  dont want the player to miss anything"). It was placed from its own rng stream, so nothing else moved. DecorSecret,
  the tell wall, the break-in and ChestLoot stay compiled behind the flag; Dungeonwise shows shards instead of walls.
- **Four floors, one per biome, then the boss** (`RunFloors`; 2026-09-22, back from the eight of 2026-09-13: "1 floor
  per biome"): 14 to 24 rooms carrying 84, 130, 156 and 192 enemies (the count was tripled the same day it was cut:
  "many enemies is what makes it difficult"), each floor's bodies +12% health and +6% damage on the one before
  (`npc_floor_health`, `npc_floor_damage`). Each floor has THREE shapes (warren, pit, cathedral), each with its own
  size and rooms factor, and the floor's seed picks one (`RunFloors.For(floor, seed)`; same seed, same shape, so the
  fixed first run stays fixed). `gentest ramp` runs every shape of every floor (12 rows) and prints SHORT for any that
  cannot hold its count.

Chests (`Code/Run/ChestLoot.cs`) are plain breakables: every one drops 3-6 shards and has a 15% chance of a potion.

In the world, `ScrollPickup` floats, turns and glows like a shard and is taken with E like a potion (white outline on
the one in focus); it is the closed bundle while unknown and the open scroll (`sm_prop_scroll_open_01`) once known, and
a black-sealed enchantment glows a cold violet. Spell scrolls wear the gold-sealed atlas (`shaders/realmsscrollspell.vmat`, atlas `04_a`), enchantment
scrolls the pack's red seal (`shaders/scrollfull.vmat`); both are at a FULL ambient floor, so a scroll is lit wherever
it lies. Potions glow red or blue (`potion_glow`).

**Pickup lighting** (user, 2026-09-17): a pickup in an unlit corner was invisible, so the pickups draw with their own
copies of the pack materials at a raised ambient floor - `tools/gen_lit_pickup_vmats.py` writes
`Assets/Shaders/PickupLit/*.vmat` at 0.3 (potions, weapons on the floor and in racks) and the two scroll materials at
1.0, and `Code/Rendering/PickupLit.cs` swaps a renderer's slots for the twins by name. The name list is in both files
and must match; a missing vmat would render purple, so nothing is probed blindly.

HUD icons are rendered from the Dungeon Realms scroll models:

    "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe" -b --factory-startup --python tools/render_scroll_icons.py

The script writes `Assets/ui/scrolls/{closed,open}_{mod,spell}.png`. The rune code is drawn over the icon by the HUD.

Weapon skins (`Code/Inventory/WeaponSkins.cs`):

- **How they unlock** (user, 2026-09-17). A weapon a monster carries is unlocked by killing one carrying it:
  `WeaponSkin.Carriers` lists the NPC kinds and `CarrierSword` which of that kind's two weapon rolls it is, and
  `SkeletonNpc.Die` hands it to whoever swung (`PlayerInventory.UnlockSkinFor`). Nothing drops - the unlock is the
  reward. Everything nobody carries (`FromCrates`: the other swords and every staff) turns up either in a broken crate
  or barrel (`crate_skin_chance` 5%, `ChestLoot.CrateSkin`) or standing in a weapon rack (`rack_skin_chance` 5%,
  `ChestLoot.RackSkin`, rolled at build time off the floor's seed), or will be bought in the shop. Both pools skip
  weapons already found and never put two of the same on the floor. Vases are too small to hold one.
- **On the floor.** `SkinPickup` is the player's own copy of the model, white outline and E like the potions (a weapon
  in view wins over a scroll, a scroll over a potion). A crate drop floats, turns and carries a cold light
  (`skin_pickup_scale`); a racked one (`InRack`, `skin_rack_scale`) stands where the builder put it, leaning into the
  rack, with no float, no turn and no light. Taken, it unlocks at once: skins are cosmetic, so there is nothing to
  carry home and nothing to lose on a death. `skin_drop [name]` drops one in front of the player.
- **The banner.** `UI.UnlockNotice` is the card that drops in high above the crosshair for a weapon unlocked or a
  scroll bought, fed by `PlayerInventory.SkinFound` / `ScrollBought` (a scroll TAKEN, or a spell learned, opens on
  screen instead: `UI.ScrollReveal`). Gold edge for what is kept for good, red wax for an enchantment scroll. The Armory tab marks a weapon new until it is looked at.
- **Player-only copies.** Every skin except the starting sword and staff is held as its own copy in
  `Assets/PlayerWeapons/<skin id>.fbx/.vmdl`. The pack models (`Source`) keep their own size, and the monsters' swords
  are baked into their character FBX anyway. Build the copies with
  `blender -b --factory-startup --python tools/gen_player_weapons.py [-- --only id1,id2]`. The tool rebuilds each
  outline shell from the blade (exact copy, smooth normals, no baked offset) and sets each copy's `import_scale` from
  the weapon tuning.
- **Tuning in game.** `weapon_tune` (sword) or `weapon_tune staff` opens a panel like the potion tuner: pick any skin
  with the arrows, then nudge its offset, rotation, spin about the blade, slide along the shaft, flip, spell tip and
  import scale. A new import scale shows at once as a scaled prop. Save writes `voidmage/weapon_tuning.json` in the
  s&box data folder.
- **Baking.** Running the tool again merges that save into `Assets/data/weapon_tuning.json` (the committed copy) and
  bakes the import scales. `weapon_tune_reload` rereads both files.
- The four FantasyKingdom staffs were toonified with
  `TOON_BASE_MAT=FantasyKingdomShader blender -b --python tools/toonify_adventure.py -- <fbx dir> <out dir>`.

Motion runs on one C# clock, like the boon screen; everything is laid out in C# and hovered by rectangle.
`inv_anim_speed 0.25` slows everything for review.

Console:

- `inv_give <name|random|spell|all> [count]` puts scrolls straight on the staff, as if taken.
- `inv_drop <name|random|spell>` drops one in front of the player, to take with E.
- `inv_clear` takes every enchantment off the staff.
- `inv_skin <name|all>`
- `inv_unlock_all`
- `inv_reset`
- `inv_list`
- `carrier_scrolls`, `carrier_spells`, `carrier_mark_scale`, `scroll_new_spell_chance`
- `inv_burn_mod` sets the shards a burn gives.
- `chest_shards_min`, `chest_shards_max`, `chest_potion_chance`, `chest_spell_chance`
- `scroll_hover_height`, `scroll_scale`, `scroll_pickup_sound`

## FX meshes (Synty POLYGON Particle FX)

    python tools/gen_fx_assets.py [--src "C:\Users\skipp\Desktop\Speelboord Studios\PolygonFx\FBX"]

Copies the SELECT table of FBX (crystals, shards, spheres, spikes, cones, ring, star, spark, light-ray cube, flame,
smoke) to `Assets/VFX/models/` and writes a no-physics vmdl per mesh in the `fx_ring_mesh.vmdl` format, every slot
remapped to `shaders/fx_unlit_add.vmat` (glows) or `fx_unlit_alpha.vmat` (solids); `fx_unlit.shader` is white_unlit
plus the renderer's Tint (vVertexColor), two-sided, no depth write, additive by `F_ADDITIVE`. Import scale is read
off the FBX extent (cm-authored 0.3937; the metre-authored Star / LightRayCube 39.37). `SM_LightRayRound 1.fbx` is
ASCII FBX and is skipped. The editor compiles the vmdls on first open; `spell_fx fx_crystal_02` spawns one 120 u
ahead for a size check. `StyleVfx.FxModel` returns null (and the mesh layer is skipped) until they compile.

# Overworld source scenes (Fantasy Kingdom + Adventure)

Two imported Synty environment scenes, kept as separate source/preset scenes to build the
overworld from. Neither is wired into gameplay; `Assets/scenes/minimal.scene` is untouched.

| scene | from | objects | models |
|---|---|---|---|
| `Assets/scenes/fantasykingdom_houses.scene` | heden's import of `Demo_Layout_Houses_With_Interiors.unity` | 5459 | 397 of `Assets/FantasyKingdom` (1091) |
| `Assets/scenes/adventure_demo.scene` | Synty `POLYGON_Adventure` `Demo.unity` | 1292 | 110 of `Assets/AdventurePack` (268) |

## Fantasy Kingdom — copied from heden

heden had already toonified the pack and converted the Unity scene, and the result uses only
engine-native components (`ModelRenderer` / `ModelCollider` / `CameraComponent` /
`DirectionalLight`), so it is a straight copy:

```
python tools/copy_fantasykingdom.py --check     # report what would be copied
python tools/copy_fantasykingdom.py
```

Copies 1091 FBX + 1091 vmdl into `Assets/FantasyKingdom`, 75 atlases into
`Assets/materials/FantasyKingdomTextures` (+ `materials/heden_flat_wheat.png`), the 15
`FantasyKingdom*.vmat` into `Assets/Shaders`, and the scene itself. Deliberately NOT copied:
`.vmdl_c` / `.vtex_c` / `.scene_c` (s&box recompiles them — the first open of either scene is
slow, once), and the `Sandbox.Clutter.ClutterGridSystem` entry, whose `$blob` lives in a
`.scene_d` sidecar we do not carry (it held an empty clutter grid).

Both shaders the pack needs (`heden_toon`, `heden_outline`) already existed here with an
identical feature set, and the one atlas both projects shared was byte-identical.

### Village night (2026-09-15)

The village is lit by its materials far more than by the sun: the 14 outdoor vmats
(`AdventureShader` + the 13 non-outline `FantasyKingdom*`) shipped with `g_flAmbientFloor 0.5-0.75`
(shader default 0.05, added to the shadow side unconditionally) and `g_flSunColorInfluence 0.4`, so
recolouring the DirectionalLight barely dims anything. The scene has no skybox, envmap or fog
component; the sky is the camera's clear colour, and the retro filter's depth fog blackens it.

```
python tools/village_night_vmats.py            # night: floor 0.12, sun influence 1.0, heden_ramp_night
python tools/village_night_vmats.py --revert   # daylight preset back (per-file floor values)
python tools/village_night_vmats.py --check    # report only
```

With sun influence at 1.0 the DirectionalLight's colour picker in the editor tunes the whole village
(moon blue ~0.45,0.55,0.85 at ~0.6). Rain / splashes / lightning / thunder are
`Code/Village/VillageWeather.cs` on the scene camera (RunManager creates one if the camera has none
and turns it on at home, off on a floor); convars `rain_density`, `rain_collide`, `rain_lightning`,
`village_lightning_now`. The clips are the user's: `Assets/Sounds/Rainloop.mp3` and
`Thunder01..04.mp3`, referenced by `rain.sound` / `thunder.sound` (2D). The drop sprite's texture is
`Assets/VFX/rain_streak.png` from `python tools/gen_rain_streak.py [--size 256 --line 4]` (square on
purpose, one thin vertical line; a drop is one world-vertical sprite plus a head dot, no trail renderer
and no engine collision — both allocate or trace per particle and were the stutter).

## Adventure — fresh toonify and import

```
python  tools/extract_adventure.py <work>                                # FBX + Demo.unity out of the archives
blender --background --factory-startup --python tools/toonify_adventure.py -- <src> <out>
python  tools/verify_adventure.py <src> <out>                            # writes _report.txt
copy    <out>/*.fbx  -> Assets/AdventurePack/
copy    PolyAdventureTexture_01.png -> Assets/Textures/AdventureTextures/polyadventuretexture_01.png
python  tools/fbx_scan_all.py Assets/AdventurePack scan.json
python  tools/gen_pack_assets.py adventure scan.json
python  tools/unity_scene_to_placements.py <extract> PolygonAdventure_Demo Assets/AdventurePack/adventure_demo_placements.json
python  tools/import_synty_scene.py Assets/AdventurePack/adventure_demo_placements.json \
        Assets/scenes/adventure_demo.scene --no-collider "(Flower|Grass|Reeds|Plant|Lillypad|Cloud|Mushroom)"
```

Pack facts (measured, not assumed):

* **The source is already cm.** `toonify_dungeon.py` scales its input x100 because the Dungeon
  pack's Synty source is metre-sized (`SM_Env_Wall_01` = 5.095 units). Adventure's is not
  (`SM_Bld_Fence_01` = 311 units = a 3.1 m fence), so `UNIT_MUL = 1.0`. Always check a known
  piece with `fbx_bounds.py` against `Assets/DungeonPack/SM_Env_Wall_01.fbx` (509 x 515) first.
* **No baked shell offset.** `OFFSET = 0.0`: the outline hull is an exact copy of the base
  carrying smooth normals, and `heden_outline.shader` does the inflation. (The older DungeonPack
  files were built with a 0.02 push baked in; that is redundant with the shader.)
* **One material for the whole pack.** All 267 static meshes are on
  `PolyAdventureMaterial_01 (PolyAdventureTexture_01)` — snow variants included, snow is in the
  atlas — so there is one `AdventureShader.vmat` + one `AdventureOutline.vmat`, on the outdoor
  daylight preset from `FantasyKingdomShader.vmat`, not DungeonPack's dark one.
* 7 of 268 files lose 1-4 degenerate polys through Blender's FBX round trip. The DungeonPack run
  did the same on 4 files; it is expected.

## `import_synty_scene.py` — the Unity → s&box conversion

A Python port of the maths in `heden/Editor/SyntySceneImport.cs` (that file is an editor window
tied to `Heden.Client` / `ScenePerfRules` / `InstancedProps`, none of which exist here). Basis:
Unity `+X → -Y`, `+Y → +Z`, `+Z → +X`, det = -1; `M_sbox = Cᵀ · M_unity · C`; translation
x 39.3701. Negative scale is stripped **before** the matrix is built — doing it after decomposition
bakes in a bogus 180° rotation and the piece faces the wrong way.

**Validated against heden's C# output**: re-running it on
`Assets/FantasyKingdom/Demo_Layout_Houses_With_Interiors_placements.json` reproduces
`fantasykingdom_houses.scene` with a max position delta of 0.005 in and **zero** rotation or
scale differences across 5284 compared objects. Rerun that comparison if the basis is ever touched.

Two gotchas worth keeping:

* Every `PrefabInstance` in these demo scenes is parented under a grouping GameObject, so
  `unity_scene_to_placements.py` has to compose transforms up the chain.
* A prefab's name is not reliably its mesh name (`SM_Env_Rock_10.prefab` holds
  `SM_Env_Rock_010`), so the mesh is resolved through the prefab's own `m_Mesh` guid.
  `FX_Fire_01` is a particle prefab with no mesh and is skipped — the one placement dropped
  out of 1293.

## Checking a scene

```
python tools/verify_scene_assets.py Assets/scenes/adventure_demo.scene Assets/scenes/fantasykingdom_houses.scene
```

Walks scene → vmdl → mesh file → vmat → shader → texture and reports anything dangling, then
prints world extents in inches and metres so a scale slip is visible without opening the editor.

# Quaternius monsters (Bat, Dragon, Frog, Rat, Snake, Snake_Angry, Spider, Wasp)

Eight small creatures out of two Quaternius packs on the desktop — `Monster Pack Animated by Quaternius`
(Bat, Dragon) and `Easy Animated Enemy Pack - Jan 2019` (the other six) — imported with their animations
and toonified. Assets only so far: no prefab, no animgraph, no brain, no `anim_graph_name` on the vmdls.

```
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup ^
    --python tools/prep_monster.py -- --kind all       # or --kind bat
python tools/gen_monster_assets.py                     # or --kind bat [--force-vmats]
```

`tools/monster_kinds.py` is the table (source .blend, armature and mesh object names, eye fix-up, size);
`prep_monster.py` writes `Assets/Characters/Monsters/<Title>/<kind>.fbx`, one clip FBX per action in
`Assets/Animations/Monsters/<Title>/`, and a manifest in `tools/out/monsters/<kind>.json`;
`gen_monster_assets.py` reads the manifest and writes the vmats and the vmdl. Resizing a monster is one
number in `monster_kinds.py` plus a rerun of the second script — no Blender.

| kind | bones | clips | vmdl scale | in-game size (wide, tall, long) |
|---|---|---|---|---|
| bat | 23 | attack, attack2, death, flying, hit | 0.06954 | 19.0, 28.4, 30.0 |
| dragon | 27 | attack, attack2, death, flying, hit | 0.12460 | 42.5, 48.0, 46.2 |
| frog | 28 | attack, death, idle, jump | 0.05141 | 18.0, 7.7, 16.5 |
| rat | 31 | attack, death, idle, jump, run, walk | 0.09461 | 14.1, 21.7, 66.0 |
| snake | 15 | attack, death, idle, jump, walk | 0.11199 | 11.8, 34.0, 32.3 |
| snake_angry | 15 | attack, death, idle, jump, walk | 0.11130 | 11.7, 34.0, 32.1 |
| spider | 39 | attack, death, idle, jump, walk | 0.10106 | 60.0, 19.7, 53.3 |
| wasp | 29 | attack, death, flying | 0.06878 | 24.9, 26.0, 16.4 |

**Scale comes from measuring the exported FBX** (`fbx_bounds.mesh_world_sizes`), never from Blender's
numbers. Two traps, and the first cost a 100x-too-big first pass: Blender's FBX export writes geometry in
METRES under a flat 100x node scale on every mesh node, and s&box applies that node scale (the same thing
the DungeonPack tiles hit from the other side), so a scale derived from the raw vertex numbers is 100x out;
and `object.dimensions` / `bound_box` in Blender is the EVALUATED box — the armature-posed one, the bat's
wings spread mid-flap — not the bind pose the FBX actually carries. The manifest keeps the Blender rest
numbers for reference only.

The third trap is node ROTATION, and it cost a round of wrong capsules and body heights. Exporting with
`bake_space_transform=False` leaves the geometry in Blender's Z-up orientation and puts the axis conversion
on the node as a -90 degrees X rotation, so the raw vertex extents are (width, DEPTH, HEIGHT) and reading
index 1 as the height gets a quadruped exactly wrong — the rat reported 66 units tall when 66 is its
nose-to-tail length. `mesh_world_sizes` rotates the bounding box now instead of only scaling it. The vmdl
scales were never affected (the largest extent is the same either way); the capsule, the agent height and
`BodyHeight` all were.

`idle`, `flying`, `walk` and `run` are marked looping; every rig keeps all its bones (`do_not_discard`),
including the pole targets, and each monster's own root bone (`Root`, or `root` on the frog and the rat)
is the vmdl's `default_root_bone_name`.

Things these packs need fixing on the way in, all of them handled by `prep_monster.py`:

- **NO CLIP HAS ROOT MOTION.** Not one action in either pack keys the root bone's location, so every clip
  animates in place and the vmdls carry no `ExtractMotion` — the code has to move the body, the way
  `SkeletonNpc`/`AttackSpec.LungeSpeed` already does for the Tormented Soul. Jump and walk included.
- The Bat's and the Dragon's **eyes ship on their own one-bone armature that no clip animates**: left alone
  they hang in the air while the body flies. They are joined into the body mesh and skinned 100 % to the
  `Face` bone (`eyes` in `monster_kinds.py` names it), and the eye armature is deleted.
- The Easy pack meshes carry **no UV layer at all**; an empty one is added (nothing to unwrap — the flat
  colours ride a white texture).
- Every material carries a **stale, disconnected `Diffuse BSDF` alongside the live Principled one, holding a
  different colour** (the bat's belly is 0.80/0.53/0.09 live, 0.74/0.63/0.32 stale). `base_color` follows the
  surface link out of the ACTIVE Material Output; `node_tree.get_output_node("ALL")` is worse than useless
  here — on the bat it returns the stale output, on the frog it returns nothing.
- Several .blends were **saved with their armature in pose mode**, and the FBX exporter puts it back there
  after every export, so `select_all` / `duplicate` / `join` fail their poll on the next call. `mode` is per
  object, so the guard has to sweep `bpy.data.objects` (`view_layer.objects` hands out `None` entries right
  after a join or a remove).
- The Rat's armature carries a **0.395 object scale**; nothing applies it, the vmdl scale is derived from the
  measured rest bounds either way.

Toonify: the outline shell is a SEPARATE object (`<Title>_Outline`) rather than duplicated faces in the body
mesh the way the Synty characters do it. These meshes are flat shaded and `heden_outline.shader` inflates
along the vertex normal, so a shell with the pack's hard normals splits open at every edge; the duplicate is
shade-smoothed (sharp faces, sharp edges and custom split normals all cleared — check the FBX: the body's
normals are `ByPolygonVertex`, the shell's `ByVertice`) and keeps the body's vertex groups and armature
modifier. One `RenderMeshFile` node holds both objects.

Colour: the packs have no textures, only a flat base colour per material slot. Each slot gets its own
`Assets/Shaders/Monster<Kind><Slot>.vmat` — `heden_toon.shader` over `Assets/Textures/Monsters/flat_white.png`
with the pack's colour in `g_vTextureTint`. The albedo sampler reads sRGB, so a white texel is 1.0 linear and
the tint IS the albedo, exactly the LINEAR value Blender had. The bat and the dragon really are near-black
that way (Quaternius authored their body at 0.02-0.05); brighten the tint in the material editor, not in the
generator. `snake_angry` shares `snake`'s seven vmats (same slots, same colours). `gen_monster_assets.py`
never overwrites a vmat that exists unless `--force-vmats`, so hand tuning survives; vmdls are always rewritten.

## The monsters in the game (Code/Npc, prefabs, brains)

They are not a new NPC system: every one of them is `SkeletonNpc` + `SkeletonBody` + `NavMeshAgent` +
`CapsuleCollider` with its own vmdl, animgraph, ragdoll and brain — the Tormented Soul's shape exactly, which
is why a flier works at all (hover on a child object, clips playing in place, the code moving the body).

```
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup ^
    --python tools/measure_monster_clips.py -- --kind all      # hit times, reaches, stance speeds
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup ^
    --python tools/gen_monster_ragdoll.py -- --kind all        # capsules from the skin weights
python tools/gen_monster_animgraph.py                          # Assets/animgraphs/monster_*.vanmgrph
python tools/gen_monster_assets.py                             # vmdls (now with graph + ragdoll)
python tools/gen_monster_prefabs.py                            # prefabs, brains, MonsterKinds.Generated.cs
python tools/verify_monsters.py                                # walks every reference; run after any of the above
```

The user's brief (2026-09-09): simple, low health, low damage, arriving in small groups; the dragon comes
SOLO and is somewhat stronger but still under a skeleton slave; **no monster plays its death clip, they all
ragdoll**.

| kind | role | health | damage | speed walk/run | hover | attacks | staggers |
|---|---|---|---|---|---|---|---|
| bat | hostile | 20 | 5 | 90 / 150 | 42 | attack, attack2 | yes |
| dragon | hostile | 40 | 9 | 100 / 180 | 54 | attack, attack2 | yes |
| frog | **critter** | 12 | – | hops 25 u | – | none | no |
| rat | hostile | 20 | 5 | 15 / 84 | – | attack | no |
| snake / snake_angry | hostile | 20 | 5 | 70 / 70 | – | attack | no |
| spider | hostile | 20 | 5 | 61 / 61 | – | attack | no |
| wasp | hostile | 20 | 5 | 90 / 160 | 40 | attack | no |

The rat is 3x and the spider 2x the size they imported at (the user's call, 2026-09-10). Their `loco.playback`
came DOWN by the same factor so both keep the speed they had: speed is `stance-foot speed x units_per_bu x
playback` and `units_per_bu` scales with `size`, so untouched playback would have had the giant rat running at
253 u/s, faster than a skeleton.

Everything in that table lives in `tools/monster_kinds.py`; nothing is typed into a prefab by hand.

**The frog is not an enemy.** `SkeletonNpc.Passive` makes it never acquire a target, so it never approaches,
engages or attacks — it hops around its spawn on the brain's `Wander` and can be killed like anything else.
It keeps the `enemy,npc` tags on purpose: every damage path in the game (the sword arc, spell areas, hazards)
finds it by those tags, and one flag on the component is a far smaller blast radius than a new tag everywhere.
Hostile skeletons ignore it for free, because they hunt players and raised allies and it is neither.

**Only the bat and the dragon have a hit clip.** `SkeletonNpc.CanStagger` is off for the other six: a hit
still lands, still flashes, still shoves, but never enters the Hurt state — which would otherwise freeze the
monster for `HurtSeconds` with no animation playing. It routes through the same `FlinchCount` path that hyper
armor and poise already used.

**`BodyHeight` and `EyeHeight` are now per-NPC properties** (they were a hardcoded 70 and 60). Spell homing,
area damage and hazard containment all take `BodyHeight * 0.55` as a body's chest, and a bolt homing 38 units
over a rat's head misses it entirely. A flier's includes its hover, because its root is on the floor and its
body is not.

Speeds are NOT free, exactly as for the skeletons: the clips animate in place, so a body may only travel as
fast as the clip's own stance feet, times the animgraph's `loco.playback`. `measure_monster_clips.py` reads
that per clip; `gen_monster_prefabs.py` derives WalkSpeed/RunSpeed from it and prints which is which. The knob
for "this thing is too slow" is `loco.playback` in `monster_kinds.py` (rat 2.5, spider 3.0), not the speed.
The footless kinds have no contact to betray a slide and carry `speed` outright instead.

Attack numbers are measured, never authored. **The hit is MAXIMUM EXTENSION, not peak strike speed** — every
one of these is a bite or a sting, not a cut, and measured the other way the spider's "hit" lands at 0.03 bu
of reach on the way out, half a clip before its jaws are anywhere. `HitRange` is the measured reach plus 28
units with a floor of 38, because at this scale the reach barely matters: a rat's head leads its own root by
6 units while the two capsules hold it 28 from the player, so the gap decides whether a bite can ever connect.

## Ragdolls (the corpses came apart; fixed 2026-09-10)

They ragdoll rather than playing a death clip, and the first attempt tore itself to pieces — the user:
*"their models get stretched because it splits in parts and the mesh gets stretched and other part rolls
away."* Three separate defects, all of them now covered by `tools/verify_monsters.py`:

1. **Bodies with no joint.** `SkeletonCorpse.BuildJoints` walks up the bone hierarchy for an ancestor that
   owns a body and, finding none, used to `continue` — leaving an unconstrained rigidbody that simply rolls
   off. That is correct for exactly ONE body (the ragdoll's root) and catastrophic for any other. The
   generator only closed its body set over *skinned* ancestors, so a single unskinned control bone in the
   middle of a rig broke the chain: measured, the spider had **10** free bodies, the frog 6, the rat 5, the
   snake 3. The generator now closes over the FULL hierarchy, adding unskinned `connector` bones.
2. **IK effectors can never be reached through the hierarchy.** The frog's and rat's feet and the spider's
   are parented straight to the armature root, hanging off nothing. Handled in DATA, never at runtime: a
   root-parented bone with no skinned descendants gets **no body** and a `weld` override naming the nearest
   limb bone in the rest pose (`FrontFoot_L:FrontLowLeg_L`), which becomes `SkeletonBody.CorpseWelds` on the
   prefab and is what `MonsterCorpse` drives it from. A body there would joint to the torso and the foot
   would swing from the belly. The root itself gets a body only when more than one chain still hangs off it,
   so every ragdoll is one tree with one free root (`verify_monsters.py`: `MAX_UNPARENTED = 1`).
3. **Capsules anchored to the skin instead of the bone.** `SkeletonCorpse.BuildJoints` anchors every ball
   joint at `Vector3.Zero` in the child bone's frame — the bone ORIGIN — so a capsule fitted to wherever the
   skin happens to sit (the snake's head starts 47 units down its own bone) is a joint pulling on a point
   OUTSIDE the body it drives, and the solver answers that lever arm with torque. 9 of the snake's 13 capsules
   did not contain their own origin. The capsule now uses the skeleton generator's formula verbatim —
   `p0 = axis·min(0.7r, 0.2L)`, `p1 = axis·max(L−0.7r, 0.6L)` off the BONE length — and the skin decides
   the RADIUS only (median perpendicular distance, capped at `MAX_RADIUS_OF_LENGTH` and `MAX_RADIUS_OF_MODEL`).
   That also makes a sphere impossible by construction (segment ≥ 0.4L). The min-radius floor was also dead
   code — computed from `mesh.dimensions` (Blender world units) and compared against armature-space radii,
   100x apart.
4. **A dot in a bone name is a body that never exists.** The one that cost the most time, found only by
   reading `sbox-dev.log`. Blender names mirrored bones `FrontLeg.L`; s&box's compiled skeleton spells that
   `FrontLeg_L` (read straight out of `spider.vmdl_c`), and `ModelPhysics.CreateParts` does
   `bones.GetBone(part.BoneName)` and `continue`s on null — no warning, no error. Counting the capsules whose
   name has no dot predicted the engine's body count exactly: spider **4 of 16** (all eight legs missing),
   frog 4, bat 5, wasp 8, rat 9, dragon 5; the snake alone has no dots in its rig. Every bone name the
   generator writes for the engine goes through `bone_id()` now (`parent_bone`, the welds), the verifier
   rejects any name outside `[A-Za-z0-9_]`, and `MonsterCorpse` warns whenever `Model.Physics.Parts.Count`
   differs from the bodies the engine actually built — that one line is the whole diagnosis.
5. **A connector must be as substantial as what it carries.** A connector (a skinless bone kept only to hold
   the tree together) got `min_radius`; mass comes from capsule volume, so it became a near-weightless rod
   the whole creature hung off. The spider's `Body` was a 9.5-unit rod of mass 17 with ten bodies and 1098 of
   mass on it (65x); the snake's `Body` carried its chest at 34x and `Root` the tail at 25x. Torque at a joint
   scales with the lever arm, so load-ratio x connector length is the metric — 615 and 407 on the two that
   exploded against ≤ 72 on the six that did not. A connector is now sized to the fattest body it links; it
   is physics-only geometry and ragdoll-vs-ragdoll is `Ignore`, so that costs nothing. All eight sit at 1-13
   and the verifier caps the metric at 100.

The budget is **16 bodies**, the skeleton's own number, and it means the same thing: one body per part you
want to FLEX at. Bodies go on SEGMENTS, scored by the skin in a bone's subtree over the bodies its chain
costs, which marches down the spine and then out along every limb; every other bone is driven to ride its
parent rigidly by `MonsterCorpse`, exactly as a skeleton's fingers ride its hand. Two orderings tried before
that both read badly: dropping the smallest leaf until the budget fits ate both of the frog's back legs, and
dropping the deepest leaf kept the root-parented IK effectors while throwing away the bat's wings and the
frog's head. Fattest-bone-first was wrong too — it spent the rat's budget on tail segments before its front
legs ever got one.

`skeleton_corpse_debug 1` prints `bodies=` and `joints=` per corpse; they should differ by exactly 1.
`monster_corpse_debug 1` prints `parts` / `bodies` / `welded` and names any bone that is unreachable — parts
and bodies must be equal, or that monster is running on a fraction of its ragdoll.

In the dungeon (`DungeonBuilder`): `MonsterGroupChance` (0.4) is the chance a room's encounter is monsters
instead of skeletons, and a monster room holds ONE species in its own group size, so you meet a nest rather
than a zoo. `CritterChance` (0.2) adds frogs independently — a room can hold both. Corridors and ambush rooms
stay skeletons. `monster_spawn <kind> [count]` drops a group in front of you; `skeleton_spawn bat` works too,
and every `skeleton_*` console command covers them (they are `SkeletonNpc`s).

## The frog hops (fixed 2026-09-10)

It used to travel at a flat 45 u/s with its jump clip looping in the locomotion blend, which reads as a frog
skating along with an animation playing over the top — the user: *"he has constant forward movement but his
hop is not constant movement."* A blend2d item loops on its own clock and can never be phase-aligned to the
travel; only a state entry resets a clip to frame 0. So a hop is a ONE-SHOT, exactly like an attack:

* `SkeletonState.Hop` (appended last — the value goes over the wire), counted by the synced `HopCount`, which
  `SkeletonBody` turns into the graph's `hop_trigger` pulse the same way it does an attack.
* Travel is `SkeletonNpc.TickHopTravel`: `MoveBodyBy(forward * HopSpeed * dt)` and ONLY between `HopLiftTime`
  and `HopLandTime` — the clip's own measured airborne window (`air_start` / `air_end` from
  `measure_monster_clips.py`; the frog's feet leave the ground at 0.375 s and land at 0.625 s of a 0.875 s
  clip, so it crouches still, travels 25 units, and settles still). It must NOT be built by pulsing `MoveTo`
  or `_agent.MaxSpeed`: each of those throws the crowd path away, the documented cause of the old skeleton
  jitter.
* `hop=dict(clip=..., distance=..., rest=...)` in `monster_kinds.py` is the whole tuning surface; `HopSpeed`
  is derived as `distance / window`.
* `SkeletonNpc.AtRest` (Idle **or** Hop) replaces the bare `State == Idle` tests in `SkeletonSystem`
  (a hopper would otherwise never qualify to go dormant) and `DungeonAmbience` (which treats any non-idle NPC
  within 600 u as a fight — a frog hopping past would have silenced the dungeon).

## Traps ignore them (2026-09-10)

`SkeletonNpc.TriggersTraps` is false on every monster, the dragon included: they are small animals and a rat
should not drop a portcullis. The exclusion sits in `TrapSystem.Occupied`, the single choke point where a body
can ARM a trap — so a monster caught in a trap someone else set off still takes the hit, which is the
difference between "too light to trigger it" and "immune to spikes". Cycling traps (blade, saw) never consult
it and still cut anything that walks through.

The same edit fixed something independent: every NPC used to be measured as `TrapBase.SkeletonHeight` = 70
units tall whatever it was, so a 22-unit rat counted as reaching into the flame head's jet band (46-78 above
the floor) that it should walk straight under. All five NPC body tests read each body's own `BodyHeight` now.
Skeletons are unaffected, theirs being 70.

## Round 3 (2026-09-10): silent attacks, unreachable bites, the wasp's yaw

**Monsters were damaging the player with no animation.** `SkeletonNpc.Decide` picks between `Kind.SwordA` and
`Kind.SwordB` on a flat 50/50 with no check that a second clip exists, while this generator only emitted
states for `attack_kind` 1..len(attacks). The five one-attack monsters therefore had **no state for kind 2**,
so half of every attack they made changed no state at all — and damage is dealt on a wall clock
(`TickAttack` -> `LandHit` at `HitTime`) that never consults the graph, with the attack *sound* still playing
off the same counter. Fixed on both sides: `attack_clips()` builds a map TOTAL over kinds 1-4 (repeating the
first clip, the way `gen_soul_animgraph.py` always has), and `SkeletonNpc.Swing()` never rolls SwordB when it
is the same animation as SwordA. `verify_monsters.py` fails if any kind lacks a state.

**The spider could not attack at all, and bulldozed instead.** Every distance in `SkeletonNpc` is centre to
centre and nothing subtracts a radius, so the closest a monster can get is its own COLLIDER radius plus the
player's half-width (11.32: a capsule of r=11.31 over a 16x16 box). The spider's capsule radius alone was 30
against a `HitRange` floored at 38, so `distance <= StartRange` could never be true: it ran at a point inside
the player forever, and since its collider is a static body with no Rigidbody, PhysX resolved the overlap by
moving the only movable thing — you. The old constant budgeted the **NavMeshAgent** radius (always 16) rather
than the CapsuleCollider radius (up to 40); they are different numbers. `HitRange` and the brain's
`PreferredRange` are both derived from the real floor now, and `gen_monster_prefabs.py` prints the margin —
which must be positive, or that monster cannot fight. `PreferredRange` matters as much as `HitRange`:
`_holdingGround`, the only thing that calls `Stop()` in the engage loop, latches on `d <= PreferredRange`.

Colliders are oversized by `hitbox` (1.35, the frog exempt) — the small ones were fiddly to hit.

**The wasp flew sideways.** Its `Head` sits at +X where every other rig in both packs has it at -Y. `yaw` in
monster_kinds.py turns the armature object at export in `prep_monster.py` — deliberately as an OBJECT
rotation, not applied, so the bones' local matrices are untouched and every clip is unaffected while the FBX
carries the turn on both nodes.

### Ragdolls: what was actually wrong (2026-09-10)

**Do not put monster-specific code in `SkeletonCorpse`.** It is shared by every corpse in the game. A pass
added there for the monsters — re-stamping a bone transform each frame for bones with no physics body —
broke the SKELETONS, which were fine and were not the brief. The bug: it captured the corpse's transform once
at spawn, but `ModelPhysics` runs with `IgnoreRoot` false, so the corpse GameObject's transform is rewritten
from the root body every tick. Every stamped bone was therefore placed in a stale space and smeared away as
the body travelled — on a skeleton that is 28 bones including both clavicles, the neck and the weapon and
shield sockets. All of it has been removed; `SkeletonCorpse` is back to what it was.

**Why a skeleton ragdolls well and an auto-generated animal did not**, now that the answer is known:

1. *Its joint graph is its bone graph.* Every skeleton body descends from the pelvis. These packs park their
   IK effectors — the rat's and spider's feet, the snake's mouth — on the ARMATURE ROOT, hanging off nothing.
   `gen_monster_ragdoll.py` fixes that in DATA, not at runtime: a root-parented bone with no skinned children
   never gets a body, and if several chains still hang off the root then the root itself gets one. Every
   monster is now a single tree with one free root, and `verify_monsters.py` enforces it (`MAX_UNPARENTED = 1`).
2. *Its capsules are capsules, and they contain their own bone origin.* The first fit pulled both ends in by
   the radius, which collapses the segment to a point whenever a bone's vertices spread less than two radii —
   every creature's torso and head came out a SPHERE, and a sphere resists no rotation about any axis. The
   second fit spanned the skin's extent, which floated the capsule away from the bone the joint anchors on
   (item 3 above). The bone-anchored formula fixes both at once: the segment is ≥ 0.4 of the bone length, so
   it cannot be a sphere, and it starts within 0.7r of the origin, so the joint is always inside the body.
   The generator ABORTS if either ever fails.

Measured against the hand-authored skeleton, which is the control:

| | bodies | spheres | anchors outside the body | max radius/length | fattest capsule |
|---|---|---|---|---|---|
| skeleton (hand-authored) | 16 | 0 | 0 | 2.65 | 7 % of the body |
| every monster, now | 13-16 | 0 | 0 | ≤ 1.11 | 8 % of the body |

**What a bone without a body actually does** (decompiled, settled, do not re-derive).
`SetBoneTransform` -> `SceneModel.SetBoneOverride` -> `animNative.SetPhysicsBone`, taking a transform LOCAL
TO THE SCENE OBJECT. `ModelPhysics.OnUpdate` -> `PositionRendererBonesFromPhysics` sets overrides for the
bones that own a body and for no others, and never clears one during update (`ClearPhysicsBones` runs only
in `DestroyPhysics`), while re-driving the corpse GameObject from the FIRST body every tick. `SeedPose`
stamps an override on EVERY bone once at death. Net: a body-less bone keeps its death pose relative to the
corpse root forever. **It does not follow its parent.**

A skeleton survives that because Synty weights the whole hand to `hand_l`: its 30 body-less bones (fingers,
twists, clavicles, sockets) carry almost no skin, so freezing them is invisible. These packs are the opposite
— 48 % of the rat's vertices sat on body-less bones, 21 % of the spider's and bat's — and that frozen skin was
the corpse tearing apart. Measure body-less SKIN, never body count.

**`Code/Npc/MonsterCorpse.cs`** is the fix, and it is monster-only by construction: `SkeletonCorpse.Spawn`
takes an optional `welds` that defaults to null, `SkeletonBody.WeldCorpseBones` is false on every skeleton
prefab, and the component is only ever created when the dying body passed a weld map. Each frame in
`OnPreRender` (which runs after every `OnUpdate`, so after `ModelPhysics` has written its bodies) it walks the
body-less bones drivers-first and places each at its driver's world transform composed with its bind-pose
offset — the driver being its parent, or the weld target for a bone the rig parented to the armature root.
**It re-reads `renderer.WorldTransform` every frame.** Caching it at spawn is precisely what smeared the
skeletons when this logic briefly lived in `SkeletonCorpse`, because `ModelPhysics` rewrites that transform
from the root body every tick. `_world` is seeded with each bone's bind transform rather than
`default(Transform)` (zero scale, invalid quaternion), so nothing non-finite can reach `SetBoneTransform`.

`verify_monsters.py` now hard-fails on: a capsule that does not contain its bone origin, a sphere, a bone
name outside `[A-Za-z0-9_]`, a connector load metric over 100, more than one free root, and more than 3 % of
the skin on bones nothing drives.

Still missing, deliberately: **no sounds**. Neither pack ships any, and the skeleton's bone-break death on a
frog would be worse than silence, so `AttackSound` / `DeathSound` / `BlockSound` are empty on every monster.

# PSX Horror-Fantasy Megapack monsters (toonified 2026-09-14, assets only)

23 low-poly horror monsters (`C:\Users\skipp\Desktop\Speelboord Studios\PSX Horror-Fantasy Megapack\PSX Horror-Fantasy
Megapack\<folder>`: abomination, abomination2, alieninvader, anomaly, bigabomination, bigfoot, blackbutcher, bloodwraith,
clown1-3, devildemon, doll1-2, elkdemon, eyehead, greencyclope, greengoliath, killerpig, mrz, muscularabomination,
plaguedoctor, werewolf) imported on the user's "just import and toonify them, animations and purpose later". Output:
`Assets/Characters/PsxHorror/<name>.fbx + .vmdl`, `Assets/Textures/PsxHorrorTextures/<name>.png`, `Assets/Shaders/Psx<Name>.vmat`
+ `PsxOutline.vmat`. No prefab, brain, graph, clips or ragdoll; nothing seen in-engine yet.

    blender -b --factory-startup --python tools/toonify_psx.py -- [--kind werewolf]   (-> the FBX + tools/out/psxhorror/chars.json)
    python tools/gen_psx_assets.py [--kind werewolf] [--force-vmats] [--force-textures]

* **The pack has no real-world scale**: every FBX is normalised to ~100 cm along its longest axis (the abomination 175), so
  `tools/psx_kinds.py` carries a `size` per monster (largest extent in game units, the Quaternius rule): humanoids 72,
  the obviously big ones 88, the dolls 48. `gen_psx_assets.py` measures the exported FBX (`fbx_bounds.mesh_world_sizes`)
  and writes `size / extent` as the vmdl's ScaleAndMirror through `gen_synty_chars.write_vmdl` (which now honours a
  per-entry `scale`; PACKS entry `psxhorror`). Retune = edit the number, rerun gen (no Blender).
* **Textures come from the GLBs**, not the loose PNGs: each folder's Godot `.glb` embeds the texture the model was
  exported with, and five folders' PNGs differ from it (eyehead 256 vs the GLB's 512, the muscular abomination's three
  candidate PNGs vs a 1024, three byte-different re-saves). One material per model.
* **Three rig flavours, kept as they are**: ten on AccuRig's UE export (Epic names `root / pelvis / spine_01 / thigh_l...`
  + a `CC_Base_Pelvis` extra, 71-83 bones -- the skeleton donor's names, so those may take a rename-retarget later), ten
  on the Reallusion CC rig (`RL_BoneRoot / CC_Base_*`, 105-119 bones incl. `_end` leaves), three on Mixamo (elkdemon,
  greengoliath, plaguedoctor: `mixamorig:` stripped, a `root` bone added at the origin since Hips was the only top bone;
  a colon or dot in a bone name is what s&box silently mangles). The FBX/GLB animations are T-pose stacks only.
* Toonify = the monster convention: custom normals kept on the body (the flat PSX shading IS the look), the outline hull
  a SEPARATE smooth-shaded `<Mesh>_Outline` object with position-keyed vertex normals (a hull with hard normals splits
  at every edge), same vertex groups + armature modifier; export = prep_skeleton_npc.py's rigged settings.
* **Thirteen rigs were wrong as shipped** (found 2026-09-15 when the elkdemon got animations; the first export of
  2026-09-14 carried both faults). Two different faults:
  * The ten AccuRig UE exports (abomination, bloodwraith, clowns, dolls, eyehead, greencyclope, mrz) have a **bind pose
    Blender reads lying down** (pelvis at y = -0.5, z = 0: the cluster matrices missed the Y-up conversion) while the
    T-pose stack's node transforms and the vertices stand upright; rest -> stack pose is a rigid -90 deg X turn with a
    ~1 cm residual. Skinned as shipped, the stack pose deformed clown1 into a 1.7 x 1.0 x 0.7 m smear (measured). Fix
    (`toonify_psx.bind_pose_fix`): fit the rigid transform rest -> stack pose over every bone head (Kabsch) and move the
    REST onto it, bones only. A rig whose stack is a different pose (Mixamo's T-pose stack over an A-pose bind, residual
    far above 3 cm) or already matches (the CC ten) is left alone.
  * The three Mixamo exports (elkdemon, greengoliath, plaguedoctor) **stand facing +X with their origin at mid-height**
    (feet at z = -0.49 m); every Synty character, and s&box's forward, is -Y in Blender with the feet on z = 0.
    `orient_and_ground` yaws any rig whose feet (ankle -> toe) do not point -Y and drops it onto z = 0, bones AND
    vertices.
  Both fixes are written into the data (`transform_data`: bone rests, vertices, custom normals) with the object
  transforms left as imported: applying the object transforms instead made Blender's exporter write each mesh node with
  its own -90/x100 conversion under the armature's, a layout the importer reads lying down. The pose is reset to the
  rest before the export (`reset_pose`: the exporter writes the CURRENT pose as the bone nodes, and the stale stack pose
  gave the first elkdemon a 173x mesh node). The run refuses to export a monster that still faces elsewhere, floats, or
  whose pose differs from its rest; the manifest keeps `forward`, `turned_deg`, `raised_m`, `bind_pose`. Verified by
  front/side renders of the exports (Blender workbench) and a re-import probe (pose == rest, evaluated == rest bounds).

## The elkdemon's idles (2026-09-15)

The user's "put these animations on elkdemon, I will choose which ones I want to use": the Synty **ANIMATION Idles**
pack's `Polygon/Masculine` folder (165 clips: stances with enter/loop/exit, actions like yawn, stretch, check watch,
use phone, look around, weight shifts) retargeted onto the elkdemon's own Mixamo rig and listed in its vmdl. No graph,
no prefab, no purpose yet.

    blender -b --factory-startup --python tools/retarget_synty_to_rig.py -- --rig Assets/Characters/PsxHorror/elkdemon.fbx ^
        --names mixamo --out Assets/Animations/PsxHorror/Elkdemon --strip-prefix A_POLY_IDL_ --strip-suffix _Masc [--report] ^
        "C:/Users/skipp/Desktop/Speelboord Studios/ANIMATION_Idles_SourceFiles_v3/SourceFiles/Animations/Polygon/Masculine/**/*.fbx"
    python tools/gen_psx_assets.py --kind elkdemon        (psx_kinds `clips=True` -> every FBX in that folder is an AnimFile)

`tools/retarget_synty_to_rig.py` is retarget_synty.py for a target that is NOT the donor: another skeleton, another
rest pose, other bone names. It renames the target to Epic names for the run (`--names mixamo` table; back before the
export), aims the monster into the animation rig's T-pose first (retarget_fpp.prepose, hand aimed at the index when
there is no middle finger), applies the clip's first-frame delta from the T-pose on top (retarget_synty's exact rule),
binds through the helper bones, then rewrites the pelvis track after the bake as `own rest pelvis + k * (source pelvis -
T-pose pelvis)` with k = the rests' pelvis-height ratio (0.666 for the elk), root travel scaled by the same k. It refuses
a rig whose feet do not point -Y. `--report`: limb directions match the source to 0.0 deg on every sampled frame (the
spine line spans three bones of other proportions, 1.2 deg), pelvis at 0.58 m for the source's 0.87. Clip names:
`base`, `<stance>_enter/_loop/_exit`, `<action>`; looping = `psx_kinds.LOOPING` (`_loop` + `base`). Nothing seen in the
engine; the elk's rest hangs its arms, so the T-pose aim decides how its shoulders read in every clip.

# Fantasy Rivals, Dark Fantasy and Dark Fortress (Synty packs, toonified 2026-09-11)

Three more Synty packs, imported the Realms way but with their characters as well. POLYGON **Fantasy Rivals**
is a character pack (20 hero/villain characters, their 22 weapons, 18 small props); POLYGON **Dark Fantasy**
is a gothic village + crypt kit (613 static meshes: buildings, props, weapons, character attachments) with 15
characters; POLYGON **Dark Fortress** is the castle kit (819 static meshes: curtain walls, 45-degree pieces,
towers, dungeons, siege gear) with 13 characters. All three are assets only: no prefab, no brain, no animgraph, no clips — models to look at and pick from.

```
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup ^
    --python tools/toonify_synty.py -- rivals <Source_Files> <work>/rivals_static <work>/rivals_chars all
python tools/verify_synty.py <Source_Files>/FBX <work>/rivals_static <work>/rivals_chars ^
    --src-chars <Source_Files>/Characters/Unreal_Characters --src-chars <Source_Files>/Characters
python tools/rivals_textures.py <Source_Files>          # -> Assets/Textures/FantasyRivalsTextures (PNG, lowercase)
copy   <work>/rivals_static/*.fbx -> Assets/FantasyRivals/
copy   <work>/rivals_chars/*.fbx  -> Assets/Characters/FantasyRivals/ ; _chars.json -> tools/out/rivals/chars.json
python tools/fbx_scan_all.py Assets/FantasyRivals scan.json
python tools/gen_pack_assets.py rivals scan.json        # static vmdls + FantasyRivalsShader/Outline + 15 palette twins
python tools/gen_synty_chars.py --pack rivals           # character vmdls from the manifest
```

Dark Fantasy is the same run with `darkfantasy`, `tools/darkfantasy_textures.py`, `Assets/DarkFantasy`,
`Assets/Characters/DarkFantasy`, `tools/out/darkfantasy/chars.json`, and `verify_synty.py` with
`--pack darkfantasy` (which sets its material families, static skip list and character source folders).
Its static list is every FBX under `FBX/` except the `Characters` subtree, plus the static
`Characters/Attachments/SM_Chr_Attach_*` pieces.

Dark Fortress likewise with `darkfortress` / `tools/darkfortress_textures.py` / `Assets/DarkFortress` /
`Assets/Characters/DarkFortress` / `tools/out/darkfortress/chars.json`.

`toonify_synty.py` is `toonify_realms.py` plus a rigged path, driven by a `PACKS` table (source folders,
MaterialList name, slot -> vmat family map, smooth-normal regex, `no_outline` slots, output naming,
`split_chars`). One tool for all three because the packs share everything that matters:

* **Both sources are already cm** (`SM_Base_Dungeon_01` 497 x 496 = a 5 m slab, `SM_Prop_Barrel_01` 96 tall,
  `SK_Character_Mystic_01` 185 tall), `UNIT_MUL = 1.0`. Static vmdls `import_scale 0.3937`; characters keep
  `import_scale 1.0` and get `ModelModifier_ScaleAndMirror 0.3937` (the skeleton NPC convention for a rigged
  FBX). The only non-unit node scale in either pack is Dark Fantasy's `INVERTED_NORMAL_SPHERE` skybox at 390x,
  which is skipped.
* **The FBX material names are Maya junk** (`lambert38`, `Gothic`, `MAT_01A8`, `_Ghost2`), so every slot is
  looked up in the pack's `MaterialList_*.txt` by mesh name **and slot index** — 86 Dark Fantasy building
  pieces carry two to four slots (atlas + a tiling brick/roof/stucco/window texture) and the list writes them in
  order. Unknown slots fall back to the pack's atlas material.
* **The characters come from `Unreal_Characters`**, the Epic-named rig every other character here uses. Synty
  exported its `root` as an FBX *Root*-type node (the Dungeon skeletons have a LimbNode), which Blender turns
  into the armature OBJECT, so the imported rig had no root bone and three parentless bones (pelvis,
  ik_foot_root, ik_hand_root); `ensure_root_bone` puts an identity `root` back and parents them under it.
  `verify_synty.py` fails a character without one.
* **Rig facts (measured with `fbx_bounds`, 2026-09-11):** Fantasy Rivals and Dark Fantasy share ONE rig —
  0.00 cm / 0.00 deg over all 50 common bones between `SK_Chr_Skeleton_01` and `SK_Character_Mystic_01`, so
  clips retargeted for one play on the other. The big ones (Rivals' `BR_` characters, Dark Fantasy's
  `Demon_01` / `Gargoyle_01`) keep the names but have their own limb lengths (arms 5-12 cm longer, a `Belly_01`
  bone). CORRECTED 2026-09-12 (a Blender probe of both FBX in one scene, `matrix_world @ head_local`): the
  Dark Fantasy skeleton rig's joints sit at **0.000 cm** from the Dungeon skeleton donor's on every shared bone
  (the donor's splayed left thumb aside, 2.6 / 4.4 cm), with rest orientations 1-25 deg off — the earlier
  "pelvis 180 deg off, limbs 1-5 cm" line came from a raw node parse and was wrong. So a bone RENAME still
  binds wrong, but the `prep_skeleton_npc.py` re-skin (`bone_map="epic"`) carries the Sword & Shield clips with
  no retarget; see "Corpse, Dark Fantasy skeletons, armor tiers, blood". The big characters keep their own
  limb lengths and would still need `retarget_fpp.py`.
* **Outline shells on skinned meshes are separate objects** (`<mesh>_Outline`, the monster convention), a
  vertex-exact copy carrying the body's vertex groups and armature modifier so it deforms with it; no baked
  offset. Faces on a `no_outline` slot are cut out of the shell (Dark Fantasy's alpha-cut
  vine/moss/grunge/divider decals and window panes): an inflated hull behind a flat card is a black
  card. A mesh whose every face is such a card gets no shell at all.
* **Scale is verified off the exported FBX** with `fbx_bounds.mesh_world_sizes` (node scale and rotation
  applied) against the source mesh, per mesh, within 0.5 %: the rigged export writes metres under a 100x node
  scale, so raw vertex extents mean nothing there.

Materials, Fantasy Rivals: one atlas (`FantasyRivals_Texture_01_A`) for everything, shipped in 16 palettes
(4 textures x A-D, one UV layout) -> `FantasyRivalsShader` + 15 `FantasyRivalsShader02B`-style twins for the
runtime swap the mossy floors use. The SpiritDemon's second slot (`_Ghost2`) and the Mystic's four spectral
arms (`SK_Arms_Lower/Upper_01`, their own little rig) are Synty's translucent custom shader over
`Mystic_Arms.tga`, a four-column colour gradient whose alpha fades out -> hand-kept `FantasyRivalsGhost.vmat`
(heden_foliage cutout over it, a dissolve edge rather than a soft fade; a real soft-alpha toon material is a
shader job for when they are in the game). Emissive masks are copied but nothing reads them (heden_toon has no
emissive input). `FX_Runes.fbx` (a flat rune decal for Synty's rune shader) is skipped; `Medusa_Snakes.fbx` and
`Mystic_Arms.fbx` are duplicates of the `SK_` files and skipped; `SK_MedusaSnakes_01` (Medusa's hair, 28 snake
bones) is imported as its own rigged model.

Materials, Dark Fantasy: the atlas (`PolygonDarkFantasy_Mat_01_A`, 667 slots; `_01_B` / `_02_B` are palette
twins used by a handful of candelabras, mapped to the same base material) -> `DarkFantasyShader` + 11 twins;
17 tiling families -> one toon vmat each over its 2048 diffuse (`DarkFantasyBrickSmall01`, `Roof01`,
`Stone02`, `StuccoBrown01-03`, `FloorTiles01`, `WoodFloorBoards01/02`, `HouseWall01`, `Window01/02/04`...);
`gen_pack_assets.py` writes all of them from its `vmats` table. The pack's normal and metallic maps are not
copied (nothing reads them). Hand-kept: `DarkFantasyVines` / `Moss01` / `Grunge01` / `DividerPattern01`
(heden_foliage cutouts; the textures' alpha runs 0-255). Skipped: `FX_*` (3-5 vertex flame and glow cards
for Synty's particles -- the MaterialList's 21 `Additive_Circle_Glow_01` slots all live there; the project's
own fire lights a candle), the two `Misc` meshes, `SM_Env_Skydome_01`, the combined `Characters.fbx` /
`BigCharacters.fbx`, and the `Individual/` classic-rig duplicates. `Capes.fbx` (four capes on `Cape_Rig`) and
`Tail_Rig_01.fbx` (the gargoyle and demon tails) are rigged and go through the character path. The main atlas
was missing from the user's source folder at first (moved, not copied); it lives in `Textures/Alts` now and
`darkfantasy_textures.py` reads it from there.

Materials, Dark Fortress: the atlas in the five palettes Synty actually placed meshes on (`Mat_01_A` 719
slots; `_01_B` 60, `_04_C` 19 -- the 45-degree floor tiles --, `_02_B`, `_04_B`, `_01_C`) each mapped to its own
twin (`DarkFortressShader01B`...), the same rule the Dark Fantasy twins now follow; eight tiling families
over their 4096 diffuse (`BrickLarge01` -- also the `_Double`/`_Half` tiling variants, which the toon material
cannot re-tile --, `BrickMedium01`, `BrickSmall01`, `SmoothStone01`, `SmoothStoneDark01`, `ChippedStone01`,
`Planks01`); five hand-kept cutouts (`Grunge01`, `Moss01` from the TGA -- the PNG's alpha is solid --,
`Thorns01`, `Chains01` for the 27 chain/cage/banner slots, `Spiderweb01`); and the nine spirit pools / water
planes on the dungeon's own `DungeonWater.vmat`. Glass, potion liquids, specimen jars and the wind-shader
flags/hammock are the atlas. The MaterialList's fire, ember, blood, portal, lightning and candle-glow slots
only occur on the six skipped `FX_` card files and on Unity particle systems. `FBX/Collision` (187 Unity
hulls) is not imported: the vmdl's collision comes from the render mesh. Two source files
(`SM_Prop_Shelf_04_Insert_01/02`) ship with their FBX visibility OFF, which made Blender's `select_all`
export them as empty files -- the exporter now unhides and selects explicitly.

Dark Fortress characters: no `SK_` files -- 11 characters and 7 rigged attachments (armor, fur, hoods,
pustules, hair) sit in ONE `Characters.fbx` on Synty's **classic** rig (`Root`/`Hips`/`Spine_01`, sided
fingers `Thumb_01_L`), the Behemoth and Colossus in `BR_Characters.fbx` on a bigger classic rig, the cape
on `Cape_Rig`. `split_chars` exports one FBX per mesh, each carrying the whole armature
(`apothecary_01`, `attach_hood_02`, `behemoth_01`, `attach_cape_01`). MEASURED: this classic rig's 28
shared joints sit exactly (0.00 cm) where the Dungeon Realms skeletons' do, i.e. it is the rig
`prep_skeleton_npc.py` already re-skins onto the Epic donor for the `realms_0N` kinds -- the only difference
is the sided finger names, so `bone_map="synty_classic"` needs a sided variant before one of these can be a
skeleton kind. Here the `Root` bone is a real LimbNode, so no root bone had to be added.

Output naming: static files keep Synty's name (`sm_prop_barrel_01.vmdl`); characters drop the `SK_` /
`SM_` / `Chr_` / `(BR_)Character_` prefixes (`troll_01`, `spiritdemon`, `skeleton_heavyarmor_01`, `capes`,
`attach_hair_witch_01`). The FBX node names inside are untouched (`SK_BR_Character_Troll_01` +
`..._Outline`), which is what the vmdl exception lists key on.

# Wardrobe (character customizer, Fantasy Sorcerers pack)

The player's outfit is the Synty Sidekick "Fantasy Sorcerers" pack: 131 pieces exported and toonified once
by Blender (`Desktop/Speelboord Studios/SorcererParts`, see its README) and brought in by

```
python tools/gen_sorcerer_parts.py
```

which copies `charactertoon/sorc_{set}_{slot}.fbx/.vmdl`, the seven 32x32 palette PNGs (upscaled x16) and
the manifest into `Assets/`, writes `Assets/Shaders/SorcererPalette.vmat` + `_01..06.vmat` (same heden_toon
material, only the albedo palette differs) and gives every vmdl a material group per palette
(`palette_01..06`). Colour set in game = `renderer.MaterialGroup`, never a MaterialOverride: the hit flash
sets an override, material groups yield to it and come back when it clears. A named group's `from` must be
the path the DEFAULT group produced (`shaders/sorcererpalette.vmat`), not the raw mesh material name: keyed on
the raw name the compiled model carried no group materials at all (check with `grep -a` on the `.vmdl_c`).
Hair and beards use `charactercolor.vmat` (flat renderer Tint, shared hair colour) and have no groups. It also generates
`Code/Character/SorcererCatalogue.cs` (61 options + 6 presets).
Rerun it after re-exporting the pack; it never touches the `human_*` body parts.

Catalogue: `AppearanceOptions` slots Hat, Hair, FacialHair, Torso (torso + both arms + hands), Legs (hips + both
legs), Shoes, ShoulderLeft/Right, HipLeft/Right + Palette; "None" first in every slot. The generated catalogue
reads Synty's Sidekick database (`My project (5)/.../Database/Side_Kick_Data.db`, read only): torso and legs
options are the six "Fantasy Sorcerers NN" presets' bundles, and those presets (back pieces dropped) are the
wardrobe's preset buttons. File numbers do not pair pieces (torso 01 is worn with upper arms 03); the database
is the only real matching. Back, face and hip-back pieces are not offered. A hat hides the hair but keeps the
choice. `DefaultLook()` is preset 1, `WearPreset` fills every slot (a preset without hair keeps the hair).
`CharacterAppearance` dresses any body from the synced json (one holder GameObject per slot, bone-merged,
`Hides` disables the skin regions under a piece), `ApplySaved` puts the RunSave outfit on a spawned player.

UI: `Code/UI/Wardrobe/` — the B key (`Wardrobe` action). `WardrobeStage` builds a hidden stage 30000 units
under the map in the same scene (tag `wardrobe`, excluded from the main camera): a dressed mannequin, its
own sun + fill (tagged for the stage cameras, since a camera's RenderTags whitelist filters lights too), a
preview camera rendering to a 2x-supersampled texture the panel shows as a background, and an icon rig
whose pieces are baked one per two frames into 256px transparent textures (only the piece renders: the
icon camera whitelists `wardrobe_icon`, which only the piece holder carries). Convars `wardrobe_fov`,
`wardrobe_pitch`, `wardrobe_sun_*`, `wardrobe_icon_*`, `wardrobe_supersample`. Look at the results, do
not guess: `wardrobe_icon_sheet` writes every icon to `data/voidmage/wardrobe/icons.png`, `wardrobe_shot`
the preview to `preview.png` (both under `sbox/data/local/voidmage#local/`).

Not done yet: first-person sleeves/gloves (`human_fp_arms.vmdl` is one baked bare-skin model without body
groups; it needs regenerating with groups plus bone-merged sleeve pieces), per-zone colours (a runtime 32x32
palette texture), a village prop that opens the wardrobe.
