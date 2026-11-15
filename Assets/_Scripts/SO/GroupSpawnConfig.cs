using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Squad config (a group of mobs in formation): formation type, leader, follower list, inter-slot
/// spacing and spawn presets per difficulty level. Like a regular point (MobSpawnConfig),
/// the squad spawns only on DifficultyLevels that have a preset — this is how groups are
/// "bound to a level/stage". In v1 followers are regular mobs (no melee/grenadier behavior),
/// so formation and combat states don't conflict.
/// </summary>
[CreateAssetMenu(fileName = "GroupSpawnConfig", menuName = "Scriptable Objects/GroupSpawnConfig")]
public class GroupSpawnConfig : ScriptableObject
{
	[Title("Formation")]
	[SerializeField] private ECS.FormationType _formation = ECS.FormationType.Wedge;
	[SerializeField, Required] private string _leaderMobId;
	[SerializeField] private FollowerEntry[] _followers;

	[Title("Geometry")]
	[SerializeField, MinValue(0.1f)] private float _spacingX = 1.6f;
	[SerializeField, MinValue(0.1f)] private float _spacingZ = 1.6f;

	[Title("Spawn by difficulty level")]
	[Tooltip("On which difficulty stages the squad appears and with what base cooldown. " +
		"No preset for the current stage — the squad doesn't spawn.")]
	[SerializeField] private SpawnPreset[] _spawnPresets;
	[SerializeField, MinValue(0f), Tooltip("Delay before the point's first spawn check.")]
	private float _initialDelay = 0f;

	public ECS.FormationType Formation => _formation;
	public string LeaderMobId => _leaderMobId;
	public FollowerEntry[] Followers => _followers;
	public float SpacingX => _spacingX;
	public float SpacingZ => _spacingZ;
	public float InitialDelay => _initialDelay;

	/// <summary>
	/// Base cooldown for the difficulty stage. false — no preset, the squad doesn't spawn on this stage.
	/// </summary>
	public bool TryGetCooldown(DifficultyLevel level, out float cooldown)
	{
		if (_spawnPresets != null)
		{
			foreach (var preset in _spawnPresets)
			{
				if (preset.DifficultyLevel == level)
				{
					cooldown = preset.Cooldown;
					return true;
				}
			}
		}
		cooldown = 0f;
		return false;
	}

	[Serializable]
	public class FollowerEntry
	{
		[SerializeField] private string _mobId;
		[SerializeField, MinValue(1)] private int _count = 1;

		public string MobId => _mobId;
		public int Count => _count;
	}
}
