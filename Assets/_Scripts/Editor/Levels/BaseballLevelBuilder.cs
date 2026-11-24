using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using static Crowds.EditorTools.KeyLevelBuilder;
using static Crowds.EditorTools.VenueKit;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Campaign level "Бейсбольное поле" (Level_06_Baseball) — Tools ▸ Crowds ▸ Build Baseball Level. A ballpark:
	/// home plate near the camera (+X), the outfield up the screen (−X). Padded walls enclose fair + foul territory:
	/// backstop, two foul-side walls, the outfield arc (R 58 from home). Zombies come out of both dugouts and three
	/// doors in the outfield wall (dark vestibules on the MobOnly navmesh area).
	/// Difficulty: above the Key level, below the rest — 3 quota "innings" (30 / 44 / 58 mobs), Fatmen from inning 2,
	/// a shotgun on the mound in the first break. Cover: tarp rolls, equipment carts, a batting cage, crates.
	/// Safe to re-run. See Docs/ArenaLevelsFeature.md.
	/// </summary>
	public static class BaseballLevelBuilder
	{
		private const string Name = "Level_06_Baseball";
		private const string AssetName = "Level_Baseball";
		internal const string DisplayName = "Бейсбольное поле";

		private static readonly Vector2 Home = new(32f, 0f);
		private const float ArcR = 58f, BasePath = 20f, InfieldR = 29f, WallH = 2.4f;
		private const float FoulHalf = 45f;    // foul lines at 180° ± 45°
		private const float ArcHalf = 49.07f;  // the outfield arc meets the short side walls at z ±43.8

		private static Material _grass, _grassAlt, _dirt, _dirtDark, _chalk, _pad, _padTop, _foulPole, _seatA, _seatB, _riser;
		private static Material _tarp, _cart, _net, _metal, _light, _board, _boardLit, _bench, _rubber;

		[MenuItem("Tools/Crowds/Build Baseball Level")]
		public static void Build()
		{
			Init();
			CreateBallparkMaterials();
			var cleared = MobsCleared();
			var stages = new List<DifficultyStage>
			{
				Quota(DifficultyLevel.level1, 5f, 0.45f, cleared, "Иннинг 1/3: зомби высыпали из дагаутов",
					("Slow", 20), ("Fast", 10)),
				Breather(DifficultyLevel.level2, 12f, "Перерыв: на питчерской горке дробовик и припасы"),
				Quota(DifficultyLevel.level3, 3f, 0.35f, cleared, "Иннинг 2/3: толстяки на поле!",
					("Fast", 30), ("Slow", 10), ("Fatman", 4)),
				Breather(DifficultyLevel.level4, 12f, "Перерыв: припасы у баз"),
				Quota(DifficultyLevel.level5, 3f, 0.3f, cleared, "Иннинг 3/3: решающий",
					("Fast", 35), ("Slow", 15), ("Fatman", 7), ("FatmanStrong", 1)),
				Finish(),
			};
			var config = Config($"{ConfigFolder}/{AssetName}.asset", stages, new Vector2(6f, 70f),
				new[] { "Pistol", "SMG", "Shotgun" }, new[] { "speedup" });

			var prefab = BuildLevel(config);
			Definition(prefab, AssetName, DisplayName);
			AssetDatabase.SaveAssets();
			CampaignSetup.Apply();
			Debug.Log($"[BaseballLevelBuilder] Level built: {LevelFolder}/{Name}.prefab");
		}

		private static Vector2 Arc(float deg) => Home + Dir(deg) * ArcR;

		private static GameObject BuildLevel(LevelConfig config)
		{
			var l = Begin(Name, new Rect(-28f, -46f, 67f, 92f), _grass);

			// --- Walls: backstop, foul-side walls (with the dugout gaps), short returns, outfield arc (3 door gaps) ---
			var backA = new Vector2(37f, -12f);
			var backB = new Vector2(37f, 12f);
			WallSeg(l.Walls, "Backstop", backA, backB, WallH, _pad, _padTop);
			const float dugoutAt = 14f, sideLen = 45f;
			var doorSpots = new List<(Vector3 pos, Vector3 inward, string name, bool dugout)>();
			foreach (float s in new[] { -1f, 1f })
			{
				var a = new Vector2(37f, 12f * s);
				var dir = new Vector2(-1f, s).normalized;
				var b = a + dir * sideLen;
				WallWithGaps(l.Walls, $"Side{(s < 0 ? "S" : "N")}", a, b, WallH, _pad, _padTop, 4.2f, dugoutAt);
				var arcEnd = Arc(180f - s * ArcHalf);
				WallSeg(l.Walls, $"Return{(s < 0 ? "S" : "N")}", b, arcEnd, WallH, _pad, _padTop);
				var p = a + dir * dugoutAt;
				var inward = new Vector2(-0.7071f, -0.7071f * s);
				doorSpots.Add((To3(p), new Vector3(inward.x, 0, inward.y), $"Dugout_{(s < 0 ? "1B" : "3B")}", true));
			}
			// Outfield arc in chords, leaving 3.4 m gaps at the doors (centre field, left/right centre).
			float[] doors = { 180f - 28f, 180f, 180f + 28f };
			float gapDeg = 3.4f / ArcR * Mathf.Rad2Deg;
			var pieces = new List<(float, float)>();
			float cursor = 180f - ArcHalf;
			foreach (float d in doors)
			{
				pieces.Add((cursor, d - gapDeg * 0.5f));
				cursor = d + gapDeg * 0.5f;
			}
			pieces.Add((cursor, 180f + ArcHalf));
			int seg = 0;
			foreach (var (a0, a1) in pieces)
			{
				int n = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / 6f));
				for (int i = 0; i < n; i++)
					WallSeg(l.Walls, $"Outfield_{++seg}", Arc(Mathf.Lerp(a0, a1, i / (float)n)), Arc(Mathf.Lerp(a0, a1, (i + 1) / (float)n)),
						WallH, _pad, _padTop);
			}
			foreach (float d in doors)
			{
				var p = Arc(d);
				var inward = (Home - p).normalized;
				doorSpots.Add((To3(p), new Vector3(inward.x, 0, inward.y), $"OutfieldDoor_{d:0}", false));
			}

			// --- Spawn points: deep in the dugouts and outfield doors ---
			var mobs = new[]
			{
				Mob("Slow", 3, (DifficultyLevel.level1, 3f), (DifficultyLevel.level3, 3f), (DifficultyLevel.level5, 2.5f)),
				Mob("Fast", 2, (DifficultyLevel.level1, 3.5f), (DifficultyLevel.level3, 2f), (DifficultyLevel.level5, 1.8f)),
				Mob("Fatman", 1, (DifficultyLevel.level3, 6f), (DifficultyLevel.level5, 5f)),
				Mob("FatmanStrong", 1, (DifficultyLevel.level5, 10f)),
			};
			foreach (var (pos, inward, name, dugout) in doorSpots)
			{
				var spawn = MobDoor(l.Props, name, pos, inward, dugout ? 4.2f : 3.4f, dugout ? 4.5f : 4f, 2.4f, _pad);
				Spawn(l.Spawns, dugout ? "Dugout" : "Outfield", spawn, mobs);
				if (dugout)
					Dugout(l.Props, pos, inward);
			}

			// --- Field paint (one decal-receiving mesh) ---
			PaintField(l.Root.transform);

			// --- Venue outside the walls (visual) ---
			foreach (float s in new[] { -1f, 1f })
			{
				var a = new Vector2(37f, 12f * s);
				var dir = new Vector2(-1f, s).normalized;
				var outward = new Vector2(0.7071f, 0.7071f * s);
				Bleachers(l.Props, a + dir * 20f + outward * 0.8f, a + dir * 43f + outward * 0.8f, outward, 5, _seatA, _seatB, _riser);
				FoulPole(l.Props, Arc(180f - s * ArcHalf));
				LightTower(l.Props, new Vector2(-21f, 45f * s));
			}
			Scoreboard(l.Props, new Vector2(-37f, 0f)); // behind the centre-field door

			// --- Cover on the field ---
			TarpRoll(l.Props, new Vector2(20f, 24f), 45f);
			TarpRoll(l.Props, new Vector2(20f, -24f), -45f);
			EquipmentCart(l.Props, new Vector2(-2f, 18f), 30f);
			EquipmentCart(l.Props, new Vector2(-2f, -18f), -30f);
			BattingCage(l.Props, new Vector2(-12f, 0f));
			var crate = AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath);
			if (crate != null)
				foreach (var (p, i) in new[] { (new Vector2(10f, 22f), 0), (new Vector2(10f, -22f), 1), (new Vector2(-14f, 28f), 2), (new Vector2(-14f, -28f), 3) })
				{
					var c = (GameObject)PrefabUtility.InstantiatePrefab(crate, l.Props);
					c.name = $"Crate_{i + 1}";
					c.transform.SetPositionAndRotation(To3(p), Quaternion.Euler(0, 17f + i * 37f, 0));
					c.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				}

			// --- Mission: zone (ammo fallback), loot ---
			Zone(l.Mission, "field", V(6f, 0f), new Vector3(64f, 2f, 88f));
			StageLootPoint(l.Loot, "Start", V(16f, -3f), DifficultyLevel.level1, 1f, Ammo(Caliber.Mm9, 30));
			StageLootPoint(l.Loot, "Mound", V(18.5f, 0f), DifficultyLevel.level2, 1.6f,
				Weapon("Shotgun"), Ammo(Caliber.Gauge12, 24), Ammo(Caliber.Mm9, 30), Health(30));
			StageLootPoint(l.Loot, "FirstBase", V(17.9f, -14.1f), DifficultyLevel.level4, 1.2f,
				Ammo(Caliber.Gauge12, 24), Health(30), Grenade());
			StageLootPoint(l.Loot, "ThirdBase", V(17.9f, 14.1f), DifficultyLevel.level4, 1.2f,
				Ammo(Caliber.None, 30), Health(25), Grenade());
			StageLootPoint(l.Loot, "SecondBase", V(3.7f, 0f), DifficultyLevel.level4, 1f, Bonus("shield"));
			foreach (var p in new[] { V(22f, 8f), V(22f, -8f), V(2f, 10f), V(2f, -10f), V(-12f, 8f) })
				FallbackPoint(l, p);

			return VenueKit.Finish(l, config, V(16f, 0f), -90f, $"{LevelFolder}/{Name}.prefab");
		}

		// ================================================================== paint

		private static void PaintField(Transform root)
		{
			var g = new GroundPainter();
			// Mowing stripes (lighter bands across the whole park), the warning track along the outfield wall.
			for (float x = -28f; x < 39f; x += 6f)
				g.Rect(_grassAlt, 0.01f, x, x + 3f, -46f, 46f);
			g.Band(_dirtDark, 0.02f, Home, ArcR - 3.2f, ArcR, 180f - ArcHalf, 180f + ArcHalf, 64);
			// Infield: dirt wedge from home, the grass diamond on it, base paths, cut-outs, mound.
			g.Pie(_dirt, 0.02f, Home, InfieldR, 180f - FoulHalf, 180f + FoulHalf, 40);
			var b1 = Home + Dir(180f + FoulHalf) * BasePath;
			var b3 = Home + Dir(180f - FoulHalf) * BasePath;
			var b2 = Home + Dir(180f) * BasePath * Mathf.Sqrt(2f);
			var centre = (Home + b2) * 0.5f;
			float half = BasePath * 0.7071f - 2.6f;
			g.Poly(_grass, 0.03f, new[] { centre + new Vector2(half, 0), centre + new Vector2(0, half), centre - new Vector2(half, 0), centre - new Vector2(0, half) });
			foreach (var (a, b) in new[] { (Home, b1), (b1, b2), (b2, b3), (b3, Home) })
				g.Line(_dirt, 0.04f, a, b, 1.3f);
			foreach (var b in new[] { b1, b2, b3 })
				g.Disc(_dirt, 0.04f, b, 2.1f, 24);
			g.Disc(_dirt, 0.04f, Home, 4.2f, 32);
			var mound = Home + Dir(180f) * BasePath * Mathf.Sqrt(2f) * 0.476f;
			g.Disc(_dirt, 0.04f, mound, 2.8f, 28);
			g.Rect(_chalk, 0.05f, mound.x - 0.1f, mound.x + 0.1f, -0.35f, 0.35f); // pitcher's rubber
			// Chalk: foul lines to the wall, batter's boxes, bases, home plate.
			foreach (float s in new[] { -1f, 1f })
				g.Line(_chalk, 0.05f, Home + Dir(180f + s * FoulHalf) * 1.5f, Home + Dir(180f + s * FoulHalf) * (ArcR - 0.6f), 0.14f);
			foreach (float s in new[] { -1f, 1f })
			{
				float z0 = s * 0.6f, z1 = s * 1.8f;
				var c0 = new Vector2(Home.x - 1.2f, Mathf.Min(z0, z1));
				var c1 = new Vector2(Home.x + 1.2f, Mathf.Max(z0, z1));
				g.Line(_chalk, 0.05f, new Vector2(c0.x, c0.y), new Vector2(c1.x, c0.y), 0.08f);
				g.Line(_chalk, 0.05f, new Vector2(c0.x, c1.y), new Vector2(c1.x, c1.y), 0.08f);
				g.Line(_chalk, 0.05f, new Vector2(c0.x, c0.y), new Vector2(c0.x, c1.y), 0.08f);
				g.Line(_chalk, 0.05f, new Vector2(c1.x, c0.y), new Vector2(c1.x, c1.y), 0.08f);
			}
			foreach (var b in new[] { b1, b2, b3 })
				g.Poly(_chalk, 0.06f, new[] { b + new Vector2(0.45f, 0), b + new Vector2(0, 0.45f), b - new Vector2(0.45f, 0), b - new Vector2(0, 0.45f) });
			g.Poly(_chalk, 0.06f, new[]
			{
				Home + new Vector2(0.45f, 0f), Home + new Vector2(0.15f, 0.3f), Home + new Vector2(-0.3f, 0.3f),
				Home + new Vector2(-0.3f, -0.3f), Home + new Vector2(0.15f, -0.3f),
			});
			// On-deck circles in front of the dugouts.
			foreach (float s in new[] { -1f, 1f })
				g.Ring(_chalk, 0.05f, new Vector2(29f, 12f * s), 0.75f, 0.85f, 24);
			g.Build(root, $"{Name}_Paint");
		}

		// ================================================================== props

		// Bench, helmet rack and a roof slab over the dugout vestibule (outside the wall).
		private static void Dugout(Transform parent, Vector3 wall, Vector3 inward)
		{
			var root = Child(parent, "DugoutDressing");
			root.SetPositionAndRotation(wall, Quaternion.LookRotation(-inward));
			root.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			Primitive(PrimitiveType.Cube, "Roof", root, _riser, new Vector3(0, 2.6f, 2.3f), Quaternion.identity, new Vector3(9f, 0.2f, 5f));
			foreach (float s in new[] { -1f, 1f })
			{
				Primitive(PrimitiveType.Cube, "Bench", root, _bench, new Vector3(s * 3.3f, 0.45f, 0.9f), Quaternion.identity, new Vector3(2.2f, 0.1f, 0.5f));
				Primitive(PrimitiveType.Cube, "BenchLeg", root, _metal, new Vector3(s * 3.3f, 0.22f, 0.9f), Quaternion.identity, new Vector3(0.1f, 0.44f, 0.4f));
			}
		}

		private static void FoulPole(Transform parent, Vector2 p)
		{
			var go = Primitive(PrimitiveType.Cylinder, "FoulPole", parent, _foulPole, new Vector3(p.x, FloorY + 4.5f, p.y), Quaternion.identity,
				new Vector3(0.22f, 9f, 0.22f));
			go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
		}

		private static void LightTower(Transform parent, Vector2 p)
		{
			var root = Child(parent, "LightTower");
			root.position = To3(p);
			root.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			Primitive(PrimitiveType.Cylinder, "Pole", root, _metal, new Vector3(0, 4.5f, 0), Quaternion.identity, new Vector3(0.45f, 9f, 0.45f));
			var face = Quaternion.LookRotation(new Vector3(Home.x - p.x, 0, Home.y - p.y));
			var bank = Child(root, "Bank");
			bank.localPosition = new Vector3(0, 9.2f, 0);
			bank.localRotation = face * Quaternion.Euler(25f, 0, 0);
			Primitive(PrimitiveType.Cube, "Frame", bank, _metal, Vector3.zero, Quaternion.identity, new Vector3(3.4f, 1.6f, 0.25f));
			for (int r = 0; r < 2; r++)
				for (int c = 0; c < 4; c++)
					Primitive(PrimitiveType.Cube, "Lamp", bank, _light, new Vector3(-1.2f + c * 0.8f, -0.4f + r * 0.8f, 0.14f), Quaternion.identity,
						new Vector3(0.6f, 0.6f, 0.05f));
		}

		// Big scoreboard on posts behind centre field, facing home: dark board with a grid of amber bulbs.
		private static void Scoreboard(Transform parent, Vector2 p)
		{
			var root = Child(parent, "Scoreboard");
			root.SetPositionAndRotation(To3(p), Quaternion.LookRotation(new Vector3(1f, 0, 0)));
			root.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			foreach (float s in new[] { -1f, 1f })
				Primitive(PrimitiveType.Cube, "Post", root, _metal, new Vector3(s * 4.5f, 2.5f, -0.3f), Quaternion.identity, new Vector3(0.4f, 5f, 0.4f));
			Primitive(PrimitiveType.Cube, "Board", root, _board, new Vector3(0, 6.5f, 0), Quaternion.identity, new Vector3(13f, 4.5f, 0.5f));
			Primitive(PrimitiveType.Cube, "Trim", root, _padTop, new Vector3(0, 8.85f, 0), Quaternion.identity, new Vector3(13.2f, 0.2f, 0.55f));
			// Innings row + two score rows of bulbs.
			for (int c = 0; c < 9; c++)
				for (int r = 0; r < 2; r++)
					if ((c + r * 3) % 4 != 0)
						Primitive(PrimitiveType.Cube, "Bulb", root, _boardLit, new Vector3(-4.8f + c * 1.0f, 5.6f + r * 1.0f, 0.27f),
							Quaternion.identity, new Vector3(0.55f, 0.55f, 0.04f));
			Primitive(PrimitiveType.Cube, "Title", root, _boardLit, new Vector3(0, 7.9f, 0.27f), Quaternion.identity, new Vector3(6f, 0.5f, 0.04f));
		}

		// Rolled infield tarp lying on the foul grass: solid cover (carves, stops bullets).
		private static void TarpRoll(Transform parent, Vector2 p, float yaw)
		{
			var root = Child(parent, "TarpRoll");
			root.SetPositionAndRotation(To3(p), Quaternion.Euler(0, yaw, 0));
			Primitive(PrimitiveType.Cylinder, "Roll", root, _tarp, new Vector3(0, 0.6f, 0), Quaternion.Euler(0, 0, 90), new Vector3(1.2f, 7f, 1.2f));
			foreach (float x in new[] { -3.55f, 3.55f })
				Primitive(PrimitiveType.Cylinder, "Cap", root, _metal, new Vector3(x, 0.6f, 0), Quaternion.Euler(0, 0, 90), new Vector3(0.5f, 0.1f, 0.5f));
			Solid(root.gameObject, new Vector3(0, 0.6f, 0), new Vector3(7.2f, 1.2f, 1.3f), blocksBullets: true);
		}

		// Equipment cart: steel bin with ball buckets and bats. Solid cover.
		private static void EquipmentCart(Transform parent, Vector2 p, float yaw)
		{
			var root = Child(parent, "EquipmentCart");
			root.SetPositionAndRotation(To3(p), Quaternion.Euler(0, yaw, 0));
			Primitive(PrimitiveType.Cube, "Bin", root, _cart, new Vector3(0, 0.6f, 0), Quaternion.identity, new Vector3(2.4f, 0.8f, 1.2f));
			foreach (float x in new[] { -0.8f, 0f, 0.8f })
			{
				Primitive(PrimitiveType.Cylinder, "Bucket", root, _chalk, new Vector3(x, 1.18f, 0.2f), Quaternion.identity, new Vector3(0.45f, 0.36f, 0.45f));
				Primitive(PrimitiveType.Cylinder, "Bat", root, _bench, new Vector3(x + 0.3f, 1.2f, -0.35f), Quaternion.Euler(0, 0, 70), new Vector3(0.07f, 0.9f, 0.07f));
			}
			foreach (float x in new[] { -1f, 1f })
				foreach (float z in new[] { -0.5f, 0.5f })
					Primitive(PrimitiveType.Cylinder, "Wheel", root, _riser, new Vector3(x, 0.12f, z), Quaternion.Euler(90, 0, 0), new Vector3(0.24f, 0.06f, 0.24f));
			Solid(root.gameObject, new Vector3(0, 0.6f, 0), new Vector3(2.5f, 1.2f, 1.3f), blocksBullets: true);
		}

		// Portable batting cage (frame + net): carves the navmesh, bullets pass through the net.
		private static void BattingCage(Transform parent, Vector2 p)
		{
			var root = Child(parent, "BattingCage");
			root.SetPositionAndRotation(To3(p), Quaternion.identity);
			const float L = 7f, W = 4f, H = 3f;
			foreach (float x in new[] { -L * 0.5f, 0f, L * 0.5f })
				foreach (float z in new[] { -W * 0.5f, W * 0.5f })
					Primitive(PrimitiveType.Cube, "Post", root, _metal, new Vector3(x, H * 0.5f, z), Quaternion.identity, new Vector3(0.08f, H, 0.08f));
			foreach (float z in new[] { -W * 0.5f, W * 0.5f })
				Primitive(PrimitiveType.Cube, "NetSide", root, _net, new Vector3(0, H * 0.5f, z), Quaternion.identity, new Vector3(L, H, 0.02f));
			Primitive(PrimitiveType.Cube, "NetBack", root, _net, new Vector3(-L * 0.5f, H * 0.5f, 0), Quaternion.identity, new Vector3(0.02f, H, W));
			Primitive(PrimitiveType.Cube, "TopRail", root, _metal, new Vector3(0, H, 0), Quaternion.identity, new Vector3(L, 0.08f, 0.08f));
			Solid(root.gameObject, new Vector3(0, H * 0.5f, 0), new Vector3(L + 0.2f, H, W + 0.2f), blocksBullets: false);
		}

		private static void CreateBallparkMaterials()
		{
			_grass = Mat("Ball_Grass", new Color(0.24f, 0.5f, 0.2f), 0f, 0.15f);
			_grassAlt = Mat("Ball_GrassAlt", new Color(0.3f, 0.58f, 0.24f), 0f, 0.15f);
			_dirt = Mat("Ball_Dirt", new Color(0.66f, 0.44f, 0.28f), 0f, 0.1f);
			_dirtDark = Mat("Ball_WarningTrack", new Color(0.5f, 0.33f, 0.22f), 0f, 0.1f);
			_chalk = Mat("Ball_Chalk", new Color(0.95f, 0.95f, 0.92f), 0f, 0.2f);
			_pad = Mat("Ball_WallPad", new Color(0.08f, 0.26f, 0.17f), 0f, 0.25f);
			_padTop = Mat("Ball_WallTop", new Color(0.95f, 0.78f, 0.12f), 0f, 0.3f);
			_foulPole = Mat("Ball_FoulPole", new Color(1f, 0.85f, 0.1f), 0.2f, 0.4f);
			_seatA = Mat("Ball_SeatBlue", new Color(0.12f, 0.27f, 0.6f), 0f, 0.35f);
			_seatB = Mat("Ball_SeatRed", new Color(0.6f, 0.12f, 0.12f), 0f, 0.35f);
			_riser = Mat("Ball_Concrete", new Color(0.55f, 0.55f, 0.53f), 0f, 0.15f);
			_tarp = Mat("Ball_Tarp", new Color(0.12f, 0.24f, 0.42f), 0f, 0.4f);
			_cart = Mat("Ball_CartSteel", new Color(0.35f, 0.37f, 0.4f), 0.6f, 0.4f);
			_net = Mat("Ball_Net", new Color(0.1f, 0.1f, 0.11f), 0f, 0.1f);
			_metal = Mat("Ball_Metal", new Color(0.5f, 0.52f, 0.55f), 0.7f, 0.45f);
			_light = Mat("Ball_LightGlow", new Color(1f, 0.98f, 0.9f), 0f, 0.3f, new Color(3f, 2.9f, 2.6f));
			_board = Mat("Ball_Board", new Color(0.05f, 0.08f, 0.06f), 0f, 0.3f);
			_boardLit = Mat("Ball_BoardLit", new Color(1f, 0.7f, 0.2f), 0f, 0.3f, new Color(2.2f, 1.3f, 0.25f));
			_bench = Mat("Ball_BenchWood", new Color(0.55f, 0.38f, 0.22f), 0f, 0.2f);
			_rubber = _chalk;
		}
	}
}
