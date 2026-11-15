using System;
using System.Linq;
using UnityEngine;

[Serializable]
public class MobSpawnConfig : IWeightable
{

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

	public bool TryGetRandomSpawnConfig(DifficultyLevel level, out MobSpawnConfig config)
	{
		config = null;
		var configs = _spawnConfigs.Where(config => config.SpawnPresets.Any(preset => preset.DifficultyLevel == level));
		config = configs.GetRandomByWeight();
		return config != null;
	}

	/// <summary>Same, restricted to configs accepted by <paramref name="filter"/> (e.g. mob ids with wave quota left).</summary>
	public bool TryGetRandomSpawnConfig(DifficultyLevel level, Func<MobSpawnConfig, bool> filter, out MobSpawnConfig config)
	{
		var configs = _spawnConfigs.Where(c => filter(c) && c.SpawnPresets.Any(preset => preset.DifficultyLevel == level));
		config = configs.GetRandomByWeight();
		return config != null;
	}
}
