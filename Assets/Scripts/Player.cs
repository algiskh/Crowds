using UnityEngine;

public class Player : MonoBehaviour
{
	[SerializeField] private SimpleAnimator _animator;
	[SerializeField] private Weapon _weapon;

	public SimpleAnimator Animator => _animator;
	public Weapon Weapon => _weapon;

}
