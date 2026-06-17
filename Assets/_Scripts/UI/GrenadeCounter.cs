using LightSide;
using UnityEngine;

/// <summary>
/// UI-вью счётчика гранат у игрока.
/// Виджет целиком показывается, когда есть хотя бы одна граната;
/// числовой счётчик ("x3") показывается только когда гранат больше одной.
/// </summary>
public class GrenadeCounter : MonoBehaviour
{
	[SerializeField] private GameObject _root;     // весь виджет (иконка + счётчик)
	[SerializeField] private UniText _countText;   // "x{count}"

	public void SetCount(int count)
	{
		if (_root != null)
			_root.SetActive(count > 0);

		if (_countText != null)
		{
			_countText.gameObject.SetActive(count > 1);
			if (count > 1)
				_countText.Text = $"x{count}";
		}
	}
}
