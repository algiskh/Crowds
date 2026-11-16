# Cheerleaders (NPC) and the hidden "don't forget the girls" quest — Reference

Three cheerleader NPCs dance on a stage in a corner of the hockey arena ("Ледовая арена"). They are not enemies: the player can't shoot, stab or blow them up. Their fate depends on whether the player keeps an eye on them, which makes an unannounced mini-quest.

## Building

1. **Tools ▸ Crowds ▸ Build Cheerleader NPC** (`Editor/Npc/CheerleaderNpcBuilder.cs`), from `Assets/Model/Cheerleader`:
   - Textures: `_BaseColor.jpg` (sRGB) and `_Normal.jpg` (NormalMap). They were JPEGs saved as `.png`; they were renamed together with their metas, so the guids are kept. `_face.png` is a portrait, not a mesh texture. It can serve as the future achievement icon.
   - Material: URP Lit `Assets/Model/Cheerleader/Cheerleader.mat`, remapped onto the FBX material. The model is imported as Generic via `MobModelPipeline.SetupModel`.
   - Clips: every `cheerleader_v2_10k@<Take>.fbx` becomes a clip `Cheerleader_<Take>`, and the **state name = take name** (e.g. `Can Can`). Everything loops except `Falling Forward Death`. If the hips drift, looping clips get an in-place copy in `Assets/_Animations/Npc/`. This reuses `MobModelPipeline.SetupClip/MakeInPlaceIfDrifting/ValidateBindings/SetupController` (now `internal`).
   - Controller: `Assets/_Animations/Npc/Cheerleader.controller`, default state `Idle`.
   - Prefab: `Assets/_Prefabs/Npc/Cheerleader.prefab`.
     - Root: `Cheerleader` + `NavMeshAgent` (r 0.3, 4.5 m/s, **disabled** until the escape).
     - Child `Visual`: the model scaled to **1.8 m**, with an Animator (no root motion, AlwaysAnimate). A `Pompom` under each hand bone (`mixamorig:LeftHand/RightHand`, scale compensated for the bone), listed in `Cheerleader._props`.
   - Pompom: `Assets/_Prefabs/Npc/Pompom.prefab`, primitive (white handle, green core, 26 white/green strands). Materials `Assets/_Materials/Npc/Pompom_*`. Root = grip, +Y = toward the fingers. **Shown only while dancing** (`Cheerleader.SetPropsVisible`).
2. **Tools ▸ Crowds ▸ Build Cheer Zombie Mob** (`Editor/Npc/CheerZombieMobBuilder.cs`), from `Assets/Model/Cheerleader Zombie`. The rigged mesh is `cheerleader_zombie_5k (1).fbx`; the other FBX has no skeleton. The textures were also JPEG-as-png and are renamed to `.jpg`. Steps:
   - copies `FastMob.prefab` → `_Prefabs/Mobs/CheerZombieMob.prefab` once;
   - writes the preset `_Data/Crowd/Setup/CheerZombieMob_ModelSetup.asset` with idle = Zombie Idle, run = Treadmill Running, die = Dying, 1.8 m, VAT bake into `_Data/Crowd/CheerZombieMob/` (the library goes to the config; VAT has no live Animator, so the 2 s death animation only plays on the skinned fallback);
   - runs `MobModelPipeline`;
   - sets `CheerZombie.asset`: prefab → CheerZombieMob, **150 HP**, death animation 2 s (the crowd library is set by the pipeline). The other stats are copied from Fast.

   No pompoms on the zombie.
