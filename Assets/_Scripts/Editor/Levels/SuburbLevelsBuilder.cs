using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using static Crowds.EditorTools.KeyLevelBuilder;
using SB = Crowds.EditorTools.SuburbBuilder;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Two finite American-suburb levels with the same difficulty as Infinite Scroll — Tools ▸ Crowds ▸ Build Suburb
	/// Levels (Cul-de-sac + Backyards). Safe to re-run (prefabs/meshes rewritten in place, same guids).
	///
	/// Difficulty parity with Infinite Scroll (Level_01): the LevelConfig is a copy of Level1 (same stages, Kill10
	/// gate, ammo fallback) switched to SectorMode.Sliding with one sector; mobs come from the same spawn-point prefabs
	/// (RegularSpawnPoint, GroupSlowSpawnPoint, ToxicPoint at sewer hatches), map loot points / the stage-1 crate
	/// trigger are clones of Level_01's (LootPoint1, BreakableSpawn1), pre-placed pickups are the same MapLoot prefabs.
	///
	///   "Тупик Мейпл-Корт" (Level_04_CulDeSac): a street enters from the left screen edge (barricaded) and ends in a
	///     round turnaround with a raised grass island and a tree. 3 houses on the far side, 2 + an empty lot for sale
	///     on the camera side, 3 around the bulb (houses stand on the walkable floor: carving NavMeshObstacle +
	///     BulletBlocker). Fenced backyards / side yards with pools (slow), sheds, trampolines, swings, grills.
	///   "Задние дворы" (Level_05_Backyards): the backs of two house rows; 6 fenced backyards (privacy dividers with
	///     gates, picket fences with gates onto a gravel service alley down the middle), pools, sheds, trampolines,
	///     swing sets, grills, picnic tables, alley trash cans and lamps.
	///
	/// New props (Assets/_Prefabs/Suburb/Prop_*.prefab, single baked meshes): PrivacyFence, Mailbox, StreetLamp, Tree,
	/// Shed, Pool (ShallowWater zone), Trampoline, SwingSet, Grill, PicnicTable, Barricade, TrashCans, ForSaleSign.
	/// See Docs/SuburbLevelsFeature.md.
	/// </summary>
	public static class SuburbLevelsBuilder
	{
		private const string Log = "[SuburbLevelsBuilder]";
		private const string LevelFolder = "Assets/_Prefabs/Levels";
		private const string LevelMeshFolder = LevelFolder + "/Meshes";
		private const string ConfigFolder = "Assets/_Data/Levels/Configs";
		private const string DefinitionFolder = "Assets/_Data/Levels/Definitions";
		private const string Level01Path = LevelFolder + "/Level_01.prefab";
		private const string Level1ConfigPath = ConfigFolder + "/Level1.asset";
		private const string MatFolder = "Assets/_Materials/Suburb";
		private const string SpawnRegularPath = "Assets/_Prefabs/SpawnPoints/RegularSpawnPoint.prefab";
		private const string SpawnToxicPath = "Assets/_Prefabs/SpawnPoints/ToxicPoint.prefab";
		private const string SpawnGroupPath = "Assets/_Prefabs/SpawnPoints/GroupS/GroupSlowSpawnPoint.prefab";
		private const string HatchPath = "Assets/_Prefabs/Props/SewerHatch.prefab";
		private const string MapLootFolder = "Assets/_Prefabs/MapLoot";
		private const string VehicleFolder = "Assets/_Prefabs/Vehicles";

		private const float Top = FloorY; // -0.5
		// Surface heights above the floor top (visual only, ignored by the navmesh bake). Every pair of overlapping
		// layers is >= 1 cm apart: closer surfaces z-fight at the camera distance, and the blood/scorch decals (projected
		// on both) flicker. Stack: slab < stripes < front paths < sidewalk < road / joints < marks / drives < curbs < island.
		private const float HOuter = 0.004f, HStripe = 0.01f, HPath = 0.02f, HWalk = 0.03f, HJoint = 0.042f,
			HRoad = 0.04f, HMark = 0.05f, HDrive = 0.05f, HCurb = 0.065f, HIsland = 0.075f, HIslandCurb = 0.09f;
		private const float FenceInset = 0.3f;     // perimeter fence line inside the floor edge
		private const float PointClearance = 0.7f; // spawn/loot points must be this far from obstacles

		private static Material _fenceWood, _fenceWoodDark, _metalDark, _metalGalv, _poolWater, _poolWaterDeep, _poolTile;
		private static Material _shedRed, _bark, _leaf, _leafAlt, _trampMat, _trampPad, _swingFrame, _orange, _lampGlow;
		private static Material _amberGlow, _mailBlack, _flagRed, _binGreen, _binGrey, _gravel, _signRed;
		private static Material _hazYellow, _hazBag, _hazBlack, _hazOrange, _biohazard;

		private const string BiohazardTexturePath = "Assets/Textures/Hazmat/Biohazard.png";
		private const string HazmatTruckName = "Truck_Hazmat";

		private static readonly Dictionary<string, GameObject> Props = new();
		private static SB.HouseKit _kit;
		private static GameObject _fenceBreakable;
		private static Transform _lootTemplate, _crateTriggerTemplate;

		private sealed class Ctx
		{
			public Rect Floor;
			public Transform Root, Houses, Props, Fences, Spawns, Loot, Preplaced, Vehicles;
			public GameObject Ground;
			public Vector3 Start;
			public float StartYaw;
			public readonly List<Transform> Points = new();
		}

		private struct Placed
		{
			public Transform House;
			public bool HasDrive;
			public Vector3 DriveStart, DriveEnd, Facing;
		}

		[MenuItem("Tools/Crowds/Build Suburb Levels (Cul-de-sac + Backyards)")]
		public static void Build()
		{
			var level01 = AssetDatabase.LoadAssetAtPath<GameObject>(Level01Path);
			if (level01 == null)
			{
				Debug.LogError($"{Log} {Level01Path} not found — it is the template for loot points.");
				return;
			}
			_lootTemplate = FindChild(level01.transform, "LootPoint1 (1)");
			_crateTriggerTemplate = FindChild(level01.transform, "BreakableSpawn1");
			if (_lootTemplate == null || _crateTriggerTemplate == null)
				Debug.LogWarning($"{Log} LootPoint1 (1) / BreakableSpawn1 not found in Level_01 — map loot points skipped.");

			EnsureFolder(LevelMeshFolder);
			_kit = SB.BuildKit();
			CreatePropMaterials();
			BuildProps();
			_fenceBreakable = BuildBreakableFence();

			var culDeSac = Assemble("Level_04_CulDeSac", "Level_CulDeSac", "Тупик Мейпл-Корт",
				Rect.MinMaxRect(-36f, -50f, 34f, 56f), new Vector3(0f, Top, -30f), 0f, FillCulDeSac);
			var backyards = Assemble("Level_05_Backyards", "Level_Backyards", "Задние дворы",
				Rect.MinMaxRect(-27f, -46f, 27f, 46f), new Vector3(0f, Top, -5f), 0f, FillBackyards);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			CampaignSetup.Apply(); // campaign order (tutorial first) + level icons
			Debug.Log($"{Log} Built {(culDeSac != null ? culDeSac.name : "—")} and {(backyards != null ? backyards.name : "—")}.");
		}

		// ================================================================== level assembly

		private static GameObject Assemble(string prefabName, string assetName, string displayName, Rect floor,
			Vector3 start, float startYaw, System.Action<Ctx> fill)
		{
			var config = CopyConfig($"{ConfigFolder}/{assetName}.asset");
			string prefabPath = $"{LevelFolder}/{prefabName}.prefab";
			var root = new GameObject(prefabName);
			GameObject saved;
			try
			{
				var levelRoot = root.AddComponent<LevelRoot>();

				// One walkable slab (navmesh source), lawn-coloured; everything else on top is visual dressing.
				var floorGo = Child(root.transform, "Floor").gameObject;
				var sector = floorGo.AddComponent<FloorSector>();
				var slab = Child(floorGo.transform, "FloorMesh").gameObject;
				slab.AddComponent<MeshFilter>().sharedMesh = BuildFloorMesh(new[] { floor }, $"{LevelMeshFolder}/{prefabName}_Floor.asset");
				var slabRenderer = slab.AddComponent<MeshRenderer>();
				slabRenderer.sharedMaterial = SB._grass;
				slabRenderer.renderingLayerMask = FloorRenderingLayers;

				var c = new Ctx
				{
					Floor = floor,
					Root = root.transform,
					Houses = Child(root.transform, "Houses"),
					Props = Child(root.transform, "Props"),
					Fences = Child(root.transform, "Fences"),
					Vehicles = Child(root.transform, "Vehicles"),
					Spawns = Child(root.transform, "SpawnPoints"),
					Loot = Child(root.transform, "LootPoints"),
					Preplaced = Child(root.transform, "_PreplacedLoot"),
					Ground = new GameObject($"{prefabName}_Ground"),
					Start = start,
					StartYaw = startYaw,
				};
				try
				{
					// Mowing stripes over the whole floor (2.5 m bands along Z), lighter bands only.
					for (float z = floor.yMin; z < floor.yMax - 0.01f; z += 5f)
						Rect2(c, SB._grassAlt, floor.xMin, floor.xMax, z, Mathf.Min(z + 2.5f, floor.yMax), HStripe);
					fill(c);
					var ground = SB.Bake(c.Ground, $"{prefabName}_Ground", null, castShadows: false, receiveDecals: true,
						extra: go => go.AddComponent<NavMeshModifier>().ignoreFromBuild = true);
					Put(ground, root.transform, "Ground", Vector3.zero, 0f);
				}
				finally
				{
					Object.DestroyImmediate(c.Ground);
				}

				var startT = Child(root.transform, "PlayerStart");
				startT.SetPositionAndRotation(c.Start, Quaternion.Euler(0f, c.StartYaw, 0f));
				c.Points.Add(startT);
				CheckClearance(c);

				var so = new SerializedObject(levelRoot);
				so.FindProperty("_levelConfig").objectReferenceValue = config;
				var sectors = so.FindProperty("_sectors");
				sectors.arraySize = 1;
				sectors.GetArrayElementAtIndex(0).objectReferenceValue = sector;
				so.FindProperty("_playerStart").objectReferenceValue = startT;
				so.ApplyModifiedPropertiesWithoutUndo();

				saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool ok);
				if (!ok)
				{
					Debug.LogError($"{Log} Failed to save {prefabPath}");
					return null;
				}
			}
			finally
			{
				Object.DestroyImmediate(root);
			}

			BuildDefinitionFor(saved, $"{DefinitionFolder}/{assetName}.asset", displayName);
			return saved;
		}

		// Level1 copy (same stages / Kill10 gate / ammo fallback), one sliding sector, arena-like spawn window.
		private static LevelConfig CopyConfig(string path)
		{
			if (AssetDatabase.LoadAssetAtPath<LevelConfig>(path) == null && !AssetDatabase.CopyAsset(Level1ConfigPath, path))
				Debug.LogError($"{Log} Can't copy {Level1ConfigPath} → {path}");
			var config = AssetDatabase.LoadAssetAtPath<LevelConfig>(path);
			SetField(config, "_sectorMode", SectorMode.Sliding);
			SetField(config, "_activeSectorRadius", 1);
			SetField(config, "_spawnDistance", new Vector2(10f, 45f));
			EditorUtility.SetDirty(config);
			return config;
		}

		private static void BuildDefinitionFor(GameObject prefab, string path, string displayName)
		{
			var definition = LoadOrCreate<LevelDefinition>(path);
			var so = new SerializedObject(definition);
			so.FindProperty("_levelPrefab").objectReferenceValue = prefab;
			so.FindProperty("_displayName").stringValue = displayName;
			var first = AssetDatabase.LoadAssetAtPath<LevelDefinition>(FirstDefinitionPath);
			if (first != null && so.FindProperty("_icon").objectReferenceValue == null)
				so.FindProperty("_icon").objectReferenceValue = first.Icon;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(definition);

			// Appended to the campaign (existing order untouched).
			var library = AssetDatabase.LoadAssetAtPath<LevelLibrary>(CampaignLibraryPath);
			if (library == null)
			{
				Debug.LogWarning($"{Log} {CampaignLibraryPath} not found — add {path} by hand.");
				return;
			}
			var lso = new SerializedObject(library);
			var levels = lso.FindProperty("_levels");
			for (int i = 0; i < levels.arraySize; i++)
				if (levels.GetArrayElementAtIndex(i).objectReferenceValue == definition)
					return;
			levels.arraySize++;
			levels.GetArrayElementAtIndex(levels.arraySize - 1).objectReferenceValue = definition;
			lso.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(library);
		}

		// ================================================================== level 4: cul-de-sac

		private static readonly Vector3 Bulb = new(0f, 0f, 22f);
		private const float BulbR = 13f, BulbWalkR = 14.5f, IslandR = 4f, StreetHalf = 4.5f, WalkOuter = 6f;

		private static void FillCulDeSac(Ctx c)
		{
			var f = c.Floor;
			float streetEnd = Bulb.z - Mathf.Sqrt(BulbR * BulbR - StreetHalf * StreetHalf); // where the curbs meet the ring

			// --- Outside the fence: lawns all round + the street running on past the barricade (left screen edge).
			OuterGround(c, 26f);
			Rect2(c, SB._concrete, -WalkOuter, WalkOuter, f.yMin - 26f, f.yMin, HWalk);
			Rect2(c, SB._asphalt, -StreetHalf, StreetHalf, f.yMin - 26f, f.yMin, HRoad);

			// --- Street: sidewalks + asphalt + curbs + dashed centre line + sidewalk joints.
			Rect2(c, SB._concrete, -WalkOuter, WalkOuter, f.yMin, streetEnd + 1f, HWalk);
			Rect2(c, SB._asphalt, -StreetHalf, StreetHalf, f.yMin - 26f, streetEnd + 2f, HRoad);
			foreach (float x in new[] { -StreetHalf, StreetHalf })
				Rect2(c, SB._curb, x - 0.125f, x + 0.125f, f.yMin - 26f, streetEnd, HCurb);
			for (float z = f.yMin - 25f; z < streetEnd - 3f; z += 10f)
				Rect2(c, SB._lineYellow, -0.075f, 0.075f, z, z + 3f, HMark);
			for (float z = f.yMin - 26f; z < streetEnd; z += 1.5625f)
				foreach (float x in new[] { -(StreetHalf + WalkOuter) * 0.5f, (StreetHalf + WalkOuter) * 0.5f })
					SB.Box(c.Ground, SB._joint, new Vector3(x, Top + HJoint - 0.002f, z), new Vector3(WalkOuter - StreetHalf, 0.004f, 0.04f));

			// --- Turnaround: sidewalk ring, asphalt, curb (open toward the street), raised grass island with a tree.
			Disc(c.Ground, SB._concrete, new Vector3(Bulb.x, Top + HWalk, Bulb.z), BulbWalkR);
			Disc(c.Ground, SB._asphalt, new Vector3(Bulb.x, Top + HRoad, Bulb.z), BulbR);
			float gap = Mathf.Asin((StreetHalf + 0.2f) / BulbR) * Mathf.Rad2Deg;
			CurbRing(c, Bulb, BulbR, 48, 270f, gap, HCurb);
			Disc(c.Ground, SB._grass, new Vector3(Bulb.x, Top + HIsland, Bulb.z), IslandR);
			CurbRing(c, Bulb, IslandR, 24, 0f, 0f, HIslandCurb);
			Put(Prop("Tree"), c.Props, "Tree_Island", new Vector3(Bulb.x, Top + HIsland, Bulb.z), 20f);

			// --- Houses. Far side (top of the screen) faces +X, camera side faces −X, the bulb ring faces its centre.
			var far = new[]
			{
				(_kit.Ranch, SB._sidingCream, SB._roofCharcoal, SB._doorRed, -38f),
				(_kit.Colonial, SB._sidingSage, SB._roofBrown, SB._doorBlack, -20f),
				(_kit.Cape, SB._sidingBlue, SB._roofSlate, SB._doorNavy, -2f),
			};
			var placed = new List<Placed>();
			foreach (var (spec, siding, roof, door, z) in far)
				placed.Add(House(c, spec, siding, roof, door, new Vector3(-17f, Top, z), Vector3.right,
					17f - WalkOuter, 17f - StreetHalf, onFloor: true));
			var near = new[]
			{
				(_kit.Colonial, SB._sidingYellow, SB._roofCharcoal, SB._doorGreen, -38f),
				(_kit.Ranch, SB._sidingGrey, SB._roofSlate, SB._doorRed, -18f),
			};
			foreach (var (spec, siding, roof, door, z) in near)
				placed.Add(House(c, spec, siding, roof, door, new Vector3(17f, Top, z), Vector3.left,
					17f - WalkOuter, 17f - StreetHalf, onFloor: true));
			var ring = new[]
			{
				(_kit.Cape, SB._sidingWhite, SB._roofCharcoal, SB._doorGreen, 150f),
				(_kit.Ranch, SB._sidingBeige, SB._roofBrown, SB._doorNavy, 90f),
				(_kit.Colonial, SB._sidingWhite, SB._roofSlate, SB._doorRed, 30f),
			};
			const float ringFront = 21f;
			foreach (var (spec, siding, roof, door, angle) in ring)
			{
				var dir = Dir(angle);
				placed.Add(House(c, spec, siding, roof, door, Bulb + dir * ringFront + Vector3.up * Top, -dir,
					ringFront - BulbWalkR, ringFront - BulbR, onFloor: true));
			}

			// Empty lot on the camera side, between the last house and the bulb.
			Put(Prop("ForSaleSign"), c.Props, "ForSale", new Vector3(8.5f, Top, -1f), -90f);

			// --- Vehicles: two on driveways (nose to the garage), one at the kerb, the ice-cream van in the bulb.
			ParkOnDrive(c, "Sedan_Navy", placed[0]);
			ParkOnDrive(c, "Pickup_Red", placed[4]);
			ParkOnDrive(c, "Sedan_Red", placed[6]);
			Vehicle(c, "Sedan_Silver", new Vector3(-3.3f, Top + HRoad, -12f), 180f);
			var vanDir = Dir(340f);
			Vehicle(c, "Truck_IceCream", Bulb + vanDir * 9.3f + Vector3.up * (Top + HRoad), 20f);

			// --- Street furniture.
			foreach (float z in new[] { -44f, -26f, -8f })
				Put(Prop("StreetLamp"), c.Props, "Lamp", new Vector3(-5.3f, Top + HWalk, z), 90f);
			foreach (float z in new[] { -34f, -10f, 4f })
				Put(Prop("StreetLamp"), c.Props, "Lamp", new Vector3(5.3f, Top + HWalk, z), -90f);
			foreach (float angle in new[] { 205f, 335f, 120f })
			{
				var d = Dir(angle);
				Put(Prop("StreetLamp"), c.Props, "Lamp", Bulb + d * 13.85f + Vector3.up * (Top + HWalk), Yaw(-d));
			}
			// Road closed at the screen edge: two barricades with gaps (centre + sidewalks) mobs squeeze through.
			Put(Prop("Barricade"), c.Props, "Barricade_W", new Vector3(-2.5f, Top + HRoad, f.yMin + 2.5f), 0f);
			Put(Prop("Barricade"), c.Props, "Barricade_E", new Vector3(2.5f, Top + HRoad, f.yMin + 2.5f), 0f);

			// --- Lawns: trees and hedges on the lot lines.
			foreach (var p in new[]
			         {
				         new Vector3(-8f, Top, -24f), new Vector3(-8.5f, Top, -6f), new Vector3(9f, Top, -33f),
				         new Vector3(12.5f, Top, 6f), new Vector3(-27f, Top, 12f), new Vector3(-22f, Top, 50f),
				         new Vector3(22f, Top, 50f), new Vector3(31f, Top, -28f), new Vector3(-11f, Top, 9f),
			         })
				Put(Prop("Tree"), c.Props, "Tree", p, p.x * 37f + p.z * 11f);
			Put(_kit.Hedge, c.Props, "Hedge_1", new Vector3(-13f, Top + 0.01f, -29f), 0f);
			Put(_kit.Hedge, c.Props, "Hedge_2", new Vector3(-13f, Top + 0.01f, -11f), 0f);
			Put(_kit.Hedge, c.Props, "Hedge_3", new Vector3(13f, Top + 0.01f, -28f), 0f);

			// --- Backyards and side yards.
			Put(Prop("Pool"), c.Props, "Pool_W", new Vector3(-31.5f, Top, -29f), 90f);
			Put(Prop("Shed"), c.Props, "Shed_W", new Vector3(-31.5f, Top, -44.5f), 90f);
			Put(Prop("Trampoline"), c.Props, "Trampoline_W", new Vector3(-31.5f, Top, -12f), 0f);
			Put(Prop("Grill"), c.Props, "Grill_W", new Vector3(-29.5f, Top, -3.5f), 30f);
			Put(Prop("PicnicTable"), c.Props, "Picnic_W", new Vector3(-32.5f, Top, -1f), 90f);
			Put(Prop("SwingSet"), c.Props, "Swings_NW", new Vector3(-18f, Top, 15f), 0f);
			Put(Prop("Pool"), c.Props, "Pool_NE", new Vector3(21f, Top, 13.5f), 0f);
			Put(Prop("Shed"), c.Props, "Shed_E", new Vector3(30.5f, Top, -45.5f), -90f);
			Put(Prop("Grill"), c.Props, "Grill_E", new Vector3(29.5f, Top, -33f), -40f);
			Put(Prop("PicnicTable"), c.Props, "Picnic_E", new Vector3(30.5f, Top, -23f), 90f);
			Put(Prop("Trampoline"), c.Props, "Trampoline_NE", new Vector3(28.5f, Top, 22f), 0f);

			// --- Perimeter: privacy fence all round, open where the street leaves (barricades there).
			float x0 = f.xMin + FenceInset, x1 = f.xMax - FenceInset, z0 = f.yMin + FenceInset, z1 = f.yMax - FenceInset;
			PrivacyRun(c, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), "Fence_W");
			PrivacyRun(c, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), "Fence_E");
			PrivacyRun(c, new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), "Fence_N");
			PrivacyRun(c, new Vector3(x0, 0, z0), new Vector3(-WalkOuter - 0.5f, 0, z0), "Fence_S_W");
			PrivacyRun(c, new Vector3(WalkOuter + 0.5f, 0, z0), new Vector3(x1, 0, z0), "Fence_S_E");

			// --- Mobs: behind the barricade, in the backyards, behind the bulb houses; toxics from the sewers.
			Group(c, new Vector3(0f, Top, f.yMin + 0.9f));
			foreach (var p in new[]
			         {
				         new Vector3(-3.5f, Top, f.yMin + 0.9f), new Vector3(3.5f, Top, f.yMin + 0.9f),
				         new Vector3(-34.3f, Top, -38f), new Vector3(-34.3f, Top, -20f), new Vector3(-34.3f, Top, 5f),
				         new Vector3(-34.3f, Top, 20f), new Vector3(-30f, Top, 50f), new Vector3(-8f, Top, 54.5f),
				         new Vector3(8f, Top, 54.5f), new Vector3(30f, Top, 50f), new Vector3(31.5f, Top, -38f),
				         new Vector3(31.5f, Top, -15f), new Vector3(31.5f, Top, 2f), new Vector3(31.5f, Top, 14f),
			         })
				Regular(c, p);
			foreach (var p in new[]
			         {
				         new Vector3(-2.2f, 0, -40f), new Vector3(2.2f, 0, -18f), new Vector3(-2.5f, 0, 2f),
				         Bulb + Dir(60f) * 7f,
			         })
				ToxicHatch(c, new Vector3(p.x, Top + HRoad + 0.002f, p.z));

			// --- Loot (Level_01 parity): 3 map loot points, the stage-1 crate trigger, pre-placed pickups.
			LootPoint(c, new Vector3(-3f, Top, -20f));
			LootPoint(c, new Vector3(9f, Top, 8f));
			LootPoint(c, new Vector3(-14f, Top, 22f));
			CrateTrigger(c, new Vector3(9f, Top, -40f));
			MapLoot(c, "Shotgun", new Vector3(1.5f, Top, -6f));
			MapLoot(c, "SpeedUp", new Vector3(11f, Top, -2f));
			MapLoot(c, "Shield", new Vector3(-12f, Top, 18f));
			MapLoot(c, "Grenade", new Vector3(6.5f, Top, -22f));
			MapLoot(c, "Grenade", new Vector3(-8f, Top, 9f));
			MapLoot(c, "Grenade", new Vector3(7f, Top, 30f));
		}

		// ================================================================== level 5: backyards

		private const float AlleyHalf = 3f, YardFenceX = 3.2f, LotFenceX = 26.8f;

		private static void FillBackyards(Ctx c)
		{
			var f = c.Floor;
			OuterGround(c, 34f);
			// Streets the houses face, off the floor on both sides (sidewalk + road), for the "real block" look.
			foreach (float s in new[] { -1f, 1f })
			{
				Rect2(c, SB._concrete, Mathf.Min(s * 45f, s * 46.5f), Mathf.Max(s * 45f, s * 46.5f), f.yMin - 34f, f.yMax + 34f, HWalk);
				Rect2(c, SB._asphalt, Mathf.Min(s * 46.5f, s * 58f), Mathf.Max(s * 46.5f, s * 58f), f.yMin - 34f, f.yMax + 34f, HRoad);
			}
			// Gravel service alley down the middle (runs on past both ends).
			Rect2(c, _gravel, -AlleyHalf, AlleyHalf, f.yMin - 34f, f.yMax + 34f, HWalk);

			// --- Houses: backs to the yards, just off the floor; fences close the gaps between them.
			var west = new[]
			{
				(_kit.Colonial, SB._sidingCream, SB._roofSlate, SB._doorNavy, -30.5f),
				(_kit.Ranch, SB._sidingWhite, SB._roofCharcoal, SB._doorRed, 0f),
				(_kit.Cape, SB._sidingSage, SB._roofBrown, SB._doorBlack, 30.5f),
			};
			var east = new[]
			{
				(_kit.Cape, SB._sidingYellow, SB._roofCharcoal, SB._doorGreen, -30.5f),
				(_kit.Colonial, SB._sidingGrey, SB._roofBrown, SB._doorRed, 0f),
				(_kit.Ranch, SB._sidingBlue, SB._roofSlate, SB._doorBlack, 30.5f),
			};
			foreach (float s in new[] { -1f, 1f })
			{
				var spans = new List<(float min, float max)>();
				foreach (var (spec, siding, roof, door, z) in s < 0 ? west : east)
				{
					// The house mesh reaches back (−Z local) to its back stoop; that end touches the floor edge.
					float depth = -spec.Prefab.GetComponent<MeshFilter>().sharedMesh.bounds.min.z;
					var facing = new Vector3(s, 0f, 0f);
					var lot = new Vector3(s * (f.xMax + 0.05f + depth), Top, z);
					var p = House(c, spec, siding, roof, door, lot, facing, 45f - Mathf.Abs(lot.x),
						46.5f - Mathf.Abs(lot.x), onFloor: false);
					spans.Add(Span(p.House, spec));
				}
				spans.Sort((a, b) => a.min.CompareTo(b.min));
				float x = s * LotFenceX, cursor = f.yMin + FenceInset;
				int n = 0;
				foreach (var (min, max) in spans)
				{
					PrivacyRun(c, new Vector3(x, 0, cursor), new Vector3(x, 0, min), $"Fence_Lots_{(s < 0 ? "W" : "E")}{++n}");
					cursor = max;
				}
				PrivacyRun(c, new Vector3(x, 0, cursor), new Vector3(x, 0, f.yMax - FenceInset), $"Fence_Lots_{(s < 0 ? "W" : "E")}{++n}");

				// Yard dividers (privacy, 3 m gate) and the picket fences onto the alley (3 m gate per yard). One
				// section of every divider line is breakable (boards missing / askew): shoot or hack through it for a
				// shortcut between the yards. The middle segment of the house half (x 26.8…16) or of the alley half
				// (13…3.2), alternating, so the four breaks are spread out; the ends by the spawn points stay solid.
				foreach (float zd in new[] { -15f, 15f })
				{
					bool outer = (s < 0) == (zd > 0); // W: house half at +15, alley half at −15; E: the other way round
					PrivacyRun(c, new Vector3(s * LotFenceX, 0, zd), new Vector3(s * 16f, 0, zd), "Divider", outer ? new[] { 2 } : null);
					PrivacyRun(c, new Vector3(s * 13f, 0, zd), new Vector3(s * YardFenceX, 0, zd), "Divider", outer ? null : new[] { 2 });
				}
				foreach (var (za, zb) in new[] { (f.yMin + FenceInset, -15f), (-15f, 15f), (15f, f.yMax - FenceInset) })
				{
					float mid = (za + zb) * 0.5f;
					PicketRun(c, new Vector3(s * YardFenceX, 0, za), new Vector3(s * YardFenceX, 0, mid - 1.5f), "Picket");
					PicketRun(c, new Vector3(s * YardFenceX, 0, mid + 1.5f), new Vector3(s * YardFenceX, 0, zb), "Picket");
				}
				// Ends of the yards (the alley ends stay open: mobs come down the alley).
				foreach (float ze in new[] { f.yMin + FenceInset, f.yMax - FenceInset })
					PrivacyRun(c, new Vector3(s * LotFenceX, 0, ze), new Vector3(s * YardFenceX, 0, ze), "Fence_End");
			}
			// The alley itself is closed by barricades a metre inside the floor ends.
			foreach (float ze in new[] { f.yMin + 2.2f, f.yMax - 2.2f })
				Put(Prop("Barricade"), c.Props, "Barricade", new Vector3(0f, Top + HWalk, ze), 0f);

			// --- Yards (W = top of the screen, E = camera side; rows 1..3 along +Z).
			Put(Prop("Pool"), c.Props, "Pool_W1", new Vector3(-15f, Top, -31f), 90f);
			Put(Prop("Tree"), c.Props, "Tree_W1", new Vector3(-24f, Top, -19f), 12f);
			Put(Prop("PicnicTable"), c.Props, "Picnic_W1", new Vector3(-7f, Top, -40f), 0f);
			Put(Prop("Trampoline"), c.Props, "Trampoline_W2", new Vector3(-20f, Top, 6f), 0f);
			Put(Prop("SwingSet"), c.Props, "Swings_W2", new Vector3(-10f, Top, -7f), 0f);
			Put(Prop("Grill"), c.Props, "Grill_W2", new Vector3(-23f, Top, -9f), 15f);
			Put(_kit.Hedge, c.Props, "Hedge_W2", new Vector3(-8f, Top + 0.01f, 8f), 90f);
			Put(Prop("Shed"), c.Props, "Shed_W3", new Vector3(-23f, Top, 41f), 90f);
			// Yard W3 is a biohazard response site: decontamination tent, piles of yellow waste bags (toxic stench:
			// light poison + green fog), a hazmat truck in the alley; fast toxic zombies come out of here.
			Put(Prop("HazmatTent"), c.Props, "HazmatTent_W3", new Vector3(-14f, Top, 29f), 90f);
			Put(Prop("HazmatBags"), c.Props, "HazmatBags_W3a", new Vector3(-20.5f, Top, 22f), 20f);
			Put(Prop("HazmatBags"), c.Props, "HazmatBags_W3b", new Vector3(-20f, Top, 33.5f), 140f);
			Put(Prop("HazmatBags"), c.Props, "HazmatBags_W3c", new Vector3(-8.5f, Top, 33.5f), 260f);
			Put(Prop("HazmatBags"), c.Props, "HazmatBags_Alley", new Vector3(2.1f, Top + HWalk, 19f), 75f);
			Put(Prop("Tree"), c.Props, "Tree_W3", new Vector3(-7.5f, Top, 40f), 47f);
			Put(Prop("Shed"), c.Props, "Shed_E1", new Vector3(23f, Top, -41.5f), -90f);
			Put(Prop("Trampoline"), c.Props, "Trampoline_E1", new Vector3(13f, Top, -26f), 0f);
			Put(Prop("Tree"), c.Props, "Tree_E1", new Vector3(8f, Top, -40f), 75f);
			Put(Prop("PicnicTable"), c.Props, "Picnic_E1", new Vector3(21f, Top, -21f), 90f);
			Put(Prop("Pool"), c.Props, "Pool_E2", new Vector3(16f, Top, 0f), 90f);
			Put(Prop("Tree"), c.Props, "Tree_E2", new Vector3(24f, Top, 11f), 33f);
			Put(Prop("SwingSet"), c.Props, "Swings_E3", new Vector3(15f, Top, 37f), 0f);
			Put(Prop("Grill"), c.Props, "Grill_E3", new Vector3(21f, Top, 22f), -20f);
			Put(_kit.Hedge, c.Props, "Hedge_E3", new Vector3(8f, Top + 0.01f, 26f), 90f);
			Put(Prop("Tree"), c.Props, "Tree_E3", new Vector3(23f, Top, 41f), 5f);

			// --- Alley: trash cans, lamps, a pickup left in the alley.
			Put(Prop("TrashCans"), c.Props, "Bins", new Vector3(-2.3f, Top + HWalk, -20f), 90f);
			Put(Prop("TrashCans"), c.Props, "Bins", new Vector3(2.3f, Top + HWalk, 8f), -90f);
			Put(Prop("TrashCans"), c.Props, "Bins", new Vector3(-2.3f, Top + HWalk, 37f), 90f);
			foreach (float z in new[] { -30f, 0f, 30f })
				Put(Prop("StreetLamp"), c.Props, "AlleyLamp", new Vector3(-2.6f, Top + HWalk, z + 4f), 90f);
			Vehicle(c, "Pickup_Teal", new Vector3(0.6f, Top + HWalk, -36f), 0f);
			Vehicle(c, HazmatTruckName, new Vector3(-1f, Top + HWalk, 24f), 0f);

			// --- Mobs: yard corners by the houses, both alley ends; toxics from the alley sewers.
			Group(c, new Vector3(-1.5f, Top, f.yMax - 0.9f));
			foreach (float s in new[] { -1f, 1f })
				foreach (var (x, z) in new[]
				         {
					         (25.5f, -44.5f), (6f, -44.5f), (25.5f, -13.5f), (25.5f, 13.5f), (25.5f, 44.5f), (6f, 44.5f),
				         })
					Regular(c, new Vector3(s * x, Top, z));
			Regular(c, new Vector3(0f, Top, f.yMin + 0.9f));
			Regular(c, new Vector3(1.5f, Top, f.yMax - 0.9f));
			foreach (var z in new[] { -22f, 12f, 30f })
				ToxicHatch(c, new Vector3(0f, Top + HWalk + 0.002f, z));
			// Fast toxic zombies from the biohazard site (Level1-copy stages: from stage 3 on, faster later).
			var fastToxic = new[]
			{
				Mob("FastToxic", 1, (DifficultyLevel.level3, 14f), (DifficultyLevel.level4, 12f), (DifficultyLevel.level5, 10f),
					(DifficultyLevel.level6, 10f), (DifficultyLevel.level7, 9f), (DifficultyLevel.level8, 8f)),
			};
			foreach (var pos in new[] { new Vector3(-18f, Top, 26f), new Vector3(-10f, Top, 37f), new Vector3(2.2f, Top, 28f) })
			{
				Spawn(c.Spawns, "Hazmat", pos, fastToxic);
				c.Points.Add(c.Spawns.GetChild(c.Spawns.childCount - 1));
			}

			// --- Loot.
			LootPoint(c, new Vector3(-9f, Top, 0f));
			LootPoint(c, new Vector3(9f, Top, -31f));
			LootPoint(c, new Vector3(10f, Top, 31f));
			CrateTrigger(c, new Vector3(16f, Top, 11f));
			MapLoot(c, "Shotgun", new Vector3(0f, Top, 4f));
			MapLoot(c, "SpeedUp", new Vector3(-14.5f, Top, -13f));
			MapLoot(c, "Shield", new Vector3(15f, Top, 20f));
			MapLoot(c, "Grenade", new Vector3(-5f, Top, -30f));
			MapLoot(c, "Grenade", new Vector3(5f, Top, 30f));
			MapLoot(c, "Grenade", new Vector3(-20f, Top, 30f));
		}

		// ================================================================== placement helpers

		// House whose front-centre (middle of its MinX..MaxX at the front wall) is at lotPoint, facing `facing`.
		// Front walk (from the porch steps) and driveway (from the garage) run straight along `facing`; walkLen and
		// driveLen are measured from the front wall (the walk starts at the steps, so it is StepsZ shorter).
		private static Placed House(Ctx c, SB.HouseSpec spec, Material siding, Material roof, Material door,
			Vector3 lotPoint, Vector3 facing, float walkLen, float driveLen, bool onFloor)
		{
			facing.y = 0f;
			facing.Normalize();
			var rot = Quaternion.LookRotation(facing);
			var origin = lotPoint - rot * new Vector3((spec.MinX + spec.MaxX) * 0.5f, 0f, 0f);
			origin.y = Top;
			var go = (GameObject)PrefabUtility.InstantiatePrefab(spec.Prefab, c.Houses);
			go.name = $"{spec.Prefab.name}_{c.Houses.childCount}";
			go.transform.SetPositionAndRotation(origin, rot);
			SB.Recolor(go, siding, roof, door);
			if (onFloor)
			{
				// Houses ignore the bake (prefab NavMeshModifier) — on a walkable floor they carve instead.
				var b = go.GetComponent<MeshFilter>().sharedMesh.bounds;
				Carve(go, b.center, b.size + new Vector3(0.2f, 0f, 0.2f));
			}

			var side = Vector3.Cross(Vector3.up, facing);
			if (walkLen - spec.StepsZ > 0.3f)
			{
				var a = origin + rot * new Vector3(spec.DoorX, 0f, spec.StepsZ);
				var e = a + facing * (walkLen - spec.StepsZ);
				Strip(c, SB._drive, a, e, 1.2f, HPath);
				Put(Prop("Mailbox"), c.Props, "Mailbox", e - facing * 0.4f + side * 1.1f, Yaw(facing));
			}
			var placed = new Placed { House = go.transform, Facing = facing };
			if (spec.GarageX.HasValue && driveLen > 0.3f)
			{
				var a = origin + rot * new Vector3(spec.GarageX.Value, 0f, 0f);
				var e = a + facing * driveLen;
				Strip(c, SB._drive, a, e, 3.2f, HDrive);
				placed.HasDrive = true;
				placed.DriveStart = a;
				placed.DriveEnd = e;
				Put(Prop("TrashCans"), c.Props, "Bins", e - facing * 1.2f + side * 2.3f, Yaw(-side));
			}
			return placed;
		}

		// World Z span of a house's MinX..MaxX (for houses turned to face ±X).
		private static (float min, float max) Span(Transform house, SB.HouseSpec spec)
		{
			float a = house.TransformPoint(new Vector3(spec.MinX, 0, 0)).z;
			float b = house.TransformPoint(new Vector3(spec.MaxX, 0, 0)).z;
			return (Mathf.Min(a, b), Mathf.Max(a, b));
		}

		private static void ParkOnDrive(Ctx c, string vehicle, Placed house)
		{
			if (!house.HasDrive)
			{
				Debug.LogWarning($"{Log} {house.House.name} has no driveway — {vehicle} not parked.");
				return;
			}
			var pos = house.DriveStart + house.Facing * 3.6f;
			pos.y = Top + HDrive;
			Vehicle(c, vehicle, pos, Yaw(-house.Facing));
		}

		private static void Vehicle(Ctx c, string name, Vector3 pos, float yaw)
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{VehicleFolder}/{name}.prefab");
			if (prefab == null)
			{
				Debug.LogWarning($"{Log} {name} not found (run Build Street Vehicles first).");
				return;
			}
			Put(prefab, c.Vehicles, name, pos, yaw);
		}

		private static void Regular(Ctx c, Vector3 pos) => SpawnPrefab(c, SpawnRegularPath, "RegularSpawnPoint", pos);
		private static void Group(Ctx c, Vector3 pos) => SpawnPrefab(c, SpawnGroupPath, "GroupSpawnPoint", pos);

		private static void SpawnPrefab(Ctx c, string path, string name, Vector3 pos)
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			if (prefab == null)
			{
				Debug.LogError($"{Log} {path} not found.");
				return;
			}
			var t = Put(prefab, c.Spawns, $"{name}_{c.Spawns.childCount + 1}", pos, 0f).transform;
			c.Points.Add(t);
		}

		// Sewer hatch (light poison zone) + a ToxicPoint on it: toxic zombies only crawl out of the sewers.
		private static void ToxicHatch(Ctx c, Vector3 pos)
		{
			var hatch = AssetDatabase.LoadAssetAtPath<GameObject>(HatchPath);
			if (hatch != null)
				Put(hatch, c.Props, "SewerHatch", pos, 0f);
			SpawnPrefab(c, SpawnToxicPath, "ToxicPoint_Hatch", new Vector3(pos.x, Top, pos.z));
		}

		private static void LootPoint(Ctx c, Vector3 pos)
		{
			if (_lootTemplate == null)
				return;
			var go = Object.Instantiate(_lootTemplate.gameObject, c.Loot);
			go.name = $"LootPoint_{c.Loot.childCount}";
			go.transform.SetPositionAndRotation(pos, Quaternion.identity);
			c.Points.Add(go.transform);
		}

		private static void CrateTrigger(Ctx c, Vector3 pos)
		{
			if (_crateTriggerTemplate == null)
				return;
			var go = Object.Instantiate(_crateTriggerTemplate.gameObject, c.Loot);
			go.name = "BreakableSpawn1";
			go.transform.SetPositionAndRotation(pos, Quaternion.identity);
			c.Points.Add(go.transform);
		}

		private static void MapLoot(Ctx c, string name, Vector3 pos)
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MapLootFolder}/{name}.prefab");
			if (prefab == null)
			{
				Debug.LogWarning($"{Log} MapLoot {name} not found.");
				return;
			}
			c.Points.Add(Put(prefab, c.Preplaced, name, pos, 0f).transform);
		}

		private static GameObject Put(GameObject prefab, Transform parent, string name, Vector3 pos, float yaw)
		{
			var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
			go.name = name;
			go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
			return go;
		}

		private static GameObject Prop(string name) =>
			Props.TryGetValue(name, out var prefab) ? prefab : throw new System.ArgumentException($"No prop {name}");

		private static Vector3 Dir(float degrees)
		{
			float r = degrees * Mathf.Deg2Rad;
			return new Vector3(Mathf.Cos(r), 0f, Mathf.Sin(r));
		}

		private static float Yaw(Vector3 forward) => Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

		// ================================================================== fences

		// breakAt: segment indices (from a) replaced by the breakable fence section (Backyards).
		private static void PrivacyRun(Ctx c, Vector3 a, Vector3 b, string name, params int[] breakAt) =>
			FenceRun(c, Prop("PrivacyFence"), 2.4f, 1.85f, a, b, name, blocksBullets: true, breakAt);

		private static void PicketRun(Ctx c, Vector3 a, Vector3 b, string name) =>
			FenceRun(c, _kit.Fence, 2f, 1.1f, a, b, name, blocksBullets: false);

		// Segments along a → b (stretched to fit exactly); the run root carries one carving obstacle (+ BulletBlocker
		// for privacy fences), the segments themselves are ignored by the bake. Segments listed in breakAt become
		// breakable sections (own collider + carving obstacle, no BulletBlocker over them — the breakable itself stops
		// the bullet and takes the damage); the solid stretches between them get one "Solid" child each instead.
		private static void FenceRun(Ctx c, GameObject segment, float segmentLength, float height, Vector3 a, Vector3 b,
			string name, bool blocksBullets, int[] breakAt = null)
		{
			a.y = b.y = Top;
			var d = b - a;
			float length = d.magnitude;
			if (length < 0.3f)
				return;
			var run = Child(c.Fences, name);
			run.SetPositionAndRotation(a, Quaternion.Euler(0f, Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg, 0f));
			int n = Mathf.Max(1, Mathf.CeilToInt(length / segmentLength - 0.05f));
			float s = length / n;
			var breaks = new HashSet<int>();
			if (breakAt != null && _fenceBreakable != null)
				foreach (int i in breakAt)
					if (i >= 0 && i < n)
						breaks.Add(i);
			for (int i = 0; i < n; i++)
			{
				bool broken = breaks.Contains(i);
				var go = (GameObject)PrefabUtility.InstantiatePrefab(broken ? _fenceBreakable : segment, run);
				go.name = broken ? $"Breakable_{i + 1}" : $"Segment_{i + 1}";
				go.transform.SetLocalPositionAndRotation(new Vector3(s * i, 0f, 0f), Quaternion.identity);
				go.transform.localScale = new Vector3(s / segmentLength, 1f, 1f);
			}
			run.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			if (breaks.Count == 0)
			{
				var center = new Vector3(length * 0.5f, height * 0.5f, -0.03f);
				var size = new Vector3(length, height, 0.35f);
				Carve(run.gameObject, center, size);
				if (blocksBullets)
					Blocker(run.gameObject, center, size);
				return;
			}
			for (int i = 0; i < n;)
			{
				if (breaks.Contains(i))
				{
					i++;
					continue;
				}
				int j = i;
				while (j < n && !breaks.Contains(j))
					j++;
				var solid = Child(run, $"Solid_{i + 1}-{j}");
				float x0 = s * i, x1 = s * j;
				var center = new Vector3((x0 + x1) * 0.5f, height * 0.5f, -0.03f);
				var size = new Vector3(x1 - x0, height, 0.35f);
				Carve(solid.gameObject, center, size);
				if (blocksBullets)
					Blocker(solid.gameObject, center, size);
				i = j;
			}
		}

		private static void Carve(GameObject go, Vector3 center, Vector3 size)
		{
			var o = go.AddComponent<NavMeshObstacle>();
			o.shape = NavMeshObstacleShape.Box;
			o.center = center;
			o.size = size;
			o.carving = true;
			o.carveOnlyStationary = true;
		}

		private static void Blocker(GameObject go, Vector3 center, Vector3 size)
		{
			var blocker = go.AddComponent<BulletBlocker>();
			SetField(blocker, "_center", center);
			SetField(blocker, "_size", size);
			SparkOnHit(blocker);
		}

		// Warns about spawn/loot points inside (or too close to) obstacles or off the floor.
		private static void CheckClearance(Ctx c)
		{
			var boxes = new List<(Bounds bounds, string name)>();
			foreach (var o in c.Root.GetComponentsInChildren<NavMeshObstacle>(true))
			{
				var m = o.transform.localToWorldMatrix;
				Vector3 half = o.shape == NavMeshObstacleShape.Box
					? o.size * 0.5f
					: new Vector3(o.radius, o.height * 0.5f, o.radius);
				var bounds = new Bounds(m.MultiplyPoint3x4(o.center), Vector3.zero);
				for (int i = 0; i < 8; i++)
				{
					var corner = o.center + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y,
						(i & 4) == 0 ? -half.z : half.z);
					bounds.Encapsulate(m.MultiplyPoint3x4(corner));
				}
				bounds.Expand(new Vector3(PointClearance * 2f, 100f, PointClearance * 2f));
				boxes.Add((bounds, o.name));
			}
			int warnings = 0;
			foreach (var p in c.Points)
			{
				var pos = p.position;
				if (!c.Floor.Contains(new Vector2(pos.x, pos.z)))
				{
					Debug.LogWarning($"{Log} {c.Root.name}/{p.name} at ({pos.x:0.#}, {pos.z:0.#}) is off the floor.");
					warnings++;
					continue;
				}
				foreach (var (bounds, name) in boxes)
				{
					if (!bounds.Contains(new Vector3(pos.x, bounds.center.y, pos.z)))
						continue;
					Debug.LogWarning($"{Log} {c.Root.name}/{p.name} at ({pos.x:0.#}, {pos.z:0.#}) is within {PointClearance} m of '{name}'.");
					warnings++;
					break;
				}
			}
			Debug.Log($"{Log} {c.Root.name}: {c.Points.Count} points checked, {warnings} warnings.");
		}

		// ================================================================== ground dressing

		// Axis-aligned surface slab, top at Top + h.
		private static void Rect2(Ctx c, Material m, float x0, float x1, float z0, float z1, float h) =>
			SB.Box(c.Ground, m, new Vector3((x0 + x1) * 0.5f, Top + h - 0.01f, (z0 + z1) * 0.5f),
				new Vector3(x1 - x0, 0.02f, z1 - z0));

		// Surface strip of `width` from a to b (any direction in XZ), top at Top + h.
		private static void Strip(Ctx c, Material m, Vector3 a, Vector3 b, float width, float h)
		{
			a.y = b.y = 0f;
			var d = b - a;
			if (d.sqrMagnitude < 0.01f)
				return;
			var centre = (a + b) * 0.5f;
			SB.Box(c.Ground, m, new Vector3(centre.x, Top + h - 0.01f, centre.z), new Vector3(width, 0.02f, d.magnitude),
				Quaternion.LookRotation(d));
		}

		// Lawn all round the floor (outside the fence), so nothing floats over the void.
		private static void OuterGround(Ctx c, float margin)
		{
			var f = c.Floor;
			Rect2(c, SB._grass, f.xMin - margin, f.xMin, f.yMin - margin, f.yMax + margin, HOuter);
			Rect2(c, SB._grass, f.xMax, f.xMax + margin, f.yMin - margin, f.yMax + margin, HOuter);
			Rect2(c, SB._grass, f.xMin, f.xMax, f.yMin - margin, f.yMin, HOuter);
			Rect2(c, SB._grass, f.xMin, f.xMax, f.yMax, f.yMax + margin, HOuter);
		}

		// Ring of curb blocks around `centre`, leaving out ±gap degrees around `gapAt`.
		private static void CurbRing(Ctx c, Vector3 centre, float radius, int segments, float gapAt, float gap, float h)
		{
			float length = 2f * Mathf.PI * radius / segments * 1.04f;
			for (int i = 0; i < segments; i++)
			{
				float angle = (i + 0.5f) * 360f / segments;
				if (gap > 0f && Mathf.Abs(Mathf.DeltaAngle(angle, gapAt)) < gap)
					continue;
				var d = Dir(angle);
				var p = centre + d * radius;
				var tangent = new Vector3(-d.z, 0f, d.x);
				SB.Box(c.Ground, SB._curb, new Vector3(p.x, Top + h - 0.01f, p.z), new Vector3(0.25f, 0.02f, length),
					Quaternion.LookRotation(tangent));
			}
		}

		private static Mesh _disc;

		// Flat disc (top face only) of radius r, centred at pos (pos.y = top).
		private static void Disc(GameObject t, Material m, Vector3 pos, float r)
		{
			_disc ??= BuildDiscMesh(64);
			var go = new GameObject("Disc");
			go.transform.SetParent(t.transform, false);
			go.transform.localPosition = pos;
			go.transform.localScale = new Vector3(r, 1f, r);
			go.AddComponent<MeshFilter>().sharedMesh = _disc;
			go.AddComponent<MeshRenderer>().sharedMaterial = m;
		}

		private static Mesh BuildDiscMesh(int segments)
		{
			var verts = new Vector3[segments + 1];
			var normals = new Vector3[segments + 1];
			var uvs = new Vector2[segments + 1];
			var tris = new int[segments * 3];
			normals[0] = Vector3.up;
			uvs[0] = new Vector2(0.5f, 0.5f);
			for (int i = 0; i < segments; i++)
			{
				float a = i * Mathf.PI * 2f / segments;
				verts[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				normals[i + 1] = Vector3.up;
				uvs[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
				tris[i * 3] = 0;
				tris[i * 3 + 1] = i + 2 > segments ? 1 : i + 2;
				tris[i * 3 + 2] = i + 1;
			}
			var mesh = new Mesh { name = "Disc", vertices = verts, normals = normals, uv = uvs, triangles = tris };
			mesh.RecalculateBounds();
			return mesh;
		}

		private static void Prim(GameObject t, PrimitiveType type, Material m, Vector3 pos, Vector3 scale, Quaternion? rot = null)
		{
			var go = GameObject.CreatePrimitive(type);
			Object.DestroyImmediate(go.GetComponent<Collider>());
			go.transform.SetParent(t.transform, false);
			go.transform.SetLocalPositionAndRotation(pos, rot ?? Quaternion.identity);
			go.transform.localScale = scale;
			go.GetComponent<MeshRenderer>().sharedMaterial = m;
		}

		// ================================================================== props

		private static void BuildProps()
		{
			Props.Clear();
			Props["PrivacyFence"] = BuildPrivacyFence();
			Props["Mailbox"] = BuildMailbox();
			Props["StreetLamp"] = BuildStreetLamp();
			Props["Tree"] = BuildTree();
			Props["Shed"] = BuildShed();
			Props["Pool"] = BuildPool();
			Props["Trampoline"] = BuildTrampoline();
			Props["SwingSet"] = BuildSwingSet();
			Props["Grill"] = BuildGrill();
			Props["PicnicTable"] = BuildPicnicTable();
			Props["Barricade"] = BuildBarricade();
			Props["TrashCans"] = BuildTrashCans();
			Props["ForSaleSign"] = BuildForSaleSign();
			Props["HazmatBags"] = BuildHazmatBags();
			Props["HazmatTent"] = BuildHazmatTent();
			BuildHazmatTruck();
		}

		private static GameObject BakeProp(GameObject t, string name, bool castShadows, System.Action<GameObject> extra = null)
		{
			try
			{
				return SB.Bake(t, name, null, castShadows, false, go =>
				{
					go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
					extra?.Invoke(go);
				});
			}
			finally
			{
				Object.DestroyImmediate(t);
			}
		}

		// Solid prop: carves the navmesh by its mesh bounds and stops bullets.
		private static void Solid(GameObject go, bool blocksBullets)
		{
			var b = go.GetComponent<MeshFilter>().sharedMesh.bounds;
			Carve(go, b.center, b.size);
			if (blocksBullets)
				Blocker(go, b.center, b.size);
		}

		// 2.4 m of 1.85 m vertical-board privacy fence; local X = 0..2.4 along the fence, rails on the −Z side.
		private static GameObject BuildPrivacyFence()
		{
			var t = SB.Temp("Prop_PrivacyFence");
			const float L = 2.4f, H = 1.8f;
			foreach (float x in new[] { 0.06f, L - 0.06f })
				SB.Box(t, _fenceWoodDark, new Vector3(x, 0.95f, -0.06f), new Vector3(0.1f, 1.9f, 0.1f));
			foreach (float y in new[] { 0.35f, 1.45f })
				SB.Box(t, _fenceWoodDark, new Vector3(L * 0.5f, y, -0.04f), new Vector3(L, 0.09f, 0.04f));
			const int boards = 17;
			float w = L / boards;
			for (int i = 0; i < boards; i++)
				SB.Box(t, i % 4 == 1 ? _fenceWoodDark : _fenceWood, new Vector3(w * (i + 0.5f), 0.05f + H * 0.5f, 0f),
					new Vector3(w - 0.012f, H, 0.02f));
			SB.Box(t, _fenceWoodDark, new Vector3(L * 0.5f, H + 0.07f, -0.02f), new Vector3(L, 0.05f, 0.09f));
			return BakeProp(t, "Prop_PrivacyFence", castShadows: true);
		}

		// ================================================================== breakable fence

		private const string FenceBreakablePrefabPath = "Assets/_Prefabs/Breakables/PrivacyFenceBroken.prefab";
		private const string FenceBreakableConfigPath = "Assets/_Data/Breakables/PrivacyFence.asset";
		private const string FenceBreakableId = "PrivacyFence";
		private const string BoxBreakEffectId = "box_break"; // wood splinters (MallLevelBuilder builds the effect)

		/// <summary>
		/// A privacy-fence section that can be shot / blown / hacked through (Backyards). It reads as damaged: boards
		/// missing, a few knocked askew (one hanging off a rail, one leaning out), the cap rail snapped over the gap,
		/// a couple of boards on the grass. Same 2.4 m local frame as Prop_PrivacyFence, so it drops into a run in
		/// place of a segment. Destroyed → debris (posts, stumps, boards on the ground), no collider, no carving.
		/// </summary>
		private static GameObject BuildBreakableFence()
		{
			var config = LoadOrCreate<BreakableConfig>(FenceBreakableConfigPath);
			const float L = 2.4f, H = 1.8f;
			const int boards = 17;
			float w = L / boards;
			var prefab = SavePrefab(FenceBreakablePrefabPath, root =>
			{
				Material Board(int i) => i % 4 == 1 ? _fenceWoodDark : _fenceWood;
				Vector3 BoardSize(float h) => new(w - 0.012f, h, 0.02f);

				// --- Intact (damaged-looking) section.
				var intact = new GameObject("Intact_Temp");
				foreach (float x in new[] { 0.06f, L - 0.06f })
					SB.Box(intact, _fenceWoodDark, new Vector3(x, 0.95f, -0.06f), new Vector3(0.1f, 1.9f, 0.1f));
				foreach (float y in new[] { 0.35f, 1.45f })
					SB.Box(intact, _fenceWoodDark, new Vector3(L * 0.5f, y, -0.04f), new Vector3(L, 0.09f, 0.04f));
				var missing = new HashSet<int> { 6, 7, 12 };
				for (int i = 0; i < boards; i++)
				{
					if (missing.Contains(i))
						continue;
					float x = w * (i + 0.5f);
					switch (i)
					{
						case 5: // knocked sideways, still nailed at the bottom
							SB.Box(intact, Board(i), new Vector3(x + 0.1f, 0.05f + H * 0.5f, 0f), BoardSize(H), Quaternion.Euler(0, 0, -9f));
							break;
						case 8: // hanging off the top rail by one nail, swung into the gap
							SB.Box(intact, Board(i), new Vector3(x - 0.22f, 0.55f + H * 0.4f, 0.03f), BoardSize(H * 0.8f), Quaternion.Euler(0, 0, 24f));
							break;
						case 11: // snapped: only the lower half left
							SB.Box(intact, Board(i), new Vector3(x, 0.05f + 0.42f, 0f), BoardSize(0.84f), Quaternion.Euler(0, 0, 4f));
							break;
						case 13: // leaning out of the fence (reads from above)
							SB.Box(intact, Board(i), new Vector3(x, 0.05f + H * 0.48f, 0.2f), BoardSize(H * 0.96f), Quaternion.Euler(14f, 0, -3f));
							break;
						default:
							SB.Box(intact, Board(i), new Vector3(x, 0.05f + H * 0.5f, 0f), BoardSize(H));
							break;
					}
				}
				// Cap rail snapped over the gap: two pieces, the right one sagging.
				float gapX = w * 6.5f;
				SB.Box(intact, _fenceWoodDark, new Vector3(gapX * 0.5f, H + 0.07f, -0.02f), new Vector3(gapX - 0.05f, 0.05f, 0.09f));
				SB.Box(intact, _fenceWoodDark, new Vector3((gapX + L) * 0.5f + 0.02f, H - 0.02f, -0.02f), new Vector3(L - gapX - 0.1f, 0.05f, 0.09f),
					Quaternion.Euler(0, 0, 6f));
				// Fallen boards on the grass in front of the gap.
				SB.Box(intact, _fenceWood, new Vector3(w * 7f, 0.012f, 0.55f), new Vector3(w - 0.012f, 0.02f, H * 0.95f), Quaternion.Euler(0, 18f, 0));
				SB.Box(intact, _fenceWoodDark, new Vector3(w * 12.3f, 0.012f, -0.6f), new Vector3(w - 0.012f, 0.02f, H * 0.7f), Quaternion.Euler(0, -32f, 0));
				var intactVisual = VenueKit.Combine(intact, root.transform, "Prop_PrivacyFenceBroken_Intact", castShadows: true);

				// --- Debris: posts stay, rails and boards down, short stumps along the line.
				var debris = new GameObject("Debris_Temp");
				var random = new System.Random(11);
				float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
				foreach (float x in new[] { 0.06f, L - 0.06f })
					SB.Box(debris, _fenceWoodDark, new Vector3(x, 0.95f, -0.06f), new Vector3(0.1f, 1.9f, 0.1f));
				SB.Box(debris, _fenceWoodDark, new Vector3(0.5f, 0.3f, -0.05f), new Vector3(0.9f, 0.09f, 0.04f), Quaternion.Euler(0, 0, -18f));
				SB.Box(debris, _fenceWoodDark, new Vector3(L * 0.55f, 0.03f, -0.5f), new Vector3(L * 0.8f, 0.05f, 0.09f), Quaternion.Euler(0, 9f, 0));
				for (int i = 0; i < boards; i += 2)
				{
					float h = R(0.12f, 0.45f);
					SB.Box(debris, Board(i), new Vector3(w * (i + 0.5f), 0.05f + h * 0.5f, 0f), BoardSize(h), Quaternion.Euler(0, 0, R(-8f, 8f)));
				}
				for (int i = 0; i < 7; i++)
				{
					float side = i % 2 == 0 ? 1f : -1f;
					SB.Box(debris, Board(i), new Vector3(R(0.3f, L - 0.3f), 0.012f + i * 0.004f, side * R(0.3f, 1.1f)),
						new Vector3(w - 0.012f, 0.02f, R(0.7f, H)), Quaternion.Euler(0, R(-60f, 60f), 0));
				}
				var debrisVisual = VenueKit.Combine(debris, root.transform, "Prop_PrivacyFenceBroken_Debris", castShadows: false);
				debrisVisual.SetActive(false);

				var burst = Child(root.transform, "Burst");
				burst.localPosition = new Vector3(L * 0.5f, 0.9f, 0f);

				var collider = root.AddComponent<BoxCollider>();
				collider.center = new Vector3(L * 0.5f, 0.95f, -0.03f);
				collider.size = new Vector3(L, 1.9f, 0.3f);
				root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				var obstacle = root.AddComponent<NavMeshObstacle>();
				obstacle.shape = NavMeshObstacleShape.Box;
				obstacle.center = collider.center;
				obstacle.size = new Vector3(L, 1.9f, 0.35f);
				obstacle.carving = true;
				obstacle.carveOnlyStationary = true;

				var breakable = root.AddComponent<Breakable>();
				SetField(breakable, "_config", config);
				SetField(breakable, "_collider", collider);
				SetField(breakable, "_obstacle", obstacle);
				SetField(breakable, "_intactVisual", intactVisual);
				SetField(breakable, "_debrisVisual", debrisVisual);
				SetField(breakable, "_effectPoints", new[] { burst });
			});

			// Config: 80 HP, bullets / explosions / melee, splinters, no loot, debris left behind (navmesh freed).
			var so = new SerializedObject(config);
			so.FindProperty("_id").stringValue = FenceBreakableId;
			so.FindProperty("_prefab").objectReferenceValue = prefab != null ? prefab.GetComponent<Breakable>() : null;
			so.FindProperty("_maxHealth").floatValue = 80f;
			so.FindProperty("_damageableBy").intValue =
				(int)(BreakableDamageSources.Bullet | BreakableDamageSources.Explosion | BreakableDamageSources.Melee);
			var effects = so.FindProperty("_destructionEffects");
			effects.arraySize = 1;
			var fx = effects.GetArrayElementAtIndex(0);
			fx.FindPropertyRelative("EffectId").stringValue = BoxBreakEffectId;
			fx.FindPropertyRelative("Delay").floatValue = 0f;
			fx.FindPropertyRelative("PointIndex").intValue = 0;
			so.FindProperty("_damageOnDestruction").boolValue = false;
			so.FindProperty("_lootTable").arraySize = 0;
			so.FindProperty("_lootCount").intValue = 0;
			so.FindProperty("_afterDestruction").enumValueIndex = (int)AfterDestruction.Debris;
			so.FindProperty("_debrisKeepsObstacle").boolValue = false;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(config);
			return prefab;
		}

		private static GameObject BuildMailbox()
		{
			var t = SB.Temp("Prop_Mailbox");
			SB.Box(t, SB._trim, new Vector3(0, 0.55f, 0), new Vector3(0.09f, 1.1f, 0.09f));
			SB.Box(t, _mailBlack, new Vector3(0, 1.2f, 0.05f), new Vector3(0.24f, 0.22f, 0.5f));
			SB.Box(t, _mailBlack, new Vector3(0, 1.33f, 0.05f), new Vector3(0.17f, 0.06f, 0.5f));
			SB.Box(t, _flagRed, new Vector3(0.135f, 1.3f, -0.05f), new Vector3(0.02f, 0.2f, 0.05f));
			SB.Box(t, _flagRed, new Vector3(0.135f, 1.39f, 0.03f), new Vector3(0.02f, 0.05f, 0.18f));
			return BakeProp(t, "Prop_Mailbox", castShadows: true);
		}

		// Arm and lamp head along local +Z (turn it toward the street).
		private static GameObject BuildStreetLamp()
		{
			var t = SB.Temp("Prop_StreetLamp");
			SB.Box(t, _metalDark, new Vector3(0, 0.15f, 0), new Vector3(0.35f, 0.3f, 0.35f));
			SB.Box(t, _metalDark, new Vector3(0, 2.4f, 0), new Vector3(0.12f, 4.5f, 0.12f));
			SB.Box(t, _metalDark, new Vector3(0, 4.62f, 0.6f), new Vector3(0.08f, 0.08f, 1.3f));
			SB.Box(t, _metalDark, new Vector3(0, 4.56f, 1.2f), new Vector3(0.4f, 0.16f, 0.6f));
			SB.Box(t, _lampGlow, new Vector3(0, 4.47f, 1.2f), new Vector3(0.32f, 0.02f, 0.5f));
			return BakeProp(t, "Prop_StreetLamp", castShadows: true);
		}

		// Boxy tree: trunk + a cluster of turned cubes. The trunk carves the navmesh.
		private static GameObject BuildTree()
		{
			var t = SB.Temp("Prop_Tree");
			SB.Box(t, _bark, new Vector3(0, 1.1f, 0), new Vector3(0.38f, 2.2f, 0.38f));
			SB.Box(t, _leaf, new Vector3(0, 3.0f, 0), new Vector3(2.9f, 1.9f, 2.9f), Quaternion.Euler(0, 15f, 0));
			SB.Box(t, _leafAlt, new Vector3(0.3f, 4.2f, -0.2f), new Vector3(2.1f, 1.5f, 2.1f), Quaternion.Euler(0, 40f, 0));
			SB.Box(t, _leaf, new Vector3(-0.8f, 3.4f, 0.7f), new Vector3(1.7f, 1.4f, 1.7f), Quaternion.Euler(0, -20f, 0));
			SB.Box(t, _leafAlt, new Vector3(0.9f, 3.2f, 0.8f), new Vector3(1.5f, 1.2f, 1.5f), Quaternion.Euler(0, 5f, 0));
			return BakeProp(t, "Prop_Tree", castShadows: true, go =>
			{
				var o = go.AddComponent<NavMeshObstacle>();
				o.shape = NavMeshObstacleShape.Capsule;
				o.center = new Vector3(0, 1f, 0);
				o.radius = 0.35f;
				o.height = 2f;
				o.carving = true;
				o.carveOnlyStationary = true;
			});
		}

		// Garden shed (house frame: front +Z at z = 0, body to −Z): red walls, grey gable roof, double doors.
		private static GameObject BuildShed()
		{
			var t = SB.Temp("Prop_Shed");
			const float W = 3.2f, D = 2.6f, wall = 2.1f, roofH = 0.9f, found = 0.12f;
			SB.Box(t, SB._foundation, new Vector3(0, found * 0.5f, -D * 0.5f), new Vector3(W + 0.1f, found, D + 0.1f));
			SB.Box(t, _shedRed, new Vector3(0, found + wall * 0.5f, -D * 0.5f), new Vector3(W, wall, D));
			SB.Prism(t, _shedRed, new Vector3(0, found + wall, -D * 0.5f), new Vector3(W, roofH, D));
			SB.GableRoof(t, 0f, W, D, found + wall, roofH, 0.15f);
			var front = SB.Face(t, new Vector3(0, found, 0f), 0f);
			foreach (float s in new[] { -1f, 1f })
			{
				SB.Box(front, SB._trim, new Vector3(s * 0.42f, 0.95f, 0.03f), new Vector3(0.8f, 1.9f, 0.06f));
				SB.Box(front, _fenceWoodDark, new Vector3(s * 0.42f, 0.95f, 0.065f), new Vector3(0.07f, 1.95f, 0.02f),
					Quaternion.Euler(0, 0, s * 22f));
			}
			var side = SB.Face(t, new Vector3(W * 0.5f, found, -D * 0.5f), 90f);
			SB.Window(side, 0f, 1.4f, 0.6f, 0.5f, shutters: false);
			return BakeProp(t, "Prop_Shed", castShadows: true, go => Solid(go, blocksBullets: true));
		}

		// In-ground pool 9 × 4.5 (local X × Z) with coping, deck, ladder, diving board and two loungers.
		// Walk-through: a ShallowWater zone slows whoever wades in.
		private static GameObject BuildPool()
		{
			var t = SB.Temp("Prop_Pool");
			const float L = 9f, W = 4.5f;
			void Slab(Material m, float x, float z, float sx, float sz, float top) =>
				SB.Box(t, m, new Vector3(x, top - 0.01f, z), new Vector3(sx, 0.02f, sz));
			Slab(SB._porch, 0, 0, L + 3f, W + 3f, 0.012f);
			Slab(_poolWater, 0, 0, L, W, 0.025f);
			Slab(_poolWaterDeep, L * 0.25f, 0, L * 0.5f - 0.1f, W - 0.1f, 0.035f); // >= 1 cm over the water: no z-fight
			foreach (float s in new[] { -1f, 1f })
			{
				Slab(_poolTile, 0, s * (W * 0.5f + 0.2f), L + 0.8f, 0.4f, 0.06f);
				Slab(_poolTile, s * (L * 0.5f + 0.2f), 0, 0.4f, W, 0.06f);
			}
			float lx = -L * 0.5f + 1.2f;
			foreach (float s in new[] { -0.25f, 0.25f })
			{
				SB.Box(t, _metalGalv, new Vector3(lx + s, 0.45f, W * 0.5f + 0.05f), new Vector3(0.05f, 0.9f, 0.05f));
				SB.Box(t, _metalGalv, new Vector3(lx + s, 0.9f, W * 0.5f - 0.12f), new Vector3(0.05f, 0.05f, 0.35f));
			}
			SB.Box(t, SB._trim, new Vector3(L * 0.5f + 0.9f, 0.36f, 0), new Vector3(1.8f, 0.06f, 0.5f));
			SB.Box(t, _metalGalv, new Vector3(L * 0.5f + 1.6f, 0.17f, 0), new Vector3(0.3f, 0.34f, 0.5f));
			foreach (float x in new[] { -2f, 1f })
			{
				float z = -(W * 0.5f + 0.95f);
				SB.Box(t, SB._trim, new Vector3(x, 0.25f, z), new Vector3(1.7f, 0.08f, 0.65f));
				SB.Box(t, SB._trim, new Vector3(x - 0.85f, 0.45f, z), new Vector3(0.08f, 0.45f, 0.65f), Quaternion.Euler(0, 0, -25f));
			}
			return BakeProp(t, "Prop_Pool", castShadows: false,
				go => go.AddComponent<ShallowWater>().ApplyDefaults(new Vector2(L, W)));
		}

		private static GameObject BuildTrampoline()
		{
			var t = SB.Temp("Prop_Trampoline");
			const float r = 1.75f, h = 0.92f;
			Disc(t, _trampMat, new Vector3(0, h, 0), r);
			CurbLike(t, _trampPad, r + 0.18f, 24, h + 0.02f, 0.36f, 0.06f);
			for (int i = 0; i < 6; i++)
			{
				var d = Dir(i * 60f + 30f);
				SB.Box(t, _metalGalv, d * (r + 0.2f) + Vector3.up * (h * 0.5f), new Vector3(0.06f, h, 0.06f));
			}
			return BakeProp(t, "Prop_Trampoline", castShadows: true, go =>
			{
				var o = go.AddComponent<NavMeshObstacle>();
				o.shape = NavMeshObstacleShape.Capsule;
				o.center = new Vector3(0, 0.5f, 0);
				o.radius = r + 0.3f;
				o.height = 1f;
				o.carving = true;
				o.carveOnlyStationary = true;
			});
		}

		// Ring of boxes (trampoline pad).
		private static void CurbLike(GameObject t, Material m, float radius, int segments, float y, float width, float height)
		{
			float length = 2f * Mathf.PI * radius / segments * 1.04f;
			for (int i = 0; i < segments; i++)
			{
				var d = Dir((i + 0.5f) * 360f / segments);
				SB.Box(t, m, d * radius + Vector3.up * y, new Vector3(width, height, length),
					Quaternion.LookRotation(new Vector3(-d.z, 0f, d.x)));
			}
		}

		// A-frame swing set along local X, two swings. Walk-through.
		private static GameObject BuildSwingSet()
		{
			var t = SB.Temp("Prop_SwingSet");
			foreach (float x in new[] { -1.8f, 1.8f })
				foreach (float s in new[] { -1f, 1f })
					SB.Box(t, _swingFrame, new Vector3(x, 1.12f, s * 0.32f), new Vector3(0.08f, 2.4f, 0.08f),
						Quaternion.Euler(-s * 15f, 0, 0));
			SB.Box(t, _swingFrame, new Vector3(0, 2.3f, 0), new Vector3(3.8f, 0.1f, 0.1f));
			foreach (float x in new[] { -0.8f, 0.8f })
			{
				foreach (float s in new[] { -0.2f, 0.2f })
					SB.Box(t, _metalGalv, new Vector3(x + s, 1.55f, 0), new Vector3(0.02f, 1.5f, 0.02f));
				SB.Box(t, _trampMat, new Vector3(x, 0.78f, 0), new Vector3(0.5f, 0.04f, 0.2f));
			}
			return BakeProp(t, "Prop_SwingSet", castShadows: true);
		}

		private static GameObject BuildGrill()
		{
			var t = SB.Temp("Prop_Grill");
			for (int i = 0; i < 3; i++)
			{
				var d = Dir(i * 120f);
				SB.Box(t, _metalDark, d * 0.22f + Vector3.up * 0.4f, new Vector3(0.04f, 0.8f, 0.04f));
			}
			Prim(t, PrimitiveType.Sphere, _metalDark, new Vector3(0, 0.88f, 0), new Vector3(0.6f, 0.45f, 0.6f));
			SB.Box(t, _metalGalv, new Vector3(0, 1.13f, 0), new Vector3(0.15f, 0.04f, 0.04f));
			return BakeProp(t, "Prop_Grill", castShadows: true);
		}

		private static GameObject BuildPicnicTable()
		{
			var t = SB.Temp("Prop_PicnicTable");
			SB.Box(t, SB._porch, new Vector3(0, 0.76f, 0), new Vector3(1.9f, 0.05f, 0.8f));
			foreach (float s in new[] { -1f, 1f })
				SB.Box(t, SB._porch, new Vector3(0, 0.45f, s * 0.62f), new Vector3(1.9f, 0.05f, 0.3f));
			foreach (float x in new[] { -0.7f, 0.7f })
			{
				foreach (float s in new[] { -1f, 1f })
					SB.Box(t, SB._porch, new Vector3(x, 0.38f, s * 0.25f), new Vector3(0.08f, 0.82f, 0.08f),
						Quaternion.Euler(s * 28f, 0, 0));
				SB.Box(t, SB._porch, new Vector3(x, 0.42f, 0), new Vector3(0.08f, 0.06f, 1.55f));
			}
			return BakeProp(t, "Prop_PicnicTable", castShadows: true, go => Solid(go, blocksBullets: false));
		}

		// "Road closed" sawhorse: striped boards along local X, amber lamp; carves, shoot over it.
		private static GameObject BuildBarricade()
		{
			var t = SB.Temp("Prop_Barricade");
			const float L = 2.6f;
			const int stripes = 8;
			foreach (float y in new[] { 0.55f, 0.9f })
				for (int i = 0; i < stripes; i++)
					SB.Box(t, i % 2 == 0 ? _orange : SB._trim, new Vector3(-L * 0.5f + L / stripes * (i + 0.5f), y, 0),
						new Vector3(L / stripes, 0.22f, 0.04f));
			foreach (float x in new[] { -1.1f, 1.1f })
				foreach (float s in new[] { -1f, 1f })
					SB.Box(t, SB._trim, new Vector3(x, 0.5f, s * 0.18f), new Vector3(0.06f, 1.02f, 0.06f), Quaternion.Euler(-s * 12f, 0, 0));
			SB.Box(t, _amberGlow, new Vector3(-1f, 1.1f, 0), new Vector3(0.12f, 0.12f, 0.12f));
			return BakeProp(t, "Prop_Barricade", castShadows: true, go => Solid(go, blocksBullets: false));
		}

		private static GameObject BuildTrashCans()
		{
			var t = SB.Temp("Prop_TrashCans");
			foreach (var (x, m) in new[] { (-0.36f, _binGreen), (0.36f, _binGrey) })
			{
				SB.Box(t, m, new Vector3(x, 0.52f, 0), new Vector3(0.58f, 0.98f, 0.68f));
				SB.Box(t, m, new Vector3(x, 1.04f, 0.02f), new Vector3(0.62f, 0.06f, 0.74f));
				foreach (float s in new[] { -0.22f, 0.22f })
					SB.Box(t, _trampMat, new Vector3(x + s, 0.09f, -0.3f), new Vector3(0.06f, 0.18f, 0.18f));
			}
			return BakeProp(t, "Prop_TrashCans", castShadows: true);
		}

		// Flat emblem quad (biohazard sign) at pos, facing 'normal'.
		private static void Emblem(GameObject t, Vector3 pos, Vector3 normal, float size, Vector3? up = null)
		{
			// Unity's quad is visible from its -Z side: point -Z along the normal.
			var rot = Quaternion.LookRotation(-normal, up ?? (Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up));
			Prim(t, PrimitiveType.Quad, _biohazard, pos, new Vector3(size, size, 1f), rot);
		}

		// Pile of yellow biohazard waste bags + a hazard drum. Walk-through (decor) but stinks: a SewerHatch-type zone
		// (light poison for the player, 1 HP / 0.5 s) with the Toxic zombie's green fog.
		// Bags after Assets/REFs/toxic_plastic_bag.jpg (kept yellow): a full, squarish, slightly sagging body, folds
		// gathering into a twisted neck, a knot and a flared tuft of plastic on top, a CAUTION/BIOHAZARD label.
		private static GameObject BuildHazmatBags()
		{
			var t = SB.Temp("Prop_HazmatBags");
			var meshes = new List<Mesh>();
			// (base position, half width X, half depth Z, height, yaw, tilt X/Z) - upright bags stand on the ground.
			var bags = new[]
			{
				(new Vector3(-0.5f, 0f, -0.25f), 0.34f, 0.28f, 0.82f, 10f, new Vector2(4f, -6f)),
				(new Vector3(0.32f, 0f, -0.45f), 0.32f, 0.27f, 0.76f, 55f, new Vector2(-5f, 5f)),
				(new Vector3(0.25f, 0f, 0.42f), 0.35f, 0.3f, 0.86f, -30f, new Vector2(3f, 4f)),
				(new Vector3(-0.45f, 0f, 0.62f), 0.3f, 0.26f, 0.72f, 80f, new Vector2(-6f, -3f)),
				(new Vector3(0.98f, 0f, 0.15f), 0.29f, 0.25f, 0.68f, -60f, new Vector2(0f, 9f)),
			};
			int seed = 0;
			foreach (var (pos, w, d, h, yaw, tilt) in bags)
				Bag(t, meshes, pos, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt.x, 0f, tilt.y), w, d, h, ++seed);
			// One bag thrown on top, lying on its side with the label facing up.
			Bag(t, meshes, new Vector3(0.22f, 0.6f, 0.05f), Quaternion.Euler(0f, 25f, 0f) * Quaternion.Euler(0f, 0f, 78f),
				0.32f, 0.26f, 0.78f, ++seed);
			// Hazard drum.
			Prim(t, PrimitiveType.Cylinder, _hazYellow, new Vector3(-1.25f, 0.45f, -0.55f), new Vector3(0.6f, 0.45f, 0.6f));
			foreach (float y in new[] { 0.25f, 0.65f })
				Prim(t, PrimitiveType.Cylinder, _hazBlack, new Vector3(-1.25f, y, -0.55f), new Vector3(0.62f, 0.03f, 0.62f));
			Emblem(t, new Vector3(-1.25f, 0.905f, -0.55f), Vector3.up, 0.5f);
			var prefab = BakeProp(t, "Prop_HazmatBags", castShadows: true, go =>
			{
				var zone = go.AddComponent<SewerHatch>();
				SetField(zone, "_radius", 1.7f);
				SetField(zone, "_linger", 0.3f);
				SetField(zone, "_affectsMobs", false);
				SetField(zone, "_entryEffect", true);
				SetField(zone, "_modifiers", new Modifier[] { SewerHatch.CreateDefaultStench() });
				var toxic = AssetDatabase.LoadAssetAtPath<GameObject>(ToxicMobPrefabPath);
				var fogSource = toxic != null ? FindChild(toxic.transform, ToxicFogName) : null;
				if (fogSource != null)
				{
					var fog = Object.Instantiate(fogSource.gameObject, go.transform);
					fog.name = "Stench";
					fog.transform.SetLocalPositionAndRotation(new Vector3(0, 0.4f, 0), Quaternion.Euler(-90f, 0f, 0f));
					fog.transform.localScale = Vector3.one * 1.4f;
				}
			});
			foreach (var m in meshes)
				Object.DestroyImmediate(m); // baked into the prop mesh
			return prefab;
		}

		// One waste bag under t: base centre at pos, label on its local +X face.
		private static void Bag(GameObject t, List<Mesh> meshes, Vector3 pos, Quaternion rot, float w, float d, float h, int seed)
		{
			var bag = new GameObject("Bag");
			bag.transform.SetParent(t.transform, false);
			bag.transform.SetLocalPositionAndRotation(pos, rot);
			var mesh = BuildBagMesh(w, d, h, seed);
			meshes.Add(mesh);
			bag.AddComponent<MeshFilter>().sharedMesh = mesh;
			bag.AddComponent<MeshRenderer>().sharedMaterial = _hazBag;

			// Label: emblem + two black "CAUTION / BIOHAZARD" text bars below it, just off the bulging side.
			float x = w * (BagRadius(0.42f) + 0.06f);
			float s = Mathf.Min(w, d) * 0.62f;
			Emblem(bag, new Vector3(x, h * 0.5f, 0f), Vector3.right, s);
			foreach (var (y, len) in new[] { (0.3f, 1.25f), (0.23f, 0.95f) })
				Prim(bag, PrimitiveType.Quad, _hazBlack, new Vector3(x, h * y, 0f), new Vector3(s * len, s * 0.13f, 1f),
					Quaternion.LookRotation(Vector3.left, Vector3.up));
		}

		// Bag profile: (height fraction, radius fraction) from the bottom to the top of the tuft.
		private static readonly Vector2[] BagProfile =
		{
			new(0f, 0.72f), new(0.04f, 0.93f), new(0.14f, 1f), new(0.42f, 1.03f), new(0.6f, 0.97f), new(0.72f, 0.78f),
			new(0.81f, 0.48f), new(0.87f, 0.2f), new(0.9f, 0.1f), new(0.925f, 0.085f), new(0.95f, 0.13f),
			new(0.99f, 0.24f), new(1.03f, 0.27f), new(1.05f, 0.18f),
		};

		private static float BagRadius(float y)
		{
			for (int i = 1; i < BagProfile.Length; i++)
				if (y <= BagProfile[i].x)
				{
					var a = BagProfile[i - 1];
					var b = BagProfile[i];
					return Mathf.Lerp(a.y, b.y, Mathf.SmoothStep(0f, 1f, (y - a.x) / (b.x - a.x)));
				}
			return BagProfile[^1].y;
		}

		// Lathe along the profile with a squarish (superellipse) section that rounds off towards the neck, vertical
		// folds growing into the neck and a ragged tuft. Smooth normals, no UVs (plain glossy plastic).
		private static Mesh BuildBagMesh(float w, float d, float h, int seed)
		{
			const int seg = 32;
			const int rings = 40;
			float top = BagProfile[^1].x;
			var verts = new List<Vector3>();
			var tris = new List<int>();
			verts.Add(Vector3.zero); // bottom pole
			for (int r = 0; r < rings; r++)
			{
				float y = top * r / (rings - 1);
				float radius = BagRadius(y);
				// Section: square-ish body (n = 3.2), round neck and tuft (n = 2).
				float n = Mathf.Lerp(3.2f, 2f, Mathf.InverseLerp(0.6f, 0.82f, y));
				// Fold depth: almost smooth low on the body, deep creases into the neck, ragged lobes on the tuft.
				float fold = y < 0.93f ? Mathf.Lerp(0.012f, 0.16f, Mathf.InverseLerp(0.45f, 0.88f, y)) : 0.32f;
				// The full bag sags: the lower half bulges a bit more.
				float sag = 1f + 0.05f * Mathf.Exp(-Mathf.Pow((y - 0.22f) / 0.18f, 2f));
				for (int i = 0; i < seg; i++)
				{
					float a = i * Mathf.PI * 2f / seg;
					float c = Mathf.Cos(a), s = Mathf.Sin(a);
					float px = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / n);
					float pz = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / n);
					float k = 1f + fold * (0.6f * Mathf.Sin(7f * a + seed * 1.7f + y * 4f) +
					                       0.4f * Mathf.Sin(12f * a + seed * 2.9f - y * 7f));
					if (y > 0.93f)
						k *= 1f + 0.25f * Mathf.Sin(5f * a + seed); // a few big petals of plastic above the knot
					// The twisted neck leans a little off the centre.
					float lean = Mathf.InverseLerp(0.7f, 1f, y) * 0.08f;
					verts.Add(new Vector3((px * radius * k * sag + lean) * w, y * h, (pz * radius * k * sag) * d));
				}
			}
			verts.Add(new Vector3(0.08f * w, top * h, 0f)); // top pole

			int last = verts.Count - 1;
			for (int i = 0; i < seg; i++)
			{
				int j = (i + 1) % seg;
				tris.Add(0); tris.Add(1 + i); tris.Add(1 + j);
				for (int r = 0; r < rings - 1; r++)
				{
					int a0 = 1 + r * seg + i, a1 = 1 + r * seg + j;
					int b0 = a0 + seg, b1 = a1 + seg;
					tris.Add(a0); tris.Add(b0); tris.Add(b1);
					tris.Add(a0); tris.Add(b1); tris.Add(a1);
				}
				int t0 = 1 + (rings - 1) * seg;
				tris.Add(last); tris.Add(t0 + j); tris.Add(t0 + i);
			}
			var mesh = new Mesh { name = "WasteBag" };
			mesh.SetVertices(verts);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		// Yellow decontamination tent (4.2 x 3.2 m, ridge along local X, open front +Z) with a shower arch, a table
		// and biohazard emblems on the roof. Carves the navmesh; fabric - bullets pass.
		private static GameObject BuildHazmatTent()
		{
			var t = SB.Temp("Prop_HazmatTent");
			const float L = 4.2f, D = 3.2f, eave = 1.6f, ridge = 2.3f, th = 0.05f;
			// Back wall, gable ends (stepped triangle), front: two side panels around the open door.
			SB.Box(t, _hazYellow, new Vector3(0, eave * 0.5f, -D * 0.5f), new Vector3(L, eave, th));
			foreach (float s in new[] { -1f, 1f })
			{
				float x = s * L * 0.5f;
				SB.Box(t, _hazYellow, new Vector3(x, eave * 0.5f, 0), new Vector3(th, eave, D));
				SB.Box(t, _hazYellow, new Vector3(x, eave + 0.17f, 0), new Vector3(th, 0.34f, D * 0.66f));
				SB.Box(t, _hazYellow, new Vector3(x, eave + 0.5f, 0), new Vector3(th, 0.32f, D * 0.3f));
				SB.Box(t, _hazYellow, new Vector3(s * (L * 0.5f - 0.75f), eave * 0.5f, D * 0.5f), new Vector3(1.5f, eave, th));
				// Rolled-up door flaps.
				SB.Box(t, _hazOrange, new Vector3(s * 0.62f, eave - 0.12f, D * 0.5f + 0.06f), new Vector3(0.24f, 0.18f, 0.12f));
			}
			// Roof panels sloping from the ridge to the eaves.
			float run = D * 0.5f, rise = ridge - eave, len = Mathf.Sqrt(run * run + rise * rise);
			float pitch = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
			foreach (float s in new[] { -1f, 1f })
			{
				var rot = Quaternion.Euler(s * pitch, 0f, 0f);
				var centre = new Vector3(0, (eave + ridge) * 0.5f + 0.03f, s * run * 0.5f);
				SB.Box(t, _hazYellow, centre, new Vector3(L + 0.25f, th, len + 0.12f), rot);
				var normal = rot * Vector3.up;
				Emblem(t, centre + normal * 0.035f, normal, 1.1f, rot * Vector3.forward);
			}
			SB.Box(t, _hazBlack, new Vector3(0, ridge + 0.04f, 0), new Vector3(L + 0.3f, 0.06f, 0.08f));
			// Dark floor mat inside, a folding table with a sample box.
			SB.Box(t, _hazBlack, new Vector3(0, 0.01f, 0), new Vector3(L - 0.1f, 0.02f, D - 0.1f));
			SB.Box(t, SB._trim, new Vector3(-0.9f, 0.74f, -0.9f), new Vector3(1.6f, 0.05f, 0.7f));
			foreach (float x in new[] { -1.6f, -0.2f })
				SB.Box(t, _metalGalv, new Vector3(x, 0.37f, -0.9f), new Vector3(0.05f, 0.74f, 0.6f));
			SB.Box(t, _hazOrange, new Vector3(-1.2f, 0.9f, -0.9f), new Vector3(0.45f, 0.28f, 0.35f));
			// Decontamination shower arch in front of the door.
			foreach (float s in new[] { -1f, 1f })
				SB.Box(t, _metalGalv, new Vector3(s * 0.7f, 1.1f, D * 0.5f + 1.3f), new Vector3(0.07f, 2.2f, 0.07f));
			SB.Box(t, _metalGalv, new Vector3(0, 2.2f, D * 0.5f + 1.3f), new Vector3(1.47f, 0.07f, 0.07f));
			SB.Box(t, _hazOrange, new Vector3(0, 0.03f, D * 0.5f + 1.3f), new Vector3(1.4f, 0.06f, 1.2f)); // drip tray
			return BakeProp(t, "Prop_HazmatTent", castShadows: true, go =>
			{
				// Carve the tent body only (the shower arch is walk-through).
				Carve(go, new Vector3(0, 1.15f, 0), new Vector3(L + 0.2f, 2.3f, D + 0.2f));
			});
		}

		/// <summary>
		/// Vehicles/Truck_Hazmat.prefab: a variant of Truck_Mail (same step van, solid, stops bullets) repainted hazmat
		/// yellow with black/orange bands and biohazard emblems on both sides and the roof.
		/// </summary>
		private static void BuildHazmatTruck()
		{
			var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{VehicleFolder}/Truck_Mail.prefab");
			if (source == null)
			{
				Debug.LogWarning($"{Log} Truck_Mail not found (run Build Street Vehicles first) - no hazmat truck.");
				return;
			}
			var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
			try
			{
				go.name = HazmatTruckName;
				foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
				{
					var mats = r.sharedMaterials;
					for (int i = 0; i < mats.Length; i++)
					{
						if (mats[i] == null) continue;
						switch (mats[i].name)
						{
							case "Veh_Truck_White": mats[i] = _hazYellow; break;
							case "Veh_Mail_Blue": mats[i] = _hazBlack; break;
							case "Veh_Mail_Red": mats[i] = _hazOrange; break;
						}
					}
					r.sharedMaterials = mats;
				}
				// Van frame (VehicleBuilder.BuildVan): half width 1.1, box z -2.6..1.95, roof cap top ~2.83.
				var emblems = new GameObject("Emblems");
				emblems.transform.SetParent(go.transform, false);
				foreach (float s in new[] { -1f, 1f })
					Emblem(emblems, new Vector3(s * 1.125f, 1.85f, -0.9f), new Vector3(s, 0, 0), 1.15f);
				Emblem(emblems, new Vector3(0, 2.845f, -0.4f), Vector3.up, 1.8f);
				foreach (var r in emblems.GetComponentsInChildren<MeshRenderer>())
					r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				PrefabUtility.SaveAsPrefabAsset(go, $"{VehicleFolder}/{HazmatTruckName}.prefab", out bool ok);
				if (!ok)
					Debug.LogError($"{Log} Failed to save {HazmatTruckName}.");
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		private static GameObject BuildForSaleSign()
		{
			var t = SB.Temp("Prop_ForSaleSign");
			SB.Box(t, SB._trim, new Vector3(0, 0.9f, 0), new Vector3(0.08f, 1.8f, 0.08f));
			SB.Box(t, SB._trim, new Vector3(0.45f, 1.7f, 0), new Vector3(0.9f, 0.06f, 0.06f));
			SB.Box(t, _signRed, new Vector3(0.55f, 1.35f, 0), new Vector3(0.7f, 0.45f, 0.03f));
			SB.Box(t, SB._trim, new Vector3(0.55f, 1.35f, 0.017f), new Vector3(0.6f, 0.12f, 0.005f));
			SB.Box(t, SB._trim, new Vector3(0.55f, 1.35f, -0.017f), new Vector3(0.6f, 0.12f, 0.005f));
			return BakeProp(t, "Prop_ForSaleSign", castShadows: true);
		}

		// ================================================================== materials

		private static void CreatePropMaterials()
		{
			CreateHazmatMaterials();
			_fenceWood = PMat("Suburb_FenceWood", new Color(0.58f, 0.42f, 0.29f), 0f, 0.15f);
			_fenceWoodDark = PMat("Suburb_FenceWoodDark", new Color(0.43f, 0.31f, 0.21f), 0f, 0.12f);
			_metalDark = PMat("Suburb_MetalDark", new Color(0.17f, 0.17f, 0.19f), 0.6f, 0.45f);
			_metalGalv = PMat("Suburb_MetalGalv", new Color(0.62f, 0.64f, 0.66f), 0.7f, 0.5f);
			_poolWater = PMat("Suburb_PoolWater", new Color(0.24f, 0.66f, 0.86f), 0f, 0.95f);
			_poolWaterDeep = PMat("Suburb_PoolWaterDeep", new Color(0.13f, 0.47f, 0.72f), 0f, 0.95f);
			_poolTile = PMat("Suburb_PoolCoping", new Color(0.9f, 0.9f, 0.86f), 0f, 0.3f);
			_shedRed = PMat("Suburb_ShedRed", new Color(0.55f, 0.16f, 0.13f), 0f, 0.2f);
			_bark = PMat("Suburb_Bark", new Color(0.32f, 0.22f, 0.15f), 0f, 0.05f);
			_leaf = PMat("Suburb_Leaf", new Color(0.2f, 0.42f, 0.16f), 0f, 0.1f);
			_leafAlt = PMat("Suburb_LeafAlt", new Color(0.28f, 0.5f, 0.2f), 0f, 0.1f);
			_trampMat = PMat("Suburb_RubberBlack", new Color(0.06f, 0.06f, 0.07f), 0f, 0.3f);
			_trampPad = PMat("Suburb_TrampolinePad", new Color(0.15f, 0.35f, 0.78f), 0f, 0.3f);
			_swingFrame = PMat("Suburb_SwingFrame", new Color(0.16f, 0.45f, 0.24f), 0.4f, 0.45f);
			_orange = PMat("Suburb_BarricadeOrange", new Color(0.95f, 0.45f, 0.08f), 0f, 0.35f);
			_lampGlow = PMat("Suburb_LampGlow", new Color(1f, 0.93f, 0.75f), 0f, 0.5f, new Color(1.6f, 1.4f, 1.0f));
			_amberGlow = PMat("Suburb_AmberGlow", new Color(1f, 0.6f, 0.1f), 0f, 0.5f, new Color(1.5f, 0.75f, 0.1f));
			_mailBlack = PMat("Suburb_MailboxBlack", new Color(0.09f, 0.09f, 0.1f), 0.3f, 0.4f);
			_flagRed = PMat("Suburb_FlagRed", new Color(0.8f, 0.1f, 0.1f), 0f, 0.4f);
			_binGreen = PMat("Suburb_BinGreen", new Color(0.15f, 0.33f, 0.18f), 0f, 0.3f);
			_binGrey = PMat("Suburb_BinGrey", new Color(0.35f, 0.36f, 0.38f), 0f, 0.3f);
			_gravel = PMat("Suburb_Gravel", new Color(0.56f, 0.54f, 0.5f), 0f, 0.05f);
			_signRed = PMat("Suburb_SignRed", new Color(0.75f, 0.12f, 0.12f), 0f, 0.3f);
		}

		// Lit See-Through, colour/metallic/smoothness (+ emission) enforced on every run.
		private static void CreateHazmatMaterials()
		{
			_hazYellow = PMat("Suburb_HazmatYellow", new Color(0.98f, 0.78f, 0.08f), 0f, 0.35f);
			_hazBag = PMat("Suburb_HazmatBag", new Color(1f, 0.85f, 0.1f), 0f, 0.75f);
			_hazBlack = PMat("Suburb_HazmatBlack", new Color(0.06f, 0.06f, 0.06f), 0f, 0.3f);
			_hazOrange = PMat("Suburb_HazmatOrange", new Color(0.95f, 0.38f, 0.05f), 0f, 0.4f);
			_biohazard = BiohazardMat();
		}

		// Emblem decal material: the generated biohazard sign (yellow disc, black rim + symbol), alpha-clipped.
		private static Material BiohazardMat()
		{
			if (AssetImporter.GetAtPath(BiohazardTexturePath) is TextureImporter importer &&
			    (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp))
			{
				importer.alphaIsTransparency = true;
				importer.wrapMode = TextureWrapMode.Clamp;
				importer.maxTextureSize = 512;
				importer.SaveAndReimport();
			}
			var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BiohazardTexturePath);
			if (texture == null)
				Debug.LogWarning($"{Log} {BiohazardTexturePath} not found - plain emblem quads.");
			var m = PMat("Suburb_Biohazard", Color.white, 0f, 0.3f);
			m.SetTexture("_BaseMap", texture);
			m.SetTexture("_MainTex", texture);
			m.SetFloat("_AlphaClip", 1f);
			m.SetFloat("_Cutoff", 0.5f);
			m.EnableKeyword("_ALPHATEST_ON");
			m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
			EditorUtility.SetDirty(m);
			return m;
		}

		private static Material PMat(string name, Color color, float metallic, float smoothness, Color? emission = null)
		{
			string path = $"{MatFolder}/{name}.mat";
			var m = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (m == null)
			{
				m = new Material(SeeThroughShader.FindLit()) { name = name };
				AssetDatabase.CreateAsset(m, path);
			}
			m.SetColor("_BaseColor", color);
			m.SetColor("_Color", color);
			m.SetFloat("_Metallic", metallic);
			m.SetFloat("_Smoothness", smoothness);
			if (emission.HasValue)
			{
				m.EnableKeyword("_EMISSION");
				m.SetColor("_EmissionColor", emission.Value);
				m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
			}
			m.enableInstancing = true;
			EditorUtility.SetDirty(m);
			return m;
		}
	}
}
