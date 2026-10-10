# Known Issues — бэклог багов и оптимизаций

Итог аудита кода (`Assets/_Scripts`, 2026-10). Здесь — то, что **сознательно не исправлено** и
оставлено для будущих решений. Строки указаны на момент аудита — перед правкой сверяться с кодом.

Уже исправлено в рамках аудита (для контекста): удалён лидерборд (`Remote/Leaderboard` + объект
в сцене с токеном), `MoveSystem` пишет `Direction` по `ref`, DoT-тики создают отдельный
`RequestDamageComponent`, цели запросов урона/модификаторов/эффектов — `EcsPackedEntity`,
`GunConfig.ShotDebuffs/ReloadDebuffs` — `[SerializeReference]`, убран `using NUnit` из рантайма,
перезарядка только при неполном магазине и наличии запаса, отход гренадёра по NavMesh-пути,
`InitializeAsync` в try/finally, поражение приоритетнее победы, прогресс перезарядки в UI.

## Общие заметки по EcsLite

- Установленная версия EcsLite **сбрасывает компонент в `default` при `Del`** (`components.cs`).
  Комментарии в коде вида «EcsLite не обнуляет переиспользуемые struct'ы» неверны; ручное
  обнуление managed-полей перед `Del` не нужно.
- Реальный риск — **сырые `int`** сущностей, живущие дольше кадра. В DEBUG → «Cant touch destroyed
  entity», без DEBUG → тихое попадание в переиспользованный id. Для всего, что переживает кадр,
  использовать `EcsPackedEntity` (`world.PackEntity` / `Unpack`).
- Запрос, который не удалось обработать, всё равно удалять — иначе он висит вечно.

## Баги

### Бой / оружие
- **`BulletSystem.cs:83-96`** — один `Stack<Bullet>` на все калибры и на вражеские пули, а префаб
  выбирается по калибру (`ResolveProjectile`). Из пула может прийти пуля с чужим мешем/трейлом/
  `MaxPierceCount`. Решение: `Dictionary<префаб|Caliber, Stack<Bullet>>`, как у мобов/эффектов.
- **`BulletSystem.cs:95` + `BulletOverlapSystem.cs:84-89`** — пуле не выставляется поворот;
  Raycast-проверка идёт по устаревшему `transform.forward`, OverlapBox — с устаревшим поворотом.
  Решение: `rotation = LookRotation(direction)`, луч по `move.Direction`.
- **`CollisionSystem.cs:69-80`** — в ветке последнего пробития нет `ContainsFixed`: пуля повторно
  бьёт того же моба и гасится, не долетая до второго. `FixedList32Bytes<int>` вмещает 7 int —
  `MaxPierceCount > 8` бросит на `Add`. Решение: проверка до обеих веток + clamp по ёмкости.
- **`WeaponFireSystem.cs:29-35`** — ранний `return` во время single-load перезарядки пропускает
  удаление `RequestFireComponent`, запросы копятся. На этой утечке держится `hasFiringRequest`
  в `WeaponReloadSystem` (прерывание перезарядки выстрелом). Решение: явный флаг «прервать».
- **`EffectsSystem.cs:75-79`, `DecalSystem.cs:60-64`** — при неизвестном id запрос не удаляется
  и логирует каждый кадр. `DecalSystem.cs:68-70` использует `decal` до null-проверки.
- **`DamageSystem`** — `EndGameComponent` создаётся на каждый удар, пока HP игрока ≤ 0.
- **`GrenadeLauncher.cs:27-30`** — `Instantiate(null)` до проверки на null; `Count` гранат уже
  уменьшен в `GrenadeThrowSystem:80`.
- **`GrenadeThrowSystem.cs:24`** — на паузе/блоке ввода `IsCharging` не сбрасывается: отпускание
  кнопки во время паузы → бросок сразу после снятия паузы; превью прицела остаётся видимым.
- **`ModifiersSystem`** — каждый применённый клон DoT спаунит свой VFX: N дробин = N эффектов на
  одном мобе. Решение: один эффект на (owner, DamageType).

### Мобы / движение / ИИ
- **`MoveSystem.cs:53-66`** — порог достижения waypoint'а 0.05 при шаге > 0.1 (низкий FPS, бафф
  скорости) → дрожание на углу до следующего репаса. Решение: `if (dist <= step)` — снап и переход.
  Новый waypoint после перехода не выравнивается по `y`.
- **`MobPathfindingSystem.cs:110-120`** — при неполном/неудачном пути `MovePath` удаляется и моб
  едет `MoveDirect` по последнему направлению; если он ушёл с navmesh, путь больше не строится.
  Решение: обнулять `Direction` либо рулить прямо к игроку.
- **Спаун: `GroupSpawnSystem.cs:245,269`, `SpawnPointSystem.cs:45,64`** — `mobCount` читается раз
  за кадр и не растёт: групповой спаун и несколько точек в одном кадре превышают `ActiveMobLimit`.
