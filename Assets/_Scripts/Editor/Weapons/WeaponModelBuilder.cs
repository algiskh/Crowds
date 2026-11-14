using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Builds the primitive-style gun prefabs (Assets/_Prefabs/Weapons) from the specs below and assigns them to the
	/// GunConfigs (GunConfig.Model). Tools ▸ Crowds ▸ Build Weapon Models. Safe to re-run: an existing prefab keeps its
	/// root object and WeaponModel component (so nested instances, e.g. the Mini-14 in Gunner.prefab, keep their
	/// pose overrides) — only the children are rebuilt. Materials are created once and never overwritten.
	///
	/// Convention (WeaponModel): root = right-hand grip, barrel along +Z, +Y up, shooter's left = -X, units = metres.
	/// References: Assets/REFs. See Docs/WeaponModels.md.
	/// </summary>
	public static class WeaponModelBuilder
	{
		private const string PrefabFolder = "Assets/_Prefabs/Weapons";
		private const string MaterialFolder = "Assets/_Materials/Weapons";
		private const string ConfigFolder = "Assets/_Data/Weapons";
		private const string LegacyMobRifle = PrefabFolder + "/MobRifle.prefab";

		private enum Mat { Wood, Steel, Polymer, Parkerized, Aluminum, Brass, Olive }

		// name, base colour, metallic, smoothness
		private static readonly Dictionary<Mat, (string name, Color color, float metallic, float smoothness)> Materials = new()
		{
			{ Mat.Wood,       ("Weapon_Wood",       new Color(0.42f, 0.22f, 0.10f), 0.0f, 0.35f) },
			{ Mat.Steel,      ("Weapon_BluedSteel", new Color(0.10f, 0.11f, 0.13f), 0.7f, 0.55f) },
			{ Mat.Polymer,    ("Weapon_Polymer",    new Color(0.06f, 0.06f, 0.06f), 0.0f, 0.25f) },
			{ Mat.Parkerized, ("Weapon_Parkerized", new Color(0.30f, 0.32f, 0.30f), 0.4f, 0.30f) },
			{ Mat.Aluminum,   ("Weapon_Aluminum",   new Color(0.33f, 0.34f, 0.36f), 0.5f, 0.40f) },
			{ Mat.Brass,      ("Weapon_Brass",      new Color(0.72f, 0.55f, 0.20f), 0.8f, 0.60f) },
			{ Mat.Olive,      ("Weapon_Olive",      new Color(0.24f, 0.26f, 0.16f), 0.0f, 0.30f) },
		};

		[MenuItem("Tools/Crowds/Build Weapon Models")]
		public static void BuildAll()
		{
			EnsureFolder(PrefabFolder);
			EnsureFolder(MaterialFolder);

			// The Gunner's temporary rifle becomes the Mini-14 (same guid → Gunner.prefab and the preset keep the reference).
			string mini14Path = PrefabFolder + "/Mini14.prefab";
			if (File.Exists(LegacyMobRifle) && !File.Exists(mini14Path))
			{
				string error = AssetDatabase.MoveAsset(LegacyMobRifle, mini14Path);
				if (!string.IsNullOrEmpty(error))
					Debug.LogError($"[WeaponModelBuilder] Can't rename MobRifle → Mini14: {error}");
			}

			var materials = new Dictionary<Mat, Material>();
			foreach (var pair in Materials)
				materials[pair.Key] = GetOrCreateMaterial(pair.Value.name, pair.Value.color, pair.Value.metallic, pair.Value.smoothness);

			foreach (var spec in Specs())
			{
				var model = BuildPrefab(spec, materials);
				if (model != null && !string.IsNullOrEmpty(spec.Config))
					AssignToConfig(spec.Config, model);
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log("[WeaponModelBuilder] Weapon models built.");
		}

		// ------------------------------------------------------------------ specs

		private static IEnumerable<Spec> Specs()
		{
			// Ruger Mini-14 Ranch: wooden stock with a straight wrist, blued steel action/barrel, short 5-rd magazine.
			// Shared by the player's Rifle and the Gunner zombie (MobWeapon).
			yield return new Spec("Mini14", typeof(MobWeapon), "RifleConfig", muzzle: V(0, 0.045f, 0.775f), leftHand: V(-0.035f, -0.045f, 0.22f))
				.Box("Butt", Mat.Wood, V(0, -0.035f, -0.30f), V(0.042f, 0.13f, 0.20f), pitch: -6)
				.Box("ButtPlate", Mat.Polymer, V(0, -0.045f, -0.403f), V(0.044f, 0.13f, 0.012f), pitch: -6)
				.Box("Wrist", Mat.Wood, V(0, -0.015f, -0.10f), V(0.036f, 0.05f, 0.18f), pitch: -10)
				.Box("Stock", Mat.Wood, V(0, 0.0f, 0.17f), V(0.046f, 0.05f, 0.40f))
				.Box("StockNose", Mat.Wood, V(0, 0.005f, 0.385f), V(0.04f, 0.035f, 0.04f))
				.Box("Receiver", Mat.Steel, V(0, 0.045f, 0.07f), V(0.034f, 0.04f, 0.22f))
				.Box("BoltHandle", Mat.Steel, V(0.026f, 0.05f, 0.12f), V(0.018f, 0.01f, 0.012f))
				.Box("RearSight", Mat.Steel, V(0, 0.07f, 0.0f), V(0.012f, 0.012f, 0.02f))
				.Box("Handguard", Mat.Steel, V(0, 0.045f, 0.27f), V(0.03f, 0.025f, 0.18f))
				.Box("GasBlock", Mat.Steel, V(0, 0.028f, 0.40f), V(0.026f, 0.04f, 0.035f))
				.Cyl("Barrel", Mat.Steel, V(0, 0.045f, 0.47f), 0.018f, 0.58f)
				.Cyl("Crown", Mat.Steel, V(0, 0.045f, 0.755f), 0.022f, 0.02f)
				.Box("FrontSight", Mat.Steel, V(0, 0.062f, 0.735f), V(0.005f, 0.018f, 0.012f))
				.Box("TriggerGuard", Mat.Steel, V(0, -0.035f, 0.02f), V(0.01f, 0.025f, 0.06f))
				.Box("Magazine", Mat.Steel, V(0, -0.04f, 0.10f), V(0.026f, 0.06f, 0.045f), pitch: -8, magazine: true);

			// Colt M4 (carry-handle upper): black steel/polymer, gray aluminium 30-rd magazine.
			yield return new Spec("AR", typeof(WeaponModel), "ARConfig", muzzle: V(0, 0.05f, 0.645f), leftHand: V(-0.035f, 0.0f, 0.27f))
				.Box("Lower", Mat.Steel, V(0, 0.005f, 0.03f), V(0.032f, 0.05f, 0.20f))
				.Box("Upper", Mat.Steel, V(0, 0.05f, 0.04f), V(0.034f, 0.045f, 0.22f))
				.Box("CarryHandle", Mat.Steel, V(0, 0.097f, 0.04f), V(0.016f, 0.012f, 0.13f))
				.Box("HandleRear", Mat.Steel, V(0, 0.08f, -0.02f), V(0.018f, 0.03f, 0.02f))
				.Box("HandleFront", Mat.Steel, V(0, 0.08f, 0.10f), V(0.016f, 0.025f, 0.015f))
				.Cyl("BufferTube", Mat.Steel, V(0, 0.04f, -0.16f), 0.03f, 0.18f)
				.Box("Stock", Mat.Polymer, V(0, 0.005f, -0.29f), V(0.04f, 0.11f, 0.16f))
				.Box("PistolGrip", Mat.Polymer, V(0, -0.045f, -0.015f), V(0.03f, 0.09f, 0.035f), pitch: 20)
				.Box("TriggerGuard", Mat.Steel, V(0, -0.035f, 0.04f), V(0.01f, 0.008f, 0.06f))
				.Cyl("Handguard", Mat.Polymer, V(0, 0.05f, 0.285f), 0.05f, 0.23f)
				.Cyl("DeltaRing", Mat.Steel, V(0, 0.05f, 0.165f), 0.056f, 0.015f)
				.Cyl("Barrel", Mat.Steel, V(0, 0.05f, 0.385f), 0.016f, 0.47f)
				.Box("FrontSight", Mat.Steel, V(0, 0.085f, 0.43f), V(0.016f, 0.07f, 0.02f))
				.Cyl("FlashHider", Mat.Steel, V(0, 0.05f, 0.62f), 0.022f, 0.04f)
				.Box("Magazine", Mat.Aluminum, V(0, -0.075f, 0.085f), V(0.024f, 0.12f, 0.06f), pitch: -12, magazine: true);

			// M3 "grease gun": parkerized tube receiver, long vertical stick magazine, wire stock.
			yield return new Spec("SMG", typeof(WeaponModel), "SMGConfig", muzzle: V(0, 0.05f, 0.44f), leftHand: V(-0.035f, -0.03f, 0.15f))
				.Cyl("Receiver", Mat.Parkerized, V(0, 0.05f, 0.085f), 0.055f, 0.27f)
				.Cyl("FrontCap", Mat.Parkerized, V(0, 0.05f, 0.225f), 0.06f, 0.025f)
				.Cyl("Barrel", Mat.Parkerized, V(0, 0.05f, 0.33f), 0.018f, 0.20f)
				.Box("EjectionCover", Mat.Parkerized, V(0, 0.08f, 0.06f), V(0.03f, 0.01f, 0.09f))
				.Box("RearSight", Mat.Parkerized, V(0, 0.085f, -0.03f), V(0.008f, 0.015f, 0.01f))
				.Box("MagHousing", Mat.Parkerized, V(0, 0.0f, 0.17f), V(0.035f, 0.06f, 0.045f))
				.Box("TriggerHousing", Mat.Parkerized, V(0, 0.01f, 0.06f), V(0.03f, 0.04f, 0.16f))
				.Box("PistolGrip", Mat.Parkerized, V(0, -0.045f, -0.015f), V(0.03f, 0.10f, 0.04f), pitch: 15)
				.Box("TriggerGuard", Mat.Steel, V(0, -0.025f, 0.04f), V(0.008f, 0.008f, 0.05f))
				.Cyl("StockRodL", Mat.Steel, V(-0.015f, 0.05f, -0.165f), 0.008f, 0.23f)
				.Cyl("StockRodR", Mat.Steel, V(0.015f, 0.05f, -0.165f), 0.008f, 0.23f)
				.Box("StockButt", Mat.Steel, V(0, -0.02f, -0.28f), V(0.04f, 0.15f, 0.008f))
				.Box("Magazine", Mat.Steel, V(0, -0.14f, 0.17f), V(0.025f, 0.22f, 0.04f), magazine: true);

			// Beretta 92: black steel slide/frame, black grips. Magazine is inside the grip → nothing to hide.
			// Two-handed cup grip (Mixamo pistol clips hold it so): the left wrist sits below-left of the grip, the palm
			// wraps the right hand's fingers from the left. The point is the wrist (= mixamorig:LeftHand bone).
			yield return new Spec("Pistol", typeof(WeaponModel), "PistolConfig", muzzle: V(0, 0.04f, 0.19f), leftHand: V(-0.055f, -0.065f, -0.02f))
				.Box("Slide", Mat.Steel, V(0, 0.038f, 0.0675f), V(0.03f, 0.035f, 0.215f))
				.Cyl("BarrelTip", Mat.Steel, V(0, 0.04f, 0.178f), 0.014f, 0.012f)
				.Box("Frame", Mat.Steel, V(0, 0.008f, 0.08f), V(0.028f, 0.025f, 0.15f))
				.Box("Grip", Mat.Polymer, V(0, -0.05f, -0.015f), V(0.032f, 0.11f, 0.045f), pitch: 15)
				.Box("TriggerGuard", Mat.Steel, V(0, -0.025f, 0.05f), V(0.008f, 0.008f, 0.05f))
				.Box("TriggerGuardFront", Mat.Steel, V(0, -0.012f, 0.073f), V(0.008f, 0.025f, 0.008f))
				.Box("Hammer", Mat.Steel, V(0, 0.03f, -0.045f), V(0.01f, 0.02f, 0.012f))
				.Box("FrontSight", Mat.Steel, V(0, 0.059f, 0.165f), V(0.005f, 0.008f, 0.008f))
				.Box("RearSight", Mat.Steel, V(0, 0.059f, -0.03f), V(0.02f, 0.008f, 0.008f));

			// Remington 870: black polymer stock & pump, blued barrel + magazine tube. Tube magazine → nothing to hide.
			yield return new Spec("Shotgun", typeof(WeaponModel), "ShotgunConfig", muzzle: V(0, 0.045f, 0.625f), leftHand: V(-0.035f, -0.02f, 0.32f))
				.Box("Butt", Mat.Polymer, V(0, -0.04f, -0.27f), V(0.042f, 0.14f, 0.22f), pitch: -6)
				.Box("Wrist", Mat.Polymer, V(0, -0.01f, -0.09f), V(0.036f, 0.055f, 0.17f), pitch: -12)
				.Box("Receiver", Mat.Steel, V(0, 0.03f, 0.07f), V(0.036f, 0.06f, 0.20f))
				.Box("EjectionPort", Mat.Polymer, V(0.0185f, 0.04f, 0.08f), V(0.002f, 0.014f, 0.06f))
				.Box("TriggerGuard", Mat.Steel, V(0, -0.012f, 0.04f), V(0.01f, 0.02f, 0.07f))
				.Cyl("Barrel", Mat.Steel, V(0, 0.045f, 0.395f), 0.022f, 0.45f)
				.Cyl("MagTube", Mat.Steel, V(0, 0.015f, 0.365f), 0.022f, 0.39f)
				.Cyl("MagCap", Mat.Steel, V(0, 0.015f, 0.565f), 0.026f, 0.02f)
				.Box("Pump", Mat.Polymer, V(0, 0.015f, 0.34f), V(0.04f, 0.04f, 0.17f))
				.Ball("Bead", Mat.Aluminum, V(0, 0.06f, 0.615f), 0.008f);

			// Sawed-off side-by-side: wooden pistol grip & forend, blued action and barrels. Break action → nothing to hide.
			yield return new Spec("SawedOff", typeof(WeaponModel), "SawedOffConfig", muzzle: V(0, 0.04f, 0.405f), leftHand: V(-0.035f, -0.025f, 0.17f))
				.Box("GripWrist", Mat.Wood, V(0, -0.01f, -0.06f), V(0.035f, 0.05f, 0.12f), pitch: 15)
				.Box("GripPommel", Mat.Wood, V(0, -0.05f, -0.14f), V(0.037f, 0.06f, 0.08f), pitch: 45)
				.Box("Action", Mat.Steel, V(0, 0.025f, 0.03f), V(0.045f, 0.055f, 0.10f))
				.Cyl("BarrelL", Mat.Steel, V(-0.0115f, 0.04f, 0.24f), 0.022f, 0.32f)
				.Cyl("BarrelR", Mat.Steel, V(0.0115f, 0.04f, 0.24f), 0.022f, 0.32f)
				.Box("Rib", Mat.Steel, V(0, 0.053f, 0.24f), V(0.006f, 0.004f, 0.32f))
				.Box("Forend", Mat.Wood, V(0, 0.012f, 0.18f), V(0.042f, 0.03f, 0.20f))
				.Box("TriggerGuard", Mat.Steel, V(0, -0.025f, 0.02f), V(0.01f, 0.008f, 0.06f))
				.Box("HammerL", Mat.Steel, V(-0.012f, 0.06f, -0.015f), V(0.006f, 0.02f, 0.01f))
				.Box("HammerR", Mat.Steel, V(0.012f, 0.06f, -0.015f), V(0.006f, 0.02f, 0.01f));

			// M60: black polymer stock & handguard, blued receiver/barrel, folded bipod, olive ammo box + brass belt
			// on the left side (hidden while reloading, like a magazine).
			yield return new Spec("MachineGun", typeof(WeaponModel), "MachineGunConfig", muzzle: V(0, 0.055f, 0.735f), leftHand: V(-0.04f, -0.01f, 0.25f))
				.Box("Stock", Mat.Polymer, V(0, -0.005f, -0.33f), V(0.05f, 0.12f, 0.26f), pitch: -4)
				.Box("ButtPlate", Mat.Steel, V(0, -0.01f, -0.462f), V(0.052f, 0.125f, 0.01f), pitch: -4)
				.Box("Receiver", Mat.Steel, V(0, 0.04f, -0.04f), V(0.05f, 0.07f, 0.32f))
				.Box("FeedCover", Mat.Steel, V(0, 0.085f, -0.02f), V(0.052f, 0.02f, 0.18f))
				.Box("PistolGrip", Mat.Polymer, V(0, -0.045f, -0.015f), V(0.032f, 0.09f, 0.04f), pitch: 15)
				.Box("TriggerHousing", Mat.Steel, V(0, -0.005f, 0.01f), V(0.03f, 0.03f, 0.09f))
				.Box("Handguard", Mat.Polymer, V(0, 0.035f, 0.25f), V(0.055f, 0.055f, 0.26f))
				.Cyl("Barrel", Mat.Steel, V(0, 0.055f, 0.41f), 0.022f, 0.58f)
				.Cyl("GasCylinder", Mat.Steel, V(0, 0.02f, 0.45f), 0.02f, 0.14f)
				.Box("CarryHandle", Mat.Steel, V(0, 0.12f, 0.25f), V(0.01f, 0.01f, 0.09f))
				.Box("HandlePost", Mat.Steel, V(0, 0.1f, 0.21f), V(0.01f, 0.04f, 0.01f))
				.Box("FrontSight", Mat.Steel, V(0, 0.085f, 0.65f), V(0.006f, 0.04f, 0.012f))
				.Cyl("FlashHider", Mat.Steel, V(0, 0.055f, 0.705f), 0.03f, 0.06f)
				.Cyl("BipodL", Mat.Steel, V(-0.02f, 0.025f, 0.62f), 0.01f, 0.2f)
				.Cyl("BipodR", Mat.Steel, V(0.02f, 0.025f, 0.62f), 0.01f, 0.2f)
				.Box("AmmoBox", Mat.Olive, V(-0.07f, -0.03f, 0.0f), V(0.06f, 0.10f, 0.10f), magazine: true)
				.Box("AmmoBelt", Mat.Brass, V(-0.04f, 0.045f, 0.0f), V(0.04f, 0.012f, 0.04f), magazine: true);
		}

		// ------------------------------------------------------------------ building

		private static WeaponModel BuildPrefab(Spec spec, Dictionary<Mat, Material> materials)
		{
			string path = $"{PrefabFolder}/{spec.Name}.prefab";
			bool exists = File.Exists(path);
			var root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject(spec.Name);
			try
			{
				for (int i = root.transform.childCount - 1; i >= 0; i--)
					UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

				var model = root.GetComponent<WeaponModel>();
				if (model == null)
					model = (WeaponModel)root.AddComponent(spec.Component);

				Transform magazine = null;
				var magazines = new List<GameObject>();
				foreach (var part in spec.Parts)
				{
					Transform parent = root.transform;
					if (part.Magazine)
					{
						if (magazine == null)
						{
							magazine = new GameObject("Magazine").transform;
							magazine.SetParent(root.transform, false);
							magazines.Add(magazine.gameObject);
						}
						parent = magazine;
					}
					CreatePart(part, parent, materials[part.Material]);
				}

				var grip = CreatePoint("Grip", root.transform, Vector3.zero);
				var muzzle = CreatePoint("Muzzle", root.transform, spec.Muzzle);
				var leftHand = spec.LeftHand.HasValue ? CreatePoint("LeftHand", root.transform, spec.LeftHand.Value) : null;

				var so = new SerializedObject(model);
				so.FindProperty("_grip").objectReferenceValue = grip;
				so.FindProperty("_muzzle").objectReferenceValue = muzzle;
				so.FindProperty("_leftHandTarget").objectReferenceValue = leftHand;
				var mags = so.FindProperty("_magazines");
				mags.arraySize = magazines.Count;
				for (int i = 0; i < magazines.Count; i++)
					mags.GetArrayElementAtIndex(i).objectReferenceValue = magazines[i];
				var effect = so.FindProperty("_muzzleEffectId");
				if (string.IsNullOrEmpty(effect.stringValue))
					effect.stringValue = "muzzle_flash";
				so.ApplyModifiedPropertiesWithoutUndo();

				PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
				if (!success)
				{
					Debug.LogError($"[WeaponModelBuilder] Failed to save {path}");
					return null;
				}
			}
			finally
			{
				if (exists)
					PrefabUtility.UnloadPrefabContents(root);
				else
					UnityEngine.Object.DestroyImmediate(root);
			}

			var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			return asset != null ? asset.GetComponent<WeaponModel>() : null;
		}

		private static void CreatePart(Part part, Transform parent, Material material)
		{
			var go = GameObject.CreatePrimitive(part.Primitive);
			go.name = part.Name;
			UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
			var t = go.transform;
			t.SetParent(parent, false);
			t.SetLocalPositionAndRotation(part.Center, part.Rotation);
			t.localScale = part.Scale;
			go.GetComponent<MeshRenderer>().sharedMaterial = material;
		}

		private static Transform CreatePoint(string name, Transform parent, Vector3 position)
		{
			var t = new GameObject(name).transform;
			t.SetParent(parent, false);
			t.localPosition = position;
			return t;
		}

		private static void AssignToConfig(string configName, WeaponModel model)
		{
			string path = $"{ConfigFolder}/{configName}.asset";
			var config = AssetDatabase.LoadAssetAtPath<GunConfig>(path);
			if (config == null)
			{
				Debug.LogWarning($"[WeaponModelBuilder] GunConfig not found: {path}");
				return;
			}
			var so = new SerializedObject(config);
			var prop = so.FindProperty("_model");
			if (prop.objectReferenceValue == model)
				return;
			prop.objectReferenceValue = model;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(config);
		}

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

		// ------------------------------------------------------------------ spec DSL

		private readonly struct Part
		{
			public readonly string Name;
			public readonly PrimitiveType Primitive;
			public readonly Mat Material;
			public readonly Vector3 Center;
			public readonly Quaternion Rotation;
			public readonly Vector3 Scale;
			public readonly bool Magazine;

			public Part(string name, PrimitiveType primitive, Mat material, Vector3 center, Quaternion rotation, Vector3 scale, bool magazine)
			{
				Name = name;
				Primitive = primitive;
				Material = material;
				Center = center;
				Rotation = rotation;
				Scale = scale;
				Magazine = magazine;
			}
		}

		private sealed class Spec
		{
			public readonly string Name;
			public readonly Type Component;
			public readonly string Config;
			public readonly Vector3 Muzzle;
			public readonly Vector3? LeftHand;
			public readonly List<Part> Parts = new();

			public Spec(string name, Type component, string config, Vector3 muzzle, Vector3? leftHand)
			{
				Name = name;
				Component = component;
				Config = config;
				Muzzle = muzzle;
				LeftHand = leftHand;
			}

			/// <summary>Box. pitch = rotation about X in degrees (+ tilts the front end down / the bottom backwards).</summary>
			public Spec Box(string name, Mat mat, Vector3 center, Vector3 size, float pitch = 0f, bool magazine = false)
			{
				Parts.Add(new Part(name, PrimitiveType.Cube, mat, center, Quaternion.Euler(pitch, 0f, 0f), size, magazine));
				return this;
			}

			/// <summary>Cylinder along Z (Unity's cylinder is 2 units tall along Y).</summary>
			public Spec Cyl(string name, Mat mat, Vector3 center, float diameter, float length, bool magazine = false)
			{
				Parts.Add(new Part(name, PrimitiveType.Cylinder, mat, center, Quaternion.Euler(90f, 0f, 0f),
					new Vector3(diameter, length * 0.5f, diameter), magazine));
				return this;
			}

			public Spec Ball(string name, Mat mat, Vector3 center, float diameter, bool magazine = false)
			{
				Parts.Add(new Part(name, PrimitiveType.Sphere, mat, center, Quaternion.identity, Vector3.one * diameter, magazine));
				return this;
			}
		}
	}
}
