using UnityEngine;
using UnityEngine.AI;

public class Player : MonoBehaviour
{
	[SerializeField] private SimpleAnimator _animator;
	[Tooltip("Parameter-driven body animator on the character model (see Docs/PlayerAnimation.md). Optional.")]
	[SerializeField] private PlayerAnimator _bodyAnimator;
	[SerializeField] private Weapon _weapon;
	[SerializeField] private MeleeConfig _meleeConfig;
	[SerializeField] private FloorSector _currentSector;
	private int _entity;
	public FloorSector CurrentSector => _currentSector;
	public SimpleAnimator Animator => _animator;
	public PlayerAnimator BodyAnimator => _bodyAnimator;
	public Weapon Weapon => _weapon;
	public int Entity => _entity;
	public MeleeConfig MeleeConfig => _meleeConfig;

	public void Initialize(int entity)
	{
		_entity = entity;
	}

	public void SetSector(FloorSector sector)
	{
		_currentSector = sector;
	}
}
