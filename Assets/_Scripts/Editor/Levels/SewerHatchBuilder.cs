using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using static Crowds.EditorTools.KeyLevelBuilder;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Sewer hatches for the Infinite Scroll suburb (Level_01) — Tools ▸ Crowds ▸ Build Sewer Hatches (Infinite Scroll).
	/// 1. Rebuilds _Prefabs/Props/SewerHatch.prefab (KeyLevelBuilder.BuildHatchPrefab: lid + stench + SewerHatch zone —
	///    light poison for the player standing on it). The Key level keeps its instances (same prefab guid).
	/// 2. Level_01: removes every ToxicPoint instance and gives each FloorSector a "SewerHatches" child (re-created on
	///    every run, rides along when the sector recycles) with hatches on the road lanes and here and there on the
	///    sidewalks — never on the grass. Every hatch gets a ToxicPoint spawn on its centre, so all the toxic zombies
	///    (Toxic / FastToxic / ToxicHelmet) crawl out of the sewers; the regular spawn points are untouched.
	/// Run after Build Suburb and Build Street Vehicles (it warns when a hatch is too close to a vehicle or to a
	/// spawn/loot point). Safe to re-run. See Docs/SewerHatchFeature.md.
	/// </summary>
	public static class SewerHatchBuilder
	{
		private const string Level01Path = "Assets/_Prefabs/Levels/Level_01.prefab";
		private const string ToxicPointPath = "Assets/_Prefabs/SpawnPoints/ToxicPoint.prefab";
		private const string ContainerName = "SewerHatches";
		private const string Log = "[SewerHatchBuilder]";

		// Suburb cross-section (sector local, see SuburbBuilder / Docs/SuburbFeature.md).
		private const float Top = -0.5f;
		private const float RoadH = 0.006f, WalkH = 0.03f;
		private const float Road0 = -11f, Road1 = 3f, CentreLineX = -4f;
		private const float WalkW0 = -14f, WalkW1 = -12.5f, WalkE0 = 4.5f, WalkE1 = 6f;
		private const float WalkScale = 0.9f;   // a 1.45 m rim × 0.9 fits the 1.5 m sidewalk with a small margin
		private const float VehicleClearance = 1f;
		private const float PointClearance = 2f;

		private enum Surface { Road, WalkWest, WalkEast }

		/// <summary>Sector index (sorted by Z: 0 = z −50, 1 = z 0 / player start, 2 = z +50), surface, local Z (X for roads), yaw.</summary>
		private readonly struct Placement
		{
			public readonly int Sector;
			public readonly Surface Surface;
			public readonly float X, Z, Yaw;

			public Placement(int sector, Surface surface, float x, float z, float yaw)
			{
				Sector = sector; Surface = surface; X = x; Z = z; Yaw = yaw;
			}
		}

		// 6 hatches (4 on the road lanes, 2 on the sidewalks), 2 per sector. The former 4 ToxicPoints became 6 — their
		// cooldowns in ToxicPoint.prefab were scaled ×1.5, so the toxic spawn rate stays the same. Spots keep clear of
		// the vehicles, the regular spawns, the breakable spawns and the pre-placed loot of the start sector.
		private static readonly Placement[] Placements =
		{
			// Sector 0 (z −50, Row_A): east lane, sidewalk by the houses.
			new(0, Surface.Road, -1.5f, -14f, 20f),
			new(0, Surface.WalkWest, 0f, 9f, 0f),
			// Sector 1 (z 0, player start): both lanes, ≥ 10 m from the start.
			new(1, Surface.Road, -7.5f, -8f, 65f),
			new(1, Surface.Road, -0.8f, 19f, 140f),
			// Sector 2 (z +50, Row_C): west lane, far sidewalk.
			new(2, Surface.Road, -7.6f, 15f, 35f),
			new(2, Surface.WalkEast, 0f, -19f, 0f),
		};

		[MenuItem("Tools/Crowds/Build Sewer Hatches (Infinite Scroll)")]
		public static void Build()
		{
			EnsureFolder(MaterialFolder);
			EnsureFolder(PropFolder);
			CreateMaterials();
			var hatch = BuildHatchPrefab();
			var toxicPoint = AssetDatabase.LoadAssetAtPath<GameObject>(ToxicPointPath);
			if (hatch == null || toxicPoint == null)
			{
				Debug.LogError($"{Log} Missing {(hatch == null ? "SewerHatch prefab" : ToxicPointPath)}.");
				return;
			}

			ApplyToLevel(hatch, toxicPoint);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}

		private static void ApplyToLevel(GameObject hatch, GameObject toxicPoint)
		{
			var root = PrefabUtility.LoadPrefabContents(Level01Path);
			try
			{
				var sectors = new List<FloorSector>(root.GetComponentsInChildren<FloorSector>(true));
				sectors.Sort((a, b) => a.transform.position.z.CompareTo(b.transform.position.z));
				if (sectors.Count == 0)
				{
					Debug.LogError($"{Log} No FloorSector in {Level01Path}.");
					return;
				}

				int removed = RemoveToxicPoints(root.transform, toxicPoint);

				var parents = new Transform[sectors.Count];
				for (int i = 0; i < sectors.Count; i++)
				{
					var sector = sectors[i].transform;
					var old = sector.Find(ContainerName);
					if (old != null)
						Object.DestroyImmediate(old.gameObject);
					parents[i] = new GameObject(ContainerName).transform;
					parents[i].SetParent(sector, false);
				}

				var points = CollectKeepClearPoints(root.transform);
				int placed = 0;
				foreach (var p in Placements)
				{
					if (p.Sector >= sectors.Count)
					{
						Debug.LogWarning($"{Log} No sector {p.Sector} — hatch skipped.");
						continue;
					}

					var (local, scale) = Resolve(p);
					int index = ++placed;
					var h = (GameObject)PrefabUtility.InstantiatePrefab(hatch, parents[p.Sector]);
					h.name = $"SewerHatch_{index}";
					h.transform.SetLocalPositionAndRotation(local, Quaternion.Euler(0, p.Yaw, 0));
					h.transform.localScale = Vector3.one * scale;

					var spawn = (GameObject)PrefabUtility.InstantiatePrefab(toxicPoint, parents[p.Sector]);
					spawn.name = $"ToxicPoint_Hatch{index}";
					spawn.transform.SetLocalPositionAndRotation(new Vector3(local.x, Top, local.z), Quaternion.identity);

					CheckClearance(sectors[p.Sector].transform, h.transform, 0.75f * scale, points);
				}

				PrefabUtility.SaveAsPrefabAsset(root, Level01Path, out bool ok);
				if (!ok)
					Debug.LogError($"{Log} Failed to save {Level01Path}");
				else
					Debug.Log($"{Log} {placed} hatches (+ ToxicPoint each) over {sectors.Count} sectors; " +
					          $"{removed} old ToxicPoint(s) removed.");
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		// Road: given X (must be in a lane, off the centre line). Sidewalks: X = the sidewalk's middle, slightly smaller lid.
		private static (Vector3 local, float scale) Resolve(Placement p)
		{
			switch (p.Surface)
			{
				case Surface.WalkWest:
					return (new Vector3((WalkW0 + WalkW1) * 0.5f, Top + WalkH, p.Z), WalkScale);
				case Surface.WalkEast:
					return (new Vector3((WalkE0 + WalkE1) * 0.5f, Top + WalkH, p.Z), WalkScale);
				default:
					if (p.X - 0.75f < Road0 + 0.3f || p.X + 0.75f > Road1 - 0.3f || Mathf.Abs(p.X - CentreLineX) < 1f)
						Debug.LogWarning($"{Log} Road hatch at x {p.X} touches the kerb or the centre line.");
					return (new Vector3(p.X, Top + RoadH, p.Z), 1f);
			}
		}

		// Old free-standing toxic spawns: all toxic zombies now come out of the hatches.
		private static int RemoveToxicPoints(Transform root, GameObject toxicPoint)
		{
			var doomed = new List<GameObject>();
			foreach (var sp in root.GetComponentsInChildren<SpawnPoint>(true))
			{
				var go = sp.gameObject;
				if (!PrefabUtility.IsOutermostPrefabInstanceRoot(go))
					continue;
				if (PrefabUtility.GetCorrespondingObjectFromSource(go) == toxicPoint)
					doomed.Add(go);
			}
			foreach (var go in doomed)
				Object.DestroyImmediate(go);
			return doomed.Count;
		}

		// Regular spawns, loot points, breakable spawns and the pre-placed loot — hatches must not sit on them.
		private static List<Transform> CollectKeepClearPoints(Transform root)
		{
			var list = new List<Transform>();
			foreach (var t in root.GetComponentsInChildren<Transform>(true))
			{
				string n = t.name;
				bool point = (n.Contains("SpawnPoint") && t.GetComponent<SpawnPoint>() != null) || n.StartsWith("LootPoint")
					|| n.StartsWith("BreakableSpawn") || n.StartsWith("GroupSpawnPoint")
					|| (t.parent != null && t.parent.name == "_PreplacedLoot");
				if (point)
					list.Add(t);
			}
			return list;
		}

		private static void CheckClearance(Transform sector, Transform hatch, float radius, List<Transform> points)
		{
			Vector3 c = hatch.position;
			foreach (var p in points)
			{
				var d = new Vector2(p.position.x - c.x, p.position.z - c.z).magnitude - radius;
				if (d < PointClearance)
					Debug.LogWarning($"{Log} {sector.name}/{hatch.name} is {d:0.0} m from '{p.name}' — move one of them.");
			}
			foreach (var obstacle in sector.GetComponentsInChildren<NavMeshObstacle>(true))
			{
				var local = obstacle.transform.InverseTransformPoint(c);
				var half = obstacle.size * 0.5f;
				float dx = Mathf.Max(0f, Mathf.Abs(local.x - obstacle.center.x) - half.x);
				float dz = Mathf.Max(0f, Mathf.Abs(local.z - obstacle.center.z) - half.z);
				float d = Mathf.Sqrt(dx * dx + dz * dz) - radius;
				if (d < VehicleClearance)
					Debug.LogWarning($"{Log} {sector.name}/{hatch.name} is {d:0.0} m from '{obstacle.name}' — move the hatch.");
			}
		}
	}
}
