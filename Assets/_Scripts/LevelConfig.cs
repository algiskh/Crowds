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
	[MinValue(0f), Tooltip("Breather: no mob spawns (regular and group) for the first N seconds of the stage, " +
	                       "time to pick up loot. Counts into DifficultyTimer.")]
	public float RestTime;
	public SmartConditionWrapper[] EndConditions; // Conditions to end stage, if null will end when timer is over
	public bool ShowTimer = true;

	[Tooltip("Wave mode: mobs spawn only while the stage timer runs; after it the stage waits for its EndConditions " +
	         "(e.g. MobsClearedCondition = 'kill the rest'). Off = spawning continues until the stage ends (classic).")]
	public bool SpawnOnlyWhileTimer;

	[Tooltip("Objective line shown at the top of the screen for this stage (MissionSystem → ObjectiveBanner). Empty = hidden.")]
	[TextArea(1, 3)]
	public string Objective;

	[Tooltip("Wave quota: exact number of mobs per mob id for this stage. Non-empty → spawn points spawn only ids " +
	         "with quota left and stop when it is spent (the timer then only counts the rest/minimum time). " +
	         "Pair with MobsClearedCondition, which also waits for the quota.")]
	public MobQuotaEntry[] MobQuota;

	public bool HasMobQuota => MobQuota != null && MobQuota.Length > 0;

	public bool HasEndConditions => EndConditions != null && EndConditions.Length > 0;

	/// <summary>True during the stage's RestTime. <paramref name="timerLeft"/> = DifficultyComponent.DifficultyTimer (counts down).</summary>
	public bool IsResting(float timerLeft) => RestTime > 0f && DifficultyTimer - timerLeft < RestTime;

	/// <summary>Spawners may emit mobs now: not resting, and (in wave mode) the timer hasn't run out.</summary>
	public bool IsSpawning(float timerLeft) => !IsResting(timerLeft) && !(SpawnOnlyWhileTimer && timerLeft <= 0f);
}

[Serializable]
public class MobQuotaEntry
{
	public string MobId;
	[MinValue(0)] public int Count;
}

/// <summary>
/// Per-level look/behaviour of a bonus (BonusConfig.Id): a different loot icon (e.g. skates instead of the speed
/// boot on the ice arena) plus extra modifiers granted together with the bonus modifier for the same duration.
/// </summary>
[Serializable]
public class BonusLevelOverride
{
	[Tooltip("BonusConfig.Id this override applies to (e.g. 'speedup').")]
	public string BonusId;

	[PreviewField(50, ObjectFieldAlignment.Left), Tooltip("Loot icon on this level. Empty = BonusConfig.Preview.")]
	public Sprite Preview;

	[Tooltip("Extra modifiers granted with the bonus on this level (cloned; Lifetime is set to the bonus duration), " +
	         "e.g. SkatesModifier on the ice arena.")]
	[SerializeReference] public Modifier[] ExtraModifiers = Array.Empty<Modifier>();
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

	[SerializeField, BoxGroup("Spawning"),
	 Tooltip("Player→spawn point distance window (min, max) in metres. (0,0) = derived from the sector size " +
	         "(DistanceBetweenSectors/4 … DistanceBetweenSectors) — fine for scrolling levels, too coarse for arenas.")]
	private Vector2 _spawnDistance = Vector2.zero;

	[SerializeField, BoxGroup("Spawning"),
	 Tooltip("Weapon ids mobs may drop on this level (GunConfig ids). Empty = no restriction. Other weapon drops " +
	         "are rolled as 'no loot'. Map / scripted loot is not affected.")]
	private string[] _allowedMobWeaponDrops = System.Array.Empty<string>();

	[SerializeField, BoxGroup("Spawning"),
	 Tooltip("Ammo calibers mobs never drop on this level (rolled as 'no loot').")]
	private Caliber[] _bannedMobAmmoCalibers = System.Array.Empty<Caliber>();

	[SerializeField, BoxGroup("Spawning"),
	 Tooltip("Bonus ids (LootType.Bonus) mobs never drop on this level, e.g. 'shield'.")]
	private string[] _bannedMobBonusIds = System.Array.Empty<string>();

	[SerializeField, BoxGroup("Bonuses"),
	 Tooltip("Per-level bonus overrides: loot icon and extra modifiers by BonusConfig.Id (map, crate and mob loot alike).")]
	private BonusLevelOverride[] _bonusOverrides = System.Array.Empty<BonusLevelOverride>();

	/// <summary>Override for the bonus <paramref name="bonusId"/> on this level, or null.</summary>
	public BonusLevelOverride GetBonusOverride(string bonusId)
	{
		if (_bonusOverrides == null || string.IsNullOrEmpty(bonusId))
			return null;
		for (int i = 0; i < _bonusOverrides.Length; i++)
			if (_bonusOverrides[i] != null && _bonusOverrides[i].BonusId == bonusId)
				return _bonusOverrides[i];
		return null;
	}

	/// <summary>Spawn distance window; falls back to the sector-based one when not set.</summary>
	public void GetSpawnDistance(float sectorDistance, out float min, out float max)
	{
		if (_spawnDistance.y > 0f)
		{
			min = _spawnDistance.x;
			max = _spawnDistance.y;
		}
		else
		{
			min = sectorDistance / 4f;
			max = sectorDistance;
		}
	}

	/// <summary>True when a mob may drop the weapon <paramref name="id"/> on this level.</summary>
	public bool IsMobWeaponDropAllowed(string id)
	{
		if (_allowedMobWeaponDrops == null || _allowedMobWeaponDrops.Length == 0)
			return true;
		for (int i = 0; i < _allowedMobWeaponDrops.Length; i++)
			if (_allowedMobWeaponDrops[i] == id)
				return true;
		return false;
	}

	/// <summary>
	/// True when a mob may drop <paramref name="loot"/> here: weapon whitelist (AllowedMobWeaponDrops), banned ammo
	/// calibers and banned bonus ids.
	/// </summary>
	public bool IsMobLootAllowed(PossibleLoot loot)
	{
		if (loot == null)
			return true;
		switch (loot.LootType)
		{
			case LootType.Weapon:
				return IsMobWeaponDropAllowed(loot.Id);
			case LootType.Ammo:
				if (_bannedMobAmmoCalibers != null)
					for (int i = 0; i < _bannedMobAmmoCalibers.Length; i++)
						if (_bannedMobAmmoCalibers[i] == loot.AmmoCaliber && loot.AmmoCaliber != Caliber.None)
							return false;
				return true;
			case LootType.Bonus:
				if (_bannedMobBonusIds != null)
					for (int i = 0; i < _bannedMobBonusIds.Length; i++)
						if (_bannedMobBonusIds[i] == loot.Id)
							return false;
				return true;
			default:
				return true;
		}
	}

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
