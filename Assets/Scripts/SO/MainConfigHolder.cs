using System;
using UnityEngine;

[Obsolete]
[CreateAssetMenu(fileName = "MainConfigHolder", menuName = "Scriptable Objects/MainConfigHolder", order = 1)]
public class MainConfigHolder : ScriptableObject
{
	[SerializeField] private MobConfig[] _mobConfigs;
	[SerializeField] private PlayerConfig _playerConfig;
	[SerializeField] private GunConfig _gunConfig;
	[SerializeField] private float _minSpawnCoolDown = 1f;
	[SerializeField] private float _maxSpawnCoolDown = 10f;
	[SerializeField] private float _defaultCollisionRadius = 0.9f;
	[SerializeField] private int _initialAmmo = 30;
	[SerializeField] private Loot _lootPrefab;

	public float MinSpawnCoolDown => _minSpawnCoolDown;
	public float MaxSpawnCoolDown => _maxSpawnCoolDown;
	public float DefaultCollisionRadius => _defaultCollisionRadius;
	public int InitialAmmo => _initialAmmo;
	public PlayerConfig PlayerConfig => _playerConfig;
	public GunConfig GunConfig => _gunConfig;
	public Loot LootPrefab => _lootPrefab;

	public MobConfig GetRandomConfig()
	{
		if (_mobConfigs == null || _mobConfigs.Length == 0)
		{
			Debug.LogWarning("No mob configs available.");
			return null;
		}
		int randomIndex = UnityEngine.Random.Range(0, _mobConfigs.Length);
		return _mobConfigs[randomIndex];
	}

	public MobConfig GetConfig(string id)
	{
		foreach (var config in _mobConfigs)
		{
			if (config.Id == id)
			{
				return config;
			}
		}
		Debug.LogWarning($"Config with ID {id} not found.");
		return null;
	}
}