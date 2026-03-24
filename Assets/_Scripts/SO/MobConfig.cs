
using System;
using UnityEngine;

[CreateAssetMenu(fileName = "MobConfig", menuName = "Scriptable Objects/MobConfig", order = 1)]
public class MobConfig: ScriptableObject
{

	[SerializeField] private string _id;
	[SerializeField] private float _health;
	[SerializeField] private float _speed;
	[SerializeField] private float _damage;
	[SerializeField] private float _collisionRadius;
	[SerializeField] private float _hitCooldown;
	[SerializeField] private Mob _prefab;
	[SerializeField] private PossibleLoot[] _possibleLoots;
	[SerializeField] private TargetType _targetType;
	public string Id => _id;
	public float Health => _health;
	public float Speed => _speed;
	public Mob Prefab => _prefab;
	public float HitRadius => _collisionRadius;
	public PossibleLoot[] PossibleLoots => _possibleLoots;
	public float HitCooldown => _hitCooldown;
	public float Damage => _damage;
	public TargetType TargetType => _targetType;
}
