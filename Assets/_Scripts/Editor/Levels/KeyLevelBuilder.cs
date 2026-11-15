using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Builds the campaign level "Запертая дверь" (ref: Assets/REFs/LEVEL_KEY.png) — Tools ▸ Crowds ▸ Build Key Level.
	/// Safe to re-run: every asset is rewritten in place (same guids), materials are created once.
	///
	/// Layout (floor top y = -0.5, one FloorSector, SectorMode.Sliding):
	///   P1 east  x[14,42]  z[-12,12] — start (38,0) facing west; 2 waves Slow+Fast; pistol start.
	///   P2 west  x[-30,14] z[-12,12] — long hall. Gate G12 (x=14) opens after P1 and shuts behind the player;
	///                                   breather + shotgun/supplies; 2 waves: Toxic, then FastToxic; 6 sewer
	///                                   hatches (symmetric, double as spawn points); crates on the west side;
	///                                   key appears after wave 2.
	///   P3 north x[-30,-4] z[12,38]  — key gate G23 (x=-17, z=12), shuts behind the player; Fast+Fatman, last wave
	///                                   + ToxicHelmet.
	///   Exit     x[-20,-14] z[38,48] — gate G3N (z=38) opens after P3; reaching the exit zone wins.
	/// Walls and closed gates stop bullets (BulletBlocker). Ammo fallback: when the current caliber runs low and none
	/// of it lies around, a small ammo pack drops on the player's platform (AdditionalLoot + NoAmmoAroundCondition,
	/// zone-aware points). Spawns: P1/P3 — big open wall pipes, P2 — the sewer hatches. 3 oil slicks on P3 slow
	/// everyone by 25%. Mob weapon drops: Rifle/SMG only.
	///
	/// Produces: materials (_Materials/Level), prop prefabs (_Prefabs/Props: SewerHatch, KeyPickup, LevelGate),
	/// condition assets (_Data/SmartConditions/Mission), LevelConfig (_Data/Levels/Configs/Level_Key),
	/// level prefab (_Prefabs/Levels/Level_02_Key), LevelDefinition (_Data/Levels/Definitions/Level_Key) inserted
	/// as the 2nd campaign level. See Docs/KeyLevelFeature.md.
	/// </summary>
	public static class KeyLevelBuilder
	{
		internal const string MaterialFolder = "Assets/_Materials/Level";
		internal const string PropFolder = "Assets/_Prefabs/Props";
		internal const string ConditionFolder = "Assets/_Data/SmartConditions/Mission";
		internal const string LevelPrefabPath = "Assets/_Prefabs/Levels/Level_02_Key.prefab";
		internal const string LevelConfigPath = "Assets/_Data/Levels/Configs/Level_Key.asset";
		internal const string DefinitionPath = "Assets/_Data/Levels/Definitions/Level_Key.asset";
		internal const string CampaignLibraryPath = "Assets/_Data/Levels/CampaignLevelLibrary.asset";
		internal const string FirstDefinitionPath = "Assets/_Data/Levels/Definitions/LevelDefinition.asset";
		internal const string FloorMaterialPath = "Assets/_Materials/Floor.mat";
		internal const string CratePrefabPath = "Assets/_Prefabs/Breakables/Crate.prefab";
		internal const string ToxicMobPrefabPath = "Assets/_Prefabs/Mobs/ToxicMob.prefab";
		internal const string ToxicFogName = "FogCalmGrey";

		internal const float FloorY = -0.5f;
		internal const float WallHeight = 2.2f;
		internal const float WallThickness = 0.8f;
		internal const float GateWidth = 5f;

		internal const string KeyId = "p2key";
		internal const string ZoneP1 = "p1";
		internal const string ZoneP2 = "p2";
		internal const string ZoneP3 = "p3";
		internal const string AdditionalSpawnTag = "AdditionalSpawn";
		internal const int FallbackAmmoCount = 10;
		internal const string ZoneExit = "exit";
		internal const string GateP1P2 = "g12";
		internal const string GateP2P3 = "g23";
		internal const string GateNorth = "g3n";

		// Bullet hits on walls / closed gates (BulletBlocker.ImpactEffectId → FxHolder).
		internal const string WallSparkId = "wall_spark";
		internal const string WallSparkPrefabPath = "Assets/_Prefabs/Effects/WallSpark.prefab";
		internal const string SparkSourcePath = "Assets/Epic Toon FX/Prefabs/Combat/Explosions (Misc)/SparkExplosion.prefab";
		internal const float WallSparkScale = 0.35f;
		internal const string FxHolderPath = "Assets/Resources/FxHolder.asset";

		// URP decals: the Decal Projector prefab has DecalLayerMask = 4 (rendering layer 2), so only renderers whose
		// renderingLayerMask has that bit receive blood/scorch decals. Level_01 floors use 5 (default + layer 2).
		// Walls/props keep the default (1) → no decals on them.
		internal const uint FloorRenderingLayers = 1u | 4u;

		internal static Material _wall, _wallCap, _gate, _hazard, _hazardDark, _hatch, _hatchRim, _keyGold, _keyGlow, _lampRed, _exitMark;
		internal static Material _pipeSupply, _pipeReturn, _boiler, _valve, _outlet, _outletDark, _sludge, _oil;

		[MenuItem("Tools/Crowds/Build Key Level")]
		public static void Build()
		{
			EnsureFolder(MaterialFolder);
			EnsureFolder(PropFolder);
			EnsureFolder(ConditionFolder);
			CreateMaterials();

			BuildWallSparkEffect();
			var hatch = BuildHatchPrefab();
			var key = BuildKeyPrefab();
			var gate = BuildGatePrefab();
			var oilSlick = BuildOilSlickPrefab();

			var mobsCleared = Condition<MobsClearedConditionWrapper>("MobsCleared", null, null);
			var inP2 = Condition<PlayerInZoneConditionWrapper>("PlayerInZone_P2", "_zoneId", ZoneP2);
			var atExit = Condition<PlayerInZoneConditionWrapper>("PlayerInZone_Exit", "_zoneId", ZoneExit);
			var g23Open = Condition<GateOpenedConditionWrapper>("GateOpened_G23", "_gateId", GateP2P3);
			var lowAmmo = Condition<NoAmmoAroundConditionWrapper>("LowAmmoNoneAround", prototype =>
			{
				prototype.FindPropertyRelative("_sameCaliberOnly").boolValue = true;
				prototype.FindPropertyRelative("_lowAmmoThreshold").intValue = 3;
			});

			var config = BuildLevelConfig(mobsCleared, inP2, atExit, g23Open, lowAmmo);
			var levelPrefab = BuildLevelPrefab(config, hatch, key, gate, oilSlick);
			BuildDefinition(levelPrefab);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"[KeyLevelBuilder] Level built: {LevelPrefabPath}");
		}

		// ================================================================== level config

		internal static LevelConfig BuildLevelConfig(SmartConditionWrapper mobsCleared, SmartConditionWrapper inP2,
			SmartConditionWrapper atExit, SmartConditionWrapper g23Open, SmartConditionWrapper lowAmmo)
		{
			var config = LoadOrCreate<LevelConfig>(LevelConfigPath);

			var cleared = new[] { mobsCleared };
			var stages = new List<DifficultyStage>
			{
				Wave(DifficultyLevel.level1, 30f, rest: 3f, interSpawn: 0.6f, cleared,
					"Платформа 1 — волна 1/2: отбейтесь от зомби"),
				Wave(DifficultyLevel.level2, 40f, rest: 5f, interSpawn: 0.5f, cleared,
					"Платформа 1 — волна 2/2"),
				Step(DifficultyLevel.level3, 10f, new[] { inP2 },
					"Проход на запад открыт: соберите припасы и дробовик"),
				Wave(DifficultyLevel.level4, 35f, rest: 6f, interSpawn: 0.45f, cleared,
					"Платформа 2 — волна 1/2: токсичные зомби"),
				Wave(DifficultyLevel.level5, 45f, rest: 5f, interSpawn: 0.4f, cleared,
					"Платформа 2 — волна 2/2: быстрые токсики"),
				Step(DifficultyLevel.level6, 0f, new[] { g23Open },
					"Подберите ключ и откройте дверь на север"),
				Wave(DifficultyLevel.level7, 40f, rest: 4f, interSpawn: 0.45f, cleared,
					"Платформа 3 — волна 1/2"),
				Wave(DifficultyLevel.level8, 50f, rest: 5f, interSpawn: 0.4f, cleared,
					"Платформа 3 — последняя волна"),
				Step(DifficultyLevel.level9, 0f, new[] { atExit },
					"Северный проход открыт — уходите!"),
				Step(DifficultyLevel.finish, 0f, null, null),
			};

			SetField(config, "_difficultyStages", stages);
			// Ammo fallback (like the endless level): low on the current caliber and none of it around → a small pack
			// of current-caliber ammo (None) drops at a loot point on the player's platform.
			var fallback = new AdditionalLootConfig();
			fallback.AdditionalLoot[lowAmmo] = new[] { Ammo(Caliber.None, FallbackAmmoCount) };
			SetField(config, "_AdditionalLootConfigs", new List<AdditionalLootConfig> { fallback });
			SetField(config, "_sectorMode", SectorMode.Sliding);
			SetField(config, "_activeSectorRadius", 1);
			SetField(config, "_spawnDistance", new Vector2(7f, 45f));
			SetField(config, "_allowedMobWeaponDrops", new[] { "Rifle", "SMG" });
			EditorUtility.SetDirty(config);
			return config;
		}

		internal static DifficultyStage Wave(DifficultyLevel level, float spawnTime, float rest, float interSpawn,
			SmartConditionWrapper[] end, string objective) => new()
		{
			DifficultyLevel = level,
			DifficultyTimer = spawnTime,
			SpeedMultiplier = 1.25f,
			InterSpawnCooldown = interSpawn,
			RestTime = rest,
			EndConditions = end,
			ShowTimer = false,
			SpawnOnlyWhileTimer = true,
			Objective = objective,
		};

		internal static DifficultyStage Step(DifficultyLevel level, float minTime, SmartConditionWrapper[] end,
			string objective) => new()
		{
			DifficultyLevel = level,
			DifficultyTimer = minTime,
			SpeedMultiplier = 1f,
			InterSpawnCooldown = 0.5f,
			RestTime = minTime,
			EndConditions = end ?? System.Array.Empty<SmartConditionWrapper>(),
			ShowTimer = false,
			SpawnOnlyWhileTimer = true,
			Objective = objective,
		};

		// ================================================================== level prefab

		internal static GameObject BuildLevelPrefab(LevelConfig config, GameObject hatch, GameObject key, GameObject gate,
			GameObject oilSlick)
		{
			var root = new GameObject("Level_02_Key");
			try
			{
				var levelRoot = root.AddComponent<LevelRoot>();

				// --- Floor (one sector) ---
				var floorGo = new GameObject("Floor");
				floorGo.transform.SetParent(root.transform, false);
				var sector = floorGo.AddComponent<FloorSector>();
				var floorMat = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
				// One mesh, UV = world XZ in metres (the floor texture tiles per metre, like the ProBuilder floor).
				var floorMesh = BuildFloorMesh(new[]
				{
					new Rect(14, -12, 28, 24),   // P1
					new Rect(-30, -12, 44, 24),  // P2 (long hall)
					new Rect(-30, 12, 26, 26),   // P3
					new Rect(-20, 38, 6, 10),    // exit corridor
				});
				var slab = new GameObject("FloorMesh");
				slab.transform.SetParent(floorGo.transform, false);
				slab.AddComponent<MeshFilter>().sharedMesh = floorMesh;
				var slabRenderer = slab.AddComponent<MeshRenderer>();
				slabRenderer.sharedMaterial = floorMat;
				slabRenderer.renderingLayerMask = FloorRenderingLayers; // receives decals (see FloorRenderingLayers)

				// --- Walls (Default layer, no colliders: the navmesh bake cuts them out; agents stay off).
				// Each wall carries a BulletBlocker, so bullets of both teams stop on it.
				var walls = Child(root.transform, "Walls");
				WallX(walls, "South", -12, -30, 42);
				WallZ(walls, "East", 42, -12, 12);
				WallX(walls, "P1North", 12, 14, 42);
				WallZ(walls, "P1P2_S", 14, -12, -GateWidth / 2);
				WallZ(walls, "P1P2_N", 14, GateWidth / 2, 12);
				WallZ(walls, "West", -30, -12, 38);
				WallX(walls, "P2P3_W", 12, -30, -17 - GateWidth / 2);
				WallX(walls, "P2North_E", 12, -17 + GateWidth / 2, 14);
				WallZ(walls, "P3East", -4, 12, 38);
				WallX(walls, "P3North_W", 38, -30, -17 - GateWidth / 2);
				WallX(walls, "P3North_E", 38, -17 + GateWidth / 2, -4);
				WallZ(walls, "CorridorW", -20, 38, 48);
				WallZ(walls, "CorridorE", -14, 38, 48);
				WallX(walls, "CorridorEnd", 48, -20, -14);

				// --- Gates ---
				var gates = Child(root.transform, "Gates");
				Gate(gate, gates, "Gate_P1_P2", new Vector3(14, FloorY, 0), 90f, GateP1P2, LevelGate.OpenMode.OnStage,
					DifficultyLevel.level3, null, "Проход на запад открыт", ZoneP2, "Проход за спиной закрылся");
				Gate(gate, gates, "Gate_P2_P3", new Vector3(-17, FloorY, 12), 0f, GateP2P3, LevelGate.OpenMode.WithKey,
					DifficultyLevel.level6, KeyId, "Дверь отперта", ZoneP3, "Дверь за спиной захлопнулась");
				Gate(gate, gates, "Gate_North", new Vector3(-17, FloorY, 38), 0f, GateNorth, LevelGate.OpenMode.OnStage,
					DifficultyLevel.level9, null, "Северный проход открыт", null, null);

				// --- Zones / start / exit marker ---
				var mission = Child(root.transform, "Mission");
				// Platform zones (inset ~1 m from the walls): entering P2 ends the breather and shuts G12; entering P3
				// shuts G23; the ammo fallback drops in the player's zone. The exit zone wins the level.
				Zone(mission, ZoneP1, new Vector3(28, FloorY, 0), new Vector3(26, 2, 22));
				Zone(mission, ZoneP2, new Vector3(-8, FloorY, 0), new Vector3(42, 2, 22));
				Zone(mission, ZoneP3, new Vector3(-17, FloorY, 25), new Vector3(24, 2, 24));
				Zone(mission, ZoneExit, new Vector3(-17, FloorY, 45), new Vector3(5, 2, 5));
				var exitMark = Primitive(PrimitiveType.Cylinder, "ExitMarker", mission, _exitMark,
					new Vector3(-17, FloorY + 0.02f, 45), Quaternion.identity, new Vector3(3.5f, 0.01f, 3.5f));
				exitMark.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

				var start = Child(mission, "PlayerStart");
				start.SetPositionAndRotation(new Vector3(38, FloorY, 0), Quaternion.Euler(0, -90, 0));

				// --- Key (hidden until stage level6) ---
				var keyInstance = (GameObject)PrefabUtility.InstantiatePrefab(key, mission);
				keyInstance.name = "Key_P2";
				keyInstance.transform.position = new Vector3(-8, FloorY, 2.5f);
				var keySo = new SerializedObject(keyInstance.GetComponent<LevelKeyPickup>());
				keySo.FindProperty("_keyId").stringValue = KeyId;
				keySo.FindProperty("_appearOnStage").intValue = (int)DifficultyLevel.level6;
				keySo.ApplyModifiedPropertiesWithoutUndo();
				keyInstance.SetActive(false);

				// --- Stage loot ---
				var loot = Child(root.transform, "StageLoot");
				StageLootPoint(loot, "P1_Start", new Vector3(33, FloorY, -3), DifficultyLevel.level1, 1f,
					Ammo(Caliber.Mm9, 24));
				StageLootPoint(loot, "P1_Wave2", new Vector3(28, FloorY, 0), DifficultyLevel.level2, 1.5f,
					Ammo(Caliber.Mm9, 30), Health(25), Grenade());
				StageLootPoint(loot, "P2_Breather", new Vector3(6, FloorY, 0), DifficultyLevel.level3, 2.2f,
					Weapon("Shotgun"), Ammo(Caliber.Gauge12, 16), Ammo(Caliber.Mm9, 40), Health(30));
				StageLootPoint(loot, "P2_KeyReward", new Vector3(-12, FloorY, -2), DifficultyLevel.level6, 1.8f,
					Ammo(Caliber.Gauge12, 12), Ammo(Caliber.Mm9, 30), Health(30));
				StageLootPoint(loot, "P3_Entry", new Vector3(-17, FloorY, 17), DifficultyLevel.level7, 1.5f,
					Ammo(Caliber.None, 25), Health(25), Grenade());
				StageLootPoint(loot, "P3_LastWave", new Vector3(-17, FloorY, 27), DifficultyLevel.level8, 1.5f,
					Ammo(Caliber.None, 25), Ammo(Caliber.Gauge12, 12), Health(30));

				// Ammo fallback points (tag AdditionalSpawn → AdditionalLootSpawnSystem), 2 per platform; the system
				// picks a free one inside the player's zone.
				var fallbackPoints = Child(root.transform, "AmmoFallbackPoints");
				foreach (var pos in new[]
				         {
					         new Vector3(31, FloorY, -5), new Vector3(24, FloorY, 5),        // P1
					         new Vector3(-1, FloorY, -3), new Vector3(-16, FloorY, 3),       // P2
					         new Vector3(-22, FloorY, 21), new Vector3(-11, FloorY, 30),     // P3
				         })
				{
					var point = Child(fallbackPoints, $"AmmoPoint_{fallbackPoints.childCount + 1}");
					point.position = pos;
					point.gameObject.tag = AdditionalSpawnTag;
				}

				// --- Sewer hatches (+ a spawn point on each: zombies crawl out of the sewers) ---
				var props = Child(root.transform, "Props");
				var spawns = Child(root.transform, "SpawnPoints");
				// All hatches are on P2, mirror-symmetric about its centre lines (x = -8, z = 0): 3 pairs along the hall.
				var p2Hatches = new[]
				{
					new Vector3(-20, FloorY, -6.5f), new Vector3(-20, FloorY, 6.5f),
					new Vector3(-8, FloorY, -8f), new Vector3(-8, FloorY, 8f),
					new Vector3(4, FloorY, -6.5f), new Vector3(4, FloorY, 6.5f),
				};
				int hatchIndex = 0;
				foreach (var pos in p2Hatches)
				{
					var h = (GameObject)PrefabUtility.InstantiatePrefab(hatch, props);
					h.name = $"SewerHatch_{++hatchIndex}";
					h.transform.SetPositionAndRotation(pos, Quaternion.identity);
				}

				// --- Heating: one supply/return loop along the outer walls of P1+P2, through the dividing wall ---
				BuildHeating(root.transform, props);

				// --- Big open pipes (Ø1 m) in the walls of P1 and P3: mob spawns sit in front of them ---
				int outletIndex = 0;
				foreach (var (wall, inward) in Concat2(P1Outlets, P3Outlets))
					Outlet(props, $"OpenPipe_{++outletIndex}", wall, inward);

				// --- Oil slicks on P3 (slow the player and mobs by 25% while on them) ---
				int slickIndex = 0;
				foreach (var (pos, radius, seed, yaw) in P3OilSlicks)
				{
					var slickGo = (GameObject)PrefabUtility.InstantiatePrefab(oilSlick, props);
					slickGo.name = $"OilSlick_{++slickIndex}";
					slickGo.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
					var sso = new SerializedObject(slickGo.GetComponent<OilSlick>());
					sso.FindProperty("_radius").floatValue = radius;
					sso.FindProperty("_seed").intValue = seed;
					sso.ApplyModifiedPropertiesWithoutUndo();
				}

				// --- Crates on the west side of P2 (scene breakables, excluded from the bake → no hole when broken) ---
				var crate = AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath);
				if (crate != null)
				{
					var cratePositions = new[]
					{
						new Vector3(-27.5f, FloorY, 2f), new Vector3(-24.5f, FloorY, -1.5f), new Vector3(-27.5f, FloorY, -4f),
						new Vector3(-27.5f, FloorY, -9.5f), new Vector3(-24f, FloorY, -9.8f),
					};
					for (int i = 0; i < cratePositions.Length; i++)
					{
						var c = (GameObject)PrefabUtility.InstantiatePrefab(crate, props);
						c.name = $"Crate_{i + 1}";
						c.transform.SetPositionAndRotation(cratePositions[i], Quaternion.Euler(0, i * 23f, 0));
						c.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
					}
				}
				else
				{
					Debug.LogWarning($"[KeyLevelBuilder] {CratePrefabPath} not found — no crates.");
				}

				// --- Spawn points ---
				// P1: waves 1-2 (Slow + Fast)
				var p1Waves = new[]
				{
					Mob("Slow", 3, (DifficultyLevel.level1, 7f), (DifficultyLevel.level2, 5f)),
					Mob("Fast", 1, (DifficultyLevel.level1, 9f), (DifficultyLevel.level2, 6f)),
				};
				// Zombies come out of the big open pipes in the walls (spawn point just in front of each mouth).
				foreach (var (wall, inward) in P1Outlets)
					Spawn(spawns, "P1", OutletSpawnPoint(wall, inward), p1Waves);

				// P2: toxic zombies only — wave 1 regular Toxic, wave 2 FastToxic. They crawl out of the stinking hatches
				// (the only spawn points of P2).
				var p2Waves = new[]
				{
					Mob("Toxic", 1, (DifficultyLevel.level4, 5.5f)),
					Mob("FastToxic", 1, (DifficultyLevel.level5, 5.5f)),
				};
				foreach (var pos in p2Hatches)
					Spawn(spawns, "P2", pos, p2Waves);

				// P3: Fast + Fatman, last wave + ToxicHelmet
				var p3Waves = new[]
				{
					Mob("Fast", 4, (DifficultyLevel.level7, 4f), (DifficultyLevel.level8, 3.5f)),
					Mob("Fatman", 1, (DifficultyLevel.level7, 10f), (DifficultyLevel.level8, 9f)),
					Mob("ToxicHelmet", 1, (DifficultyLevel.level8, 9f)),
				};
				foreach (var (wall, inward) in P3Outlets)
					Spawn(spawns, "P3", OutletSpawnPoint(wall, inward), p3Waves);

				// --- LevelRoot wiring ---
				var so = new SerializedObject(levelRoot);
				so.FindProperty("_levelConfig").objectReferenceValue = config;
				var sectors = so.FindProperty("_sectors");
				sectors.arraySize = 1;
				sectors.GetArrayElementAtIndex(0).objectReferenceValue = sector;
				so.FindProperty("_playerStart").objectReferenceValue = start;
				so.ApplyModifiedPropertiesWithoutUndo();

				var saved = PrefabUtility.SaveAsPrefabAsset(root, LevelPrefabPath, out bool ok);
				if (!ok)
					Debug.LogError($"[KeyLevelBuilder] Failed to save {LevelPrefabPath}");
				return saved;
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		// ================================================================== props

		internal static GameObject BuildHatchPrefab()
		{
			string path = $"{PropFolder}/SewerHatch.prefab";
			return SavePrefab(path, root =>
			{
				root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				Primitive(PrimitiveType.Cylinder, "Rim", root.transform, _hatchRim, new Vector3(0, 0.02f, 0), Quaternion.identity,
					new Vector3(1.45f, 0.02f, 1.45f));
				Primitive(PrimitiveType.Cylinder, "Lid", root.transform, _hatch, new Vector3(0, 0.045f, 0), Quaternion.identity,
					new Vector3(1.2f, 0.025f, 1.2f));
				// Cast grate pattern: bars across the lid + a ring.
				for (int i = -2; i <= 2; i++)
					Primitive(PrimitiveType.Cube, $"Bar{i + 2}", root.transform, _hatchRim, new Vector3(i * 0.18f, 0.075f, 0),
						Quaternion.identity, new Vector3(0.05f, 0.02f, Mathf.Sqrt(1f - (i * 0.18f / 0.58f) * (i * 0.18f / 0.58f)) * 1.05f));
				Primitive(PrimitiveType.Cube, "Cross", root.transform, _hatchRim, new Vector3(0, 0.08f, 0),
					Quaternion.identity, new Vector3(1.05f, 0.02f, 0.06f));

				// Stench: the Toxic zombie's green fog (copied with its overrides from ToxicMob.prefab).
				var toxic = AssetDatabase.LoadAssetAtPath<GameObject>(ToxicMobPrefabPath);
				var fogSource = toxic != null ? FindChild(toxic.transform, ToxicFogName) : null;
				if (fogSource != null)
				{
					var fog = Object.Instantiate(fogSource.gameObject, root.transform);
					fog.name = "Stench";
					fog.transform.SetLocalPositionAndRotation(new Vector3(0, 0.15f, 0), Quaternion.Euler(-90f, 0f, 0f));
					fog.transform.localScale = Vector3.one;
				}
				else
				{
					Debug.LogWarning($"[KeyLevelBuilder] '{ToxicFogName}' not found in {ToxicMobPrefabPath} — hatch without stench.");
				}
			});
		}

		internal static GameObject BuildKeyPrefab()
		{
			string path = $"{PropFolder}/KeyPickup.prefab";
			return SavePrefab(path, root =>
			{
				root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				var pickup = root.AddComponent<LevelKeyPickup>();

				var visual = new GameObject("Visual").transform;
				visual.SetParent(root.transform, false);
				visual.localPosition = new Vector3(0, 0.7f, 0);
				// A big old-style key lying flat (readable from the top-down camera): ring bow, shaft, two teeth.
				var bow = new GameObject("Bow").transform;
				bow.SetParent(visual, false);
				bow.localPosition = new Vector3(0, 0, -0.45f);
				for (int i = 0; i < 8; i++)
				{
					float a = i * Mathf.PI / 4f;
					Primitive(PrimitiveType.Cube, $"Ring{i}", bow, _keyGold, new Vector3(Mathf.Sin(a) * 0.2f, 0, Mathf.Cos(a) * 0.2f),
						Quaternion.Euler(0, a * Mathf.Rad2Deg, 0), new Vector3(0.17f, 0.08f, 0.07f));
				}
				Primitive(PrimitiveType.Cube, "Shaft", visual, _keyGold, new Vector3(0, 0, 0.12f), Quaternion.identity,
					new Vector3(0.09f, 0.08f, 0.75f));
				Primitive(PrimitiveType.Cube, "Tooth1", visual, _keyGold, new Vector3(0.1f, 0, 0.36f), Quaternion.identity,
					new Vector3(0.14f, 0.08f, 0.08f));
				Primitive(PrimitiveType.Cube, "Tooth2", visual, _keyGold, new Vector3(0.08f, 0, 0.47f), Quaternion.identity,
					new Vector3(0.1f, 0.08f, 0.07f));

				Primitive(PrimitiveType.Cylinder, "Glow", root.transform, _keyGlow, new Vector3(0, 0.02f, 0), Quaternion.identity,
					new Vector3(1.6f, 0.01f, 1.6f));
				var lightGo = new GameObject("Light");
				lightGo.transform.SetParent(root.transform, false);
				lightGo.transform.localPosition = new Vector3(0, 1.5f, 0);
				var light = lightGo.AddComponent<Light>();
				light.type = LightType.Point;
				light.color = new Color(1f, 0.8f, 0.3f);
				light.range = 5f;
				light.intensity = 3f;
				light.shadows = LightShadows.None;

				var so = new SerializedObject(pickup);
				so.FindProperty("_visual").objectReferenceValue = visual;
				so.ApplyModifiedPropertiesWithoutUndo();
			});
		}

		internal static GameObject BuildGatePrefab()
		{
			string path = $"{PropFolder}/LevelGate.prefab";
			return SavePrefab(path, root =>
			{
				// The closed panel must not cut the baked navmesh (the carving obstacle does that, and can be undone).
				root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				var gate = root.AddComponent<LevelGate>();

				var obstacle = root.AddComponent<NavMeshObstacle>();
				obstacle.shape = NavMeshObstacleShape.Box;
				obstacle.center = new Vector3(0, 1f, 0);
				obstacle.size = new Vector3(GateWidth + 0.4f, 2f, 1.4f);
				obstacle.carving = true;
				obstacle.carveOnlyStationary = false;

				var panel = new GameObject("Panel").transform;
				panel.SetParent(root.transform, false);
				var door = Primitive(PrimitiveType.Cube, "Door", panel, _gate, new Vector3(0, WallHeight / 2f, 0),
					Quaternion.identity, new Vector3(GateWidth, WallHeight, 0.45f));
				// The closed panel stops bullets; the panel hides itself when fully open → the blocker goes with it.
				SparkOnHit(door.AddComponent<BulletBlocker>());
				// Hazard stripes along the top and bottom (both faces).
				for (int i = 0; i < 7; i++)
				{
					float x = -GateWidth / 2f + 0.36f + i * 0.72f;
					var mat = i % 2 == 0 ? _hazard : _hazardDark;
					Primitive(PrimitiveType.Cube, $"StripeTop{i}", panel, mat, new Vector3(x, WallHeight - 0.2f, 0),
						Quaternion.identity, new Vector3(0.72f, 0.3f, 0.5f));
					Primitive(PrimitiveType.Cube, $"StripeBottom{i}", panel, mat, new Vector3(x, 0.2f, 0),
						Quaternion.identity, new Vector3(0.72f, 0.3f, 0.5f));
				}
				Primitive(PrimitiveType.Cube, "TopCap", panel, _wallCap, new Vector3(0, WallHeight + 0.03f, 0), Quaternion.identity,
					new Vector3(GateWidth, 0.06f, 0.5f));

				var lamp = Primitive(PrimitiveType.Cube, "LockedLamp", root.transform, _lampRed,
					new Vector3(0, WallHeight + 0.35f, 0), Quaternion.identity, new Vector3(0.6f, 0.25f, 0.6f));
				lamp.SetActive(false);

				var so = new SerializedObject(gate);
				so.FindProperty("_panel").objectReferenceValue = panel;
				so.FindProperty("_obstacle").objectReferenceValue = obstacle;
				so.FindProperty("_lockedIndicator").objectReferenceValue = lamp;
				so.FindProperty("_sinkDepth").floatValue = WallHeight + 0.2f;
				so.ApplyModifiedPropertiesWithoutUndo();
			});
		}

		internal static void Gate(GameObject prefab, Transform parent, string name, Vector3 position, float yaw, string id,
			LevelGate.OpenMode mode, DifficultyLevel stage, string requiredKey, string message, string closeZone,
			string closeMessage)
		{
			var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
			go.name = name;
			go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
			var so = new SerializedObject(go.GetComponent<LevelGate>());
			so.FindProperty("_id").stringValue = id;
			so.FindProperty("_openMode").intValue = (int)mode;
			so.FindProperty("_openOnStage").intValue = (int)stage;
			so.FindProperty("_requiredKey").stringValue = requiredKey ?? string.Empty;
			so.FindProperty("_openMessage").stringValue = message;
			so.FindProperty("_closeWhenPlayerInZone").stringValue = closeZone ?? string.Empty;
			so.FindProperty("_closeMessage").stringValue = closeMessage ?? string.Empty;
			so.ApplyModifiedPropertiesWithoutUndo();
			// Key gates show a red lamp until unlocked.
			var lamp = FindChild(go.transform, "LockedLamp");
			if (lamp != null)
				lamp.gameObject.SetActive(mode == LevelGate.OpenMode.WithKey);
		}

		// ================================================================== geometry helpers

		internal const string FloorMeshPath = "Assets/_Prefabs/Levels/Meshes/Level_02_Key_Floor.asset";

		/// <summary>
		/// Floor top quads (y = FloorY) + a 0.5 m skirt on the outer edges, UV = world XZ. Rects are (xMin, zMin, w, h).
		/// Rewritten in place on re-run (same guid).
		/// </summary>
		internal static Mesh BuildFloorMesh(Rect[] rects, string meshPath = FloorMeshPath)
		{
			var verts = new List<Vector3>();
			var uvs = new List<Vector2>();
			var normals = new List<Vector3>();
			var tris = new List<int>();
			const float skirt = 0.5f;

			void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
			{
				int i = verts.Count;
				verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
				uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
				for (int k = 0; k < 4; k++) normals.Add(n);
				// a-b-c-d clockwise seen from the normal side
				tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
				tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
			}

			foreach (var r in rects)
			{
				float x0 = r.xMin, x1 = r.xMax, z0 = r.yMin, z1 = r.yMax, y = FloorY, yb = FloorY - skirt;
				// Top (seen from above: x0z0 → x0z1 → x1z1 → x1z0 is clockwise).
				Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up,
					new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0));
				// Skirts (hidden by the walls mostly; keep the slab looking solid at the exit/gaps).
				Quad(new Vector3(x0, yb, z0), new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, yb, z0), Vector3.back,
					new Vector2(x0, 0), new Vector2(x0, skirt), new Vector2(x1, skirt), new Vector2(x1, 0));
				Quad(new Vector3(x1, yb, z1), new Vector3(x1, y, z1), new Vector3(x0, y, z1), new Vector3(x0, yb, z1), Vector3.forward,
					new Vector2(x1, 0), new Vector2(x1, skirt), new Vector2(x0, skirt), new Vector2(x0, 0));
				Quad(new Vector3(x0, yb, z1), new Vector3(x0, y, z1), new Vector3(x0, y, z0), new Vector3(x0, yb, z0), Vector3.left,
					new Vector2(z1, 0), new Vector2(z1, skirt), new Vector2(z0, skirt), new Vector2(z0, 0));
				Quad(new Vector3(x1, yb, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x1, yb, z1), Vector3.right,
					new Vector2(z0, 0), new Vector2(z0, skirt), new Vector2(z1, skirt), new Vector2(z1, 0));
			}

			EnsureFolder(Path.GetDirectoryName(meshPath).Replace('\\', '/'));
			var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
			bool created = mesh == null;
			if (created)
				mesh = new Mesh { name = Path.GetFileNameWithoutExtension(meshPath) };
			mesh.Clear();
			mesh.SetVertices(verts);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			mesh.RecalculateTangents();
			if (created)
				AssetDatabase.CreateAsset(mesh, meshPath);
			else
				EditorUtility.SetDirty(mesh);
			return mesh;
		}

		// Wall along X at z, from x0 to x1 (centred on the boundary line).
		internal static void WallX(Transform parent, string name, float z, float x0, float x1)
		{
			float len = x1 - x0 + WallThickness;
			WallBox(parent, name, new Vector3((x0 + x1) / 2f, 0, z), new Vector3(len, WallHeight, WallThickness));
		}

		// Wall along Z at x, from z0 to z1.
		internal static void WallZ(Transform parent, string name, float x, float z0, float z1)
		{
			float len = z1 - z0 + WallThickness;
			WallBox(parent, name, new Vector3(x, 0, (z0 + z1) / 2f), new Vector3(WallThickness, WallHeight, len));
		}

		internal static void WallBox(Transform parent, string name, Vector3 center, Vector3 size)
		{
			var go = Primitive(PrimitiveType.Cube, name, parent, _wall,
				new Vector3(center.x, FloorY + size.y / 2f, center.z), Quaternion.identity, size);
			SparkOnHit(go.AddComponent<BulletBlocker>()); // unit-cube box in local space = the scaled wall
			Primitive(PrimitiveType.Cube, "Cap", go.transform, _wallCap, new Vector3(0, 0.5f + 0.03f / size.y, 0),
				Quaternion.identity, new Vector3(1.04f, 0.06f / size.y, 1.04f));
		}

		internal static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat,
			Vector3 localPos, Quaternion localRot, Vector3 scale)
		{
			var go = GameObject.CreatePrimitive(type);
			go.name = name;
			Object.DestroyImmediate(go.GetComponent<Collider>());
			go.transform.SetParent(parent, false);
			go.transform.SetLocalPositionAndRotation(localPos, localRot);
			// Unity's cylinder is 2 units tall: halve Y so 'scale.y' is the real height.
			go.transform.localScale = type == PrimitiveType.Cylinder ? new Vector3(scale.x, scale.y * 0.5f, scale.z) : scale;
			var renderer = go.GetComponent<MeshRenderer>();
			if (mat != null)
				renderer.sharedMaterial = mat;
			renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
			return go;
		}

		internal static Transform Child(Transform parent, string name)
		{
			var t = new GameObject(name).transform;
			t.SetParent(parent, false);
			return t;
		}

		internal static void Zone(Transform parent, string id, Vector3 position, Vector3 size)
		{
			var t = Child(parent, $"Zone_{id}");
			t.position = position;
			var zone = t.gameObject.AddComponent<LevelZone>();
			var so = new SerializedObject(zone);
			so.FindProperty("_id").stringValue = id;
			so.FindProperty("_size").vector3Value = size;
			so.ApplyModifiedPropertiesWithoutUndo();
		}

		// ================================================================== spawn / loot helpers

		internal readonly struct MobEntry
		{
			public readonly string Id;
			public readonly float Weight;
			public readonly (DifficultyLevel level, float cooldown)[] Presets;

			public MobEntry(string id, float weight, (DifficultyLevel, float)[] presets)
			{
				Id = id;
				Weight = weight;
				Presets = presets;
			}
		}

		internal static MobEntry Mob(string id, float weight, params (DifficultyLevel, float)[] presets) => new(id, weight, presets);

		internal static int _spawnCounter;

		internal static void Spawn(Transform parent, string area, Vector3 position, MobEntry[] mobs)
		{
			var t = Child(parent, $"Spawn_{area}_{++_spawnCounter}");
			t.position = position;
			var point = t.gameObject.AddComponent<SpawnPoint>();
			var so = new SerializedObject(point);
			var configs = so.FindProperty("_spawnConfigs");
			configs.arraySize = mobs.Length;
			for (int i = 0; i < mobs.Length; i++)
			{
				var c = configs.GetArrayElementAtIndex(i);
				c.FindPropertyRelative("_mobId").stringValue = mobs[i].Id;
				c.FindPropertyRelative("_weight").floatValue = mobs[i].Weight;
				var presets = c.FindPropertyRelative("_spawnPresets");
				presets.arraySize = mobs[i].Presets.Length;
				for (int j = 0; j < mobs[i].Presets.Length; j++)
				{
					var p = presets.GetArrayElementAtIndex(j);
					p.FindPropertyRelative("DifficultyLevel").intValue = (int)mobs[i].Presets[j].level;
					p.FindPropertyRelative("Cooldown").floatValue = mobs[i].Presets[j].cooldown;
				}
			}
			so.ApplyModifiedPropertiesWithoutUndo();
		}

		internal static void StageLootPoint(Transform parent, string name, Vector3 position, DifficultyLevel stage,
			float spread, params PossibleLoot[] loot)
		{
			var t = Child(parent, $"Loot_{name}");
			t.position = position;
			var stageLoot = t.gameObject.AddComponent<StageLoot>();
			SetField(stageLoot, "_onStage", stage);
			SetField(stageLoot, "_spread", spread);
			SetField(stageLoot, "_loot", loot);
			EditorUtility.SetDirty(stageLoot);
		}

		internal static PossibleLoot Ammo(Caliber caliber, int count) =>
			new() { LootType = LootType.Ammo, AmmoCaliber = caliber, Count = count, Chance = 1f, Id = string.Empty };
		internal static PossibleLoot Health(int amount) =>
			new() { LootType = LootType.Health, Count = amount, Chance = 1f, Id = string.Empty };
		internal static PossibleLoot Weapon(string id) =>
			new() { LootType = LootType.Weapon, Id = id, Count = 1, Chance = 1f };
		internal static PossibleLoot Grenade() =>
			new() { LootType = LootType.Grenade, Id = string.Empty, Count = 1, Chance = 1f };

		internal static IEnumerable<Vector3> Concat(params Vector3[][] arrays)
		{
			foreach (var a in arrays)
				foreach (var v in a)
					yield return v;
		}

		// ================================================================== conditions / definition

		internal static SmartConditionWrapper Condition<T>(string name, string field, string value) where T : SmartConditionWrapper =>
			Condition<T>(name, field == null ? null : prototype =>
			{
				var prop = prototype.FindPropertyRelative(field);
				if (prop != null)
					prop.stringValue = value;
				else
					Debug.LogError($"[KeyLevelBuilder] {name}: field '{field}' not found on the prototype.");
			});

		internal static SmartConditionWrapper Condition<T>(string name, System.Action<SerializedProperty> setup)
			where T : SmartConditionWrapper
		{
			string path = $"{ConditionFolder}/{name}.asset";
			var asset = AssetDatabase.LoadAssetAtPath<T>(path);
			if (asset == null)
			{
				asset = ScriptableObject.CreateInstance<T>();
				AssetDatabase.CreateAsset(asset, path);
			}

			var so = new SerializedObject(asset);
			var prototype = so.FindProperty("_prototype");
			if (prototype == null)
			{
				Debug.LogError($"[KeyLevelBuilder] {name}: '_prototype' isn't serialized — set it by hand.");
				return asset;
			}
			setup?.Invoke(prototype);
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(asset);
			return asset;
		}

		internal static void BuildDefinition(GameObject levelPrefab)
		{
			var definition = LoadOrCreate<LevelDefinition>(DefinitionPath);
			var so = new SerializedObject(definition);
			so.FindProperty("_levelPrefab").objectReferenceValue = levelPrefab;
			so.FindProperty("_displayName").stringValue = "Запертая дверь";
			var first = AssetDatabase.LoadAssetAtPath<LevelDefinition>(FirstDefinitionPath);
			if (first != null && so.FindProperty("_icon").objectReferenceValue == null)
				so.FindProperty("_icon").objectReferenceValue = first.Icon;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(definition);

			// 2nd campaign level (early, but not the very first).
			var library = AssetDatabase.LoadAssetAtPath<LevelLibrary>(CampaignLibraryPath);
			if (library == null)
			{
				Debug.LogWarning($"[KeyLevelBuilder] {CampaignLibraryPath} not found — add {DefinitionPath} by hand.");
				return;
			}
			var lso = new SerializedObject(library);
			var levels = lso.FindProperty("_levels");
			for (int i = 0; i < levels.arraySize; i++)
				if (levels.GetArrayElementAtIndex(i).objectReferenceValue == definition)
					return;
			int index = Mathf.Min(1, levels.arraySize);
			levels.InsertArrayElementAtIndex(index);
			levels.GetArrayElementAtIndex(index).objectReferenceValue = definition;
			lso.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(library);
		}

		// ================================================================== materials

		internal static void CreateMaterials()
		{
			_wall = Mat("Level_Wall", new Color(0.46f, 0.45f, 0.42f), 0f, 0.15f);
			_wallCap = Mat("Level_WallCap", new Color(0.30f, 0.30f, 0.29f), 0f, 0.2f);
			_gate = Mat("Level_GateSteel", new Color(0.33f, 0.36f, 0.38f), 0.6f, 0.4f);
			_hazard = Mat("Level_HazardYellow", new Color(1f, 0.78f, 0.1f), 0f, 0.3f);
			_hazardDark = Mat("Level_HazardBlack", new Color(0.06f, 0.06f, 0.06f), 0f, 0.3f);
			_hatch = Mat("Level_HatchIron", new Color(0.16f, 0.17f, 0.16f), 0.7f, 0.35f);
			_hatchRim = Mat("Level_HatchRim", new Color(0.24f, 0.24f, 0.22f), 0.6f, 0.3f);
			_keyGold = Mat("Level_KeyGold", new Color(1f, 0.78f, 0.25f), 1f, 0.7f, new Color(0.9f, 0.6f, 0.1f));
			_keyGlow = Mat("Level_KeyGlow", new Color(1f, 0.85f, 0.4f), 0f, 0f, new Color(1.2f, 0.85f, 0.25f));
			_lampRed = Mat("Level_LampRed", new Color(1f, 0.1f, 0.1f), 0f, 0.5f, new Color(2f, 0.1f, 0.1f));
			_exitMark = Mat("Level_ExitMarker", new Color(0.2f, 1f, 0.4f), 0f, 0.2f, new Color(0.2f, 1.2f, 0.4f));
			_pipeSupply = Mat("Level_PipeSupply", new Color(0.50f, 0.17f, 0.10f), 0.45f, 0.35f);
			_pipeReturn = Mat("Level_PipeReturn", new Color(0.36f, 0.38f, 0.40f), 0.65f, 0.4f);
			_boiler = Mat("Level_Boiler", new Color(0.20f, 0.29f, 0.24f), 0.5f, 0.35f);
			_valve = Mat("Level_Valve", new Color(0.80f, 0.10f, 0.08f), 0.3f, 0.4f);
			_outlet = Mat("Level_OutletPipe", new Color(0.30f, 0.27f, 0.23f), 0.55f, 0.25f);
			_outletDark = Mat("Level_OutletDark", new Color(0.01f, 0.01f, 0.01f), 0f, 0f);
			_sludge = Mat("Level_Sludge", new Color(0.12f, 0.14f, 0.08f), 0f, 0.75f);
			_oil = Mat("Level_Oil", new Color(0.015f, 0.015f, 0.018f), 0.2f, 0.92f);
		}

		// ================================================================== wall spark effect

		/// <summary>
		/// _Prefabs/Effects/WallSpark.prefab: SceneEffect wrapper around Epic Toon FX "SparkExplosion" scaled down to a
		/// bullet ricochet, registered in FxHolder as <see cref="WallSparkId"/> (pooled by EffectsSystem, prewarmed by
		/// PoolPrewarmer like every FxHolder entry).
		/// </summary>
		internal static void BuildWallSparkEffect()
		{
			var source = AssetDatabase.LoadAssetAtPath<GameObject>(SparkSourcePath);
			if (source == null)
			{
				Debug.LogWarning($"[KeyLevelBuilder] {SparkSourcePath} not found — walls without impact sparks.");
				return;
			}
			EnsureFolder(Path.GetDirectoryName(WallSparkPrefabPath).Replace('\\', '/'));
			var prefab = SavePrefab(WallSparkPrefabPath, root =>
			{
				var effect = root.AddComponent<SceneEffect>();
				var sparks = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
				sparks.name = "Sparks";
				// ETFX systems use Hierarchy scaling: one scale shrinks size, speed and spread together.
				sparks.transform.localScale = Vector3.one * WallSparkScale;
				var so = new SerializedObject(effect);
				so.FindProperty("_particles").objectReferenceValue = sparks.GetComponent<ParticleSystem>();
				so.ApplyModifiedPropertiesWithoutUndo();
			});
			if (prefab == null)
				return;

			var holder = AssetDatabase.LoadAssetAtPath<EffectsHolder>(FxHolderPath);
			if (holder == null)
			{
				Debug.LogWarning($"[KeyLevelBuilder] {FxHolderPath} not found — add '{WallSparkId}' by hand.");
				return;
			}
			var hso = new SerializedObject(holder);
			var wrappers = hso.FindProperty("_fxWrappers");
			SerializedProperty entry = null;
			for (int i = 0; i < wrappers.arraySize; i++)
				if (wrappers.GetArrayElementAtIndex(i).FindPropertyRelative("Id").stringValue == WallSparkId)
					entry = wrappers.GetArrayElementAtIndex(i);
			if (entry == null)
			{
				wrappers.arraySize++;
				entry = wrappers.GetArrayElementAtIndex(wrappers.arraySize - 1);
			}
			entry.FindPropertyRelative("Id").stringValue = WallSparkId;
			entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab.GetComponent<SceneEffect>();
			entry.FindPropertyRelative("HasDuration").boolValue = true;
			entry.FindPropertyRelative("Duration").floatValue = 0.8f;
			entry.FindPropertyRelative("IsChild").boolValue = false;
			hso.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(holder);
		}

		internal static void SparkOnHit(BulletBlocker blocker)
		{
			var so = new SerializedObject(blocker);
			so.FindProperty("_impactEffectId").stringValue = WallSparkId;
			so.ApplyModifiedPropertiesWithoutUndo();
		}

		// ================================================================== open pipes / oil slicks

		internal const float OutletRadius = 0.5f;      // Ø1 m
		internal const float OutletLength = 1.2f;      // from the wall centre line to the mouth
		internal const float OutletSpawnDistance = 2.4f;

		// (point on the wall centre line, inward normal). Floor-level, mouth facing into the platform.
		internal static readonly (Vector3, Vector3)[] P1Outlets =
		{
			(new Vector3(22, FloorY, 12), Vector3.back), (new Vector3(34, FloorY, 12), Vector3.back),
			(new Vector3(22, FloorY, -12), Vector3.forward), (new Vector3(34, FloorY, -12), Vector3.forward),
			(new Vector3(42, FloorY, -7), Vector3.left),
		};

		internal static readonly (Vector3, Vector3)[] P3Outlets =
		{
			(new Vector3(-30, FloorY, 20), Vector3.right), (new Vector3(-30, FloorY, 31), Vector3.right),
			(new Vector3(-4, FloorY, 20), Vector3.left), (new Vector3(-4, FloorY, 31), Vector3.left),
			(new Vector3(-26, FloorY, 38), Vector3.back),
		};

		// (centre, mean radius, shape seed, yaw)
		internal static readonly (Vector3, float, int, float)[] P3OilSlicks =
		{
			(new Vector3(-24, FloorY + 0.012f, 27), 1.9f, 11, 0f),
			(new Vector3(-10, FloorY + 0.012f, 23.5f), 1.6f, 23, 40f),
			(new Vector3(-14, FloorY + 0.012f, 32.5f), 2.1f, 37, 115f),
		};

		internal static Vector3 OutletSpawnPoint(Vector3 wall, Vector3 inward) => wall + inward * OutletSpawnDistance;

		internal static IEnumerable<(Vector3, Vector3)> Concat2(params (Vector3, Vector3)[][] arrays)
		{
			foreach (var a in arrays)
				foreach (var v in a)
					yield return v;
		}

		/// <summary>
		/// A big open drain pipe (Ø1 m) sticking out of a wall at floor level: shell, lip, black mouth and a slimy
		/// puddle in front. Solid for the navmesh bake (no NavMeshModifier), so agents walk around the shell; the spawn
		/// point sits <see cref="OutletSpawnDistance"/> from the wall, in front of the mouth.
		/// </summary>
		internal static void Outlet(Transform parent, string name, Vector3 wall, Vector3 inward)
		{
			var root = Child(parent, name);
			root.SetPositionAndRotation(wall, Quaternion.LookRotation(inward));
			var along = Quaternion.Euler(90, 0, 0); // cylinder axis → local +Z (out of the wall)
			float y = OutletRadius;
			Primitive(PrimitiveType.Cylinder, "Shell", root, _outlet, new Vector3(0, y, OutletLength * 0.5f), along,
				new Vector3(OutletRadius * 2f, OutletLength, OutletRadius * 2f));
			Primitive(PrimitiveType.Cylinder, "Lip", root, _outlet, new Vector3(0, y, OutletLength - 0.05f), along,
				new Vector3(OutletRadius * 2.2f, 0.1f, OutletRadius * 2.2f));
			Primitive(PrimitiveType.Cylinder, "Mouth", root, _outletDark, new Vector3(0, y, OutletLength + 0.002f), along,
				new Vector3(OutletRadius * 1.7f, 0.01f, OutletRadius * 1.7f));
			var puddle = Primitive(PrimitiveType.Cylinder, "Sludge", root, _sludge, new Vector3(0, 0.008f, OutletLength + 0.6f),
				Quaternion.identity, new Vector3(1.1f, 0.005f, 1.6f));
			puddle.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
				r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
		}

		/// <summary>_Prefabs/Props/OilSlick.prefab: OilSlick (procedural blob mesh, slow zone) + glossy black material.</summary>
		internal static GameObject BuildOilSlickPrefab()
		{
			string path = $"{PropFolder}/OilSlick.prefab";
			return SavePrefab(path, root =>
			{
				root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				root.AddComponent<MeshFilter>();
				var renderer = root.AddComponent<MeshRenderer>();
				renderer.sharedMaterial = _oil;
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
				var slick = root.AddComponent<OilSlick>();
				// ModifierZone: −25% speed while standing on the slick.
				SetField(slick, "_linger", 0.25f);
				SetField(slick, "_modifiers", new Modifier[]
				{
					new SpeedModifier { Id = ModifierConstants.SpeedOilSlick, Source = BuffSource.Environment, Value = 0.75f },
				});
			});
		}

		// ================================================================== heating pipes (P1 + P2 decor)

		// The outer walls of P1+P2 form one rectangle; the heating loop runs inside it along the walls.
		internal const float HeatX0 = -30f, HeatX1 = 42f, HeatZ0 = -12f, HeatZ1 = 12f;
		internal const float DividerX = 14f;           // P1/P2 wall: the loop passes through it (north and south)
		internal const float DoorG23X = -17f;          // key gate in the north wall: the pipes bridge over it
		internal const float PipeRadius = 0.085f;

		internal readonly struct PipeLine
		{
			public readonly float Inset, Height, BridgeHeight;
			public readonly Material Material;

			public PipeLine(float inset, float height, float bridgeHeight, Material material)
			{
				Inset = inset;
				Height = height;
				BridgeHeight = bridgeHeight;
				Material = material;
			}
		}

		/// <summary>
		/// Decor only (no colliders, excluded from the bake): a supply + return pipe loop around P1 and P2 — one heating
		/// circuit fed by a boiler on the P1 east wall, passing through the P1/P2 wall (collars on both sides) and
		/// bridging over the key gate. Kept deliberately simple: plain runs + elbows, one valve at the boiler.
		/// The boiler is solid (kept in the bake, so the navmesh goes around it).
		/// </summary>
		internal static void BuildHeating(Transform levelRoot, Transform props)
		{
			var heating = Child(levelRoot, "Heating");
			heating.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

			var supply = new PipeLine(0.62f, 1.55f, 2.95f, _pipeSupply);
			var ret = new PipeLine(0.88f, 1.10f, 2.70f, _pipeReturn);
			foreach (var line in new[] { supply, ret })
			{
				var lineRoot = Child(heating, line.Material == _pipeSupply ? "Supply" : "Return");
				var points = LoopPoints(line);
				for (int i = 0; i < points.Count; i++)
				{
					var a = points[i];
					var b = points[(i + 1) % points.Count];
					PipeSegment(lineRoot, a, b, PipeRadius, line.Material);
					Joint(lineRoot, a, PipeRadius, line.Material);
				}
				// Collars where the loop goes through the P1/P2 wall.
				foreach (float z in new[] { HeatZ1 - line.Inset, HeatZ0 + line.Inset })
					foreach (float side in new[] { -1f, 1f })
						Primitive(PrimitiveType.Cylinder, "WallCollar", lineRoot, _hatchRim,
							new Vector3(DividerX + side * (WallThickness * 0.5f + 0.03f), FloorY + line.Height, z),
							Quaternion.Euler(0, 0, 90), new Vector3(PipeRadius * 3.2f, 0.06f, PipeRadius * 3.2f));
			}

			// One valve on the supply line next to the boiler.
			Valve(heating, new Vector3(HeatX1 - supply.Inset, FloorY + supply.Height, 7.5f), Vector3.left);

			Boiler(props, new Vector3(HeatX1 - 1.15f, FloorY, 5f));
		}

		// Closed loop (clockwise from the NE corner) inset from the outer walls; on the north wall it hops over the
		// key gate (riser → bridge → riser).
		internal static List<Vector3> LoopPoints(PipeLine line)
		{
			float y = FloorY + line.Height, yb = FloorY + line.BridgeHeight, d = line.Inset;
			float x0 = HeatX0 + d, x1 = HeatX1 - d, z0 = HeatZ0 + d, z1 = HeatZ1 - d;
			float bridgeE = DoorG23X + GateWidth * 0.5f + 0.7f, bridgeW = DoorG23X - GateWidth * 0.5f - 0.7f;
			return new List<Vector3>
			{
				new(x1, y, z1),
				new(bridgeE, y, z1), new(bridgeE, yb, z1), new(bridgeW, yb, z1), new(bridgeW, y, z1),
				new(x0, y, z1),
				new(x0, y, z0),
				new(x1, y, z0),
			};
		}

		internal static void PipeSegment(Transform parent, Vector3 a, Vector3 b, float radius, Material mat)
		{
			Vector3 d = b - a;
			if (d.sqrMagnitude < 1e-4f)
				return;
			Primitive(PrimitiveType.Cylinder, "Pipe", parent, mat, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d),
				new Vector3(radius * 2f, d.magnitude, radius * 2f));
		}

		internal static void Joint(Transform parent, Vector3 p, float radius, Material mat) =>
			Primitive(PrimitiveType.Sphere, "Elbow", parent, mat, p, Quaternion.identity, Vector3.one * radius * 2.3f);

		// Hand wheel on a pipe: hub + rim (8 short bars) + 2 spokes, facing 'facing' (away from the wall).
		internal static void Valve(Transform parent, Vector3 position, Vector3 facing, float scale = 1f)
		{
			var root = Child(parent, "Valve");
			root.SetPositionAndRotation(position + facing * (0.14f * scale), Quaternion.LookRotation(facing));
			root.localScale = Vector3.one * scale;
			Primitive(PrimitiveType.Cylinder, "Stem", root, _hatchRim, new Vector3(0, 0, -0.07f), Quaternion.Euler(90, 0, 0),
				new Vector3(0.04f, 0.14f, 0.04f));
			Primitive(PrimitiveType.Cylinder, "Hub", root, _valve, Vector3.zero, Quaternion.Euler(90, 0, 0),
				new Vector3(0.07f, 0.04f, 0.07f));
			const float r = 0.13f;
			for (int i = 0; i < 8; i++)
			{
				float a = i * Mathf.PI / 4f;
				Primitive(PrimitiveType.Cube, "Rim", root, _valve, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0),
					Quaternion.Euler(0, 0, a * Mathf.Rad2Deg + 90f), new Vector3(0.105f, 0.03f, 0.03f));
			}
			Primitive(PrimitiveType.Cube, "Spoke1", root, _valve, Vector3.zero, Quaternion.identity, new Vector3(2f * r, 0.025f, 0.025f));
			Primitive(PrimitiveType.Cube, "Spoke2", root, _valve, Vector3.zero, Quaternion.identity, new Vector3(0.025f, 2f * r, 0.025f));
		}

		// Upright boiler on the P1 east wall: tank, dome, base, flue to the wall. The loop's east run
		// passes through it. Solid for the navmesh bake (no NavMeshModifier) → mobs and the player walk around it.
		internal static void Boiler(Transform parent, Vector3 floorPos)
		{
			var root = Child(parent, "Boiler");
			root.position = floorPos;
			const float radius = 0.75f, height = 2.0f;
			Primitive(PrimitiveType.Cube, "Base", root, _hatchRim, new Vector3(0, 0.08f, 0), Quaternion.identity,
				new Vector3(radius * 2.1f, 0.16f, radius * 2.1f));
			Primitive(PrimitiveType.Cylinder, "Tank", root, _boiler, new Vector3(0, 0.16f + height * 0.5f, 0), Quaternion.identity,
				new Vector3(radius * 2f, height, radius * 2f));
			Primitive(PrimitiveType.Sphere, "Dome", root, _boiler, new Vector3(0, 0.16f + height, 0), Quaternion.identity,
				new Vector3(radius * 2f, radius * 0.9f, radius * 2f));
			foreach (float h in new[] { 0.5f, 1.5f })
				Primitive(PrimitiveType.Cylinder, "Band", root, _hatchRim, new Vector3(0, 0.16f + h, 0), Quaternion.identity,
					new Vector3(radius * 2.06f, 0.06f, radius * 2.06f));
			// Flue into the wall.
			Primitive(PrimitiveType.Cylinder, "Flue", root, _pipeReturn, new Vector3(0.55f, 2.35f, 0), Quaternion.Euler(0, 0, 90),
				new Vector3(0.22f, 0.9f, 0.22f));
		}

		internal static Material Mat(string name, Color color, float metallic, float smoothness, Color? emission = null)
		{
			string path = $"{MaterialFolder}/{name}.mat";
			var material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material != null)
				return material;
			material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
			material.SetColor("_BaseColor", color);
			material.SetColor("_Color", color);
			material.SetFloat("_Metallic", metallic);
			material.SetFloat("_Smoothness", smoothness);
			if (emission.HasValue)
			{
				material.EnableKeyword("_EMISSION");
				material.SetColor("_EmissionColor", emission.Value);
				material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
			}
			material.enableInstancing = true;
			AssetDatabase.CreateAsset(material, path);
			return material;
		}

		// ================================================================== generic helpers

		internal static GameObject SavePrefab(string path, System.Action<GameObject> build)
		{
			var root = new GameObject(Path.GetFileNameWithoutExtension(path));
			try
			{
				build(root);
				var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
				if (!ok)
					Debug.LogError($"[KeyLevelBuilder] Failed to save {path}");
				return saved;
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		internal static T LoadOrCreate<T>(string path) where T : ScriptableObject
		{
			var asset = AssetDatabase.LoadAssetAtPath<T>(path);
			if (asset != null)
				return asset;
			EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
			asset = ScriptableObject.CreateInstance<T>();
			AssetDatabase.CreateAsset(asset, path);
			return asset;
		}

		internal static void SetField(object target, string field, object value)
		{
			var type = target.GetType();
			FieldInfo info = null;
			while (type != null && info == null)
			{
				info = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				type = type.BaseType;
			}
			if (info == null)
			{
				Debug.LogError($"[KeyLevelBuilder] Field '{field}' not found on {target.GetType().Name}");
				return;
			}
			info.SetValue(target, value);
		}

		internal static Transform FindChild(Transform root, string name)
		{
			for (int i = 0; i < root.childCount; i++)
			{
				var child = root.GetChild(i);
				if (child.name == name)
					return child;
				var found = FindChild(child, name);
				if (found != null)
					return found;
			}
			return null;
		}

		internal static void EnsureFolder(string path)
		{
			if (AssetDatabase.IsValidFolder(path))
				return;
			string parent = Path.GetDirectoryName(path).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
		}
	}
}
