using System;
using LightSide;
using UnityEngine;
using UnityEngine.UI;

// Кнопка выбора одного уровня в главном меню. Заполняется из LevelDefinition,
// по клику пробрасывает выбранный уровень в колбэк меню.
public class LevelSelectButton : MonoBehaviour
{
	[SerializeField] private Button _button;
	[SerializeField] private Image _icon;
	[SerializeField] private UniText _label;

	private LevelDefinition _level;
	private Action<LevelDefinition> _onClick;

	public void Setup(LevelDefinition level, Action<LevelDefinition> onClick)
	{
		_level = level;
		_onClick = onClick;

		if (_label != null)
			_label.Text = level.DisplayName;

		if (_icon != null)
		{
			_icon.sprite = level.Icon;
			_icon.enabled = level.Icon != null;
		}

		_button.onClick.RemoveListener(HandleClick);
		_button.onClick.AddListener(HandleClick);
	}

	private void HandleClick() => _onClick?.Invoke(_level);
}
