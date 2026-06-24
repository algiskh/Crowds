using UnityEngine;

namespace Scene.UI
{
	/// <summary>
	/// Полоска здоровья без Canvas: один квад-MeshRenderer, управляемый общим
	/// материалом + MaterialPropertyBlock. Нет перестроений Canvas на каждое
	/// изменение HP (как у UGUI <see cref="ValueBar"/>), и при включённом на
	/// материале GPU Instancing все бары мобов сходятся в инстансированные
	/// draw call'ы. По инстансу меняются только _Fill и _FillColor.
	///
	/// Реализует тот же <see cref="IValueBar"/>, что и экранный ValueBar, поэтому
	/// для DamageSystem/MobSpawnSystem замена прозрачна. Билборд бара по-прежнему
	/// делает LookAtCameraSystem через <see cref="Transform"/>.
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

		[Tooltip("Прятать бар при полном здоровье — меньше прозрачных квадов в кадре. " +
			"Бар появляется после первого урона.")]
		[SerializeField] private bool _hideWhenFull = true;

		[Tooltip("Здоровье, при котором ширина бара равна базовой (scale.x из префаба).")]
		[SerializeField] private float _referenceHealth = 100f;

		[Tooltip("Насколько здоровье влияет на ширину бара. 1 = линейно (150 HP → ×1.5), " +
			"0 = ширина постоянна, 0.5 = половина прироста (150 HP → ×1.25). " +
			"Сильным мобам ширину обычно гасят (<1).")]
		[SerializeField, Range(0f, 1f)] private float _widthGrowth = 0.4f;

		[Tooltip("Жёсткие границы итогового множителя ширины, чтобы очень сильные/слабые " +
			"мобы не получали огромных/исчезающих баров.")]
		[SerializeField] private float _minWidthFactor = 0.75f;
		[SerializeField] private float _maxWidthFactor = 2f;

		// Один квад на все бары — общая память, не плодим Mesh per-mob.
		private static Mesh _sharedQuad;

		private MaterialPropertyBlock _mpb;
		private float _max = 1f;
		private float _fill = 1f;
		private bool _visibleRequested = true;

		// Авторская ширина бара (scale из префаба) — точка отсчёта для масштабирования
		// по здоровью. Снимается один раз; пул переиспользует мобов и зовёт SetMaxValue
		// повторно, поэтому считаем от исходного scale, а не от уже растянутого.
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

			// Ширина бара растёт с максимальным здоровьем, но прирост гасится
			// коэффициентом _widthGrowth и зажимается в [min,max], иначе сильные
			// мобы получают слишком длинные бары.
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

		// Мобам подпись не нужна; метод есть только ради общего интерфейса.
		public IValueBar SetText(string text) => this;

		/// <summary>
		/// Единая точка решения «показывать/скрывать» и записи свойств. Видимость
		/// = запрошена И есть что показывать (fill>0) И не прячем полный бар.
		/// Скрытие — через renderer.enabled (GameObject остаётся активным, чтобы
		/// билборд продолжал работать; стоимость отключённого рендера нулевая).
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

		// Единичный квад в плоскости XY, центр в нуле, заполнение растёт вдоль uv.x.
		// Размер/пропорции бара задаются localScale трансформа в префабе.
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
