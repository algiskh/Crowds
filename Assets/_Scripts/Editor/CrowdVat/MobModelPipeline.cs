using System.Collections.Generic;
using System.IO;
using System.Linq;
using Scene.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Runs a <see cref="MobModelSetupPreset"/>: texture import → URP Lit material (BaseColor + Normal) remapped onto
	/// the FBX → generic clips (looping, in-place copy if the hips drift) → AnimatorController → swap the visual
	/// child of the mob prefab (scaled to the previous height) → VAT bake → assign the CrowdLibrary to the configs.
	/// Idempotent. Every step logs with the "[MobModel]" prefix (grep Editor.log to verify a run).
	/// </summary>
	public static class MobModelPipeline
	{
		private const string Log = "[MobModel]";
		private const string HipsBone = "mixamorig:Hips";
		private const string ControllerFolder = "Assets/_Animations/Mob";

		[MenuItem("Tools/Crowds/Run Selected Mob Model Preset")]
		private static void RunSelected()
		{
			var presets = Selection.objects.OfType<MobModelSetupPreset>().ToArray();
			if (presets.Length == 0) { Debug.LogError($"{Log} Select a MobModelSetupPreset asset (Assets/_Data/Crowd/Setup/)."); return; }
			foreach (var p in presets) Run(p);
		}

		public static bool Run(MobModelSetupPreset p)
		{
			NormalizeReferences(p);
			if (p.Model == null || p.Prefab == null) { Debug.LogError($"{Log} Preset '{p.name}' needs Model and Prefab.", p); return false; }
			string modelPath = AssetDatabase.GetAssetPath(p.Model);
			string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
			string visual = string.IsNullOrEmpty(p.VisualName) ? p.Model.name : p.VisualName;
			Debug.Log($"{Log} ===== Preset '{p.name}': {modelPath} → {AssetDatabase.GetAssetPath(p.Prefab)} =====", p);

			try
			{
				Progress(p, "Textures", 0.05f);
				ResolveTextures(p, folder);
				SetupTextures(p);

				Progress(p, "Material", 0.15f);
				string matName = string.IsNullOrEmpty(p.MaterialName) ? visual + "Material" : p.MaterialName;
				Material material = SetupMaterial(p, $"{folder}/{matName}.mat");

				Progress(p, "Model import", 0.25f);
				SetupModel(modelPath, material);
				var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

				Progress(p, "Animation clips", 0.4f);
				var states = new List<(string state, AnimationClip clip)>();
				// One FBX may back several states (e.g. a placeholder until a dedicated clip exists): import it once.
				var clipsByFbx = new Dictionary<string, AnimationClip>();
				foreach (var src in p.Clips)
				{
					if (src.Fbx == null || string.IsNullOrEmpty(src.State)) { Debug.LogWarning($"{Log} Skipping an empty clip entry.", p); continue; }
					if (!AnimationTypes.TryFromStateName(src.State, out _))
						Debug.LogWarning($"{Log} State '{src.State}' doesn't match an AnimationType — the game and the VAT baker will ignore it.", p);
					string fbxPath = AssetDatabase.GetAssetPath(src.Fbx);
					if (!clipsByFbx.TryGetValue(fbxPath, out var clip))
					{
						clip = SetupClip(fbxPath, $"{visual}_{src.State}", src.Loop, src.Fbx == p.Model, modelPath);
						if (clip == null) return false;
						if (src.RemoveRootDrift) clip = MakeInPlaceIfDrifting(clip, $"{folder}/{visual}_{src.State}_InPlace.anim");
						ValidateBindings(modelAsset, clip);
						clipsByFbx[fbxPath] = clip;
					}
					else
					{
						Debug.Log($"{Log} State '{src.State}' reuses clip '{clip.name}'.");
					}
					states.Add((src.State, clip));
				}
				if (states.Count == 0) { Debug.LogError($"{Log} No clips configured.", p); return false; }

				Progress(p, "Animator controller", 0.5f);
				var controller = SetupController($"{ControllerFolder}/{visual}.controller", states);

				Progress(p, "Prefab", 0.6f);
				if (!SetupPrefab(p, modelAsset, visual, controller, states)) return false;

				CrowdAnimationLibrary library = null;
				if (p.BakeVat)
				{
					Progress(p, "VAT bake", 0.75f);
					library = VatBakerWindow.Bake(p.Prefab.gameObject, p.BakeFps);
					if (library == null) { Debug.LogError($"{Log} VAT bake failed, see [VAT] errors above.", p); return false; }
				}

				Progress(p, "Mob configs", 0.95f);
				AssignConfigs(p, library);

				AssetDatabase.SaveAssets();
				Debug.Log($"{Log} Done '{p.name}'.", p);
				return true;
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		private static void Progress(MobModelSetupPreset p, string step, float t) =>
			EditorUtility.DisplayProgressBar($"Mob model setup: {p.name}", step, t);

		// --- Textures / material ----------------------------------------------------------------------

		/// <summary>
		/// Hand-written preset YAML may point at an FBX's legacy "Prefab" object (fileID 100100000) instead of its root
		/// GameObject — reload every model/clip reference by path so it's a real GameObject, and write it back.
		/// </summary>
		private static void NormalizeReferences(MobModelSetupPreset p)
		{
			GameObject Fix(GameObject go)
			{
				if (ReferenceEquals(go, null)) return null;
				string path = AssetDatabase.GetAssetPath(go);
				return string.IsNullOrEmpty(path) ? go : AssetDatabase.LoadAssetAtPath<GameObject>(path);
			}

			p.Model = Fix(p.Model);
			p.HeightReference = Fix(p.HeightReference);
			foreach (var c in p.Clips) c.Fbx = Fix(c.Fbx);
			EditorUtility.SetDirty(p);
			AssetDatabase.SaveAssetIfDirty(p);
		}

		/// <summary>Empty texture slots are filled from the model folder by name (BaseColor/Albedo/Diffuse, Normal).</summary>
		private static void ResolveTextures(MobModelSetupPreset p, string folder)
		{
			var files = Directory.GetFiles(folder)
				.Where(f => !f.EndsWith(".meta"))
				.Select(f => f.Replace('\\', '/'))
				.Where(f => new[] { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".exr" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
				.ToArray();

			foreach (var f in files)
			{
				// Bare hand-written metas (guid only) can import as a Cubemap — a Texture2D slot then reads null.
				if (AssetImporter.GetAtPath(f) is TextureImporter ti && ti.textureShape != TextureImporterShape.Texture2D)
				{
					Debug.Log($"{Log} {f}: texture shape {ti.textureShape} → Texture2D.");
					ti.textureShape = TextureImporterShape.Texture2D;
					ti.SaveAndReimport();
				}
				var main = AssetDatabase.LoadMainAssetAtPath(f);
				Debug.Log($"{Log} Texture candidate {f}: main asset = {(main != null ? main.GetType().Name : "null (import failed?)")}, importer = {AssetImporter.GetAtPath(f)?.GetType().Name ?? "none"}.");
			}

			Texture2D Find(params string[] keys) => files
				.Where(path => keys.Any(k => Path.GetFileNameWithoutExtension(path).IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0))
				.Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
				.FirstOrDefault(t => t != null);

			bool changed = false;
			if (p.Normal == null && (p.Normal = Find("normal", "_nrm")) != null) changed = true;
			if (p.BaseColor == null && (p.BaseColor = Find("basecolor", "albedo", "diffuse", "base_color", "color")) != null) changed = true;
			if (changed)
			{
				EditorUtility.SetDirty(p);
				AssetDatabase.SaveAssetIfDirty(p);
				Debug.Log($"{Log} Textures auto-assigned from {folder}: base={(p.BaseColor ? p.BaseColor.name : "none")}, normal={(p.Normal ? p.Normal.name : "none")}.", p);
			}
			if (p.BaseColor == null) Debug.LogWarning($"{Log} No BaseColor texture — the material will be untextured. See 'Texture candidate' lines above.", p);
		}

		/// <summary>
		/// LoadPrefabContents refuses a prefab whose nested prefab source was deleted (e.g. the old FBX). Remove such
		/// PrefabInstances (+ their stripped objects and components added onto them) straight from the YAML, null out
		/// references to them and reimport. Returns true if the file changed.
		/// </summary>
		private static bool StripMissingNestedPrefabs(string prefabPath)
		{
			string text = File.ReadAllText(prefabPath);
			var header = new System.Text.RegularExpressions.Regex(@"^--- !u!(\d+) &(-?\d+)(?: stripped)?\r?$",
				System.Text.RegularExpressions.RegexOptions.Multiline);
			var matches = header.Matches(text);
			if (matches.Count == 0) return false;

			var docs = new List<(string id, int cls, string body)>();
			for (int i = 0; i < matches.Count; i++)
			{
				int start = matches[i].Index;
				int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
				docs.Add((matches[i].Groups[2].Value, int.Parse(matches[i].Groups[1].Value), text.Substring(start, end - start)));
			}
			string preamble = text.Substring(0, matches[0].Index);

			var removed = new HashSet<string>();
			var sourceRx = new System.Text.RegularExpressions.Regex(@"m_SourcePrefab: \{fileID: -?\d+, guid: ([0-9a-f]{32})");
			foreach (var d in docs.Where(d => d.cls == 1001))
			{
				var m = sourceRx.Match(d.body);
				if (!m.Success) continue;
				string path = AssetDatabase.GUIDToAssetPath(m.Groups[1].Value);
				if (string.IsNullOrEmpty(path) || !File.Exists(path))
				{
					removed.Add(d.id);
					Debug.Log($"{Log} {prefabPath}: removing nested prefab instance {d.id} with missing source {m.Groups[1].Value}.");
				}
			}
			if (removed.Count == 0) return false;

			// Stripped objects of the removed instances, then anything attached to removed GameObjects.
			foreach (var d in docs)
				foreach (var inst in removed.ToArray())
					if (d.body.Contains($"m_PrefabInstance: {{fileID: {inst}}}")) removed.Add(d.id);
			bool grew = true;
			while (grew)
			{
				grew = false;
				foreach (var d in docs)
					if (!removed.Contains(d.id) && removed.Any(r => d.body.Contains($"m_GameObject: {{fileID: {r}}}")))
						grew |= removed.Add(d.id);
			}

			var sb = new System.Text.StringBuilder(preamble);
			foreach (var d in docs.Where(d => !removed.Contains(d.id)))
			{
				string body = d.body;
				foreach (var r in removed)
				{
					// Drop from children lists, null every other local reference.
					body = System.Text.RegularExpressions.Regex.Replace(body, $@"^[ \t]*- \{{fileID: {r}\}}\r?\n", "",
						System.Text.RegularExpressions.RegexOptions.Multiline);
					body = body.Replace($"{{fileID: {r}}}", "{fileID: 0}");
				}
				sb.Append(body);
			}

			File.WriteAllText(prefabPath, sb.ToString());
			AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
			Debug.Log($"{Log} {prefabPath}: stripped {removed.Count} YAML objects of missing nested prefabs.");
			return true;
		}

		private static void SetupTextures(MobModelSetupPreset p)
		{
			if (p.BaseColor != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(p.BaseColor)) is TextureImporter bi
			    && (bi.textureType != TextureImporterType.Default || !bi.sRGBTexture))
			{
				bi.textureType = TextureImporterType.Default;
				bi.sRGBTexture = true;
				bi.SaveAndReimport();
			}

			if (p.Normal != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(p.Normal)) is TextureImporter ni
			    && ni.textureType != TextureImporterType.NormalMap)
			{
				ni.textureType = TextureImporterType.NormalMap;
				ni.SaveAndReimport();
			}
		}

		private static Material SetupMaterial(MobModelSetupPreset p, string path)
		{
			var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (mat == null)
			{
				mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = Path.GetFileNameWithoutExtension(path) };
				AssetDatabase.CreateAsset(mat, path);
			}

			if (p.BaseColor != null)
			{
				mat.SetTexture("_BaseMap", p.BaseColor);
				mat.SetTexture("_MainTex", p.BaseColor);
			}
			mat.SetColor("_BaseColor", Color.white);
			if (p.Normal != null)
			{
				mat.SetTexture("_BumpMap", p.Normal);
				mat.SetFloat("_BumpScale", 1f);
				mat.EnableKeyword("_NORMALMAP");
			}
			mat.SetFloat("_Smoothness", 0.15f);
			mat.SetFloat("_Metallic", 0f);
			mat.enableInstancing = true;
			EditorUtility.SetDirty(mat);
			AssetDatabase.SaveAssetIfDirty(mat);
			Debug.Log($"{Log} Material {path}: base={(p.BaseColor ? p.BaseColor.name : "none")}, normal={(p.Normal ? p.Normal.name : "none")}.", mat);
			return mat;
		}

		// --- Model / clips ----------------------------------------------------------------------------

		private static void SetupModel(string modelPath, Material material)
		{
			var imp = (ModelImporter)AssetImporter.GetAtPath(modelPath);
			imp.animationType = ModelImporterAnimationType.Generic;
			imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
			imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
			imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
			imp.SaveAndReimport();

			// Embedded texture paths usually point to the exporter's machine and don't resolve: remap every
			// material the FBX defines onto our URP material.
			var names = new HashSet<string>();
			foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(modelPath))
				if (obj is Material m && m != material) names.Add(m.name);
			foreach (var r in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Renderer>())
				foreach (var m in r.sharedMaterials)
					if (m != null && m != material) names.Add(m.name);

			if (names.Count > 0)
			{
				foreach (var n in names)
					imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), material);
				imp.SaveAndReimport();
			}

			foreach (var r in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Renderer>())
				foreach (var m in r.sharedMaterials)
					if (m != material)
						Debug.LogWarning($"{Log} Renderer '{r.name}' still uses '{(m != null ? m.name : "null")}' — remap it in the FBX Materials tab.", r);

			Debug.Log($"{Log} Model {modelPath}: Generic, {names.Count} material(s) remapped → {material.name}.");
		}

		private static AnimationClip SetupClip(string path, string clipName, bool loop, bool isModel, string modelPath)
		{
			var imp = (ModelImporter)AssetImporter.GetAtPath(path);
			imp.animationType = ModelImporterAnimationType.Generic;
			imp.importAnimation = true;
			if (!isModel)
			{
				imp.materialImportMode = ModelImporterMaterialImportMode.None; // animation-only file

				// Generic clips carry absolute bone positions: the animation FBX must be scaled exactly like the model,
				// otherwise the hips land 100x off (cm vs m — e.g. a fresh Unity meta has useFileScale=1, an older one 0)
				// and the body sinks into the floor / flies away.
				var modelImp = (ModelImporter)AssetImporter.GetAtPath(modelPath);
				if (imp.useFileScale != modelImp.useFileScale || !Mathf.Approximately(imp.globalScale, modelImp.globalScale))
				{
					Debug.Log($"{Log} {Path.GetFileName(path)}: scale settings aligned with the model " +
					          $"(useFileScale {imp.useFileScale}→{modelImp.useFileScale}, globalScale {imp.globalScale}→{modelImp.globalScale}).");
					imp.useFileScale = modelImp.useFileScale;
					imp.globalScale = modelImp.globalScale;
				}
			}

			var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
			if (clips.Length == 0) { Debug.LogError($"{Log} No animation take in {path}."); return null; }
			clips = new[] { clips[0] };
			clips[0].name = clipName;
			clips[0].loopTime = loop;
			clips[0].loopPose = false;
			imp.clipAnimations = clips;
			imp.SaveAndReimport();

			var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == clipName);
			if (clip == null) Debug.LogError($"{Log} Clip '{clipName}' not found in {path} after import.");
			return clip;
		}

		/// <summary>
		/// Mobs are moved by the NavMeshAgent, so a clip whose hips travel on XZ would drift and snap back each loop.
		/// If drift is detected, writes a copy with the linear drift removed (the per-step sway is kept).
		/// </summary>
		private static AnimationClip MakeInPlaceIfDrifting(AnimationClip clip, string outPath)
		{
			var bindings = AnimationUtility.GetCurveBindings(clip)
				.Where(b => b.type == typeof(Transform) && LastSegment(b.path) == HipsBone)
				.ToArray();
			var xz = bindings.Where(b => b.propertyName == "m_LocalPosition.x" || b.propertyName == "m_LocalPosition.z").ToArray();

			var hipsY = bindings.FirstOrDefault(b => b.propertyName == "m_LocalPosition.y");
			float hipHeight = hipsY.path != null ? Mathf.Abs(AnimationUtility.GetEditorCurve(clip, hipsY)?.Evaluate(0f) ?? 1f) : 1f;

			float maxDrift = 0f;
			foreach (var binding in xz)
			{
				var c = AnimationUtility.GetEditorCurve(clip, binding);
				if (c == null || c.length < 2) continue;
				maxDrift = Mathf.Max(maxDrift, Mathf.Abs(c.keys[c.length - 1].value - c.keys[0].value));
			}

			if (maxDrift < hipHeight * 0.1f)
			{
				Debug.Log($"{Log} Clip '{clip.name}' is in-place (hips XZ drift {maxDrift:0.###}, hip height {hipHeight:0.###}), length {clip.length:0.##}s.");
				return clip;
			}

			var copy = Object.Instantiate(clip);
			foreach (var binding in xz)
			{
				var c = AnimationUtility.GetEditorCurve(clip, binding);
				if (c == null || c.length < 2) continue;
				float t0 = c.keys[0].time, t1 = c.keys[c.length - 1].time;
				float v0 = c.keys[0].value, v1 = c.keys[c.length - 1].value;
				float slope = t1 > t0 ? (v1 - v0) / (t1 - t0) : 0f;
				var keys = c.keys;
				for (int i = 0; i < keys.Length; i++)
				{
					keys[i].value -= slope * (keys[i].time - t0);
					keys[i].inTangent -= slope;
					keys[i].outTangent -= slope;
				}
				AnimationUtility.SetEditorCurve(copy, binding, new AnimationCurve(keys) { preWrapMode = c.preWrapMode, postWrapMode = c.postWrapMode });
			}

			var settings = AnimationUtility.GetAnimationClipSettings(copy);
			settings.loopTime = AnimationUtility.GetAnimationClipSettings(clip).loopTime;
			AnimationUtility.SetAnimationClipSettings(copy, settings);
			copy.name = Path.GetFileNameWithoutExtension(outPath);

			var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
			if (existing != null)
			{
				EditorUtility.CopySerialized(copy, existing);
				existing.name = copy.name;
				EditorUtility.SetDirty(existing);
				Object.DestroyImmediate(copy);
				copy = existing;
			}
			else
			{
				AssetDatabase.CreateAsset(copy, outPath);
			}

			AssetDatabase.SaveAssets();
			Debug.Log($"{Log} Clip '{clip.name}' travelled {maxDrift:0.###} on XZ — wrote in-place copy {outPath}.", copy);
			return copy;
		}

		private static string LastSegment(string path)
		{
			int i = path.LastIndexOf('/');
			return i >= 0 ? path.Substring(i + 1) : path;
		}

		/// <summary>Generic clips bind by transform path — warn if the animation FBX hierarchy doesn't match the model.</summary>
		private static void ValidateBindings(GameObject model, AnimationClip clip)
		{
			int missing = 0, total = 0;
			string example = null;
			foreach (var path in AnimationUtility.GetCurveBindings(clip).Select(x => x.path).Distinct())
			{
				total++;
				if (path.Length > 0 && model.transform.Find(path) == null) { missing++; example ??= path; }
			}
			// Hips height must match the model's bind pose (same units) — a 10x+ mismatch means a scale problem.
			var hips = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == HipsBone);
			var hipsBinding = AnimationUtility.GetCurveBindings(clip)
				.FirstOrDefault(b => b.type == typeof(Transform) && LastSegment(b.path) == HipsBone && b.propertyName == "m_LocalPosition.y");
			if (hips != null && hipsBinding.path != null)
			{
				float clipY = Mathf.Abs(AnimationUtility.GetEditorCurve(clip, hipsBinding)?.Evaluate(0f) ?? 0f);
				float modelY = Mathf.Abs(hips.localPosition.y);
				if (clipY > 0.0001f && modelY > 0.0001f && (clipY / modelY > 3f || modelY / clipY > 3f))
					Debug.LogWarning($"{Log} Clip '{clip.name}': hips height {clipY:0.###} vs model {modelY:0.###} — unit/scale mismatch, the body will sink or float.", clip);
			}

			if (missing > 0)
				Debug.LogWarning($"{Log} Clip '{clip.name}': {missing}/{total} bone paths missing on the model (e.g. '{example}') — those bones won't animate.", clip);
			else
				Debug.Log($"{Log} Clip '{clip.name}' binds all {total} paths.");
		}

		private static AnimatorController SetupController(string path, List<(string state, AnimationClip clip)> states)
		{
			var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)
			                 ?? AnimatorController.CreateAnimatorControllerAtPath(path);

			var sm = controller.layers[0].stateMachine;
			foreach (var s in sm.states.ToArray())
				sm.RemoveState(s.state);

			AnimatorState first = null, idle = null;
			for (int i = 0; i < states.Count; i++)
			{
				// State names must match AnimationType names — used by SimpleAnimator and the VAT baker.
				var st = sm.AddState(states[i].state, new Vector3(300, i * 80, 0));
				st.motion = states[i].clip;
				first ??= st;
				if (states[i].state == "idle") idle = st;
			}
			sm.defaultState = idle ?? first;

			EditorUtility.SetDirty(controller);
			AssetDatabase.SaveAssets();
			Debug.Log($"{Log} Controller {path}: {string.Join(", ", states.Select(s => s.state))}.", controller);
			return controller;
		}

		// --- Prefab -----------------------------------------------------------------------------------

		private static bool SetupPrefab(MobModelSetupPreset p, GameObject modelAsset, string visualName, AnimatorController controller,
			List<(string state, AnimationClip clip)> states)
		{
			string prefabPath = AssetDatabase.GetAssetPath(p.Prefab);
			StripMissingNestedPrefabs(prefabPath);
			GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
			try
			{
				var mob = root.GetComponent<Mob>();
				if (mob == null) { Debug.LogError($"{Log} {prefabPath} has no Mob component."); return false; }

				// Old visuals: the child owning the SimpleAnimator, a child with our visual name, or a nested
				// prefab whose source asset is gone (e.g. the FBX was replaced).
				var old = new List<GameObject>();
				var oldAnimator = root.GetComponentInChildren<SimpleAnimator>(true);
				if (oldAnimator != null && oldAnimator.gameObject != root) old.Add(oldAnimator.gameObject);
				foreach (Transform child in root.transform)
					if (child.name == visualName || PrefabUtility.IsPrefabAssetMissing(child.gameObject))
						if (!old.Contains(child.gameObject)) old.Add(child.gameObject);

				float targetHeight = p.TargetHeight;
				int siblingIndex = -1;
				foreach (var go in old)
				{
					if (targetHeight <= 0f) targetHeight = MeasureHeight(go);
					if (siblingIndex < 0 && go.transform.parent == root.transform) siblingIndex = go.transform.GetSiblingIndex();
				}
				if (targetHeight <= 0.01f && p.HeightReference != null)
				{
					var tmp = Object.Instantiate(p.HeightReference);
					tmp.hideFlags = HideFlags.HideAndDontSave;
					targetHeight = MeasureHeight(tmp);
					Object.DestroyImmediate(tmp);
					Debug.Log($"{Log} Height taken from reference '{p.HeightReference.name}': {targetHeight:0.###}.");
				}

				// Keep a hand-tuned weapon pose across re-runs (the weapon lives under the old visual's hand bone).
				(Vector3 pos, Quaternion rot, Vector3 scale)? weaponPose = null;
				foreach (var go in old)
				{
					var w = go.GetComponentInChildren<WeaponModel>(true);
					if (w != null) { weaponPose = (w.transform.localPosition, w.transform.localRotation, w.transform.localScale); break; }
				}

				foreach (var go in old)
				{
					Debug.Log($"{Log} Removing old visual '{go.name}'.");
					Object.DestroyImmediate(go);
				}

				foreach (var name in p.RemoveObjects.Where(n => !string.IsNullOrEmpty(n)))
				{
					var t = root.transform.Find(name);
					if (t == null) continue;
					Debug.Log($"{Log} Removing '{name}' (RemoveObjects).");
					Object.DestroyImmediate(t.gameObject);
				}
				foreach (var name in p.HideRenderers.Where(n => !string.IsNullOrEmpty(n)))
				{
					var t = root.transform.Find(name);
					if (t == null) continue;
					foreach (var r in t.GetComponents<Renderer>()) r.enabled = false;
					Debug.Log($"{Log} Renderers on '{name}' disabled (HideRenderers); its collider stays.");
				}

				var visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
				visual.name = visualName;
				if (siblingIndex >= 0) visual.transform.SetSiblingIndex(siblingIndex);
				visual.transform.localPosition = Vector3.zero;
				visual.transform.localRotation = Quaternion.identity;
				visual.transform.localScale = Vector3.one;

				// Match the previous height so the collider, health bar and HitRadius stay valid.
				float nativeHeight = MeasureHeight(visual);
				float scale = targetHeight > 0.01f && nativeHeight > 0.0001f ? targetHeight / nativeHeight : 1f;
				if (targetHeight <= 0.01f)
					Debug.LogWarning($"{Log} No target height (no old visual, no TargetHeight/HeightReference) — scale left at 1.");
				visual.transform.localScale = Vector3.one * scale;
				Debug.Log($"{Log} Native height {nativeHeight:0.###}, target {targetHeight:0.###} → visual scale {scale:0.####}.");

				var b = ComputeBounds(visual);
				if (b.size.y < b.size.z || b.size.y < b.size.x)
					Debug.LogWarning($"{Log} Bounds look lying down ({b.size}); check FBX axis conversion.");

				var animator = visual.GetComponent<Animator>();
				if (animator == null) animator = visual.AddComponent<Animator>();
				animator.runtimeAnimatorController = controller;
				animator.applyRootMotion = false;
				animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

				var simple = visual.GetComponent<SimpleAnimator>();
				if (simple == null) simple = visual.AddComponent<SimpleAnimator>();
				var so = new SerializedObject(simple);
				so.FindProperty("_animator").objectReferenceValue = animator;
				so.FindProperty("_defaultAnimationName").stringValue = controller.layers[0].stateMachine.defaultState.name;
				so.ApplyModifiedPropertiesWithoutUndo();

				var mso = new SerializedObject(mob);
				mso.FindProperty("_animator").objectReferenceValue = simple;
				var alignClip = states.FirstOrDefault(s => s.state == p.WeaponAlignState).clip;
				mso.FindProperty("_weapon").objectReferenceValue = AttachWeapon(p, visual, weaponPose, alignClip);
				mso.ApplyModifiedPropertiesWithoutUndo();

				var skinned = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
				int verts = skinned.Sum(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0);
				int tris = skinned.Sum(s => s.sharedMesh != null ? (int)(s.sharedMesh.GetIndexCount(0) / 3) : 0);
				if (skinned.Length != 1)
					Debug.LogWarning($"{Log} Model has {skinned.Length} SkinnedMeshRenderers; the VAT baker only bakes the first one.");
				Debug.Log($"{Log} Mesh: {verts} verts, ~{tris} tris (VAT budget for mass mobs: ≤ ~5k tris).");

				PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
				Debug.Log($"{Log} Prefab {prefabPath} saved with visual '{visualName}'.");
				return true;
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		/// <summary>
		/// Instantiates the preset's weapon under the hand bone. The bone sits under the import-scaled visual, so the
		/// local scale is divided by the bone's lossy scale to get WeaponScale in world units. A pose captured from
		/// the previous run (hand-tuned in the prefab) wins over the preset's initial offsets.
		/// </summary>
		private static WeaponModel AttachWeapon(MobModelSetupPreset p, GameObject visual, (Vector3 pos, Quaternion rot, Vector3 scale)? keptPose,
			AnimationClip alignClip)
		{
			if (p.WeaponPrefab == null) return null;

			var bone = visual.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == p.WeaponBone);
			if (bone == null)
			{
				Debug.LogError($"{Log} Weapon bone '{p.WeaponBone}' not found on the model — weapon not attached.");
				return null;
			}

			var weapon = (WeaponModel)PrefabUtility.InstantiatePrefab(p.WeaponPrefab, bone);
			if (keptPose.HasValue)
			{
				weapon.transform.localPosition = keptPose.Value.pos;
				weapon.transform.localRotation = keptPose.Value.rot;
				weapon.transform.localScale = keptPose.Value.scale;
				Debug.Log($"{Log} Weapon '{weapon.name}' re-attached to '{bone.name}' with the previous (tuned) pose.");
			}
			else
			{
				float boneScale = Mathf.Max(bone.lossyScale.x, 0.0001f);
				weapon.transform.localScale = Vector3.one * (p.WeaponScale / boneScale);

				if (alignClip != null)
				{
					// Pose the body in the align clip's first frame, then point the barrel along the character's
					// forward (prefab root +Z), level with the ground, grip at the bone + offset. The local pose under
					// the bone is what's stored, so it follows the hand in every other clip.
					var root = visual.transform.parent;
					AnimationMode.StartAnimationMode();
					try
					{
						AnimationMode.BeginSampling();
						AnimationMode.SampleAnimationClip(visual, alignClip, 0f);
						AnimationMode.EndSampling();

						Vector3 fwd = root != null ? root.forward : Vector3.forward;
						Quaternion worldRot = Quaternion.LookRotation(fwd, Vector3.up);
						Vector3 worldPos = bone.position + worldRot * p.WeaponLocalPosition;
						weapon.transform.localRotation = Quaternion.Inverse(bone.rotation) * worldRot;
						weapon.transform.localPosition = bone.InverseTransformPoint(worldPos);
					}
					finally
					{
						AnimationMode.StopAnimationMode();
					}
					Debug.Log($"{Log} Weapon '{weapon.name}' attached to '{bone.name}', aligned to '{alignClip.name}' frame 0 (tune in the prefab if needed, re-runs keep it).");
				}
				else
				{
					weapon.transform.localPosition = p.WeaponLocalPosition / boneScale;
					weapon.transform.localRotation = Quaternion.Euler(p.WeaponLocalEuler);
					Debug.Log($"{Log} Weapon '{weapon.name}' attached to '{bone.name}' with WeaponLocalEuler (no align clip) — tune it in the prefab.");
				}
			}

			if (weapon.Muzzle == null)
				Debug.LogWarning($"{Log} Weapon '{weapon.name}' has no Muzzle — shots fall back to the config offsets.", weapon);
			return weapon;
		}

		private static float MeasureHeight(GameObject go) => ComputeBounds(go).size.y;

		private static Bounds ComputeBounds(GameObject go)
		{
			// Body only: a held weapon must not change the measured height between re-runs.
			var renderers = go.GetComponentsInChildren<Renderer>(true)
				.Where(r => r.GetComponentInParent<WeaponModel>(true) == null)
				.ToArray();
			if (renderers.Length == 0) return default;
			var b = renderers[0].bounds;
			for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
			return b;
		}

		// --- Configs ----------------------------------------------------------------------------------

		private static void AssignConfigs(MobModelSetupPreset p, CrowdAnimationLibrary library)
		{
			foreach (var config in p.Configs.Where(c => c != null))
			{
				if (config.Prefab != p.Prefab)
					Debug.LogWarning($"{Log} Config '{config.name}' uses prefab '{(config.Prefab ? config.Prefab.name : "none")}', not '{p.Prefab.name}' — the VAT won't match its colliders.", config);

				if (library == null) continue;
				var so = new SerializedObject(config);
				so.FindProperty("_crowdLibrary").objectReferenceValue = library;
				so.ApplyModifiedPropertiesWithoutUndo();
				EditorUtility.SetDirty(config);
				Debug.Log($"{Log} {config.name}.CrowdLibrary = {library.name}.", config);
			}
		}
	}
}
