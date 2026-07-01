# Crowds — Контекст проекта

Top-down шутер на Unity с ECS-архитектурой. «Толпы» мобов атакуют игрока, тот стреляет, собирает лут.

## Стек

- **Unity 6000.3.10f1** (URP 17.3)
- **Leopotam EcsLite** — ECS-фреймворк (github)
- **Sirenix Odin Inspector** — атрибуты `[BoxGroup]`, `[Title]`, `[Required]`, `[Button]` повсюду в инспекторах
- **Unity Input System** (new) — `InputSystem_Actions.inputactions` в `Assets/`
- **UniTask** — `com.cysharp.unitask`
- **Unity AI Navigation** (NavMesh) — для патфайндинга мобов
- **MPUIKit**, **ParticleEffectForUGUI**, **UniText** — UI-плагины

## Архитектура

**Точка входа:** `Assets/_Scripts/ECS/EntryPoint.cs` — `MonoBehaviour`, создаёт `EcsWorld`, `EcsSystems`, регистрирует все singleton-сущности и системы в `Awake`, гоняет `_systems.Run()` в `Update`.

**Паттерн singleton:** многие «singleton»-данные хранятся как одиночная сущность с одним компонентом (`MainHolderComponent`, `SpawnRequestComponent` и т.д.). Доступ — через расширения в `Extensions.cs`:
- `world.CreateSimpleEntity<T>()` — создать singleton-сущность
- `world.GetAsSingleton<T>()` / `TryGetAsSingleton<T>(out value)` — получить
- `world.GetAsSingleton<T1,T2>()` — с доп. фильтром
- `world.ForEachComponentInTheWorld<T>(action, deleteAfter)` — итерация
- `world.DeleteAllWith<T>()` — удалить все сущности с компонентом

