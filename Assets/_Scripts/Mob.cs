using Scene.UI;
using UnityEngine;
using UnityEngine.AI;

public class Mob: MonoBehaviour
{
	[SerializeField] private MeshHealthBar _valueBar;
	[SerializeField] private Collider _collider;
	[SerializeField] private SimpleAnimator _animator;
	// Optional held weapon (ranged mobs): muzzle = bullet spawn + muzzle-flash anchor. Null → config offsets.
	[SerializeField] private MobWeapon _weapon;
	public Vector2 Position => transform.position;
	public IValueBar ValueBar => _valueBar;
	public Collider Collider => _collider;
	public SimpleAnimator Animator => _animator;
	public MobWeapon Weapon => _weapon;

	/// <summary>
	/// Swaps the held weapon for <paramref name="prefab"/> (RangedMobConfig.WeaponOverride), keeping the current
	/// weapon's parent bone and local pose — weapons share the convention root = grip, barrel along +Z.
	/// Called once per new instance (the mob pool is per config id, so pooled instances keep their weapon).
	/// </summary>
	public void ReplaceWeapon(MobWeapon prefab)
	{
		if (prefab == null || _weapon == null)
			return;

		var old = _weapon.transform;
		var weapon = Instantiate(prefab, old.parent);
		weapon.transform.SetLocalPositionAndRotation(old.localPosition, old.localRotation);
		weapon.transform.localScale = old.localScale;
		Destroy(old.gameObject);
		_weapon = weapon;
	}


	private NavMeshAgent _agent;

	private void Awake()
	{
		if (_collider == null)
		{
			_collider = GetComponent<Collider>();
			if (_collider == null)
			{
				Debug.LogError("Collider is not assigned and not found on the Mob GameObject.");
			}
		}
	}

	public string Id { get; private set; }
	public void SetId(string id)
	{
		Id = id;
	}
}