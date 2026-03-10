using System;
using ECS;
using Leopotam.EcsLite;
using UnityEngine;

[CreateAssetMenu(fileName = "NoAmmoAroundConditionWrapper", menuName = "Scriptable Objects/Smart Conditions/No Ammo Around Condition")]
public class NoAmmoAroundConditionWrapper : SmartConditionWrapper<NoAmmoAroundCondition>
{
}

[Serializable]
public sealed class NoAmmoAroundCondition : SmartCondition<NoAmmoAroundCondition>
{
	[NonSerialized] private int _current;

	public override void Initialize(EcsWorld world)
	{
		Debug.Log($"{nameof(FragsCondition)}: Initialize");
		base.Initialize(world);
		_current = 0;
	}

	public override void Iterate()
	{
		var lootPool = _world.GetPool<LootComponent>();
		var lootFilter = _world.Filter<LootComponent>().End();

		bool hasAmmoLoot = false;
		foreach (var lootEntity in lootFilter)
		{
			var loot = lootPool.Get(lootEntity);
			if (loot.LootType == LootType.Ammo)
			{
				hasAmmoLoot = true; break;
			}
		}

		if (_world.TryGetAsSingleton(out WeaponComponent weapon) && weapon.AmmoCount == 0 && !hasAmmoLoot)
		{
			IsFulfilled = true;
		}
		else
		{
			IsFulfilled = false;
		}
	}

	public override NoAmmoAroundCondition CloneTyped()
	{
		return new NoAmmoAroundCondition();
	}
}
