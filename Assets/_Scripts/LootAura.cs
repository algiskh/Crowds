using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ground marker under a loot item that reads on any floor: a flat ring lying on the ground with
///  • a bright ring + soft inner glow tinted by the loot type → stands out on dark floors;
///  • a dark outer halo and a thin dark inner edge (black is not affected by the tint) → stands out on light floors
///    (white/blue ice, concrete).
/// It pulses slowly (scale + alpha, per-instance phase). Built at runtime on a tiny world-space Canvas with the UI
/// shader (always in the build, unlit, transparent), the ring texture is generated once and shared.
/// Created by Loot.Awake, colour set by LootSystem on spawn (<see cref="ColorFor"/>). See Docs/LootFeature.md.
/// </summary>
public sealed class LootAura : MonoBehaviour
{
	private const int TextureSize = 128;
	private const float Diameter = 1.7f;
	private const float GroundOffset = 0.15f; // above every floor dressing layer (suburb ground tops out at ~0.09 m) + navmesh height error
	private const float PulseSpeed = 2.6f;

	private static Sprite _sprite;

	private Image _image;
	private RectTransform _rect;
	private Color _color = Color.white;
	private float _phase;

	public static LootAura Create(Transform loot)
	{
		var go = new GameObject("Aura", typeof(RectTransform));
		go.layer = loot.gameObject.layer;
		go.transform.SetParent(loot, false);
		var aura = go.AddComponent<LootAura>();
		aura.Build();
		return aura;
	}

	/// <summary>Ring colour per loot type (the dark parts stay dark).</summary>
	public static Color ColorFor(LootType type) => type switch
	{
		LootType.Ammo => new Color(1f, 0.82f, 0.22f),
		LootType.Health => new Color(0.35f, 1f, 0.45f),
		LootType.Weapon => new Color(1f, 0.52f, 0.15f),
		LootType.Grenade => new Color(1f, 0.32f, 0.25f),
		LootType.Bonus => new Color(0.35f, 0.85f, 1f),
		_ => Color.white,
	};

	public void SetColor(Color color)
	{
		_color = color;
		if (_image != null)
			_image.color = color;
	}

	private void Build()
	{
		// Flat on the ground: the canvas plane (XY) rotated onto XZ, facing up.
		transform.localPosition = new Vector3(0f, GroundOffset, 0f);
		transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		var canvas = gameObject.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.WorldSpace;
		_rect = (RectTransform)transform;
		_rect.sizeDelta = Vector2.one;
		_rect.localScale = Vector3.one * Diameter;

		var imageGo = new GameObject("Ring", typeof(RectTransform));
		imageGo.layer = gameObject.layer;
		imageGo.transform.SetParent(transform, false);
		var imageRect = (RectTransform)imageGo.transform;
		imageRect.anchorMin = Vector2.zero;
		imageRect.anchorMax = Vector2.one;
		imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;
		_image = imageGo.AddComponent<Image>();
		_image.sprite = GetSprite();
		_image.raycastTarget = false;
		_image.color = _color;
		_phase = Random.value * Mathf.PI * 2f;
	}

	private void OnEnable() => _phase = Random.value * Mathf.PI * 2f;

	private void Update()
	{
		if (_image == null)
			return;
		float s = Mathf.Sin(Time.time * PulseSpeed + _phase) * 0.5f + 0.5f; // 0..1
		_rect.localScale = Vector3.one * (Diameter * (0.94f + 0.08f * s));
		var c = _color;
		c.a = 0.8f + 0.2f * s;
		_image.color = c;
	}

	// Radial profile (r = 0 centre … 1 edge): glow → dark inner edge → bright ring → dark halo fading out.
	private static Sprite GetSprite()
	{
		if (_sprite != null)
			return _sprite;
		var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
		{
			name = "LootAuraRing",
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear,
			hideFlags = HideFlags.DontSave,
		};
		var pixels = new Color32[TextureSize * TextureSize];
		float half = TextureSize * 0.5f;
		for (int y = 0; y < TextureSize; y++)
		{
			for (int x = 0; x < TextureSize; x++)
			{
				float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
				float r = Mathf.Sqrt(dx * dx + dy * dy);
				pixels[y * TextureSize + x] = Profile(r);
			}
		}
		texture.SetPixels32(pixels);
		texture.Apply(false, true);
		_sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f), TextureSize);
		_sprite.name = "LootAuraRing";
		_sprite.hideFlags = HideFlags.DontSave;
		return _sprite;
	}

	private static Color32 Profile(float r)
	{
		const float ringIn = 0.60f, ringOut = 0.74f;
		if (r >= 1f)
			return new Color32(0, 0, 0, 0);
		if (r >= ringOut)
		{
			// Dark halo outside the ring, fading to nothing at the edge.
			float t = Mathf.InverseLerp(ringOut, 1f, r);
			return Dark(0.6f * (1f - t * t));
		}
		if (r >= ringIn)
		{
			// Bright ring (white → tinted by Image.color), soft edges.
			float centre = (ringIn + ringOut) * 0.5f, halfWidth = (ringOut - ringIn) * 0.5f;
			float edge = 1f - Mathf.Clamp01((Mathf.Abs(r - centre) - halfWidth * 0.55f) / (halfWidth * 0.45f));
			// Blend into the dark border on both sides so the ring keeps a dark outline.
			return Color32.Lerp(Dark(0.6f), Bright(1f), edge);
		}
		if (r >= ringIn - 0.05f)
			return Dark(0.45f); // thin dark inner edge
		// Soft inner glow, a bit stronger toward the ring.
		return Bright(Mathf.Lerp(0.12f, 0.3f, r / ringIn));
	}

	private static Color32 Dark(float alpha) => new(0, 0, 0, (byte)(Mathf.Clamp01(alpha) * 255f));
	private static Color32 Bright(float alpha) => new(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
}
