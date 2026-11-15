using UnityEngine;
using UnityEngine.UI;

public class Loot : MonoBehaviour
{
	[SerializeField] private Canvas _spriteLooker;
	[SerializeField] private Image _image;

	private Color _defaultColor = Color.white;
	private bool _defaultColorCached;
	private LootAura _aura;

	public Canvas SpriteLooker => _spriteLooker;

	private void Awake()
	{
		CacheDefaultColor();
		// Ground ring under the item: readable on dark and light floors alike (see LootAura).
		_aura = LootAura.Create(transform);
	}

	/// <summary>Tints the ground aura (LootAura.ColorFor(type)); called by LootSystem on every spawn.</summary>
	public void SetAuraColor(Color color)
	{
		if (_aura == null)
			_aura = LootAura.Create(transform);
		_aura.SetColor(color);
	}

	public void SetSprite(Sprite sprite)
	{
		_image.sprite = sprite;
	}

	/// <summary>
	/// Tint the icon toward the warning color. <paramref name="t"/> is 0 (default color)..1 (full warning).
	/// Used by the despawn pulse on mob-dropped loot.
	/// </summary>
	public void SetWarningTint(Color warningColor, float t)
	{
		if (_image == null) return;
		CacheDefaultColor();
		_image.color = Color.Lerp(_defaultColor, warningColor, Mathf.Clamp01(t));
	}

	/// <summary>Restore the icon to its default color. Call when (re)spawning a pooled loot.</summary>
	public void ResetColor()
	{
		if (_image == null) return;
		CacheDefaultColor();
		_image.color = _defaultColor;
	}

	private void CacheDefaultColor()
	{
		if (_defaultColorCached) return;
		if (_image != null) _defaultColor = _image.color;
		_defaultColorCached = true;
	}
}
