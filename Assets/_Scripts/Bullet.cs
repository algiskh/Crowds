using UnityEngine;

/// <summary>
/// Projectile view (pooled per prefab by BulletSystem). Visual convention (Tools ▸ Crowds ▸ Build Projectile Models):
/// the root holds Bullet + TrailRenderer (+ collider), the mesh is in children, the nose points along +Z —
/// BulletSystem turns the root along the flight direction.
/// </summary>
public class Bullet : MonoBehaviour
{
	private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

	[SerializeField] private int _maxPierceCount = 1;
	public int MaxPierceCount => _maxPierceCount;

	[Tooltip("Tracer glow: the GunConfig.BulletColor tint of the head is multiplied by this (HDR > 1 → bloom). " +
	         "The trail glow comes from its additive material's own color.")]
	[SerializeField, Min(0f)] private float _tintIntensity = 1f;

	private TrailRenderer _trail;
	private MeshRenderer[] _renderers;
	private Gradient _defaultTrailGradient;
	private MaterialPropertyBlock _mpb;
	private bool _isTinted;

	/// <summary>Prefab this instance was created from (runtime only): BulletSystem keeps one pool per prefab.</summary>
	public Bullet SourcePrefab { get; set; }

	private void Awake()
	{
		_trail = GetComponent<TrailRenderer>();
		// Root mesh (legacy ball prefabs) or child parts (body + nose).
		_renderers = GetComponentsInChildren<MeshRenderer>(true);
		if (_trail != null)
			_defaultTrailGradient = _trail.colorGradient;
	}

	private void OnDisable()
	{
		if (_trail != null)
		{
			_trail.Clear();
		}
	}

	/// <summary>
	/// Tints the projectile (mesh via MaterialPropertyBlock, trail via its gradient).
	/// <paramref name="color"/> with alpha 0 restores the prefab's own look. Bullets are pooled and shared
	/// between guns/teams, so this is called on every spawn.
	/// </summary>
	public void SetColor(Color color)
	{
		bool tint = color.a > 0f;
		if (!tint && !_isTinted)
			return;
		_isTinted = tint;

		foreach (var meshRenderer in _renderers)
		{
			if (tint)
			{
				_mpb ??= new MaterialPropertyBlock();
				meshRenderer.GetPropertyBlock(_mpb);
				var hdr = color * _tintIntensity;
				hdr.a = 1f;
				_mpb.SetColor(BaseColorId, hdr);
				meshRenderer.SetPropertyBlock(_mpb);
			}
			else
			{
				meshRenderer.SetPropertyBlock(null);
			}
		}

		if (_trail != null && _defaultTrailGradient != null)
		{
			if (tint)
			{
				// Keep the prefab's alpha fade, swap the hue.
				var gradient = new Gradient();
				gradient.SetKeys(
					new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
					_defaultTrailGradient.alphaKeys);
				_trail.colorGradient = gradient;
			}
			else
			{
				_trail.colorGradient = _defaultTrailGradient;
			}
		}
	}
}
