using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using static Crowds.EditorTools.KeyLevelBuilder;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Builds the campaign level "Ледовая арена" — Tools ▸ Crowds ▸ Build Hockey Level. Safe to re-run (assets are
	/// rewritten in place, same guids). Reuses the KeyLevelBuilder helpers (materials, primitives, spawn/loot helpers,
	/// wall-spark effect, conditions).
	///
	/// Layout (floor top y = -0.5, one FloorSector, SectorMode.Sliding):
	///   arena floor  x[-36,36] z[-20,20], enclosed by primitive stands (parapet stops bullets, tiers behind it);
	///   props in the ring outside the boards: overturned vending machine at the SE gate, cheerleader stage (N side),
	///   seven-segment scoreboard on the west stand (deaths:frags, ECS.ScoreboardSystem);
	///   rink         60×30 rounded rectangle (corner R 8.5) at the centre, boards 1.1 m with 4 gates on the long
	///                sides; the ice is an IceRink ModifierZone → TractionModifier → inertial sliding for everybody;
	///   8 tunnel portals in the stands = mob spawn points; player starts at centre ice.
	/// Script: 3 quota waves (DifficultyStage.MobQuota) — 25 Slow + 15 Fast; 70 Fast + 8 Fatman;
	///   5 GunnerBare + 5 GunnerMob (helmet) + 45 Fast — with 12 s breathers in between when loot is thrown onto the ice.
	/// Loot: no shotguns, 12ga or shield (mob drops filtered by LevelConfig); ammo fallback on the ice.
	/// Produces: LevelConfig _Data/Levels/Configs/Level_Hockey, prefab _Prefabs/Levels/Level_03_Hockey (+ meshes),
	/// mob config _Data/Mobs/GunnerBare, LevelDefinition _Data/Levels/Definitions/Level_Hockey (3rd campaign level).
	/// See Docs/HockeyLevelFeature.md.
	/// </summary>
	public static class HockeyLevelBuilder
	{
		private const string LevelPrefabPath = "Assets/_Prefabs/Levels/Level_03_Hockey.prefab";
		private const string FloorMeshPathH = "Assets/_Prefabs/Levels/Meshes/Level_03_Hockey_Floor.asset";
		private const string IceMeshPath = "Assets/_Prefabs/Levels/Meshes/Level_03_Hockey_Ice.asset";
		private const string LevelConfigPathH = "Assets/_Data/Levels/Configs/Level_Hockey.asset";
		private const string DefinitionPathH = "Assets/_Data/Levels/Definitions/Level_Hockey.asset";
		private const string GunnerSourcePath = "Assets/_Data/Mobs/GunnerMob.asset";
		private const string GunnerBarePath = "Assets/_Data/Mobs/GunnerBare.asset";
		private const string GunnerBareId = "GunnerBare";
		private const string MobHolderPath = "Assets/_Data/MobConfigHolder.asset";
		private const string FastSourcePath = "Assets/_Data/Mobs/Fast.asset";
		private const string CheerZombiePath = "Assets/_Data/Mobs/CheerZombie.asset";
		private const string CheerZombieId = "CheerZombie";

		// Arena / rink geometry. The ring between the boards and the stands is kept narrow (~5–6 m) on purpose so the
		// player isn't tempted to kite around the rink outside the boards.
		private const float ArenaX = 36f, ArenaZ = 20f;
		private const float RinkLength = 60f, RinkWidth = 30f, RinkCorner = 8.5f;
		private const float BoardHeight = 1.1f, BoardThickness = 0.3f;
		private const float ParapetHeight = 1.4f;
		private const int StandTiers = 4;
		private const float TierDepth = 1.2f, TierRise = 0.55f;
		private const float IceGrip = 3f;            // TractionModifier: m/s² of velocity change on the ice
		private const string ZoneRink = "rink";
		private static readonly Color IceColor = new(0.5f, 0.6f, 0.68f);

		// Gates in the long boards (x ranges), both sides.
		private static readonly Vector2[] BoardGaps = { new(-12f, -9f), new(9f, 12f) };

		private static Material _ice, _lineRed, _lineBlue, _board, _kick, _rail, _seatRed, _seatBlue, _net;
		private static Material _vendingBody, _vendingGlass, _vendingPanel, _vendingWhite, _stageDeck, _speaker;
		private static Material _scoreHousing, _scoreFrame, _segmentLit, _segmentOff;
		private static Material _tunnelBlack, _resurfacerWhite, _tire, _headlight, _beacon;

		[MenuItem("Tools/Crowds/Build Hockey Level")]
		public static void Build()
		{
			EnsureFolder(MaterialFolder);
			CreateMaterials();
			CreateHockeyMaterials();
			BuildWallSparkEffect();
			EnsureMobOnlyArea();
			EnsureGunnerBare();
			EnsureCheerZombie();

			var mobsCleared = Condition<MobsClearedConditionWrapper>("MobsCleared", null, null);
			var lowAmmo = Condition<NoAmmoAroundConditionWrapper>("LowAmmoNoneAround", prototype =>
			{
				prototype.FindPropertyRelative("_sameCaliberOnly").boolValue = true;
				prototype.FindPropertyRelative("_lowAmmoThreshold").intValue = 3;
			});

			var config = BuildConfig(mobsCleared, lowAmmo);
			var prefab = BuildPrefab(config);
			BuildHockeyDefinition(prefab);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"[HockeyLevelBuilder] Level built: {LevelPrefabPath}");
		}

		// ================================================================== config

		private static LevelConfig BuildConfig(SmartConditionWrapper mobsCleared, SmartConditionWrapper lowAmmo)
		{
			var config = LoadOrCreate<LevelConfig>(LevelConfigPathH);
			var cleared = new[] { mobsCleared };
			var stages = new List<DifficultyStage>
			{
				QuotaWave(DifficultyLevel.level1, rest: 5f, interSpawn: 0.35f, cleared, "Волна 1/3: выживите",
					("Slow", 25), ("Fast", 15)),
				Breather(DifficultyLevel.level2, 12f, "Передышка: на лёд выбросили припасы"),
				QuotaWave(DifficultyLevel.level3, rest: 3f, interSpawn: 0.25f, cleared, "Волна 2/3: быстрые и толстяки",
					("Fast", 70), ("Fatman", 8)),
				Breather(DifficultyLevel.level4, 12f, "Передышка: на лёд выбросили припасы"),
				QuotaWave(DifficultyLevel.level5, rest: 3f, interSpawn: 0.25f, cleared, "Волна 3/3: стрелки!",
					(GunnerBareId, 5), ("GunnerMob", 5), ("Fast", 45)),
				new() { DifficultyLevel = DifficultyLevel.finish, DifficultyTimer = 0f, SpeedMultiplier = 1f,
					InterSpawnCooldown = 0.5f, EndConditions = System.Array.Empty<SmartConditionWrapper>(), ShowTimer = false },
			};

			SetField(config, "_difficultyStages", stages);
			var fallback = new AdditionalLootConfig();
			fallback.AdditionalLoot[lowAmmo] = new[] { Ammo(Caliber.None, 10) };
			SetField(config, "_AdditionalLootConfigs", new List<AdditionalLootConfig> { fallback });
			SetField(config, "_sectorMode", SectorMode.Sliding);
			SetField(config, "_activeSectorRadius", 1);
			SetField(config, "_spawnDistance", new Vector2(6f, 70f));
			// No shotguns / 12ga / shield on this level.
			SetField(config, "_allowedMobWeaponDrops", new[] { "Pistol", "SMG", "AR", "Rifle", "MachineGun" });
			SetField(config, "_bannedMobAmmoCalibers", new[] { Caliber.Gauge12 });
			SetField(config, "_bannedMobBonusIds", new[] { "shield" });
			EditorUtility.SetDirty(config);
			return config;
		}

		private static DifficultyStage QuotaWave(DifficultyLevel level, float rest, float interSpawn,
			SmartConditionWrapper[] end, string objective, params (string id, int count)[] quota)
		{
			var entries = new MobQuotaEntry[quota.Length];
			for (int i = 0; i < quota.Length; i++)
				entries[i] = new MobQuotaEntry { MobId = quota[i].id, Count = quota[i].count };
			return new DifficultyStage
			{
				DifficultyLevel = level,
				// The timer only covers the rest window here; spawning runs until the quota is spent and the stage
				// ends on MobsCleared (which also waits for the quota). Must stay > 0 (spawn cooldown lerp).
				DifficultyTimer = Mathf.Max(1f, rest),
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

		private static DifficultyStage Breather(DifficultyLevel level, float seconds, string objective) => new()
		{
			DifficultyLevel = level,
			DifficultyTimer = seconds,
			SpeedMultiplier = 1f,
			InterSpawnCooldown = 0.5f,
			RestTime = seconds,
			EndConditions = System.Array.Empty<SmartConditionWrapper>(),
			ShowTimer = true,
			SpawnOnlyWhileTimer = true,
			Objective = objective,
		};

		// ================================================================== prefab

		private static GameObject BuildPrefab(LevelConfig config)
		{
			var root = new GameObject("Level_03_Hockey");
			try
			{
				var levelRoot = root.AddComponent<LevelRoot>();

				// --- Arena floor (one sector, receives decals) ---
				var floorGo = new GameObject("Floor");
				floorGo.transform.SetParent(root.transform, false);
				var sector = floorGo.AddComponent<FloorSector>();
				var floorMesh = BuildFloorMesh(new[] { new Rect(-ArenaX, -ArenaZ, ArenaX * 2f, ArenaZ * 2f) }, FloorMeshPathH);
				var slab = new GameObject("FloorMesh");
				slab.transform.SetParent(floorGo.transform, false);
				slab.AddComponent<MeshFilter>().sharedMesh = floorMesh;
				var slabRenderer = slab.AddComponent<MeshRenderer>();
				slabRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
				slabRenderer.renderingLayerMask = FloorRenderingLayers;

				// --- Rink: ice surface + slide zone + markings + goals + boards ---
				var rink = Child(root.transform, "Rink");
				BuildIce(rink);
				BuildMarkings(rink);
				Goal(rink, -RinkLength * 0.5f + 3.5f, 1f);
				Goal(rink, RinkLength * 0.5f - 3.5f, -1f);
				BuildBoards(rink);

				// --- Stands around the arena (parapet = wall + bullet blocker) with tunnel portals ---
				var stands = Child(root.transform, "Stands");
				var cheerCorner = ComputeCheerCorner();
				var tunnels = BuildStands(stands, cheerCorner);
				BuildScoreboard(stands);

				// --- Props between the boards and the stands ---
				var props = Child(root.transform, "Props");
				// Overturned vending machine at the south-east board gate (x 9…12): narrows the gate and the corridor.
				VendingMachineOverturned(props, new Vector3(9.6f, FloorY, -RinkWidth * 0.5f - 1.25f), 70f);
				// Ice resurfacer parked on the ice in the corner opposite the cheerleaders (+X/−Z), nose toward centre ice.
				IceResurfacer(rink, new Vector3(19.5f, FloorY + 0.02f, -10.5f), -55f);
				CheerStage(props, cheerCorner);

				// --- Mission: zone (banner/fallback), start, loot ---
				var mission = Child(root.transform, "Mission");
				Zone(mission, ZoneRink, new Vector3(0, FloorY, 0), new Vector3(RinkLength, 2, RinkWidth));
				var start = Child(mission, "PlayerStart");
				start.SetPositionAndRotation(new Vector3(0, FloorY, 0), Quaternion.identity);

				var loot = Child(root.transform, "StageLoot");
				StageLootPoint(loot, "Start", new Vector3(0, FloorY, -3), DifficultyLevel.level1, 1f, Ammo(Caliber.Mm9, 24));
				StageLootPoint(loot, "Break1_West", new Vector3(-10, FloorY, 0), DifficultyLevel.level2, 1.2f,
					Weapon("SMG"), Ammo(Caliber.Mm9, 40));
				StageLootPoint(loot, "Break1_East", new Vector3(10, FloorY, 0), DifficultyLevel.level2, 1.2f,
					Health(30), Grenade());
				StageLootPoint(loot, "Break1_North", new Vector3(0, FloorY, 8), DifficultyLevel.level2, 1.2f,
					Ammo(Caliber.None, 30), Ammo(Caliber.Mm9, 30));
				StageLootPoint(loot, "Break2_West", new Vector3(-10, FloorY, -6), DifficultyLevel.level4, 1.2f,
					Weapon("AR"), Ammo(Caliber.Cal556, 60));
				StageLootPoint(loot, "Break2_East", new Vector3(10, FloorY, 6), DifficultyLevel.level4, 1.2f,
					Health(40), Grenade());
				StageLootPoint(loot, "Break2_South", new Vector3(0, FloorY, -8), DifficultyLevel.level4, 1.2f,
					Ammo(Caliber.None, 40), Health(25));

				// Ammo fallback points on the ice (AdditionalSpawn tag → AdditionalLootSpawnSystem).
				var fallbackPoints = Child(root.transform, "AmmoFallbackPoints");
				foreach (var pos in new[] { new Vector3(-14, FloorY, 6), new Vector3(14, FloorY, -6),
					         new Vector3(-6, FloorY, -8), new Vector3(6, FloorY, 8) })
				{
					var point = Child(fallbackPoints, $"AmmoPoint_{fallbackPoints.childCount + 1}");
					point.position = pos;
					point.gameObject.tag = AdditionalSpawnTag;
				}

				// --- Spawn points: in front of every tunnel portal ---
				var spawns = Child(root.transform, "SpawnPoints");
				var mobs = new[]
				{
					Mob("Slow", 5, (DifficultyLevel.level1, 3.5f)),
					Mob("Fast", 3, (DifficultyLevel.level1, 3.5f), (DifficultyLevel.level3, 2.2f), (DifficultyLevel.level5, 2.5f)),
					Mob("Fatman", 1, (DifficultyLevel.level3, 2.2f)),
					Mob(GunnerBareId, 1, (DifficultyLevel.level5, 2.5f)),
					Mob("GunnerMob", 1, (DifficultyLevel.level5, 2.5f)),
				};
				// Spawn points sit deep inside the dark tunnels (MobOnly navmesh area — the player can't walk in).
				foreach (var (pos, _) in tunnels)
					Spawn(spawns, "Tunnel", pos, mobs);

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
					Debug.LogError($"[HockeyLevelBuilder] Failed to save {LevelPrefabPath}");
				return saved;
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		// ================================================================== rink

		// Rounded-rectangle outline (counter-clockwise seen from above), centred at the origin.
		private static List<Vector2> RinkOutline(float inset, int segmentsPerCorner)
		{
			float hx = RinkLength * 0.5f - inset, hz = RinkWidth * 0.5f - inset, r = RinkCorner - inset;
			var centres = new[] { new Vector2(hx - r, hz - r), new Vector2(-(hx - r), hz - r),
				new Vector2(-(hx - r), -(hz - r)), new Vector2(hx - r, -(hz - r)) };
			var points = new List<Vector2>();
			for (int c = 0; c < 4; c++)
			{
				float a0 = c * Mathf.PI * 0.5f;
				for (int i = 0; i <= segmentsPerCorner; i++)
				{
					float a = a0 + i * Mathf.PI * 0.5f / segmentsPerCorner;
					points.Add(centres[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
				}
			}
			return points;
		}

		private static void BuildIce(Transform rink)
		{
			// Surface: a fan mesh over the rounded rectangle, 2 cm above the arena floor; receives decals.
			var outline = RinkOutline(0f, 10);
			var verts = new List<Vector3> { Vector3.zero };
			var uvs = new List<Vector2> { Vector2.zero };
			foreach (var p in outline)
			{
				verts.Add(new Vector3(p.x, 0f, p.y));
				uvs.Add(p / 4f);
			}
			var tris = new List<int>();
			for (int i = 0; i < outline.Count; i++)
			{
				int a = i + 1, b = (i + 1) % outline.Count + 1;
				// Outline is CCW from above → (centre, next, current) is clockwise = facing up.
				tris.Add(0); tris.Add(b); tris.Add(a);
			}
			var normals = new List<Vector3>();
			for (int i = 0; i < verts.Count; i++) normals.Add(Vector3.up);

			EnsureFolder(System.IO.Path.GetDirectoryName(IceMeshPath).Replace('\\', '/'));
			var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(IceMeshPath);
			bool created = mesh == null;
			if (created) mesh = new Mesh { name = "Level_03_Hockey_Ice" };
			mesh.Clear();
			mesh.SetVertices(verts);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			if (created) AssetDatabase.CreateAsset(mesh, IceMeshPath);
			else EditorUtility.SetDirty(mesh);

			var ice = new GameObject("Ice");
			ice.transform.SetParent(rink, false);
			ice.transform.localPosition = new Vector3(0, FloorY + 0.02f, 0);
			ice.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = ice.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = _ice;
			renderer.renderingLayerMask = FloorRenderingLayers;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

			// Slide zone: same rounded rectangle → TractionModifier for whoever stands on it.
			var zone = ice.AddComponent<IceRink>();
			SetField(zone, "_size", new Vector2(RinkLength, RinkWidth));
			SetField(zone, "_cornerRadius", RinkCorner);
			SetField(zone, "_linger", 0.1f);
			SetField(zone, "_modifiers", new Modifier[]
			{
				new TractionModifier { Id = ModifierConstants.TractionIce, Source = BuffSource.Environment, Value = IceGrip },
			});
			EditorUtility.SetDirty(zone);
		}

		private static void BuildMarkings(Transform rink)
		{
			var marks = Child(rink, "Markings");
			marks.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			float y = FloorY + 0.024f;
			const float h = 0.004f;
			Primitive(PrimitiveType.Cube, "CentreLine", marks, _lineRed, new Vector3(0, y, 0), Quaternion.identity,
				new Vector3(0.35f, h, RinkWidth - 0.1f));
			foreach (float x in new[] { -8.5f, 8.5f })
				Primitive(PrimitiveType.Cube, "BlueLine", marks, _lineBlue, new Vector3(x, y, 0), Quaternion.identity,
					new Vector3(0.35f, h, RinkWidth - 0.1f));
			// Goal lines: width of the rink at that x (inside the rounded corners).
			float gx = RinkLength * 0.5f - 3.5f;
			float dx = gx - (RinkLength * 0.5f - RinkCorner);
			float half = RinkWidth * 0.5f - RinkCorner + Mathf.Sqrt(Mathf.Max(0f, RinkCorner * RinkCorner - dx * dx));
			foreach (float x in new[] { -gx, gx })
				Primitive(PrimitiveType.Cube, "GoalLine", marks, _lineRed, new Vector3(x, y, 0), Quaternion.identity,
					new Vector3(0.1f, h, half * 2f - 0.2f));
			Primitive(PrimitiveType.Cylinder, "CentreDot", marks, _lineBlue, new Vector3(0, y + 0.001f, 0), Quaternion.identity,
				new Vector3(0.6f, h, 0.6f));
			foreach (float x in new[] { -20f, 20f })
				foreach (float z in new[] { -7f, 7f })
					Primitive(PrimitiveType.Cylinder, "FaceoffDot", marks, _lineRed, new Vector3(x, y + 0.001f, z),
						Quaternion.identity, new Vector3(0.6f, h, 0.6f));
			foreach (float x in new[] { -gx, gx })
				Primitive(PrimitiveType.Cylinder, "Crease", marks, _lineBlue, new Vector3(x + Mathf.Sign(-x) * 0.9f, y - 0.001f, 0),
					Quaternion.identity, new Vector3(2.4f, h, 3.4f));
		}

		// Goal at x, open toward +x (dir = 1) or −x (dir = −1). Solid for the bake (a small obstacle on the ice).
		private static void Goal(Transform rink, float x, float dir)
		{
			var goal = Child(rink, dir > 0 ? "Goal_West" : "Goal_East");
			goal.position = new Vector3(x, FloorY, 0);
			const float w = 1.8f, h = 1.2f, d = 1.0f;
			foreach (float z in new[] { -w * 0.5f, w * 0.5f })
				Primitive(PrimitiveType.Cylinder, "Post", goal, _rail, new Vector3(0, h * 0.5f, z),
					Quaternion.identity, new Vector3(0.1f, h, 0.1f));
			Primitive(PrimitiveType.Cylinder, "Crossbar", goal, _rail, new Vector3(0, h, 0), Quaternion.Euler(90, 0, 0),
				new Vector3(0.1f, w, 0.1f));
			// Net: double-sided cut-out quads with the 256×256 mesh texture (see BuildNetTexture).
			Primitive(PrimitiveType.Quad, "NetBack", goal, _net, new Vector3(-dir * d, h * 0.4f, 0), Quaternion.Euler(0, 90f, 0),
				new Vector3(w, h * 0.8f, 1f));
			Primitive(PrimitiveType.Quad, "NetTop", goal, _net, new Vector3(-dir * d * 0.5f, h * 0.9f, 0),
				Quaternion.Euler(0, 0, dir * 20f) * Quaternion.Euler(90f, 0, 0), new Vector3(d * 1.05f, w, 1f));
			foreach (float z in new[] { -w * 0.5f, w * 0.5f })
				Primitive(PrimitiveType.Quad, "NetSide", goal, _net, new Vector3(-dir * d * 0.5f, h * 0.45f, z),
					Quaternion.identity, new Vector3(d, h * 0.9f, 1f));
		}

		// Boards along the rink outline (straight + rounded corners), white with a yellow kick plate and a red rail;
		// two gates in each long side. They cut the navmesh (bake) but don't stop bullets (low enough to shoot over).
		private static void BuildBoards(Transform rink)
		{
			var boards = Child(rink, "Boards");
			float hx = RinkLength * 0.5f, hz = RinkWidth * 0.5f, straightX = hx - RinkCorner, straightZ = hz - RinkCorner;

			// Long sides, split by the gates.
			foreach (float z in new[] { -hz, hz })
			{
				float from = -straightX;
				foreach (var gap in BoardGaps)
				{
					BoardSegment(boards, new Vector3(from, 0, z), new Vector3(gap.x, 0, z));
					from = gap.y;
					// Gate posts.
					foreach (float px in new[] { gap.x, gap.y })
						Primitive(PrimitiveType.Cube, "GatePost", boards, _rail, new Vector3(px, FloorY + BoardHeight * 0.55f, z),
							Quaternion.identity, new Vector3(0.18f, BoardHeight * 1.1f, BoardThickness + 0.1f));
				}
				BoardSegment(boards, new Vector3(from, 0, z), new Vector3(straightX, 0, z));
			}
			// Short sides.
			foreach (float x in new[] { -hx, hx })
				BoardSegment(boards, new Vector3(x, 0, -straightZ), new Vector3(x, 0, straightZ));

			// Corners: 8 chords each.
			const int chords = 8;
			var centres = new[] { new Vector2(straightX, straightZ), new Vector2(-straightX, straightZ),
				new Vector2(-straightX, -straightZ), new Vector2(straightX, -straightZ) };
			for (int c = 0; c < 4; c++)
			{
				float a0 = c * Mathf.PI * 0.5f;
				for (int i = 0; i < chords; i++)
				{
					float a = a0 + i * Mathf.PI * 0.5f / chords, b = a0 + (i + 1) * Mathf.PI * 0.5f / chords;
					var p = centres[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RinkCorner;
					var q = centres[c] + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * RinkCorner;
					BoardSegment(boards, new Vector3(p.x, 0, p.y), new Vector3(q.x, 0, q.y));
				}
			}
		}

		private static void BoardSegment(Transform parent, Vector3 a, Vector3 b)
		{
			Vector3 d = b - a;
			float len = d.magnitude;
			if (len < 0.05f)
				return;
			var rot = Quaternion.LookRotation(d / len);
			var mid = (a + b) * 0.5f;
			len += 0.08f; // overlap at the chord joints
			var seg = Child(parent, "Board");
			seg.SetPositionAndRotation(new Vector3(mid.x, FloorY, mid.z), rot);
			Primitive(PrimitiveType.Cube, "Panel", seg, _board, new Vector3(0, BoardHeight * 0.5f, 0), Quaternion.identity,
				new Vector3(BoardThickness, BoardHeight, len));
			Primitive(PrimitiveType.Cube, "Kick", seg, _kick, new Vector3(0, 0.12f, 0), Quaternion.identity,
				new Vector3(BoardThickness + 0.02f, 0.24f, len));
			Primitive(PrimitiveType.Cube, "Rail", seg, _rail, new Vector3(0, BoardHeight + 0.03f, 0), Quaternion.identity,
				new Vector3(BoardThickness + 0.06f, 0.06f, len));
		}

		// ================================================================== stands

		/// <summary>
		/// Stands on all four sides: a concrete parapet on the arena edge (cuts the navmesh, stops bullets with sparks)
		/// and stepped tiers with coloured seat rows behind it. Long sides run past the corners to close them.
		/// The cheerleader corner (world −X/+Z, see <see cref="ComputeCheerCorner"/>) is cut out of the north and west
		/// stands and closed by a short diagonal corner stand right behind the stage.
		/// Tunnel portals (dark openings with a frame) in the parapet — returned as (point on the edge, inward normal)
		/// for the spawn points.
		/// </summary>
		private static List<(Vector3, Vector3)> BuildStands(Transform parent, CheerCorner corner)
		{
			float depth = 0.4f + StandTiers * TierDepth;
			var tunnels = new List<(Vector3, Vector3)>();

			// (edge line point, outward normal, [from, to] and tunnel offsets along the edge's local X from that point)
			// Local X = LookRotation(outward) right: north → +x, south → −x, west → +z, east → −z.
			var sides = new (Vector3 line, Vector3 outward, float from, float to, Material seat, float[] tunnelsAt)[]
			{
				(new Vector3(0, 0, ArenaZ), Vector3.forward, corner.NorthCutX, ArenaX + depth, _seatRed, new[] { -25f, 0f, 25f }),
				(new Vector3(0, 0, -ArenaZ), Vector3.back, -ArenaX - depth, ArenaX + depth, _seatBlue, new[] { -25f, 0f, 25f }),
				(new Vector3(-ArenaX, 0, 0), Vector3.left, -ArenaZ, corner.WestCutZ, _seatBlue, new[] { 0f }),
				(new Vector3(ArenaX, 0, 0), Vector3.right, -ArenaZ, ArenaZ, _seatRed, new[] { 0f }),
				(corner.WallCentre, corner.Dir, -corner.WallLength * 0.5f, corner.WallLength * 0.5f, _seatRed, System.Array.Empty<float>()),
			};

			int index = 0;
			foreach (var side in sides)
			{
				var stand = Child(parent, index == 4 ? "Stand_CheerCorner" : $"Stand_{index + 1}");
				index++;
				var rot = Quaternion.LookRotation(side.outward); // local +Z = outward, local X = along the edge
				float mid = (side.from + side.to) * 0.5f;
				var length = side.to - side.from;
				var centre = side.line + rot * Vector3.right * mid;
				stand.SetPositionAndRotation(new Vector3(centre.x, FloorY, centre.z), rot);

				// Tunnel holes (stand-local X ranges) cut through the parapet and the first TunnelTiers tiers.
				var holes = new List<Vector2>();
				foreach (float along in side.tunnelsAt)
					holes.Add(new Vector2(along - mid - TunnelWidth * 0.5f, along - mid + TunnelWidth * 0.5f));

				// Parapet on the edge line.
				foreach (var (a, b) in Segments(-length * 0.5f, length * 0.5f, holes))
				{
					var parapet = Primitive(PrimitiveType.Cube, "Parapet", stand, _wall, new Vector3((a + b) * 0.5f, ParapetHeight * 0.5f, 0),
						Quaternion.identity, new Vector3(b - a, ParapetHeight, 0.8f));
					SparkOnHit(parapet.AddComponent<BulletBlocker>());
					Primitive(PrimitiveType.Cube, "ParapetCap", stand, _wallCap, new Vector3((a + b) * 0.5f, ParapetHeight + 0.03f, 0),
						Quaternion.identity, new Vector3(b - a, 0.06f, 0.85f));
				}

				for (int t = 0; t < StandTiers; t++)
				{
					float top = ParapetHeight + TierRise * (t + 1);
					float z = 0.4f + TierDepth * (t + 0.5f);
					var tierHoles = t < TunnelTiers ? holes : null;
					foreach (var (a, b) in Segments(-length * 0.5f, length * 0.5f, tierHoles))
					{
						Primitive(PrimitiveType.Cube, $"Tier{t}", stand, _wall, new Vector3((a + b) * 0.5f, top * 0.5f, z), Quaternion.identity,
							new Vector3(b - a, top, TierDepth));
						if (b - a > 0.6f)
							Primitive(PrimitiveType.Cube, $"Seats{t}", stand, side.seat, new Vector3((a + b) * 0.5f, top + 0.12f, z + 0.25f),
								Quaternion.identity, new Vector3(b - a - 0.4f, 0.24f, 0.45f));
					}
				}

				foreach (float along in side.tunnelsAt)
				{
					Tunnel(stand, along - mid);
					// Spawn deep inside the dark tunnel: mobs walk out of the darkness.
					var spawnPoint = stand.TransformPoint(new Vector3(along - mid, 0, TunnelSpawnDepth));
					tunnels.Add((spawnPoint, -side.outward));
				}
			}
			return tunnels;
		}

		// Tunnels: 3 m wide, 2.4 m high, through the parapet and the first two tiers (z −0.4 … 2.8 in stand space).
		private const float TunnelWidth = 3f, TunnelHeight = 2.4f, TunnelSpawnDepth = 2f;
		private const int TunnelTiers = 2;
		internal const string MobOnlyArea = "MobOnly";
		private const int MobOnlyAreaIndex = 3; // first user area in Navigation ▸ Areas

		// [from, to] minus the holes, as (start, end) pieces.
		private static List<(float, float)> Segments(float from, float to, List<Vector2> holes)
		{
			var result = new List<(float, float)>();
			var sorted = holes == null ? new List<Vector2>() : new List<Vector2>(holes);
			sorted.Sort((x, y) => x.x.CompareTo(y.x));
			float cursor = from;
			foreach (var hole in sorted)
			{
				if (hole.x > cursor + 0.01f)
					result.Add((cursor, Mathf.Min(hole.x, to)));
				cursor = Mathf.Max(cursor, hole.y);
			}
			if (to > cursor + 0.01f)
				result.Add((cursor, to));
			return result;
		}

		/// <summary>
		/// Mob tunnel cut into a stand (stand-local: X along the edge, −Z = into the arena, tunnel at X = along):
		/// pitch-black unlit lining (floor, walls, ceiling, back), a concrete roof slab over it, the portal frame
		/// (pillars + lintel) in front. The floor is the navmesh area <see cref="MobOnlyArea"/>: mobs walk out, the
		/// player's agent excludes it (EntryPoint), so the player can't walk in. The spawn point is
		/// <see cref="TunnelSpawnDepth"/> m deep, so mobs appear out of the dark. The back wall stops bullets.
		/// </summary>
		private static void Tunnel(Transform stand, float along)
		{
			const float w = TunnelWidth, h = TunnelHeight, z0 = -0.4f;
			float z1 = 0.4f + TierDepth * TunnelTiers; // back wall = front face of the first uncut tier
			float depth = z1 - z0, zc = (z0 + z1) * 0.5f;
			var tunnel = Child(stand, "Tunnel");
			tunnel.localPosition = new Vector3(along, 0, 0);
			tunnel.localRotation = Quaternion.identity;
			var id = Quaternion.identity;

			var floor = Primitive(PrimitiveType.Cube, "Floor", tunnel, _tunnelBlack, new Vector3(0, 0.01f, zc), id, new Vector3(w, 0.02f, depth));
			var modifier = floor.AddComponent<NavMeshModifier>();
			modifier.overrideArea = true;
			modifier.area = MobOnlyAreaIndex;
			foreach (float side in new[] { -1f, 1f })
				Primitive(PrimitiveType.Cube, "Wall", tunnel, _tunnelBlack, new Vector3(side * (w * 0.5f - 0.05f), h * 0.5f, zc), id,
					new Vector3(0.1f, h, depth));
			Primitive(PrimitiveType.Cube, "Ceiling", tunnel, _tunnelBlack, new Vector3(0, h - 0.02f, zc), id, new Vector3(w, 0.04f, depth));
			var back = Primitive(PrimitiveType.Cube, "Back", tunnel, _tunnelBlack, new Vector3(0, h * 0.5f, z1 - 0.05f), id,
				new Vector3(w, h, 0.1f));
			back.AddComponent<BulletBlocker>();
			// Roof slab above the cut (tier 0 is lower than the tunnel).
			float roofTop = ParapetHeight + TierRise * TunnelTiers;
			Primitive(PrimitiveType.Cube, "Roof", tunnel, _wall, new Vector3(0, (h + roofTop) * 0.5f, zc), id,
				new Vector3(w + 0.1f, Mathf.Max(0.1f, roofTop - h), depth));

			// Portal frame in front.
			foreach (float side in new[] { -1f, 1f })
				Primitive(PrimitiveType.Cube, "Pillar", tunnel, _wallCap, new Vector3(side * (w * 0.5f + 0.2f), h * 0.5f, -0.6f),
					id, new Vector3(0.4f, h, 0.5f));
			Primitive(PrimitiveType.Cube, "Lintel", tunnel, _wallCap, new Vector3(0, h + 0.2f, -0.6f), id,
				new Vector3(w + 0.8f, 0.4f, 0.5f));

			foreach (var r in tunnel.GetComponentsInChildren<MeshRenderer>())
				if (r.sharedMaterial == _tunnelBlack)
					r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		}

		/// <summary>
		/// Names navmesh area <see cref="MobOnlyAreaIndex"/> "MobOnly" in ProjectSettings/NavMeshAreas.asset (tunnel
		/// floors). Same cost as Walkable. Mobs path with AllAreas; the player's agent mask excludes it.
		/// </summary>
		private static void EnsureMobOnlyArea()
		{
			var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
			if (assets == null || assets.Length == 0)
			{
				Debug.LogWarning("[HockeyLevelBuilder] NavMeshAreas.asset not found: add a 'MobOnly' area at index 3 by hand.");
				return;
			}
			var so = new SerializedObject(assets[0]);
			var areas = so.FindProperty("areas");
			if (areas == null || areas.arraySize <= MobOnlyAreaIndex)
			{
				Debug.LogWarning("[HockeyLevelBuilder] Unexpected NavMeshAreas layout: add a 'MobOnly' area at index 3 by hand.");
				return;
			}
			var area = areas.GetArrayElementAtIndex(MobOnlyAreaIndex);
			var name = area.FindPropertyRelative("name");
			if (name.stringValue == MobOnlyArea)
				return;
			if (!string.IsNullOrEmpty(name.stringValue))
				Debug.LogWarning($"[HockeyLevelBuilder] Nav area {MobOnlyAreaIndex} '{name.stringValue}' renamed to '{MobOnlyArea}'.");
			name.stringValue = MobOnlyArea;
			area.FindPropertyRelative("cost").floatValue = 1f;
			so.ApplyModifiedPropertiesWithoutUndo();
			AssetDatabase.SaveAssets();
		}

		// ================================================================== props

		/// <summary>
		/// Generic primitive ice resurfacer (no branding): white body on four tyres, driver seat + steering wheel at the
		/// front, tall snow tank with a blue band behind, wide conditioner board at the rear, headlights, red tail
		/// lights, amber beacon. Solid for the bake; the chassis and tank stop bullets (a piece of cover on the ice).
		/// Local frame: +Z = forward (nose), X = width.
		/// </summary>
		private static void IceResurfacer(Transform parent, Vector3 position, float yaw)
		{
			var m = Child(parent, "IceResurfacer");
			m.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
			var id = Quaternion.identity;
			const float width = 1.9f;

			foreach (float x in new[] { -0.9f, 0.9f })
				foreach (float z in new[] { -1.3f, 1.3f })
					Primitive(PrimitiveType.Cylinder, "Tyre", m, _tire, new Vector3(x, 0.36f, z), Quaternion.Euler(0, 0, 90f),
						new Vector3(0.72f, 0.26f, 0.72f));

			var chassis = Primitive(PrimitiveType.Cube, "Chassis", m, _resurfacerWhite, new Vector3(0, 0.6f, 0), id, new Vector3(width, 0.5f, 4.0f));
			SparkOnHit(chassis.AddComponent<BulletBlocker>());
			Primitive(PrimitiveType.Cube, "Skirt", m, _lineBlue, new Vector3(0, 0.38f, 0), id, new Vector3(width + 0.04f, 0.08f, 4.02f));
			Primitive(PrimitiveType.Cube, "Hood", m, _resurfacerWhite, new Vector3(0, 1.12f, 1.55f), id, new Vector3(1.8f, 0.55f, 0.9f));

			// Driver: seat, backrest, steering column + wheel.
			Primitive(PrimitiveType.Cube, "Seat", m, _speaker, new Vector3(0.35f, 0.95f, 0.55f), id, new Vector3(0.55f, 0.14f, 0.5f));
			Primitive(PrimitiveType.Cube, "Backrest", m, _speaker, new Vector3(0.35f, 1.25f, 0.3f), Quaternion.Euler(-10f, 0, 0),
				new Vector3(0.55f, 0.55f, 0.1f));
			Primitive(PrimitiveType.Cylinder, "SteeringColumn", m, _speaker, new Vector3(0.35f, 1.2f, 1.0f), Quaternion.Euler(-35f, 0, 0),
				new Vector3(0.05f, 0.5f, 0.05f));
			Primitive(PrimitiveType.Cylinder, "SteeringWheel", m, _speaker, new Vector3(0.35f, 1.45f, 0.88f), Quaternion.Euler(-35f, 0, 0),
				new Vector3(0.4f, 0.03f, 0.4f));

			// Snow tank behind the driver.
			var tank = Primitive(PrimitiveType.Cube, "SnowTank", m, _resurfacerWhite, new Vector3(0, 1.45f, -0.85f), id,
				new Vector3(width, 1.3f, 1.9f));
			SparkOnHit(tank.AddComponent<BulletBlocker>());
			Primitive(PrimitiveType.Cube, "TankBand", m, _lineBlue, new Vector3(0, 1.55f, -0.85f), id, new Vector3(width + 0.03f, 0.22f, 1.93f));
			Primitive(PrimitiveType.Cube, "TankLid", m, _resurfacerWhite, new Vector3(0, 2.14f, -0.95f), Quaternion.Euler(8f, 0, 0),
				new Vector3(width - 0.1f, 0.1f, 1.7f));

			// Conditioner board at the rear (the part that shaves and floods the ice).
			Primitive(PrimitiveType.Cube, "Conditioner", m, _vendingPanel, new Vector3(0, 0.25f, -2.2f), id, new Vector3(2.2f, 0.35f, 0.45f));

			foreach (float x in new[] { -0.6f, 0.6f })
			{
				Primitive(PrimitiveType.Cylinder, "Headlight", m, _headlight, new Vector3(x, 1.15f, 2.01f), Quaternion.Euler(90f, 0, 0),
					new Vector3(0.18f, 0.04f, 0.18f));
				Primitive(PrimitiveType.Cube, "TailLight", m, _lampRed, new Vector3(x * 1.2f, 1.0f, -1.82f), id, new Vector3(0.18f, 0.1f, 0.04f));
			}
			Primitive(PrimitiveType.Cylinder, "Beacon", m, _beacon, new Vector3(-0.6f, 2.3f, -0.3f), id, new Vector3(0.18f, 0.18f, 0.18f));

			foreach (var r in m.GetComponentsInChildren<MeshRenderer>())
				if (r.sharedMaterial == _headlight || r.sharedMaterial == _beacon || r.sharedMaterial == _lampRed)
					r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		}

		private const string NetTexturePath = MaterialFolder + "/Textures/GoalNet.png";
		private const int NetTextureSize = 256, NetCell = 32, NetLine = 3;

		/// <summary>
		/// Writes the goal-net texture: 256×256 RGBA, transparent, white diamond mesh (two diagonal line families,
		/// 32 px period → seamless tiling, knots where they cross). Imported with alpha-is-transparency, repeat wrap and
		/// coverage-preserving mips (thin lines don't vanish at distance).
		/// </summary>
		private static Texture2D BuildNetTexture()
		{
			EnsureFolder(MaterialFolder + "/Textures");
			var tex = new Texture2D(NetTextureSize, NetTextureSize, TextureFormat.RGBA32, false);
			var pixels = new Color32[NetTextureSize * NetTextureSize];
			for (int y = 0; y < NetTextureSize; y++)
				for (int x = 0; x < NetTextureSize; x++)
				{
					int a = (x + y) % NetCell, b = (x - y + NetTextureSize) % NetCell;
					bool line = a < NetLine || b < NetLine;
					bool knot = a < NetLine + 1 && b < NetLine + 1;
					byte shade = (byte)(knot ? 225 : 245);
					pixels[y * NetTextureSize + x] = line ? new Color32(shade, shade, shade, 255) : new Color32(255, 255, 255, 0);
				}
			tex.SetPixels32(pixels);
			tex.Apply();
			System.IO.File.WriteAllBytes(NetTexturePath, tex.EncodeToPNG());
			Object.DestroyImmediate(tex);
			AssetDatabase.ImportAsset(NetTexturePath, ImportAssetOptions.ForceUpdate);

			if (AssetImporter.GetAtPath(NetTexturePath) is TextureImporter imp)
			{
				imp.textureType = TextureImporterType.Default;
				imp.textureShape = TextureImporterShape.Texture2D;
				imp.sRGBTexture = true;
				imp.alphaSource = TextureImporterAlphaSource.FromInput;
				imp.alphaIsTransparency = true;
				imp.wrapMode = TextureWrapMode.Repeat;
				imp.filterMode = FilterMode.Trilinear;
				imp.mipmapEnabled = true;
				imp.mipMapsPreserveCoverage = true;
				imp.alphaTestReferenceValue = 0.5f;
				imp.maxTextureSize = NetTextureSize;
				imp.SaveAndReimport();
			}
			return AssetDatabase.LoadAssetAtPath<Texture2D>(NetTexturePath);
		}

		// URP Lit, alpha-clipped (cut-out), double-sided, net texture tiled 2×2 per quad (~10 cm diamonds).
		private static void ConfigureNetMaterial(Material mat, Texture2D tex)
		{
			if (mat == null)
				return;
			mat.SetTexture("_BaseMap", tex);
			mat.SetTexture("_MainTex", tex);
			mat.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
			mat.SetTextureScale("_MainTex", new Vector2(2f, 2f));
			mat.SetColor("_BaseColor", Color.white);
			mat.SetFloat("_Surface", 0f);
			mat.SetFloat("_AlphaClip", 1f);
			mat.SetFloat("_Cutoff", 0.5f);
			mat.EnableKeyword("_ALPHATEST_ON");
			mat.SetFloat("_Cull", 0f); // both sides
			mat.SetOverrideTag("RenderType", "TransparentCutout");
			mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
			mat.doubleSidedGI = true;
			EditorUtility.SetDirty(mat);
		}

		/// <summary>
		/// Primitive soda vending machine knocked over onto its back (front panel facing up — reads well from the
		/// top-down camera): glass display with shelves, button column, logo band, dispenser flap, a few spilled cans.
		/// Solid for the navmesh bake (agents walk around it), low enough to shoot over (no BulletBlocker).
		/// Local frame: X = width, Y = up (the machine's depth), Z = its length.
		/// </summary>
		private static void VendingMachineOverturned(Transform parent, Vector3 position, float yaw)
		{
			const float w = 0.95f, h = 0.8f, l = 1.85f;
			var vm = Child(parent, "VendingMachine_Overturned");
			vm.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
			var id = Quaternion.identity;
			float top = h + 0.006f;

			Primitive(PrimitiveType.Cube, "Body", vm, _vendingBody, new Vector3(0, h * 0.5f, 0), id, new Vector3(w, h, l));
			Primitive(PrimitiveType.Cube, "Glass", vm, _vendingGlass, new Vector3(-0.12f, top, 0.2f), id, new Vector3(0.6f, 0.012f, 1.15f));
			for (int i = 0; i < 4; i++)
				Primitive(PrimitiveType.Cube, "Shelf", vm, _vendingWhite, new Vector3(-0.12f, top + 0.004f, -0.25f + i * 0.3f), id,
					new Vector3(0.58f, 0.006f, 0.025f));
			Primitive(PrimitiveType.Cube, "ButtonPanel", vm, _vendingPanel, new Vector3(0.3f, top, 0.25f), id, new Vector3(0.22f, 0.012f, 0.7f));
			for (int i = 0; i < 6; i++)
				Primitive(PrimitiveType.Cube, "Button", vm, _vendingWhite, new Vector3(0.3f, top + 0.01f, 0.5f - i * 0.08f), id,
					new Vector3(0.12f, 0.02f, 0.04f));
			Primitive(PrimitiveType.Cube, "CoinSlot", vm, _vendingWhite, new Vector3(0.3f, top + 0.01f, -0.2f), id, new Vector3(0.05f, 0.02f, 0.1f));
			Primitive(PrimitiveType.Cube, "LogoBand", vm, _vendingWhite, new Vector3(0, top, 0.78f), id, new Vector3(w - 0.08f, 0.012f, 0.16f));
			Primitive(PrimitiveType.Cube, "Dispenser", vm, _speaker, new Vector3(-0.05f, top, -0.72f), id, new Vector3(0.6f, 0.012f, 0.22f));

			// Spilled cans (decor, ignored by the bake).
			var cans = Child(vm, "Cans");
			cans.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			var canSpots = new (Vector3 pos, float yaw, Material mat)[]
			{
				(new Vector3(0.78f, 0.033f, -0.55f), 20f, _vendingBody),
				(new Vector3(0.98f, 0.033f, -0.05f), 75f, _vendingWhite),
				(new Vector3(-0.72f, 0.033f, 0.65f), 140f, _lineBlue),
			};
			foreach (var can in canSpots)
				Primitive(PrimitiveType.Cylinder, "Can", cans, can.mat, can.pos, Quaternion.Euler(0, can.yaw, 90f),
					new Vector3(0.066f, 0.12f, 0.066f));
		}

		// Cheerleader corner = world −X/+Z: the top-right of the screen (the fixed camera looks −X, screen right = +Z),
		// so the girls face the camera. Passage between the rink boards and the stage front kept narrow on purpose.
		private const float CheerPassage = 2.6f, CheerDeckLength = 7f, CheerDeckDepth = 5f, CheerDeckHeight = 0.9f;

		internal struct CheerCorner
		{
			public Vector3 Dir;         // from the rink corner toward the arena corner (stage local +Z)
			public Vector3 Perp;        // along the stage front (stage local +X)
			public Vector3 FrontCentre; // middle of the stage front edge
			public Vector3 StageCentre;
			public Vector3 WallCentre;  // diagonal corner stand: parapet centre line right behind the stage
			public float WallLength;
			public float NorthCutX;     // the north stand starts here (x)
			public float WestCutZ;      // the west stand ends here (z)
		}

		/// <summary>
		/// Geometry of the cheerleader corner: the stage sits on the diagonal from the rink's rounded corner to the arena
		/// corner, <see cref="CheerPassage"/> m off the boards; a short diagonal stand closes the corner behind it and the
		/// north/west stands are cut back to meet that stand (the stage is set into the stands).
		/// </summary>
		private static CheerCorner ComputeCheerCorner()
		{
			var rinkCorner = new Vector3(-(RinkLength * 0.5f - RinkCorner), 0, RinkWidth * 0.5f - RinkCorner);
			var arenaCorner = new Vector3(-ArenaX, 0, ArenaZ);
			var c = new CheerCorner();
			c.Dir = (arenaCorner - rinkCorner).normalized;
			c.Perp = new Vector3(c.Dir.z, 0, -c.Dir.x); // = LookRotation(Dir) right
			c.FrontCentre = rinkCorner + c.Dir * (RinkCorner + BoardThickness * 0.5f + CheerPassage);
			c.StageCentre = c.FrontCentre + c.Dir * (CheerDeckDepth * 0.5f);
			c.WallCentre = c.FrontCentre + c.Dir * (CheerDeckDepth + 0.4f); // parapet inner face flush with the deck back
			const float parapetOuter = 0.8f; // reach the outer face of both edge parapets (no gaps)
			float toNorth = (ArenaZ + parapetOuter - c.WallCentre.z) / c.Perp.z;
			float toWest = (c.WallCentre.x + ArenaX + parapetOuter) / c.Perp.x;
			float half = Mathf.Max(toNorth, toWest);
			c.WallLength = half * 2f;
			c.NorthCutX = c.WallCentre.x + c.Perp.x * half;
			c.WestCutZ = c.WallCentre.z - c.Perp.z * half;
			return c;
		}

		/// <summary>
		/// Cheerleader stage set into the stands in the cheer corner (deck 0.9 m, just under the 1.1 m board rail):
		/// skirt, deck with a yellow edge toward the ice, speakers in the back corners. Not Walkable for the bake.
		/// Three cheerleader NPCs (Assets/_Prefabs/Npc/Cheerleader.prefab — Tools ▸ Crowds ▸ Build Cheerleader NPC)
		/// stand on the slots facing the ice and the camera; <see cref="CheerleaderStand"/> holds them, the floor points
		/// in the passage in front of the stage (escape start / zombie spawns) and the escape point in the opposite corner.
		/// Local −Z = toward the ice.
		/// </summary>
		private static void CheerStage(Transform parent, CheerCorner corner)
		{
			const float length = CheerDeckLength, depth = CheerDeckDepth, deckH = CheerDeckHeight;
			var stage = Child(parent, "CheerleaderStage");
			stage.SetPositionAndRotation(new Vector3(corner.StageCentre.x, FloorY, corner.StageCentre.z), Quaternion.LookRotation(corner.Dir));
			var modifier = stage.gameObject.AddComponent<NavMeshModifier>();
			modifier.overrideArea = true;
			modifier.area = 1; // Not Walkable
			var id = Quaternion.identity;

			Primitive(PrimitiveType.Cube, "Skirt", stage, _seatBlue, new Vector3(0, (deckH - 0.1f) * 0.5f, 0), id,
				new Vector3(length, deckH - 0.1f, depth));
			Primitive(PrimitiveType.Cube, "Deck", stage, _stageDeck, new Vector3(0, deckH - 0.05f, 0), id,
				new Vector3(length + 0.1f, 0.1f, depth + 0.1f));
			Primitive(PrimitiveType.Cube, "EdgeTrim", stage, _kick, new Vector3(0, deckH - 0.16f, -depth * 0.5f - 0.03f), id,
				new Vector3(length, 0.08f, 0.04f));
			foreach (float side in new[] { -1f, 1f })
				Primitive(PrimitiveType.Cube, "Speaker", stage, _speaker, new Vector3(side * (length * 0.5f - 0.45f), deckH + 0.45f, depth * 0.5f - 0.5f), id,
					new Vector3(0.6f, 0.9f, 0.5f));

			// Slots: 3 dancers in a row near the front edge, facing the ice.
			const int dancers = 3;
			var slotRoot = Child(stage, "Slots");
			var floorRoot = Child(stage, "FloorPoints");
			var slots = new Transform[dancers];
			var floor = new Transform[dancers];
			for (int i = 0; i < dancers; i++)
			{
				float x = (i - (dancers - 1) * 0.5f) * 2f;
				slots[i] = Child(slotRoot, $"Slot_{i + 1}");
				slots[i].SetLocalPositionAndRotation(new Vector3(x, deckH, -depth * 0.5f + 1.3f), Quaternion.Euler(0, 180f, 0));
				// Middle of the passage in front of the stage ("under the stage", on the navmesh).
				floor[i] = Child(floorRoot, $"Floor_{i + 1}");
				floor[i].SetLocalPositionAndRotation(new Vector3(x, 0f, -depth * 0.5f - CheerPassage * 0.5f), Quaternion.Euler(0, 180f, 0));
			}

			// Escape point: the opposite (south-east) corner of the arena floor.
			var escape = Child(parent, "CheerEscapePoint");
			escape.position = new Vector3(ArenaX - 2.5f, FloorY, -ArenaZ + 2.5f);

			// NPC instances.
			var girls = new List<Cheerleader>();
			var npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CheerleaderNpcBuilder.PrefabPath);
			if (npcPrefab == null)
				Debug.LogWarning($"[HockeyLevelBuilder] {CheerleaderNpcBuilder.PrefabPath} not found — run Tools ▸ Crowds ▸ Build Cheerleader NPC, then rebuild the level.");
			else
			{
				var girlsRoot = Child(stage, "Cheerleaders");
				for (int i = 0; i < dancers; i++)
				{
					var go = (GameObject)PrefabUtility.InstantiatePrefab(npcPrefab, girlsRoot);
					go.name = $"Cheerleader_{i + 1}";
					go.transform.SetPositionAndRotation(slots[i].position, slots[i].rotation);
					girls.Add(go.GetComponent<Cheerleader>());
				}
			}

			var stand = stage.gameObject.AddComponent<CheerleaderStand>();
			SetField(stand, "_cheerleaders", girls.ToArray());
			SetField(stand, "_slots", slots);
			SetField(stand, "_floorPoints", floor);
			SetField(stand, "_escapePoint", escape);
			SetField(stand, "_zombieMobId", CheerZombieId);
			SetField(stand, "_transformStages", new[] { DifficultyLevel.level2, DifficultyLevel.level4 });
			EditorUtility.SetDirty(stand);
		}

		/// <summary>
		/// Outdated seven-segment scoreboard on the top tier of the west stand (behind the west goal — the fixed camera
		/// looks −X, so this stand faces the screen). Shows "HOME:AWAY" = player deaths : frags on 3 + 3 cells, digits
		/// growing outward from the colon, unused cells dark: "   :51 ", "  1:81 " after death (0 deaths = dark).
		/// Tilted back 20° toward the camera.
		/// Driven at runtime by ECS.ScoreboardSystem through <see cref="ArenaScoreboard"/>.
		/// </summary>
		private static void BuildScoreboard(Transform stands)
		{
			const float dw = 0.8f, dh = 1.4f, t = 0.14f, gap = 0.3f, colonW = 0.4f, margin = 0.5f, header = 0.45f;
			const int digits = 3; // per side: symmetric 000:000
			float groupWidth = digits * dw + (digits - 1) * gap;
			float width = margin * 2f + groupWidth * 2f + gap * 2f + colonW;
			float height = dh + 0.5f + header;

			var board = Child(stands, "Scoreboard");
			// Same frame as the stands: +Z outward (away from the arena), X along the edge (= screen right here).
			board.SetPositionAndRotation(new Vector3(-ArenaX, FloorY, 0), Quaternion.LookRotation(Vector3.left));
			board.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			var id = Quaternion.identity;

			float tierTop = ParapetHeight + TierRise * StandTiers;
			float standZ = 0.4f + TierDepth * (StandTiers - 0.5f);
			float bottom = tierTop + 0.6f;
			// Posts run up into the (tilted) housing's back so the board doesn't float.
			float postTop = bottom + height * 0.5f;
			foreach (float side in new[] { -1f, 1f })
				Primitive(PrimitiveType.Cylinder, "Post", board, _scoreFrame,
					new Vector3(side * (width * 0.5f - 0.6f), (tierTop + postTop) * 0.5f, standZ + 0.1f), id,
					new Vector3(0.18f, postTop - tierTop, 0.18f));

			var display = Child(board, "Display");
			display.SetLocalPositionAndRotation(new Vector3(0, bottom + height * 0.5f, standZ), Quaternion.Euler(20f, 0, 0));
			Primitive(PrimitiveType.Cube, "Housing", display, _scoreHousing, Vector3.zero, id, new Vector3(width, height, 0.35f));
			foreach (float side in new[] { -1f, 1f })
			{
				Primitive(PrimitiveType.Cube, "FrameH", display, _scoreFrame, new Vector3(0, side * (height * 0.5f + 0.05f), 0), id,
					new Vector3(width + 0.2f, 0.1f, 0.4f));
				Primitive(PrimitiveType.Cube, "FrameV", display, _scoreFrame, new Vector3(side * (width * 0.5f + 0.05f), 0, 0), id,
					new Vector3(0.1f, height + 0.2f, 0.4f));
			}

			const float faceZ = -0.185f;
			float cy = -header * 0.5f + 0.05f;
			float x0 = -width * 0.5f + margin;
			float homeX0 = x0;
			float colonX = x0 + groupWidth + gap + colonW * 0.5f;
			float awayX0 = x0 + groupWidth + gap + colonW + gap;

			// Team plates above the digits: red = home (the player), blue = away (the horde).
			float plateY = height * 0.5f - 0.3f;
			Primitive(PrimitiveType.Cube, "PlateHome", display, _seatRed, new Vector3(homeX0 + groupWidth * 0.5f, plateY, faceZ), id,
				new Vector3(groupWidth + 0.1f, 0.25f, 0.01f));
			Primitive(PrimitiveType.Cube, "PlateAway", display, _seatBlue, new Vector3(awayX0 + groupWidth * 0.5f, plateY, faceZ), id,
				new Vector3(groupWidth + 0.1f, 0.25f, 0.01f));

			var home = new List<Renderer>();
			var away = new List<Renderer>();
			for (int i = 0; i < digits; i++)
			{
				home.AddRange(SegmentDigit(display, $"Home{i}", homeX0 + dw * 0.5f + i * (dw + gap), cy, faceZ, dw, dh, t));
				away.AddRange(SegmentDigit(display, $"Away{i}", awayX0 + dw * 0.5f + i * (dw + gap), cy, faceZ, dw, dh, t));
			}
			foreach (float side in new[] { -1f, 1f })
				Primitive(PrimitiveType.Cube, "Colon", display, _segmentLit, new Vector3(colonX, cy + side * dh * 0.2f, faceZ), id,
					new Vector3(t * 1.1f, t * 1.1f, 0.01f)).GetComponent<MeshRenderer>().shadowCastingMode =
					UnityEngine.Rendering.ShadowCastingMode.Off;

			var scoreboard = board.gameObject.AddComponent<ArenaScoreboard>();
			SetField(scoreboard, "_homeSegments", home.ToArray());
			SetField(scoreboard, "_awaySegments", away.ToArray());
			SetField(scoreboard, "_litMaterial", _segmentLit);
			SetField(scoreboard, "_offMaterial", _segmentOff);
			scoreboard.SetScore(0, 0); // prefab shows "   :0  "; ScoreboardSystem takes over at runtime
			EditorUtility.SetDirty(scoreboard);
		}

		// One seven-segment digit centred at (cx, cy) on the display face; renderers in a…g order (ArenaScoreboard).
		private static Renderer[] SegmentDigit(Transform display, string name, float cx, float cy, float z, float dw, float dh, float t)
		{
			var digit = Child(display, name);
			float hx = dw * 0.5f - t * 0.5f, hy = dh * 0.5f - t * 0.5f, q = dh * 0.25f;
			var horizontal = new Vector3(dw - t * 2f - 0.02f, t, 0.01f);
			var vertical = new Vector3(t, dh * 0.5f - t * 1.1f, 0.01f);
			var specs = new (string n, Vector3 p, Vector3 s)[]
			{
				("a", new Vector3(cx, cy + hy, z), horizontal),
				("b", new Vector3(cx + hx, cy + q, z), vertical),
				("c", new Vector3(cx + hx, cy - q, z), vertical),
				("d", new Vector3(cx, cy - hy, z), horizontal),
				("e", new Vector3(cx - hx, cy - q, z), vertical),
				("f", new Vector3(cx - hx, cy + q, z), vertical),
				("g", new Vector3(cx, cy, z), horizontal),
			};
			var renderers = new Renderer[specs.Length];
			for (int i = 0; i < specs.Length; i++)
			{
				var go = Primitive(PrimitiveType.Cube, specs[i].n, digit, _segmentOff, specs[i].p, Quaternion.identity, specs[i].s);
				var r = go.GetComponent<MeshRenderer>();
				r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				renderers[i] = r;
			}
			return renderers;
		}

		// ================================================================== mobs / definition / materials

		/// <summary>GunnerBare: a copy of GunnerMob without the helmet (created once, registered in MobConfigHolder).</summary>
		private static void EnsureGunnerBare()
		{
			var config = AssetDatabase.LoadAssetAtPath<MobConfig>(GunnerBarePath);
			if (config == null)
			{
				if (AssetDatabase.LoadAssetAtPath<MobConfig>(GunnerSourcePath) == null ||
				    !AssetDatabase.CopyAsset(GunnerSourcePath, GunnerBarePath))
				{
					Debug.LogError($"[HockeyLevelBuilder] Can't copy {GunnerSourcePath} → {GunnerBarePath}");
					return;
				}
				config = AssetDatabase.LoadAssetAtPath<MobConfig>(GunnerBarePath);
			}
			var so = new SerializedObject(config);
			so.FindProperty("_id").stringValue = GunnerBareId;
			var headwear = so.FindProperty("_headwear");
			if (headwear != null)
				headwear.objectReferenceValue = null;
			// The bare gunner keeps the Gunner prefab's own rifle (Mini-14), single shots: the AR + 3-round burst
			// belong to the helmeted GunnerMob only.
			so.FindProperty("_weaponOverride").objectReferenceValue = null;
			so.FindProperty("_burstCount").intValue = 1;
			so.FindProperty("_cooldown").floatValue = 0.8f;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(config);

			var holder = AssetDatabase.LoadAssetAtPath<MobConfigHolder>(MobHolderPath);
			if (holder == null)
			{
				Debug.LogWarning($"[HockeyLevelBuilder] {MobHolderPath} not found — add {GunnerBareId} by hand.");
				return;
			}
			var hso = new SerializedObject(holder);
			var list = hso.FindProperty("_mobConfigs");
			for (int i = 0; i < list.arraySize; i++)
				if (list.GetArrayElementAtIndex(i).objectReferenceValue == config)
					return;
			list.arraySize++;
			list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = config;
			hso.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(holder);
		}

		/// <summary>
		/// CheerZombie: zombie cheerleader that replaces a forgotten cheerleader (CheerleaderSystem). Same stats and
		/// prefab as Fast for now (copied once from Fast.asset; swap the prefab when the zombie cheerleader model
		/// arrives — re-runs only enforce the id), registered in MobConfigHolder.
		/// </summary>
		private static void EnsureCheerZombie()
		{
			var config = AssetDatabase.LoadAssetAtPath<MobConfig>(CheerZombiePath);
			if (config == null)
			{
				if (AssetDatabase.LoadAssetAtPath<MobConfig>(FastSourcePath) == null ||
				    !AssetDatabase.CopyAsset(FastSourcePath, CheerZombiePath))
				{
					Debug.LogError($"[HockeyLevelBuilder] Can't copy {FastSourcePath} → {CheerZombiePath}");
					return;
				}
				config = AssetDatabase.LoadAssetAtPath<MobConfig>(CheerZombiePath);
			}
			var so = new SerializedObject(config);
			so.FindProperty("_id").stringValue = CheerZombieId;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(config);

			var holder = AssetDatabase.LoadAssetAtPath<MobConfigHolder>(MobHolderPath);
			if (holder == null)
			{
				Debug.LogWarning($"[HockeyLevelBuilder] {MobHolderPath} not found — add {CheerZombieId} by hand.");
				return;
			}
			var hso = new SerializedObject(holder);
			var list = hso.FindProperty("_mobConfigs");
			for (int i = 0; i < list.arraySize; i++)
				if (list.GetArrayElementAtIndex(i).objectReferenceValue == config)
					return;
			list.arraySize++;
			list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = config;
			hso.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(holder);
		}

		private static void BuildHockeyDefinition(GameObject levelPrefab)
		{
			var definition = LoadOrCreate<LevelDefinition>(DefinitionPathH);
			var so = new SerializedObject(definition);
			so.FindProperty("_levelPrefab").objectReferenceValue = levelPrefab;
			so.FindProperty("_displayName").stringValue = "Ледовая арена";
			var first = AssetDatabase.LoadAssetAtPath<LevelDefinition>(FirstDefinitionPath);
			if (first != null && so.FindProperty("_icon").objectReferenceValue == null)
				so.FindProperty("_icon").objectReferenceValue = first.Icon;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(definition);

			// 3rd campaign level (after "Запертая дверь").
			var library = AssetDatabase.LoadAssetAtPath<LevelLibrary>(CampaignLibraryPath);
			if (library == null)
			{
				Debug.LogWarning($"[HockeyLevelBuilder] {CampaignLibraryPath} not found — add {DefinitionPathH} by hand.");
				return;
			}
			var lso = new SerializedObject(library);
			var levels = lso.FindProperty("_levels");
			for (int i = 0; i < levels.arraySize; i++)
				if (levels.GetArrayElementAtIndex(i).objectReferenceValue == definition)
					return;
			int index = Mathf.Min(2, levels.arraySize);
			levels.InsertArrayElementAtIndex(index);
			levels.GetArrayElementAtIndex(index).objectReferenceValue = definition;
			lso.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(library);
		}

		private static void CreateHockeyMaterials()
		{
			// Grey-blue (not white), so loot and its aura read well on the ice. Mat() only creates once → enforce.
			_ice = Mat("Level_Ice", IceColor, 0f, 0.85f);
			_ice.SetColor("_BaseColor", IceColor);
			_ice.SetColor("_Color", IceColor);
			_ice.SetFloat("_Smoothness", 0.85f);
			EditorUtility.SetDirty(_ice);
			_lineRed = Mat("Level_IceLineRed", new Color(0.78f, 0.08f, 0.08f), 0f, 0.9f);
			_lineBlue = Mat("Level_IceLineBlue", new Color(0.10f, 0.25f, 0.75f), 0f, 0.9f);
			_board = Mat("Level_Board", new Color(0.94f, 0.94f, 0.91f), 0f, 0.5f);
			_kick = Mat("Level_BoardKick", new Color(0.95f, 0.78f, 0.15f), 0f, 0.4f);
			_rail = Mat("Level_BoardRail", new Color(0.75f, 0.10f, 0.10f), 0.3f, 0.5f);
			_seatRed = Mat("Level_SeatRed", new Color(0.62f, 0.12f, 0.12f), 0f, 0.35f);
			_seatBlue = Mat("Level_SeatBlue", new Color(0.12f, 0.22f, 0.55f), 0f, 0.35f);
			_net = Mat("Level_GoalNet", new Color(0.85f, 0.85f, 0.85f), 0f, 0.2f);

			_vendingBody = Mat("Level_VendingRed", new Color(0.72f, 0.07f, 0.07f), 0.2f, 0.55f);
			_vendingGlass = Mat("Level_VendingGlass", new Color(0.12f, 0.17f, 0.22f), 0.1f, 0.92f);
			_vendingPanel = Mat("Level_VendingPanel", new Color(0.16f, 0.16f, 0.18f), 0.5f, 0.45f);
			_vendingWhite = Mat("Level_VendingWhite", new Color(0.92f, 0.92f, 0.9f), 0f, 0.4f);
			_stageDeck = Mat("Level_StageDeck", new Color(0.17f, 0.17f, 0.2f), 0f, 0.3f);
			_speaker = Mat("Level_Speaker", new Color(0.05f, 0.05f, 0.06f), 0f, 0.2f);
			_scoreHousing = Mat("Level_ScoreboardHousing", new Color(0.06f, 0.06f, 0.07f), 0.2f, 0.25f);
			_scoreFrame = Mat("Level_ScoreboardFrame", new Color(0.36f, 0.37f, 0.39f), 0.6f, 0.4f);
			// Old amber-red LED segments; the unlit ones stay faintly visible like on a real worn-out board.
			_segmentLit = Mat("Level_SegmentLit", new Color(1f, 0.32f, 0.06f), 0f, 0.6f, new Color(2.4f, 0.55f, 0.08f));
			_segmentOff = Mat("Level_SegmentOff", new Color(0.13f, 0.07f, 0.05f), 0f, 0.5f);

			// Pitch-black unlit lining of the mob tunnels (no light ever brightens it → "out of the darkness").
			_tunnelBlack = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Level_TunnelBlack.mat");
			if (_tunnelBlack == null)
			{
				_tunnelBlack = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Level_TunnelBlack" };
				_tunnelBlack.SetColor("_BaseColor", Color.black);
				_tunnelBlack.enableInstancing = true;
				AssetDatabase.CreateAsset(_tunnelBlack, $"{MaterialFolder}/Level_TunnelBlack.mat");
			}

			_resurfacerWhite = Mat("Level_ResurfacerWhite", new Color(0.93f, 0.94f, 0.95f), 0.1f, 0.6f);
			_tire = Mat("Level_Tire", new Color(0.05f, 0.05f, 0.05f), 0f, 0.15f);
			_headlight = Mat("Level_Headlight", new Color(1f, 0.97f, 0.85f), 0f, 0.9f, new Color(2.2f, 2.1f, 1.7f));
			_beacon = Mat("Level_Beacon", new Color(1f, 0.6f, 0.05f), 0f, 0.8f, new Color(2.4f, 1.3f, 0.1f));

			// Goal net: 256×256 diamond mesh with alpha, cut-out, double-sided. Mat() only creates once → enforce.
			ConfigureNetMaterial(_net, BuildNetTexture());
		}
	}
}
