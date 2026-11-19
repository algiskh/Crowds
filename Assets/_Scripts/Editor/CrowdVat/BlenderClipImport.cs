using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Brings a clip animated in Blender onto a Mixamo model whose hierarchy starts at "mixamorig:Hips".
	/// Blender's FBX exporter always writes the armature object as an extra root node ("Armature"), so the clip's
	/// paths read "Armature/mixamorig:Hips/..." and don't bind; with "Add Leaf Bones" it also adds "*_end" bones.
	/// This writes an .anim copy with the root node removed (its transform is folded into the hips curves and its
	/// scale into every bone position), "_end" bones dropped, then validates bindings / hip height and puts the clip
	/// into an AnimatorController state. Logs with "[BlenderClip]".
	/// </summary>
	public static class BlenderClipImport
	{
		private const string Log = "[BlenderClip]";
		private const string HipsBone = "mixamorig:Hips";

		// Zombie1: Mixamo "Zombie Running" holds the mesh + rig; "Wobbling" was posed in Blender (ice skid).
		private const string Zombie1Folder = "Assets/Model/Zombie1";
		private const string Zombie1Model = Zombie1Folder + "/model@Zombie Running.fbx";
		private const string Zombie1Idle = Zombie1Folder + "/model@Zombie Idle.fbx";
		private const string Zombie1Wobbling = Zombie1Folder + "/model@Wobbling.fbx";
		private const string Zombie1Controller = "Assets/_Animations/Mob/Zombie1.controller";

		[MenuItem("Tools/Crowds/Zombie1 ▸ Import Wobbling (skid) Clip")]
		private static void ImportZombie1Wobbling()
		{
			var clip = Import(Zombie1Wobbling, Zombie1Model, "Assets/_Animations/Mob/Zombie1_skid.anim", loop: true, referenceClipFbx: Zombie1Idle, lastFrame: 45);
			if (clip != null)
				SetState(Zombie1Controller, "skid", clip);
		}

		/// <summary>Imports <paramref name="fbxPath"/> (Blender export) as a clip bound to <paramref name="modelPath"/>'s hierarchy.</summary>
		public static AnimationClip Import(string fbxPath, string modelPath, string outPath, bool loop, string referenceClipFbx = null, int lastFrame = -1)
		{
			if (AssetImporter.GetAtPath(fbxPath) is not ModelImporter)
			{
				Debug.LogError($"{Log} {fbxPath} not found / not imported yet (focus Unity and retry).");
				return null;
			}

			// Generic, scale aligned with the model, one looping take.
			var source = MobModelPipeline.SetupClip(fbxPath, Path.GetFileNameWithoutExtension(outPath) + "_raw", loop, isModel: false, modelPath);
			if (source == null) return null;

			var fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
			var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

			// The extra root: the parent of the hips in the Blender file, if it isn't the file root itself.
			var hipsInFbx = fbxRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == HipsBone);
			if (hipsInFbx == null) { Debug.LogError($"{Log} {fbxPath} has no '{HipsBone}'."); return null; }
			var armature = hipsInFbx.parent != fbxRoot.transform ? hipsInFbx.parent : null;
			string prefix = armature != null ? AnimationUtility.CalculateTransformPath(armature, fbxRoot.transform) + "/" : "";

			// Unity often makes Blender's "Armature" node the asset root itself (then the paths already bind, but its
			// rotation/scale sit on the root, outside the clip). Either way: express the hips' parent space of the
			// Blender file in the model's hips-parent space and fold that into the clip.
			var modelHips = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == HipsBone);
			if (modelHips == null) { Debug.LogError($"{Log} {modelPath} has no '{HipsBone}'."); return null; }
			Matrix4x4 a = modelHips.parent.localToWorldMatrix.inverse * hipsInFbx.parent.localToWorldMatrix;
			Vector3 aPos = a.GetColumn(3);
			Quaternion aRot = a.rotation;
			Vector3 aScaleV = a.lossyScale;
			float aScale = aScaleV.x;
			if (Mathf.Abs(aScaleV.y - aScale) > aScale * 0.01f || Mathf.Abs(aScaleV.z - aScale) > aScale * 0.01f)
				Debug.LogWarning($"{Log} Hips parent space has a non-uniform scale {aScaleV} — using {aScale}.");
			Debug.Log($"{Log} {Path.GetFileName(fbxPath)}: extra node '{(armature != null ? armature.name : "none")}', hips parent " +
			          $"'{hipsInFbx.parent.name}' → model space: pos {aPos}, rot {aRot.eulerAngles}, scale {aScale:0.####}.");

			// Units: the parent-space scale alone isn't reliable after a Blender round trip (unit scale, armature object
			// scale, import globalScale all mix). Mixamo bones are rigid, so the exact factor is the model's bone length
			// over the clip's: median over bones below the hips (their positions are in bone space, unaffected by the pose).
			{
				var ratios = new List<float>();
				var byPath = AnimationUtility.GetCurveBindings(source)
					.Where(b => b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalPosition"))
					.GroupBy(b => b.path);
				foreach (var g in byPath)
				{
					string path = prefix.Length > 0 && g.Key.StartsWith(prefix) ? g.Key.Substring(prefix.Length) : g.Key;
					if (path == HipsBone || !path.StartsWith(HipsBone + "/") || path.EndsWith("_end")) continue;
					var bone = model.transform.Find(path);
					if (bone == null || bone.localPosition.magnitude < 0.02f) continue;
					var p = Vector3.zero;
					foreach (var b in g)
					{
						float v = AnimationUtility.GetEditorCurve(source, b).Evaluate(0f);
						if (b.propertyName.EndsWith(".x")) p.x = v; else if (b.propertyName.EndsWith(".y")) p.y = v; else p.z = v;
					}
					if (p.magnitude * aScale > 1e-6f) ratios.Add(bone.localPosition.magnitude / (p.magnitude * aScale));
				}

				if (ratios.Count > 0)
				{
					ratios.Sort();
					float k = ratios[ratios.Count / 2];
					Debug.Log($"{Log} Bone lengths vs model: x{k:0.####} (median of {ratios.Count}, range {ratios[0]:0.###}..{ratios[^1]:0.###}) — applied to all positions.");
					if (Mathf.Abs(k - 1f) > 0.001f) aScale *= k;
				}
				else
				{
					Debug.LogWarning($"{Log} No bone position curves to compare with the model — scale left as is.");
				}
			}

			var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(outPath), frameRate = source.frameRate };
			int dropped = 0, copied = 0;
			var hipsCurves = new Dictionary<string, AnimationCurve>();

			foreach (var b in AnimationUtility.GetCurveBindings(source))
			{
				var curve = AnimationUtility.GetEditorCurve(source, b);
				string path = b.path;
				if (prefix.Length > 0)
				{
					if (path + "/" == prefix) { dropped++; continue; }         // the root node itself
					if (path.StartsWith(prefix)) path = path.Substring(prefix.Length);
				}
				string last = path.Substring(path.LastIndexOf('/') + 1);
				// Blender leaf bones ("HeadTop_End_end"); Mixamo's own ends are "_End" (capital E) and are kept.
				if (last.EndsWith("_end"))
				{
					dropped++;
					continue;
				}

				if (b.type == typeof(Transform) && path == HipsBone)
				{
					hipsCurves[b.propertyName] = curve; // folded below
					continue;
				}

				if (b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalPosition") && !Mathf.Approximately(aScale, 1f))
					curve = Scaled(curve, aScale);

				AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, b.type, b.propertyName), curve);
				copied++;
			}

			WriteHips(clip, hipsCurves, aPos, aRot, aScale);
			TrimStaticTail(clip, lastFrame);
			FixFacingAndGround(clip, model, string.IsNullOrEmpty(referenceClipFbx) ? null : FirstClip(referenceClipFbx));

			var settings = AnimationUtility.GetAnimationClipSettings(clip);
			settings.loopTime = loop;
			AnimationUtility.SetAnimationClipSettings(clip, settings);

			var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
			if (existing != null)
			{
				EditorUtility.CopySerialized(clip, existing);
				existing.name = clip.name;
				EditorUtility.SetDirty(existing);
				Object.DestroyImmediate(clip);
				clip = existing;
			}
			else
			{
				AssetDatabase.CreateAsset(clip, outPath);
			}
			AssetDatabase.SaveAssets();
			Debug.Log($"{Log} Wrote {outPath}: {copied} curves + hips, {dropped} dropped (root node / _end bones), length {clip.length:0.##}s.", clip);

			clip = MobModelPipeline.MakeInPlaceIfDrifting(clip, outPath);
			MobModelPipeline.ValidateBindings(model, clip);
			LogHipHeight(clip, "new clip");
			if (!string.IsNullOrEmpty(referenceClipFbx))
			{
				var reference = FirstClip(referenceClipFbx);
				if (reference != null) LogHipHeight(reference, $"reference '{reference.name}'");
			}
			return clip;
		}

		/// <summary>Hips: new = rootTRS · hips (root scale is uniform, so it only scales the position).</summary>
		private static void WriteHips(AnimationClip clip, Dictionary<string, AnimationCurve> c, Vector3 aPos, Quaternion aRot, float aScale)
		{
			if (c.Count == 0) { Debug.LogWarning($"{Log} No hips curves in the clip."); return; }

			var times = c.Values.SelectMany(x => x.keys.Select(k => k.time)).Distinct().OrderBy(t => t).ToArray();
			bool hasQuat = c.ContainsKey("m_LocalRotation.w");
			bool hasEuler = c.ContainsKey("localEulerAnglesRaw.x");
			float Eval(string prop, float t, float def) => c.TryGetValue(prop, out var cv) ? cv.Evaluate(t) : def;

			var px = new List<Keyframe>(); var py = new List<Keyframe>(); var pz = new List<Keyframe>();
			var qx = new List<Keyframe>(); var qy = new List<Keyframe>(); var qz = new List<Keyframe>(); var qw = new List<Keyframe>();
			Quaternion prev = Quaternion.identity;
			bool hasPos = c.ContainsKey("m_LocalPosition.x");

			foreach (float t in times)
			{
				if (hasPos)
				{
					var p = new Vector3(Eval("m_LocalPosition.x", t, 0), Eval("m_LocalPosition.y", t, 0), Eval("m_LocalPosition.z", t, 0));
					p = aPos + aRot * (p * aScale);
					px.Add(new Keyframe(t, p.x)); py.Add(new Keyframe(t, p.y)); pz.Add(new Keyframe(t, p.z));
				}

				Quaternion q = hasQuat
					? new Quaternion(Eval("m_LocalRotation.x", t, 0), Eval("m_LocalRotation.y", t, 0), Eval("m_LocalRotation.z", t, 0), Eval("m_LocalRotation.w", t, 1)).normalized
					: hasEuler
						? Quaternion.Euler(Eval("localEulerAnglesRaw.x", t, 0), Eval("localEulerAnglesRaw.y", t, 0), Eval("localEulerAnglesRaw.z", t, 0))
						: Quaternion.identity;
				q = aRot * q;
				if (Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); // keep the curve continuous
				prev = q;
				qx.Add(new Keyframe(t, q.x)); qy.Add(new Keyframe(t, q.y)); qz.Add(new Keyframe(t, q.z)); qw.Add(new Keyframe(t, q.w));
			}

			void Set(string prop, List<Keyframe> keys)
			{
				var curve = new AnimationCurve(keys.ToArray());
				for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
				AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(HipsBone, typeof(Transform), prop), curve);
			}

			if (hasPos) { Set("m_LocalPosition.x", px); Set("m_LocalPosition.y", py); Set("m_LocalPosition.z", pz); }
			Set("m_LocalRotation.x", qx); Set("m_LocalRotation.y", qy); Set("m_LocalRotation.z", qz); Set("m_LocalRotation.w", qw);

			// Hips scale curves (normally constant 1) are kept as is.
			foreach (var kv in c.Where(kv => kv.Key.StartsWith("m_LocalScale")))
				AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(HipsBone, typeof(Transform), kv.Key), kv.Value);
		}

		private static AnimationCurve Scaled(AnimationCurve c, float s)
		{
			var keys = c.keys;
			for (int i = 0; i < keys.Length; i++)
			{
				keys[i].value *= s;
				keys[i].inTangent *= s;
				keys[i].outTangent *= s;
			}
			return new AnimationCurve(keys) { preWrapMode = c.preWrapMode, postWrapMode = c.postWrapMode };
		}

		/// <summary>
		/// Blender bakes the whole scene range (250 frames by default): the pose stops changing at the action's last key
		/// and the rest is a still tail. Cut every curve at the last moment any of them still changes, and no later than
		/// <paramref name="lastFrame"/> (when > 0; e.g. 45 for Zombie1 Wobbling — frame 45 repeats frame 0).
		/// </summary>
		private static void TrimStaticTail(AnimationClip clip, int lastFrame)
		{
			var bindings = AnimationUtility.GetCurveBindings(clip);
			var curves = bindings.Select(b => AnimationUtility.GetEditorCurve(clip, b)).ToArray();
			float end = 0f;
			foreach (var c in curves)
				for (int i = c.length - 1; i > 0; i--)
					if (Mathf.Abs(c.keys[i].value - c.keys[i - 1].value) > 1e-4f * Mathf.Max(1f, Mathf.Abs(c.keys[i].value)))
					{
						end = Mathf.Max(end, c.keys[i].time);
						break;
					}

			float length = clip.length;
			if (lastFrame > 0) end = end > 0f ? Mathf.Min(end, lastFrame / clip.frameRate) : lastFrame / clip.frameRate;
			if (end <= 0f || end >= length - 0.5f / clip.frameRate) return;

			for (int k = 0; k < bindings.Length; k++)
			{
				var keys = curves[k].keys.Where(x => x.time <= end + 1e-4f).ToArray();
				AnimationUtility.SetEditorCurve(clip, bindings[k], new AnimationCurve(keys) { preWrapMode = curves[k].preWrapMode, postWrapMode = curves[k].postWrapMode });
			}
			Debug.Log($"{Log} Trimmed the still tail: {length:0.##}s → {end:0.##}s ({Mathf.RoundToInt(end * clip.frameRate)} frames).");
		}

		/// <summary>
		/// Pose fix-ups measured with forward kinematics straight from the curves (model bind values for unanimated
		/// channels), so no scene sampling is involved:
		///  1. Facing: the hips' yaw at t=0 is compared with the reference clip's; a difference that rounds to a
		///     multiple of 90° (an export axis mismatch) is rotated out of the hips rotation/position.
		///  2. Ground: Blender's rest pose may keep the hips at the origin (feet under the floor) — the hips are shifted so
		///     the lowest foot/toe point over the whole clip stands where the reference clip's feet do at t=0 (the
		///     model's bind pose without a reference).
		/// </summary>
		private static void FixFacingAndGround(AnimationClip clip, GameObject model, AnimationClip reference)
		{
			var root = model.transform;
			var hips = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == HipsBone);
			var feet = root.GetComponentsInChildren<Transform>(true)
				.Where(t => t.name.StartsWith("mixamorig:") && (t.name.Contains("Foot") || t.name.Contains("Toe")) && !t.name.EndsWith("_end"))
				.ToArray();
			if (hips == null || feet.Length == 0) { Debug.LogWarning($"{Log} No hips/feet on the model — facing/ground left as is."); return; }
			string hipsPath = AnimationUtility.CalculateTransformPath(hips, root);

			// 1. Facing.
			if (reference != null)
			{
				float yawRef = Yaw(World(hips, root, CurvesByPath(reference), 0f).rotation);
				float yawClip = Yaw(World(hips, root, CurvesByPath(clip), 0f).rotation);
				float delta = Mathf.DeltaAngle(yawRef, yawClip);
				float snap = Mathf.Round(delta / 90f) * 90f;
				Debug.Log($"{Log} Hips yaw: clip {yawClip:0.#}°, reference {yawRef:0.#}° (diff {delta:0.#}°).");
				if (Mathf.Abs(snap) > 0.1f && CurvesByPath(clip).TryGetValue(hipsPath, out var c))
				{
					var fix = Quaternion.AngleAxis(-snap, Vector3.up); // hips parent = model root (identity space)
					var times = c.Values.SelectMany(x => x.keys.Select(k => k.time)).Distinct().OrderBy(t => t).ToArray();
					var pos = new Vector3[times.Length];
					var rot = new Quaternion[times.Length];
					Quaternion prev = Quaternion.identity;
					for (int i = 0; i < times.Length; i++)
					{
						var (p, r, _) = Local(hips, c, times[i]);
						pos[i] = fix * p;
						rot[i] = fix * r;
						if (Quaternion.Dot(prev, rot[i]) < 0f) rot[i] = new Quaternion(-rot[i].x, -rot[i].y, -rot[i].z, -rot[i].w);
						prev = rot[i];
					}
					foreach (var b in AnimationUtility.GetCurveBindings(clip).Where(b => b.path == hipsPath && b.type == typeof(Transform)
						         && (b.propertyName.StartsWith("m_LocalPosition") || b.propertyName.StartsWith("m_LocalRotation") || b.propertyName.StartsWith("localEuler"))))
						AnimationUtility.SetEditorCurve(clip, b, null);
					string[] pn = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
					string[] rn = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
					for (int a = 0; a < 3; a++) SetCurve(clip, hipsPath, pn[a], times, pos.Select(v => v[a]).ToArray());
					for (int a = 0; a < 4; a++) SetCurve(clip, hipsPath, rn[a], times, rot.Select(q => q[a]).ToArray());
					Debug.Log($"{Log} Facing fixed: hips rotated {-snap:0}° about the vertical (export axis mismatch).");
				}
			}

			// 2. Ground.
			float ground = reference != null
				? feet.Min(f => World(f, root, CurvesByPath(reference), 0f).GetColumn(3).y)
				: feet.Min(f => World(f, root, new Dictionary<string, Dictionary<string, AnimationCurve>>(), 0f).GetColumn(3).y);
			var clipCurves = CurvesByPath(clip);
			float lowest = float.MaxValue;
			int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));
			for (int i = 0; i <= frames; i++)
			{
				float t = Mathf.Min(clip.length, i / clip.frameRate);
				lowest = Mathf.Min(lowest, feet.Min(f => World(f, root, clipCurves, t).GetColumn(3).y));
			}

			float dy = ground - lowest;
			if (Mathf.Abs(dy) < 0.001f) { Debug.Log($"{Log} Feet already on the ground (y {lowest:0.###})."); return; }
			var binding = EditorCurveBinding.FloatCurve(hipsPath, typeof(Transform), "m_LocalPosition.y");
			var cy = AnimationUtility.GetEditorCurve(clip, binding);
			if (cy == null) { Debug.LogWarning($"{Log} No hips Y curve — can't ground the clip."); return; }
			var keys = cy.keys;
			for (int i = 0; i < keys.Length; i++) keys[i].value += dy;
			AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys) { preWrapMode = cy.preWrapMode, postWrapMode = cy.postWrapMode });
			Debug.Log($"{Log} Grounded: lowest foot y {lowest:0.###} → {ground:0.###} ({(reference != null ? $"'{reference.name}' feet" : "bind pose")}), hips moved {dy:+0.###;-0.###}.");
		}

		private static float Yaw(Quaternion q)
		{
			var f = q * Vector3.forward;
			f.y = 0f;
			return f.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
		}

		private static Dictionary<string, Dictionary<string, AnimationCurve>> CurvesByPath(AnimationClip clip)
		{
			var result = new Dictionary<string, Dictionary<string, AnimationCurve>>();
			foreach (var b in AnimationUtility.GetCurveBindings(clip))
			{
				if (b.type != typeof(Transform)) continue;
				if (!result.TryGetValue(b.path, out var d)) result[b.path] = d = new Dictionary<string, AnimationCurve>();
				d[b.propertyName] = AnimationUtility.GetEditorCurve(clip, b);
			}
			return result;
		}

		/// <summary>Local TRS of <paramref name="bone"/> at <paramref name="t"/>: curve values, bind values for the rest.</summary>
		private static (Vector3 pos, Quaternion rot, Vector3 scale) Local(Transform bone, Dictionary<string, AnimationCurve> c, float t)
		{
			if (c == null) return (bone.localPosition, bone.localRotation, bone.localScale);
			float E(string prop, float def) => c.TryGetValue(prop, out var cv) && cv != null ? cv.Evaluate(t) : def;

			var p = bone.localPosition;
			p = new Vector3(E("m_LocalPosition.x", p.x), E("m_LocalPosition.y", p.y), E("m_LocalPosition.z", p.z));
			Quaternion r = bone.localRotation;
			if (c.ContainsKey("m_LocalRotation.w"))
				r = new Quaternion(E("m_LocalRotation.x", r.x), E("m_LocalRotation.y", r.y), E("m_LocalRotation.z", r.z), E("m_LocalRotation.w", r.w)).normalized;
			else if (c.ContainsKey("localEulerAnglesRaw.x"))
				r = Quaternion.Euler(E("localEulerAnglesRaw.x", 0), E("localEulerAnglesRaw.y", 0), E("localEulerAnglesRaw.z", 0));
			else if (c.ContainsKey("localEulerAngles.x"))
				r = Quaternion.Euler(E("localEulerAngles.x", 0), E("localEulerAngles.y", 0), E("localEulerAngles.z", 0));
			var s = bone.localScale;
			s = new Vector3(E("m_LocalScale.x", s.x), E("m_LocalScale.y", s.y), E("m_LocalScale.z", s.z));
			return (p, r, s);
		}

		/// <summary>Bone matrix in the model root's space (the root itself is not animated).</summary>
		private static Matrix4x4 World(Transform bone, Transform root, Dictionary<string, Dictionary<string, AnimationCurve>> curves, float t)
		{
			var m = Matrix4x4.identity;
			for (var x = bone; x != null && x != root; x = x.parent)
			{
				curves.TryGetValue(AnimationUtility.CalculateTransformPath(x, root), out var c);
				var (p, r, s) = Local(x, c, t);
				m = Matrix4x4.TRS(p, r, s) * m;
			}
			return m;
		}

		private static void SetCurve(AnimationClip clip, string path, string prop, float[] times, float[] values)
		{
			var curve = new AnimationCurve(times.Select((t, i) => new Keyframe(t, values[i])).ToArray());
			for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
			AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop), curve);
		}

		private static AnimationClip FirstClip(string fbxPath) =>
			AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

		/// <summary>Hips local position at t=0 (zero if the clip has no hips position curves).</summary>
		private static Vector3 HipPosition(AnimationClip clip)
		{
			if (clip == null) return Vector3.zero;
			var v = Vector3.zero;
			foreach (var b in AnimationUtility.GetCurveBindings(clip))
			{
				if (b.type != typeof(Transform) || !b.path.EndsWith(HipsBone)) continue;
				var c = AnimationUtility.GetEditorCurve(clip, b);
				if (c == null) continue;
				if (b.propertyName == "m_LocalPosition.x") v.x = c.Evaluate(0f);
				else if (b.propertyName == "m_LocalPosition.y") v.y = c.Evaluate(0f);
				else if (b.propertyName == "m_LocalPosition.z") v.z = c.Evaluate(0f);
			}
			return v;
		}

		private static void LogHipHeight(AnimationClip clip, string label)
		{
			Debug.Log($"{Log} Hip height ({label}): {HipPosition(clip).y:0.###} at t=0.");
		}

		/// <summary>Adds (or repoints) a base-layer state; other states and the default state stay untouched.</summary>
		public static void SetState(string controllerPath, string stateName, AnimationClip clip)
		{
			var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
			if (controller == null) { Debug.LogError($"{Log} Controller {controllerPath} not found."); return; }

			var sm = controller.layers[0].stateMachine;
			var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName)
			            ?? sm.AddState(stateName, new Vector3(300, sm.states.Length * 80, 0));
			state.motion = clip;
			EditorUtility.SetDirty(controller);
			AssetDatabase.SaveAssets();
			Debug.Log($"{Log} {controllerPath}: state '{stateName}' → {clip.name}.", controller);
		}
	}
}
