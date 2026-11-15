using System;
using ECS;
using Leopotam.EcsLite;
using UnityEngine;

[CreateAssetMenu(fileName = "NoAmmoAroundConditionWrapper", menuName = "Scriptable Objects/Smart Conditions/No Ammo Around Condition")]
public class NoAmmoAroundConditionWrapper : SmartConditionWrapper<NoAmmoAroundCondition>
{
}

/// <summary>
/// "The player is out of ammo and there's none lying around" — drives the AdditionalLoot fallback
/// (AdditionalLootSpawnSystem). Defaults keep the original rule: current caliber reserve == 0 and no ammo loot of any
/// caliber on the map. Options: count only ammo loot usable by the current weapon (same caliber or None), and treat
/// a small reserve (magazine + reserve &lt;= threshold) as "out of ammo".
/// </summary>
[Serializable]
public sealed class NoAmmoAroundCondition : SmartCondition<NoAmmoAroundCondition>
{
	[SerializeField, Tooltip("Only ammo loot of the current weapon's caliber (or caliber None) counts as 'ammo around'.")]
	private bool _sameCaliberOnly;
	[SerializeField, Min(0), Tooltip("Fulfilled when magazine + reserve of the current caliber is at most this. " +
	                                 "0 = the original rule (reserve == 0, magazine ignored).")]
	private int _lowAmmoThreshold;

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

		if (!_world.TryGetAsSingleton(out WeaponComponent weapon) || weapon.GunConfig == null
			|| !_world.TryGetAsSingleton(out AmmoInventoryComponent inventory))
		{
			IsFulfilled = false;
			return;
		}
		var caliber = weapon.GunConfig.Caliber;

		bool hasAmmoLoot = false;
		foreach (var lootEntity in lootFilter)
		{
			var loot = lootPool.Get(lootEntity);
			if (loot.LootType != LootType.Ammo)
				continue;
			if (_sameCaliberOnly && loot.AmmoCaliber != Caliber.None && loot.AmmoCaliber != caliber)
				continue;
			hasAmmoLoot = true;
			break;
		}

		bool outOfAmmo = _lowAmmoThreshold > 0
			? inventory.Get(caliber) + weapon.CurrentMagazineCount <= _lowAmmoThreshold
			: inventory.Get(caliber) == 0;
		IsFulfilled = outOfAmmo && !hasAmmoLoot;
	}

	public override NoAmmoAroundCondition CloneTyped()
	{
		return new NoAmmoAroundCondition { _sameCaliberOnly = _sameCaliberOnly, _lowAmmoThreshold = _lowAmmoThreshold };
	}
}
