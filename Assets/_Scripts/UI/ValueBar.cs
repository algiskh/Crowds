using LightSide;
using UnityEngine;
using UnityEngine.UI;

namespace Scene.UI
{
	public interface IValueBar
	{
		Transform Transform { get; }
		IValueBar ApplyValue(float value);
		IValueBar SetMaxValue(float value);
		IValueBar SetVisible(bool visible);
		IValueBar SetText(string text);
	}

	public class ValueBar : MonoBehaviour, IValueBar
	{
		[SerializeField] private Image _bar;
		[Tooltip("Опционально: подпись на баре (например, оставшиеся секунды бонуса).")]
		[SerializeField] private UniText _valueText;
		[SerializeField]
		private Gradient _gradient = new Gradient
		{
			colorKeys = new[]
				{
					new GradientColorKey(Color.red, 0f),
					new GradientColorKey(Color.yellow, 0.5f),
					new GradientColorKey(Color.green, 1f)
				}
		};
		private float _max;

		public Transform Transform => transform;

		public IValueBar ApplyValue(float value)
		{
			float fill = value / _max;
			_bar.fillAmount = fill;

			_bar.color = _gradient.Evaluate(fill);

			gameObject.SetActive(fill > 0f);
			return this;
		}

		public IValueBar SetMaxValue(float value)
		{
			_max = value;
			return this;
		}

		public IValueBar SetVisible(bool visible)
		{
			gameObject.SetActive(visible);
			return this;
		}

		/// <summary>Подпись на баре. Пустая строка прячет текст. No-op, если поле не назначено.</summary>
		public IValueBar SetText(string text)
		{
			if (_valueText == null)
				return this;

			bool show = !string.IsNullOrEmpty(text);
			if (_valueText.gameObject.activeSelf != show)
				_valueText.gameObject.SetActive(show);
			if (show)
				_valueText.Text = text;
			return this;
		}
	}
}