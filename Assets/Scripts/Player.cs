using UnityEngine;
using UnityEngine.AI;

public class Player : MonoBehaviour
{
	[SerializeField] private SimpleAnimator _animator;
	[SerializeField] private Weapon _weapon;
	[SerializeField] private FloorSector _currentSector;
	private int _entity;
	public FloorSector CurrentSector => _currentSector;
	public SimpleAnimator Animator => _animator;
	public Weapon Weapon => _weapon;
	public int Entity => _entity;

	public void Initialize(int entity)
	{
		_entity = entity;
	}

	public void SetSector(FloorSector sector)
	{
		_currentSector = sector;
	}
}
