using Sirenix.OdinInspector;
#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
#endif
using Sirenix.Serialization;
using System;
using System.Collections.Generic;
using UnityEngine;

public enum SectorMode
{
	// Infinite scroll: 3 sectors are recycled (objects are moved forward/back).
	Recycling,
	// Finite level: pre-placed sectors are enabled/disabled based on player position.
	Sliding
}

[Serializable]
public class DifficultyStage
{
	public DifficultyLevel DifficultyLevel;
	public float DifficultyTimer;
	[MinValue(1f)]
	public float SpeedMultiplier = 1f;
	[MinValue(0.05f)]
	public float InterSpawnCooldown = 0.5f; // Obligatory cooldown between mob spawns
	public SmartConditionWrapper[] EndConditions; // Conditions to end stage, if null will end when timer is over
	public bool ShowTimer = true;

	public bool HasEndConditions => EndConditions != null && EndConditions.Length > 0;
}

[Serializable]
public class AdditionalLootConfig
{
	[OdinSerialize]
	public Dictionary<SmartConditionWrapper, PossibleLoot[]> AdditionalLoot = new();
}

[CreateAssetMenu(fileName = "LevelConfig", menuName = "Scriptable Objects/LevelConfig")]
public class LevelConfig : SerializedScriptableObject
{
	[SerializeField,
	 ValidateInput("ValidateStages", "Difficulty stages must be in ascending order"),
	 OnCollectionChanged("OnStagesChanged")]
	private List<DifficultyStage> _difficultyStages = new();

	[OdinSerialize]
	private List<AdditionalLootConfig> _AdditionalLootConfigs = new();

	[SerializeField, BoxGroup("Sectors")]
	private SectorMode _sectorMode = SectorMode.Recycling;
	public SectorMode SectorMode => _sectorMode;

	[SerializeField, BoxGroup("Sectors"), MinValue(0),
	 Tooltip("Sliding mode: how many sectors to keep active on each side of the player (1 = a window of 3).")]
	private int _activeSectorRadius = 1;
	public int ActiveSectorRadius => _activeSectorRadius;

	public DifficultyStage GetFirstStage(bool showTutorial = false)
	{
		var first = _difficultyStages[0];
		if (first.DifficultyLevel is DifficultyLevel.tutorial && !showTutorial)
		{
			return GetNonTutorial(0);
		}
		return first;

		DifficultyStage GetNonTutorial(int index)
		{
			if (index >= _difficultyStages.Count)
				return null;

			if (_difficultyStages[index].DifficultyLevel is DifficultyLevel.tutorial)
				return GetNonTutorial(index + 1);
			else
				return _difficultyStages[index];
		}
	}

	public DifficultyStage GetNextStage(DifficultyLevel currentLevel)
	{
		for (int i = 0; i < _difficultyStages.Count; i++)
		{
			if (_difficultyStages[i].DifficultyLevel == currentLevel)
			{
				if (i + 1 < _difficultyStages.Count)
					return _difficultyStages[i + 1];

				return null; // last stage
			}
		}
		return null; // not found
	}

	public IEnumerable<AdditionalLootConfig> GetAdditionalLootConfigs()
	{
		return _AdditionalLootConfigs;
	}

#if UNITY_EDITOR
	private bool ValidateStages(List<DifficultyStage> stages)
	{
		for (int i = 1; i < stages.Count; i++)
		{
			if (stages[i].DifficultyLevel <= stages[i - 1].DifficultyLevel)
				return false;
		}
		return true;
	}

	private void OnStagesChanged(CollectionChangeInfo info, object value)
	{
		if (info.ChangeType == CollectionChangeType.Add && value is DifficultyStage newStage)
		{
			if (_difficultyStages.Count > 1)
			{
				var prevStage = _difficultyStages[_difficultyStages.Count - 2];
				var nextLevel = prevStage.DifficultyLevel + 1;

				// Only assign if it stays within the enum range.
				if (Enum.IsDefined(typeof(DifficultyLevel), nextLevel))
					newStage.DifficultyLevel = nextLevel;
			}
		}
	}
#endif
}