**Все компоненты** объявлены в одном файле `Assets/_Scripts/ECS/Components.cs` (struct'ы).

**Системы** (`IEcsRunSystem`) — по одному файлу на систему в `Assets/_Scripts/ECS/`. Порядок регистрации важен, см. `EntryPoint.RegisterSystems()`.

**Конфиги** — ScriptableObject'ы в `Assets/_Scripts/SO/`. Корень — `MainHolder` (ссылки на `MobConfigHolder`, `GunConfigHolder`, `EffectsHolder`, `DecalsConfigHolder`, `SoundHolder`, `SpriteHolder`, `PlayerConfig`). Уровни — `LevelConfig`.

**Пулы** (Mob, Bullet, Effect, Decal, Loot, MapLoot) — singleton-компоненты со `Stack<>`/`List<>`/`Dictionary<>` и родительским `Transform`.

## Структура `Assets/_Scripts/`

- `ECS/` — системы, `Components.cs`, `EntryPoint.cs`, `Extensions.cs`
- `SO/` — ScriptableObject-конфиги
- `UI/` — MonoBehaviour-view для UI (`AmmoCounter`, `FailWindow`, `DifficultyTimerView`, `ValueBar`)
- `Animation/` — `SimpleAnimator` (Animator cross-fade wrapper, hash-cached), `AnimationType` (+ `AnimationTypes` enum→state-name/hash map)
- `SmartConditions/` — обёртки условий (`FragsConditionWrapper`, `NoAmmoAroundConditionWrapper`)
- `Enums/` — enum-типы
- `Input/InputActionsHolder.cs`
- `Remote/Leaderboard/` — лидерборд
- Корень `_Scripts/` — MonoBehaviour'ы сцены: `Player`, `Mob`, `Weapon`, `Bullet`, `Loot`, `SpawnPoint`, `FloorSector`, `NavMeshManager`, `AimVisualizer`, `PathGizmoDrawer` и enum'ы вроде `DamageType`, `AimType`, `BonusType`

## Ассеты

- `Assets/_Prefabs/` — игровые префабы (`Mobs/`, `UI/`, `MapLoot/`, `Effects/`, `Decals/`, оружие `762Piercer.prefab`, `Bullet.prefab`, `Shell.prefab`, `FloorSector.prefab`, `Loot.prefab`)
- `Assets/_Data/` — ассеты `ScriptableObject`
- `Assets/Scenes/SampleScene.unity` — основная сцена
- `Assets/_Animations/`, `_Materials/`, `_Sounds/`, `Shader/`, `Sprites/` — ресурсы
- Вендорные папки: `Epic Toon FX/`, `MPUIKit/`, `UniText/`, `Modern GDR - Free icons pack/`, `Plugins/`

## Соглашения

- Namespace `ECS` для систем/компонентов (хотя компоненты живут в глобальном namespace).
- MonoBehaviour-поля приватные с `[SerializeField]`, префикс `_`. Группируются через `[BoxGroup]`.
- Комментарии и заголовки `[Title(...)]` иногда на русском (не ломать кодировку при правках `EntryPoint.cs`).
- Файлы `.meta` коммитим — не удалять и не создавать файлы в обход Unity.
- Новые системы: добавить класс `IEcsRun/Init/DestroySystem` → зарегистрировать в `EntryPoint.RegisterSystems()` в правильном порядке.
- Новые компоненты: добавить `struct` в `Components.cs`.

## Команды

Билды и проверка типов — через Unity Editor (открыть сцену, Play). CLI-билда в репо нет. Перед выводами «готово по UI» — нужно явно прогнать в плей-моде; автотестов нет.

## Документация по фичам

- `Docs/ModifierSystem.md` — общая система модификаторов (баффы/дебаффы/DoT на любой сущности): модель `Modifier` и подтипы, `ModifiersSystem`, пути применения (прямой add vs `TryApplyModifierComponent`), потребители (`GetModifier<T>`), линковка с эффектами, плюс краткая сводка по бонусам. Читать вместо повторного обхода кода.
- `Docs/GrenadeFeature.md` — система гранат (лут → бросок с зарядкой и прицеливанием → дуговой полёт → радиальный взрыв). Читать вместо повторного обхода кода.
- `Docs/BonusFeature.md` — подбираемые бонусы (speed up, shield) поверх общей системы модификаторов: `BonusConfig`/`BonusConfigHolder` SO, `LootType.Bonus`, `BonusSystem`, бары speed/shield в `PlayerStats` с таймером. Читать вместо повторного обхода кода.
- `Docs/MeleeMobFeature.md` — моб ближнего боя с телеграфированной атакой (как у игрока): фазы замах→удар→кулдаун в одной анимации `attack`, `MeleeMobConfig : MobConfig` + общий `MeleeConfig`, `MeleeAttackerComponent`/`MeleeAttackerSystem`, отключение контактного урона в `CollisionSystem`. Читать вместо повторного обхода кода.
- `Docs/LootFeature.md` — система лута целиком: типы (`LootType`), источники спауна (`RequestSpawnSource`: моб/карта/доп.спаун), поток `DamageSystem`→`RequestLootSpawn`→`LootSystem` (розыгрыш drop-таблицы, выбор спрайта, пул), подбор в `CollisionSystem` по типам, и **настраиваемый таймер деспауна лута с мобов (по типам, на `MainHolder`)**. Читать вместо повторного обхода кода.
- `Docs/AmmoSystem.md` — патроны по калибрам (enum `Caliber`): запас хранится в `AmmoInventoryComponent` (словарь `Caliber`→кол-во), оружие с одинаковым `GunConfig.Caliber` делит пул; магазин остаётся в `WeaponComponent`. Лут патронов несёт калибр в поле `AmmoCaliber` (`None` → калибр текущего оружия). Новый калибр = добавить член enum с `[InspectorName]`. `AmmoConfig`/`AmmoConfigHolder` (на `MainHolder`) задают по калибру префаб снаряда (убран из `GunConfig`, fallback `MainHolder.BulletPrefab`) и иконку лута. Читать вместо повторного обхода кода.
- `Docs/SectorFeature.md` — система секторов пола под игроком: два режима в `LevelConfig.SectorMode` — `Recycling` (бесконечный скролл, 3 сектора переиспользуются с переносом объектов) и `Sliding` (конечный уровень, заранее расставленные секторы включаются/выключаются окном вокруг игрока). `CheckSectorSystem`, `NavMeshManager`, `FloorSector`. Читать вместо повторного обхода кода.
- `Docs/FailSequenceFeature.md` — кинематографичная концовка при гибели игрока: фазы блок управления → красная пелена → меню+пауза по 0.5с (`FailSequenceSystem`, `FailSequenceComponent`, `InputLockComponent`, рантайм-оверлей `FailScreenOverlay`), общий `GameOverActions.StopAllMoves`, рабочая кнопка рестарта. Читать вместо повторного обхода кода.
- `Docs/LoadingScreenFeature.md` — занавес загрузки между меню/рестартом и готовым уровнем: рантайм-оверлей `LoadingScreen` (спиннер + `UniText` + прогресс-бар, `DontDestroyOnLoad`, анимация на unscaledDeltaTime), неблокирующий бут `EntryPoint.InitializeAsync` (UniTask, покадровые шаги), асинхронное запекание navmesh (`NavMeshManager.Configure(bake:false)` + `RebuildNavMeshAsync`), `LoadSceneAsync` из меню/окон рестарта. Читать вместо повторного обхода кода.

## Git

- Главная ветка — `main`, текущая — `master`.
- `.csproj`/`.sln` в `.gitignore` — Unity их регенерирует, не коммитить.
