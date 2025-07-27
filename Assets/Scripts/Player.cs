using UnityEngine;

public class Player : MonoBehaviour
{
	[SerializeField] private SimpleAnimator _animator;
	[SerializeField] private Weapon _weapon;
	[SerializeField] private AudioSource _audioSource;
	private Vector2 _muzzleOriginal;
	public SimpleAnimator Animator => _animator;
	public Weapon Weapon => _weapon;

}
