using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Visual check of a mob prefab against a reference picture (e.g. a frame exported from Figma): opens the prefab
	/// in Prefab Mode, points an orthographic Scene camera at it from the chosen side and places the picture as a
	/// semi-transparent sprite over the model (or beside it). The picture's empty margins are trimmed (alpha, or the
	/// corner colour for opaque images) and the character is scaled to the visual's height, feet on the ground.
	/// The overlay object is DontSave — it never ends up in the prefab. Driven by <see cref="MobModelSetupPreset"/>
	/// ("Show Reference" / "Hide Reference"); see .claude/skills/mob-model/SKILL.md.
	/// </summary>
	public static class MobReferenceOverlay
	{
		private const string OverlayName = "__MobReferenceOverlay";
		private const string Log = "[MobRef]";

		public static void Show(MobModelSetupPreset p, bool frameCamera = true)
		{
			if (p.Prefab == null) { Debug.LogError($"{Log} Preset '{p.name}' has no Prefab.", p); return; }
			if (p.ReferenceImage == null) { Debug.LogError($"{Log} Preset '{p.name}' has no Reference Image.", p); return; }

			string prefabPath = AssetDatabase.GetAssetPath(p.Prefab);
			var stage = PrefabStageUtility.GetCurrentPrefabStage();
			if (stage == null || stage.assetPath != prefabPath)
				stage = PrefabStageUtility.OpenPrefab(prefabPath);
			if (stage == null) { Debug.LogError($"{Log} Can't open {prefabPath} in Prefab Mode.", p); return; }

			var root = stage.prefabContentsRoot;
			DestroyOverlay(stage.scene);

			if (!TryGetVisualBounds(root, p.VisualName, out var bounds))
			{
				Debug.LogError($"{Log} No renderers under '{root.name}' to measure.", p);
				return;
			}

			var tex = LoadReadable(p.ReferenceImage);
			if (tex == null) { Debug.LogError($"{Log} Can't read the reference image.", p); return; }
			var content = FindContentRect(tex);

			float height = bounds.size.y * Mathf.Max(0.01f, p.ReferenceScale);
			float ppu = content.height / height;
			// Pivot: horizontal centre of the content, its bottom row → the feet.
			var pivot = new Vector2(content.center.x / tex.width, content.yMin / tex.height);
			var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect);
			sprite.name = tex.name;
			sprite.hideFlags = HideFlags.DontSave;

			var go = new GameObject(OverlayName) { hideFlags = HideFlags.DontSave };
			SceneManager.MoveGameObjectToScene(go, stage.scene);
			var sr = go.AddComponent<SpriteRenderer>();
			sr.sprite = sprite;
			sr.color = new Color(1f, 1f, 1f, p.ReferenceMode == MobModelSetupPreset.ReferenceOverlayMode.Overlay ? p.ReferenceOpacity : 1f);
			sr.flipX = p.ReferenceFlipX;
			var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
			if (shader != null)
				sr.sharedMaterial = new Material(shader) { name = OverlayName, hideFlags = HideFlags.DontSave };

			// Camera direction for the view (the mob faces +Z of its root).
			Transform t = root.transform;
			Vector3 toCamera = p.ReferenceView switch
			{
				MobModelSetupPreset.ReferenceViewSide.Back => -t.forward,
				MobModelSetupPreset.ReferenceViewSide.Left => -t.right,
				MobModelSetupPreset.ReferenceViewSide.Right => t.right,
				_ => t.forward
			};
			Vector3 screenRight = Vector3.Cross(Vector3.up, -toCamera).normalized; // camera's right in world

			float depth = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(toCamera.x), Mathf.Abs(toCamera.y), Mathf.Abs(toCamera.z)));
			Vector3 feet = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
			Vector3 pos = feet + toCamera * (depth + 0.05f)
			              + screenRight * p.ReferenceOffset.x + Vector3.up * p.ReferenceOffset.y * height;
			if (p.ReferenceMode == MobModelSetupPreset.ReferenceOverlayMode.SideBySide)
			{
				float width = content.width / ppu;
				float modelWidth = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(screenRight.x), Mathf.Abs(screenRight.y), Mathf.Abs(screenRight.z)));
				pos += screenRight * (modelWidth + width * 0.5f + 0.1f);
			}
			go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-toCamera, Vector3.up));

			var sv = SceneView.lastActiveSceneView;
			if (sv != null && frameCamera)
			{
				sv.orthographic = true;
				sv.in2DMode = false;
				float frameHeight = Mathf.Max(height, bounds.size.y) * 0.65f;
				Vector3 lookAt = feet + Vector3.up * height * 0.5f;
				if (p.ReferenceMode == MobModelSetupPreset.ReferenceOverlayMode.SideBySide)
					lookAt += screenRight * (content.width / ppu) * 0.5f;
				sv.LookAt(lookAt, Quaternion.LookRotation(-toCamera, Vector3.up), frameHeight, true, false);
				sv.Repaint();
			}

			Debug.Log($"{Log} '{p.ReferenceImage.name}' ({tex.width}x{tex.height}, content {content.width}x{content.height} px) " +
			          $"over '{root.name}' ({p.ReferenceView}, {p.ReferenceMode}): visual height {bounds.size.y:0.###} m, " +
			          $"picture height {height:0.###} m.", p);
		}

		/// <summary>True if the overlay is in the open Prefab Mode stage of this preset's prefab.</summary>
		public static bool IsShownFor(MobModelSetupPreset p)
		{
			var stage = PrefabStageUtility.GetCurrentPrefabStage();
			if (stage == null || p.Prefab == null || stage.assetPath != AssetDatabase.GetAssetPath(p.Prefab)) return false;
			foreach (var go in stage.scene.GetRootGameObjects())
				if (go.name == OverlayName) return true;
			return false;
		}

		public static void Hide()
		{
			var stage = PrefabStageUtility.GetCurrentPrefabStage();
			if (stage != null) DestroyOverlay(stage.scene);
			for (int i = 0; i < SceneManager.sceneCount; i++) DestroyOverlay(SceneManager.GetSceneAt(i));
			SceneView.RepaintAll();
		}

		private static void DestroyOverlay(UnityEngine.SceneManagement.Scene scene)
		{
			if (!scene.IsValid() || !scene.isLoaded) return;
			foreach (var go in scene.GetRootGameObjects())
			{
				if (go.name != OverlayName) continue;
				var sr = go.GetComponent<SpriteRenderer>();
				if (sr != null && sr.sharedMaterial != null && sr.sharedMaterial.hideFlags == HideFlags.DontSave)
					Object.DestroyImmediate(sr.sharedMaterial);
				if (sr != null && sr.sprite != null)
				{
					var tex = sr.sprite.texture;
					Object.DestroyImmediate(sr.sprite);
					if (tex != null && tex.hideFlags == HideFlags.DontSave) Object.DestroyImmediate(tex);
				}
				Object.DestroyImmediate(go);
			}
		}

		private static bool TryGetVisualBounds(GameObject root, string visualName, out Bounds bounds)
		{
			var visual = string.IsNullOrEmpty(visualName) ? null : root.transform.Find(visualName);
			var renderers = (visual != null ? visual.gameObject : root).GetComponentsInChildren<Renderer>(false);
			bounds = default;
			bool any = false;
			foreach (var r in renderers)
			{
				if (r is ParticleSystemRenderer || r is SpriteRenderer || r is LineRenderer || r is TrailRenderer) continue;
				if (!any) { bounds = r.bounds; any = true; }
				else bounds.Encapsulate(r.bounds);
			}
			return any;
		}

		/// <summary>Reads the image file directly (no importer changes needed for isReadable/compression).</summary>
		private static Texture2D LoadReadable(Texture2D asset)
		{
			string path = AssetDatabase.GetAssetPath(asset);
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
			var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
			{
				name = asset.name,
				hideFlags = HideFlags.DontSave,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			if (!tex.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(tex); return null; }
			return tex;
		}

		/// <summary>
		/// Pixel rect of the drawing: by alpha if the picture has transparency, else everything that differs from the
		/// top-left corner colour (a plain Figma frame background). Falls back to the whole picture.
		/// </summary>
		private static RectInt FindContentRect(Texture2D tex)
		{
			var px = tex.GetPixels32();
			int w = tex.width, h = tex.height;
			bool hasAlpha = false;
			for (int i = 0; i < px.Length && !hasAlpha; i += 7) hasAlpha = px[i].a < 128;
			Color32 bg = px[(h - 1) * w]; // top-left (rows are bottom-up)

			int minX = w, minY = h, maxX = -1, maxY = -1;
			for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = px[y * w + x];
				bool content = hasAlpha
					? c.a >= 128
					: Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 40;
				if (!content) continue;
				if (x < minX) minX = x;
				if (x > maxX) maxX = x;
				if (y < minY) minY = y;
				if (y > maxY) maxY = y;
			}

			return maxX < 0 ? new RectInt(0, 0, w, h) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
		}
	}
}
