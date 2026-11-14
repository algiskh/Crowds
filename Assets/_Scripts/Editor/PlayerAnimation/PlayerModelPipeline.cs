using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Player body pipeline (counterpart of MobModelPipeline for the Humanoid player):
	/// 1. <see cref="ScanClips"/> — maps animation FBXs in <c>Rifle/</c>, <c>Pistol/</c>, <c>Common/</c> next to the model to
	///    controller slots by file-name keywords; fills gaps (Aim = Idle, mirrored strafes/diagonals, diagonal → straight).
	/// 2. <see cref="Run"/> — textures + URP material, model as Humanoid, every clip as Humanoid (Copy From the model's
	///    avatar, root baked into pose, loop/mirror/trim), creates Player.controller if missing and puts the clips into its
	///    slots, then builds the body under the Player of the open scene (Animator, PlayerAnimator, WeaponHandIK,
	///    Player._bodyAnimator, placeholder renderers off). The scene is marked dirty — save it.
	/// Re-runnable. Docs/PlayerAnimation.md.
	/// </summary>
	public static class PlayerModelPipeline
	{
		private const string Log = "[PlayerModel]";
		private const string ControllerPath = "Assets/_Animations/Player/Player.controller";
		private const string RightHandBone = "mixamorig:RightHand";

		// Order of the children in the Locomotion_* blend trees (PlayerAnimatorControllerBuilder.CreateLocomotion).
		private static readonly string[] LocomotionOrder =
			{ "Idle", "Forward", "Backward", "Left", "Right", "ForwardLeft", "ForwardRight", "BackwardLeft", "BackwardRight" };

		private static readonly string[] ClipFolders = { "Rifle", "Pistol", "Common" };

		private static readonly string[] SkipWords = { "turn", "jump", "crouch", "kneel", "prone", "sprint", "pose" };

		[MenuItem("Tools/Crowds/Run Selected Player Model Preset")]
		private static void RunSelected()
		{
			if (Selection.activeObject is PlayerModelSetupPreset p) Run(p);
			else Debug.LogWarning($"{Log} Select a PlayerModelSetupPreset asset.");
		}

		/// <summary>Select the player FBX (with skin) → creates Assets/_Data/Player/&lt;name&gt;_PlayerModelSetup.asset and scans clips.</summary>
		[MenuItem("Assets/Crowds/Create Player Model Preset")]
		private static void CreatePresetFromSelection()
		{
			var model = Selection.activeObject as GameObject;
			string modelPath = model != null ? AssetDatabase.GetAssetPath(model) : null;
			if (modelPath == null || !modelPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
			{
				Debug.LogWarning($"{Log} Select the player's FBX (with skin) in the Project window.");
				return;
			}

			const string folder = "Assets/_Data/Player";
			if (!AssetDatabase.IsValidFolder(folder))
				AssetDatabase.CreateFolder("Assets/_Data", "Player");
			string path = $"{folder}/{Path.GetFileNameWithoutExtension(modelPath)}_PlayerModelSetup.asset";
			var preset = AssetDatabase.LoadAssetAtPath<PlayerModelSetupPreset>(path);
			if (preset == null)
			{
				preset = ScriptableObject.CreateInstance<PlayerModelSetupPreset>();
				preset.Model = model;
				AssetDatabase.CreateAsset(preset, path);
			}
			ScanClips(preset);
			Selection.activeObject = preset;
		}

		// =====================================================================================  scan

		public static void ScanClips(PlayerModelSetupPreset p)
		{
			if (p.Model == null) { Debug.LogError($"{Log} Model is not set.", p); return; }
			string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(p.Model))!.Replace('\\', '/');

			Undo.RecordObject(p, "Scan player clips");
			ResolveTextures(p, folder);

			var found = new Dictionary<PlayerClipSlot, (PlayerModelSetupPreset.ClipEntry entry, float score)>();
			// Only the sorted sub-folders: anything else (e.g. Unsorted/ with whole Mixamo packs) is ignored.
			var files = ClipFolders
				.Select(f => $"{folder}/{f}")
				.Where(Directory.Exists)
				.SelectMany(d => Directory.GetFiles(d, "*.fbx", SearchOption.AllDirectories))
				.Select(f => f.Replace('\\', '/'));
			foreach (var path in files)
			{
				if (path == AssetDatabase.GetAssetPath(p.Model)) continue;
				var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (fbx == null) continue;

				string sub = Path.GetDirectoryName(path)!.Replace('\\', '/').Substring(folder.Length).Trim('/').ToLowerInvariant();
				string name = Path.GetFileNameWithoutExtension(path);
				if (name.Contains('@')) name = name.Substring(name.IndexOf('@') + 1);
				string n = name.ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
				float length = TakeLength(path);

				foreach (var (slot, score) in Classify(sub, n))
				{
					if (found.TryGetValue(slot, out var old) && old.score >= score) continue;
					found[slot] = (new PlayerModelSetupPreset.ClipEntry { Slot = slot, Fbx = fbx, Loop = IsLooping(slot) }, score);

					var e = found[slot].entry;
					if ((slot == PlayerClipSlot.Rifle_Fire || slot == PlayerClipSlot.Pistol_Fire) && p.FireClipLength > 0f && length > p.FireClipLength)
						e.To = p.FireClipLength;
					if (slot == PlayerClipSlot.ThrowCharge) e.To = length * p.ThrowSplit;
					if (slot == PlayerClipSlot.Throw) e.From = length * p.ThrowSplit;
				}
			}

			var result = found.ToDictionary(kv => kv.Key, kv => kv.Value.entry);
			FillGaps(result, "Rifle_");
			FillGaps(result, "Pistol_");
			FillActionGaps(p, result, "Pistol_", "Rifle_");
			FillActionGaps(p, result, "Rifle_", "Pistol_");

			p.Clips = result.Values.OrderBy(e => e.Slot).ToList();
			EditorUtility.SetDirty(p);
			AssetDatabase.SaveAssetIfDirty(p);

			var missing = Enum.GetValues(typeof(PlayerClipSlot)).Cast<PlayerClipSlot>().Where(s => !result.ContainsKey(s)).ToList();
			Debug.Log($"{Log} Scan: {p.Clips.Count} slot(s) filled" +
			          (missing.Count > 0 ? $", empty: {string.Join(", ", missing)}" : ", all slots filled") + ".", p);
		}

		/// <summary>Slot(s) for a file by its sub-folder (rifle/pistol/common) and lower-case name.</summary>
		private static IEnumerable<(PlayerClipSlot slot, float score)> Classify(string sub, string n)
		{
			bool Has(params string[] words) => words.Any(w => n.Contains(w));

			string set = sub.Contains("pistol") ? "Pistol_" : sub.Contains("rifle") ? "Rifle_" : sub.Contains("common") ? null
				: Has("pistol", "handgun") ? "Pistol_" : Has("rifle", "gun") ? "Rifle_" : null;

			// Common clips (any folder).
			if (Has("death", "dying") || n == "die" || n.StartsWith("die "))
			{
				yield return (PlayerClipSlot.Death, 1f);
				yield break;
			}
			if (Has("grenade", "throw", "toss"))
			{
				yield return (PlayerClipSlot.ThrowCharge, 1f);
				yield return (PlayerClipSlot.Throw, 1f);
				yield break;
			}
			if (Has("melee", "butt", "punch", "kick", "stab", "slash", "swing", "bash"))
			{
				yield return (PlayerClipSlot.Melee, 1f);
				yield break;
			}
			if (set == null || Has(SkipWords))
				yield break;

			PlayerClipSlot S(string suffix) => (PlayerClipSlot)Enum.Parse(typeof(PlayerClipSlot), set + suffix);

			if (Has("reload")) { yield return (S("Reload"), 1f); yield break; }
			if (Has("fire", "firing", "shoot", "shot")) { yield return (S("Fire"), 1f); yield break; }
			if (Has("hit", "reaction", "impact")) { yield return (S("Hit"), 1f); yield break; }

			bool back = Has("backward", "back");
			bool fwd = Has("forward");
			bool left = Has("left");
			bool right = Has("right");
			bool moving = back || fwd || left || right || Has("run", "walk", "strafe", "jog");
			if (!moving)
			{
				if (Has("idle")) yield return (S("Idle"), Has("aim") ? 2f : 1f);
				else if (Has("aim")) yield return (S("Aim"), 1f);
				yield break;
			}

			// Running clips beat walking ones for the same slot.
			float score = Has("run", "jog") ? 2f : Has("walk") ? 1f : 1.5f;
			string dir = back && left ? "BackwardLeft" : back && right ? "BackwardRight"
				: fwd && left ? "ForwardLeft" : fwd && right ? "ForwardRight"
				: left ? "Left" : right ? "Right" : back ? "Backward" : "Forward";
			yield return (S(dir), score);
		}

		private static bool IsLooping(PlayerClipSlot slot)
		{
			string s = slot.ToString();
			// ThrowCharge is NOT looped: a non-looping state holds its last frame (the top of the wind-up).
			return LocomotionOrder.Any(l => s.EndsWith("_" + l)) || s.EndsWith("_Aim");
		}

		private static void FillGaps(Dictionary<PlayerClipSlot, PlayerModelSetupPreset.ClipEntry> slots, string set)
		{
			PlayerClipSlot S(string suffix) => (PlayerClipSlot)Enum.Parse(typeof(PlayerClipSlot), set + suffix);

			void Copy(string from, string to, bool mirror)
			{
				if (slots.ContainsKey(S(to)) || !slots.TryGetValue(S(from), out var src)) return;
				slots[S(to)] = new PlayerModelSetupPreset.ClipEntry
				{
					Slot = S(to), Fbx = src.Fbx, Loop = IsLooping(S(to)), From = src.From, To = src.To,
					Mirror = mirror ? !src.Mirror : src.Mirror,
				};
			}

			Copy("Idle", "Aim", false);
			Copy("Aim", "Idle", false);
			Copy("Left", "Right", true);
			Copy("Right", "Left", true);
			Copy("ForwardLeft", "ForwardRight", true);
			Copy("ForwardRight", "ForwardLeft", true);
			Copy("BackwardLeft", "BackwardRight", true);
			Copy("BackwardRight", "BackwardLeft", true);
			// No diagonals at all: the straight clip (the blend tree smooths it).
			Copy("Forward", "ForwardLeft", false);
			Copy("Forward", "ForwardRight", false);
			Copy("Backward", "BackwardLeft", false);
			Copy("Backward", "BackwardRight", false);
		}

		/// <summary>
		/// Missing one-shot actions of a set. An empty state on the UpperBody layer would snap the arms to the
		/// muscle-zero pose, so every action state gets something:
		///  Reload ← the other set's reload (hands go to the other grip for the reload; readable from top-down);
		///  Fire / Hit ← a short piece of the set's own Aim pose (no motion) + PlayerAnimator's procedural kick,
		///  see <see cref="IsProceduralStandIn"/>. Mixamo has no two-handed pistol fire / reload / hit clips.
		/// </summary>
		private static void FillActionGaps(PlayerModelSetupPreset p, Dictionary<PlayerClipSlot, PlayerModelSetupPreset.ClipEntry> slots,
			string set, string other)
		{
			PlayerClipSlot S(string prefix, string suffix) => (PlayerClipSlot)Enum.Parse(typeof(PlayerClipSlot), prefix + suffix);

			var reload = S(set, "Reload");
			if (!slots.ContainsKey(reload) && slots.TryGetValue(S(other, "Reload"), out var src))
				slots[reload] = new PlayerModelSetupPreset.ClipEntry
					{ Slot = reload, Fbx = src.Fbx, Mirror = src.Mirror, From = src.From, To = src.To };

			if (!slots.TryGetValue(S(set, "Aim"), out var aim)) return;
			foreach (var (suffix, length) in new[] { ("Fire", p.FireClipLength > 0f ? p.FireClipLength : 0.3f), ("Hit", 0.4f) })
			{
				var slot = S(set, suffix);
				if (slots.ContainsKey(slot)) continue;
				slots[slot] = new PlayerModelSetupPreset.ClipEntry
					{ Slot = slot, Fbx = aim.Fbx, Mirror = aim.Mirror, From = aim.From, To = aim.From + length };
			}
		}

		/// <summary>A Fire/Hit entry that is a piece of the Aim pose (stand-in made by FillActionGaps or by hand).</summary>
		private static bool IsProceduralStandIn(PlayerModelSetupPreset p, string set, string action)
		{
			var a = p.Clips.FirstOrDefault(e => e.Slot.ToString() == set + "_Aim");
			var x = p.Clips.FirstOrDefault(e => e.Slot.ToString() == set + "_" + action);
			return a != null && x != null && a.Fbx != null && a.Fbx == x.Fbx && Mathf.Approximately(a.From, x.From);
		}

		private static float TakeLength(string path)
		{
			var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
				.FirstOrDefault(c => !c.name.StartsWith("__preview__"));
			return clip != null ? clip.length : 0f;
		}

		private static void ResolveTextures(PlayerModelSetupPreset p, string folder)
		{
			if (p.BaseColor != null && p.Normal != null) return;
			foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
				if (p.BaseColor == null && (name.Contains("basecolor") || name.Contains("albedo") || name.Contains("diffuse")))
					p.BaseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
				else if (p.Normal == null && name.Contains("normal"))
					p.Normal = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
			}
		}

		// =====================================================================================  run

		public static bool Run(PlayerModelSetupPreset p)
		{
			if (p.Model == null) { Debug.LogError($"{Log} Model is not set.", p); return false; }
			var player = Object.FindFirstObjectByType<Player>();
			if (player == null) { Debug.LogError($"{Log} No Player in the open scene — open Assets/Scenes/SampleScene.unity."); return false; }

			try
			{
				string modelPath = AssetDatabase.GetAssetPath(p.Model);
				string folder = Path.GetDirectoryName(modelPath)!.Replace('\\', '/');

				EditorUtility.DisplayProgressBar(Log, "Textures & material", 0.05f);
				SetupTextures(p);
				string matName = string.IsNullOrEmpty(p.MaterialName) ? Path.GetFileNameWithoutExtension(modelPath) + "Material" : p.MaterialName;
				var material = SetupMaterial(p, $"{folder}/{matName}.mat");

				EditorUtility.DisplayProgressBar(Log, "Model (Humanoid)", 0.1f);
				var avatar = SetupModel(modelPath, material);
				if (avatar == null) return false;

				EditorUtility.DisplayProgressBar(Log, "Clips", 0.2f);
				var clips = SetupClips(p, avatar, modelPath);

				EditorUtility.DisplayProgressBar(Log, "Controller", 0.8f);
				if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null)
					PlayerAnimatorControllerBuilder.Create();
				var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
				if (controller == null) { Debug.LogError($"{Log} {ControllerPath} could not be created."); return false; }
				FillController(controller, clips);

				EditorUtility.DisplayProgressBar(Log, "Body in the scene", 0.9f);
				BuildBody(p, player, controller, avatar, clips);

				AssetDatabase.SaveAssets();
				var missing = Enum.GetValues(typeof(PlayerClipSlot)).Cast<PlayerClipSlot>().Where(s => !clips.ContainsKey(s)).ToList();
				Debug.Log($"{Log} Done: {clips.Count} clip slot(s) filled" +
				          (missing.Count > 0 ? $", empty: {string.Join(", ", missing)}" : "") +
				          ". Save the scene, then check it in Play mode.", player);
				return true;
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		// ---------------------------------------------------------------- art

		private static void SetupTextures(PlayerModelSetupPreset p)
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

		private static Material SetupMaterial(PlayerModelSetupPreset p, string path)
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
			mat.SetFloat("_Smoothness", 0.2f);
			mat.SetFloat("_Metallic", 0f);
			EditorUtility.SetDirty(mat);
			AssetDatabase.SaveAssetIfDirty(mat);
			return mat;
		}

		private static Avatar SetupModel(string modelPath, Material material)
		{
			var imp = (ModelImporter)AssetImporter.GetAtPath(modelPath);
			imp.animationType = ModelImporterAnimationType.Human;
			imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
			imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
			imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
			imp.SaveAndReimport();

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

			var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
			if (avatar == null || !avatar.isValid || !avatar.isHuman)
			{
				Debug.LogError($"{Log} {modelPath}: Humanoid avatar is invalid — open the FBX ▸ Rig ▸ Configure… and fix the red bones.",
					AssetDatabase.LoadAssetAtPath<Object>(modelPath));
				return null;
			}
			Debug.Log($"{Log} Model {modelPath}: Humanoid, avatar '{avatar.name}', {names.Count} material(s) → {material.name}.");
			return avatar;
		}

		// ---------------------------------------------------------------- clips

		private static Dictionary<PlayerClipSlot, AnimationClip> SetupClips(PlayerModelSetupPreset p, Avatar avatar, string modelPath)
		{
			var result = new Dictionary<PlayerClipSlot, AnimationClip>();
			var modelImp = (ModelImporter)AssetImporter.GetAtPath(modelPath);
			var groups = p.Clips.Where(e => e.Fbx != null).GroupBy(e => AssetDatabase.GetAssetPath(e.Fbx)).ToList();

			for (int g = 0; g < groups.Count; g++)
			{
				string path = groups[g].Key;
				EditorUtility.DisplayProgressBar(Log, $"Clips: {Path.GetFileName(path)}", 0.2f + 0.6f * g / groups.Count);
				bool isModel = path == modelPath;
				var imp = (ModelImporter)AssetImporter.GetAtPath(path);

				// Frame rate of the take, read before the clips are replaced.
				var take = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
				float fps = take != null && take.frameRate > 0f ? take.frameRate : 30f;

				if (!isModel)
				{
					imp.animationType = ModelImporterAnimationType.Human;
					imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
					imp.sourceAvatar = avatar;
					imp.materialImportMode = ModelImporterMaterialImportMode.None;
					imp.useFileScale = modelImp.useFileScale;
					imp.globalScale = modelImp.globalScale;
				}
				imp.importAnimation = true;

				var template = imp.defaultClipAnimations.FirstOrDefault();
				if (template == null) { Debug.LogError($"{Log} No animation take in {path}."); continue; }

				var list = new List<ModelImporterClipAnimation>();
				foreach (var e in groups[g])
				{
					var c = new ModelImporterClipAnimation
					{
						name = e.Slot.ToString(),
						takeName = template.takeName,
						firstFrame = template.firstFrame,
						lastFrame = template.lastFrame,
						loopTime = e.Loop,
						loopPose = e.Loop,
						mirror = e.Mirror,
						// Movement is driven by code (Apply Root Motion is off). Rotation and height are baked into the
						// pose. Horizontal motion stays in the pose only for one-shot full-body moves (death fall, melee
						// lunge, throw step); for everything else it is extracted as root motion and thus discarded, so
						// even non-"In Place" Mixamo clips (8-way packs) run on the spot.
						lockRootRotation = true, keepOriginalOrientation = true,
						lockRootHeightY = true, keepOriginalPositionY = true,
						lockRootPositionXZ = KeepsHorizontalMotion(e.Slot), keepOriginalPositionXZ = true,
					};
					if (e.From > 0f) c.firstFrame = Mathf.Min(template.lastFrame - 1, template.firstFrame + e.From * fps);
					if (e.To > 0f) c.lastFrame = Mathf.Clamp(template.firstFrame + e.To * fps, c.firstFrame + 1, template.lastFrame);
					list.Add(c);
				}
				imp.clipAnimations = list.ToArray();
				imp.SaveAndReimport();

				var imported = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToList();
				foreach (var e in groups[g])
				{
					var clip = imported.FirstOrDefault(c => c.name == e.Slot.ToString());
					if (clip != null) result[e.Slot] = clip;
					else Debug.LogError($"{Log} Clip {e.Slot} not found in {path} after import.");
				}
			}
			return result;
		}

		private static bool KeepsHorizontalMotion(PlayerClipSlot slot) =>
			slot is PlayerClipSlot.Death or PlayerClipSlot.Melee or PlayerClipSlot.Throw or PlayerClipSlot.ThrowCharge;

		// ---------------------------------------------------------------- controller

		private static void FillController(AnimatorController controller, Dictionary<PlayerClipSlot, AnimationClip> clips)
		{
			AnimationClip Get(string slot) =>
				Enum.TryParse(slot, out PlayerClipSlot s) && clips.TryGetValue(s, out var c) ? c : null;

			var layers = controller.layers;
			foreach (var layer in layers)
			{
				foreach (var child in layer.stateMachine.states)
				{
					var state = child.state;
					string n = state.name;
					if (n.StartsWith("Locomotion_") && state.motion is BlendTree tree)
					{
						string set = n.Substring("Locomotion_".Length);
						var children = tree.children;
						for (int i = 0; i < children.Length && i < LocomotionOrder.Length; i++)
						{
							var clip = Get($"{set}_{LocomotionOrder[i]}");
							if (clip != null) children[i].motion = clip;
						}
						tree.children = children;
						EditorUtility.SetDirty(tree);
						continue;
					}

					// Aim_Rifle → Rifle_Aim, Melee → Melee, Death → Death…
					int us = n.IndexOf('_');
					string slot = us > 0 ? $"{n.Substring(us + 1)}_{n.Substring(0, us)}" : n;
					var stateClip = Get(slot);
					if (stateClip != null)
					{
						state.motion = stateClip;
						EditorUtility.SetDirty(state);
					}
				}
			}
			EditorUtility.SetDirty(controller);
		}

		// ---------------------------------------------------------------- scene body

		private static void BuildBody(PlayerModelSetupPreset p, Player player, AnimatorController controller, Avatar avatar,
			Dictionary<PlayerClipSlot, AnimationClip> clips)
		{
			var root = player.transform;
			var old = root.Find(p.BodyName);
			// Keep the tuned weapon pose across re-runs.
			Vector3? keptPos = null, keptEuler = null;
			if (old != null)
			{
				var oldAnimator = old.GetComponent<PlayerAnimator>();
				if (oldAnimator != null)
				{
					var so = new SerializedObject(oldAnimator);
					keptPos = so.FindProperty("_weaponLocalPosition").vector3Value;
					keptEuler = so.FindProperty("_weaponLocalEuler").vector3Value;
				}
				Undo.DestroyObjectImmediate(old.gameObject);
			}

			var body = (GameObject)PrefabUtility.InstantiatePrefab(p.Model, root);
			Undo.RegisterCreatedObjectUndo(body, "Player body");
			body.name = p.BodyName;
			var t = body.transform;
			t.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
			t.localScale = Vector3.one;

			// Height & feet: scale to TargetHeight, put the soles on the bottom of the placeholder capsule (or the root).
			var bounds = WorldBounds(body);
			if (bounds.size.y > 0.01f && p.TargetHeight > 0f)
				t.localScale = Vector3.one * (p.TargetHeight / bounds.size.y);
			bounds = WorldBounds(body);
			float floorY = root.position.y;
			var capsule = root.GetComponentsInChildren<CapsuleCollider>(true).FirstOrDefault();
			if (capsule != null)
				floorY = capsule.bounds.min.y;
			t.position += Vector3.up * (floorY - bounds.min.y);

			foreach (var name in p.HideRenderers)
				foreach (var tr in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == name && !x.IsChildOf(t)))
					foreach (var r in tr.GetComponents<Renderer>())
					{
						Undo.RecordObject(r, "Hide placeholder");
						r.enabled = false;
					}

			var animator = body.GetComponent<Animator>();
			if (animator == null) animator = body.AddComponent<Animator>();
			animator.runtimeAnimatorController = controller;
			animator.avatar = avatar;
			animator.applyRootMotion = false;
			animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

			var view = body.AddComponent<PlayerAnimator>();
			var vso = new SerializedObject(view);
			vso.FindProperty("_animator").objectReferenceValue = animator;
			if (clips.TryGetValue(PlayerClipSlot.Rifle_Reload, out var rr)) vso.FindProperty("_rifleReloadClipLength").floatValue = rr.length;
			if (clips.TryGetValue(PlayerClipSlot.Pistol_Reload, out var pr)) vso.FindProperty("_pistolReloadClipLength").floatValue = pr.length;
			var hand = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == RightHandBone);
			vso.FindProperty("_weaponBone").objectReferenceValue = hand;
			// First guess = the Gunner's Mini-14 pose in a Mixamo right hand (metres / degrees); tune in the Aim pose.
			vso.FindProperty("_weaponLocalPosition").vector3Value = keptPos ?? new Vector3(-0.0035f, 0.05f, -0.0025f);
			vso.FindProperty("_weaponLocalEuler").vector3Value =
				keptEuler ?? new Quaternion(-0.48768893f, 0.53553534f, 0.541051f, 0.4273469f).eulerAngles;
			vso.FindProperty("_weaponScale").floatValue = 1f;
			// Sets without real Fire / Hit clips get the procedural recoil / flinch.
			void SetList(string field, string action)
			{
				var list = vso.FindProperty(field);
				var sets = ((WeaponAnimationSet[])Enum.GetValues(typeof(WeaponAnimationSet)))
					.Where(s => IsProceduralStandIn(p, s.ToString(), action)).ToList();
				list.arraySize = sets.Count;
				for (int i = 0; i < sets.Count; i++)
					list.GetArrayElementAtIndex(i).enumValueIndex = (int)sets[i];
				if (sets.Count > 0) Debug.Log($"{Log} Procedural {action}: {string.Join(", ", sets)}.");
			}
			SetList("_proceduralRecoilSets", "Fire");
			SetList("_proceduralFlinchSets", "Hit");

			if (p.LeftHandIK)
			{
				var ik = body.AddComponent<WeaponHandIK>();
				var iso = new SerializedObject(ik);
				iso.FindProperty("_animator").objectReferenceValue = animator;
				iso.FindProperty("_stateLayer").stringValue = PlayerAnimator.UpperBodyLayer;
				var states = iso.FindProperty("_disabledStates");
				string[] disabled = { "Reload_Rifle", "Reload_Pistol", "Melee", "ThrowCharge", "Throw", "Death" };
				states.arraySize = disabled.Length;
				for (int i = 0; i < disabled.Length; i++)
					states.GetArrayElementAtIndex(i).stringValue = disabled[i];
				iso.ApplyModifiedPropertiesWithoutUndo();
				vso.FindProperty("_leftHandIK").objectReferenceValue = ik;
			}
			vso.ApplyModifiedPropertiesWithoutUndo();

			var pso = new SerializedObject(player);
			pso.FindProperty("_bodyAnimator").objectReferenceValue = view;
			pso.ApplyModifiedProperties();

			if (hand == null)
				Debug.LogWarning($"{Log} Bone '{RightHandBone}' not found — weapon models won't be shown.", body);
			EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
			Selection.activeGameObject = body;
		}

		private static Bounds WorldBounds(GameObject go)
		{
			var renderers = go.GetComponentsInChildren<Renderer>(true);
			if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
			var b = renderers[0].bounds;
			foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
			return b;
		}
	}
}
