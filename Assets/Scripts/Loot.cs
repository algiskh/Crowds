using UnityEngine;

public class Loot : MonoBehaviour
{
	[SerializeField] private SpriteRenderer _renderer;

	public void SetSprite(Sprite sprite)
	{
		_renderer.sprite = sprite;
	}
}
