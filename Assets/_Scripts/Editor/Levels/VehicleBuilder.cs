using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using static Crowds.EditorTools.KeyLevelBuilder;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Street vehicles for the Infinite Scroll suburb (Level_01) — Tools ▸ Crowds ▸ Build Street Vehicles (Infinite Scroll).
	/// Primitive-style models after Assets/REFs (pickup1/2, sedan1/2, icecream_truck1-3), each baked into one mesh
	/// (one submesh per material), colour variants = same mesh with swapped paint materials:
	///   Pickup_Teal / Pickup_Red              — regular-cab fleetside pickup, two-tone (paint + lower band);
	///   Sedan_Red / Sedan_Silver / Sedan_Navy — compact 4-door sedan with a trunk spoiler;
	///   Truck_IceCream                        — step van, white→blue bands, serving window with a striped awning,
	///                                           menu tiles, a giant cone on the roof;
	///   Truck_Mail                            — the same step van in white with a blue + red side stripe (no logos).
	/// Vehicles are solid scenery: a carving NavMeshObstacle (neither the player nor mobs pass — both walk the navmesh),
	/// a BulletBlocker (bullets stop with sparks) and no Breakable (can't be destroyed). The visual is ignored by the
	/// navmesh bake. Level_01: each FloorSector gets a "Vehicles" child (rides along when the sector recycles); the
	/// untextured ProBuilder walls (Wall1, Wall1 (1)) are removed. Safe to re-run. See Docs/VehiclesFeature.md.
	/// Model local frame: front faces +Z, ground at y = 0, width along X.
	/// </summary>
	public static class VehicleBuilder
	{
		private const string PrefabFolder = "Assets/_Prefabs/Vehicles";
		private const string MeshFolder = PrefabFolder + "/Meshes";
		private const string MatFolder = "Assets/_Materials/Vehicles";
		private const string Level01Path = "Assets/_Prefabs/Levels/Level_01.prefab";
		private const string Log = "[VehicleBuilder]";

		// Suburb cross-section (sector local, see SuburbBuilder / Docs/SuburbFeature.md).
		private const float Top = -0.5f;            // slab top
		private const float RoadH = 0.006f;         // asphalt above the slab
		private const float DriveH = 0.032f;        // driveway above the slab
		private const float CurbParkX = -9.95f;     // kerbside parking lane on the houses' side (road starts at x = -11)
		private const float DrivewayParkX = -23.4f; // on the ranch driveway, nose toward the garage
		private const float RanchGarageX = -6.9f;   // House_Ranch local X of the garage door centre (SuburbBuilder.BuildRanch)
		private const float ClearanceWarn = 2f;     // warn when a spawn/loot point is this close to a vehicle footprint

		private static Material _tealPaint, _redPaint, _sedanRed, _sedanSilver, _sedanNavy, _lowerSilver, _lowerCharcoal;
		private static Material _glass, _tire, _rim, _rimWhite, _chrome, _black, _head, _tail, _amber, _plate, _bed, _grille;
		private static Material _white, _iceBlue, _paleBlue, _mailBlue, _mailRed, _awningPink, _waffle, _strawberry,
			_vanilla, _mint, _lemon, _choco;

		private static readonly List<Mesh> TempMeshes = new();

		/// <summary>Where a vehicle goes: sector index (sorted by Z), prefab, spot, sector-local X/Z, yaw.</summary>
		private readonly struct Placement
		{
			public readonly int Sector;
			public readonly string Prefab;
			public readonly bool Driveway; // true → X/Z taken from the ranch garage of this sector
			public readonly float X, Z, Yaw;

			public Placement(int sector, string prefab, float x, float z, float yaw, bool driveway = false)
			{
				Sector = sector; Prefab = prefab; X = x; Z = z; Yaw = yaw; Driveway = driveway;
			}
		}

		// 3 vans on the road, 4 cars parked by the houses (2 on ranch driveways, 2 at the kerb). Points chosen clear of
		// the sectors' spawn/loot/breakable points (the builder re-checks and warns).
		private static readonly Placement[] Placements =
		{
			// Sector 0 (z −50, Row_A).
			new(0, "Truck_Mail", -7.3f, 12f, 184f),
			new(0, "Pickup_Teal", 0, 0, -90f, driveway: true),
			// Sector 1 (z 0, Row_B, player start) — the ice-cream van stands where the bare wall was.
			new(1, "Truck_IceCream", -0.6f, -17f, 4f),
			new(1, "Sedan_Red", CurbParkX, -20.5f, 180f),
			// Sector 2 (z +50, Row_C).
			new(2, "Truck_Mail", -0.7f, -12f, -5f),
			new(2, "Sedan_Silver", 0, 0, -90f, driveway: true),
			new(2, "Pickup_Red", CurbParkX, -2f, 180f),
		};

		private class VehicleInfo
		{
			public GameObject Prefab;
			public Bounds Footprint; // local, body only
		}

		[MenuItem("Tools/Crowds/Build Street Vehicles (Infinite Scroll)")]
		public static void Build()
		{
			EnsureFolder(PrefabFolder);
			EnsureFolder(MeshFolder);
			EnsureFolder(MatFolder);
			CreateMaterials();

			var vehicles = new Dictionary<string, VehicleInfo>();
			try
			{
				BuildPickups(vehicles);
				BuildSedans(vehicles);
				BuildVan(vehicles, "Truck_IceCream", iceCream: true);
				BuildVan(vehicles, "Truck_Mail", iceCream: false);
			}
			finally
			{
				foreach (var m in TempMeshes)
					if (m != null) Object.DestroyImmediate(m);
				TempMeshes.Clear();
			}

			ApplyToLevel(vehicles);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"{Log} Vehicles built and placed in {Level01Path}.");
		}

		// ================================================================== level

		private static void ApplyToLevel(Dictionary<string, VehicleInfo> vehicles)
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

				RemoveBareWalls(root.transform);

				var parents = new Transform[sectors.Count];
				for (int i = 0; i < sectors.Count; i++)
				{
					var sector = sectors[i].transform;
					var old = sector.Find("Vehicles");
					if (old != null)
						Object.DestroyImmediate(old.gameObject);
					parents[i] = new GameObject("Vehicles").transform;
					parents[i].SetParent(sector, false);
				}

				var points = CollectKeepClearPoints(root.transform);
				int placed = 0;
				foreach (var p in Placements)
				{
					if (p.Sector >= sectors.Count || !vehicles.TryGetValue(p.Prefab, out var info))
					{
						Debug.LogWarning($"{Log} Skipped {p.Prefab} (sector {p.Sector}).");
						continue;
					}
					var sector = sectors[p.Sector].transform;
					Vector3 local;
					if (p.Driveway)
					{
						if (!TryFindRanchDriveway(sector, out float garageZ))
						{
							Debug.LogWarning($"{Log} No House_Ranch in {sector.name} — {p.Prefab} not placed (run Build Suburb first).");
							continue;
						}
						local = new Vector3(DrivewayParkX, Top + DriveH, garageZ);
					}
					else
					{
						local = new Vector3(p.X, Top + RoadH, p.Z);
					}

					var go = (GameObject)PrefabUtility.InstantiatePrefab(info.Prefab, parents[p.Sector]);
					go.name = p.Prefab;
					go.transform.SetLocalPositionAndRotation(local, Quaternion.Euler(0, p.Yaw, 0));
					CheckClearance(go.transform, info.Footprint, points);
					placed++;
				}

				PrefabUtility.SaveAsPrefabAsset(root, Level01Path, out bool ok);
				if (!ok)
					Debug.LogError($"{Log} Failed to save {Level01Path}");
				else
					Debug.Log($"{Log} {placed} vehicles placed over {sectors.Count} sectors.");
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		// The untextured ProBuilder placeholder walls of the original level.
		private static void RemoveBareWalls(Transform root)
		{
			var doomed = new List<GameObject>();
			foreach (var t in root.GetComponentsInChildren<Transform>(true))
				if ((t.name == "Wall1" || t.name.StartsWith("Wall1 (")) && t.GetComponent<MeshCollider>() != null && t.childCount == 0)
					doomed.Add(t.gameObject);
			foreach (var go in doomed)
			{
				Debug.Log($"{Log} Removed placeholder wall '{go.name}'.");
				Object.DestroyImmediate(go);
			}
		}

		private static bool TryFindRanchDriveway(Transform sector, out float garageZ)
		{
			foreach (var t in sector.GetComponentsInChildren<Transform>(true))
			{
				// Far (house) side only: DrivewayParkX is on that side; the camera-side row lives under "Houses_East".
				if (!t.name.StartsWith("House_Ranch") || t.parent == null || t.parent.name != "Houses")
					continue;
				garageZ = sector.InverseTransformPoint(t.TransformPoint(new Vector3(RanchGarageX, 0, 0))).z;
				return true;
			}
			garageZ = 0;
			return false;
		}

		// Spawn points, loot points, breakable spawns and the pre-placed loot — vehicles must not sit on them.
		private static List<Transform> CollectKeepClearPoints(Transform root)
		{
			var list = new List<Transform>();
			foreach (var t in root.GetComponentsInChildren<Transform>(true))
			{
				string n = t.name;
				bool point = n.Contains("SpawnPoint") || n.StartsWith("ToxicPoint") || n.StartsWith("LootPoint")
					|| n.StartsWith("BreakableSpawn") || (t.parent != null && t.parent.name == "_PreplacedLoot");
				if (point)
					list.Add(t);
			}
			return list;
		}

		private static void CheckClearance(Transform vehicle, Bounds footprint, List<Transform> points)
		{
			foreach (var p in points)
			{
				var local = vehicle.InverseTransformPoint(p.position);
				float dx = Mathf.Max(0f, Mathf.Abs(local.x - footprint.center.x) - footprint.extents.x);
				float dz = Mathf.Max(0f, Mathf.Abs(local.z - footprint.center.z) - footprint.extents.z);
				float d = Mathf.Sqrt(dx * dx + dz * dz);
				if (d < ClearanceWarn)
					Debug.LogWarning($"{Log} {vehicle.parent.parent.name}/{vehicle.name} is {d:0.0} m from '{p.name}' — move one of them.");
			}
		}

		// ================================================================== pickup

		private static void BuildPickups(Dictionary<string, VehicleInfo> vehicles)
		{
			var t = Temp("Pickup");
			try
			{
				// Short hood (ref pickup1): the nose ends 1.5 m ahead of the windshield base, front axle moved back with it.
				const float r = 0.38f, wx = 0.86f, wz = 1.62f, wzFront = 1.36f;
				Wheels(t, r, 0.27f, wx, wzFront, -wz, _rim, 0.22f);

				// Two-tone: lower band + paint above (ref pickup1).
				Box(t, _lowerSilver, new Vector3(0, 0.56f, -0.225f), new Vector3(1.92f, 0.28f, 4.75f));
				Box(t, _tealPaint, new Vector3(0, 0.92f, 1.325f), new Vector3(1.9f, 0.44f, 1.55f)); // hood + fenders
				Box(t, _tealPaint, new Vector3(0, 0.92f, -0.1f), new Vector3(1.9f, 0.44f, 1.3f));   // cab lower
				Box(t, _black, new Vector3(0, 0.705f, -0.225f), new Vector3(1.93f, 0.025f, 4.76f)); // two-tone seam
				// Cab: raked windshield, flat roof, upright back.
				Extrude(t, _tealPaint, new[] { V(-0.75f, 1.14f), V(0.6f, 1.14f), V(0.02f, 1.86f), V(-0.72f, 1.86f) }, -0.88f, 0.88f);
				Pane(t, _glass, new Vector3(0, 1.16f, 0.58f), new Vector3(0, 1.84f, 0.04f), 1.62f, Vector3.forward);
				Box(t, _glass, new Vector3(0, 1.5f, -0.765f), new Vector3(1.45f, 0.48f, 0.03f));
				foreach (float s in new[] { -1f, 1f })
				{
					Extrude(t, _glass, new[] { V(-0.62f, 1.22f), V(0.44f, 1.22f), V(0.0f, 1.78f), V(-0.62f, 1.78f) },
						s * 0.875f, s * 0.893f);
					// Door seams + handle, mirror.
					Box(t, _black, new Vector3(s * 0.955f, 0.92f, -0.7f), new Vector3(0.01f, 0.42f, 0.025f));
					Box(t, _black, new Vector3(s * 0.955f, 0.92f, 0.5f), new Vector3(0.01f, 0.42f, 0.025f));
					Box(t, _black, new Vector3(s * 0.958f, 1.04f, -0.5f), new Vector3(0.015f, 0.04f, 0.16f));
					Box(t, _chrome, new Vector3(s * 1.03f, 1.32f, 0.42f), new Vector3(0.12f, 0.2f, 0.09f));
					Box(t, _black, new Vector3(s * 0.96f, 1.27f, 0.42f), new Vector3(0.1f, 0.03f, 0.03f));
				}

				// Bed: liner floor, side walls, tailgate, taillights, bumper.
				Box(t, _bed, new Vector3(0, 0.715f, -1.66f), new Vector3(1.74f, 0.03f, 1.74f));
				foreach (float s in new[] { -1f, 1f })
					Box(t, _tealPaint, new Vector3(s * 0.91f, 0.92f, -1.665f), new Vector3(0.08f, 0.44f, 1.77f));
				Box(t, _tealPaint, new Vector3(0, 0.92f, -2.59f), new Vector3(1.9f, 0.44f, 0.08f));
				Box(t, _black, new Vector3(0, 1.02f, -2.635f), new Vector3(0.24f, 0.05f, 0.01f)); // tailgate handle
				foreach (float s in new[] { -1f, 1f })
					Box(t, _tail, new Vector3(s * 0.87f, 0.96f, -2.64f), new Vector3(0.14f, 0.3f, 0.03f));
				Box(t, _chrome, new Vector3(0, 0.5f, -2.68f), new Vector3(1.96f, 0.2f, 0.16f));
				Box(t, _plate, new Vector3(0, 0.5f, -2.765f), new Vector3(0.32f, 0.16f, 0.01f));

				// Front: chrome-framed black grille, stacked headlights, amber corners, chrome bumper.
				Box(t, _chrome, new Vector3(0, 0.9f, 2.105f), new Vector3(1.12f, 0.4f, 0.03f));
				Box(t, _black, new Vector3(0, 0.9f, 2.12f), new Vector3(1.0f, 0.3f, 0.02f));
				foreach (float s in new[] { -1f, 1f })
				{
					Box(t, _head, new Vector3(s * 0.7f, 0.97f, 2.115f), new Vector3(0.3f, 0.1f, 0.03f));
					Box(t, _head, new Vector3(s * 0.7f, 0.84f, 2.115f), new Vector3(0.3f, 0.1f, 0.03f));
					Box(t, _amber, new Vector3(s * 0.9f, 0.905f, 2.11f), new Vector3(0.09f, 0.24f, 0.03f));
				}
				Box(t, _chrome, new Vector3(0, 0.52f, 2.17f), new Vector3(1.96f, 0.22f, 0.18f));
				Box(t, _black, new Vector3(0, 0.38f, 2.15f), new Vector3(1.7f, 0.08f, 0.1f)); // air dam

				var (mesh, mats) = BakeMesh(t, "Pickup");
				var footprint = new Bounds(new Vector3(0, 0.93f, -0.3f), new Vector3(2f, 1.86f, 4.95f));
				vehicles["Pickup_Teal"] = SaveVehicle("Pickup_Teal", mesh, mats, footprint);
				vehicles["Pickup_Red"] = SaveVehicle("Pickup_Red", mesh,
					Swap(mats, (_tealPaint, _redPaint), (_lowerSilver, _lowerCharcoal)), footprint);
			}
			finally
			{
				Object.DestroyImmediate(t);
			}
		}

		// ================================================================== sedan

		private static void BuildSedans(Dictionary<string, VehicleInfo> vehicles)
		{
			var t = Temp("Sedan");
			try
			{
				const float r = 0.31f;
				Wheels(t, r, 0.2f, 0.78f, 1.33f, -1.3f, _rim, 0.19f);

				var paint = _sedanRed;
				Box(t, paint, new Vector3(0, 0.55f, 0), new Vector3(1.74f, 0.5f, 4.3f));
				// Rounded nose and tail.
				Extrude(t, paint, new[] { V(2.15f, 0.3f), V(2.3f, 0.36f), V(2.3f, 0.64f), V(2.15f, 0.8f) }, -0.84f, 0.84f);
				Extrude(t, paint, new[] { V(-2.15f, 0.32f), V(-2.28f, 0.38f), V(-2.28f, 0.78f), V(-2.15f, 0.82f) }, -0.84f, 0.84f);
				// Greenhouse: raked windshield and rear window, narrower than the body.
				Extrude(t, paint, new[] { V(-1.45f, 0.8f), V(1.2f, 0.8f), V(0.45f, 1.4f), V(-0.85f, 1.4f) }, -0.76f, 0.76f);
				Pane(t, _glass, new Vector3(0, 0.83f, 1.16f), new Vector3(0, 1.38f, 0.48f), 1.36f, Vector3.forward);
				Pane(t, _glass, new Vector3(0, 0.83f, -1.41f), new Vector3(0, 1.38f, -0.86f), 1.3f, Vector3.back);
				foreach (float s in new[] { -1f, 1f })
				{
					Extrude(t, _glass, new[] { V(-1.25f, 0.86f), V(1.0f, 0.86f), V(0.42f, 1.33f), V(-0.8f, 1.33f) },
						s * 0.758f, s * 0.775f);
					Box(t, paint, new Vector3(s * 0.778f, 1.1f, -0.15f), new Vector3(0.012f, 0.5f, 0.08f)); // B-pillar
					Box(t, _black, new Vector3(s * 0.873f, 0.62f, 0), new Vector3(0.012f, 0.045f, 3.0f));   // side moulding
					Box(t, _black, new Vector3(s * 0.873f, 0.55f, -0.15f), new Vector3(0.012f, 0.42f, 0.02f)); // door seam
					Box(t, _black, new Vector3(s * 0.876f, 0.73f, 0.45f), new Vector3(0.012f, 0.035f, 0.13f));
					Box(t, _black, new Vector3(s * 0.876f, 0.73f, -0.75f), new Vector3(0.012f, 0.035f, 0.13f));
					Box(t, paint, new Vector3(s * 0.93f, 0.98f, 0.98f), new Vector3(0.14f, 0.1f, 0.07f));     // mirror
				}
				// Trunk spoiler (ref sedan2).
				Box(t, paint, new Vector3(0, 0.98f, -2.05f), new Vector3(1.5f, 0.04f, 0.25f));
				foreach (float s in new[] { -1f, 1f })
					Box(t, paint, new Vector3(s * 0.6f, 0.89f, -2.05f), new Vector3(0.06f, 0.16f, 0.08f));
				// Front: headlights, small grille, lower intake.
				foreach (float s in new[] { -1f, 1f })
				{
					Box(t, _head, new Vector3(s * 0.56f, 0.68f, 2.29f), new Vector3(0.44f, 0.14f, 0.04f));
					Box(t, _amber, new Vector3(s * 0.8f, 0.68f, 2.27f), new Vector3(0.06f, 0.12f, 0.04f));
					Box(t, _tail, new Vector3(s * 0.6f, 0.7f, -2.285f), new Vector3(0.42f, 0.16f, 0.03f));
					Box(t, _amber, new Vector3(s * 0.47f, 0.7f, -2.3f), new Vector3(0.12f, 0.1f, 0.02f));
				}
				Box(t, _black, new Vector3(0, 0.66f, 2.305f), new Vector3(0.5f, 0.12f, 0.03f));
				Box(t, _chrome, new Vector3(0, 0.66f, 2.322f), new Vector3(0.08f, 0.08f, 0.01f));
				Box(t, _black, new Vector3(0, 0.42f, 2.305f), new Vector3(0.9f, 0.1f, 0.03f));
				Box(t, _plate, new Vector3(0, 0.52f, -2.29f), new Vector3(0.32f, 0.16f, 0.01f));

				var (mesh, mats) = BakeMesh(t, "Sedan");
				var footprint = new Bounds(new Vector3(0, 0.7f, 0), new Vector3(1.78f, 1.4f, 4.62f));
				vehicles["Sedan_Red"] = SaveVehicle("Sedan_Red", mesh, mats, footprint);
				vehicles["Sedan_Silver"] = SaveVehicle("Sedan_Silver", mesh, Swap(mats, (_sedanRed, _sedanSilver)), footprint);
				vehicles["Sedan_Navy"] = SaveVehicle("Sedan_Navy", mesh, Swap(mats, (_sedanRed, _sedanNavy)), footprint);
			}
			finally
			{
				Object.DestroyImmediate(t);
			}
		}

		// ================================================================== step van (ice cream / mail)

		private static void BuildVan(Dictionary<string, VehicleInfo> vehicles, string name, bool iceCream)
		{
			var t = Temp(name);
			try
			{
				const float half = 1.1f, z0 = -2.6f, z1 = 1.95f, floor = 0.6f, roof = 2.75f;
				Wheels(t, 0.42f, 0.3f, 0.98f, 1.75f, -1.45f, _rimWhite, 0.25f);
				Box(t, _black, new Vector3(0, 0.5f, -0.3f), new Vector3(1.8f, 0.2f, 4.6f)); // chassis

				// Body bands: ice cream = white top fading to blue (ref truck1/2), mail = all white.
				var low = iceCream ? _iceBlue : _white;
				var mid = iceCream ? _paleBlue : _white;
				Band(t, low, floor, 1.45f);
				Band(t, mid, 1.45f, 1.95f);
				Band(t, _white, 1.95f, roof);
				void Band(GameObject g, Material m, float y0, float y1) =>
					Box(g, m, new Vector3(0, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(half * 2f, y1 - y0, z1 - z0));
				Box(t, _white, new Vector3(0, roof + 0.04f, (z0 + z1) * 0.5f), new Vector3(half * 2f - 0.08f, 0.08f, z1 - z0 - 0.08f));
				foreach (float s in new[] { -1f, 1f })
				{
					Box(t, _amber, new Vector3(s * 0.95f, roof - 0.05f, z1 + 0.01f), new Vector3(0.1f, 0.06f, 0.03f));
					Box(t, _tail, new Vector3(s * 0.9f, roof - 0.05f, z0 - 0.01f), new Vector3(0.1f, 0.06f, 0.03f));
				}

				// Nose (hood over the engine) + flat two-pane windshield.
				Extrude(t, low, new[] { V(1.9f, floor), V(2.6f, floor), V(2.6f, 1.05f), V(2.25f, 1.42f), V(1.9f, 1.45f) }, -1.05f, 1.05f);
				foreach (float s in new[] { -1f, 1f })
					Box(t, _glass, new Vector3(s * 0.48f, 2.15f, z1 + 0.01f), new Vector3(0.82f, 0.72f, 0.03f));
				Box(t, _white, new Vector3(0, 2.15f, z1 + 0.012f), new Vector3(0.1f, 0.74f, 0.03f));
				foreach (float s in new[] { -1f, 1f })
				{
					Cyl(t, _head, new Vector3(s * 0.72f, 0.95f, 2.615f), 0.1f, 0.04f, Vector3.forward);
					Box(t, _chrome, new Vector3(s * 0.72f, 0.95f, 2.6f), new Vector3(0.26f, 0.26f, 0.02f));
					Box(t, _amber, new Vector3(s * 1.055f, 1.0f, 2.4f), new Vector3(0.02f, 0.06f, 0.1f));
				}
				Box(t, _grille, new Vector3(0, 0.92f, 2.605f), new Vector3(0.8f, 0.42f, 0.02f));
				for (int i = 0; i < 4; i++)
					Box(t, _black, new Vector3(0, 0.77f + i * 0.1f, 2.618f), new Vector3(0.78f, 0.02f, 0.01f));
				var bumper = iceCream ? _chrome : _black;
				Box(t, bumper, new Vector3(0, 0.62f, 2.68f), new Vector3(2.2f, 0.2f, 0.16f));

				// Cab sides: front side window, door with window + handle, mirror on an arm.
				foreach (float s in new[] { -1f, 1f })
				{
					float x = s * (half + 0.006f);
					Box(t, _glass, new Vector3(x, 2.12f, 1.72f), new Vector3(0.012f, 0.72f, 0.36f));
					Box(t, _glass, new Vector3(x, 2.05f, 1.0f), new Vector3(0.012f, 0.6f, 0.62f));
					foreach (float dz in new[] { 0.6f, 1.45f })
						Box(t, _black, new Vector3(x, 1.6f, dz), new Vector3(0.01f, 1.8f, 0.03f));
					Box(t, _black, new Vector3(x, 2.5f, 1.025f), new Vector3(0.01f, 0.03f, 0.88f));
					Box(t, _chrome, new Vector3(s * (half + 0.02f), 1.55f, 0.72f), new Vector3(0.03f, 0.05f, 0.14f));
					Box(t, _black, new Vector3(s * (half + 0.15f), 2.0f, 1.9f), new Vector3(0.3f, 0.03f, 0.03f));
					Box(t, _chrome, new Vector3(s * (half + 0.29f), 1.95f, 1.9f), new Vector3(0.06f, 0.34f, 0.18f));
				}

				// Rear: split doors with small windows, taillights, step bumper, plate.
				Box(t, _black, new Vector3(0, 1.6f, z0 - 0.006f), new Vector3(0.02f, 1.9f, 0.01f));
				foreach (float s in new[] { -1f, 1f })
				{
					Box(t, _glass, new Vector3(s * 0.45f, 2.15f, z0 - 0.01f), new Vector3(0.45f, 0.45f, 0.02f));
					Box(t, _tail, new Vector3(s * 0.95f, 1.0f, z0 - 0.01f), new Vector3(0.12f, 0.2f, 0.02f));
				}
				Box(t, bumper, new Vector3(0, 0.55f, z0 - 0.1f), new Vector3(1.6f, 0.12f, 0.25f));
				Box(t, _plate, new Vector3(0, 0.78f, z0 - 0.008f), new Vector3(0.32f, 0.16f, 0.01f));

				if (iceCream)
					IceCreamDressing(t, half);
				else
					MailDressing(t, half, z0, z1);

				var (mesh, mats) = BakeMesh(t, name);
				vehicles[name] = SaveVehicle(name, mesh, mats,
					new Bounds(new Vector3(0, 1.4f, (z0 - 0.2f + 2.7f) * 0.5f), new Vector3(half * 2f + 0.04f, 2.8f, 2.7f - z0 + 0.2f)));
			}
			finally
			{
				Object.DestroyImmediate(t);
			}
		}

		// Serving window on the right (kerb) side with a pink/white awning, menu tiles, a giant cone on the roof.
		private static void IceCreamDressing(GameObject t, float half)
		{
			const float wz = -1.0f, wLen = 1.5f, wy0 = 1.5f, wy1 = 2.35f;
			float x = half + 0.006f;
			Box(t, _glass, new Vector3(x, (wy0 + wy1) * 0.5f, wz), new Vector3(0.012f, wy1 - wy0, wLen));
			// Insect screen (ref truck3).
			for (int i = 1; i < 6; i++)
				Box(t, _grille, new Vector3(x + 0.004f, (wy0 + wy1) * 0.5f, wz - wLen * 0.5f + wLen * i / 6f), new Vector3(0.008f, wy1 - wy0, 0.015f));
			for (int i = 1; i < 4; i++)
				Box(t, _grille, new Vector3(x + 0.004f, wy0 + (wy1 - wy0) * i / 4f, wz), new Vector3(0.008f, 0.015f, wLen));
			Box(t, _chrome, new Vector3(half + 0.01f, wy1 + 0.03f, wz), new Vector3(0.02f, 0.06f, wLen + 0.1f));
			Box(t, _chrome, new Vector3(half + 0.1f, wy0 - 0.03f, wz), new Vector3(0.2f, 0.05f, wLen + 0.1f)); // counter
			// Awning: hinged above the window, tilted 20° down, pink/white stripes across.
			const int stripes = 5;
			float len = 0.72f, tilt = 20f * Mathf.Deg2Rad;
			var centre = new Vector3(half + len * 0.5f * Mathf.Cos(tilt), wy1 + 0.1f - len * 0.5f * Mathf.Sin(tilt), 0);
			for (int i = 0; i < stripes; i++)
			{
				float sz = (wLen + 0.2f) / stripes;
				centre.z = wz - (wLen + 0.2f) * 0.5f + sz * (i + 0.5f);
				Box(t, i % 2 == 0 ? _awningPink : _white, centre, new Vector3(len, 0.035f, sz), Quaternion.Euler(0, 0, -20f));
			}
			// Menu tiles (abstract ice-cream pictures) next to the window and on the far side.
			var tiles = new[] { _strawberry, _mint, _lemon, _choco };
			foreach (float s in new[] { -1f, 1f })
			{
				float tz = s > 0 ? 0.1f : -1.0f;
				for (int i = 0; i < 4; i++)
				{
					var p = new Vector3(s * (half + 0.007f), 1.95f + (i / 2) * 0.32f, tz + (i % 2) * 0.32f - 0.16f);
					Box(t, _white, p, new Vector3(0.008f, 0.29f, 0.29f));
					Box(t, tiles[i], p + new Vector3(s * 0.004f, 0.02f, 0), new Vector3(0.008f, 0.17f, 0.17f));
				}
			}
			// Giant cone on the roof: waffle cone point-down on a stand, two scoops and a cherry.
			const float cz = -0.6f, roofTop = 2.83f;
			Box(t, _chrome, new Vector3(0, roofTop + 0.05f, cz), new Vector3(0.3f, 0.1f, 0.3f));
			Cone(t, _waffle, new Vector3(0, roofTop + 0.08f, cz), 0.32f, 0.85f);
			Sphere(t, _strawberry, new Vector3(0, roofTop + 1.02f, cz), 0.36f);
			Sphere(t, _vanilla, new Vector3(0, roofTop + 1.3f, cz), 0.26f);
			Sphere(t, _tail, new Vector3(0, roofTop + 1.58f, cz), 0.07f);
		}

		// Mail van: blue band over a thin red stripe on the sides and the back (no logos).
		private static void MailDressing(GameObject t, float half, float z0, float z1)
		{
			foreach (float s in new[] { -1f, 1f })
			{
				float x = s * (half + 0.004f);
				// Broken by the cab door seams — run the stripes behind the door only, plus a short piece on the nose side.
				Box(t, _mailBlue, new Vector3(x, 1.32f, (z0 + 0.58f) * 0.5f), new Vector3(0.008f, 0.18f, 0.58f - z0));
				Box(t, _mailRed, new Vector3(x, 1.17f, (z0 + 0.58f) * 0.5f), new Vector3(0.008f, 0.06f, 0.58f - z0));
				Box(t, _mailBlue, new Vector3(x, 1.32f, (1.47f + z1) * 0.5f), new Vector3(0.008f, 0.18f, z1 - 1.47f));
				Box(t, _mailRed, new Vector3(x, 1.17f, (1.47f + z1) * 0.5f), new Vector3(0.008f, 0.06f, z1 - 1.47f));
				// Blue square where the eagle would be — just a plain panel.
				Box(t, _mailBlue, new Vector3(s * (half + 0.005f), 2.15f, -1.2f), new Vector3(0.008f, 0.5f, 0.7f));
				Box(t, _white, new Vector3(s * (half + 0.009f), 2.15f, -1.2f), new Vector3(0.008f, 0.36f, 0.56f));
				Box(t, _mailBlue, new Vector3(s * (half + 0.013f), 2.15f, -1.2f), new Vector3(0.008f, 0.2f, 0.4f));
			}
			Box(t, _mailBlue, new Vector3(0, 1.32f, z0 - 0.004f), new Vector3(2.2f, 0.18f, 0.008f));
			Box(t, _mailRed, new Vector3(0, 1.17f, z0 - 0.004f), new Vector3(2.2f, 0.06f, 0.008f));
		}

		// ================================================================== parts

		private static void Wheels(GameObject t, float r, float width, float x, float zFront, float zRear, Material rim, float rimR)
		{
			foreach (float s in new[] { -1f, 1f })
				foreach (float z in new[] { zFront, zRear })
				{
					Cyl(t, _tire, new Vector3(s * x, r, z), r, width, Vector3.right);
					Cyl(t, rim, new Vector3(s * (x + width * 0.5f - 0.015f), r, z), rimR, 0.04f, Vector3.right);
					Cyl(t, _black, new Vector3(s * (x + width * 0.5f + 0.008f), r, z), rimR * 0.3f, 0.02f, Vector3.right);
				}
		}

		// ================================================================== primitives

		private static Vector2 V(float z, float y) => new(z, y);

		private static GameObject Temp(string name) => new(name);

		private static void Box(GameObject t, Material m, Vector3 pos, Vector3 size, Quaternion? rot = null)
		{
			var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Object.DestroyImmediate(go.GetComponent<Collider>());
			go.transform.SetParent(t.transform, false);
			go.transform.SetLocalPositionAndRotation(pos, rot ?? Quaternion.identity);
			go.transform.localScale = size;
			go.GetComponent<MeshRenderer>().sharedMaterial = m;
		}

		// Cylinder of radius r and length len along `axis` (X or Z).
		private static GameObject Cyl(GameObject t, Material m, Vector3 pos, float r, float len, Vector3 axis)
		{
			var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			Object.DestroyImmediate(go.GetComponent<Collider>());
			go.transform.SetParent(t.transform, false);
			var rot = axis == Vector3.right ? Quaternion.Euler(0, 0, 90f) : Quaternion.Euler(90f, 0, 0);
			go.transform.SetLocalPositionAndRotation(pos, rot);
			go.transform.localScale = new Vector3(r * 2f, len * 0.5f, r * 2f);
			go.GetComponent<MeshRenderer>().sharedMaterial = m;
			return go;
		}

		private static void Sphere(GameObject t, Material m, Vector3 pos, float r)
		{
			var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			Object.DestroyImmediate(go.GetComponent<Collider>());
			go.transform.SetParent(t.transform, false);
			go.transform.localPosition = pos;
			go.transform.localScale = Vector3.one * (r * 2f);
			go.GetComponent<MeshRenderer>().sharedMaterial = m;
		}

		// Thin glass pane between a (bottom) and b (top) in the YZ plane, `width` along X, facing `outward`.
		private static void Pane(GameObject t, Material m, Vector3 a, Vector3 b, float width, Vector3 outward)
		{
			var d = (b - a).normalized;
			var n = Vector3.Cross(Vector3.right, d).normalized;
			if (Vector3.Dot(n, outward) < 0f) n = -n;
			const float thick = 0.03f;
			Box(t, m, (a + b) * 0.5f + n * (thick * 0.5f + 0.004f), new Vector3(width, (b - a).magnitude * 0.92f, thick),
				Quaternion.LookRotation(n, d));
		}

		private static void AddMesh(GameObject t, Material m, Mesh mesh, Vector3 pos)
		{
			TempMeshes.Add(mesh);
			var go = new GameObject(mesh.name);
			go.transform.SetParent(t.transform, false);
			go.transform.localPosition = pos;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = m;
		}

		// Convex polygon given in (z, y), extruded along X from x0 to x1.
		private static void Extrude(GameObject t, Material m, Vector2[] profile, float x0, float x1)
		{
			var b = new MeshBuilder("Extrude");
			Vector2 c = Vector2.zero;
			foreach (var p in profile) c += p;
			c /= profile.Length;
			Vector3 P(float x, Vector2 q) => new(x, q.y, q.x);
			float lo = Mathf.Min(x0, x1), hi = Mathf.Max(x0, x1);
			for (int i = 1; i < profile.Length - 1; i++)
			{
				b.Tri(P(lo, profile[0]), P(lo, profile[i]), P(lo, profile[i + 1]), Vector3.left);
				b.Tri(P(hi, profile[0]), P(hi, profile[i]), P(hi, profile[i + 1]), Vector3.right);
			}
			for (int i = 0; i < profile.Length; i++)
			{
				var a = profile[i];
				var e = profile[(i + 1) % profile.Length];
				var edge = e - a;
				var n2 = new Vector2(edge.y, -edge.x);
				if (Vector2.Dot(n2, (a + e) * 0.5f - c) < 0f) n2 = -n2;
				var n = new Vector3(0, n2.y, n2.x).normalized;
				b.Quad(P(lo, a), P(hi, a), P(hi, e), P(lo, e), n);
			}
			AddMesh(t, m, b.Build(), Vector3.zero);
		}

		// Cone, apex at `apex`, opening upward to a base of radius r at height h (+ a flat cap).
		private static void Cone(GameObject t, Material m, Vector3 apex, float r, float h)
		{
			const int seg = 14;
			var b = new MeshBuilder("Cone");
			var top = new Vector3(0, h, 0);
			for (int i = 0; i < seg; i++)
			{
				float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
				var p0 = new Vector3(Mathf.Cos(a0) * r, h, Mathf.Sin(a0) * r);
				var p1 = new Vector3(Mathf.Cos(a1) * r, h, Mathf.Sin(a1) * r);
				var mid = (p0 + p1) * 0.5f;
				// Side normal: outward, tilted down (the cone opens upward from the apex).
				var n = (new Vector3(mid.x, 0, mid.z).normalized * h + Vector3.down * r).normalized;
				b.Tri(Vector3.zero, p0, p1, n);
				b.Tri(top, p0, p1, Vector3.up);
			}
			AddMesh(t, m, b.Build(), apex);
		}

		private class MeshBuilder
		{
			private readonly string _name;
			private readonly List<Vector3> _v = new(), _n = new();
			private readonly List<int> _t = new();

			public MeshBuilder(string name) => _name = name;

			public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n)
			{
				if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) (b, c) = (c, b);
				int i = _v.Count;
				_v.Add(a); _v.Add(b); _v.Add(c);
				_n.Add(n); _n.Add(n); _n.Add(n);
				_t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
			}

			public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
			{
				Tri(a, b, c, n);
				Tri(a, c, d, n);
			}

			public Mesh Build()
			{
				var mesh = new Mesh { name = _name };
				mesh.SetVertices(_v);
				mesh.SetNormals(_n);
				mesh.SetTriangles(_t, 0);
				mesh.RecalculateBounds();
				return mesh;
			}
		}

		// ================================================================== bake / prefabs

		/// <summary>Combines every MeshRenderer under t into one mesh (one submesh per material), saved in place.</summary>
		private static (Mesh mesh, Material[] mats) BakeMesh(GameObject t, string name)
		{
			var order = new List<Material>();
			var groups = new Dictionary<Material, List<CombineInstance>>();
			var toLocal = t.transform.worldToLocalMatrix;
			foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
			{
				var mf = r.GetComponent<MeshFilter>();
				if (mf == null || mf.sharedMesh == null) continue;
				var m = r.sharedMaterial;
				if (!groups.TryGetValue(m, out var list))
				{
					groups[m] = list = new List<CombineInstance>();
					order.Add(m);
				}
				list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toLocal * r.transform.localToWorldMatrix });
			}

			var subs = new List<CombineInstance>();
			foreach (var m in order)
			{
				var sub = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
				sub.CombineMeshes(groups[m].ToArray(), true, true);
				subs.Add(new CombineInstance { mesh = sub, transform = Matrix4x4.identity });
			}
			var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
			mesh.CombineMeshes(subs.ToArray(), false, false);
			mesh.RecalculateBounds();
			foreach (var s in subs) Object.DestroyImmediate(s.mesh);
			mesh = SaveMesh(mesh, $"{MeshFolder}/{name}.asset");
			Debug.Log($"{Log} {name}: {mesh.vertexCount} verts, {order.Count} materials.");
			return (mesh, order.ToArray());
		}

		// Shared: rewrites an existing asset in place so loaded instances update at once.
		private static Mesh SaveMesh(Mesh mesh, string path) => SuburbBuilder.SaveMesh(mesh, path);

		private static Material[] Swap(Material[] mats, params (Material from, Material to)[] swaps)
		{
			var result = (Material[])mats.Clone();
			for (int i = 0; i < result.Length; i++)
				foreach (var (from, to) in swaps)
					if (result[i] == from)
						result[i] = to;
			return result;
		}

		// Prefab = mesh + renderer + carving obstacle (impassable) + bullet blocker (stops bullets, indestructible).
		private static VehicleInfo SaveVehicle(string name, Mesh mesh, Material[] mats, Bounds footprint)
		{
			var go = new GameObject(name);
			try
			{
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				var renderer = go.AddComponent<MeshRenderer>();
				renderer.sharedMaterials = mats;
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
				renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

				// The body must not become walkable navmesh (roof) — the obstacle cuts the hole instead.
				go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				var obstacle = go.AddComponent<NavMeshObstacle>();
				obstacle.shape = NavMeshObstacleShape.Box;
				obstacle.center = footprint.center;
				obstacle.size = footprint.size + new Vector3(0.2f, 0f, 0.2f);
				obstacle.carving = true;
				obstacle.carveOnlyStationary = false; // the sector teleports when it recycles — re-carve at once

				var blocker = go.AddComponent<BulletBlocker>();
				SetField(blocker, "_center", footprint.center);
				SetField(blocker, "_size", footprint.size);
				SparkOnHit(blocker);

				string path = $"{PrefabFolder}/{name}.prefab";
				PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
				if (!ok)
					Debug.LogError($"{Log} Failed to save {path}");
				return new VehicleInfo { Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path), Footprint = footprint };
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		// ================================================================== materials

		private static void CreateMaterials()
		{
			_tealPaint = VMat("Veh_Paint_Teal", new Color(0.1f, 0.27f, 0.4f), 0.3f, 0.6f);
			_redPaint = VMat("Veh_Paint_PickupRed", new Color(0.6f, 0.08f, 0.07f), 0.3f, 0.6f);
			_sedanRed = VMat("Veh_Paint_SedanRed", new Color(0.55f, 0.05f, 0.09f), 0.35f, 0.65f);
			_sedanSilver = VMat("Veh_Paint_Silver", new Color(0.66f, 0.68f, 0.7f), 0.6f, 0.55f);
			_sedanNavy = VMat("Veh_Paint_Navy", new Color(0.1f, 0.14f, 0.27f), 0.35f, 0.6f);
			_lowerSilver = VMat("Veh_Lower_Silver", new Color(0.62f, 0.63f, 0.65f), 0.5f, 0.5f);
			_lowerCharcoal = VMat("Veh_Lower_Charcoal", new Color(0.18f, 0.18f, 0.2f), 0.2f, 0.4f);

			_glass = VMat("Veh_Glass", new Color(0.07f, 0.09f, 0.12f), 0f, 0.92f);
			_tire = VMat("Veh_Tire", new Color(0.07f, 0.07f, 0.075f), 0f, 0.1f);
			_rim = VMat("Veh_Rim", new Color(0.72f, 0.74f, 0.76f), 0.6f, 0.6f);
			_rimWhite = VMat("Veh_RimWhite", new Color(0.9f, 0.9f, 0.88f), 0.1f, 0.4f);
			_chrome = VMat("Veh_Chrome", new Color(0.8f, 0.81f, 0.83f), 0.8f, 0.75f);
			_black = VMat("Veh_Trim_Black", new Color(0.06f, 0.06f, 0.07f), 0f, 0.3f);
			_head = VMat("Veh_Headlight", new Color(0.95f, 0.95f, 0.88f), 0f, 0.9f);
			_tail = VMat("Veh_Taillight", new Color(0.75f, 0.06f, 0.06f), 0f, 0.7f);
			_amber = VMat("Veh_Amber", new Color(0.98f, 0.55f, 0.1f), 0f, 0.7f);
			_plate = VMat("Veh_Plate", new Color(0.92f, 0.92f, 0.88f), 0f, 0.3f);
			_bed = VMat("Veh_BedLiner", new Color(0.12f, 0.12f, 0.13f), 0f, 0.05f);
			_grille = VMat("Veh_Grille", new Color(0.35f, 0.36f, 0.38f), 0.4f, 0.4f);

			_white = VMat("Veh_Truck_White", new Color(0.95f, 0.95f, 0.94f), 0f, 0.45f);
			_iceBlue = VMat("Veh_Truck_IceBlue", new Color(0.55f, 0.74f, 0.95f), 0f, 0.45f);
			_paleBlue = VMat("Veh_Truck_PaleBlue", new Color(0.76f, 0.87f, 0.98f), 0f, 0.45f);
			_mailBlue = VMat("Veh_Mail_Blue", new Color(0.1f, 0.2f, 0.48f), 0f, 0.45f);
			_mailRed = VMat("Veh_Mail_Red", new Color(0.78f, 0.1f, 0.12f), 0f, 0.45f);
			_awningPink = VMat("Veh_Awning_Pink", new Color(0.98f, 0.5f, 0.68f), 0f, 0.3f);
			_waffle = VMat("Veh_IceCream_Waffle", new Color(0.82f, 0.6f, 0.32f), 0f, 0.2f);
			_strawberry = VMat("Veh_IceCream_Strawberry", new Color(0.98f, 0.62f, 0.74f), 0f, 0.35f);
			_vanilla = VMat("Veh_IceCream_Vanilla", new Color(0.99f, 0.95f, 0.8f), 0f, 0.35f);
			_mint = VMat("Veh_IceCream_Mint", new Color(0.6f, 0.92f, 0.75f), 0f, 0.35f);
			_lemon = VMat("Veh_IceCream_Lemon", new Color(0.98f, 0.9f, 0.4f), 0f, 0.35f);
			_choco = VMat("Veh_IceCream_Choco", new Color(0.4f, 0.24f, 0.14f), 0f, 0.35f);
		}

		// Lit See-Through (URP Lit + hole around the player), colour/metallic/smoothness enforced on every run.
		private static Material VMat(string name, Color color, float metallic, float smoothness)
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
			m.enableInstancing = true;
			EditorUtility.SetDirty(m);
			return m;
		}
	}
}
