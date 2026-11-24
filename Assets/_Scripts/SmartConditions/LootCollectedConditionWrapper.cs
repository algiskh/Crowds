using System;
using Leopotam.EcsLite;
using UnityEngine;

// Stage end-condition for scripted (mission) levels — DifficultyStage.EndConditions. See Docs/TutorialLevelFeature.md.

[CreateAssetMenu(fileName = "LootCollectedCondition", menuName = "Scriptable Objects/Smart Conditions/Loot Collected Condition")]
public class LootCollectedConditionWrapper : SmartConditionWrapper<LootCollectedCondition>
{
}

/// <summary>
/// "The player picked it up": fulfilled once matching map / stage loot has appeared and none of it is left on the
/// floor. Mob drops (they carry a LifeTimeComponent and may simply expire) are ignored. Map and stage loot never
/// despawn, so "gone" means "collected". A latch (<c>_sawAny</c>) covers the frames between the stage start and the
/// loot actually spawning (StageLoot raises spawn requests; LootSystem creates the entities later).
/// </summary>
[Serializable]
public sealed class LootCollectedCondition : SmartCondition<LootCollectedCondition>
{
	[SerializeField] private LootType _lootType = LootType.Ammo;
	[SerializeField, Tooltip("Loot id to match (bonus id 'shield', weapon id...). Empty = any of the type.")]
	private string _id = string.Empty;

	[NonSerialized] private bool _sawAny;

	public override void Initialize(EcsWorld world)
	{
		base.Initialize(world);
		_sawAny = false;
	}

	public override void Iterate()
	{
		if (IsFulfilled)
			return; // latched

		var lootPool = _world.GetPool<LootComponent>();
		var disposablePool = _world.GetPool<DisposableComponent>();
		var filter = _world.Filter<LootComponent>().Inc<DisposableComponent>().Exc<LifeTimeComponent>().End();

		bool anyLeft = false;
		foreach (var entity in filter)
		{
			if (disposablePool.Get(entity).IsDisposed)
				continue;
			ref var loot = ref lootPool.Get(entity);
			if (loot.LootType != _lootType)
				continue;
			if (!string.IsNullOrEmpty(_id) && loot.Id != _id)
				continue;
			anyLeft = true;
			break;
		}

		if (anyLeft)
			_sawAny = true;
		IsFulfilled = _sawAny && !anyLeft;
	}

	public override LootCollectedCondition CloneTyped() => new() { _lootType = _lootType, _id = _id };
}
