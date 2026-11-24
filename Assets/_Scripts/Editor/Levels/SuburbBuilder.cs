using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using static Crowds.EditorTools.KeyLevelBuilder;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Dresses the Infinite Scroll level (Level_01, SectorMode.Recycling) as an endless "liminal" American suburb —
	/// Tools ▸ Crowds ▸ Build Suburb (Infinite Scroll). Safe to re-run (meshes/prefabs rewritten in place, same guids).
	///
	/// The walkable platforms (FloorSector slabs, 49.5 × 50 m) keep their area and colliders; nothing is added ON them
	/// except flat surface dressing (no navmesh effect, receives decals). Cross-section along X (the street runs along
	/// Z, the scroll direction; the fixed camera looks −X, so the houses at −X face the screen):
	///
	///   [houses + white picket fences, off the platform] | front lawns | sidewalk | grass strip | curb | ROAD |
	///   curb | grass strip | sidewalk | front lawns | [houses + fences, off the platform, camera side]
	///
	/// Both sides are lined with the same house prefabs: the far (−X) side faces the camera, the near (+X) side turns
	/// its facades to the street (yaw −90) and is seen from behind/above — the Lit See-Through shader cuts a soft hole
	/// where it covers the player. Each sector also gets up to 2 boxy hedges (Hedge.prefab, one per side) on the front
	/// lawns along a property line: no collider, a ModifierZone that slows whoever pushes through, like an oil slick.
	///
	/// Prefabs (Assets/_Prefabs/Suburb), each a single baked mesh with one submesh per material:
	///   Fence_Picket       — 2 m white picket segment (posts, rails, pointed pickets);
	///   House_Ranch        — 1-story ranch: porch with columns, picture window, attached garage, chimney;
	///   House_Colonial     — 2-story colonial: full-width porch, shuttered windows, side chimney;
	///   House_Cape         — 1.5-story cape: steep roof with two dormers, small porch;
	///   Street_Section     — one 50 m sector of street surface: lawns (mowing stripes), sidewalks with joints,
	///                        grass strips, curbs, asphalt with a dashed centre line, yard ground under both
	///                        house rows;
	///   Row_A / Row_B / Row_C — 3 lots per sector: houses (per-row siding/roof/door colours), fences closing the gaps
	///                        between them (the drop behind is never visible), driveways and front walks;
	///   Row_A_East / Row_B_East / Row_C_East — the same, mirrored for the camera side (other colours);
	///   Hedge              — 6 × 1.2 m clipped hedge (Hedge ModifierZone, SpeedModifier 0.55).
	/// Level_01: every FloorSector gets a "Suburb" child = Street_Section + Row_A/B/C + Row_X_East + Hedges, so the
	/// dressing rides along
	/// when CheckSectorSystem leap-frogs the sector. Three slightly different rows repeating every 150 m — the street
	/// feels endless. See Docs/SuburbFeature.md.
	/// </summary>
	public static class SuburbBuilder
	{
		private const string PrefabFolder = "Assets/_Prefabs/Suburb";
		private const string MeshFolder = PrefabFolder + "/Meshes";
		private const string MatFolder = "Assets/_Materials/Suburb";
		private const string Level01Path = "Assets/_Prefabs/Levels/Level_01.prefab";
		private const string Log = "[SuburbBuilder]";

		// FloorSector slab (local): x [-25.5, 24], z [-25.5, 24.5], top y = -0.5.
		private const float SlabMinX = -25.5f, SlabMaxX = 24f, SlabMinZ = -25.5f, SlabMaxZ = 24.5f, Top = -0.5f;
		// Cross-section (x).
		private const float WalkW0 = -14f, WalkW1 = -12.5f, Road0 = -11f, Road1 = 3f, WalkE0 = 4.5f, WalkE1 = 6f;
		private const float CurbWidth = 0.25f;
		private const float HouseFrontX = -28.5f; // porches + steps end ≈ -25.9: everything stays off the platform
		private const float FenceX = -31f;        // fences between the houses, a bit behind the facades
		private const float YardMinX = -48f;
		// Camera (+X) side, mirrored: steps of the deepest porch (colonial, 3.05 m) end at ≈ 24.45, past the slab edge.
		private const float HouseFrontEX = 27.5f;
		private const float FenceEX = 30f;
		private const float YardMaxX = 46f;
		// Hedges: footprint (length across the lawn × thickness), height; inset from the slab edge.
		private const float HedgeLength = 6f, HedgeThickness = 1.2f, HedgeHeight = 1.15f, HedgeEdgeInset = 0.3f;
		private const float HedgeClearance = 1.5f; // keep this far from spawn/loot points and vehicles
		private const int LotsPerSector = 3;
		private const float SectorLength = SlabMaxZ - SlabMinZ; // 50

		// Surface heights above the slab top (visual only — the navmesh stays on the slab).
		private const float HLawn = 0.01f, HRoad = 0.006f, HMark = 0.012f, HWalk = 0.03f, HJoint = 0.031f, HCurb = 0.06f,
			HPath = 0.025f, HDrive = 0.032f;

		internal static Material _grass, _grassAlt, _concrete, _joint, _curb, _asphalt, _lineYellow, _drive, _soil;
		internal static Material _trim, _glass, _porch, _foundation, _brick, _fence, _hedge, _hedgeTop;
		internal static Material _sidingCream, _sidingBlue, _sidingSage, _sidingBeige, _sidingGrey, _sidingWhite, _sidingYellow;
		internal static Material _roofCharcoal, _roofBrown, _roofSlate;
		internal static Material _doorRed, _doorNavy, _doorGreen, _doorBlack;
		internal static uint _floorLayers = 1;

		// Base slot materials the house prefabs are built with (rows swap them per house).
		internal static Material BaseSiding => _sidingWhite;
		internal static Material BaseRoof => _roofCharcoal;
		internal static Material BaseDoor => _doorBlack;

		internal class HouseSpec
		{
			public GameObject Prefab;
			public float MinX, MaxX;   // local extents along the street (house local X)
			public float DoorX;        // local X of the front door (front walk)
			public float StepsZ;       // local Z where the porch steps end
			public float? GarageX;     // local X of the garage door centre (driveway), if any
		}

		/// <summary>House kit shared with SuburbLevelsBuilder (finite suburb levels).</summary>
		internal sealed class HouseKit
		{
			public GameObject Fence, Hedge;
			public HouseSpec Ranch, Colonial, Cape;
		}

		/// <summary>
		/// Creates the materials and (re)builds the fence, hedge and the three house prefabs in place (same guids, so
		/// Level_01 picks up any change too). Doesn't touch Level_01 itself.
		/// </summary>
		internal static HouseKit BuildKit()
		{
			EnsureFolder(PrefabFolder);
			EnsureFolder(MeshFolder);
			EnsureFolder(MatFolder);
			CreateSuburbMaterials();
			ReadFloorLayers();
			return new HouseKit
			{
				Fence = BuildFence(),
				Ranch = BuildRanch(),
				Colonial = BuildColonial(),
				Cape = BuildCape(),
				Hedge = BuildHedge(),
			};
		}

		[MenuItem("Tools/Crowds/Build Suburb (Infinite Scroll)")]
		public static void Build()
		{
			EnsureFolder(PrefabFolder);
			EnsureFolder(MeshFolder);
			EnsureFolder(MatFolder);
			CreateSuburbMaterials();
			ReadFloorLayers();

			var fence = BuildFence();
			var ranch = BuildRanch();
			var colonial = BuildColonial();
			var cape = BuildCape();
			var street = BuildStreet();
			var hedge = BuildHedge();

			var rows = new[]
			{
				BuildRow("Row_A", false, fence, new[]
				{
					(ranch, _sidingCream, _roofCharcoal, _doorRed),
					(colonial, _sidingBlue, _roofSlate, _doorNavy),
					(cape, _sidingWhite, _roofCharcoal, _doorGreen),
				}),
				BuildRow("Row_B", false, fence, new[]
				{
					(colonial, _sidingSage, _roofBrown, _doorBlack),
					(cape, _sidingYellow, _roofSlate, _doorNavy),
					(ranch, _sidingGrey, _roofCharcoal, _doorRed),
				}),
				BuildRow("Row_C", false, fence, new[]
				{
					(cape, _sidingBlue, _roofCharcoal, _doorRed),
					(ranch, _sidingBeige, _roofBrown, _doorGreen),
					(colonial, _sidingWhite, _roofSlate, _doorBlack),
				}),
			};
			// Camera side: the same prefabs, other orders/colours, so facing houses never match.
			var eastRows = new[]
			{
				BuildRow("Row_A_East", true, fence, new[]
				{
					(colonial, _sidingYellow, _roofCharcoal, _doorGreen),
					(ranch, _sidingWhite, _roofSlate, _doorNavy),
					(cape, _sidingSage, _roofBrown, _doorBlack),
				}),
				BuildRow("Row_B_East", true, fence, new[]
				{
					(cape, _sidingGrey, _roofSlate, _doorRed),
					(colonial, _sidingCream, _roofBrown, _doorNavy),
					(ranch, _sidingBlue, _roofCharcoal, _doorBlack),
				}),
				BuildRow("Row_C_East", true, fence, new[]
				{
					(ranch, _sidingSage, _roofCharcoal, _doorRed),
					(cape, _sidingWhite, _roofBrown, _doorGreen),
					(colonial, _sidingBeige, _roofSlate, _doorNavy),
				}),
			};

			ApplyToLevel(street, rows, eastRows, hedge);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"{Log} Suburb built and applied to {Level01Path}.");
		}

		// ================================================================== level

		internal static void ApplyToLevel(GameObject street, GameObject[] rows, GameObject[] eastRows, GameObject hedge)
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
				var keepClear = CollectKeepClearPoints(root.transform);
				int hedges = 0;
				for (int i = 0; i < sectors.Count; i++)
				{
					var sector = sectors[i].transform;
					var old = sector.Find("Suburb");
					if (old != null)
						Object.DestroyImmediate(old.gameObject);
					var suburb = new GameObject("Suburb").transform;
					suburb.SetParent(sector, false);
					Instance(street, suburb, "Street");
					Instance(rows[i % rows.Length], suburb, "Houses");
					// "Houses_East" — VehicleBuilder looks for driveways only under "Houses".
					Instance(eastRows[i % eastRows.Length], suburb, "Houses_East");
					hedges += PlaceHedges(hedge, sector, suburb, i, keepClear);
				}
				Debug.Log($"{Log} {hedges} hedges placed.");
				PrefabUtility.SaveAsPrefabAsset(root, Level01Path, out bool ok);
				if (!ok)
					Debug.LogError($"{Log} Failed to save {Level01Path}");
				else
					Debug.Log($"{Log} {sectors.Count} sectors dressed: {string.Join(", ", sectors.ConvertAll(s => s.name))}.");
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		// ================================================================== hedges

		// One hedge per side of the street at most, on the front lawn along a property line between two lots
		// (perpendicular to the street, from the slab edge toward the sidewalk — walks and driveways sit mid-lot and
		// at the garages, clear of the lines). Sides alternate the two lines per sector; a line too close to a
		// spawn/loot point or a vehicle is skipped (then the other one is tried).
		internal static int PlaceHedges(GameObject hedge, Transform sector, Transform suburb, int sectorIndex,
			List<Transform> keepClear)
		{
			var parent = new GameObject("Hedges").transform;
			parent.SetParent(suburb, false);
			var vehicles = sector.Find("Vehicles");
			float lot = SectorLength / LotsPerSector;
			var lines = new[] { SlabMinZ + lot, SlabMinZ + lot * 2f };
			int placed = 0;
			foreach (bool east in new[] { false, true })
			{
				float x = east
					? SlabMaxX - HedgeEdgeInset - HedgeLength * 0.5f
					: SlabMinX + HedgeEdgeInset + HedgeLength * 0.5f;
				int first = (sectorIndex + (east ? 1 : 0)) % 2;
				bool done = false;
				for (int k = 0; k < lines.Length && !done; k++)
				{
					float z = lines[(first + k) % lines.Length];
					var centre = new Vector3(x, Top + HLawn, z);
					if (!IsHedgeSpotClear(sector, centre, keepClear, vehicles, out string blocker))
					{
						Debug.Log($"{Log} {sector.name}: hedge spot x {x:0.#}, z {z:0.#} skipped, too close to {blocker}.");
						continue;
					}
					var go = (GameObject)PrefabUtility.InstantiatePrefab(hedge, parent);
					go.name = east ? "Hedge_East" : "Hedge_West";
					go.transform.SetLocalPositionAndRotation(centre, Quaternion.identity);
					placed++;
					done = true;
				}
				if (!done)
					Debug.LogWarning($"{Log} {sector.name}: no clear spot for the {(east ? "east" : "west")} hedge.");
			}
			return placed;
		}

		internal static bool IsHedgeSpotClear(Transform sector, Vector3 localCentre, List<Transform> keepClear,
			Transform vehicles, out string blocker)
		{
			var half = new Vector2(HedgeLength * 0.5f + HedgeClearance, HedgeThickness * 0.5f + HedgeClearance);
			foreach (var p in keepClear)
			{
				var local = sector.InverseTransformPoint(p.position);
				if (Mathf.Abs(local.x - localCentre.x) <= half.x && Mathf.Abs(local.z - localCentre.z) <= half.y)
				{
					blocker = p.name;
					return false;
				}
			}
			if (vehicles != null)
			{
				var world = sector.TransformPoint(localCentre);
				var box = new Bounds(world, new Vector3(half.x * 2f, 10f, half.y * 2f));
				foreach (var r in vehicles.GetComponentsInChildren<Renderer>(true))
				{
					if (!r.bounds.Intersects(box))
						continue;
					blocker = r.name;
					return false;
				}
			}
			blocker = null;
			return true;
		}

		// Spawn points, loot points, breakable spawns and the pre-placed loot (same rule as VehicleBuilder).
		internal static List<Transform> CollectKeepClearPoints(Transform root)
		{
			var list = new List<Transform>();
			foreach (var t in root.GetComponentsInChildren<Transform>(true))
			{
				string n = t.name;
				if (n.Contains("SpawnPoint") || n.StartsWith("ToxicPoint") || n.StartsWith("LootPoint")
				    || n.StartsWith("BreakableSpawn") || (t.parent != null && t.parent.name == "_PreplacedLoot"))
					list.Add(t);
			}
			return list;
		}

		// Clipped hedge: dark body, lighter flat top, a few shallow lumps on the faces so it isn't a bare cube.
		// Local frame: length along X, thickness along Z, centred, ground at y = 0.
		internal static GameObject BuildHedge()
		{
			var t = Temp("Hedge");
			try
			{
				const float L = HedgeLength, T = HedgeThickness, H = HedgeHeight;
				Box(t, _hedge, new Vector3(0, H * 0.5f, 0), new Vector3(L, H, T));
				Box(t, _hedgeTop, new Vector3(0, H + 0.02f, 0), new Vector3(L - 0.08f, 0.04f, T - 0.08f));
				var random = new System.Random(7);
				float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
				foreach (float side in new[] { -1f, 1f })
				{
					for (float x = -L * 0.5f + 0.5f; x < L * 0.5f - 0.3f; x += R(0.7f, 1.1f))
					{
						float w = R(0.45f, 0.8f), h = R(0.35f, 0.7f), y = R(0.25f, H - 0.3f);
						Box(t, _hedge, new Vector3(x, y, side * (T * 0.5f + 0.02f)), new Vector3(w, h, 0.08f));
					}
				}
				foreach (float end in new[] { -1f, 1f })
					Box(t, _hedge, new Vector3(end * (L * 0.5f + 0.02f), R(0.4f, 0.7f), R(-0.2f, 0.2f)),
						new Vector3(0.08f, R(0.4f, 0.6f), R(0.5f, 0.8f)));

				return Bake(t, "Hedge", null, castShadows: true, receiveDecals: false, extra: go =>
				{
					go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
					go.AddComponent<Hedge>().ApplyDefaults(new Vector2(L, T));
				});
			}
			finally
			{
				Object.DestroyImmediate(t);
			}
		}

		internal static GameObject Instance(GameObject prefab, Transform parent, string name)
		{
			var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
			go.name = name;
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			return go;
		}

		// Decal projectors draw only on rendering layer 4 (Decal prefab m_DecalLayerMask = 4); level floors use 1|4.
		// (The FloorSector prefab itself says 1 — Level_01 overrides it to 5 per instance — so don't read it from there.)
		internal static void ReadFloorLayers() => _floorLayers = FloorRenderingLayers;

		// ================================================================== street

		internal static GameObject BuildStreet()
		{
			var t = Temp("Street_Section");
			try
			{
				float z0 = SlabMinZ, z1 = SlabMaxZ;
				// Yard ground under both house rows: also hides the slab's side faces.
				Slab(t, _grassAlt, YardMinX, SlabMinX, z0, z1, -1f, Top + HLawn);
				Slab(t, _grassAlt, SlabMaxX, YardMaxX, z0, z1, -1f, Top + HLawn);
				// Lawns with mowing stripes (2.5 m bands along the street).
				Striped(t, SlabMinX, WalkW0, z0, z1);
				Striped(t, WalkE1, SlabMaxX, z0, z1);
				// Grass strips between the sidewalks and the curbs.
				Strip(t, _grass, WalkW1, Road0, z0, z1, HLawn);
				Strip(t, _grass, Road1, WalkE0, z0, z1, HLawn);
				// Sidewalks + expansion joints (32 per sector → tiles across sectors).
				foreach (var (a, b) in new[] { (WalkW0, WalkW1), (WalkE0, WalkE1) })
				{
					Strip(t, _concrete, a, b, z0, z1, HWalk);
					const int joints = 32;
					for (int i = 1; i < joints; i++)
					{
						float z = z0 + SectorLength * i / joints;
						Box(t, _joint, new Vector3((a + b) * 0.5f, Top + HJoint - 0.002f, z), new Vector3(b - a, 0.004f, 0.04f));
					}
				}
				// Road, curbs, dashed centre line (3 m dash every 10 m → tiles across sectors).
				Strip(t, _asphalt, Road0, Road1, z0, z1, HRoad);
				Strip(t, _curb, Road0 - CurbWidth, Road0, z0, z1, HCurb);
				Strip(t, _curb, Road1, Road1 + CurbWidth, z0, z1, HCurb);
				float centre = (Road0 + Road1) * 0.5f;
				for (float z = z0 + 3.5f; z < z1; z += 10f)
					Box(t, _lineYellow, new Vector3(centre, Top + HMark - 0.002f, z + 1.5f), new Vector3(0.15f, 0.004f, 3f));

				return Bake(t, "Street_Section", null, castShadows: false, receiveDecals: true,
					extra: go => go.AddComponent<NavMeshModifier>().ignoreFromBuild = true);
			}
			finally
			{
				Object.DestroyImmediate(t);
			}
		}

		internal static void Striped(GameObject t, float x0, float x1, float z0, float z1)
		{
			const float band = 2.5f; // 20 bands per sector → tiles
			int n = 0;
			for (float z = z0; z < z1 - 0.01f; z += band, n++)
				Box(t, n % 2 == 0 ? _grass : _grassAlt, new Vector3((x0 + x1) * 0.5f, Top + HLawn - 0.01f, z + band * 0.5f),
					new Vector3(x1 - x0, 0.02f, band));
		}

		// Thin surface strip along the whole sector, top at Top + h.
		internal static void Strip(GameObject t, Material m, float x0, float x1, float z0, float z1, float h) =>
			Box(t, m, new Vector3((x0 + x1) * 0.5f, Top + h - 0.01f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.02f, z1 - z0));

		internal static void Slab(GameObject t, Material m, float x0, float x1, float z0, float z1, float y0, float y1) =>
			Box(t, m, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, y1 - y0, z1 - z0));

		// ================================================================== rows

		// east = camera (+X) side: houses turned to face −X, everything mirrored across the street.
		internal static GameObject BuildRow(string name, bool east, GameObject fence,
			(HouseSpec spec, Material siding, Material roof, Material door)[] lots)
		{
			float frontX = east ? HouseFrontEX : HouseFrontX;
			float facing = east ? -1f : 1f;        // world X direction the facades face
			float along = east ? 1f : -1f;         // world Z direction of the house's local +X
			float walkX = east ? WalkE1 : WalkW0;  // front walks end at the near edge of the sidewalk
			float roadX = east ? Road1 : Road0;    // driveways end at the curb
			var root = new GameObject(name);
			try
			{
				root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				float lot = SectorLength / LotsPerSector;
				var spans = new List<(float min, float max)>();
				var ground = Temp("Ground");
				for (int i = 0; i < lots.Length && i < LotsPerSector; i++)
				{
					var (spec, siding, roof, door) = lots[i];
					float lotCentre = SlabMinZ + lot * (i + 0.5f);
					// House local +X maps to world −Z (yaw 90, far side) / +Z (yaw −90, camera side): centre its
					// extents in the lot.
					float rootZ = lotCentre - along * (spec.MinX + spec.MaxX) * 0.5f;
					var house = (GameObject)PrefabUtility.InstantiatePrefab(spec.Prefab, root.transform);
					house.name = $"{spec.Prefab.name}_{i + 1}";
					house.transform.SetLocalPositionAndRotation(new Vector3(frontX, Top, rootZ),
						Quaternion.Euler(0, east ? -90f : 90f, 0));
					Recolor(house, siding, roof, door);
					float zA = rootZ + along * spec.MinX, zB = rootZ + along * spec.MaxX;
					spans.Add((Mathf.Min(zA, zB), Mathf.Max(zA, zB)));

					// Front walk: from the porch steps across the front lawn to the sidewalk.
					float doorZ = rootZ + along * spec.DoorX;
					float stepsX = frontX + facing * spec.StepsZ;
					Box(ground, _drive, new Vector3((stepsX + walkX) * 0.5f, Top + HPath - 0.01f, doorZ),
						new Vector3(Mathf.Abs(walkX - stepsX), 0.02f, 1.2f));
					// Driveway from the garage to the road (crosses the sidewalk and the grass strip).
					if (spec.GarageX.HasValue)
					{
						float gz = rootZ + along * spec.GarageX.Value;
						Box(ground, _drive, new Vector3((frontX + roadX) * 0.5f, Top + HDrive - 0.01f, gz),
							new Vector3(Mathf.Abs(roadX - frontX), 0.02f, 3.2f));
					}
				}

				// White picket fences close every gap between the houses (and to the sector ends — they meet the
				// neighbouring sector's fence), so the drop behind the houses is never visible.
				spans.Sort((a, b) => a.min.CompareTo(b.min));
				float cursor = SlabMinZ;
				int fenceIndex = 0;
				foreach (var (min, max) in spans)
				{
					FenceRun(root.transform, fence, east, cursor, min, ref fenceIndex);
					cursor = max;
				}
				FenceRun(root.transform, fence, east, cursor, SlabMaxZ, ref fenceIndex);

				var groundPrefab = Bake(ground, $"{name}_Ground", null, castShadows: false, receiveDecals: true);
				Object.DestroyImmediate(ground);
				Instance(groundPrefab, root.transform, "Ground");

				string path = $"{PrefabFolder}/{name}.prefab";
				PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
				if (!ok)
					Debug.LogError($"{Log} Failed to save {path}");
				return AssetDatabase.LoadAssetAtPath<GameObject>(path);
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		// Fence segments (2 m) along Z at FenceX / FenceEX from z0 to z1, stretched slightly to fit exactly.
		internal static void FenceRun(Transform parent, GameObject fence, bool east, float z0, float z1, ref int index)
		{
			float length = z1 - z0;
			if (length < 0.2f)
				return;
			int n = Mathf.Max(1, Mathf.CeilToInt(length / 2f - 0.05f));
			float segment = length / n;
			for (int i = 0; i < n; i++)
			{
				var go = (GameObject)PrefabUtility.InstantiatePrefab(fence, parent);
				go.name = $"Fence_{++index}";
				// Far side: segment local +X runs along world +Z (yaw −90). Camera side: along −Z (yaw 90), so it starts
				// at the segment's far end. Either way the rails are on the street side.
				go.transform.SetLocalPositionAndRotation(east
						? new Vector3(FenceEX, Top, z0 + segment * (i + 1))
						: new Vector3(FenceX, Top, z0 + segment * i),
					Quaternion.Euler(0, east ? 90f : -90f, 0));
				go.transform.localScale = new Vector3(segment / 2f, 1f, 1f);
			}
		}

		// Swaps the base siding/roof/door(+shutters) materials on a house instance (instance override).
		internal static void Recolor(GameObject house, Material siding, Material roof, Material door)
		{
			var r = house.GetComponent<MeshRenderer>();
			var mats = r.sharedMaterials;
			for (int i = 0; i < mats.Length; i++)
			{
				if (mats[i] == BaseSiding) mats[i] = siding;
				else if (mats[i] == BaseRoof) mats[i] = roof;
				else if (mats[i] == BaseDoor) mats[i] = door;
			}
			r.sharedMaterials = mats;
		}

		// ================================================================== fence

		internal static GameObject BuildFence()
		{
			var t = Temp("Fence_Picket");
			try
			{
				const float len = 2f, h = 1.05f;
				foreach (float x in new[] { 0.05f, len - 0.05f })
					Box(t, _fence, new Vector3(x, 0.6f, 0), new Vector3(0.1f, 1.2f, 0.1f));
				foreach (float y in new[] { 0.3f, 0.85f })
					Box(t, _fence, new Vector3(len * 0.5f, y, -0.06f), new Vector3(len, 0.08f, 0.04f));
				const int pickets = 9;
				for (int i = 0; i < pickets; i++)
				{
					float x = 0.17f + i * (len - 0.34f) / (pickets - 1);
					Box(t, _fence, new Vector3(x, 0.08f + (h - 0.08f) * 0.5f, 0), new Vector3(0.085f, h - 0.08f, 0.025f));
					// Pointed top: a small cube turned 45°.
					Box(t, _fence, new Vector3(x, h, 0), new Vector3(0.06f, 0.06f, 0.025f), Quaternion.Euler(0, 0, 45f));
				}
				return Bake(t, "Fence_Picket", null, castShadows: true, receiveDecals: false);
			}
			finally
			{
				Object.DestroyImmediate(t);
			}
		}

		// ================================================================== houses
		// House local frame: front faces +Z, front wall at z = 0, body to −Z, width along X, ground at y = 0.

		internal static Material[] HouseSlots => new[] { BaseSiding, _trim, BaseRoof, _glass, BaseDoor, _porch, _foundation, _brick };

		internal static HouseSpec BuildRanch()
		{
			const float W = 10f, D = 9f, found = 0.45f, wall = 2.9f, roofH = 1.9f;
			const float gW = 3.8f, gD = 7f, gWall = 2.6f, gRoof = 1.3f;
			float gx = -W * 0.5f - gW * 0.5f;
			var t = Temp("House_Ranch");
			try
			{
				Body(t, 0f, W, D, found, wall, roofH);
				// Attached garage on the −X side.
				Box(t, _foundation, new Vector3(gx, 0.075f, -gD * 0.5f), new Vector3(gW + 0.05f, 0.15f, gD + 0.05f));
				Box(t, BaseSiding, new Vector3(gx, 0.15f + gWall * 0.5f, -gD * 0.5f), new Vector3(gW, gWall, gD));
				Prism(t, BaseSiding, new Vector3(gx, 0.15f + gWall, -gD * 0.5f), new Vector3(gW, gRoof, gD));
				GableRoof(t, gx, gW, gD, 0.15f + gWall, gRoof, 0.3f);
				var gFace = Face(t, new Vector3(gx, 0.15f, 0f), 0f);
				Box(gFace, _trim, new Vector3(0, 1.1f, 0.04f), new Vector3(3f, 2.2f, 0.08f));
				for (int i = 1; i <= 3; i++)
					Box(gFace, _foundation, new Vector3(0, 0.55f * i, 0.085f), new Vector3(2.9f, 0.03f, 0.02f));
				Frame(gFace, 0, 1.1f, 3f, 2.2f);

				// Porch, door, windows.
				Porch(t, 1.5f, 5f, 2f, found, 3.15f, 3);
				var front = Face(t, new Vector3(0, found, 0f), 0f);
				Door(front, 2.2f);
				Window(front, -2.5f, 1.45f, 2.4f, 1.4f, shutters: false);
				Window(front, 4.1f, 1.45f, 1f, 1.4f, shutters: true);
				SideWindows(t, W, D, found, 1.45f);
				BackWall(t, W, D, found, 1.45f, door: true);
				Chimney(t, -2.6f, -D * 0.5f - 1.2f, found + wall, roofH);
				return Spec(Bake(t, "House_Ranch", HouseSlots, true, false, go => HouseBlocker(go)),
					gx - gW * 0.5f, W * 0.5f, 2.2f, 2f + 0.65f, gx);
			}
			finally
			{
				Object.DestroyImmediate(t);
			}
		}

		internal static HouseSpec BuildColonial()
		{
			const float W = 10f, D = 8f, found = 0.5f, wall = 5.8f, roofH = 2.4f;
			var t = Temp("House_Colonial");
			try
			{
				Body(t, 0f, W, D, found, wall, roofH);
				Porch(t, 0f, W, 2.4f, found, 3.1f, 4);
				var front = Face(t, new Vector3(0, found, 0f), 0f);
				Door(front, 0f);
				foreach (float x in new[] { -3f, 3f })
					Window(front, x, 1.4f, 1.1f, 1.6f, shutters: true);
				foreach (float x in new[] { -3.3f, 0f, 3.3f })
					Window(front, x, 4.2f, 1f, 1.4f, shutters: true);
				// Band board between the floors.
				Box(t, _trim, new Vector3(0, found + 2.95f, 0.02f), new Vector3(W + 0.05f, 0.12f, 0.06f));
				SideWindows(t, W, D, found, 1.4f);
				SideWindows(t, W, D, found, 4.2f);
				BackWall(t, W, D, found, 1.4f, door: true);
				BackWall(t, W, D, found, 4.2f, door: false);
				// Exterior chimney on the +X side.
				Box(t, _brick, new Vector3(W * 0.5f + 0.45f, (found + wall + roofH + 0.8f) * 0.5f, -D * 0.5f),
					new Vector3(0.9f, found + wall + roofH + 0.8f, 1.1f));
				return Spec(Bake(t, "House_Colonial", HouseSlots, true, false, go => HouseBlocker(go)),
					// Extents = the walls (the side chimney sits behind the fence line, the fence meets the wall).
					-W * 0.5f, W * 0.5f, 0f, 2.4f + 0.65f, null);
			}
			finally
			{
				Object.DestroyImmediate(t);
			}
		}

		internal static HouseSpec BuildCape()
		{
			const float W = 9.5f, D = 8f, found = 0.45f, wall = 2.6f, roofH = 3.4f;
			var t = Temp("House_Cape");
			try
			{
				Body(t, 0f, W, D, found, wall, roofH);
				Porch(t, 0f, 3.6f, 1.8f, found, 2.95f, 2);
				var front = Face(t, new Vector3(0, found, 0f), 0f);
				Door(front, 0f);
				foreach (float x in new[] { -2.9f, 2.9f })
					Window(front, x, 1.4f, 1.1f, 1.5f, shutters: true);
				SideWindows(t, W, D, found, 1.4f);
				BackWall(t, W, D, found, 1.4f, door: true);
				// Two dormers on the front slope.
				float baseY = found + wall;
				foreach (float x in new[] { -2.3f, 2.3f })
				{
					Box(t, BaseSiding, new Vector3(x, baseY + 1.55f, -2f), new Vector3(1.6f, 1.3f, 1.6f));
					// Dormer gable: ridge along Z (prism turned 90°).
					Prism(t, BaseRoof, new Vector3(x, baseY + 2.2f, -2f), new Vector3(1.8f, 0.75f, 1.9f), Quaternion.Euler(0, 90f, 0));
					var dFace = Face(t, new Vector3(x, baseY + 0.9f, -1.2f), 0f);
					Box(dFace, _glass, new Vector3(0, 0.65f, 0.03f), new Vector3(0.8f, 0.9f, 0.06f));
					Frame(dFace, 0, 0.65f, 0.8f, 0.9f);
				}
				Chimney(t, 3f, -D * 0.5f - 0.8f, baseY, roofH);
				return Spec(Bake(t, "House_Cape", HouseSlots, true, false, go => HouseBlocker(go)),
					-W * 0.5f, W * 0.5f, 0f, 1.8f + 0.65f, null);
			}
			finally
			{
				Object.DestroyImmediate(t);
			}
		}

		internal static HouseSpec Spec(GameObject prefab, float minX, float maxX, float doorX, float stepsZ, float? garageX) =>
			new() { Prefab = prefab, MinX = minX, MaxX = maxX, DoorX = doorX, StepsZ = stepsZ, GarageX = garageX };

		// Houses stop bullets (sparks) — bounds of the baked mesh.
		internal static void HouseBlocker(GameObject go)
		{
			var bounds = go.GetComponent<MeshFilter>().sharedMesh.bounds;
			var blocker = go.AddComponent<BulletBlocker>();
			SetField(blocker, "_center", bounds.center);
			SetField(blocker, "_size", bounds.size);
			SparkOnHit(blocker);
			go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
		}

		// Foundation, siding walls, gable ends and the roof of a W × D box house.
		internal static void Body(GameObject t, float x, float W, float D, float found, float wall, float roofH)
		{
			Box(t, _foundation, new Vector3(x, found * 0.5f, -D * 0.5f), new Vector3(W + 0.1f, found, D + 0.1f));
			Box(t, BaseSiding, new Vector3(x, found + wall * 0.5f, -D * 0.5f), new Vector3(W, wall, D));
			Prism(t, BaseSiding, new Vector3(x, found + wall, -D * 0.5f), new Vector3(W, roofH, D));
			GableRoof(t, x, W, D, found + wall, roofH, 0.45f);
			// White corner boards.
			foreach (float sx in new[] { -1f, 1f })
				foreach (float sz in new[] { 0f, -D })
					Box(t, _trim, new Vector3(x + sx * (W * 0.5f - 0.04f), found + wall * 0.5f, sz + (sz == 0f ? 0.02f : -0.02f)),
						new Vector3(0.14f, wall, 0.06f));
		}

		// Two roof planes over a gable prism (ridge along X), with an overhang and a ridge cap.
		internal static void GableRoof(GameObject t, float x, float W, float D, float baseY, float h, float overhang)
		{
			const float thick = 0.12f;
			float d = D * 0.5f;
			float slope = Mathf.Sqrt(d * d + h * h);
			foreach (float s in new[] { 1f, -1f })
			{
				var down = new Vector3(0, -h, s * d).normalized;
				var rot = Quaternion.LookRotation(down, Vector3.up);
				var normal = rot * Vector3.up;
				var centre = new Vector3(x, baseY + h * 0.5f, -D * 0.5f + s * d * 0.5f) + down * (overhang * 0.5f) + normal * (thick * 0.5f);
				Box(t, BaseRoof, centre, new Vector3(W + overhang * 2f, thick, slope + overhang), rot);
			}
			Box(t, BaseRoof, new Vector3(x, baseY + h + 0.05f, -D * 0.5f), new Vector3(W + overhang * 2f, 0.12f, 0.3f));
		}

		internal static void Porch(GameObject t, float x, float w, float depth, float floorY, float roofY, int columns)
		{
			Box(t, _foundation, new Vector3(x, (floorY - 0.15f) * 0.5f, depth * 0.5f), new Vector3(w, floorY - 0.15f, depth));
			Box(t, _porch, new Vector3(x, floorY - 0.075f, depth * 0.5f), new Vector3(w + 0.1f, 0.15f, depth + 0.05f));
			// Two steps.
			float stepH = floorY / 3f;
			Box(t, _porch, new Vector3(x, stepH, depth + 0.175f), new Vector3(1.6f, stepH * 2f, 0.35f));
			Box(t, _porch, new Vector3(x, stepH * 0.5f, depth + 0.5f), new Vector3(1.6f, stepH, 0.3f));
			for (int i = 0; i < columns; i++)
			{
				float cx = x - w * 0.5f + 0.15f + i * (w - 0.3f) / (columns - 1);
				Box(t, _trim, new Vector3(cx, (floorY + roofY) * 0.5f, depth - 0.15f), new Vector3(0.18f, roofY - floorY, 0.18f));
			}
			Box(t, _trim, new Vector3(x, roofY + 0.1f, depth - 0.15f), new Vector3(w + 0.1f, 0.2f, 0.22f));
			Box(t, BaseRoof, new Vector3(x, roofY + 0.25f, depth * 0.5f), new Vector3(w + 0.3f, 0.12f, depth + 0.3f), Quaternion.Euler(-6f, 0, 0));
		}

		internal static void Door(Transform face, float x)
		{
			Box(face, BaseDoor, new Vector3(x, 1.05f, 0.04f), new Vector3(1f, 2.1f, 0.08f));
			Frame(face, x, 1.05f, 1f, 2.1f);
			Box(face, _glass, new Vector3(x, 1.75f, 0.085f), new Vector3(0.5f, 0.35f, 0.02f));
		}

		internal static void Window(Transform face, float x, float y, float w, float h, bool shutters)
		{
			Box(face, _glass, new Vector3(x, y, 0.03f), new Vector3(w, h, 0.06f));
			Frame(face, x, y, w, h);
			// Muntins.
			Box(face, _trim, new Vector3(x, y, 0.065f), new Vector3(w, 0.05f, 0.02f));
			Box(face, _trim, new Vector3(x, y, 0.065f), new Vector3(0.05f, h, 0.02f));
			if (!shutters)
				return;
			foreach (float s in new[] { -1f, 1f })
				Box(face, BaseDoor, new Vector3(x + s * (w * 0.5f + 0.12f + w * 0.2f), y, 0.04f), new Vector3(w * 0.4f, h + 0.1f, 0.05f));
		}

		internal static void Frame(Transform face, float x, float y, float w, float h)
		{
			Box(face, _trim, new Vector3(x, y + h * 0.5f + 0.06f, 0.05f), new Vector3(w + 0.24f, 0.12f, 0.1f));
			Box(face, _trim, new Vector3(x, y - h * 0.5f - 0.05f, 0.06f), new Vector3(w + 0.2f, 0.1f, 0.12f));
			foreach (float s in new[] { -1f, 1f })
				Box(face, _trim, new Vector3(x + s * (w * 0.5f + 0.05f), y, 0.05f), new Vector3(0.1f, h, 0.1f));
		}

		// One window on each side wall (the gaps between houses show the sides).
		internal static void SideWindows(GameObject t, float W, float D, float found, float y)
		{
			foreach (float s in new[] { -1f, 1f })
			{
				var side = Face(t, new Vector3(s * W * 0.5f, found, -D * 0.5f), s * 90f);
				Window(side, 0f, y, 1f, 1.3f, shutters: false);
			}
		}

		// Back wall (z = −D): two windows, optionally a back door with a small stoop between them. The Backyards level
		// shows the houses from behind.
		internal static void BackWall(GameObject t, float W, float D, float found, float y, bool door)
		{
			var back = Face(t, new Vector3(0, found, -D), 180f);
			Window(back, -W * 0.3f, y, 1.1f, 1.3f, shutters: false);
			Window(back, W * 0.3f, y, 1.1f, 1.3f, shutters: false);
			if (!door)
				return;
			Door(back, 0f);
			Box(back, _porch, new Vector3(0, -found * 0.5f, 0.6f), new Vector3(1.8f, found, 1.2f));
		}

		internal static void Chimney(GameObject t, float x, float z, float baseY, float roofH)
		{
			float top = baseY + roofH + 0.7f;
			Box(t, _brick, new Vector3(x, (baseY + top) * 0.5f, z), new Vector3(0.8f, top - baseY, 0.8f));
			Box(t, _foundation, new Vector3(x, top + 0.05f, z), new Vector3(0.95f, 0.1f, 0.95f));
		}

		// Child frame on a wall: local +Z = out of the wall, X = along it, y = 0 at the floor line.
		internal static Transform Face(GameObject t, Vector3 position, float yaw)
		{
			var f = new GameObject("Face").transform;
			f.SetParent(t.transform, false);
			f.SetLocalPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
			return f;
		}

		// ================================================================== primitives / bake

		internal static GameObject Temp(string name) => new(name);

		internal static void Box(GameObject t, Material m, Vector3 pos, Vector3 size, Quaternion? rot = null) =>
			Box(t.transform, m, pos, size, rot);

		internal static void Box(Transform parent, Material m, Vector3 pos, Vector3 size, Quaternion? rot = null)
		{
			var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Object.DestroyImmediate(go.GetComponent<Collider>());
			go.transform.SetParent(parent, false);
			go.transform.SetLocalPositionAndRotation(pos, rot ?? Quaternion.identity);
			go.transform.localScale = size;
			go.GetComponent<MeshRenderer>().sharedMaterial = m;
		}

		internal static Mesh _prism;

		// Triangular prism (gable): base on y = 0 at `pos`, triangle in the YZ plane, extruded along X.
		internal static void Prism(GameObject t, Material m, Vector3 pos, Vector3 size, Quaternion? rot = null)
		{
			_prism ??= BuildPrismMesh();
			var go = new GameObject("Prism");
			go.transform.SetParent(t.transform, false);
			go.transform.SetLocalPositionAndRotation(pos, rot ?? Quaternion.identity);
			go.transform.localScale = size;
			go.AddComponent<MeshFilter>().sharedMesh = _prism;
			go.AddComponent<MeshRenderer>().sharedMaterial = m;
		}

		internal static Mesh BuildPrismMesh()
		{
			var verts = new List<Vector3>();
			var normals = new List<Vector3>();
			var tris = new List<int>();
			void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n)
			{
				if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) (b, c) = (c, b);
				int i = verts.Count;
				verts.Add(a); verts.Add(b); verts.Add(c);
				normals.Add(n); normals.Add(n); normals.Add(n);
				tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
			}
			void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n) { Tri(a, b, c, n); Tri(a, c, d, n); }

			var l0 = new Vector3(-0.5f, 0, -0.5f); var l1 = new Vector3(-0.5f, 0, 0.5f); var l2 = new Vector3(-0.5f, 1, 0);
			var r0 = new Vector3(0.5f, 0, -0.5f); var r1 = new Vector3(0.5f, 0, 0.5f); var r2 = new Vector3(0.5f, 1, 0);
			Tri(l0, l1, l2, Vector3.left);
			Tri(r0, r1, r2, Vector3.right);
			Quad(l1, r1, r2, l2, new Vector3(0, 0.5f, 1f).normalized);
			Quad(l0, l2, r2, r0, new Vector3(0, 0.5f, -1f).normalized);
			Quad(l0, r0, r1, l1, Vector3.down);
			var mesh = new Mesh { name = "Prism" };
			mesh.SetVertices(verts);
			mesh.SetNormals(normals);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>
		/// Bakes every MeshRenderer under <paramref name="t"/> into one mesh (one submesh per material, in
		/// <paramref name="slots"/> order first), saves it to Meshes/<name>.asset (in place, same guid) and the prefab
		/// <name>.prefab = MeshFilter + MeshRenderer (+ <paramref name="extra"/> components).
		/// </summary>
		internal static GameObject Bake(GameObject t, string name, Material[] slots, bool castShadows, bool receiveDecals,
			System.Action<GameObject> extra = null)
		{
			var order = new List<Material>();
			if (slots != null) order.AddRange(slots);
			var groups = new Dictionary<Material, List<CombineInstance>>();
			var toLocal = t.transform.worldToLocalMatrix;
			foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
			{
				var mf = r.GetComponent<MeshFilter>();
				if (mf == null || mf.sharedMesh == null) continue;
				var m = r.sharedMaterial;
				if (!order.Contains(m)) order.Add(m);
				if (!groups.TryGetValue(m, out var list)) groups[m] = list = new List<CombineInstance>();
				list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toLocal * r.transform.localToWorldMatrix });
			}

			var subs = new List<CombineInstance>();
			var mats = new List<Material>();
			foreach (var m in order)
			{
				if (!groups.TryGetValue(m, out var list)) continue;
				var sub = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
				sub.CombineMeshes(list.ToArray(), true, true);
				if (MetricUV.Contains(m))
					PlanarUVs(sub);
				subs.Add(new CombineInstance { mesh = sub, transform = Matrix4x4.identity });
				mats.Add(m);
			}
			var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
			mesh.CombineMeshes(subs.ToArray(), false, false);
			mesh.RecalculateBounds();
			foreach (var s in subs) Object.DestroyImmediate(s.mesh);
			mesh = SaveMesh(mesh, $"{MeshFolder}/{name}.asset");

			var go = new GameObject(name);
			try
			{
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				var renderer = go.AddComponent<MeshRenderer>();
				renderer.sharedMaterials = mats.ToArray();
				renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
				renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
				if (receiveDecals)
					renderer.renderingLayerMask = _floorLayers;
				extra?.Invoke(go);
				string path = $"{PrefabFolder}/{name}.prefab";
				PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
				if (!ok)
					Debug.LogError($"{Log} Failed to save {path}");
				Debug.Log($"{Log} {name}: {mesh.vertexCount} verts, {mats.Count} materials.");
				return AssetDatabase.LoadAssetAtPath<GameObject>(path);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		// Materials whose submesh gets metric planar UVs in Bake (textured siding / roofs / foundation).
		internal static readonly HashSet<Material> MetricUV = new();

		/// <summary>
		/// Per-vertex planar UVs in metres, projected on each face's own plane (the baked cubes have flat per-face
		/// normals): U runs horizontally along the face, V up it — siding boards stay level on every wall and shingle
		/// rows run along the roof slope, at the same scale on every box whatever its size.
		/// </summary>
		internal static void PlanarUVs(Mesh mesh)
		{
			var verts = mesh.vertices;
			var normals = mesh.normals;
			if (normals == null || normals.Length != verts.Length)
			{
				mesh.RecalculateNormals();
				normals = mesh.normals;
			}
			var uvs = new Vector2[verts.Length];
			for (int i = 0; i < verts.Length; i++)
			{
				var n = normals[i];
				var u = Vector3.Cross(Vector3.up, n);
				if (u.sqrMagnitude < 1e-4f)
					u = Vector3.right; // horizontal face
				u.Normalize();
				var v = Vector3.Cross(n, u);
				uvs[i] = new Vector2(Vector3.Dot(verts[i], u), Vector3.Dot(verts[i], v));
			}
			mesh.uv = uvs;
		}

		internal static Mesh SaveMesh(Mesh mesh, string path)
		{
			var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
			if (existing == null)
			{
				AssetDatabase.CreateAsset(mesh, path);
				return mesh;
			}
			// Rewrite the geometry through the Mesh API, not EditorUtility.CopySerialized: CopySerialized updates the
			// serialized data (and the file), but an already loaded mesh keeps rendering its old geometry until the
			// editor restarts — a rebuilt level showed stale racks while their colliders were gone. Same asset/GUID.
			existing.Clear();
			existing.indexFormat = mesh.indexFormat;
			existing.SetVertices(mesh.vertices);
			if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal))
				existing.SetNormals(mesh.normals);
			if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent))
				existing.SetTangents(mesh.tangents);
			if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
				existing.SetColors(mesh.colors);
			var uvs = new System.Collections.Generic.List<Vector4>();
			for (int ch = 0; ch < 8; ch++)
			{
				if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + ch))
					continue;
				mesh.GetUVs(ch, uvs);
				existing.SetUVs(ch, uvs);
			}
			existing.subMeshCount = mesh.subMeshCount;
			for (int i = 0; i < mesh.subMeshCount; i++)
				existing.SetTriangles(mesh.GetTriangles(i), i, false);
			existing.RecalculateBounds();
			EditorUtility.SetDirty(existing);
			Object.DestroyImmediate(mesh);
			return existing;
		}

		// ================================================================== materials

		internal static void CreateSuburbMaterials()
		{
			_grass = SMat("Suburb_Grass", new Color(0.34f, 0.56f, 0.25f), 0.12f);
			_grassAlt = SMat("Suburb_GrassAlt", new Color(0.30f, 0.51f, 0.22f), 0.12f);
			_concrete = SMat("Suburb_Sidewalk", new Color(0.73f, 0.72f, 0.69f), 0.15f);
			_joint = SMat("Suburb_SidewalkJoint", new Color(0.52f, 0.52f, 0.5f), 0.1f);
			_curb = SMat("Suburb_Curb", new Color(0.82f, 0.82f, 0.8f), 0.15f);
			_asphalt = SMat("Suburb_Asphalt", new Color(0.2f, 0.2f, 0.22f), 0.25f);
			_lineYellow = SMat("Suburb_LineYellow", new Color(0.95f, 0.76f, 0.12f), 0.3f);
			_drive = SMat("Suburb_Driveway", new Color(0.66f, 0.65f, 0.62f), 0.15f);
			_soil = SMat("Suburb_Soil", new Color(0.3f, 0.24f, 0.18f), 0.05f);

			_trim = SMat("Suburb_Trim", new Color(0.97f, 0.97f, 0.95f), 0.35f);
			_glass = SMat("Suburb_Glass", new Color(0.11f, 0.15f, 0.21f), 0.9f);
			_porch = SMat("Suburb_PorchWood", new Color(0.55f, 0.45f, 0.35f), 0.2f);
			_foundation = SMat("Suburb_Foundation", new Color(0.56f, 0.55f, 0.53f), 0.1f);
			_brick = SMat("Suburb_Brick", new Color(0.56f, 0.26f, 0.19f), 0.1f);
			_fence = SMat("Suburb_FenceWhite", new Color(0.96f, 0.96f, 0.94f), 0.3f);
			_hedge = SMat("Suburb_Hedge", new Color(0.15f, 0.33f, 0.13f), 0.08f);
			_hedgeTop = SMat("Suburb_HedgeTop", new Color(0.22f, 0.43f, 0.17f), 0.08f);

			_sidingCream = SMat("Suburb_Siding_Cream", new Color(0.93f, 0.88f, 0.72f), 0.25f);
			_sidingBlue = SMat("Suburb_Siding_SkyBlue", new Color(0.68f, 0.8f, 0.88f), 0.25f);
			_sidingSage = SMat("Suburb_Siding_Sage", new Color(0.66f, 0.74f, 0.6f), 0.25f);
			_sidingBeige = SMat("Suburb_Siding_Beige", new Color(0.82f, 0.74f, 0.62f), 0.25f);
			_sidingGrey = SMat("Suburb_Siding_Grey", new Color(0.66f, 0.68f, 0.7f), 0.25f);
			_sidingWhite = SMat("Suburb_Siding_White", new Color(0.94f, 0.94f, 0.92f), 0.25f);
			_sidingYellow = SMat("Suburb_Siding_Yellow", new Color(0.96f, 0.86f, 0.52f), 0.25f);

			_roofCharcoal = SMat("Suburb_Roof_Charcoal", new Color(0.22f, 0.22f, 0.24f), 0.15f);
			_roofBrown = SMat("Suburb_Roof_Brown", new Color(0.36f, 0.27f, 0.22f), 0.15f);
			_roofSlate = SMat("Suburb_Roof_Slate", new Color(0.3f, 0.33f, 0.38f), 0.15f);

			_doorRed = SMat("Suburb_Door_Red", new Color(0.58f, 0.12f, 0.1f), 0.4f);
			_doorNavy = SMat("Suburb_Door_Navy", new Color(0.12f, 0.18f, 0.35f), 0.4f);
			_doorGreen = SMat("Suburb_Door_Green", new Color(0.14f, 0.32f, 0.2f), 0.4f);
			_doorBlack = SMat("Suburb_Door_Black", new Color(0.08f, 0.08f, 0.09f), 0.4f);

			TextureHouses();
		}

		// Walls, roofs and foundations: small greyscale textures (Assets/Textures, 256), tinted so each material keeps
		// its old average colour; the meshes get metric UVs (PlanarUVs), tiling = 1 / metres per tile.
		// Lap siding: 2 m tile (0.25 m boards); panels: 1.2 m (0.6 m bands); stucco: 1.5 m; shingles: 2 m (0.25 m rows).
		private static void TextureHouses()
		{
			MetricUV.Clear();
			void Tex(Material m, string path, Color average, float metresPerTile)
			{
				LevelTextures.Apply(m, path, LevelTextures.TintFor(path, average), 1f / metresPerTile);
				MetricUV.Add(m);
			}
			Tex(_sidingWhite, LevelTextures.SidingLap, new Color(0.94f, 0.94f, 0.92f), 2f);
			Tex(_sidingCream, LevelTextures.SidingLap, new Color(0.93f, 0.88f, 0.72f), 2f);
			Tex(_sidingSage, LevelTextures.SidingLap, new Color(0.66f, 0.74f, 0.6f), 2f);
			Tex(_sidingYellow, LevelTextures.SidingLap, new Color(0.96f, 0.86f, 0.52f), 2f);
			Tex(_sidingBlue, LevelTextures.SidingPanel, new Color(0.68f, 0.8f, 0.88f), 1.2f);
			Tex(_sidingGrey, LevelTextures.SidingPanel, new Color(0.66f, 0.68f, 0.7f), 1.2f);
			Tex(_sidingBeige, LevelTextures.Stucco, new Color(0.82f, 0.74f, 0.62f), 1.5f);
			Tex(_foundation, LevelTextures.Stucco, new Color(0.56f, 0.55f, 0.53f), 1.5f);
			Tex(_roofCharcoal, LevelTextures.RoofShingles, new Color(0.22f, 0.22f, 0.24f), 2f);
			Tex(_roofBrown, LevelTextures.RoofShingles, new Color(0.36f, 0.27f, 0.22f), 2f);
			Tex(_roofSlate, LevelTextures.RoofShingles, new Color(0.3f, 0.33f, 0.38f), 2f);
		}

		// Lit See-Through (URP Lit + hole around the player), colour/smoothness enforced on every run.
		internal static Material SMat(string name, Color color, float smoothness)
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
			m.SetFloat("_Metallic", 0f);
			m.SetFloat("_Smoothness", smoothness);
			m.enableInstancing = true;
			EditorUtility.SetDirty(m);
			return m;
		}
	}
}
