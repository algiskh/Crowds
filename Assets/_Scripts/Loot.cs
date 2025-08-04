using UnityEngine;
using UnityEngine.UI;

public class Loot : MonoBehaviour
{
	[SerializeField] private Canvas _spriteLooker;
	[SerializeField] private Image _image;

	public Canvas SpriteLooker => _spriteLooker;

	public void SetSprite(Sprite sprite)
	{
		_image.sprite = sprite;
	}
}
