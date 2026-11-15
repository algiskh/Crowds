using UnityEngine;

namespace Scene.UI
{
	/// <summary>
	/// Health bar without a Canvas: a single quad MeshRenderer driven by a shared
	/// material + MaterialPropertyBlock. No Canvas rebuilds on every HP change
	/// (like the UGUI <see cref="ValueBar"/>), and with GPU Instancing enabled on the
	/// material all mob bars merge into instanced draw calls. Only _Fill and _FillColor
	/// vary per instance.
	///
	/// Implements the same <see cref="IValueBar"/> as the on-screen ValueBar, so the swap
	/// is transparent for DamageSystem/MobSpawnSystem. The bar billboard is still done by
	/// LookAtCameraSystem via <see cref="Transform"/>.
	/// </summary>
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public class MeshHealthBar : MonoBehaviour, IValueBar
	{
		private static readonly int FillId = Shader.PropertyToID("_Fill");
		private static readonly int FillColorId = Shader.PropertyToID("_FillColor");

		[SerializeField] private MeshRenderer _renderer;
		[SerializeField] private MeshFilter _filter;
		[SerializeField]
		private Gradient _gradient = new Gradient
		{
			colorKeys = new[]
			{
				new GradientColorKey(Color.red, 0f),
				new GradientColorKey(Color.yellow, 0.5f),
				new GradientColorKey(Color.green, 1f)
			},
			alphaKeys = new[]
			{
				new GradientAlphaKey(1f, 0f),
				new GradientAlphaKey(1f, 1f)
			}
		};

		[Tooltip("Hide the bar at full health — fewer transparent quads in the frame. " +
			"The bar appears after the first damage.")]
		[SerializeField] private bool _hideWhenFull = true;

		[Tooltip("Health at which the bar width equals the base width (scale.x from the prefab).")]
		[SerializeField] private float _referenceHealth = 100f;

		[Tooltip("How much health affects the bar width. 1 = linear (150 HP → ×1.5), " +
			"0 = constant width, 0.5 = half the growth (150 HP → ×1.25). " +
			"Strong mobs usually get their width damped (<1).")]
		[SerializeField, Range(0f, 1f)] private float _widthGrowth = 0.4f;

		[Tooltip("Hard limits of the final width multiplier, so very strong/weak " +
			"mobs don't get huge/vanishing bars.")]
		[SerializeField] private float _minWidthFactor = 0.75f;
		[SerializeField] private float _maxWidthFactor = 2f;

		// One quad for all bars — shared memory, no per-mob Mesh allocation.
		private static Mesh _sharedQuad;

		private MaterialPropertyBlock _mpb;
		private float _max = 1f;
		private float _fill = 1f;
		private bool _visibleRequested = true;

		// The bar's authored width (scale from the prefab) — the reference point for scaling
		// by health. Captured once; the pool reuses mobs and calls SetMaxValue again,
		// so we compute from the original scale, not the already stretched one.
		private Vector3 _baseScale;
		private bool _baseScaleCaptured;

		public Transform Transform => transform;

		private void Awake()
		{
			if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
			if (_filter == null) _filter = GetComponent<MeshFilter>();
			if (_filter.sharedMesh == null) _filter.sharedMesh = GetSharedQuad();
			EnsureBaseScale();
			_mpb = new MaterialPropertyBlock();
			Refresh();
		}

		public IValueBar SetMaxValue(float value)
		{
			_max = value <= 0f ? 1f : value;

			// The bar width grows with max health, but the growth is damped
			// by the _widthGrowth coefficient and clamped to [min,max], otherwise strong
			// mobs get overly long bars.
			EnsureBaseScale();
			float ratio = _referenceHealth > 0f ? _max / _referenceHealth : 1f;
			float widthFactor = 1f + (ratio - 1f) * _widthGrowth;
			widthFactor = Mathf.Clamp(widthFactor, _minWidthFactor, _maxWidthFactor);
			var scale = transform.localScale;
			scale.x = _baseScale.x * widthFactor;
			transform.localScale = scale;
			return this;
		}

		private void EnsureBaseScale()
		{
			if (_baseScaleCaptured) return;
			_baseScale = transform.localScale;
			_baseScaleCaptured = true;
		}

		public IValueBar ApplyValue(float value)
		{
			_fill = Mathf.Clamp01(value / _max);
			Refresh();
			return this;
		}

		public IValueBar SetVisible(bool visible)
		{
			_visibleRequested = visible;
			Refresh();
			return this;
		}

		// Mobs don't need a label; the method exists only for the common interface.
		public IValueBar SetText(string text) => this;

		/// <summary>
		/// Single decision point for "show/hide" and writing properties. Visibility
		/// = requested AND has something to show (fill>0) AND not hiding the full bar.
		/// Hiding — via renderer.enabled (the GameObject stays active so the
		/// billboard keeps working; a disabled renderer costs nothing).
		/// </summary>
		private void Refresh()
		{
			if (_renderer == null) return;

			bool visible = _visibleRequested
				&& _fill > 0f
				&& !(_hideWhenFull && _fill >= 1f);

			if (_renderer.enabled != visible)
				_renderer.enabled = visible;
			if (!visible) return;

			_mpb ??= new MaterialPropertyBlock();
			_renderer.GetPropertyBlock(_mpb);
			_mpb.SetFloat(FillId, _fill);
			_mpb.SetColor(FillColorId, _gradient.Evaluate(_fill));
			_renderer.SetPropertyBlock(_mpb);
		}

		// Unit quad in the XY plane, center at zero, fill grows along uv.x.
		// The bar's size/proportions are set by the transform's localScale in the prefab.
		private static Mesh GetSharedQuad()
		{
			if (_sharedQuad != null) return _sharedQuad;

			var mesh = new Mesh { name = "HealthBarQuad" };
			mesh.vertices = new[]
			{
				new Vector3(-0.5f, -0.5f, 0f),
				new Vector3( 0.5f, -0.5f, 0f),
				new Vector3(-0.5f,  0.5f, 0f),
				new Vector3( 0.5f,  0.5f, 0f),
			};
			mesh.uv = new[]
			{
				new Vector2(0f, 0f),
				new Vector2(1f, 0f),
				new Vector2(0f, 1f),
				new Vector2(1f, 1f),
			};
			mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
			mesh.RecalculateBounds();
			_sharedQuad = mesh;
			return mesh;
		}
	}
}