- **`SpawnPointSystem.cs:47-51`** — во время межспаунового кулдауна таймеры всех точек замирают.
- **Null-проверки**: `SpawnPointSystem.cs:84` (неверный `MobId`), `MobSpawnSystem.cs:201`
  (`ValueBar`), `PlayerSystem.cs:69,72` (`MeleeConfig` до проверки), `AgentDebugger.cs:192`
  (+ `Debug.Log` каждый кадр на :217).
- **`PathRecalculationRequest`** у последователей формации никогда не потребляется (фильтр
  патфайндинга требует `PathRecalculation`) — безвредно, мёртвое состояние.
- **Порядок систем (`EntryPoint.RegisterSystems`)** — `InputSystem`/`PlayerSystem` после
  систем оружия (+1 кадр задержки ввода), `FollowSystem` до `PlayerMovementSystem` (камера
  догоняет позицию прошлого кадра — дрожание при плавающем dt).

### Мир / лут / уровни
- **`AdditionalLootSpawnSystem.cs:167-171` + `FragsConditionWrapper.cs:38-45`** —
  `CooldownMax` нигде не присваивается (всегда 0), `FragsCondition` залипает в `IsFulfilled`
  навсегда (`Reset()` не вызывается) → после N фрагов лут респаунится сразу после каждого подбора.
- **`FragsConditionWrapper.cs:53-55`** — `Dispose()` не вызывает `base.Dispose()`.
- **`AdditionalLootSpawnSystem.cs:18-19`** — `GetCopyUntyped()` может вернуть null → NRE в `Init`
  ломает весь `RegisterSystems().Init()`. **`LootSystem.cs:207`** — NRE на неизвестный id оружия.
- **`BreakableSystem.cs:195-205`** — пуловые breakable'ы с исходом `Debris` никогда не
  возвращаются в пул; повторные (не `Once`) LevelEvent'ы плодят новые инстансы.
- **`BreakableSystem.cs:94-96`** — breakable в неактивном секторе (Sliding) имеет нулевые bounds →
  проба в (0,0,0) может наносить контактный урон от мобов у начала координат.
  Решение: пропускать `!view.isActiveAndEnabled`.
- **`BreakablesClearedConditionWrapper.cs:40-44`** — пустой `_configId` считает все breakable'ы
  уровня, включая далёкие секторы → стейдж не закончится, пока не разбит весь уровень.
- **`Utils/Extensions.cs:287-303`** — bounds сектора строятся по трансформированным min/max;
  для сектора, повёрнутого на 90°/180°, min > max и проверка всегда false.

### UI / ввод / конфиги
- **`ControlBindingRow.cs:144`** — `WithControlsExcluding("<Mouse>/leftButton")`: атаку нельзя
  вернуть на ЛКМ иначе как «Reset All». Нет проверки конфликтов биндингов.
- **`AimVisualizerSystem.cs:76`** — без мыши (`Mouse.current == null`: геймпад, Steam Deck) NRE
  каждый кадр.
- **`PlayerStats.cs:15`** — максимум HP-бара захардкожен `100f` вместо `PlayerConfig.MaxHealth`.
  **`PlayerStats.cs:76`** — `SetFragCount` без null-проверки `_killsText` (дубликат `SetKillsText`).
- **`ControlsSettingsMenu.cs:86,163-169`** — `effectivePath.StartsWith` без проверки на пустую строку.
- **`MainMenuController.cs:148-162`** — `Play` без защиты от повторного входа (двойной клик → два
  `LoadSceneAsync`).
- **`UILogSlot.cs:52,58`** — `Time.deltaTime`: при паузе лог «замерзает».
- **`LocalizationHolder.cs:83`** — `GetKey` бросает при `_entries == null`, нет фолбэка на `Text`.
- **`using Unity.VisualScripting`** в `LocalizationManager.cs`, `Utils.cs:3`, `Utils/Extensions.cs:5`,
  `Components.cs` — лишняя зависимость от пакета; `LocalizationManager` — мёртвый код.
- **`VatBakerWindow.cs`** — `CeilToInt((float)totalTexels / width)` теряет точность > ~16.7M
  текселей (:113); два `Color[]` до 16384² (:120-121) → OOM; `Shader.Find` без null-проверки (:297).

### Концовка / загрузка / секторы
- **`CheckSectorSystem.cs:29-34` + `NavMeshManager.cs:124-150`** — в Sliding-режиме синхронный
  `BuildNavMesh()` при каждой смене окна; асинхронный бейк на загрузке тут же перезаписывается
  синхронным (секторы ещё все активны); нет гистерезиса на шве секторов.

## Производительность (по ожидаемому выигрышу)

