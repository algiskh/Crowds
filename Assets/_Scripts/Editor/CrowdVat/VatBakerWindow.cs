using System.Collections.Generic;
using Scene.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Bakes a mob's SkinnedMeshRenderer + Animator clips into a Vertex Animation Texture (VAT)
	/// and produces a <see cref="CrowdAnimationLibrary"/> (render mesh + position/normal maps + material).
	/// Window: Tools ▸ Crowds ▸ VAT Baker.
	///
	/// It enumerates the Animator controller's states, maps each state name to an
	/// <see cref="AnimationType"/> (via <see cref="AnimationTypes.TryFromStateName"/>), and bakes each
	/// distinct clip once. Data is stored in the mob-root local space so the runtime crowd renderer can
	/// draw it with the mob's world transform via Graphics.DrawMeshInstanced.
	/// </summary>
	public class VatBakerWindow : EditorWindow
	{
		private const int MaxTextureSize = 16384; // GPU texture dimension limit

		private GameObject _prefab;
		private float _fps = 30f;
		private bool _bakeNormals = true;
		private int _textureWidth = 8192; // data is flat-wrapped into this width; decouples from vertex count
		private string _outputFolder = "Assets/_Data/Crowd";

		[MenuItem("Tools/Crowds/VAT Baker")]
		private static void Open() => GetWindow<VatBakerWindow>("VAT Baker");

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Vertex Animation Texture Baker", EditorStyles.boldLabel);
			// Micro-instruction (full docs: Docs/CrowdRenderingFeature.md).
			EditorGUILayout.HelpBox(
				"Convert a rigged FBX mob → GPU-instanced crowd:\n" +
				"1. Assign the spawned mob PREFAB (e.g. Mob.prefab) — NOT the raw FBX. The bake is stored in\n" +
				"   the prefab-root space, so this is what keeps it aligned with the collider/health bar/ground.\n" +
				"2. Bake. Assets go to _Data/Crowd/<prefab>/ (pos/normal maps, mesh, material, _CrowdLibrary).\n" +
				"   It bakes every Animator-controller state whose name matches an AnimationType\n" +
				"   (idle/walk/run/attack/die/throw/throw_cooldown); states sharing a clip share its rows.\n" +
				"3. Assign the baked <Mob>_CrowdLibrary to that mob's MobConfig.CrowdLibrary field (opt-in).\n" +
				"   The mob then renders via CrowdRenderSystem; its Animator + SkinnedMeshRenderer switch off.\n" +
				"Tip: for the real crowd win, bake a DECIMATED low-poly skinned mesh (no code change).\n" +
				"Note: mobs needing a live bone socket (e.g. Grenadier throw origin) must stay classic — no VAT.",
				MessageType.Info);

			_prefab = (GameObject)EditorGUILayout.ObjectField("Mob Prefab", _prefab, typeof(GameObject), false);
			_fps = EditorGUILayout.FloatField("Sample FPS", _fps);
			_bakeNormals = EditorGUILayout.Toggle("Bake Normals", _bakeNormals);
			_textureWidth = EditorGUILayout.IntField("Texture Width", _textureWidth);
			_outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);

			using (new EditorGUI.DisabledScope(_prefab == null))
			{
				if (GUILayout.Button("Bake", GUILayout.Height(32)))
					Bake();
			}
		}

		private void Bake()
		{
			if (_fps < 1f) { Debug.LogError("[VAT] FPS must be >= 1."); return; }

			GameObject instance = Instantiate(_prefab);
			instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

			try
			{
				// Search includes inactive children, and picks the Animator that actually has a controller
				// (a prefab may nest an FBX with its own controller-less Animator, found first otherwise).
				var smr = FindSkinnedMesh(instance);
				var animator = FindAnimatorWithController(instance);
				if (smr == null || smr.sharedMesh == null) { Debug.LogError("[VAT] No SkinnedMeshRenderer with a mesh found."); return; }
				if (animator == null) { Debug.LogError("[VAT] No Animator with an assigned controller found in the prefab."); return; }

				var controller = animator.runtimeAnimatorController as AnimatorController;
				if (controller == null) { Debug.LogError("[VAT] Animator controller is not an AnimatorController asset."); return; }

				// Gather states that map to a known AnimationType, in declaration order.
				var entries = new List<(AnimationType type, AnimationClip clip)>();
				foreach (var layer in controller.layers)
				{
					foreach (var child in layer.stateMachine.states)
					{
						var clip = child.state.motion as AnimationClip;
						if (clip == null) continue;
						if (AnimationTypes.TryFromStateName(child.state.name, out var type))
							entries.Add((type, clip));
					}
				}

				if (entries.Count == 0) { Debug.LogError("[VAT] No states matched an AnimationType."); return; }

				// Assign a frame range per distinct clip (throw/throw_cooldown reuse the run clip -> same rows).
				var clipRanges = new Dictionary<AnimationClip, (int start, int count)>();
				int totalFrames = 0;
				foreach (var (_, clip) in entries)
				{
					if (clipRanges.ContainsKey(clip)) continue;
					int count = Mathf.Max(1, Mathf.CeilToInt(clip.length * _fps));
					clipRanges[clip] = (totalFrames, count);
					totalFrames += count;
				}

				int vertexCount = smr.sharedMesh.vertexCount;

				// Pack (frame*vertexCount + vertexId) as a flat array wrapped into a fixed-width texture,
				// so texture width no longer depends on vertex count (which can exceed the 16384 limit).
				int width = Mathf.Clamp(_textureWidth, 1, MaxTextureSize);
				long totalTexels = (long)vertexCount * totalFrames;
				int height = Mathf.CeilToInt((float)totalTexels / width);
				if (height > MaxTextureSize)
				{
					Debug.LogError($"[VAT] Baked data needs {width}x{height}; height exceeds {MaxTextureSize}. Increase Texture Width or reduce FPS/clips.");
					return;
				}

				var posColors = new Color[width * height];
				var nrmColors = new Color[width * height];
				Vector3[] meshVerts = null;     // captured at row 0 for the static render mesh
				Vector3[] meshNormals = null;
				Vector3 boundsMin = Vector3.positiveInfinity;
				Vector3 boundsMax = Vector3.negativeInfinity;

				var bake = new Mesh();
				var vbuf = new List<Vector3>(vertexCount);
				var nbuf = new List<Vector3>(vertexCount);

				AnimationMode.StartAnimationMode();
				try
				{
					foreach (var kv in clipRanges)
					{
						AnimationClip clip = kv.Key;
						(int start, int count) = kv.Value;

						for (int f = 0; f < count; f++)
						{
							float t = count > 1 ? (float)f / count * clip.length : 0f;

							AnimationMode.BeginSampling();
							// Sample on the Animator's GameObject so the clip's curve paths resolve; the mesh is
							// still baked into the prefab-root local space below (toRoot), which is what runtime draws at.
							AnimationMode.SampleAnimationClip(animator.gameObject, clip, t);
							AnimationMode.EndSampling();

							// BakeMesh gives verts in the SMR's local space; move them into mob-root local space.
							smr.BakeMesh(bake, true);
							Matrix4x4 toRoot = instance.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;

							bake.GetVertices(vbuf);
							bake.GetNormals(nbuf);

							int row = start + f;
							for (int v = 0; v < vertexCount; v++)
							{
								Vector3 p = toRoot.MultiplyPoint3x4(vbuf[v]);
								Vector3 n = nbuf.Count == vertexCount ? toRoot.MultiplyVector(nbuf[v]).normalized : Vector3.up;

								int idx = row * vertexCount + v;
								posColors[idx] = new Color(p.x, p.y, p.z, 1f);
								nrmColors[idx] = new Color(n.x, n.y, n.z, 0f);

								boundsMin = Vector3.Min(boundsMin, p);
								boundsMax = Vector3.Max(boundsMax, p);
							}

							if (row == 0)
							{
								meshVerts = new Vector3[vertexCount];
								meshNormals = new Vector3[vertexCount];
								for (int v = 0; v < vertexCount; v++)
								{
									meshVerts[v] = toRoot.MultiplyPoint3x4(vbuf[v]);
									meshNormals[v] = nbuf.Count == vertexCount ? toRoot.MultiplyVector(nbuf[v]).normalized : Vector3.up;
								}
							}
						}
					}
				}
				finally
				{
					AnimationMode.StopAnimationMode();
				}

				// --- Write output assets ---
				string mobName = _prefab.name;
				string folder = EnsureFolder(_outputFolder, mobName);

				Texture2D posMap = CreateVatTexture(posColors, width, height, $"{mobName}_VAT_Pos");
				Texture2D nrmMap = _bakeNormals ? CreateVatTexture(nrmColors, width, height, $"{mobName}_VAT_Nrm") : null;
				AssetDatabase.CreateAsset(posMap, $"{folder}/{mobName}_VAT_Pos.asset");
				if (nrmMap != null) AssetDatabase.CreateAsset(nrmMap, $"{folder}/{mobName}_VAT_Nrm.asset");

				Mesh renderMesh = BuildRenderMesh(smr.sharedMesh, meshVerts, meshNormals, vertexCount, boundsMin, boundsMax);
				renderMesh.name = $"{mobName}_VAT_Mesh";
				AssetDatabase.CreateAsset(renderMesh, $"{folder}/{mobName}_VAT_Mesh.asset");

				Material material = BuildMaterial(smr, posMap, nrmMap, _bakeNormals, width, height, vertexCount);
				material.name = $"{mobName}_VAT_Mat";
				AssetDatabase.CreateAsset(material, $"{folder}/{mobName}_VAT_Mat.mat");

				var clips = new List<CrowdClip>();
				foreach (var (type, clip) in entries)
				{
					(int start, int count) = clipRanges[clip];
					clips.Add(new CrowdClip { Type = type, StartFrame = start, FrameCount = count, Loop = IsLooping(type) });
				}

				var library = CreateInstance<CrowdAnimationLibrary>();
				library.EditorAssign(renderMesh, posMap, nrmMap, material, vertexCount, totalFrames, width, height, _fps, clips);
				AssetDatabase.CreateAsset(library, $"{folder}/{mobName}_CrowdLibrary.asset");

				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				Debug.Log($"[VAT] Baked '{mobName}': {vertexCount} verts x {totalFrames} frames, {clips.Count} clips -> {folder}", library);
				Selection.activeObject = library;
			}
			finally
			{
				DestroyImmediate(instance);
			}
		}

		private static SkinnedMeshRenderer FindSkinnedMesh(GameObject root)
		{
			foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
				if (s.sharedMesh != null)
					return s;
			return null;
		}

		private static Animator FindAnimatorWithController(GameObject root)
		{
			foreach (var a in root.GetComponentsInChildren<Animator>(true))
				if (a.runtimeAnimatorController != null)
					return a;
			return null;
		}

		private static bool IsLooping(AnimationType type)
		{
			switch (type)
			{
				case AnimationType.Idle:
				case AnimationType.Walk:
				case AnimationType.Run:
					return true;
				default:
					return false; // attack/die/throw/throw_cooldown play once
			}
		}

		private static Texture2D CreateVatTexture(Color[] colors, int width, int height, string name)
		{
			var tex = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
			{
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				anisoLevel = 0
			};
			tex.SetPixels(colors);
			tex.Apply(false, false);
			return tex;
		}

		private static Mesh BuildRenderMesh(Mesh source, Vector3[] verts, Vector3[] normals, int vertexCount, Vector3 boundsMin, Vector3 boundsMax)
		{
			var mesh = new Mesh { indexFormat = source.indexFormat };
			mesh.SetVertices(verts);
			mesh.SetNormals(normals);
			if (source.uv != null && source.uv.Length == vertexCount)
				mesh.SetUVs(0, new List<Vector2>(source.uv));

			// uv2.x = raw vertex index; the shader turns it into a texel via linear = frame*vertexCount + index.
			var vertexColumns = new Vector2[vertexCount];
			for (int v = 0; v < vertexCount; v++)
				vertexColumns[v] = new Vector2(v, 0f);
			mesh.SetUVs(1, new List<Vector2>(vertexColumns));

			mesh.subMeshCount = source.subMeshCount;
			for (int s = 0; s < source.subMeshCount; s++)
				mesh.SetTriangles(source.GetTriangles(s), s);

			Vector3 center = (boundsMin + boundsMax) * 0.5f;
			Vector3 size = boundsMax - boundsMin;
			mesh.bounds = new Bounds(center, size);
			return mesh;
		}

		private static Material BuildMaterial(SkinnedMeshRenderer smr, Texture2D posMap, Texture2D nrmMap, bool bakeNormals,
			int width, int height, int vertexCount)
		{
			var shader = Shader.Find("Crowds/CrowdVat");
			var mat = new Material(shader);
			mat.SetTexture("_PositionMap", posMap);
			if (nrmMap != null) mat.SetTexture("_NormalMap", nrmMap);
			mat.SetFloat("_UseNormalMap", bakeNormals ? 1f : 0f);
			if (bakeNormals) mat.EnableKeyword("_NORMALMAP_ON"); else mat.DisableKeyword("_NORMALMAP_ON");

			// VAT layout uniforms the shader uses to unwrap the flat data.
			mat.SetFloat("_VatWidth", width);
			mat.SetFloat("_VatHeight", height);
			mat.SetFloat("_VatVertexCount", vertexCount);

			// Reuse the source albedo so the crowd looks like the skinned mob.
			var src = smr.sharedMaterial;
			if (src != null)
			{
				Texture baseTex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture;
				if (baseTex != null) mat.SetTexture("_BaseMap", baseTex);
				if (src.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", src.GetColor("_BaseColor"));
			}
			mat.enableInstancing = true;
			return mat;
		}

		private static string EnsureFolder(string root, string mobName)
		{
			if (!AssetDatabase.IsValidFolder(root))
			{
				string parent = System.IO.Path.GetDirectoryName(root).Replace('\\', '/');
				string leaf = System.IO.Path.GetFileName(root);
				if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(parent).Replace('\\', '/'), System.IO.Path.GetFileName(parent));
				AssetDatabase.CreateFolder(parent, leaf);
			}
			string target = $"{root}/{mobName}";
			if (!AssetDatabase.IsValidFolder(target))
				AssetDatabase.CreateFolder(root, mobName);
			return target;
		}
	}
}
