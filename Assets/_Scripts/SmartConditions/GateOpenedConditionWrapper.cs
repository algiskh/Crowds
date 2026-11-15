using System;
using ECS;
using Leopotam.EcsLite;
using UnityEngine;

// Stage end-condition for scripted (mission) levels — DifficultyStage.EndConditions. See Docs/KeyLevelFeature.md.

[CreateAssetMenu(fileName = "GateOpenedCondition", menuName = "Scriptable Objects/Smart Conditions/Gate Opened Condition")]
public class GateOpenedConditionWrapper : SmartConditionWrapper<GateOpenedCondition>
{
}

/// <summary>Fulfilled once the LevelGate with <c>_gateId</c> has been opened (MissionComponent.OpenedGates).</summary>
[Serializable]
public sealed class GateOpenedCondition : SmartCondition<GateOpenedCondition>
{
	[SerializeField] private string _gateId = "gate";

	public override void Iterate()
	{
		IsFulfilled = _world.TryGetAsSingleton<MissionComponent>(out var mission) &&
		              mission.OpenedGates != null && mission.OpenedGates.Contains(_gateId);
	}

	public override GateOpenedCondition CloneTyped() => new() { _gateId = _gateId };
}
