using System;
using System.Linq;
using UnityEngine;

[Flags]
public enum LootSpawnCondition
{
	PlayerNearby = 1 << 0,
	PlayerFarby = 1 << 1,
	NoAmmo = 1 << 2,
	LowHealth = 1 << 3,
}

[Serializable]
public class LootSpawnConfig : IWeightable
{

	[SerializeField] private LootType _lootType;
	[SerializeField] private string _lootId;
	[SerializeField] private int _count;
	[SerializeField] private float _weight = 1f;
	[SerializeField] private SpawnPreset[] _spawnPresets;
	[SerializeField] private LootSpawnCondition _spawnConditions;

	public LootType LootType => _lootType;
	public int Count => _count;
	public float Weight => _weight;
	public string LootId => _lootId;
	public SpawnPreset[] SpawnPresets => _spawnPresets;
	public LootSpawnCondition SpawnConditions => _spawnConditions;

	public float GetCooldown(DifficultyLevel level)
	{
		foreach (var preset in _spawnPresets)
		{
			if (preset.DifficultyLevel == level)
			{
				return preset.Cooldown;
			}
		}
		throw new ArgumentException($"No spawn preset found for difficulty level: {level}");
	}
}

public class LootSpawnPoint : MonoBehaviour
{
	[SerializeField] private LootSpawnConfig[] _spawnConfigs;

	public LootSpawnConfig[] SpawnConfigs => _spawnConfigs;

	public LootSpawnConfig GetRandomSpawnConfig(DifficultyLevel level)
	{
		var configs = _spawnConfigs.Where(config => config.SpawnPresets.Any(preset => preset.DifficultyLevel == level));
		return configs.GetRandomByWeight();
	}
}
