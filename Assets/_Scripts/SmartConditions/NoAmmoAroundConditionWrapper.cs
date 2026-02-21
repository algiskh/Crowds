using System;
using ECS;
using Leopotam.EcsLite;
using UnityEngine;
using UnityEngine.LightTransport;


[CreateAssetMenu(fileName = "FragsConditionWrapper", menuName = "Scriptable Objects/Smart Conditions/Frags Condition")]
public class NoAmmoAroundConditionWrapper : SmartConditionWrapper<NoAmmoAroundCondition>
{
}


[Serializable]
public sealed class NoAmmoAroundCondition : SmartCondition<NoAmmoAroundCondition>
{
	[SerializeField] private int _targetFrags = 10;


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

		if (_world.TryGetAsSingleton(out WeaponComponent weapon) && weapon.AmmoCount == 0 && hasAmmoLoot)
		{
			IsFulfilled = true;
		}
		else
		{
			IsFulfilled = false;
		}
	}

	public override void Dispose()
	{
	}

	public override NoAmmoAroundCondition CloneTyped()
	{
		// Быстрый и предсказуемый клон "только конфигов"
		return new NoAmmoAroundCondition();
	}
}
