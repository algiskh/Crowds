using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Builds the primitive-style helmet prefabs (Assets/_Prefabs/Headwear) and wires them into the mob configs.
	/// Tools ▸ Crowds ▸ Build Helmet Models. Safe to re-run: an existing prefab keeps its root and Headwear component
	/// (offset/extra scale tweaks survive), only the children are rebuilt; meshes are rewritten in place (same guid);
	/// materials are created once and never overwritten.
	///
	///  • HardHat_Construction — orange construction hard hat: dome with a centre ridge and a brim with a front peak.
	///    → new MobConfig "ToxicHelmet" (copy of Toxic, x2 HP), created if missing and added to MobConfigHolder.
	///  • Helmet_PASGT — sand PASGT ("Fritz"): deep dome cut higher over the brow, lower over the ears/nape, flared rim.
	///    → MobConfig "GunnerMob".
	///
	/// Convention (Headwear): root = Head bone pivot, +Y toward the head top, +Z = face forward, metres for a 0.2 m
	/// Head→HeadTop distance (Mixamo adult). See Docs/HelmetFeature.md.
	/// </summary>
	public static class HelmetModelBuilder
	{
		private const string PrefabFolder = "Assets/_Prefabs/Headwear";
		private const string MeshFolder = PrefabFolder + "/Meshes";
		private const string MaterialFolder = "Assets/_Materials/Headwear";
		private const string MobFolder = "Assets/_Data/Mobs";
		private const string MobHolderPath = "Assets/_Data/MobConfigHolder.asset";

		private const string ToxicSource = "Toxic";
		private const string ToxicHelmetId = "ToxicHelmet";

		[MenuItem("Tools/Crowds/Build Helmet Models")]
		public static void BuildAll()
		{
			EnsureFolder(PrefabFolder);
			EnsureFolder(MeshFolder);
			EnsureFolder(MaterialFolder);

			var orange = GetOrCreateMaterial("Helmet_SafetyOrange", new Color(1f, 0.42f, 0.05f), 0f, 0.62f);
			var orangeDark = GetOrCreateMaterial("Helmet_SafetyOrangeDark", new Color(0.82f, 0.30f, 0.03f), 0f, 0.5f);
			var sand = GetOrCreateMaterial("Helmet_Sand", new Color(0.76f, 0.66f, 0.47f), 0f, 0.12f);
			var strap = GetOrCreateMaterial("Helmet_Strap", new Color(0.33f, 0.29f, 0.20f), 0f, 0.1f);

			// Unit shells (radius 1); parts scale them into ellipsoids.
			var hardHatDome = SaveMesh("HardHat_Dome", BuildShell(frontMaxDeg: 90f, backMaxDeg: 90f, flare: 0f, thickness: 0.07f));
			var pasgtDome = SaveMesh("PASGT_Dome", BuildShell(frontMaxDeg: 78f, backMaxDeg: 114f, flare: 0.10f, thickness: 0.07f));
			// Cover band: a strip of constant width just above the PASGT rim line (same flare so it hugs the shell).
			var pasgtBand = SaveMesh("PASGT_Band", BuildShell(frontMaxDeg: 72f, backMaxDeg: 108f, flare: 0.10f, thickness: 0.03f, bandWidthDeg: 9f));

			// --- Construction hard hat ---
			var hardHat = BuildPrefab("HardHat_Construction", root =>
			{
				// Dome: rim at ~forehead height (0.115), top slightly above the head top.
				MeshPart("Dome", root, hardHatDome, orange, V(0, 0.115f, 0.008f), Quaternion.identity, V(0.118f, 0.108f, 0.132f));
				// Brim: thin ellipse, longer at the front (peak), dome hides its middle.
				Primitive("Brim", root, PrimitiveType.Cylinder, orange, V(0, 0.117f, 0.03f), Quaternion.identity, V(0.27f, 0.004f, 0.33f));
				// Raised centre ridge + two side ribs: narrow copies of the dome slightly taller than it, so only a crest
				// following the curvature sticks out (the rest stays inside the dome).
				MeshPart("Ridge", root, hardHatDome, orangeDark, V(0, 0.115f, 0.008f), Quaternion.identity, V(0.028f, 0.116f, 0.14f));
				MeshPart("RibL", root, hardHatDome, orangeDark, V(0, 0.115f, 0.008f), Quaternion.Euler(0, 0, 27f), V(0.016f, 0.115f, 0.138f));
				MeshPart("RibR", root, hardHatDome, orangeDark, V(0, 0.115f, 0.008f), Quaternion.Euler(0, 0, -27f), V(0.016f, 0.115f, 0.138f));
			});

			// --- PASGT ---
			var pasgt = BuildPrefab("Helmet_PASGT", root =>
			{
				MeshPart("Shell", root, pasgtDome, sand, V(0, 0.108f, 0.0f), Quaternion.identity, V(0.124f, 0.112f, 0.138f));
				// Slightly darker cover band just above the brow line (helmet-cover elastic band).
				MeshPart("CoverBand", root, pasgtBand, strap, V(0, 0.108f, 0.0f), Quaternion.identity, V(0.127f, 0.115f, 0.141f));
				// Chin-strap ends on both sides (the strap itself would clip through the jaw in animations).
				Primitive("StrapL", root, PrimitiveType.Cube, strap, V(-0.118f, 0.07f, 0.01f), Quaternion.Euler(0, 0, 8f), V(0.008f, 0.05f, 0.018f));
				Primitive("StrapR", root, PrimitiveType.Cube, strap, V(0.118f, 0.07f, 0.01f), Quaternion.Euler(0, 0, -8f), V(0.008f, 0.05f, 0.018f));
			});

			// --- Configs ---
			if (pasgt != null)
				AssignHeadwear("GunnerMob", pasgt);
			if (hardHat != null)
			{
				var toxicHelmet = EnsureToxicHelmetConfig();
				if (toxicHelmet != null)
					AssignHeadwear(ToxicHelmetId, hardHat);
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log("[HelmetModelBuilder] Helmet models built.");
		}

		// ------------------------------------------------------------------ configs

		/// <summary>Copies Toxic → ToxicHelmet (x2 HP) once and registers it in MobConfigHolder.</summary>
		private static MobConfig EnsureToxicHelmetConfig()
		{
			string path = $"{MobFolder}/{ToxicHelmetId}.asset";
			var config = AssetDatabase.LoadAssetAtPath<MobConfig>(path);
			if (config == null)
			{
				string source = $"{MobFolder}/{ToxicSource}.asset";
				var toxic = AssetDatabase.LoadAssetAtPath<MobConfig>(source);
				if (toxic == null)
				{
					Debug.LogError($"[HelmetModelBuilder] Source mob config not found: {source}");
					return null;
				}
				if (!AssetDatabase.CopyAsset(source, path))
				{
					Debug.LogError($"[HelmetModelBuilder] Can't copy {source} → {path}");
					return null;
				}
				config = AssetDatabase.LoadAssetAtPath<MobConfig>(path);
				var so = new SerializedObject(config);
				so.FindProperty("_id").stringValue = ToxicHelmetId;
				so.FindProperty("_health").floatValue = toxic.Health * 2f;
				so.ApplyModifiedPropertiesWithoutUndo();
				EditorUtility.SetDirty(config);
				Debug.Log($"[HelmetModelBuilder] Created {path} (HP {toxic.Health * 2f}).");
			}

			var holder = AssetDatabase.LoadAssetAtPath<MobConfigHolder>(MobHolderPath);
			if (holder == null)
			{
				Debug.LogWarning($"[HelmetModelBuilder] {MobHolderPath} not found — add {ToxicHelmetId} to the holder by hand.");
				return config;
			}
			var hso = new SerializedObject(holder);
			var list = hso.FindProperty("_mobConfigs");
			for (int i = 0; i < list.arraySize; i++)
				if (list.GetArrayElementAtIndex(i).objectReferenceValue == config)
					return config;
			list.arraySize++;
			list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = config;
			hso.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(holder);
			return config;
		}

		private static void AssignHeadwear(string configName, Headwear headwear)
		{
			string path = $"{MobFolder}/{configName}.asset";
			var config = AssetDatabase.LoadAssetAtPath<MobConfig>(path);
			if (config == null)
			{
				Debug.LogWarning($"[HelmetModelBuilder] MobConfig not found: {path}");
				return;
			}
			var so = new SerializedObject(config);
			var prop = so.FindProperty("_headwear");
			if (prop.objectReferenceValue == headwear)
				return;
			prop.objectReferenceValue = headwear;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(config);
		}

		// ------------------------------------------------------------------ prefabs

		private static Headwear BuildPrefab(string name, System.Action<Transform> buildParts)
		{
			string path = $"{PrefabFolder}/{name}.prefab";
			bool exists = File.Exists(path);
			var root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject(name);
			try
			{
				for (int i = root.transform.childCount - 1; i >= 0; i--)
					Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

				if (root.GetComponent<Headwear>() == null)
					root.AddComponent<Headwear>();

				buildParts(root.transform);

				PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
				if (!success)
				{
					Debug.LogError($"[HelmetModelBuilder] Failed to save {path}");
					return null;
				}
			}
			finally
			{
				if (exists)
					PrefabUtility.UnloadPrefabContents(root);
				else
					Object.DestroyImmediate(root);
			}

			var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			return asset != null ? asset.GetComponent<Headwear>() : null;
		}

		private static void Primitive(string name, Transform parent, PrimitiveType type, Material material,
			Vector3 position, Quaternion rotation, Vector3 size)
		{
			var go = GameObject.CreatePrimitive(type);
			go.name = name;
			Object.DestroyImmediate(go.GetComponent<Collider>());
			var t = go.transform;
			t.SetParent(parent, false);
			t.SetLocalPositionAndRotation(position, rotation);
			// Unity's cylinder is 2 units tall along Y: halve Y so "size" is the real height.
			t.localScale = type == PrimitiveType.Cylinder ? new Vector3(size.x, size.y * 0.5f, size.z) : size;
			ConfigureRenderer(go.GetComponent<MeshRenderer>(), material);
		}

		private static void MeshPart(string name, Transform parent, Mesh mesh, Material material,
			Vector3 position, Quaternion rotation, Vector3 scale)
		{
			var go = new GameObject(name);
			var t = go.transform;
			t.SetParent(parent, false);
			t.SetLocalPositionAndRotation(position, rotation);
			t.localScale = scale;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			ConfigureRenderer(go.AddComponent<MeshRenderer>(), material);
		}

		private static void ConfigureRenderer(MeshRenderer renderer, Material material)
		{
			renderer.sharedMaterial = material;
			// Small props on hundreds of mobs: cast shadows are fine, light probes/occlusion aren't needed.
			renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
			renderer.allowOcclusionWhenDynamic = false;
		}

		// ------------------------------------------------------------------ meshes

		/// <summary>
		/// Unit helmet shell around the origin: a spherical cap from the top (polar 0°) down to a rim whose polar
		/// angle varies with the azimuth — <paramref name="frontMaxDeg"/> at +Z (brow), <paramref name="backMaxDeg"/>
		/// at -Z (nape). <paramref name="flare"/> pushes the lower part outward (PASGT skirt). Outer + inner surface
		/// (thickness in unit radius) + a rim strip, so it's solid from any angle. <paramref name="bandWidthDeg"/> &gt; 0 makes an open band
		/// only that many degrees tall above the rim (top cut away) — used for the PASGT cover band.
		/// </summary>
		private static Mesh BuildShell(float frontMaxDeg, float backMaxDeg, float flare, float thickness,
			float bandWidthDeg = 0f)
		{
			const int rings = 18;
			const int segments = 40;

			var verts = new List<Vector3>();
			var normals = new List<Vector3>();
			var tris = new List<int>();

			float MaxPolar(float azimuth) // azimuth 0 = +Z front
			{
				float back = (1f - Mathf.Cos(azimuth)) * 0.5f; // 0 front … 1 back
				return Mathf.Lerp(frontMaxDeg, backMaxDeg, back);
			}

			Vector3 Point(float polarDeg, float azimuth, float radius, float maxPolarDeg)
			{
				float p = polarDeg * Mathf.Deg2Rad;
				float t = Mathf.InverseLerp(maxPolarDeg * 0.55f, maxPolarDeg, polarDeg);
				float r = radius * (1f + flare * t * t);
				return new Vector3(Mathf.Sin(p) * Mathf.Sin(azimuth) * r, Mathf.Cos(p) * radius, Mathf.Sin(p) * Mathf.Cos(azimuth) * r);
			}

			// Surface grid: index = ring * (segments + 1) + seg.
			int AddSurface(float radius, bool inward)
			{
				int start = verts.Count;
				for (int i = 0; i <= rings; i++)
				{
					for (int j = 0; j <= segments; j++)
					{
						float az = j / (float)segments * Mathf.PI * 2f;
						float max = MaxPolar(az);
						float min = bandWidthDeg > 0f ? max - bandWidthDeg : 0f;
						float polar = Mathf.Lerp(min, max, i / (float)rings);
						var v = Point(polar, az, radius, max);
						verts.Add(v);
						var n = v.normalized;
						normals.Add(inward ? -n : n);
					}
				}
				for (int i = 0; i < rings; i++)
				{
					for (int j = 0; j < segments; j++)
					{
						int a = start + i * (segments + 1) + j;
						int b = a + segments + 1;
						// Unity front faces are clockwise as seen by the viewer.
						if (!inward)
						{
							tris.Add(a); tris.Add(b); tris.Add(a + 1);
							tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
						}
						else
						{
							tris.Add(a); tris.Add(a + 1); tris.Add(b);
							tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
						}
					}
				}
				return start;
			}

			int outer = AddSurface(1f, inward: false);
			int inner = AddSurface(1f - thickness, inward: true);

			// Rim strip(s) between the outer and inner edge rings (own vertices → crisp edge normals).
			void AddRim(int ring, bool top)
			{
				int start = verts.Count;
				for (int j = 0; j <= segments; j++)
				{
					var o = verts[outer + ring * (segments + 1) + j];
					var n = verts[inner + ring * (segments + 1) + j];
					var normal = top ? Vector3.up : Vector3.down;
					verts.Add(o); normals.Add(normal);
					verts.Add(n); normals.Add(normal);
				}
				for (int j = 0; j < segments; j++)
				{
					int a = start + j * 2;
					if (!top)
					{
						tris.Add(a); tris.Add(a + 1); tris.Add(a + 2);
						tris.Add(a + 1); tris.Add(a + 3); tris.Add(a + 2);
					}
					else
					{
						tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
						tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
					}
				}
			}

			AddRim(rings, top: false);
			if (bandWidthDeg > 0f)
				AddRim(0, top: true);

			var mesh = new Mesh();
			mesh.SetVertices(verts);
			mesh.SetNormals(normals);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>Writes the mesh into an existing asset (keeps its guid) or creates it.</summary>
		private static Mesh SaveMesh(string name, Mesh mesh)
		{
			mesh.name = name;
			string path = $"{MeshFolder}/{name}.asset";
			var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
			if (existing == null)
			{
				AssetDatabase.CreateAsset(mesh, path);
				return mesh;
			}
			existing.Clear();
			existing.SetVertices(mesh.vertices);
			existing.SetNormals(mesh.normals);
			existing.SetTriangles(mesh.triangles, 0);
			existing.RecalculateBounds();
			EditorUtility.SetDirty(existing);
			Object.DestroyImmediate(mesh);
			return existing;
		}

		// ------------------------------------------------------------------ helpers

		private static Material GetOrCreateMaterial(string name, Color color, float metallic, float smoothness)
		{
			string path = $"{MaterialFolder}/{name}.mat";
			var material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material != null)
				return material;

			var shader = Shader.Find("Universal Render Pipeline/Lit");
			material = new Material(shader) { name = name };
			material.SetColor("_BaseColor", color);
			material.SetColor("_Color", color);
			material.SetFloat("_Metallic", metallic);
			material.SetFloat("_Smoothness", smoothness);
			material.enableInstancing = true;
			AssetDatabase.CreateAsset(material, path);
			return material;
		}

		private static void EnsureFolder(string path)
		{
			if (AssetDatabase.IsValidFolder(path))
				return;
			string parent = Path.GetDirectoryName(path).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
		}

		private static Vector3 V(float x, float y, float z) => new(x, y, z);
	}
}
