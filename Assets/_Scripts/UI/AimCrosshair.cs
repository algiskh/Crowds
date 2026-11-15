using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space aim crosshair (cross inside a ring) + a small cursor dot. Created at runtime: its own
/// overlay Canvas with two Images whose sprites are generated procedurally (white, tinted via Image.color).
/// The crosshair sits on the distance-clamped aim point, the dot on the real cursor (it replaces the
/// hidden system cursor, so the mouse is never "lost").
/// Never intercepts clicks (raycastTarget off). Lives in the active scene — destroyed on reload.
/// </summary>
public sealed class AimCrosshair
{
	// Above the gameplay HUD, below menus/fail overlay (FailScreenOverlay uses 1000).
	private const int SortingOrder = 500;
	private const int TextureSize = 128;

	private GameObject _root;
	private RectTransform _rect;
	private Image _image;
	private RectTransform _dotRect;
	private Image _dot;

	public void EnsureCreated(Color color, float sizePixels, float ringThickness, float crossThickness, float crossGap)
	{
		if (_root != null)
			return;

		_root = new GameObject("AimCrosshair");
		var canvas = _root.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = SortingOrder;

		var imageGo = new GameObject("Crosshair");
		imageGo.transform.SetParent(_root.transform, false);
		_image = imageGo.AddComponent<Image>();
		_image.sprite = CreateSprite(ringThickness, crossThickness, crossGap);
		_image.color = color;
		_image.raycastTarget = false;

		_rect = _image.rectTransform;
		_rect.anchorMin = Vector2.zero;
		_rect.anchorMax = Vector2.zero;
		_rect.pivot = new Vector2(0.5f, 0.5f);
		_rect.sizeDelta = new Vector2(sizePixels, sizePixels);
	}

	/// <summary>Adds the cursor dot (a filled circle). Call after <see cref="EnsureCreated"/>.</summary>
	public void EnsureCursorDot(Color color, float sizePixels)
	{
		if (_root == null || _dot != null)
			return;

		var dotGo = new GameObject("CursorDot");
		dotGo.transform.SetParent(_root.transform, false);
		_dot = dotGo.AddComponent<Image>();
		_dot.sprite = CreateDotSprite();
		_dot.color = color;
		_dot.raycastTarget = false;

		_dotRect = _dot.rectTransform;
		_dotRect.anchorMin = Vector2.zero;
		_dotRect.anchorMax = Vector2.zero;
		_dotRect.pivot = new Vector2(0.5f, 0.5f);
		_dotRect.sizeDelta = new Vector2(sizePixels, sizePixels);
	}

	/// <summary>Places the cursor dot at the given screen point (pixels).</summary>
	public void SetCursorPosition(Vector2 screenPosition)
	{
		if (_dotRect == null)
			return;

		if (!_dot.enabled)
			_dot.enabled = true;
		_dotRect.position = new Vector3(screenPosition.x, screenPosition.y, 0f);
	}

	/// <summary>Places the crosshair center at the given screen point (pixels).</summary>
	public void SetScreenPosition(Vector2 screenPosition)
	{
		if (_rect == null)
			return;

		SetVisible(true);
		// For a ScreenSpaceOverlay canvas, world position == screen pixels.
		_rect.position = new Vector3(screenPosition.x, screenPosition.y, 0f);
	}

	/// <summary>Crosshair only (the dot is driven by <see cref="SetCursorPosition"/>).</summary>
	public void SetVisible(bool visible)
	{
		if (_image != null && _image.enabled != visible)
			_image.enabled = visible;
	}

	/// <summary>Crosshair and cursor dot together (pause/menus).</summary>
	public void SetAllVisible(bool visible)
	{
		SetVisible(visible);
		if (_dot != null && _dot.enabled != visible)
			_dot.enabled = visible;
	}

	/// <summary>
	/// Builds a white antialiased sprite: a ring on the edge + a cross with a gap in the center.
	/// Thickness/gap are fractions of the texture size (0..1).
	/// </summary>
	private static Sprite CreateSprite(float ringThickness, float crossThickness, float crossGap)
	{
		var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
		{
			name = "AimCrosshairTex",
			filterMode = FilterMode.Bilinear,
			wrapMode = TextureWrapMode.Clamp
		};

		float half = TextureSize * 0.5f;
		float ringHalf = Mathf.Max(0.5f, ringThickness * TextureSize * 0.5f);
		float ringRadius = half - ringHalf - 1f;
		float crossHalf = Mathf.Max(0.5f, crossThickness * TextureSize * 0.5f);
		float gap = crossGap * TextureSize * 0.5f;
		float crossLength = ringRadius - ringHalf; // lines stop at the inner edge of the ring

		var pixels = new Color32[TextureSize * TextureSize];
		for (int y = 0; y < TextureSize; y++)
		{
			for (int x = 0; x < TextureSize; x++)
			{
				float px = x + 0.5f - half;
				float py = y + 0.5f - half;

				// Ring: distance to the circle line.
				float r = Mathf.Sqrt(px * px + py * py);
				float ringA = Coverage(Mathf.Abs(r - ringRadius) - ringHalf);

				// Cross: horizontal + vertical bars, cut out in the gap and beyond the ring.
				float ax = Mathf.Abs(px);
				float ay = Mathf.Abs(py);
				float hBar = Mathf.Min(Coverage(ay - crossHalf), Coverage(gap - ax), Coverage(ax - crossLength));
				float vBar = Mathf.Min(Coverage(ax - crossHalf), Coverage(gap - ay), Coverage(ay - crossLength));

				float a = Mathf.Max(ringA, Mathf.Max(hBar, vBar));
				pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
			}
		}

		tex.SetPixels32(pixels);
		tex.Apply(false, true);

		return Sprite.Create(tex, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f), 100f);
	}

	/// <summary>White antialiased filled circle.</summary>
	private static Sprite CreateDotSprite()
	{
		const int size = 32;
		var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
		{
			name = "CursorDotTex",
			filterMode = FilterMode.Bilinear,
			wrapMode = TextureWrapMode.Clamp
		};

		float half = size * 0.5f;
		float radius = half - 1f;
		var pixels = new Color32[size * size];
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float px = x + 0.5f - half;
				float py = y + 0.5f - half;
				float a = Coverage(Mathf.Sqrt(px * px + py * py) - radius);
				pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
			}
		}

		tex.SetPixels32(pixels);
		tex.Apply(false, true);
		return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
	}

	/// <summary>Signed distance (negative = inside) → alpha with a 1px soft edge.</summary>
	private static float Coverage(float signedDistance) => Mathf.Clamp01(0.5f - signedDistance);
}