1. **Лишний `NavMeshAgent` на каждом мобе** (`_Prefabs/Mobs/*.prefab`, `m_Enabled: 1`,
   avoidance high quality). `Mob._agent` не используется, движение — через `transform`. Агент
   впустую считает crowd/avoidance и спорит с ручным движением. Удалить или выключить.
2. **`Debug.Log` в горячих путях** — `UISystem.cs:106,111` (каждый кадр),
   `FragsConditionWrapper.cs:22,32,48` (каждый кадр), `Modifier.cs:130` (каждый тик DoT),
   `AgentDebugger`, `DifficultyTimerView.cs:22,32`, `WeaponReloadSystem.StartShuttering`.
   Аллокация + stack trace, в билде тоже.
3. **`Utils/Extensions.cs:287,299` — `.mesh` вместо `.sharedMesh`** — клонирует меши пола
   (утечка, ломает батчинг) и пересчитывает bounds при каждом вызове (каждый кадр + на каждый
   декаль/лут/моб при сдвиге секторов). Решение: `sharedMesh` + кэш bounds в `FloorSector`.
   `FloorSector.MeshFilters` = null, если сектор был неактивен на `Awake`.
4. **Синхронный `BuildNavMesh()`** (`NavMeshManager:149,172`) — фриз кадра. Перейти на
   асинхронный `UpdateNavMesh` (как `RebuildNavMeshAsync`).
5. **`MobPathfindingSystem` без бюджета** — форс-репасы (`EnterChase`, breakable'ы) падают в один
   кадр; считается и для мобов в фазах атаки, и на паузе. Решение: round-robin очередь N путей за
   кадр; так как все идут к игроку — рассмотреть общий flow field.
6. **Карты collider→entity пересобираются каждый кадр** — `BulletOverlapSystem.cs:48-65`,
   `ExplosionSystem.cs:44-61` (даже когда все гранаты на фитиле), `BreakableSystem.cs:86-129`.
   Одна общая карта, обновляемая на спаун/деспаун. Буферы 32/64 при `layerMask = ~0` в толпе
   переполняются → часть мобов не получает урон: выделить damageable-слой.
7. **`AudioSource.PlayClipAtPoint`** на каждый удар/выстрел моба (`MeleeSpawnSystem:148`,
   `RangedAttackerSystem:160`) — создаёт/уничтожает GameObject. Пул AudioSource с лимитом.
8. **GC каждый кадр / на событие:**
   - `DamageSystem.cs:20` — `new List<int>()` + `Contains` → поле `HashSet<int>`; плюс полный
     обход мобов для поиска мёртвых — собирать в цикле запросов.
   - `DifficultyTimerView.cs:14-17,44-45` — `TimeSpan.ToString` каждый кадр → форматировать при
     смене секунды.
   - `PlayerStats.cs:56-57` — `ToString` таймера бонуса каждый кадр; `ValueBar.ApplyValue` —
     Gradient + `SetActive` каждый кадр.
   - `LoadingScreen.cs:214-219` — `"LOADING" + new string('.')` каждый кадр.
   - `SpawnPoint.cs:39` / `SpawnPointSystem.cs:74` — LINQ с замыканиями; для точки без конфига
     уровня таймер не сбрасывается → каждый кадр.
   - `CollisionSystem.cs:84,154,207` — `List` + `Clone()` на каждое попадание с дебаффами.
   - `MeleeSpawnSystem.cs:153-188,218-220` — обход всех `HealthComponent` на каждый удар,
     `GetAllModifiersAsCopies` + LINQ `Count()`.
   - `MoveSystem.cs:62` — `pathPool.Del` выбрасывает список waypoint'ов вместо возврата в пул.
   - `MobSpawnSystem.cs:151,174,178` — `GetComponentsInChildren` на каждый спаун (кэшировать на
     `Mob`); `GroupSpawnSystem.cs:293` / `FormationTable.ComputeOffsets` — `new` на групповой спаун.
   - `LocalizationHolder.GetKey` — LINQ `FirstOrDefault` с замыканием.
9. **Мелкое:**
   - `world.Filter<>().End()` и `GetAsSingleton` в `Run` — кэшировать в `Init` (аллокаций нет,
     но сортировка+хэш маски). Неиспользуемые выборки синглтонов: `LootSystem:44`,
     `BulletSystem:18`, `WeaponFireSystem:15`, `WeaponReloadSystem:22`, `DecalSystem:13,16`.
     `UISystem` — ~9 `GetAsSingleton` + ~15 фильтров + 8 `DeleteAllWith` каждый кадр.
   - `LookAtCameraSystem` — поворачивает хелсбары, скрытые через `renderer.enabled`.
   - `CrowdRenderSystem` — нет frustum culling; мобы группы анимируются синхронно
     (рандомизировать стартовый `ClipTime`).
   - `EffectsHolder.GetEffect` / `SoundHolder.GetClip` — линейный поиск по строке → `Dictionary`.
