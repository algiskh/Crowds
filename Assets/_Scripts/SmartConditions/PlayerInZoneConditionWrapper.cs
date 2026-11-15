using System;
using ECS;
using Leopotam.EcsLite;
using UnityEngine;

// Stage end-condition for scripted (mission) levels — DifficultyStage.EndConditions. See Docs/KeyLevelFeature.md.

[CreateAssetMenu(fileName = "PlayerInZoneCondition", menuName = "Scriptable Objects/Smart Conditions/Player In Zone Condition")]
public class PlayerInZoneConditionWrapper : SmartConditionWrapper<PlayerInZoneCondition>
{
}

/// <summary>Fulfilled while the player stands inside the LevelZone with <c>_zoneId</c> (latched once reached).</summary>
[Serializable]
public sealed class PlayerInZoneCondition : SmartCondition<PlayerInZoneCondition>
{
	[SerializeField] private string _zoneId = "zone";

	[NonSerialized] private LevelZone _zone;

	public override void Iterate()
	{
		if (IsFulfilled)
			return; // latched: reaching the zone once is enough

		if (_zone == null && _world.TryGetAsSingleton<MissionComponent>(out var mission) && mission.Zones != null)
		{
			foreach (var zone in mission.Zones)
				if (zone != null && zone.Id == _zoneId)
				{
					_zone = zone;
					break;
				}
		}

		if (_zone == null || !_world.TryGetAsSingleton<PlayerComponent>(out var player) || player.Value == null)
			return;

		IsFulfilled = _zone.Contains(player.Value.transform.position);
	}

	public override void Dispose()
	{
		base.Dispose();
		_zone = null;
	}

	public override PlayerInZoneCondition CloneTyped() => new() { _zoneId = _zoneId };
}
