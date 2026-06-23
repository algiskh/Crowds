
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class DamageDecalSet
{
	[SerializeField] private DamageSourceType _source;
	[SerializeField] private string[] _decalIds;

	public DamageSourceType Source => _source;

	/// <summary>Random decal id from the pool, or null if the pool is empty.</summary>
	public string GetRandomId()
	{
		if (_decalIds == null || _decalIds.Length == 0)
			return null;
		return _decalIds[UnityEngine.Random.Range(0, _decalIds.Length)];
	}
}

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
	[SerializeReference, OdinSerialize] private Modifier[] _attackModifiers;
	// Decal pools per damage source (bullet/melee/explosion). Empty -> fallback to the shared "Blood".
	[SerializeField] private DamageDecalSet[] _damageDecals;
	// Effect spawned on death (from EffectsHolder). Empty -> fallback to the shared "zombie_dead".
	[SerializeField, ValueDropdown(nameof(GetEffectIds))] private string _deathEffectId;
	public string Id => _id;
	public float Health => _health;
	public float Speed => _speed;
	public Mob Prefab => _prefab;
	public float HitRadius => _collisionRadius;
	public PossibleLoot[] PossibleLoots => _possibleLoots;
	public float HitCooldown => _hitCooldown;
	public float Damage => _damage;
	public TargetType TargetType => _targetType;
	public Modifier[] AttackModifiers => _attackModifiers;
	public string DeathEffectId => _deathEffectId;

	/// <summary>
	/// Random decal id for the given damage source, or null if no pool is configured
	/// for that source (the caller applies a fallback).
	/// </summary>
	public string GetDecalId(DamageSourceType source)
	{
		if (_damageDecals == null)
			return null;
		for (int i = 0; i < _damageDecals.Length; i++)
		{
			if (_damageDecals[i].Source == source)
				return _damageDecals[i].GetRandomId();
		}
		return null;
	}

	private IEnumerable<string> GetEffectIds()
	{
		var holder = EffectsHolder.Instance;

		if (holder == null)
			yield break;

		foreach (var fx in holder.GetAll())
		{
			yield return fx.Id;
		}
	}
}
