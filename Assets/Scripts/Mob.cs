using Scene.UI;
using UnityEngine;

public class Mob: MonoBehaviour
{
	[SerializeField] private SimpleAnimator _simpleAnimator;
	[SerializeField] private ValueBar _valueBar;
	public SimpleAnimator SimpleAnimator => _simpleAnimator;
	public Vector2 Position => transform.position;
	public IValueBar ValueBar => _valueBar;

	public string Id { get; private set; }
	public void SetId(string id)
	{
		Id = id;
	}
}