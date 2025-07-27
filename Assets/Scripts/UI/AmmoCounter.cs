using UnityEngine;
using UnityEngine.UI;

public class AmmoCounter : MonoBehaviour
{
	[SerializeField] private Text _text;

	public void SetAmmo(int ammo)
	{
		if (_text != null)
		{
			_text.text = $"x{ammo}";
		}
	}
}
