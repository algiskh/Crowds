using System;
using Leopotam.EcsLite;
using UnityEngine;

[CreateAssetMenu(fileName = "BreakablesClearedConditionWrapper", menuName = "Scriptable Objects/Smart Conditions/Breakables Cleared Condition")]
public class BreakablesClearedConditionWrapper : SmartConditionWrapper<BreakablesClearedCondition>
{
}

/// <summary>
/// Fulfilled when no matching breakable remains alive. Empty <c>_configId</c> = watch ALL breakables;
/// otherwise only those whose <see cref="BreakableConfig.Id"/> matches.
///
/// A latch (<c>_sawAny</c>) prevents reporting "cleared" before any matching breakable has ever
/// existed — so a stage that spawns its breakables on start (via LevelEventSystem) can't advance in
/// the one-frame window before those breakables register their ECS state.
/// </summary>
[Serializable]
public sealed class BreakablesClearedCondition : SmartCondition<BreakablesClearedCondition>
{
	[SerializeField, Tooltip("Breakable config id to watch. Empty = any breakable.")]
	private string _configId;

	[NonSerialized] private bool _sawAny;

	public override void Initialize(EcsWorld world)
	{
		base.Initialize(world);
		_sawAny = false;
	}

	public override void Iterate()
	{
		var pool = _world.GetPool<BreakableComponent>();
		var filter = _world.Filter<BreakableComponent>().End();

		bool anyAlive = false;
		foreach (var entity in filter)
		{
			if (string.IsNullOrEmpty(_configId))
			{
				anyAlive = true;
				break;
			}

			var breakable = pool.Get(entity);
			if (breakable.Config != null && breakable.Config.Id == _configId)
			{
				anyAlive = true;
				break;
			}
		}

		if (anyAlive)
			_sawAny = true;

		// Only "cleared" once we've actually seen a target breakable and now none remain.
		IsFulfilled = _sawAny && !anyAlive;
	}

	public override BreakablesClearedCondition CloneTyped()
	{
		return new BreakablesClearedCondition { _configId = _configId };
	}
}
