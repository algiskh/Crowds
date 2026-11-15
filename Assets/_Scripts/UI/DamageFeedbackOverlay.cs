using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime overlay for the player's injury feedback (driven by ECS.DamageFeedbackSystem):
///  • a full-screen red edge vignette (procedural radial texture, clear centre) whose alpha the system sets;
///  • a few pooled "hit direction" arcs placed on a ring around the screen centre, rotated toward the hit source.
/// Own Canvas below the crosshair (500), pause menu (900) and fail veil (1000); never blocks raycasts.
/// Lives in the active scene, so it's destroyed on scene reload (restart). Textures are generated once per instance.
/// </summary>
public sealed class DamageFeedbackOverlay
{
	private const int SortingOrder = 400;
	private const int MarkerCount = 4;
	// Arc ring radius as a share of the smaller screen side, and the arc size in reference pixels (1080p).
	private const float MarkerRingShare = 0.36f;
	private static readonly Vector2 MarkerSize = new(300f, 90f);

	private GameObject _root;
	private Image _vignette;
	private readonly Image[] _markers = new Image[MarkerCount];
	private readonly float[] _markerTimers = new float[MarkerCount];
	private readonly float[] _markerAngles = new float[MarkerCount];
	private float _markerLifetime = 0.8f;
	private Color _color = Color.red;
	private int _nextMarker;

	public void EnsureCreated(Color color)
	{
		if (_root != null)
			return;
		_color = color;

		_root = new GameObject("DamageFeedbackOverlay");
		var canvas = _root.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = SortingOrder;
		var scaler = _root.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		scaler.matchWidthOrHeight = 0.5f;

		var vignetteSprite = CreateVignetteSprite(256);
		_vignette = CreateImage("Vignette", _root.transform, vignetteSprite);
		var rt = _vignette.rectTransform;
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
		SetAlpha(_vignette, 0f);

		var arcSprite = CreateArcSprite(256, 80);
		for (int i = 0; i < MarkerCount; i++)
		{
			var marker = CreateImage("HitMarker" + i, _root.transform, arcSprite);
			var mrt = marker.rectTransform;
			mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0.5f);
			mrt.pivot = new Vector2(0.5f, 0.5f);
			mrt.sizeDelta = MarkerSize;
			SetAlpha(marker, 0f);
			marker.enabled = false;
			_markers[i] = marker;
		}
	}

	public void SetVignetteAlpha(float alpha)
	{
		if (_vignette == null)
			return;
		alpha = Mathf.Clamp01(alpha);
		_vignette.enabled = alpha > 0.001f;
		SetAlpha(_vignette, alpha);
	}

	/// <summary>
	/// Shows an arc toward <paramref name="screenAngleDeg"/> (0 = right, 90 = up, screen space).
	/// Reuses the oldest marker when all are busy.
	/// </summary>
	public void ShowHitDirection(float screenAngleDeg, float lifetime, float strength)
	{
		if (_root == null)
			return;
		_markerLifetime = Mathf.Max(0.05f, lifetime);
		int i = _nextMarker;
		_nextMarker = (_nextMarker + 1) % MarkerCount;
		_markerAngles[i] = screenAngleDeg;
		// Stronger hits start brighter (and so stay visible longer while fading).
		_markerTimers[i] = _markerLifetime * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(strength));
		_markers[i].enabled = true;
		PlaceMarker(i);
	}

	/// <summary>Fades the arcs. Call every unpaused frame.</summary>
	public void Tick(float dt)
	{
		if (_root == null)
			return;
		for (int i = 0; i < MarkerCount; i++)
		{
			if (_markerTimers[i] <= 0f)
				continue;
			_markerTimers[i] -= dt;
			if (_markerTimers[i] <= 0f)
			{
				_markers[i].enabled = false;
				continue;
			}
			PlaceMarker(i);
		}
	}

	public void HideAll()
	{
		SetVignetteAlpha(0f);
		for (int i = 0; i < MarkerCount; i++)
		{
			_markerTimers[i] = 0f;
			if (_markers[i] != null)
				_markers[i].enabled = false;
		}
	}

	private void PlaceMarker(int i)
	{
		var marker = _markers[i];
		var rt = marker.rectTransform;
		// Ring radius in canvas units (the canvas is scaled to 1920x1080 reference, match 0.5).
		var canvasRect = ((RectTransform)_root.transform).rect;
		float radius = Mathf.Min(canvasRect.width, canvasRect.height) * MarkerRingShare;
		float rad = _markerAngles[i] * Mathf.Deg2Rad;
		rt.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
		// The arc texture bulges toward +Y; rotate so it bulges outward along the angle.
		rt.localRotation = Quaternion.Euler(0f, 0f, _markerAngles[i] - 90f);

		float t = Mathf.Clamp01(_markerTimers[i] / _markerLifetime);
		SetAlpha(marker, t * t * 0.9f);
	}

	private Image CreateImage(string name, Transform parent, Sprite sprite)
	{
		var go = new GameObject(name);
		go.transform.SetParent(parent, false);
		var image = go.AddComponent<Image>();
		image.sprite = sprite;
		image.color = _color;
		image.raycastTarget = false;
		return image;
	}

	private void SetAlpha(Image image, float alpha)
	{
		var c = _color;
		c.a = alpha;
		image.color = c;
	}

	// White texture, alpha 0 in the centre rising to 1 at the edges/corners (stretched to the screen).
	private static Sprite CreateVignetteSprite(int size)
	{
		var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
		{
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear,
			name = "DamageVignette"
		};
		var pixels = new Color32[size * size];
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float u = (x + 0.5f) / size * 2f - 1f;
				float v = (y + 0.5f) / size * 2f - 1f;
				// Rounded-rectangle distance: hugs the edges more than a circle, leaves the centre clear.
				float d = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 4f) + Mathf.Pow(Mathf.Abs(v), 4f), 0.25f);
				float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.05f, d));
				pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
			}
		}
		tex.SetPixels32(pixels);
		tex.Apply(false, true);
		return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
	}

	// White crescent bulging toward +Y, soft edges, fading toward its tips.
	private static Sprite CreateArcSprite(int width, int height)
	{
		var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
		{
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear,
			name = "DamageHitArc"
		};
		var pixels = new Color32[width * height];
		// Circle centred far below the texture: its top band forms a gentle arc.
		float cx = width * 0.5f;
		float cy = -height * 2.2f;
		float outer = height * 3.1f;
		float thickness = height * 0.45f;
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				float dx = x + 0.5f - cx;
				float dy = y + 0.5f - cy;
				float r = Mathf.Sqrt(dx * dx + dy * dy);
				float band = 1f - Mathf.Abs(r - (outer - thickness * 0.5f)) / (thickness * 0.5f);
				float along = 1f - Mathf.Abs(dx) / (width * 0.5f); // 1 at the middle, 0 at the tips
				float a = Mathf.Clamp01(band) * Mathf.SmoothStep(0f, 1f, along);
				pixels[y * width + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * 1.6f) * 255f));
			}
		}
		tex.SetPixels32(pixels);
		tex.Apply(false, true);
		return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
	}
}
