using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Builds the cheerleader NPC — Tools ▸ Crowds ▸ Build Cheerleader NPC. Safe to re-run.
	/// Model: Assets/Model/Cheerleader (Mixamo, mesh in "cheerleader_v2_10k (1).fbx", one take per "@Clip.fbx").
	///  1. Textures (BaseColor sRGB, Normal → NormalMap) → URP Lit "Cheerleader.mat" next to the model, remapped onto
	///     the FBX material (MobModelPipeline.SetupModel: Generic rig).
	///  2. Every animation FBX → a Generic clip named after the take (state name = take name, e.g. "Can Can"); loops
	///     except the death; looping clips get an in-place copy if the hips drift (Assets/_Animations/Npc).
	///  3. Assets/_Animations/Npc/Cheerleader.controller — one state per clip, default "Idle".
	///  4. Assets/_Prefabs/Npc/Pompom.prefab — primitive white/green pompom (handle + puff of strands), root = grip,
	///     +Y = toward the fingers (Mixamo hand bone axis), metres.
	///  5. Assets/_Prefabs/Npc/Cheerleader.prefab — root: Cheerleader + NavMeshAgent (disabled until the escape);
	///     child "Visual" = the model scaled to <see cref="TargetHeight"/>, Animator (no root motion, always animate),
	///     a Pompom under each hand bone (Cheerleader._props, shown only while dancing).
	/// Run it before Tools ▸ Crowds ▸ Build Hockey Level (that one places 3 instances on the stage).
	/// See Docs/CheerleaderFeature.md.
	/// </summary>
	public static class CheerleaderNpcBuilder
	{
		private const string Log = "[CheerleaderNpc]";
		private const string ModelFolder = "Assets/Model/Cheerleader";
		private const string ModelFile = "cheerleader_v2_10k (1).fbx";
		private const string TakePrefix = "cheerleader_v2_10k@";
		private const string AnimFolder = "Assets/_Animations/Npc";
		private const string PrefabFolder = "Assets/_Prefabs/Npc";
		internal const string PrefabPath = PrefabFolder + "/Cheerleader.prefab";
		private const float TargetHeight = 1.8f;
		internal const string PompomPath = PrefabFolder + "/Pompom.prefab";
		private const string PompomMaterialFolder = "Assets/_Materials/Npc";

		// Take name → role. Anything not listed is still imported (and becomes a state) — treated as a dance.
		private static readonly string[] Dances =
			{ "Arms Hip Hop Dance", "Bellydancing", "Can Can", "Dancing Running Man", "Hip Hop Dancing", "Hip Hop Dancing (1)" };
		// Static poses for the victory (each girl her own) — order = slot order.
		private static readonly string[] Poses = { "Female Standing Pose", "Kneeling Idle", "Female Standing Pose (1)" };
		private const string Idle = "Idle";
		private const string Run = "Goofy Running";
		private const string Death = "Falling Forward Death";

		[MenuItem("Tools/Crowds/Build Cheerleader NPC")]
		public static void Build()
		{
			string modelPath = $"{ModelFolder}/{ModelFile}";
			var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
			if (modelAsset == null)
			{
				Debug.LogError($"{Log} Model not found: {modelPath}");
				return;
			}
			EnsureFolder(AnimFolder);
			EnsureFolder(PrefabFolder);

			try
			{
				EditorUtility.DisplayProgressBar("Cheerleader NPC", "Material", 0.1f);
				var material = SetupMaterial();
				MobModelPipeline.SetupModel(modelPath, material);
				modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

				// --- clips
				var takes = Directory.GetFiles(ModelFolder, TakePrefix + "*.fbx")
					.Select(p => p.Replace('\\', '/'))
					.OrderBy(p => p)
					.ToList();
				var states = new List<(string state, AnimationClip clip)>();
				for (int i = 0; i < takes.Count; i++)
				{
					string path = takes[i];
					string take = Path.GetFileNameWithoutExtension(path).Substring(TakePrefix.Length);
					EditorUtility.DisplayProgressBar("Cheerleader NPC", $"Clip {take}", 0.2f + 0.6f * i / takes.Count);
					bool loop = take != Death;
					var clip = MobModelPipeline.SetupClip(path, $"Cheerleader_{take}", loop, isModel: false, modelPath);
					if (clip == null)
						continue;
					if (loop)
						clip = MobModelPipeline.MakeInPlaceIfDrifting(clip, $"{AnimFolder}/Cheerleader_{take}_InPlace.anim");
					MobModelPipeline.ValidateBindings(modelAsset, clip);
					states.Add((take, clip));
				}
				if (states.Count == 0)
				{
					Debug.LogError($"{Log} No animation takes found in {ModelFolder} ({TakePrefix}*.fbx).");
					return;
				}
				// Idle first → default state.
				states.Sort((a, b) => (a.state == Idle ? 0 : 1).CompareTo(b.state == Idle ? 0 : 1));

				EditorUtility.DisplayProgressBar("Cheerleader NPC", "Controller", 0.85f);
				var controller = MobModelPipeline.SetupController($"{AnimFolder}/Cheerleader.controller", states);

				EditorUtility.DisplayProgressBar("Cheerleader NPC", "Prefab", 0.9f);
				var names = new HashSet<string>(states.Select(s => s.state));
				var pompom = BuildPompom();
				BuildPrefab(modelAsset, controller, names, pompom);

				AssetDatabase.SaveAssets();
				Debug.Log($"{Log} Done: {PrefabPath}, {states.Count} states.");
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		private static Material SetupMaterial()
		{
			var baseColor = FindTexture("BaseColor");
			var normal = FindTexture("Normal");
			if (baseColor != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(baseColor)) is TextureImporter bi
			    && (bi.textureType != TextureImporterType.Default || !bi.sRGBTexture || bi.textureShape != TextureImporterShape.Texture2D))
			{
				bi.textureType = TextureImporterType.Default;
				bi.textureShape = TextureImporterShape.Texture2D;
				bi.sRGBTexture = true;
				bi.SaveAndReimport();
			}
			if (normal != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) is TextureImporter ni
			    && (ni.textureType != TextureImporterType.NormalMap || ni.textureShape != TextureImporterShape.Texture2D))
			{
				ni.textureType = TextureImporterType.NormalMap;
				ni.textureShape = TextureImporterShape.Texture2D;
				ni.SaveAndReimport();
			}

			string path = $"{ModelFolder}/Cheerleader.mat";
			var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (mat == null)
			{
				mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Cheerleader" };
				AssetDatabase.CreateAsset(mat, path);
			}
			if (baseColor != null)
			{
				mat.SetTexture("_BaseMap", baseColor);
				mat.SetTexture("_MainTex", baseColor);
			}
			mat.SetColor("_BaseColor", Color.white);
			if (normal != null)
			{
				mat.SetTexture("_BumpMap", normal);
				mat.SetFloat("_BumpScale", 1f);
				mat.EnableKeyword("_NORMALMAP");
			}
			mat.SetFloat("_Smoothness", 0.25f);
			mat.SetFloat("_Metallic", 0f);
			EditorUtility.SetDirty(mat);
			Debug.Log($"{Log} Material {path}: base={(baseColor ? baseColor.name : "none")}, normal={(normal ? normal.name : "none")}.", mat);
			return mat;
		}

		// "_face" is a portrait (achievement icon material), not a mesh texture — never picked here.
		private static Texture2D FindTexture(string key)
		{
			foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ModelFolder }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				string file = Path.GetFileNameWithoutExtension(path);
				if (file.EndsWith("_" + key))
					return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
			}
			Debug.LogWarning($"{Log} No *_{key} texture in {ModelFolder}.");
			return null;
		}

		// ------------------------------------------------------------------ pompom

		/// <summary>
		/// Primitive pompom: short white handle + a puff of thin strands (alternating white / green) around a green
		/// core. Root = grip point, +Y = along the fingers. Rebuilt in place on every run (same guid).
		/// </summary>
		private static GameObject BuildPompom()
		{
			EnsureFolder(PompomMaterialFolder);
			var white = PompomMaterial("Pompom_White", new Color(0.95f, 0.95f, 0.95f));
			var green = PompomMaterial("Pompom_Green", new Color(0.09f, 0.45f, 0.30f)); // the green of her bow / face paint

			var root = new GameObject("Pompom");
			try
			{
				const float puffY = 0.07f, core = 0.09f, strandLength = 0.15f;
				// Unity's cylinder is 2 units tall: scale.y = half the height.
				Part(PrimitiveType.Cylinder, "Handle", root.transform, white, new Vector3(0, 0.02f, 0), Quaternion.identity,
					new Vector3(0.025f, 0.02f, 0.025f));
				Part(PrimitiveType.Sphere, "Core", root.transform, green, new Vector3(0, puffY, 0), Quaternion.identity,
					Vector3.one * core);
				// Strands on a Fibonacci sphere: thin flat blades pointing out of the core.
				const int strands = 26;
				float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
				for (int i = 0; i < strands; i++)
				{
					float y = 1f - (i + 0.5f) / strands * 2f;
					float r = Mathf.Sqrt(1f - y * y);
					var dir = new Vector3(Mathf.Cos(golden * i) * r, y, Mathf.Sin(golden * i) * r);
					var rot = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0, i * 37f, 0);
					Part(PrimitiveType.Cube, "Strand", root.transform, i % 2 == 0 ? white : green,
						new Vector3(0, puffY, 0) + dir * (strandLength * 0.5f), rot, new Vector3(0.022f, strandLength, 0.004f));
				}
				PrefabUtility.SaveAsPrefabAsset(root, PompomPath, out bool ok);
				if (!ok)
					Debug.LogError($"{Log} Failed to save {PompomPath}");
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
			return AssetDatabase.LoadAssetAtPath<GameObject>(PompomPath);
		}

		private static void Part(PrimitiveType type, string name, Transform parent, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
		{
			var go = GameObject.CreatePrimitive(type);
			go.name = name;
			Object.DestroyImmediate(go.GetComponent<Collider>());
			go.transform.SetParent(parent, false);
			go.transform.SetLocalPositionAndRotation(pos, rot);
			go.transform.localScale = scale;
			var r = go.GetComponent<MeshRenderer>();
			r.sharedMaterial = mat;
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
		}

		private static Material PompomMaterial(string name, Color color)
		{
			string path = $"{PompomMaterialFolder}/{name}.mat";
			var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (mat == null)
			{
				mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
				AssetDatabase.CreateAsset(mat, path);
			}
			mat.SetColor("_BaseColor", color);
			mat.SetColor("_Color", color);
			mat.SetFloat("_Smoothness", 0.55f);
			mat.SetFloat("_Metallic", 0.1f);
			mat.enableInstancing = true;
			EditorUtility.SetDirty(mat);
			return mat;
		}

		private static Transform FindBone(Transform root, string boneName)
		{
			foreach (var t in root.GetComponentsInChildren<Transform>(true))
				if (t.name == boneName || t.name.EndsWith(":" + boneName))
					return t;
			return null;
		}

		// ------------------------------------------------------------------ prefab

		private static void BuildPrefab(GameObject modelAsset, AnimatorController controller, HashSet<string> states, GameObject pompom)
		{
			var root = new GameObject("Cheerleader");
			try
			{
				var visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
				visual.name = "Visual";
				visual.transform.SetParent(root.transform, false);

				// Scale to the target height (measured in the bind pose, feet at the root).
				var renderers = visual.GetComponentsInChildren<Renderer>(true);
				var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
				foreach (var r in renderers) bounds.Encapsulate(r.bounds);
				float scale = bounds.size.y > 0.01f ? TargetHeight / bounds.size.y : 1f;
				visual.transform.localScale = Vector3.one * scale;
				Debug.Log($"{Log} Visual height {bounds.size.y:0.###} → scale {scale:0.###} ({TargetHeight} m).");

				var animator = visual.GetComponent<Animator>();
				if (animator == null)
					animator = visual.AddComponent<Animator>();
				animator.runtimeAnimatorController = controller;
				animator.applyRootMotion = false;
				animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
				foreach (var smr in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
					smr.updateWhenOffscreen = false;

				// Pompoms in both hands, world-sized (compensates the bone scale from the import + the visual scale).
				var props = new List<GameObject>();
				if (pompom != null)
					foreach (var boneName in new[] { "LeftHand", "RightHand" })
					{
						var hand = FindBone(visual.transform, boneName);
						if (hand == null)
						{
							Debug.LogWarning($"{Log} Bone '{boneName}' not found — no pompom in that hand.");
							continue;
						}
						var p = (GameObject)PrefabUtility.InstantiatePrefab(pompom, hand);
						p.name = "Pompom";
						p.transform.localPosition = Vector3.zero;
						p.transform.localRotation = Quaternion.identity;
						p.transform.localScale = Vector3.one / Mathf.Max(hand.lossyScale.x, 1e-4f);
						props.Add(p);
					}

				var agent = root.AddComponent<NavMeshAgent>();
				agent.radius = 0.3f;
				agent.height = TargetHeight;
				agent.speed = 4.5f;
				agent.acceleration = 20f;
				agent.angularSpeed = 720f;
				agent.stoppingDistance = 0.3f;
				agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
				agent.enabled = false;

				var cheer = root.AddComponent<Cheerleader>();
				var so = new SerializedObject(cheer);
				so.FindProperty("_animator").objectReferenceValue = animator;
				so.FindProperty("_agent").objectReferenceValue = agent;
				var propsProp = so.FindProperty("_props");
				propsProp.arraySize = props.Count;
				for (int i = 0; i < props.Count; i++)
					propsProp.GetArrayElementAtIndex(i).objectReferenceValue = props[i];
				var rs = so.FindProperty("_renderers");
				rs.arraySize = renderers.Length;
				for (int i = 0; i < renderers.Length; i++)
					rs.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
				SetStrings(so.FindProperty("_danceStates"), states.Where(s => !Poses.Contains(s) && s != Idle && s != Run && s != Death));
				SetStrings(so.FindProperty("_poseStates"), Poses.Where(states.Contains));
				so.FindProperty("_idleState").stringValue = Idle;
				so.FindProperty("_runState").stringValue = Run;
				so.FindProperty("_deathState").stringValue = Death;
				so.ApplyModifiedPropertiesWithoutUndo();

				foreach (var needed in new[] { Idle, Run, Death })
					if (!states.Contains(needed))
						Debug.LogWarning($"{Log} Take '{needed}' is missing — download it from Mixamo as {TakePrefix}{needed}.fbx.");

				PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
				if (!ok)
					Debug.LogError($"{Log} Failed to save {PrefabPath}");
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		private static void SetStrings(SerializedProperty prop, IEnumerable<string> values)
		{
			var list = values.ToList();
			prop.arraySize = list.Count;
			for (int i = 0; i < list.Count; i++)
				prop.GetArrayElementAtIndex(i).stringValue = list[i];
		}

		private static void EnsureFolder(string path)
		{
			if (AssetDatabase.IsValidFolder(path))
				return;
			string parent = Path.GetDirectoryName(path).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
		}
	}
}
