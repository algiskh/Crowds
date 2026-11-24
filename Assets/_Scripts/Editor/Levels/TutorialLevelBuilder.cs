using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using static Crowds.EditorTools.KeyLevelBuilder;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Tutorial level "Учебный тоннель" (Level_00_Tutorial) — Tools ▸ Crowds ▸ Build Tutorial Level. A straight service
	/// tunnel (10 m wide, runs toward −X = up the screen) split by gates into lesson rooms. Each lesson is one difficulty
	/// stage: its objective line (ObjectiveBanner, {Action} tokens → current keys) says what to do, scene arrows
	/// (TutorialArrow: floor chevrons + floating markers) show where, and the stage ends when it is done:
	///   tutorial  S0 — walk to the first gate (PlayerInZone)
	///   level1    S1 — shoot 3 Slow zombies out of the wall pipes (quota + MobsCleared)
	///   level2    S1 — pick up ammo (LootCollected: Ammo), reload hint
	///   level3    S2 — a crate bullets can't break: strike it (BreakablesCleared: TutorialCrate, melee only)
	///   level4    S3 — run a narrow lane of strong toxic hatches (PlayerInZone past the lane)
	///   level5    S3 — pick up the medkit (LootCollected: Health)
	///   level6    S4 — pick up the shield (LootCollected: Bonus 'shield')
	///   level7    S4 — shotgun + a crowd of 6 Slow / 2 Fast under the shield (quota + MobsCleared)
	///   level8    S5 — pick up the speed boost (LootCollected: Bonus 'speedup')
	///   level9    S5 — Fast zombies pour out behind: run to the exit (PlayerInZone 'exit') → finish
	/// Gates open on their stage and never shut again (no softlock if a zombie lags behind). Mob loot: no weapons /
	/// bonuses (the lesson loot is placed), ammo fallback like the other levels. Reuses KeyLevelBuilder's props
	/// (walls, gates, sewer hatches, open wall pipes). Safe to re-run. See Docs/TutorialLevelFeature.md.
	/// </summary>
	public static class TutorialLevelBuilder
	{
		private const string Log = "[TutorialLevelBuilder]";
		internal const string PrefabPath = "Assets/_Prefabs/Levels/Level_00_Tutorial.prefab";
		private const string FloorMesh = "Assets/_Prefabs/Levels/Meshes/Level_00_Tutorial_Floor.asset";
		internal const string ConfigPath = "Assets/_Data/Levels/Configs/Level_Tutorial.asset";
		internal const string TutorialDefinitionPath = "Assets/_Data/Levels/Definitions/Level_Tutorial.asset";
		private const string CrateConfigSource = "Assets/_Data/Breakables/Crate.asset";
		private const string CrateConfigPath = "Assets/_Data/Breakables/TutorialCrate.asset";
		private const string MeshFolder = PropFolder + "/Meshes";
		private const string HatchPrefabPath = PropFolder + "/SewerHatch.prefab";
		private const string GatePrefabPath = PropFolder + "/LevelGate.prefab";
		private const string ArrowFloorPath = PropFolder + "/TutorialArrow_Floor.prefab";
		private const string ArrowMarkerPath = PropFolder + "/TutorialArrow_Marker.prefab";

		internal const string DisplayName = "Учебный тоннель";
		private const string CrateId = "TutorialCrate";

		// Tunnel: z ∈ [−HalfW, HalfW]; rooms along −X. Gate lines (x) between the rooms.
		private const float HalfW = 5f;
		private const float XStart = 0f, XEnd = -160f;
		private const float G1 = -16f, G2 = -40f, G3 = -60f, G4 = -88f, G5 = -116f, G6 = -150f;
		private const float LaneX0 = -76f, LaneX1 = -66f, LaneHalf = 1.5f;

		private static Material _arrow, _lamp, _lampHousing, _tray, _paintYellow, _paintWhite, _crateMark;

		[MenuItem("Tools/Crowds/Build Tutorial Level")]
		public static void Build()
		{
			EnsureFolder(MaterialFolder);
			EnsureFolder(PropFolder);
			EnsureFolder(MeshFolder);
			EnsureFolder(ConditionFolder);
			CreateMaterials();
			CreateTutorialMaterials();

			if (AssetDatabase.LoadAssetAtPath<GameObject>(WallSparkPrefabPath) == null)
				BuildWallSparkEffect();
			var hatch = AssetDatabase.LoadAssetAtPath<GameObject>(HatchPrefabPath) ?? BuildHatchPrefab();
			var gate = AssetDatabase.LoadAssetAtPath<GameObject>(GatePrefabPath) ?? BuildGatePrefab();
			var floorArrow = BuildFloorArrowPrefab();
			var markerArrow = BuildMarkerArrowPrefab();
			var crateConfig = BuildCrateConfig();

			var config = BuildConfig();
			var prefab = BuildLevel(config, hatch, gate, floorArrow, markerArrow, crateConfig);
			if (prefab != null)
				BuildTutorialDefinition(prefab);

			AssetDatabase.SaveAssets();
			CampaignSetup.Apply();
			AssetDatabase.SaveAssets();
			Debug.Log($"{Log} Level built: {PrefabPath}");
		}

		// ================================================================== config

		private static SmartConditionWrapper LootCollected(string name, LootType type, string id) =>
			Condition<LootCollectedConditionWrapper>(name, p =>
			{
				p.FindPropertyRelative("_lootType").enumValueIndex = (int)type;
				p.FindPropertyRelative("_id").stringValue = id ?? string.Empty;
			});

		private static LevelConfig BuildConfig()
		{
			var mobsCleared = Condition<MobsClearedConditionWrapper>("MobsCleared", null, null);
			var atMove = Condition<PlayerInZoneConditionWrapper>("PlayerInZone_TutMove", "_zoneId", "t_move");
			var pastToxic = Condition<PlayerInZoneConditionWrapper>("PlayerInZone_TutToxicExit", "_zoneId", "t_toxic_exit");
			var atExit = Condition<PlayerInZoneConditionWrapper>("PlayerInZone_TutExit", "_zoneId", "t_exit");
			var ammo = LootCollected("LootCollected_Ammo", LootType.Ammo, null);
			var health = LootCollected("LootCollected_Health", LootType.Health, null);
			var shield = LootCollected("LootCollected_Shield", LootType.Bonus, "shield");
			var speed = LootCollected("LootCollected_SpeedUp", LootType.Bonus, "speedup");
			var crate = Condition<BreakablesClearedConditionWrapper>("BreakablesCleared_TutorialCrate", "_configId", CrateId);
			var lowAmmo = Condition<NoAmmoAroundConditionWrapper>("LowAmmoNoneAround", p =>
			{
				p.FindPropertyRelative("_sameCaliberOnly").boolValue = true;
				p.FindPropertyRelative("_lowAmmoThreshold").intValue = 3;
			});

			var config = LoadOrCreate<LevelConfig>(ConfigPath);
			var cleared = new[] { mobsCleared };
			var stages = new List<DifficultyStage>
			{
				Step(DifficultyLevel.tutorial, 0f, new[] { atMove },
					"Учебный тоннель. {Move} — ходить, мышь — целиться. Иди по стрелкам к воротам"),
				Quota(DifficultyLevel.level1, 1.5f, 1.2f, cleared,
					"Зомби! Наведи прицел и стреляй — {Attack}. Убей всех", ("Slow", 3)),
				Step(DifficultyLevel.level2, 0f, new[] { ammo },
					"Патроны на исходе — подбери коробку с патронами. Перезарядка — {Reload}"),
				Step(DifficultyLevel.level3, 0f, new[] { crate },
					"Пули этот ящик не берут. Подойди вплотную и ударь — {Melee}"),
				Step(DifficultyLevel.level4, 0f, new[] { pastToxic },
					"Люки впереди ядовиты, обойти их нельзя. Пробеги — не задерживайся"),
				Step(DifficultyLevel.level5, 0f, new[] { health },
					"Яд отнял здоровье — подбери аптечку"),
				Step(DifficultyLevel.level6, 0f, new[] { shield },
					"Щит на время поглощает урон — подбери его"),
				Quota(DifficultyLevel.level7, 3f, 0.7f, cleared,
					"Возьми дробовик и отбейся от толпы, пока держится щит", ("Slow", 6), ("Fast", 2)),
				Step(DifficultyLevel.level8, 0f, new[] { speed },
					"Ускорение — подбери его, впереди забег"),
				new DifficultyStage
				{
					// The chase: for 12 s Fast zombies and zombie cheerleaders pour out of the pipes behind (spawning
					// speeds up ×2 over the stage), two more pipes ahead throw cheerleaders across the way.
					// The stage ends at the exit.
					DifficultyLevel = DifficultyLevel.level9,
					DifficultyTimer = 12f,
					SpeedMultiplier = 2f,
					InterSpawnCooldown = 0.3f,
					RestTime = 0f,
					EndConditions = new[] { atExit },
					ShowTimer = false,
					SpawnOnlyWhileTimer = true,
					Objective = "Беги к выходу! За тобой погоня — черлидерши не отстанут",
				},
				Step(DifficultyLevel.finish, 0f, null, null),
			};
			SetField(config, "_difficultyStages", stages);

			var fallback = new AdditionalLootConfig();
			fallback.AdditionalLoot[lowAmmo] = new[] { Ammo(Caliber.None, FallbackAmmoCount) };
			SetField(config, "_AdditionalLootConfigs", new List<AdditionalLootConfig> { fallback });
			SetField(config, "_sectorMode", SectorMode.Sliding);
			SetField(config, "_activeSectorRadius", 1);
			SetField(config, "_spawnDistance", new Vector2(2.5f, 40f));
			// Lesson loot is placed by hand: mobs drop no weapons and no bonuses here (ammo / health drops stay).
			SetField(config, "_allowedMobWeaponDrops", new[] { "__none__" });
			SetField(config, "_bannedMobBonusIds", new[] { "shield", "speedup", "rage" });
			EditorUtility.SetDirty(config);
			return config;
		}

		private static DifficultyStage Quota(DifficultyLevel level, float rest, float interSpawn,
			SmartConditionWrapper[] end, string objective, params (string id, int count)[] quota)
		{
			var entries = new MobQuotaEntry[quota.Length];
			for (int i = 0; i < quota.Length; i++)
				entries[i] = new MobQuotaEntry { MobId = quota[i].id, Count = quota[i].count };
			return new DifficultyStage
			{
				DifficultyLevel = level,
				DifficultyTimer = Mathf.Max(1f, rest), // only the rest window; must stay > 0
				SpeedMultiplier = 1f,
				InterSpawnCooldown = interSpawn,
				RestTime = rest,
				EndConditions = end,
				ShowTimer = false,
				SpawnOnlyWhileTimer = false,
				Objective = objective,
				MobQuota = entries,
			};
		}

		// Melee-only crate: a copy of Crate with its own id, 2 strikes of the player's melee (25).
		private static BreakableConfig BuildCrateConfig()
		{
			if (AssetDatabase.LoadAssetAtPath<BreakableConfig>(CrateConfigPath) == null &&
			    !AssetDatabase.CopyAsset(CrateConfigSource, CrateConfigPath))
			{
				Debug.LogError($"{Log} Can't copy {CrateConfigSource} → {CrateConfigPath}");
				return null;
			}
			var config = AssetDatabase.LoadAssetAtPath<BreakableConfig>(CrateConfigPath);
			var so = new SerializedObject(config);
			so.FindProperty("_id").stringValue = CrateId;
			so.FindProperty("_maxHealth").floatValue = 50f;
			so.FindProperty("_damageableBy").intValue = (int)BreakableDamageSources.Melee;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(config);
			return config;
		}

		// ================================================================== level

		private static GameObject BuildLevel(LevelConfig config, GameObject hatch, GameObject gate, GameObject floorArrow,
			GameObject markerArrow, BreakableConfig crateConfig)
		{
			var root = new GameObject("Level_00_Tutorial");
			try
			{
				var levelRoot = root.AddComponent<LevelRoot>();

				// --- Floor: the tunnel + the exit room, one sector ---
				var floorGo = new GameObject("Floor");
				floorGo.transform.SetParent(root.transform, false);
				var sector = floorGo.AddComponent<FloorSector>();
				var mesh = BuildFloorMesh(new[] { new Rect(XEnd, -HalfW, XStart - XEnd, HalfW * 2f) }, FloorMesh);
				var slab = new GameObject("FloorMesh");
				slab.transform.SetParent(floorGo.transform, false);
				slab.AddComponent<MeshFilter>().sharedMesh = mesh;
				var slabRenderer = slab.AddComponent<MeshRenderer>();
				slabRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
				slabRenderer.renderingLayerMask = FloorRenderingLayers;

				// --- Walls: long sides, ends, gate lines, the toxic lane blocks ---
				var walls = Child(root.transform, "Walls");
				WallX(walls, "North", HalfW, XEnd, XStart);
				WallX(walls, "South", -HalfW, XEnd, XStart);
				WallZ(walls, "StartEnd", XStart, -HalfW, HalfW);
				WallZ(walls, "ExitEnd", XEnd, -HalfW, HalfW);
				foreach (float g in new[] { G1, G2, G3, G4, G5, G6 })
				{
					WallZ(walls, $"Gate{g:0}_S", g, -HalfW, -GateWidth / 2f);
					WallZ(walls, $"Gate{g:0}_N", g, GateWidth / 2f, HalfW);
				}
				// Collapsed section: two concrete blocks leave a 3 m lane over the toxic hatches.
				float laneLen = LaneX1 - LaneX0, blockW = HalfW - LaneHalf;
				WallBox(walls, "Lane_N", new Vector3((LaneX0 + LaneX1) / 2f, 0, LaneHalf + blockW / 2f),
					new Vector3(laneLen, WallHeight, blockW));
				WallBox(walls, "Lane_S", new Vector3((LaneX0 + LaneX1) / 2f, 0, -LaneHalf - blockW / 2f),
					new Vector3(laneLen, WallHeight, blockW));

				// --- Gates: each opens on its lesson stage and stays open ---
				var gates = Child(root.transform, "Gates");
				TunnelGate(gate, gates, G1, "tg1", DifficultyLevel.level1, "Ворота открыты");
				TunnelGate(gate, gates, G2, "tg2", DifficultyLevel.level3, "Проход открыт");
				TunnelGate(gate, gates, G3, "tg3", DifficultyLevel.level4, "Проход открыт");
				TunnelGate(gate, gates, G4, "tg4", DifficultyLevel.level6, "Проход открыт");
				TunnelGate(gate, gates, G5, "tg5", DifficultyLevel.level8, "Проход открыт");
				TunnelGate(gate, gates, G6, "tg6", DifficultyLevel.level9, "Выход открыт!");

				// --- Zones, start, exit ---
				var mission = Child(root.transform, "Mission");
				Zone(mission, "t_move", new Vector3(-13.5f, FloorY, 0), new Vector3(5f, 2, 9));
				Zone(mission, "t_s1", new Vector3((G1 + G2) / 2f, FloorY, 0), new Vector3(G1 - G2 - 1f, 2, 9));
				Zone(mission, "t_s2", new Vector3((G2 + G3) / 2f, FloorY, 0), new Vector3(G2 - G3 - 1f, 2, 9));
				Zone(mission, "t_s4", new Vector3((G4 + G5) / 2f, FloorY, 0), new Vector3(G4 - G5 - 1f, 2, 9));
				Zone(mission, "t_s5", new Vector3((G5 + G6) / 2f, FloorY, 0), new Vector3(G5 - G6 - 1f, 2, 9));
				Zone(mission, "t_toxic_exit", new Vector3(-82f, FloorY, 0), new Vector3(10f, 2, 9));
				Zone(mission, "t_exit", new Vector3((G6 + XEnd) / 2f, FloorY, 0), new Vector3(8f, 2, 8));
				var exitMark = Primitive(PrimitiveType.Cylinder, "ExitMarker", mission, _exitMark,
					new Vector3((G6 + XEnd) / 2f, FloorY + 0.02f, 0), Quaternion.identity, new Vector3(4f, 0.01f, 4f));
				exitMark.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				var start = Child(mission, "PlayerStart");
				start.SetPositionAndRotation(new Vector3(-3f, FloorY, 0f), Quaternion.Euler(0, -90, 0));

				// --- Lesson loot (spawned at the start of its stage) ---
				var loot = Child(root.transform, "StageLoot");
				StageLootPoint(loot, "Ammo", new Vector3(-30f, FloorY, 0f), DifficultyLevel.level2, 1f, Ammo(Caliber.Mm9, 20));
				StageLootPoint(loot, "Medkit", new Vector3(-83f, FloorY, 0f), DifficultyLevel.level5, 1f, Health(50));
				StageLootPoint(loot, "Shield", new Vector3(-94f, FloorY, 0f), DifficultyLevel.level6, 1f, Bonus("shield"));
				StageLootPoint(loot, "Shotgun", new Vector3(-97f, FloorY, 0f), DifficultyLevel.level7, 1.3f,
					Weapon("Shotgun"), Ammo(Caliber.Gauge12, 24));
				StageLootPoint(loot, "SpeedUp", new Vector3(-121f, FloorY, 0f), DifficultyLevel.level8, 1f, Bonus("speedup"));

				// Ammo fallback points (AdditionalSpawn): the drop lands in the player's room.
				var fallbackPoints = Child(root.transform, "AmmoFallbackPoints");
				foreach (var pos in new[]
				         {
					         new Vector3(-22, FloorY, 3), new Vector3(-34, FloorY, -3), new Vector3(-47, FloorY, 3),
					         new Vector3(-95, FloorY, -3), new Vector3(-108, FloorY, 3), new Vector3(-126, FloorY, -3),
				         })
				{
					var point = Child(fallbackPoints, $"AmmoPoint_{fallbackPoints.childCount + 1}");
					point.position = pos;
					point.gameObject.tag = AdditionalSpawnTag;
				}

				var props = Child(root.transform, "Props");
				var spawns = Child(root.transform, "SpawnPoints");

				// --- Melee crate (bullets don't hurt it) ---
				var cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath);
				if (cratePrefab != null && crateConfig != null)
				{
					var c = (GameObject)PrefabUtility.InstantiatePrefab(cratePrefab, props);
					c.name = "Crate_MeleeOnly";
					c.transform.SetPositionAndRotation(new Vector3(-50f, FloorY, 0f), Quaternion.Euler(0f, 15f, 0f));
					c.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
					var so = new SerializedObject(c.GetComponent<Breakable>());
					so.FindProperty("_config").objectReferenceValue = crateConfig;
					so.FindProperty("_overrideLoot").boolValue = true;
					so.FindProperty("_lootCount").intValue = 1;
					var table = so.FindProperty("_lootTable");
					table.arraySize = 1;
					var entry = table.GetArrayElementAtIndex(0);
					entry.FindPropertyRelative("LootType").enumValueIndex = (int)LootType.Ammo;
					entry.FindPropertyRelative("Id").stringValue = string.Empty;
					entry.FindPropertyRelative("Count").intValue = 15;
					entry.FindPropertyRelative("Chance").floatValue = 1f;
					entry.FindPropertyRelative("AmmoCaliber").enumValueIndex = (int)Caliber.Mm9;
					so.ApplyModifiedPropertiesWithoutUndo();
					// Hazard-yellow fist plate on the floor in front of it: "hit this one".
					Primitive(PrimitiveType.Cylinder, "MeleeMark", props, _crateMark,
						new Vector3(-50f, FloorY + 0.012f, 0f), Quaternion.identity, new Vector3(3.2f, 0.01f, 3.2f))
						.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				}
				else
				{
					Debug.LogError($"{Log} {CratePrefabPath} or the crate config is missing — the melee lesson can't end.");
				}

				// --- Toxic lane: 5 hatches with a much stronger stench than the regular ones ---
				for (int i = 0; i < 5; i++)
				{
					var h = (GameObject)PrefabUtility.InstantiatePrefab(hatch, props);
					h.name = $"ToxicHatch_{i + 1}";
					h.transform.SetPositionAndRotation(new Vector3(LaneX1 - 1f - i * 2f, FloorY, 0f), Quaternion.identity);
					var hso = new SerializedObject(h.GetComponent<SewerHatch>());
					var mods = hso.FindProperty("_modifiers");
					mods.arraySize = 1;
					var stench = SewerHatch.CreateDefaultStench();
					stench.Value = 5f;
					stench.Interval = 0.4f;
					mods.GetArrayElementAtIndex(0).managedReferenceValue = stench;
					hso.ApplyModifiedPropertiesWithoutUndo();
				}
				// Hazard stripes across the lane mouth.
				HazardBand(props, LaneX1 + 0.6f);
				HazardBand(props, LaneX0 - 0.6f);

				// --- Open wall pipes = spawn points ---
				var s1 = new[] { Mob("Slow", 1, (DifficultyLevel.level1, 2.5f)) };
				foreach (var (x, side) in new[] { (-26f, 1f), (-33f, -1f), (-38f, 1f) })
					PipeSpawn(props, spawns, "S1", x, side, s1);
				var s4 = new[]
				{
					Mob("Slow", 3, (DifficultyLevel.level7, 3f)),
					Mob("Fast", 1, (DifficultyLevel.level7, 6f), (DifficultyLevel.level9, 1.3f)),
					Mob("CheerZombie", 1, (DifficultyLevel.level9, 1.8f)),
				};
				foreach (var (x, side) in new[] { (-104f, 1f), (-104f, -1f), (-112f, 1f), (-112f, -1f) })
					PipeSpawn(props, spawns, "S4", x, side, s4);
				// The chase also comes from ahead: cheerleaders cut across the run.
				var s5 = new[] { Mob("CheerZombie", 1, (DifficultyLevel.level9, 3.5f)) };
				foreach (var (x, side) in new[] { (-138f, 1f), (-138f, -1f) })
					PipeSpawn(props, spawns, "S5", x, side, s5);

				// --- Arrows ---
				var arrows = Child(root.transform, "Arrows");
				Chain(floorArrow, arrows, DifficultyLevel.tutorial, -5f, -8f, -11f, -14f);
				Chain(floorArrow, arrows, DifficultyLevel.level1, -15f, -18.5f, -22f);
				Marker(markerArrow, arrows, DifficultyLevel.level2, new Vector3(-30f, FloorY, 0f));
				Chain(floorArrow, arrows, DifficultyLevel.level3, -37f, -40.5f, -44f);
				Marker(markerArrow, arrows, DifficultyLevel.level3, new Vector3(-50f, FloorY, 0f));
				Chain(floorArrow, arrows, DifficultyLevel.level4, -56f, -60f, -64f, -67f, -70f, -73f, -76f);
				Marker(markerArrow, arrows, DifficultyLevel.level5, new Vector3(-83f, FloorY, 0f));
				Chain(floorArrow, arrows, DifficultyLevel.level6, -85f, -88.5f);
				Marker(markerArrow, arrows, DifficultyLevel.level6, new Vector3(-94f, FloorY, 0f));
				Marker(markerArrow, arrows, DifficultyLevel.level7, new Vector3(-97f, FloorY, 0f));
				Chain(floorArrow, arrows, DifficultyLevel.level8, -113f, -116.5f);
				Marker(markerArrow, arrows, DifficultyLevel.level8, new Vector3(-121f, FloorY, 0f));
				Chain(floorArrow, arrows, DifficultyLevel.level9, -126f, -131f, -136f, -141f, -146f, -150.5f);

				// --- Tunnel dressing (visual only) ---
				BuildDressing(root.transform);

				// --- LevelRoot wiring ---
				var rso = new SerializedObject(levelRoot);
				rso.FindProperty("_levelConfig").objectReferenceValue = config;
				var sectors = rso.FindProperty("_sectors");
				sectors.arraySize = 1;
				sectors.GetArrayElementAtIndex(0).objectReferenceValue = sector;
				rso.FindProperty("_playerStart").objectReferenceValue = start;
				rso.ApplyModifiedPropertiesWithoutUndo();

				var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
				if (!ok)
					Debug.LogError($"{Log} Failed to save {PrefabPath}");
				return saved;
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		private static void TunnelGate(GameObject prefab, Transform parent, float x, string id, DifficultyLevel stage,
			string message) =>
			Gate(prefab, parent, $"Gate_{id}", new Vector3(x, FloorY, 0f), 90f, id, LevelGate.OpenMode.OnStage, stage,
				null, message, null, null);

		// Open pipe in the north (side +1) or south (−1) wall at x, with a spawn point in front of its mouth.
		private static void PipeSpawn(Transform props, Transform spawns, string area, float x, float side, MobEntry[] mobs)
		{
			var wall = new Vector3(x, FloorY, side * HalfW);
			var inward = side > 0 ? Vector3.back : Vector3.forward;
			Outlet(props, $"OpenPipe_{area}_{props.childCount}", wall, inward);
			Spawn(spawns, area, OutletSpawnPoint(wall, inward), mobs);
		}

		private static void HazardBand(Transform parent, float x)
		{
			var band = Child(parent, "HazardBand");
			band.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			const int n = 6;
			float w = LaneHalf * 2f / n;
			for (int i = 0; i < n; i++)
				Primitive(PrimitiveType.Cube, "Stripe", band, i % 2 == 0 ? _hazard : _hazardDark,
					new Vector3(x, FloorY + 0.006f, -LaneHalf + w * (i + 0.5f)), Quaternion.Euler(0, 45f, 0),
					new Vector3(w * 0.72f, 0.01f, w * 0.72f));
		}

		// ================================================================== arrows

		// Floor chevrons along the tunnel centre pointing −X (up the screen), pulsing one after another.
		private static void Chain(GameObject prefab, Transform parent, DifficultyLevel stage, params float[] xs)
		{
			for (int i = 0; i < xs.Length; i++)
			{
				var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
				go.name = $"Arrow_{stage}_{i + 1}";
				go.transform.SetPositionAndRotation(new Vector3(xs[i], FloorY + 0.1f, 0f), Quaternion.Euler(0, -90f, 0));
				SetupArrow(go, stage, i * 0.18f);
			}
		}

		// Floating arrow above a lesson target, tip ~1.7 m above the floor (over the loot icon).
		private static void Marker(GameObject prefab, Transform parent, DifficultyLevel stage, Vector3 target)
		{
			var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
			go.name = $"Marker_{stage}";
			go.transform.SetPositionAndRotation(target, Quaternion.identity);
			SetupArrow(go, stage, 0f);
		}

		private static void SetupArrow(GameObject go, DifficultyLevel stage, float phase)
		{
			var so = new SerializedObject(go.GetComponent<TutorialArrow>());
			var stages = so.FindProperty("_stages");
			stages.arraySize = 1;
			stages.GetArrayElementAtIndex(0).intValue = (int)stage;
			so.FindProperty("_phase").floatValue = -phase; // later arrows of the chain pulse later
			so.ApplyModifiedPropertiesWithoutUndo();
			go.SetActive(false); // MissionSystem shows it on its stage
		}

		private static GameObject BuildFloorArrowPrefab()
		{
			// Chevron (two parallelogram arms) lying flat, pointing +Z, ~1.9 m wide.
			var polys = new List<Vector2[]>
			{
				new[] { new Vector2(-0.95f, -0.45f), new Vector2(-0.95f, -0.95f), new Vector2(0f, 0f), new Vector2(0f, 0.5f) },
				new[] { new Vector2(0.95f, -0.95f), new Vector2(0.95f, -0.45f), new Vector2(0f, 0.5f), new Vector2(0f, 0f) },
			};
			var mesh = ExtrudedMesh($"{MeshFolder}/TutorialArrow_Floor.asset", polys, 0.04f, lying: true);
			return ArrowPrefab(ArrowFloorPath, mesh, TutorialArrow.Style.Floor, Vector3.zero, 1.1f, 0.35f);
		}

		private static GameObject BuildMarkerArrowPrefab()
		{
			// Standing arrow pointing down (tip at the local origin), spun around Y by TutorialArrow.
			var polys = new List<Vector2[]>
			{
				new[] { new Vector2(0f, 0f), new Vector2(0.65f, 0.75f), new Vector2(-0.65f, 0.75f) },
				new[] { new Vector2(-0.24f, 0.75f), new Vector2(0.24f, 0.75f), new Vector2(0.24f, 1.7f), new Vector2(-0.24f, 1.7f) },
			};
			var mesh = ExtrudedMesh($"{MeshFolder}/TutorialArrow_Marker.asset", polys, 0.3f, lying: false);
			return ArrowPrefab(ArrowMarkerPath, mesh, TutorialArrow.Style.Marker, new Vector3(0f, 1.7f, 0f), 0.9f, 0.4f);
		}

		private static GameObject ArrowPrefab(string path, Mesh mesh, TutorialArrow.Style style, Vector3 visualPos,
			float speed, float amplitude)
		{
			return SavePrefab(path, root =>
			{
				root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				var arrow = root.AddComponent<TutorialArrow>();
				var visual = new GameObject("Visual");
				visual.transform.SetParent(root.transform, false);
				visual.transform.localPosition = visualPos;
				visual.AddComponent<MeshFilter>().sharedMesh = mesh;
				var r = visual.AddComponent<MeshRenderer>();
				r.sharedMaterial = _arrow;
				r.shadowCastingMode = style == TutorialArrow.Style.Marker
					? UnityEngine.Rendering.ShadowCastingMode.On
					: UnityEngine.Rendering.ShadowCastingMode.Off;
				r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
				var so = new SerializedObject(arrow);
				so.FindProperty("_style").intValue = (int)style;
				so.FindProperty("_visual").objectReferenceValue = visual.transform;
				so.FindProperty("_speed").floatValue = speed;
				so.FindProperty("_amplitude").floatValue = amplitude;
				so.ApplyModifiedPropertiesWithoutUndo();
			});
		}

		/// <summary>
		/// Extrudes convex polygons (u, v). lying: (u, v) → (x, z), thickness up along Y from 0. Otherwise (u, v) → (x, y),
		/// thickness centred on Z. Flat-shaded; rewritten in place (same guid).
		/// </summary>
		private static Mesh ExtrudedMesh(string path, List<Vector2[]> polys, float thickness, bool lying)
		{
			var verts = new List<Vector3>();
			var normals = new List<Vector3>();
			var tris = new List<int>();

			Vector3 P(Vector2 p, bool far) => lying
				? new Vector3(p.x, far ? thickness : 0f, p.y)
				: new Vector3(p.x, p.y, far ? thickness * 0.5f : -thickness * 0.5f);
			Vector3 capNormal = lying ? Vector3.up : Vector3.forward;

			void Face(IReadOnlyList<Vector3> pts, Vector3 n)
			{
				int b = verts.Count;
				foreach (var p in pts)
				{
					verts.Add(p);
					normals.Add(n);
				}
				for (int i = 1; i + 1 < pts.Count; i++)
				{
					// Unity front face = clockwise from the viewer ⇔ cross(b−a, c−a) points along the normal.
					var c = Vector3.Cross(pts[i] - pts[0], pts[i + 1] - pts[0]);
					if (Vector3.Dot(c, n) >= 0f)
					{
						tris.Add(b); tris.Add(b + i); tris.Add(b + i + 1);
					}
					else
					{
						tris.Add(b); tris.Add(b + i + 1); tris.Add(b + i);
					}
				}
			}

			foreach (var poly in polys)
			{
				var far = new List<Vector3>();
				var near = new List<Vector3>();
				foreach (var p in poly)
				{
					far.Add(P(p, true));
					near.Add(P(p, false));
				}
				Face(far, capNormal);
				Face(near, -capNormal);
				// Sides: outward normal = away from the polygon centre.
				Vector2 centre = Vector2.zero;
				foreach (var p in poly)
					centre += p;
				centre /= poly.Length;
				for (int i = 0; i < poly.Length; i++)
				{
					var a = poly[i];
					var b = poly[(i + 1) % poly.Length];
					var edge = b - a;
					var n2 = new Vector2(edge.y, -edge.x).normalized;
					if (Vector2.Dot(n2, (a + b) * 0.5f - centre) < 0f)
						n2 = -n2;
					var n = lying ? new Vector3(n2.x, 0f, n2.y) : new Vector3(n2.x, n2.y, 0f);
					Face(new[] { P(a, false), P(a, true), P(b, true), P(b, false) }, n);
				}
			}

			var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
			bool created = mesh == null;
			if (created)
				mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
			mesh.Clear();
			mesh.SetVertices(verts);
			mesh.SetNormals(normals);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			if (created)
				AssetDatabase.CreateAsset(mesh, path);
			else
				EditorUtility.SetDirty(mesh);
			return mesh;
		}

		// ================================================================== dressing

		private static void BuildDressing(Transform root)
		{
			var decor = Child(root, "Dressing");
			decor.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			float face = HalfW - WallThickness * 0.5f; // inner wall face
			float len = XStart - XEnd;

			// Painted lines: dashed yellow centre line, solid white edge lines.
			for (float x = XStart - 2f; x > XEnd + 1f; x -= 3f)
				Primitive(PrimitiveType.Cube, "CentreDash", decor, _paintYellow, new Vector3(x - 0.6f, FloorY + 0.004f, 0f),
					Quaternion.identity, new Vector3(1.2f, 0.008f, 0.14f));
			foreach (float z in new[] { -face + 0.35f, face - 0.35f })
				Primitive(PrimitiveType.Cube, "EdgeLine", decor, _paintWhite, new Vector3((XStart + XEnd) / 2f, FloorY + 0.004f, z),
					Quaternion.identity, new Vector3(len - 1f, 0.008f, 0.1f));

			// Cable trays along both walls + two service pipes on the north wall.
			foreach (float side in new[] { -1f, 1f })
				Primitive(PrimitiveType.Cube, "CableTray", decor, _tray,
					new Vector3((XStart + XEnd) / 2f, FloorY + 1.75f, side * (face - 0.12f)), Quaternion.identity,
					new Vector3(len - 1f, 0.08f, 0.24f));
			PipeSegment(decor, new Vector3(XStart - 0.5f, FloorY + 1.45f, face - 0.12f),
				new Vector3(XEnd + 0.5f, FloorY + 1.45f, face - 0.12f), 0.09f, _pipeSupply);
			PipeSegment(decor, new Vector3(XStart - 0.5f, FloorY + 1.22f, face - 0.14f),
				new Vector3(XEnd + 0.5f, FloorY + 1.22f, face - 0.14f), 0.07f, _pipeReturn);

			// Caged wall lamps, alternating sides every 7 m (emissive, no real lights).
			int k = 0;
			for (float x = XStart - 4f; x > XEnd + 2f; x -= 7f, k++)
			{
				float side = k % 2 == 0 ? 1f : -1f;
				var lamp = Child(decor, "WallLamp");
				lamp.SetPositionAndRotation(new Vector3(x, FloorY + 2.0f, side * face), Quaternion.identity);
				Primitive(PrimitiveType.Cube, "Housing", lamp, _lampHousing, new Vector3(0, 0, -side * 0.07f),
					Quaternion.identity, new Vector3(0.7f, 0.2f, 0.14f));
				Primitive(PrimitiveType.Cube, "Glow", lamp, _lamp, new Vector3(0, -0.02f, -side * 0.15f),
					Quaternion.identity, new Vector3(0.6f, 0.12f, 0.05f));
				for (int b = -1; b <= 1; b++)
					Primitive(PrimitiveType.Cube, "Cage", lamp, _lampHousing, new Vector3(b * 0.25f, -0.02f, -side * 0.18f),
						Quaternion.identity, new Vector3(0.03f, 0.18f, 0.03f));
			}

			// Hazard stripes on the floor under every gate line.
			foreach (float g in new[] { G1, G2, G3, G4, G5, G6 })
			{
				const int n = 8;
				float w = GateWidth / n;
				for (int i = 0; i < n; i++)
					Primitive(PrimitiveType.Cube, "GateStripe", decor, i % 2 == 0 ? _hazard : _hazardDark,
						new Vector3(g, FloorY + 0.005f, -GateWidth / 2f + w * (i + 0.5f)), Quaternion.identity,
						new Vector3(1.4f, 0.008f, w));
			}
		}

		private static void CreateTutorialMaterials()
		{
			_arrow = Mat("Tutorial_Arrow", new Color(1f, 0.62f, 0.08f), 0f, 0.3f, new Color(2.4f, 1.25f, 0.1f));
			_lamp = Mat("Tutorial_LampGlow", new Color(1f, 0.95f, 0.8f), 0f, 0.2f, new Color(2.2f, 2.0f, 1.5f));
			_lampHousing = Mat("Tutorial_LampHousing", new Color(0.12f, 0.12f, 0.12f), 0.5f, 0.3f);
			_tray = Mat("Tutorial_CableTray", new Color(0.38f, 0.40f, 0.42f), 0.7f, 0.35f);
			_paintYellow = Mat("Tutorial_PaintYellow", new Color(0.95f, 0.75f, 0.12f), 0f, 0.15f);
			_paintWhite = Mat("Tutorial_PaintWhite", new Color(0.85f, 0.85f, 0.82f), 0f, 0.15f);
			_crateMark = Mat("Tutorial_MeleeMark", new Color(1f, 0.78f, 0.1f), 0f, 0.2f, new Color(0.45f, 0.32f, 0.02f));
		}

		// ================================================================== definition

		private static void BuildTutorialDefinition(GameObject prefab)
		{
			var definition = LoadOrCreate<LevelDefinition>(TutorialDefinitionPath);
			var so = new SerializedObject(definition);
			so.FindProperty("_levelPrefab").objectReferenceValue = prefab;
			so.FindProperty("_displayName").stringValue = DisplayName;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(definition);
			// Campaign slot (first) and the icon are set by CampaignSetup.Apply().
		}
	}
}
