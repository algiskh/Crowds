using UnityEngine;

public class Player : MonoBehaviour
{
	[SerializeField] private SimpleAnimator _animator;
	[SerializeField] private Weapon _weapon;
	private int _entity;
	public SimpleAnimator Animator => _animator;
	public Weapon Weapon => _weapon;
	public int Entity => _entity;

	public void Initialize(int entity)
	{
		_entity = entity;
	}

}
