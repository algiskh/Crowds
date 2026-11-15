using LightSide;
using UnityEngine;

/// <summary>
/// UI view of the player's grenade counter.
/// The whole widget is shown when there's at least one grenade;
/// the numeric counter ("x3") is shown only when there's more than one grenade.
/// </summary>
public class GrenadeCounter : MonoBehaviour
{
	[SerializeField] private GameObject _root;     // the whole widget (icon + counter)
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
