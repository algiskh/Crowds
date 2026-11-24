using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using static Crowds.EditorTools.KeyLevelBuilder;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Shared pieces of the arena-style campaign levels (Baseball, Mall): flat ground dressing painted into one
	/// decal-receiving mesh (<see cref="GroundPainter"/>), free-angle walls that stop bullets, mob doors (dark
	/// vestibules on the MobOnly navmesh area — mobs walk out, the player can't walk in), quota waves / breathers,
	/// level root wiring and the menu definition. Uses the KeyLevelBuilder helpers.
	/// </summary>
	internal static class VenueKit
	{
		internal const string LevelFolder = "Assets/_Prefabs/Levels";
		internal const string MeshFolder = LevelFolder + "/Meshes";
		internal const string ConfigFolder = "Assets/_Data/Levels/Configs";
		internal const string DefinitionFolder = "Assets/_Data/Levels/Definitions";
		private const string MobOnlyArea = "MobOnly";

		private static Material _vestibule;

		internal static void Init()
		{
			EnsureFolder(MaterialFolder);
			EnsureFolder(MeshFolder);
			EnsureFolder(ConditionFolder);
			CreateMaterials();
			if (AssetDatabase.LoadAssetAtPath<GameObject>(WallSparkPrefabPath) == null)
				BuildWallSparkEffect();
			_vestibule = Mat("Venue_VestibuleBlack", new Color(0.015f, 0.015f, 0.017f), 0f, 0f);
		}

		// ================================================================== stages

		internal static DifficultyStage Quota(DifficultyLevel level, float rest, float interSpawn, SmartConditionWrapper end,
			string objective, params (string id, int count)[] quota)
		{
			var entries = new MobQuotaEntry[quota.Length];
			for (int i = 0; i < quota.Length; i++)
				entries[i] = new MobQuotaEntry { MobId = quota[i].id, Count = quota[i].count };
			return new DifficultyStage
			{
				DifficultyLevel = level,
				DifficultyTimer = Mathf.Max(1f, rest), // only the rest window; the quota + MobsCleared end the wave
				SpeedMultiplier = 1f,
				InterSpawnCooldown = interSpawn,
				RestTime = rest,
				EndConditions = new[] { end },
				ShowTimer = false,
				SpawnOnlyWhileTimer = false,
				Objective = objective,
				MobQuota = entries,
			};
		}

		internal static DifficultyStage Breather(DifficultyLevel level, float seconds, string objective) => new()
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

		internal static DifficultyStage Finish() => new()
		{
			DifficultyLevel = DifficultyLevel.finish,
			SpeedMultiplier = 1f,
			InterSpawnCooldown = 0.5f,
			EndConditions = System.Array.Empty<SmartConditionWrapper>(),
			ShowTimer = false,
		};

		/// <summary>Common config: stages, ammo fallback, one sliding sector, spawn window, mob drop filters.</summary>
		internal static LevelConfig Config(string path, List<DifficultyStage> stages, Vector2 spawnDistance,
			string[] allowedWeaponDrops, string[] bannedBonuses)
		{
			var lowAmmo = Condition<NoAmmoAroundConditionWrapper>("LowAmmoNoneAround", p =>
			{
				p.FindPropertyRelative("_sameCaliberOnly").boolValue = true;
				p.FindPropertyRelative("_lowAmmoThreshold").intValue = 3;
			});
			var config = LoadOrCreate<LevelConfig>(path);
			SetField(config, "_difficultyStages", stages);
			var fallback = new AdditionalLootConfig();
			fallback.AdditionalLoot[lowAmmo] = new[] { Ammo(Caliber.None, FallbackAmmoCount) };
			SetField(config, "_AdditionalLootConfigs", new List<AdditionalLootConfig> { fallback });
			SetField(config, "_sectorMode", SectorMode.Sliding);
			SetField(config, "_activeSectorRadius", 1);
			SetField(config, "_spawnDistance", spawnDistance);
			SetField(config, "_allowedMobWeaponDrops", allowedWeaponDrops);
			SetField(config, "_bannedMobBonusIds", bannedBonuses);
			EditorUtility.SetDirty(config);
			return config;
		}

		internal static SmartConditionWrapper MobsCleared() => Condition<MobsClearedConditionWrapper>("MobsCleared", null, null);

		// ================================================================== level root

		internal sealed class Level
		{
			public GameObject Root;
			public LevelRoot LevelRoot;
			public FloorSector Sector;
			public Transform Walls, Props, Spawns, Mission, Loot, Fallback;
		}

		internal static Level Begin(string name, Rect floor, Material floorMat)
		{
			var l = new Level { Root = new GameObject(name) };
			l.LevelRoot = l.Root.AddComponent<LevelRoot>();
			var floorGo = new GameObject("Floor");
			floorGo.transform.SetParent(l.Root.transform, false);
			l.Sector = floorGo.AddComponent<FloorSector>();
			var slab = new GameObject("FloorMesh");
			slab.transform.SetParent(floorGo.transform, false);
			slab.AddComponent<MeshFilter>().sharedMesh = BuildFloorMesh(new[] { floor }, $"{MeshFolder}/{name}_Floor.asset");
			var r = slab.AddComponent<MeshRenderer>();
			r.sharedMaterial = floorMat;
			r.renderingLayerMask = FloorRenderingLayers;
			l.Walls = Child(l.Root.transform, "Walls");
			l.Props = Child(l.Root.transform, "Props");
			l.Spawns = Child(l.Root.transform, "SpawnPoints");
			l.Mission = Child(l.Root.transform, "Mission");
			l.Loot = Child(l.Root.transform, "StageLoot");
			l.Fallback = Child(l.Root.transform, "AmmoFallbackPoints");
			return l;
		}

		internal static void FallbackPoint(Level l, Vector3 pos)
		{
			var p = Child(l.Fallback, $"AmmoPoint_{l.Fallback.childCount + 1}");
			p.position = pos;
			p.gameObject.tag = AdditionalSpawnTag;
		}

		internal static GameObject Finish(Level l, LevelConfig config, Vector3 start, float startYaw, string prefabPath)
		{
			try
			{
				var startT = Child(l.Mission, "PlayerStart");
				startT.SetPositionAndRotation(start, Quaternion.Euler(0f, startYaw, 0f));
				var so = new SerializedObject(l.LevelRoot);
				so.FindProperty("_levelConfig").objectReferenceValue = config;
				var sectors = so.FindProperty("_sectors");
				sectors.arraySize = 1;
				sectors.GetArrayElementAtIndex(0).objectReferenceValue = l.Sector;
				so.FindProperty("_playerStart").objectReferenceValue = startT;
				so.ApplyModifiedPropertiesWithoutUndo();
				var saved = PrefabUtility.SaveAsPrefabAsset(l.Root, prefabPath, out bool ok);
				if (!ok)
					Debug.LogError($"[VenueKit] Failed to save {prefabPath}");
				return saved;
			}
			finally
			{
				Object.DestroyImmediate(l.Root);
			}
		}

		internal static void Definition(GameObject prefab, string assetName, string displayName)
		{
			if (prefab == null)
				return;
			var definition = LoadOrCreate<LevelDefinition>($"{DefinitionFolder}/{assetName}.asset");
			var so = new SerializedObject(definition);
			so.FindProperty("_levelPrefab").objectReferenceValue = prefab;
			so.FindProperty("_displayName").stringValue = displayName;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(definition);
		}

		// ================================================================== walls / doors

		/// <summary>Straight wall from a to b (XZ), any angle; stops bullets (sparks). Ends overlap a little.</summary>
		internal static GameObject WallSeg(Transform parent, string name, Vector2 a, Vector2 b, float height, Material mat,
			Material cap, float thickness = WallThickness)
		{
			var d = b - a;
			float len = d.magnitude + thickness * 0.5f;
			var rot = Quaternion.LookRotation(new Vector3(d.x, 0f, d.y));
			var mid = (a + b) * 0.5f;
			var go = Primitive(PrimitiveType.Cube, name, parent, mat, new Vector3(mid.x, FloorY + height * 0.5f, mid.y), rot,
				new Vector3(thickness, height, len));
			SparkOnHit(go.AddComponent<BulletBlocker>());
			if (cap != null)
				Primitive(PrimitiveType.Cube, "Cap", go.transform, cap, new Vector3(0, 0.5f + 0.04f / height, 0), Quaternion.identity,
					new Vector3(1.06f, 0.08f / height, 1f));
			return go;
		}

		/// <summary>Wall a→b with gaps of <paramref name="gapWidth"/> centred at the given distances along it.</summary>
		internal static void WallWithGaps(Transform parent, string name, Vector2 a, Vector2 b, float height, Material mat,
			Material cap, float gapWidth, params float[] gapsAt)
		{
			float len = (b - a).magnitude;
			var dir = (b - a) / len;
			var cuts = new List<float>(gapsAt);
			cuts.Sort();
			float cursor = 0f;
			int n = 0;
			foreach (float g in cuts)
			{
				float g0 = g - gapWidth * 0.5f;
				if (g0 > cursor + 0.05f)
					WallSeg(parent, $"{name}_{++n}", a + dir * cursor, a + dir * g0, height, mat, cap);
				cursor = g + gapWidth * 0.5f;
			}
			if (len > cursor + 0.05f)
				WallSeg(parent, $"{name}_{++n}", a + dir * cursor, b, height, mat, cap);
		}

		/// <summary>
		/// Mob door in a wall: a dark vestibule outside the wall line (floor on the MobOnly navmesh area — mobs walk
		/// out, the player's agent excludes it), black sides/back/ceiling, a portal frame on the inner face. The wall
		/// itself must leave a gap of <paramref name="width"/> here. Returns the spawn point position (deep inside).
		/// </summary>
		internal static Vector3 MobDoor(Transform parent, string name, Vector3 wallPoint, Vector3 inward, float width,
			float depth, float height, Material frame)
		{
			var root = Child(parent, name);
			root.SetPositionAndRotation(new Vector3(wallPoint.x, FloorY, wallPoint.z), Quaternion.LookRotation(-inward));
			var id = Quaternion.identity;
			float z0 = -WallThickness * 0.5f, z1 = depth;
			float zc = (z0 + z1) * 0.5f, len = z1 - z0;

			var floor = Primitive(PrimitiveType.Cube, "Floor", root, _vestibule, new Vector3(0, 0.01f, zc), id, new Vector3(width, 0.02f, len));
			var modifier = floor.AddComponent<NavMeshModifier>();
			modifier.overrideArea = true;
			int area = UnityEngine.AI.NavMesh.GetAreaFromName(MobOnlyArea);
			if (area < 0)
			{
				Debug.LogWarning("[VenueKit] Nav area 'MobOnly' missing — run Build Hockey Level once (it creates it); using 3.");
				area = 3;
			}
			modifier.area = area;
			foreach (float s in new[] { -1f, 1f })
				Primitive(PrimitiveType.Cube, "Side", root, _vestibule, new Vector3(s * (width * 0.5f + 0.1f), height * 0.5f, zc + 0.2f), id,
					new Vector3(0.2f, height, len - 0.4f));
			var back = Primitive(PrimitiveType.Cube, "Back", root, _vestibule, new Vector3(0, height * 0.5f, z1), id,
				new Vector3(width + 0.4f, height, 0.2f));
			back.AddComponent<BulletBlocker>();
			Primitive(PrimitiveType.Cube, "Ceiling", root, _vestibule, new Vector3(0, height + 0.05f, zc + 0.2f), id,
				new Vector3(width + 0.4f, 0.1f, len - 0.4f));
			foreach (float s in new[] { -1f, 1f })
				Primitive(PrimitiveType.Cube, "Jamb", root, frame, new Vector3(s * (width * 0.5f + 0.15f), height * 0.5f, z0 - 0.05f), id,
					new Vector3(0.3f, height, 0.3f));
			Primitive(PrimitiveType.Cube, "Lintel", root, frame, new Vector3(0, height + 0.15f, z0 - 0.05f), id,
				new Vector3(width + 0.6f, 0.3f, 0.3f));
			foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
				if (r.sharedMaterial == _vestibule)
					r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			return root.TransformPoint(new Vector3(0, 0, depth * 0.6f));
		}

		/// <summary>Solid prop root: carving obstacle (+ optional bullet blocker) over a local box; excluded from the bake.</summary>
		internal static void Solid(GameObject go, Vector3 center, Vector3 size, bool blocksBullets)
		{
			go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			var o = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
			o.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
			o.center = center;
			o.size = size;
			o.carving = true;
			o.carveOnlyStationary = true;
			if (!blocksBullets)
				return;
			var blocker = go.AddComponent<BulletBlocker>();
			SetField(blocker, "_center", center);
			SetField(blocker, "_size", size);
			SparkOnHit(blocker);
		}

		internal static Vector3 V(float x, float z) => new(x, FloorY, z);

		internal static Vector2 Dir(float degrees)
		{
			float r = degrees * Mathf.Deg2Rad;
			return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
		}

		internal static Vector3 To3(Vector2 p) => new(p.x, FloorY, p.y);

		// ================================================================== ground painter

		/// <summary>
		/// Flat ground dressing (lines, dirt, paint) collected per material and baked into one mesh (one submesh per
		/// material) lying on the floor: visual only (ignored by the bake), receives decals. Heights are metres above
		/// the floor top; keep overlapping layers ≥ 1 cm apart (closer ones z-fight and the decals flicker).
		/// All polygons must be convex.
		/// </summary>
		internal sealed class GroundPainter
		{
			private readonly List<Material> _order = new();
			private readonly Dictionary<Material, (List<Vector3> v, List<int> t)> _parts = new();

			private (List<Vector3> v, List<int> t) Part(Material m)
			{
				if (!_parts.TryGetValue(m, out var p))
				{
					p = (new List<Vector3>(), new List<int>());
					_parts[m] = p;
					_order.Add(m);
				}
				return p;
			}

			public void Poly(Material m, float h, IList<Vector2> pts)
			{
				var (v, t) = Part(m);
				int b = v.Count;
				foreach (var p in pts)
					v.Add(new Vector3(p.x, FloorY + h, p.y));
				for (int i = 1; i + 1 < pts.Count; i++)
				{
					var a = v[b];
					var c1 = v[b + i];
					var c2 = v[b + i + 1];
					// Front face up: cross(b−a, c−a) must point +Y.
					if (Vector3.Cross(c1 - a, c2 - a).y >= 0f)
					{
						t.Add(b); t.Add(b + i); t.Add(b + i + 1);
					}
					else
					{
						t.Add(b); t.Add(b + i + 1); t.Add(b + i);
					}
				}
			}

			public void Rect(Material m, float h, float x0, float x1, float z0, float z1) =>
				Poly(m, h, new[] { new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0) });

			public void Line(Material m, float h, Vector2 a, Vector2 b, float width)
			{
				var d = (b - a).normalized;
				var n = new Vector2(-d.y, d.x) * (width * 0.5f);
				Poly(m, h, new[] { a + n, b + n, b - n, a - n });
			}

			public void Disc(Material m, float h, Vector2 c, float r, int seg = 40)
			{
				var pts = new Vector2[seg];
				for (int i = 0; i < seg; i++)
					pts[i] = c + Dir(i * 360f / seg) * r;
				Poly(m, h, pts);
			}

			// Pie slice from the centre, angles in degrees (x = cos, z = sin); span must be ≤ 180°.
			public void Pie(Material m, float h, Vector2 c, float r, float a0, float a1, int seg = 32)
			{
				var pts = new List<Vector2> { c };
				for (int i = 0; i <= seg; i++)
					pts.Add(c + Dir(Mathf.Lerp(a0, a1, i / (float)seg)) * r);
				Poly(m, h, pts);
			}

			public void Band(Material m, float h, Vector2 c, float r0, float r1, float a0, float a1, int seg = 48)
			{
				for (int i = 0; i < seg; i++)
				{
					var d0 = Dir(Mathf.Lerp(a0, a1, i / (float)seg));
					var d1 = Dir(Mathf.Lerp(a0, a1, (i + 1) / (float)seg));
					Poly(m, h, new[] { c + d0 * r0, c + d0 * r1, c + d1 * r1, c + d1 * r0 });
				}
			}

			public void Ring(Material m, float h, Vector2 c, float r0, float r1, int seg = 40) => Band(m, h, c, r0, r1, 0f, 360f, seg);

			public void Build(Transform parent, string name)
			{
				var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
				var verts = new List<Vector3>();
				var subs = new List<List<int>>();
				foreach (var m in _order)
				{
					var (v, t) = _parts[m];
					int b = verts.Count;
					verts.AddRange(v);
					var tris = new List<int>(t.Count);
					foreach (int i in t)
						tris.Add(i + b);
					subs.Add(tris);
				}
				mesh.SetVertices(verts);
				var normals = new Vector3[verts.Count];
				var uvs = new Vector2[verts.Count];
				for (int i = 0; i < verts.Count; i++)
				{
					normals[i] = Vector3.up;
					uvs[i] = new Vector2(verts[i].x, verts[i].z) * 0.25f;
				}
				mesh.normals = normals;
				mesh.uv = uvs;
				mesh.subMeshCount = subs.Count;
				for (int i = 0; i < subs.Count; i++)
					mesh.SetTriangles(subs[i], i);
				mesh.RecalculateBounds();

				string path = $"{MeshFolder}/{name}.asset";
				mesh = SuburbBuilder.SaveMesh(mesh, path);   // in place: loaded instances see the new geometry

				var go = new GameObject(name);
				go.transform.SetParent(parent, false);
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				var r = go.AddComponent<MeshRenderer>();
				r.sharedMaterials = _order.ToArray();
				r.renderingLayerMask = FloorRenderingLayers;
				r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
				go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			}
		}

		/// <summary>Stepped bleachers along a→b, rising away from the field (<paramref name="outward"/>); visual only.</summary>
		internal static void Bleachers(Transform parent, Vector2 a, Vector2 b, Vector2 outward, int tiers, Material seatA,
			Material seatB, Material riser)
		{
			var root = Child(parent, "Bleachers");
			root.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
			var d = b - a;
			var rot = Quaternion.LookRotation(new Vector3(d.x, 0f, d.y));
			for (int i = 0; i < tiers; i++)
			{
				var mid = (a + b) * 0.5f + outward * (0.45f + i * 0.9f);
				float top = 0.5f + i * 0.45f;
				Primitive(PrimitiveType.Cube, "Riser", root, riser, new Vector3(mid.x, FloorY + top * 0.5f, mid.y), rot,
					new Vector3(0.9f, top, d.magnitude));
				Primitive(PrimitiveType.Cube, "Seat", root, i % 2 == 0 ? seatA : seatB,
					new Vector3(mid.x, FloorY + top + 0.04f, mid.y), rot, new Vector3(0.7f, 0.08f, d.magnitude - 0.2f));
			}
		}

		/// <summary>
		/// Bakes every renderer under <paramref name="temp"/> into one mesh (one submesh per material), saved as
		/// MeshFolder/&lt;name&gt;.asset (rewritten in place), placed under <paramref name="parent"/>; destroys temp.
		/// Excluded from the navmesh bake (solidity comes from explicit obstacles).
		/// </summary>
		internal static GameObject Combine(GameObject temp, Transform parent, string name, bool castShadows)
		{
			try
			{
				var order = new List<Material>();
				var groups = new Dictionary<Material, List<CombineInstance>>();
				var toLocal = temp.transform.worldToLocalMatrix;
				foreach (var r in temp.GetComponentsInChildren<MeshRenderer>())
				{
					var mf = r.GetComponent<MeshFilter>();
					if (mf == null || mf.sharedMesh == null)
						continue;
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
				foreach (var s in subs)
					Object.DestroyImmediate(s.mesh);

				string path = $"{MeshFolder}/{name}.asset";
				mesh = SuburbBuilder.SaveMesh(mesh, path);   // in place: loaded instances see the new geometry

				var go = new GameObject(name);
				go.transform.SetParent(parent, false);
				go.transform.SetPositionAndRotation(temp.transform.position, temp.transform.rotation);
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				var renderer = go.AddComponent<MeshRenderer>();
				renderer.sharedMaterials = order.ToArray();
				renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
				renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
				go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
				return go;
			}
			finally
			{
				Object.DestroyImmediate(temp);
			}
		}

		internal static void EnsureLevelFolders() => EnsureFolder(Path.GetDirectoryName(MeshFolder + "/x").Replace('\\', '/'));
	}
}
