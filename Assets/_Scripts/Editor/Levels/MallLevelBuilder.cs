using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using static Crowds.EditorTools.KeyLevelBuilder;
using static Crowds.EditorTools.VenueKit;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Campaign level "Склад-магазин" (Level_07_Mall) — Tools ▸ Crowds ▸ Build Mall Level. The sales floor of a
	/// members-only warehouse store: 8 blocks of tall pallet racking (4 rows × 2, a wide main aisle and a cross aisle),
	/// a broken frozen-food department in place of the two +Z rows (screen right: glass-door cases on the side wall and
	/// two rows of chest-freezer islands, iced over, inside an irregular slippery ice patch), steel columns, checkout lanes at the
	/// storefront (+X, near the camera, a low wall), freezer cases along the back wall, pallets of goods, a forklift,
	/// flatbed carts. Zombies come in through 3 loading-dock doors (back wall),
	/// 2 store entrances (front) and 2 emergency exits (sides).
	/// Difficulty: the next step after the Baseball level — 3 quota waves (40 / 57 / 73 mobs) with Fatmen, helmeted
	/// toxics and a few gunners in the last one. Racks, columns, counters, freezers and pallets stop bullets.
	/// Safe to re-run. See Docs/ArenaLevelsFeature.md.
	/// </summary>
	public static class MallLevelBuilder
	{
		private const string Name = "Level_07_Mall";
		private const string AssetName = "Level_Mall";
		internal const string DisplayName = "Склад-магазин";

		private const float X0 = -36f, X1 = 36f, Z0 = -28f, Z1 = 28f, WallH = 3.2f, FrontH = 1.2f;
		private const float RackDepth = 2.6f, RackH = 3f, Bay = 2.7f;
		private static readonly float[] RowZ = { -23f, -14.5f, -6f, 6f };   // the +14.5 and +23 rows are the frozen-food department

		// Frozen-food department (screen right): wall cases along the +Z side wall, chest-freezer islands, ice patch.
		private const float IslandW = 2f, IslandH = 0.95f, WallCaseDepth = 1.6f, WallCaseH = 2.2f;
		private static readonly (float x0, float x1)[] WallCaseSpans = { (-24f, -12.6f), (-7.4f, 14f) };   // gap = emergency exit (x −10)
		// Chest-freezer islands: (z, x0, x1). Back row near the wall cases, front row on the old +14.5 rack line.
		private static readonly (float z, float x0, float x1)[] Islands =
		{
			(21.5f, -22.5f, -13.5f), (21.5f, -3f, 12f),
			(14.5f, -24f, -8f), (14.5f, 0f, 9f),
		};
		private const float IceGrip = 3.5f;   // TractionModifier on the ice patch (the hockey rink is 3)

		// Breakable pallets of goods in the aisles (two looks, one config) + their wood-splinter burst.
		private const string CrateConfigSource = "Assets/_Data/Breakables/Crate.asset";
		private const string PalletConfigPath = "Assets/_Data/Breakables/PalletBoxes.asset";
		private const string PalletBoxesPrefabPath = "Assets/_Prefabs/Breakables/PalletBoxes.prefab";
		private const string PalletWrappedPrefabPath = "Assets/_Prefabs/Breakables/PalletWrapped.prefab";
		private const string PalletId = "PalletBoxes";
		private const string BoxBreakId = "box_break";
		private const string BoxBreakPrefabPath = "Assets/_Prefabs/Effects/BoxBreak.prefab";
		private const string GibletsSourcePath = "Assets/Epic Toon FX/Prefabs/Combat/Giblets/GibletExplodeWood.prefab";
		private const float BurstHeight = 0.6f, BurstScale = 0.7f;
		private static readonly (float x0, float x1)[] RackSpans = { (-24f, -7f), (-3f, 14f) };

		private static Material _floor, _wallMat, _wallTrim, _upright, _beam, _deck, _pallet, _kraft, _kraftDark, _whiteBox, _wrapBlue;
		private static Material _redBox, _column, _guard, _paintYellow, _paintRed, _counter, _belt, _register, _freezer, _freezerGlow;
		private static Material _forklift, _black, _cartMetal, _sign, _glass;
		private static Material _freezerDead, _freezerSign, _frost, _iceFrost, _iceGlaze;

		[MenuItem("Tools/Crowds/Build Mall Level")]
		public static void Build()
		{
			Init();
			CreateMallMaterials();
			var cleared = MobsCleared();
			var stages = new List<DifficultyStage>
			{
				Quota(DifficultyLevel.level1, 5f, 0.35f, cleared, "Открытие магазина: «покупатели» уже внутри",
					("Slow", 25), ("Fast", 15)),
				Breather(DifficultyLevel.level2, 12f, "Передышка: в главном проходе припасы"),
				Quota(DifficultyLevel.level3, 3f, 0.3f, cleared, "Волна 2/3: распродажа",
					("Fast", 35), ("Slow", 10), ("Fatman", 6), ("ToxicHelmet", 6)),
				Breather(DifficultyLevel.level4, 12f, "Передышка: припасы у касс и в главном проходе"),
				Quota(DifficultyLevel.level5, 3f, 0.25f, cleared, "Волна 3/3: «Чёрная пятница»",
					("Fast", 40), ("Slow", 20), ("Fatman", 8), ("FatmanStrong", 2), ("GunnerBare", 3)),
				Finish(),
			};
			var config = Config($"{ConfigFolder}/{AssetName}.asset", stages, new Vector2(6f, 60f),
				new[] { "Pistol", "SMG", "AR", "Shotgun" }, new[] { "speedup" });

			BuildBoxBreakEffect();
			var pallets = BuildPalletBreakables();
			var prefab = BuildLevel(config, pallets);
			Definition(prefab, AssetName, DisplayName);
			AssetDatabase.SaveAssets();
			CampaignSetup.Apply();
			Debug.Log($"[MallLevelBuilder] Level built: {LevelFolder}/{Name}.prefab");
		}

		private static GameObject BuildLevel(LevelConfig config, GameObject[] pallets)
		{
			var l = Begin(Name, new Rect(X0, Z0, X1 - X0, Z1 - Z0), _floor);

			// --- Walls with door gaps: back (3 loading docks), sides (emergency exits), low storefront (2 entrances) ---
			float[] docks = { -18f, 0f, 18f };
			float[] entrances = { -12f, 12f };
			const float exitX = -10f;
			WallWithGaps(l.Walls, "Back", new Vector2(X0, Z0), new Vector2(X0, Z1), WallH, _wallMat, _wallTrim, 3.8f,
				docks[0] - Z0, docks[1] - Z0, docks[2] - Z0);
			foreach (float s in new[] { -1f, 1f })
				WallWithGaps(l.Walls, s < 0 ? "SideS" : "SideN", new Vector2(X0, s * Z1), new Vector2(X1, s * Z1), WallH,
					_wallMat, _wallTrim, 2.6f, exitX - X0);
			WallWithGaps(l.Walls, "Storefront", new Vector2(X1, Z0), new Vector2(X1, Z1), FrontH, _wallTrim, _glass, 3.4f,
				entrances[0] - Z0, entrances[1] - Z0);

			var mobs = new[]
			{
				Mob("Slow", 3, (DifficultyLevel.level1, 3f), (DifficultyLevel.level3, 3f), (DifficultyLevel.level5, 2.5f)),
				Mob("Fast", 2, (DifficultyLevel.level1, 3.5f), (DifficultyLevel.level3, 2f), (DifficultyLevel.level5, 1.8f)),
				Mob("Fatman", 1, (DifficultyLevel.level3, 6f), (DifficultyLevel.level5, 5f)),
				Mob("ToxicHelmet", 1, (DifficultyLevel.level3, 6f)),
				Mob("FatmanStrong", 1, (DifficultyLevel.level5, 10f)),
				Mob("GunnerBare", 1, (DifficultyLevel.level5, 8f)),
			};
			foreach (float z in docks)
				Spawn(l.Spawns, "Dock", MobDoor(l.Props, $"Dock_{z:0}", V(X0, z), Vector3.right, 3.8f, 4.5f, 3f, _guard), mobs);
			foreach (float s in new[] { -1f, 1f })
				Spawn(l.Spawns, "Exit", MobDoor(l.Props, $"EmergencyExit_{(s < 0 ? "S" : "N")}", V(exitX, s * Z1), new Vector3(0, 0, -s),
					2.6f, 3.5f, 2.4f, _paintRed), mobs);
			foreach (float z in entrances)
				Spawn(l.Spawns, "Entrance", MobDoor(l.Props, $"Entrance_{z:0}", V(X1, z), Vector3.left, 3.4f, 3.5f, 2.6f, _wallTrim), mobs);

			// --- Racks (one baked mesh) + their solid boxes ---
			var racks = new GameObject("Racks_Temp");
			var rackSolids = Child(l.Root.transform, "RackSolids");
			int seed = 0;
			foreach (float z in RowZ)
				foreach (var (x0, x1) in RackSpans)
				{
					Rack(racks.transform, x0, x1, z, ++seed);
					var solid = Child(rackSolids, $"Rack_{seed}");
					solid.position = V((x0 + x1) * 0.5f, z);
					Solid(solid.gameObject, new Vector3(0, RackH * 0.5f, 0), new Vector3(x1 - x0 + 0.2f, RackH, RackDepth + 0.1f), blocksBullets: true);
				}
			Combine(racks, l.Props, $"{Name}_Racks", castShadows: true);

			// --- Frozen-food department in place of the +Z rack row ---
			FrozenFoods(l.Props);

			// --- Columns: main aisle, cross aisle, back and front open areas ---
			foreach (var (x, z) in new[]
			         {
				         (-5f, 0f), (-5f, 10.25f), (-5f, -10.25f), (-5f, 18.75f), (-5f, -18.75f),
				         (18f, 0f), (18f, 10.25f), (18f, -10.25f), (18f, 18.75f), (18f, -18.75f),
				         (-29f, 10.25f), (-29f, -10.25f), (-29f, 22f), (-29f, -22f),
			         })
				Column(l.Props, x, z);

			// --- Checkouts, freezers, goods, vehicles ---
			foreach (float z in new[] { -22f, -14f, -6f, 6f, 14f, 22f })
				Checkout(l.Props, z);
			foreach (var (z0, z1) in new[] { (Z0 + 0.6f, docks[0] - 2.5f), (docks[0] + 2.5f, docks[1] - 2.5f), (docks[1] + 2.5f, docks[2] - 2.5f), (docks[2] + 2.5f, Z1 - 0.6f) })
				Freezers(l.Props, z0, z1);
			foreach (var (x, z, yaw, i) in new[] { (9f, -2.2f, 10f, 0), (-14f, 2.4f, -5f, 1), (22.5f, 7f, 30f, 2), (-29.5f, 6f, 0f, 3), (-29.5f, -6f, 0f, 4), (22.5f, -9f, 70f, 5) })
				PalletPair(l.Props, new Vector2(x, z), yaw, i, pallets);
			Forklift(l.Props, new Vector2(-30f, -14f), 90f);
			foreach (var (x, z, yaw) in new[] { (2f, 2.8f, 0f), (-19f, -2.6f, 180f), (12f, 10.25f, 90f), (-12f, -18.75f, 90f) })
				FlatCart(l.Props, new Vector2(x, z), yaw);

			PaintFloor(l.Root.transform, docks);

			// --- Mission: zone, loot ---
			Zone(l.Mission, "store", V(0f, 0f), new Vector3(X1 - X0 - 1f, 2f, Z1 - Z0 - 1f));
			StageLootPoint(l.Loot, "Start", V(24f, -3f), DifficultyLevel.level1, 1f, Ammo(Caliber.Mm9, 30));
			StageLootPoint(l.Loot, "Break1", V(4f, 0f), DifficultyLevel.level2, 1.6f,
				Weapon("AR"), Ammo(Caliber.Cal556, 60), Ammo(Caliber.Mm9, 30), Health(30));
			StageLootPoint(l.Loot, "Break2_Checkout", V(22.5f, 0f), DifficultyLevel.level4, 1.4f,
				Health(40), Grenade(), Ammo(Caliber.None, 40));
			StageLootPoint(l.Loot, "Break2_Aisle", V(-16f, 0f), DifficultyLevel.level4, 1.2f,
				Weapon("Shotgun"), Ammo(Caliber.Gauge12, 24), Bonus("rage"));
			foreach (var p in new[] { V(10f, 3f), V(-12f, -3f), V(6f, -10.25f), V(-15f, 10.25f), V(25f, 10f), V(-31f, 0f) })
				FallbackPoint(l, p);

			return VenueKit.Finish(l, config, V(23f, 0f), -90f, $"{LevelFolder}/{Name}.prefab");
		}

		// ================================================================== props

		// Double-sided pallet racking from x0 to x1 centred on z: orange uprights, blue beams, wire decks, pallets of
		// boxes on three levels (floor, 1.1 m, 2.2 m), varied per bay by a deterministic hash.
		private static void Rack(Transform parent, float x0, float x1, float zc, int seed)
		{
			var id = Quaternion.identity;
			int bays = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / Bay));
			float bay = (x1 - x0) / bays;
			float[] levels = { 0f, 1.1f, 2.2f };
			for (int i = 0; i <= bays; i++)
			{
				float x = x0 + i * bay;
				foreach (float s in new[] { -1f, 1f })
					Primitive(PrimitiveType.Cube, "Upright", parent, _upright, new Vector3(x, FloorY + RackH * 0.5f, zc + s * (RackDepth * 0.5f - 0.05f)), id,
						new Vector3(0.1f, RackH, 0.1f));
			}
			foreach (float y in new[] { 1.05f, 2.15f, RackH - 0.05f })
				foreach (float s in new[] { -1f, 1f })
					Primitive(PrimitiveType.Cube, "Beam", parent, _beam, new Vector3((x0 + x1) * 0.5f, FloorY + y, zc + s * (RackDepth * 0.5f - 0.05f)), id,
						new Vector3(x1 - x0, 0.12f, 0.08f));
			foreach (float y in new[] { 1.1f, 2.2f })
				Primitive(PrimitiveType.Cube, "Deck", parent, _deck, new Vector3((x0 + x1) * 0.5f, FloorY + y, zc), id, new Vector3(x1 - x0, 0.03f, RackDepth - 0.1f));

			Material[] boxMats = { _kraft, _kraftDark, _whiteBox, _wrapBlue, _redBox, _kraft };
			for (int i = 0; i < bays; i++)
				for (int lv = 0; lv < levels.Length; lv++)
					foreach (float s in new[] { -1f, 1f })
					{
						int h = Hash(seed, i, lv, s > 0 ? 1 : 0);
						if (h % 7 == 0)
							continue; // an empty slot here and there
						float cx = x0 + (i + 0.5f) * bay, cz = zc + s * RackDepth * 0.25f;
						float baseY = FloorY + levels[lv] + (lv == 0 ? 0f : 0.03f);
						Primitive(PrimitiveType.Cube, "Pallet", parent, _pallet, new Vector3(cx, baseY + 0.07f, cz), id, new Vector3(bay - 0.4f, 0.14f, 1.1f));
						float height = 0.55f + (h % 5) * 0.08f;
						var m = boxMats[h % boxMats.Length];
						if (h % 3 == 0)
						{
							// Two stacks of smaller cartons.
							foreach (float dx in new[] { -0.32f, 0.32f })
								Primitive(PrimitiveType.Cube, "Box", parent, boxMats[(h / 3) % boxMats.Length],
									new Vector3(cx + dx * (bay - 0.5f), baseY + 0.14f + height * 0.5f, cz), id,
									new Vector3((bay - 0.6f) * 0.46f, height, 1f));
						}
						else
						{
							Primitive(PrimitiveType.Cube, "Load", parent, m, new Vector3(cx, baseY + 0.14f + height * 0.5f, cz), id,
								new Vector3(bay - 0.5f, height, 1.05f));
						}
					}
			// Aisle-end price sign on a post.
			foreach (float x in new[] { x0 - 0.15f, x1 + 0.15f })
				Primitive(PrimitiveType.Cube, "EndSign", parent, _sign, new Vector3(x, FloorY + 2.6f, zc), id, new Vector3(0.05f, 0.5f, 1.4f));
		}

		private static int Hash(int a, int b, int c, int d)
		{
			unchecked
			{
				int h = a * 73856093 ^ b * 19349663 ^ c * 83492791 ^ d * 2654435;
				return h & 0x7fffffff;
			}
		}

		private static void Column(Transform parent, float x, float z)
		{
			var root = Child(parent, "Column");
			root.position = V(x, z);
			var id = Quaternion.identity;
			Primitive(PrimitiveType.Cube, "Steel", root, _column, new Vector3(0, 4f, 0), id, new Vector3(0.5f, 8f, 0.5f));
			Primitive(PrimitiveType.Cube, "Guard", root, _guard, new Vector3(0, 0.6f, 0), id, new Vector3(0.8f, 1.2f, 0.8f));
			Primitive(PrimitiveType.Cube, "GuardStripe", root, _black, new Vector3(0, 0.85f, 0), id, new Vector3(0.82f, 0.15f, 0.82f));
			Solid(root.gameObject, new Vector3(0, 1.5f, 0), new Vector3(0.9f, 3f, 0.9f), blocksBullets: true);
		}

		// Checkout lane counter along X at z: conveyor, register, candy rack, lane light on a pole.
		private static void Checkout(Transform parent, float z)
		{
			var root = Child(parent, "Checkout");
			root.position = V(26.5f, z);
			var id = Quaternion.identity;
			Primitive(PrimitiveType.Cube, "Counter", root, _counter, new Vector3(0, 0.45f, 0), id, new Vector3(4.4f, 0.9f, 0.9f));
			Primitive(PrimitiveType.Cube, "Belt", root, _belt, new Vector3(-0.4f, 0.91f, 0), id, new Vector3(3.2f, 0.03f, 0.6f));
			Primitive(PrimitiveType.Cube, "Register", root, _register, new Vector3(1.7f, 1.1f, 0), id, new Vector3(0.6f, 0.4f, 0.5f));
			Primitive(PrimitiveType.Cube, "Screen", root, _black, new Vector3(1.7f, 1.45f, 0.1f), Quaternion.Euler(-20f, 0, 0), new Vector3(0.45f, 0.3f, 0.04f));
			Primitive(PrimitiveType.Cube, "CandyRack", root, _redBox, new Vector3(-1.2f, 0.7f, -0.75f), id, new Vector3(1.4f, 1.4f, 0.5f));
			Primitive(PrimitiveType.Cube, "LanePole", root, _column, new Vector3(2.1f, 1.2f, 0.35f), id, new Vector3(0.08f, 2.4f, 0.08f));
			Primitive(PrimitiveType.Cube, "LaneLight", root, _freezerGlow, new Vector3(2.1f, 2.45f, 0.35f), id, new Vector3(0.35f, 0.25f, 0.35f));
			Solid(root.gameObject, new Vector3(-0.1f, 0.6f, -0.15f), new Vector3(4.6f, 1.2f, 1.4f), blocksBullets: true);
		}

		// Row of glass-door freezer cases along the back wall between z0 and z1.
		private static void Freezers(Transform parent, float z0, float z1)
		{
			var root = Child(parent, "Freezers");
			float zc = (z0 + z1) * 0.5f, len = z1 - z0;
			root.position = V(X0 + 1.2f, zc);
			var id = Quaternion.identity;
			Primitive(PrimitiveType.Cube, "Case", root, _freezer, new Vector3(0, 1.1f, 0), id, new Vector3(1.6f, 2.2f, len));
			int doors = Mathf.Max(1, Mathf.RoundToInt(len / 1.2f));
			for (int i = 0; i < doors; i++)
			{
				float z = -len * 0.5f + (i + 0.5f) * len / doors;
				Primitive(PrimitiveType.Cube, "GlassDoor", root, _freezerGlow, new Vector3(0.81f, 1.1f, z), id, new Vector3(0.02f, 1.8f, len / doors - 0.12f));
			}
			Primitive(PrimitiveType.Cube, "Header", root, _sign, new Vector3(0.82f, 2.1f, 0), id, new Vector3(0.03f, 0.18f, len));
			Solid(root.gameObject, new Vector3(0, 1.1f, 0), new Vector3(1.8f, 2.2f, len + 0.1f), blocksBullets: true);
		}

		// ================================================================== frozen-food department

		// Broken frozen-food department: glass-door cases along the +Z side wall, two chest-freezer islands, all iced over
		// (frost crusts, icicles, doors ajar, dead lights), standing in an irregular ice patch that makes everybody slide.
		private static void FrozenFoods(Transform props)
		{
			var dept = Child(props, "FrozenFoods");
			// Visuals go to a temp root and are baked into one mesh (one renderer, a submesh per material);
			// the solid boxes stay as separate objects under the department.
			var visuals = new GameObject("FrozenFoods_Temp");
			int seed = 100;
			foreach (var (x0, x1) in WallCaseSpans)
				WallCases(visuals.transform, dept, x0, x1, ++seed);
			foreach (var (z, x0, x1) in Islands)
				ChestIsland(visuals.transform, dept, z, x0, x1, ++seed);
			Combine(visuals, dept, $"{Name}_FrozenFoods", castShadows: true);
			IcePatch(dept);
		}

		// Visual root + solid root at the same spot.
		private static (Transform visual, Transform solid) Pair(Transform visuals, Transform solids, string name, Vector3 position)
		{
			var visual = Child(visuals, name);
			visual.position = position;
			var solid = Child(solids, name);
			solid.position = position;
			return (visual, solid);
		}

		// Upright glass-door freezer cases against the +Z side wall, facing −Z (into the store).
		private static void WallCases(Transform visuals, Transform solids, float x0, float x1, int seed)
		{
			float len = x1 - x0, front = -WallCaseDepth * 0.5f;
			var (root, solid) = Pair(visuals, solids, "WallCases", V((x0 + x1) * 0.5f, Z1 - 0.1f - WallCaseDepth * 0.5f));
			var id = Quaternion.identity;
			Primitive(PrimitiveType.Cube, "Case", root, _freezer, new Vector3(0, WallCaseH * 0.5f, 0), id, new Vector3(len, WallCaseH, WallCaseDepth));
			Primitive(PrimitiveType.Cube, "Header", root, _freezerSign, new Vector3(0, WallCaseH - 0.1f, front - 0.02f), id, new Vector3(len, 0.2f, 0.04f));
			Primitive(PrimitiveType.Cube, "Kick", root, _black, new Vector3(0, 0.08f, front - 0.02f), id, new Vector3(len, 0.16f, 0.04f));
			Primitive(PrimitiveType.Cube, "FrostCap", root, _frost, new Vector3(0, WallCaseH + 0.03f, 0), id, new Vector3(len - 0.1f, 0.06f, WallCaseDepth - 0.1f));

			int doors = Mathf.Max(1, Mathf.RoundToInt(len / 1.2f));
			float w = len / doors - 0.12f;
			for (int i = 0; i < doors; i++)
			{
				int h = Hash(seed, i, 7, 3);
				float xc = -len * 0.5f + (i + 0.5f) * len / doors;
				if (h % 5 == 1)
				{
					// Door hanging open, swung outward on its left hinge; a frost mound spills out of the case.
					float a = 35f + h % 30;
					var rot = Quaternion.Euler(0, a, 0);
					var hinge = new Vector3(xc - w * 0.5f, 1.1f, front - 0.02f);
					Primitive(PrimitiveType.Cube, "GlassDoorOpen", root, _freezerDead, hinge + rot * Vector3.right * (w * 0.5f), rot, new Vector3(w, 1.8f, 0.03f));
					Primitive(PrimitiveType.Cube, "FrostMound", root, _frost, new Vector3(xc, 0.35f, front - 0.25f), Quaternion.Euler(8f, h % 40 - 20f, 4f),
						new Vector3(w * 0.9f, 0.7f, 0.7f));
				}
				else
				{
					// Most lights are dead, a few still glow.
					Primitive(PrimitiveType.Cube, "GlassDoor", root, h % 4 == 0 ? _freezerGlow : _freezerDead, new Vector3(xc, 1.1f, front - 0.01f), id,
						new Vector3(w, 1.8f, 0.02f));
					if (h % 3 == 0)
						// Frost crust creeping up the glass from the bottom.
						Primitive(PrimitiveType.Cube, "FrostCrust", root, _frost, new Vector3(xc, 0.2f + (h % 4) * 0.08f, front - 0.04f), id,
							new Vector3(w * 0.95f, 0.4f + (h % 4) * 0.16f, 0.05f));
				}
				// Icicles under the header.
				for (int k = 0; k < 3; k++)
				{
					int hk = Hash(seed, i, k, 11);
					if (hk % 3 == 0)
						continue;
					float icicle = 0.15f + (hk % 7) * 0.06f;
					Primitive(PrimitiveType.Cube, "Icicle", root, _frost, new Vector3(xc + (k - 1) * w * 0.3f, WallCaseH - 0.2f - icicle * 0.5f, front - 0.06f), id,
						new Vector3(0.05f, icicle, 0.05f));
				}
				// Ice chunk on the floor in front of the case.
				if (h % 2 == 0)
					Primitive(PrimitiveType.Cube, "IceChunk", root, _frost, new Vector3(xc + (h % 5 - 2) * 0.15f, 0.08f, front - 0.6f - (h % 3) * 0.25f),
						Quaternion.Euler(h % 25, h % 90, h % 15), new Vector3(0.22f, 0.14f, 0.18f));
			}
			Solid(solid.gameObject, new Vector3(0, WallCaseH * 0.5f, 0), new Vector3(len + 0.1f, WallCaseH, WallCaseDepth + 0.1f), blocksBullets: true);
		}

		// Island of back-to-back chest freezers (coffin cases) along X: glass lids, some slid open with frost bulging out.
		private static void ChestIsland(Transform visuals, Transform solids, float z, float x0, float x1, int seed)
		{
			float len = x1 - x0;
			var (root, solid) = Pair(visuals, solids, "ChestIsland", V((x0 + x1) * 0.5f, z));
			var id = Quaternion.identity;
			Primitive(PrimitiveType.Cube, "Body", root, _freezer, new Vector3(0, IslandH * 0.5f, 0), id, new Vector3(len, IslandH, IslandW));
			Primitive(PrimitiveType.Cube, "Kick", root, _black, new Vector3(0, 0.08f, 0), id, new Vector3(len + 0.04f, 0.16f, IslandW + 0.04f));
			Primitive(PrimitiveType.Cube, "Divider", root, _freezerSign, new Vector3(0, IslandH + 0.06f, 0), id, new Vector3(len, 0.12f, 0.08f));
			int cells = Mathf.Max(1, Mathf.RoundToInt(len / 1.8f));
			float cw = len / cells;
			var lidSize = new Vector3(cw - 0.12f, 0.03f, IslandW * 0.5f - 0.12f);
			for (int i = 0; i < cells; i++)
				foreach (float s in new[] { -1f, 1f })
				{
					int h = Hash(seed, i, s > 0 ? 1 : 0, 5);
					float xc = -len * 0.5f + (i + 0.5f) * cw, zc = s * IslandW * 0.25f;
					if (h % 4 == 1)
					{
						// Lid slid half open; the frost inside has grown over the rim.
						Primitive(PrimitiveType.Cube, "LidOpen", root, _freezerDead, new Vector3(xc + cw * 0.35f, IslandH + 0.05f, zc), id, lidSize);
						Primitive(PrimitiveType.Cube, "FrostMound", root, _frost, new Vector3(xc - cw * 0.15f, IslandH + 0.08f, zc), Quaternion.Euler(0, h % 30 - 15f, 6f),
							new Vector3(cw * 0.6f, 0.3f, IslandW * 0.45f));
					}
					else
					{
						Primitive(PrimitiveType.Cube, "Lid", root, h % 5 == 0 ? _freezerGlow : _freezerDead, new Vector3(xc, IslandH + 0.015f, zc), id, lidSize);
						if (h % 3 == 0)
							Primitive(PrimitiveType.Cube, "FrostCrust", root, _frost, new Vector3(xc + (h % 3 - 1) * 0.2f, IslandH + 0.06f, zc), Quaternion.Euler(0, h % 20, 0),
								new Vector3(cw * 0.5f, 0.06f, IslandW * 0.35f));
					}
					// Frost running down the side + an ice chunk on the floor.
					if (h % 3 != 1)
						Primitive(PrimitiveType.Cube, "FrostRun", root, _frost, new Vector3(xc, IslandH * 0.6f, s * (IslandW * 0.5f + 0.02f)), id,
							new Vector3(cw * (0.3f + (h % 4) * 0.12f), IslandH * (0.5f + (h % 3) * 0.12f), 0.04f));
					if (h % 2 == 0)
						Primitive(PrimitiveType.Cube, "IceChunk", root, _frost, new Vector3(xc + (h % 5 - 2) * 0.2f, 0.07f, s * (IslandW * 0.5f + 0.4f + (h % 3) * 0.2f)),
							Quaternion.Euler(h % 20, h % 90, h % 25), new Vector3(0.2f, 0.12f, 0.24f));
				}
			Solid(solid.gameObject, new Vector3(0, 0.6f, 0), new Vector3(len + 0.1f, 1.2f, IslandW + 0.1f), blocksBullets: true);
		}

		// Irregular ice patch around the broken freezers, built from overlapping seeded blobs (OilSlick shape).
		// Zones: each blob is an OilSlick ModifierZone with the same TractionModifier (same Id → refreshed, not stacked);
		// its own renderer is off. Look: every blob outline is painted into ONE ground mesh (GroundPainter) — frost at
		// one height, glossy glaze 1 cm above — with world-planar UVs, so overlapping blobs of one layer are identical
		// pixels (no z-fighting, no stacked height), and the whole patch is a single renderer that receives decals.
		private static void IcePatch(Transform dept)
		{
			var patch = Child(dept, "IcePatch");
			patch.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			var rebuild = typeof(OilSlick).GetMethod("RebuildShape",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var blobs = new (float x, float z, float r, float sx, float sz, float yaw, int seed, bool glaze)[]
			{
				// back row (wall cases + islands at z 21.5)
				(-18f, 22.4f, 4.2f, 1.5f, 0.95f, 0f, 11, false),
				(0f, 21.8f, 4f, 1.6f, 1f, 8f, 12, false),
				(9.5f, 23.6f, 3.2f, 1.3f, 1f, -12f, 13, false),
				(-9.6f, 24.6f, 2.4f, 1.1f, 1f, 0f, 14, false),
				(-23.5f, 25.6f, 1.7f, 1f, 1f, 0f, 17, false),
				// between the rows
				(-11f, 18.2f, 2.6f, 1.4f, 0.9f, 25f, 15, false),
				(6f, 18.4f, 2.4f, 1.5f, 0.9f, -20f, 16, false),
				// front row (islands at z 14.5) and tongues toward the main aisle
				(-16f, 14.5f, 4.4f, 1.6f, 0.95f, -4f, 18, false),
				(4.5f, 14.2f, 3.6f, 1.5f, 1f, 10f, 19, false),
				(-5.5f, 12f, 2.2f, 1.2f, 0.85f, 35f, 20, false),
				(12f, 12.5f, 2f, 1.3f, 0.8f, -30f, 25, false),
				// glossy glaze
				(-19f, 22.7f, 2.3f, 1.6f, 0.8f, 5f, 21, true),
				(2f, 21.8f, 2.4f, 1.7f, 0.8f, -6f, 22, true),
				(10.5f, 24.6f, 1.5f, 1.2f, 1f, 30f, 23, true),
				(-8.8f, 25.4f, 1.1f, 1f, 1f, 0f, 24, true),
				(-15f, 14.7f, 2.4f, 1.7f, 0.8f, 0f, 26, true),
				(4f, 14.3f, 2f, 1.5f, 0.8f, 12f, 27, true),
			};
			const int segments = 40;
			var paint = new GroundPainter();
			for (int i = 0; i < blobs.Length; i++)
			{
				var b = blobs[i];
				var go = new GameObject(b.glaze ? "IceGlazeZone" : "IceZone");
				go.transform.SetParent(patch, false);
				go.transform.SetPositionAndRotation(new Vector3(b.x, FloorY, b.z), Quaternion.Euler(0, b.yaw, 0));
				go.transform.localScale = new Vector3(b.sx, 1f, b.sz);
				go.AddComponent<MeshFilter>();
				go.AddComponent<MeshRenderer>().enabled = false;   // OilSlick requires one; the look is the baked patch
				var zone = go.AddComponent<OilSlick>();
				SetField(zone, "_radius", b.r);
				SetField(zone, "_irregularity", 0.35f);
				SetField(zone, "_seed", b.seed);
				SetField(zone, "_linger", 0.1f);
				SetField(zone, "_modifiers", new Modifier[]
				{
					new TractionModifier { Id = ModifierConstants.TractionIce, Source = BuffSource.Environment, Value = IceGrip },
				});
				EditorUtility.SetDirty(zone);

				// Outline of the same blob in world space → a fan from its centre (star-shaped, so the fan is valid).
				rebuild?.Invoke(zone, null);
				var pts = new Vector2[segments + 2];
				pts[0] = new Vector2(b.x, b.z);
				for (int k = 0; k <= segments; k++)
				{
					float a = k * Mathf.PI * 2f / segments;
					float r = zone.RadiusAt(a);
					var w = go.transform.TransformPoint(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
					pts[k + 1] = new Vector2(w.x, w.z);
				}
				paint.Poly(b.glaze ? _iceGlaze : _iceFrost, b.glaze ? 0.04f : 0.03f, pts);
			}
			paint.Build(patch, $"{Name}_Ice");
		}

		// ================================================================== breakable pallets

		// Two breakable pallets of goods side by side in an aisle (was one solid stack): cover that can be shot,
		// blown up or struck apart. Prefab instances, so EntryPoint registers them like any scene Breakable.
		private static void PalletPair(Transform parent, Vector2 p, float yaw, int i, GameObject[] pallets)
		{
			if (pallets.Length == 0)
				return;
			var root = Child(parent, "PalletStack");
			root.SetPositionAndRotation(To3(p), Quaternion.Euler(0, yaw, 0));
			for (int k = 0; k < 2; k++)
			{
				var prefab = pallets[(i + k) % pallets.Length];
				if (prefab == null)
					continue;
				var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
				go.transform.SetLocalPositionAndRotation(k == 0 ? Vector3.zero : new Vector3(1.75f, 0f, 0.1f), Quaternion.Euler(0, k * 8f, 0));
			}
		}

		// Config: a copy of Crate (keeps its drop table) with its own id, 60 HP, one roll, debris left behind.
		private static GameObject[] BuildPalletBreakables()
		{
			if (AssetDatabase.LoadAssetAtPath<BreakableConfig>(PalletConfigPath) == null &&
			    !AssetDatabase.CopyAsset(CrateConfigSource, PalletConfigPath))
			{
				Debug.LogError($"[MallLevelBuilder] Can't copy {CrateConfigSource} → {PalletConfigPath}; pallets are not built.");
				return new GameObject[0];
			}
			var config = AssetDatabase.LoadAssetAtPath<BreakableConfig>(PalletConfigPath);
			var boxes = BuildPalletPrefab(PalletBoxesPrefabPath, config, wrapped: false);
			var wrapped = BuildPalletPrefab(PalletWrappedPrefabPath, config, wrapped: true);

			var so = new SerializedObject(config);
			so.FindProperty("_id").stringValue = PalletId;
			so.FindProperty("_prefab").objectReferenceValue = boxes != null ? boxes.GetComponent<Breakable>() : null;
			so.FindProperty("_maxHealth").floatValue = 60f;
			so.FindProperty("_damageableBy").intValue =
				(int)(BreakableDamageSources.Bullet | BreakableDamageSources.Explosion | BreakableDamageSources.Melee);
			var effects = so.FindProperty("_destructionEffects");
			effects.arraySize = 1;
			var fx = effects.GetArrayElementAtIndex(0);
			fx.FindPropertyRelative("EffectId").stringValue = BoxBreakId;
			fx.FindPropertyRelative("Delay").floatValue = 0f;
			fx.FindPropertyRelative("PointIndex").intValue = 0;
			so.FindProperty("_damageOnDestruction").boolValue = false;
			so.FindProperty("_lootCount").intValue = 1;
			so.FindProperty("_afterDestruction").enumValueIndex = (int)AfterDestruction.Debris;
			so.FindProperty("_debrisKeepsObstacle").boolValue = false;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(config);
			return new[] { boxes, wrapped };
		}

		// Pallet with cartons (2×2 + 2 on top) or one shrink-wrapped load. Intact and debris visuals are each baked
		// into one mesh (one renderer); a box collider + carving obstacle; the burst point at mid height.
		private static GameObject BuildPalletPrefab(string path, BreakableConfig config, bool wrapped)
		{
			string name = System.IO.Path.GetFileNameWithoutExtension(path);
			return SavePrefab(path, root =>
			{
				var id = Quaternion.identity;
				var size = new Vector3(1.6f, 1.3f, 1.2f);

				var intactTemp = new GameObject("Intact_Temp").transform;
				Primitive(PrimitiveType.Cube, "Pallet", intactTemp, _pallet, new Vector3(0, 0.07f, 0), id, new Vector3(1.6f, 0.14f, 1.2f));
				if (wrapped)
				{
					Primitive(PrimitiveType.Cube, "Load", intactTemp, _wrapBlue, new Vector3(0, 0.72f, 0), id, new Vector3(1.5f, 1.16f, 1.1f));
					Primitive(PrimitiveType.Cube, "TopCarton", intactTemp, _kraft, new Vector3(0.3f, 1.42f, 0.1f), Quaternion.Euler(0, 12f, 0),
						new Vector3(0.6f, 0.24f, 0.45f));
				}
				else
				{
					int n = 0;
					foreach (float dx in new[] { -0.38f, 0.38f })
						foreach (float dz in new[] { -0.28f, 0.28f })
							Primitive(PrimitiveType.Cube, "Carton", intactTemp, ++n % 3 == 0 ? _kraftDark : _kraft,
								new Vector3(dx, 0.14f + 0.27f, dz), Quaternion.Euler(0, (n % 2 == 0 ? 3f : -2f), 0), new Vector3(0.72f, 0.54f, 0.52f));
					foreach (float dx in new[] { -0.36f, 0.4f })
						Primitive(PrimitiveType.Cube, "CartonTop", intactTemp, dx < 0 ? _kraftDark : _kraft,
							new Vector3(dx, 0.68f + 0.24f, dx < 0 ? 0.05f : -0.08f), Quaternion.Euler(0, dx < 0 ? 90f : 6f, 0), new Vector3(0.7f, 0.48f, 0.5f));
				}
				var intact = Combine(intactTemp.gameObject, root.transform, $"{name}_Intact", castShadows: true);

				// Debris: the pallet knocked askew + flattened cartons / torn film around it. No shadows, no collider.
				var debrisTemp = new GameObject("Debris_Temp").transform;
				Primitive(PrimitiveType.Cube, "Pallet", debrisTemp, _pallet, new Vector3(0.08f, 0.09f, -0.05f), Quaternion.Euler(3f, 9f, 4f),
					new Vector3(1.6f, 0.14f, 1.2f));
				var scrap = wrapped ? _wrapBlue : _kraft;
				foreach (var (x, z, yaw, s) in new[] { (-0.9f, 0.5f, 25f, 0.7f), (0.95f, -0.45f, -40f, 0.6f), (0.2f, 0.75f, 70f, 0.5f), (-0.5f, -0.7f, -10f, 0.55f) })
					Primitive(PrimitiveType.Cube, "Scrap", debrisTemp, scrap, new Vector3(x, 0.02f, z), Quaternion.Euler(0, yaw, 0), new Vector3(s, 0.03f, s * 0.75f));
				Primitive(PrimitiveType.Cube, "Carton", debrisTemp, _kraftDark, new Vector3(0.1f, 0.38f, 0.1f), Quaternion.Euler(0, 30f, 12f), new Vector3(0.6f, 0.4f, 0.45f));
				var debris = Combine(debrisTemp.gameObject, root.transform, $"{name}_Debris", castShadows: false);
				debris.SetActive(false);

				var burst = Child(root.transform, "Burst");
				burst.localPosition = new Vector3(0, BurstHeight, 0);

				var collider = root.AddComponent<BoxCollider>();
				collider.center = new Vector3(0, size.y * 0.5f, 0);
				collider.size = size;
				root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				var obstacle = root.AddComponent<UnityEngine.AI.NavMeshObstacle>();
				obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
				obstacle.center = collider.center;
				obstacle.size = size + new Vector3(0.1f, 0f, 0.1f);
				obstacle.carving = true;
				obstacle.carveOnlyStationary = true;

				var breakable = root.AddComponent<Breakable>();
				SetField(breakable, "_config", config);
				SetField(breakable, "_collider", collider);
				SetField(breakable, "_obstacle", obstacle);
				SetField(breakable, "_intactVisual", intact);
				SetField(breakable, "_debrisVisual", debris);
				SetField(breakable, "_effectPoints", new[] { burst });
			});
		}

		/// <summary>
		/// _Prefabs/Effects/BoxBreak.prefab ("box_break"): Epic Toon FX "GibletExplodeWood" (splinters + dust), scaled
		/// down. Its splinters collide with the world (a physics query per particle); here they bounce off one
		/// analytic floor plane instead — same look, no physics cost.
		/// </summary>
		private static void BuildBoxBreakEffect()
		{
			var source = AssetDatabase.LoadAssetAtPath<GameObject>(GibletsSourcePath);
			if (source == null)
			{
				Debug.LogWarning($"[MallLevelBuilder] {GibletsSourcePath} not found — pallets break without splinters.");
				return;
			}
			var prefab = SavePrefab(BoxBreakPrefabPath, root =>
			{
				var effect = root.AddComponent<SceneEffect>();
				var giblets = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
				giblets.name = "Giblets";
				giblets.transform.localScale = Vector3.one * BurstScale;   // ETFX uses Hierarchy scaling
				// The effect plays at the pallet's burst point, BurstHeight above the floor.
				var floor = Child(root.transform, "FloorPlane");
				floor.localPosition = new Vector3(0, -BurstHeight, 0);
				foreach (var ps in giblets.GetComponentsInChildren<ParticleSystem>(true))
				{
					var collision = ps.collision;
					if (!collision.enabled)
						continue;
					collision.type = ParticleSystemCollisionType.Planes;
					collision.SetPlane(0, floor);
				}
				var so = new SerializedObject(effect);
				so.FindProperty("_particles").objectReferenceValue = giblets.GetComponent<ParticleSystem>();
				so.ApplyModifiedPropertiesWithoutUndo();
			});
			if (prefab != null)
				RegisterEffect(BoxBreakId, prefab, 2f);
		}

		private static void Forklift(Transform parent, Vector2 p, float yaw)
		{
			var root = Child(parent, "Forklift");
			root.SetPositionAndRotation(To3(p), Quaternion.Euler(0, yaw, 0));
			var id = Quaternion.identity;
			Primitive(PrimitiveType.Cube, "Body", root, _forklift, new Vector3(0, 0.65f, -0.3f), id, new Vector3(1.2f, 1f, 2f));
			Primitive(PrimitiveType.Cube, "Counterweight", root, _black, new Vector3(0, 0.6f, -1.35f), id, new Vector3(1.2f, 0.9f, 0.4f));
			Primitive(PrimitiveType.Cube, "Seat", root, _black, new Vector3(0, 1.3f, -0.6f), id, new Vector3(0.6f, 0.3f, 0.5f));
			foreach (float s in new[] { -1f, 1f })
			{
				Primitive(PrimitiveType.Cube, "CagePost", root, _black, new Vector3(s * 0.55f, 1.6f, 0.4f), id, new Vector3(0.06f, 1.9f, 0.06f));
				Primitive(PrimitiveType.Cube, "CagePostR", root, _black, new Vector3(s * 0.55f, 1.6f, -1f), id, new Vector3(0.06f, 1.9f, 0.06f));
				Primitive(PrimitiveType.Cube, "Mast", root, _black, new Vector3(s * 0.35f, 1.3f, 0.85f), id, new Vector3(0.1f, 2.6f, 0.12f));
				Primitive(PrimitiveType.Cube, "Fork", root, _cartMetal, new Vector3(s * 0.3f, 0.08f, 1.5f), id, new Vector3(0.12f, 0.05f, 1.1f));
				foreach (float z in new[] { -1f, 0.5f })
					Primitive(PrimitiveType.Cylinder, "Wheel", root, _black, new Vector3(s * 0.62f, 0.25f, z), Quaternion.Euler(0, 0, 90), new Vector3(0.5f, 0.2f, 0.5f));
			}
			Primitive(PrimitiveType.Cube, "Roof", root, _forklift, new Vector3(0, 2.55f, -0.3f), id, new Vector3(1.2f, 0.06f, 1.5f));
			Solid(root.gameObject, new Vector3(0, 0.9f, 0f), new Vector3(1.4f, 1.8f, 3.2f), blocksBullets: true);
		}

		// Flatbed shopping cart with a couple of boxes; small obstacle, bullets pass.
		private static void FlatCart(Transform parent, Vector2 p, float yaw)
		{
			var root = Child(parent, "FlatCart");
			root.SetPositionAndRotation(To3(p), Quaternion.Euler(0, yaw, 0));
			var id = Quaternion.identity;
			Primitive(PrimitiveType.Cube, "Deck", root, _cartMetal, new Vector3(0, 0.35f, 0), id, new Vector3(0.8f, 0.05f, 1.6f));
			Primitive(PrimitiveType.Cube, "Handle", root, _cartMetal, new Vector3(0, 0.75f, -0.8f), id, new Vector3(0.8f, 0.8f, 0.05f));
			Primitive(PrimitiveType.Cube, "Box", root, _kraft, new Vector3(0, 0.6f, 0.2f), Quaternion.Euler(0, 12f, 0), new Vector3(0.6f, 0.45f, 0.7f));
			Primitive(PrimitiveType.Cube, "Box2", root, _whiteBox, new Vector3(0.05f, 0.95f, 0.25f), Quaternion.Euler(0, -8f, 0), new Vector3(0.45f, 0.25f, 0.5f));
			Solid(root.gameObject, new Vector3(0, 0.6f, 0), new Vector3(0.9f, 1.2f, 1.8f), blocksBullets: false);
		}

		private static void PaintFloor(Transform root, float[] docks)
		{
			var g = new GroundPainter();
			// Yellow safety lines along every rack row, walkway lines along the main aisle.
			foreach (float z in RowZ)
				foreach (var (x0, x1) in RackSpans)
					foreach (float s in new[] { -1f, 1f })
						g.Rect(_paintYellow, 0.01f, x0, x1, z + s * (RackDepth * 0.5f + 0.35f) - 0.06f, z + s * (RackDepth * 0.5f + 0.35f) + 0.06f);
			foreach (float s in new[] { -1f, 1f })
				g.Rect(_paintYellow, 0.01f, -24f, 14f, s * 3.4f - 0.05f, s * 3.4f + 0.05f);
			// Red/white "keep clear" hatching in front of the loading docks.
			foreach (float z in docks)
				for (int i = 0; i < 6; i++)
					g.Poly(i % 2 == 0 ? _paintRed : _wallTrim, 0.01f, new[]
					{
						new Vector2(X0 + 0.5f, z - 2.4f + i * 0.8f), new Vector2(X0 + 0.5f, z - 1.6f + i * 0.8f),
						new Vector2(X0 + 3.5f, z - 1.6f + i * 0.8f + 0.6f), new Vector2(X0 + 3.5f, z - 2.4f + i * 0.8f + 0.6f),
					});
			// Checkout lane floor tiles (darker band) and the entrance mats.
			g.Rect(_belt, 0.01f, 23.5f, 30f, Z0 + 0.6f, Z1 - 0.6f);
			foreach (float z in new[] { -12f, 12f })
				g.Rect(_black, 0.02f, X1 - 3.5f, X1 - 0.4f, z - 1.8f, z + 1.8f);
			g.Build(root, $"{Name}_Paint");
		}

		private static void CreateMallMaterials()
		{
			_floor = Mat("Mall_FloorConcrete", new Color(0.6f, 0.6f, 0.58f), 0f, 0.55f);
			_wallMat = Mat("Mall_WallPanel", new Color(0.78f, 0.79f, 0.8f), 0.2f, 0.3f);
			_wallTrim = Mat("Mall_WallTrim", new Color(0.92f, 0.92f, 0.9f), 0f, 0.3f);
			_glass = Mat("Mall_StoreGlass", new Color(0.35f, 0.5f, 0.58f), 0.1f, 0.9f);
			_upright = Mat("Mall_RackOrange", new Color(0.95f, 0.45f, 0.08f), 0.3f, 0.4f);
			_beam = Mat("Mall_RackBlue", new Color(0.1f, 0.28f, 0.62f), 0.3f, 0.4f);
			_deck = Mat("Mall_WireDeck", new Color(0.55f, 0.57f, 0.6f), 0.6f, 0.4f);
			_pallet = Mat("Mall_Pallet", new Color(0.62f, 0.48f, 0.3f), 0f, 0.1f);
			_kraft = Mat("Mall_BoxKraft", new Color(0.72f, 0.55f, 0.35f), 0f, 0.1f);
			_kraftDark = Mat("Mall_BoxKraftDark", new Color(0.58f, 0.42f, 0.26f), 0f, 0.1f);
			_whiteBox = Mat("Mall_BoxWhite", new Color(0.9f, 0.9f, 0.87f), 0f, 0.2f);
			_wrapBlue = Mat("Mall_ShrinkWrapBlue", new Color(0.35f, 0.55f, 0.78f), 0f, 0.8f);
			_redBox = Mat("Mall_BoxRed", new Color(0.72f, 0.14f, 0.12f), 0f, 0.3f);
			_column = Mat("Mall_ColumnSteel", new Color(0.42f, 0.44f, 0.47f), 0.6f, 0.4f);
			_guard = Mat("Mall_GuardYellow", new Color(0.98f, 0.8f, 0.1f), 0f, 0.4f);
			_paintYellow = Mat("Mall_PaintYellow", new Color(0.95f, 0.78f, 0.1f), 0f, 0.3f);
			_paintRed = Mat("Mall_PaintRed", new Color(0.8f, 0.12f, 0.1f), 0f, 0.3f);
			_counter = Mat("Mall_Counter", new Color(0.3f, 0.32f, 0.36f), 0.2f, 0.4f);
			_belt = Mat("Mall_Belt", new Color(0.12f, 0.12f, 0.13f), 0f, 0.3f);
			_register = Mat("Mall_Register", new Color(0.82f, 0.82f, 0.8f), 0.1f, 0.4f);
			_freezer = Mat("Mall_FreezerBody", new Color(0.85f, 0.87f, 0.9f), 0.3f, 0.5f);
			_freezerGlow = Mat("Mall_FreezerGlow", new Color(0.75f, 0.9f, 1f), 0f, 0.9f, new Color(0.9f, 1.3f, 1.6f));
			_forklift = Mat("Mall_Forklift", new Color(0.95f, 0.65f, 0.08f), 0.2f, 0.5f);
			_black = Mat("Mall_Black", new Color(0.07f, 0.07f, 0.08f), 0f, 0.3f);
			_cartMetal = Mat("Mall_CartMetal", new Color(0.6f, 0.62f, 0.65f), 0.7f, 0.5f);
			_sign = Mat("Mall_SignRed", new Color(0.82f, 0.1f, 0.12f), 0f, 0.4f, new Color(0.3f, 0.02f, 0.02f));
			_freezerDead = Mat("Mall_FreezerDead", new Color(0.32f, 0.4f, 0.46f), 0.1f, 0.9f, new Color(0.05f, 0.08f, 0.1f));
			_freezerSign = Mat("Mall_FreezerSignBlue", new Color(0.12f, 0.35f, 0.75f), 0f, 0.4f, new Color(0.05f, 0.15f, 0.4f));
			_frost = Mat("Mall_Frost", new Color(0.9f, 0.95f, 1f), 0f, 0.35f, new Color(0.08f, 0.1f, 0.12f));
			_iceFrost = Mat("Mall_IceFrost", new Color(0.78f, 0.88f, 0.95f), 0f, 0.55f);
			_iceGlaze = Mat("Mall_IceGlaze", new Color(0.62f, 0.8f, 0.93f), 0.1f, 0.95f);

			// Material textures (low detail, just what the thing is made of). Tints keep the old average colours.
			LevelTextures.Apply(_pallet, LevelTextures.PalletWood, Color.white);
			LevelTextures.Apply(_kraft, LevelTextures.CardboardMarked, new Color(0.87f, 0.79f, 0.7f));
			LevelTextures.Apply(_kraftDark, LevelTextures.CardboardPlain, new Color(0.67f, 0.62f, 0.55f));
			LevelTextures.Apply(_wrapBlue, LevelTextures.PlasticWrap, new Color(0.47f, 0.74f, 1f));
			LevelTextures.Apply(_redBox, LevelTextures.PlasticWrap, new Color(0.97f, 0.19f, 0.16f));
			// The ice patch mesh has world UVs (metres / 4): 0.5 → one tile per 8 m; the glaze at a different scale.
			LevelTextures.Apply(_iceFrost, LevelTextures.Ice, Color.white, 0.5f);
			LevelTextures.Apply(_iceGlaze, LevelTextures.Ice, new Color(0.85f, 0.95f, 1f), 0.7f);
			LevelTextures.Apply(_frost, LevelTextures.Ice, Color.white);
		}
	}
}
