using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Builds the primitive projectile visuals: bullets per caliber (cylinder + hemispherical nose along +Z),
	/// shotgun pellets (spheres) and the M67 grenade (Assets/REFs/M67.png, without the pin).
	/// Tools ▸ Crowds ▸ Build Projectile Models. Safe to re-run.
	///
	/// Existing prefabs are edited in place (same guid → AmmoConfig / MainHolder references stay): the root keeps its
	/// Bullet/Grenade component, TrailRenderer and collider (world radius preserved), the root mesh is removed and the
	/// children are rebuilt. New per-caliber prefabs are copied from Bullet.prefab and assigned to their AmmoConfig.
	/// Materials: bullets/pellets are tracers (Assets/_Materials/Projectiles: glowing Unlit head tinted by
	/// GunConfig.BulletColor, additive trail that takes the tint too), the grenade uses the Weapon_* materials made
	/// by Build Weapon Models. Units = metres. See Docs/WeaponModels.md.
	/// </summary>
	public static class ProjectileModelBuilder
	{
		private const string PrefabFolder = "Assets/_Prefabs";
		private const string WeaponMaterialFolder = "Assets/_Materials/Weapons";
		private const string AmmoFolder = "Assets/_Data/Ammo";
		private const string BasePrefab = PrefabFolder + "/Bullet.prefab";

		// Trail head width = this × projectile diameter (never thicker than the projectile).
		private const float TrailWidthFactor = 0.9f;

		// Gun barrels are 14–22 mm thick (WeaponModelBuilder); projectiles are ~4× that so they read from the top-down
		// camera, but far below the old 0.5 m balls.
		private static IEnumerable<ProjectileSpec> Specs()
		{
			// prefab, ammo config (null = none), diameter, length (0 = sphere pellet)
			yield return new ProjectileSpec("Bullet", "556", 0.07f, 0.20f);   // 5.56 + MainHolder fallback
			yield return new ProjectileSpec("Bullet9mm", "9mm", 0.07f, 0.14f);
			yield return new ProjectileSpec("Bullet762", "762", 0.09f, 0.26f);
			yield return new ProjectileSpec("Shell", "12g", 0.12f, 0f);       // buckshot pellet
		}

		// M67: body diameter (the real one is 64 mm; enlarged ~4× for the camera like the bullets).
		private const float GrenadeDiameter = 0.24f;

		[MenuItem("Tools/Crowds/Build Projectile Models")]
		public static void BuildAll()
		{
			var (head, trail) = EnsureTracerMaterials();
			foreach (var spec in Specs())
				BuildProjectile(spec, head, trail);
			BuildGrenade();
			AssetDatabase.SaveAssets();
			Debug.Log("[ProjectileModelBuilder] Bullets, pellets and the M67 grenade rebuilt.");
		}

		// ------------------------------------------------------------------ bullets

		private static void BuildProjectile(ProjectileSpec spec, Material headMaterial, Material trailMaterial)
		{
			string path = $"{PrefabFolder}/{spec.Prefab}.prefab";
			if (!File.Exists(path))
			{
				if (!AssetDatabase.CopyAsset(BasePrefab, path))
				{
					Debug.LogError($"[ProjectileModelBuilder] Can't create {path} from {BasePrefab}.");
					return;
				}
			}

			var root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				var material = headMaterial;
				StripRootMesh(root);
				ClearChildren(root.transform);

				float d = spec.Diameter;
				if (spec.Length <= 0f)
				{
					Part(root.transform, "Pellet", PrimitiveType.Sphere, material, Vector3.zero, Vector3.zero, Vector3.one * d);
				}
				else
				{
					// Nose tip at +L/2, tail at -L/2: the root (= hit check point) is the bullet's middle.
					float l = Mathf.Max(spec.Length, d);
					float bodyLength = l - d * 0.5f;
					Part(root.transform, "Body", PrimitiveType.Cylinder, material, new Vector3(0f, 0f, -d * 0.25f),
						new Vector3(90f, 0f, 0f), new Vector3(d, bodyLength * 0.5f, d));
					Part(root.transform, "Nose", PrimitiveType.Sphere, material, new Vector3(0f, 0f, l * 0.5f - d * 0.5f),
						Vector3.zero, Vector3.one * d);
				}
				SetTrailWidth(root, d * TrailWidthFactor);
				SetupTracer(root, trailMaterial);

				PrefabUtility.SaveAsPrefabAsset(root, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}

			if (spec.AmmoConfig != null)
				AssignToAmmo(spec.AmmoConfig, AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Bullet>());
		}

		private static void AssignToAmmo(string configName, Bullet prefab)
		{
			string path = $"{AmmoFolder}/{configName}.asset";
			var config = AssetDatabase.LoadAssetAtPath<AmmoConfig>(path);
			if (config == null || prefab == null)
			{
				Debug.LogWarning($"[ProjectileModelBuilder] AmmoConfig not found: {path}");
				return;
			}
			var so = new SerializedObject(config);
			var prop = so.FindProperty("_projectilePrefab");
			if (prop.objectReferenceValue == prefab)
				return;
			prop.objectReferenceValue = prefab;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(config);
		}

		// ------------------------------------------------------------------ grenade

		private static void BuildGrenade()
		{
			string path = $"{PrefabFolder}/Grenade.prefab";
			if (!File.Exists(path))
			{
				Debug.LogError($"[ProjectileModelBuilder] {path} not found.");
				return;
			}
			var olive = LoadMaterial("Weapon_Olive");
			var aluminum = LoadMaterial("Weapon_Aluminum");
			var copper = LoadMaterial("Weapon_Brass");
			var seam = LoadMaterial("Weapon_Polymer");
			if (olive == null || aluminum == null || copper == null || seam == null)
			{
				Debug.LogError("[ProjectileModelBuilder] Weapon_* materials missing — run Tools ▸ Crowds ▸ Build Weapon Models first.");
				return;
			}

			var root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				StripRootMesh(root);
				ClearChildren(root.transform);
				var t = root.transform;
				// Proportions of a 64 mm M67 scaled to GrenadeDiameter; origin = body centre, fuze up (+Y),
				// spoon (lever) along +Z. No pin / ring.
				float k = GrenadeDiameter / 0.064f;
				Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * k;

				Part(t, "Body", PrimitiveType.Sphere, olive, Vector3.zero, Vector3.zero, Vector3.one * GrenadeDiameter);
				Part(t, "Seam", PrimitiveType.Cylinder, seam, Vector3.zero, Vector3.zero, V(0.0652f, 0.0008f, 0.0652f));
				Part(t, "Neck", PrimitiveType.Cylinder, olive, V(0, 0.032f, 0), Vector3.zero, V(0.024f, 0.0055f, 0.024f));
				Part(t, "CopperRing", PrimitiveType.Cylinder, copper, V(0, 0.0385f, 0), Vector3.zero, V(0.0255f, 0.0012f, 0.0255f));
				Part(t, "FuzeBase", PrimitiveType.Cylinder, aluminum, V(0, 0.0435f, 0), Vector3.zero, V(0.0225f, 0.004f, 0.0225f));
				Part(t, "FuzeHead", PrimitiveType.Cube, aluminum, V(0, 0.052f, 0.003f), Vector3.zero, V(0.016f, 0.0095f, 0.023f));
				Part(t, "LeverTop", PrimitiveType.Cube, olive, V(0, 0.0575f, 0.012f), Vector3.zero, V(0.0075f, 0.002f, 0.016f));
				// Spoon down the body side: from beside the fuze (y .053, z .02) to below the equator (y .005, z .0335).
				var top = new Vector3(0f, 0.053f, 0.02f);
				var bottom = new Vector3(0f, 0.005f, 0.0335f);
				var dir = bottom - top;
				Part(t, "Lever", PrimitiveType.Cube, olive, (top + bottom) * 0.5f * k,
					Quaternion.LookRotation(dir, Vector3.forward).eulerAngles, V(0.0075f, 0.002f, dir.magnitude));

				SetTrailWidth(root, GrenadeDiameter * TrailWidthFactor);
				PrefabUtility.SaveAsPrefabAsset(root, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		// ------------------------------------------------------------------ tracer

		private const string ProjectileMaterialFolder = "Assets/_Materials/Projectiles";
		// Warm tracer (player bullets keep the prefab look: GunConfig.BulletColor alpha 0). HDR → picked up by Bloom.
		private static readonly Color TracerHeadColor = new(2.6f, 1.9f, 0.7f, 1f);
		private static readonly Color TracerTrailHue = new(1f, 0.78f, 0.35f, 1f);
		// Trail material color: multiplies the trail's vertex color (the hue from the gradient / GunConfig tint).
		private static readonly Color TracerTrailIntensity = new(1.8f, 1.8f, 1.8f, 1f);
		// Tinted heads (e.g. red enemy bullets) get this HDR multiplier (Bullet._tintIntensity).
		private const float TintIntensity = 2.5f;

		/// <summary>
		/// Tracer materials (created once, then only re-tuned): the head is URP Unlit with an HDR color (glows with
		/// Bloom, tinted per gun through a MaterialPropertyBlock), the trail is URP Particles/Unlit additive — it
		/// multiplies the TrailRenderer's vertex colors, so the gradient hue (and GunConfig.BulletColor) shows up.
		/// </summary>
		private static (Material head, Material trail) EnsureTracerMaterials()
		{
			if (!AssetDatabase.IsValidFolder(ProjectileMaterialFolder))
				AssetDatabase.CreateFolder("Assets/_Materials", "Projectiles");

			var head = LoadOrCreate($"{ProjectileMaterialFolder}/Tracer_Head.mat", "Universal Render Pipeline/Unlit");
			head.SetColor("_BaseColor", TracerHeadColor);
			EditorUtility.SetDirty(head);

			var trail = LoadOrCreate($"{ProjectileMaterialFolder}/Tracer_Trail.mat", "Universal Render Pipeline/Particles/Unlit");
			trail.SetColor("_BaseColor", TracerTrailIntensity);
			// Transparent, additive, no depth write.
			trail.SetFloat("_Surface", 1f);
			trail.SetFloat("_Blend", 2f);
			trail.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); // alpha fades the additive trail
			trail.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
			trail.SetFloat("_ZWrite", 0f);
			trail.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			trail.SetOverrideTag("RenderType", "Transparent");
			trail.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
			EditorUtility.SetDirty(trail);
			return (head, trail);
		}

		private static Material LoadOrCreate(string path, string shaderName)
		{
			var material = AssetDatabase.LoadAssetAtPath<Material>(path);
			var shader = Shader.Find(shaderName);
			if (material == null)
			{
				material = new Material(shader);
				AssetDatabase.CreateAsset(material, path);
			}
			else if (shader != null && material.shader != shader)
			{
				material.shader = shader;
			}
			return material;
		}

		/// <summary>Trail: additive tracer material, warm hue fading to transparent; head tint glow.</summary>
		private static void SetupTracer(GameObject root, Material trailMaterial)
		{
			var trail = root.GetComponent<TrailRenderer>();
			if (trail != null)
			{
				trail.sharedMaterial = trailMaterial;
				trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				trail.receiveShadows = false;
				var gradient = new Gradient();
				gradient.SetKeys(
					new[] { new GradientColorKey(TracerTrailHue, 0f), new GradientColorKey(TracerTrailHue, 1f) },
					new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.3f), new GradientAlphaKey(0f, 1f) });
				trail.colorGradient = gradient;
			}
			foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
			{
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				renderer.receiveShadows = false;
			}
			var bullet = root.GetComponent<Bullet>();
			if (bullet != null)
			{
				var so = new SerializedObject(bullet);
				so.FindProperty("_tintIntensity").floatValue = TintIntensity;
				so.ApplyModifiedPropertiesWithoutUndo();
			}
		}

		// ------------------------------------------------------------------ helpers

		/// <summary>Removes the legacy root ball mesh and resets the root scale, keeping the collider's world radius.</summary>
		private static void StripRootMesh(GameObject root)
		{
			var t = root.transform;
			float scale = t.localScale.x;
			if (!Mathf.Approximately(scale, 1f))
			{
				foreach (var sphere in root.GetComponents<SphereCollider>())
				{
					sphere.radius *= scale;
					sphere.center *= scale;
				}
				t.localScale = Vector3.one;
			}
			var meshRenderer = root.GetComponent<MeshRenderer>();
			if (meshRenderer != null) Object.DestroyImmediate(meshRenderer);
			var filter = root.GetComponent<MeshFilter>();
			if (filter != null) Object.DestroyImmediate(filter);
		}

		private static void ClearChildren(Transform root)
		{
			for (int i = root.childCount - 1; i >= 0; i--)
				Object.DestroyImmediate(root.GetChild(i).gameObject);
		}

		private static void SetTrailWidth(GameObject root, float width)
		{
			var trail = root.GetComponent<TrailRenderer>();
			if (trail == null)
				return;
			trail.widthMultiplier = 1f;
			trail.widthCurve = AnimationCurve.Linear(0f, width, 1f, 0f);
		}

		private static void Part(Transform parent, string name, PrimitiveType type, Material material, Vector3 position,
			Vector3 euler, Vector3 scale)
		{
			var go = GameObject.CreatePrimitive(type);
			go.name = name;
			Object.DestroyImmediate(go.GetComponent<Collider>());
			var t = go.transform;
			t.SetParent(parent, false);
			t.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
			t.localScale = scale;
			go.GetComponent<MeshRenderer>().sharedMaterial = material;
		}

		private static Material LoadMaterial(string name) =>
			AssetDatabase.LoadAssetAtPath<Material>($"{WeaponMaterialFolder}/{name}.mat");

		private readonly struct ProjectileSpec
		{
			public readonly string Prefab;
			public readonly string AmmoConfig;
			public readonly float Diameter;
			public readonly float Length;

			public ProjectileSpec(string prefab, string ammoConfig, float diameter, float length)
			{
				Prefab = prefab;
				AmmoConfig = ammoConfig;
				Diameter = diameter;
				Length = length;
			}
		}
	}
}
