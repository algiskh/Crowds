using System;
using ECS;
using Leopotam.EcsLite;
using UnityEngine;

// Stage end-condition for scripted (mission) levels — DifficultyStage.EndConditions. See Docs/KeyLevelFeature.md.

[CreateAssetMenu(fileName = "MobsClearedCondition", menuName = "Scriptable Objects/Smart Conditions/Mobs Cleared Condition")]
public class MobsClearedConditionWrapper : SmartConditionWrapper<MobsClearedCondition>
{
}

/// <summary>
/// "Wave cleared": no live mob and no pending spawn request. Pair with DifficultyStage.SpawnOnlyWhileTimer —
/// the timer is the spawn window, after it the stage waits until the player kills the rest.
/// </summary>
[Serializable]
public sealed class MobsClearedCondition : SmartCondition<MobsClearedCondition>
{
	// Also guarantees the prototype has a serialized field (a field-less one may deserialize as null, and a null
	// condition counts as fulfilled in DifficultySystem).
	[SerializeField, Min(0), Tooltip("Cleared when at most this many mobs are alive (0 = all dead).")]
	private int _maxAlive;

	public override void Iterate()
	{
		int alive = _world.Filter<MobComponent>().Inc<HealthComponent>().End().GetEntitiesCount();
		int pending = _world.Filter<MobSpawnRequestComponent>().End().GetEntitiesCount();
		// Wave quota stages: not cleared while mobs are still due to spawn.
		var difficultyStage = _world.TryGetAsSingleton<DifficultyComponent>(out var difficulty) ? difficulty.Stage : null;
		int due = WaveQuota.RemainingTotal(_world, difficultyStage);
		IsFulfilled = alive <= _maxAlive && pending == 0 && due == 0;
	}

	public override MobsClearedCondition CloneTyped() => new() { _maxAlive = _maxAlive };
}
