# Level Events — Reference

Scene-scripted actions tied to **difficulty-stage starts**, gated by the existing **smart conditions**.
An empty scene object (`LevelEventTrigger`) holds a list of entries; each entry fires when its target
`DifficultyLevel` stage begins — optionally waiting on smart conditions first — and spawns breakables.
Built on the existing difficulty / smart-condition / breakable-spawn infrastructure; this doc is the
source of truth so the code doesn't need re-exploring.

## Flow

```
DifficultySystem.ApplyStage ──► DifficultyComponent.Stage.DifficultyLevel changes
LevelEventSystem (runs after AdditionalLootSpawnSystem, before BreakableSpawnSystem):
  • detects stage change → ARMS matching entries:
        instantiates each entry's SmartConditionWrapper copies, Initialize(world),
        registers them as SmartConditionComponent (so SmartConditionSystem iterates them)
  • armed entry, all conditions IsFulfilled (or none) → world.RequestSpawnBreakable(id, point, rot)
        then disposes its conditions, marks Fired
BreakableSpawnSystem (same frame) ──► the breakable appears
SmartConditionSystem (end of frame) ──► Iterate()s armed conditions; IsFulfilled ready next frame
```

Conditions are instantiated **at arm-time (stage start)**, not at level load, so accumulator conditions
(e.g. `FragsCondition`) count from the stage boundary. They're disposed and their
`SmartConditionComponent` entities removed on fire (and on disarm when the stage is left un-fired) — the
same reference-match cleanup `DifficultySystem.FinishStage` uses, kept separate from a stage's own
`EndConditions`.

## Model

| Thing | Where |
|---|---|
| `LevelEventTrigger` MonoBehaviour | `LevelEventTrigger.cs` — `SerializedMonoBehaviour`, empty scene object; `List<LevelEventEntry> Entries` |
| `LevelEventEntry` | `LevelEventTrigger.cs` — `OnStageStart` (`DifficultyLevel`), `Conditions` (`SmartConditionWrapper[]`, empty = fire immediately), `Once`, `Spawns` (`SpawnBreakableAction[]`) |
| `SpawnBreakableAction` | `LevelEventTrigger.cs` — `ConfigId`, `Point` (Transform; null = trigger's transform), `Rotation` |
| `LevelEventHolderComponent` | `ECS/Components.cs` — singleton, `List<LevelEventTrigger> Triggers` (filled by `EntryPoint`) |
| `LevelEventObserverComponent` | `ECS/Components.cs` — runtime state per entry: `Entry`, `Origin`, `Level`, `Conditions` (armed copies), `Armed`, `Fired` |
| `LevelEventSystem` | `ECS/LevelEventSystem.cs` — arms/fires entries; registered after `AdditionalLootSpawnSystem` |

## Registration

`EntryPoint.SetupSpawnData` scans `FindObjectsByType<LevelEventTrigger>()` (same pattern as
`SpawnPoint`/`MapLoot`/`Breakable`) and always creates the `LevelEventHolderComponent` singleton (even
empty — the system no-ops on zero observers). `LevelEventSystem.Init` builds one
`LevelEventObserverComponent` per entry. `Run` watches `DifficultyComponent.Stage.DifficultyLevel`
(change-detection; the **first** stage also counts as a start) to arm, then fires when conditions pass.

## Gating the *next* stage on destruction (`BreakablesClearedCondition`)

The "start of the next difficulty level depends on smart conditions" side reuses the existing
`DifficultyStage.EndConditions : SmartConditionWrapper[]` — a stage advances when its timer is up **and**
all end-conditions are fulfilled. New condition type so that can depend on the breakables you spawned:

- `BreakablesClearedConditionWrapper` / `BreakablesClearedCondition`
  (`SmartConditions/BreakablesClearedConditionWrapper.cs`). Fulfilled when no matching breakable remains
  (`_configId` empty = any). A `_sawAny` **latch** blocks "cleared" until at least one matching breakable
  has existed, so the stage can't advance in the one-frame gap before the on-start spawn registers.

Pattern: stage N's `LevelEventTrigger` entry spawns breakables on start; stage N's `EndConditions`
include a `BreakablesClearedCondition` → the level won't progress to N+1 until they're destroyed.

## Editor setup

1. Add an empty GameObject with `LevelEventTrigger`; add child transforms as spawn points if desired.
2. Add entries: pick `OnStageStart` (e.g. `level2`), fill `Spawns` (`ConfigId` must exist in
   `MainHolder.BreakableConfigHolder`, `Point` = a child transform or leave empty for the trigger's own
   position). Optionally add `Conditions` and toggle `Once`.
3. To gate the following stage on destruction: create a `BreakablesClearedCondition` asset
   (Scriptable Objects ▸ Smart Conditions ▸ Breakables Cleared Condition), set `_configId`, and add it to
   that stage's `EndConditions` in the `LevelConfig`.

## Notes / extension

- **New action type**: add a class alongside `SpawnBreakableAction`, a field on `LevelEventEntry`, and a
  branch in `LevelEventSystem.Fire`.
- **Pure-condition trigger** (fire without a stage boundary): not implemented — entries are stage-armed.
  Add a "start" condition type or a no-stage arming path if needed.
- No automated tests — verify in Play mode: breakable appears exactly when the target stage starts,
  optional conditions gate it, `Once` prevents re-fire on level loop, and (if used) the next stage won't
  advance until the spawned breakables are destroyed.

## Related docs

- `Docs/BreakableFeature.md` — the breakable spawn/pool/destruction path this feeds into.
- `Docs/LootFeature.md` — `AdditionalLootSpawnSystem`, the "smart condition → spawn" precedent this mirrors.
