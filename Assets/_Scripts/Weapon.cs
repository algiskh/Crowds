using UnityEngine;

public class Weapon : MonoBehaviour
{
	[SerializeField] private Transform _muzzle;
	[SerializeField] private AudioSource _audioSource;
	public AudioSource AudioSource => _audioSource;
	public Transform Muzzle => _muzzle;
}
