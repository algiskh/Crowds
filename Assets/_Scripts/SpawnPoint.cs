using System;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

[Serializable]
public class MobSpawnConfig : IWeightable
{
	[Serializable] public class SpawnPreset
	{
		public DifficultyLevel DifficultyLevel;
		public float Cooldown;
	}

	[SerializeField] private string _mobId;
	[SerializeField] private float _weight = 1f;
	[SerializeField] private SpawnPreset[] _spawnPresets;

	public float Weight => _weight;
	public string MobId => _mobId;
	public SpawnPreset[] SpawnPresets => _spawnPresets;

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

public class SpawnPoint : MonoBehaviour
{
	[SerializeField] private MobSpawnConfig[] _spawnConfigs;

	public MobSpawnConfig[] SpawnConfigs => _spawnConfigs;

	public MobSpawnConfig GetRandomSpawnConfig(DifficultyLevel level)
	{
		var configs = _spawnConfigs.Where(config => config.SpawnPresets.Any(preset => preset.DifficultyLevel == level));
		return configs.GetRandomByWeight();
	}
}