3. **Tools ▸ Crowds ▸ Build Hockey Level**. Run it after steps 1–2. It places 3 instances on the stage (and creates `CheerZombie.asset` from Fast if it's missing; re-runs only enforce the id).

Log prefixes: `[CheerleaderNpc]`, `[CheerZombie]`, `[MobModel]`.

## Animation roles (set by the builder on `Cheerleader`)

| Role | States |
|---|---|
| Dances (one for the whole stand, in sync) | Arms Hip Hop Dance, Bellydancing, Can Can, Dancing Running Man, Hip Hop Dancing, Hip Hop Dancing (1). Any new take not listed below also becomes a dance. |
| Victory poses (by slot) | Female Standing Pose, Kneeling Idle, Female Standing Pose (1) |
| Idle | Idle (also after reaching the escape point) |
| Run | Goofy Running |
| Death | Falling Forward Death |

## Stage (hockey level)

The stage is in the world corner −X/+Z, which is the **top-right of the screen**: the fixed camera looks −X, so screen right is +Z. It sits on the diagonal from the rink's rounded corner to the arena corner and faces the ice and the camera, so the faces are visible.

- **Passage:** `CheerPassage` = 2.6 m between the boards and the stage front.
- **Deck:** 7 × 5 m at 0.9 m height, `NavMeshModifier` → Not Walkable.
- **Set into the stands:** the north and west stands are cut back. A short diagonal stand (`Stand_CheerCorner`: parapet with a `BulletBlocker`, plus tiers) closes the corner right behind the deck. The geometry is computed in `HockeyLevelBuilder.ComputeCheerCorner`.

`CheerleaderStand` (on the stage):

| Field | Meaning |
|---|---|
| `_cheerleaders`, `_slots` | 3 NPCs and their spots on the deck. Forward = toward the ice. |
| `_floorPoints` | 3 points in the middle of the passage ("under the stage"). Escape start and zombie spawn points. |
| `_escapePoint` | The opposite (south-east) corner of the arena floor. |
| `_transformStages` | Default level2 and level4 (the breathers). |
| `_zombieMobId` | `CheerZombie` |
| `_forgottenHint` | "вы забыли про девочек" |

## Behaviour — `ECS.CheerleaderSystem`

Registered after `FailSequenceSystem` / `ScoreboardSystem`. Each girl is an entity with `CheerleaderComponent`; there is no `MobComponent`, `HealthComponent` or collider.

| State | What happens |
|---|---|
| Dancing | All girls of the stand dance **the same** random dance at the same phase (`Cheerleader.PlayAtPhase`), with pompoms. When the whole group leaves the screen (renderer bounds vs the `Camera.main` frustum), they all switch to another dance. |
| Forgotten → Transformed | Checked **only on the frame a `_transformStages` stage (a breather) starts**, i.e. at the wave → breather transition. If none of the girls is on screen at that moment, they turn. If the player sees at least one of them, they stay alive for the whole breather, since there is no further check until the next breather starts. Results: <br>• the girls are hidden; <br>• 3 `CheerZombie` (Fast stats, 150 HP, own model `CheerZombieMob`) spawn at the floor points via `MobSpawnSystem.CreateMob`; <br>• the UI log shows "вы забыли про девочек"; <br>• quest `Forgotten = true`. <br>The zombies are ordinary mobs: the next wave's `MobsCleared` waits for them too. |
| Victory (stage `finish`) | Each girl plays her own static pose, chosen by slot index. They aren't frozen by the win pause, so the blend finishes. If they were never forgotten, quest `Saved = true`. |
| Player death → Escaping | They jump down to the floor points (`NavMesh.SamplePosition`), enable the agent and run to the escape point (±2 m). They get a `ZombieTargetComponent`. |
| Safe | Reached the escape point → `Idle`. Still a zombie target. |
| Dead | 50 HP. Damage = mob contact: `MobConfig.HitRadius` + 0.3 m, `Damage`, `HitCooldown`, the same numbers mobs use against the player. Then the death animation, the target is removed and so is `FriendlyNpcComponent`. |

Other rules:
- **Pause** (Esc or the fail menu) freezes their animators and agents.
- **Retarget** (`MobPathfindingSystem`): once `FailSequenceComponent.Phase != Inactive` and any `ZombieTargetComponent` exists, every mob paths to the nearest target instead of the player. Melee/ranged attack systems still aim at the player's body (they only face it); the contact damage is done by `CheerleaderSystem`.
- **Fail window over a living world** — the girls carry `FriendlyNpcComponent`, a generic marker for living "good" NPCs. The death sequence keeps its usual timing: the fail window opens after ~1 s.
  - If at least one friendly NPC is alive at that moment, `FailSequenceSystem` does **not** pause the world: the girls keep fleeing and the horde keeps chasing behind the window. New spawns are blocked and the controls stay locked. From the moment of death (any `FailSequencePhase` other than Inactive), `AimVisualizerSystem` shows the system cursor and hides the crosshair, and `LookAtCursorSystem` stops turning the dead player. These used to depend on the pause only.
  - 1.5 s after the last friendly NPC dies or turns, the usual full stop follows (`RequestPause` + `GameOverActions.StopAllMoves`). Girls who reached the escape point stay alive, so the world behind the window keeps running.
  - With no friendly NPCs (e.g. they were forgotten), it's the old behaviour. The camera keeps following the dead player, so the run is only visible if it happens near the player.

## Mini-quest / achievement (not implemented — data only)

Singleton `CheerleaderQuestComponent`:

| Field | When it's set |
|---|---|
| `Forgotten` | The girls were turned into zombies during a breather. |
| `Saved` | Victory with the girls never forgotten. This is the future achievement condition, e.g. "Не забыл про девочек". Read it in the win handler. |

Ideas: an achievement icon from `cheerleader_v2_10k_face.png`; count how many girls survive the escape on death.

## Limits / not verified

- Nothing has been run in Play Mode yet. Check:
  - the clip import: every clip should log "binds all N paths", and the hips should not sink;
  - the height (1.7 m);
  - that the dances stay in place;
  - that the faces are visible from the camera;
  - the transformation at a breather;
  - the escape plus the horde chasing the girls.
- Zombie cheerleaders: check that the collider and health bar (inherited from FastMob) fit the 1.8 m model, and that the die animation plays.
- Pompoms: the grip offset and orientation in the hand are first guesses (the root sits at the hand bone, +Y toward the fingers). Tune them on the Pompom instances in `Cheerleader.prefab`, but note the builder rebuilds the prefab.
- Grenades and player bullets ignore the girls (by design).
