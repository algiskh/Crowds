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
	///   arena floor  x[-40,40] z[-24,24], enclosed by primitive stands (parapet stops bullets, tiers behind it);
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

		// Arena / rink geometry.
		private const float ArenaX = 40f, ArenaZ = 24f;
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

		[MenuItem("Tools/Crowds/Build Hockey Level")]
		public static void Build()
		{
			EnsureFolder(MaterialFolder);
			CreateMaterials();
			CreateHockeyMaterials();
			BuildWallSparkEffect();
			EnsureGunnerBare();

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
				var tunnels = BuildStands(stands);

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
				foreach (var (pos, inward) in tunnels)
					Spawn(spawns, "Tunnel", pos + inward * 2f, mobs);

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
			Primitive(PrimitiveType.Cube, "NetBack", goal, _net, new Vector3(-dir * d, h * 0.4f, 0), Quaternion.identity,
				new Vector3(0.05f, h * 0.8f, w));
			Primitive(PrimitiveType.Cube, "NetTop", goal, _net, new Vector3(-dir * d * 0.5f, h * 0.9f, 0),
				Quaternion.Euler(0, 0, dir * 20f), new Vector3(d * 1.05f, 0.04f, w));
			foreach (float z in new[] { -w * 0.5f, w * 0.5f })
				Primitive(PrimitiveType.Cube, "NetSide", goal, _net, new Vector3(-dir * d * 0.5f, h * 0.45f, z),
					Quaternion.identity, new Vector3(d, h * 0.9f, 0.04f));
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
		/// Tunnel portals (dark openings with a frame) in the parapet — returned as (point on the edge, inward normal)
		/// for the spawn points.
		/// </summary>
		private static List<(Vector3, Vector3)> BuildStands(Transform parent)
		{
			float depth = 0.4f + StandTiers * TierDepth;
			var tunnels = new List<(Vector3, Vector3)>();

			// (edge centre, outward normal, edge length, seat material, tunnel offsets along the edge)
			var sides = new (Vector3 centre, Vector3 outward, float length, Material seat, float[] tunnelsAt)[]
			{
				(new Vector3(0, 0, ArenaZ), Vector3.forward, ArenaX * 2f + depth * 2f, _seatRed, new[] { -25f, 0f, 25f }),
				(new Vector3(0, 0, -ArenaZ), Vector3.back, ArenaX * 2f + depth * 2f, _seatBlue, new[] { -25f, 0f, 25f }),
				(new Vector3(-ArenaX, 0, 0), Vector3.left, ArenaZ * 2f, _seatBlue, new[] { 0f }),
				(new Vector3(ArenaX, 0, 0), Vector3.right, ArenaZ * 2f, _seatRed, new[] { 0f }),
			};

			int index = 0;
			foreach (var side in sides)
			{
				var stand = Child(parent, $"Stand_{++index}");
				var rot = Quaternion.LookRotation(side.outward); // local +Z = outward, local X = along the edge
				stand.SetPositionAndRotation(new Vector3(side.centre.x, FloorY, side.centre.z), rot);

				// Parapet on the edge line.
				var parapet = Primitive(PrimitiveType.Cube, "Parapet", stand, _wall, new Vector3(0, ParapetHeight * 0.5f, 0),
					Quaternion.identity, new Vector3(side.length, ParapetHeight, 0.8f));
				SparkOnHit(parapet.AddComponent<BulletBlocker>());
				Primitive(PrimitiveType.Cube, "ParapetCap", stand, _wallCap, new Vector3(0, ParapetHeight + 0.03f, 0),
					Quaternion.identity, new Vector3(side.length, 0.06f, 0.85f));

				for (int t = 0; t < StandTiers; t++)
				{
					float top = ParapetHeight + TierRise * (t + 1);
					float z = 0.4f + TierDepth * (t + 0.5f);
					Primitive(PrimitiveType.Cube, $"Tier{t}", stand, _wall, new Vector3(0, top * 0.5f, z), Quaternion.identity,
						new Vector3(side.length, top, TierDepth));
					Primitive(PrimitiveType.Cube, $"Seats{t}", stand, side.seat, new Vector3(0, top + 0.12f, z + 0.25f),
						Quaternion.identity, new Vector3(side.length - 0.4f, 0.24f, 0.45f));
				}

				foreach (float along in side.tunnelsAt)
				{
					Portal(stand, along);
					var edgePoint = stand.TransformPoint(new Vector3(along, 0, 0));
					tunnels.Add((edgePoint, -side.outward));
				}
			}
			return tunnels;
		}

		// Tunnel portal on a stand's parapet (stand-local: X along the edge, −Z = into the arena).
		private static void Portal(Transform stand, float along)
		{
			const float width = 3f, height = 2.4f;
			var portal = Child(stand, "TunnelPortal");
			portal.localPosition = new Vector3(along, 0, 0);
			portal.localRotation = Quaternion.identity;
			Primitive(PrimitiveType.Cube, "Dark", portal, _outletDark, new Vector3(0, height * 0.5f, -0.42f), Quaternion.identity,
				new Vector3(width, height, 0.04f));
			foreach (float side in new[] { -1f, 1f })
				Primitive(PrimitiveType.Cube, "Pillar", portal, _wallCap, new Vector3(side * (width * 0.5f + 0.2f), height * 0.5f, -0.6f),
					Quaternion.identity, new Vector3(0.4f, height, 0.5f));
			Primitive(PrimitiveType.Cube, "Lintel", portal, _wallCap, new Vector3(0, height + 0.2f, -0.6f), Quaternion.identity,
				new Vector3(width + 0.8f, 0.4f, 0.5f));
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
		}
	}
}
