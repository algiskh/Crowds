using System;
using Leopotam.EcsLite;
using UnityEngine;


[CreateAssetMenu(fileName = "FragsConditionWrapper", menuName = "Scriptable Objects/Smart Conditions/Frags Condition")]
public class FragsConditionWrapper : SmartConditionWrapper<FragsCondition>
{
}


[Serializable]
public sealed class FragsCondition : SmartCondition<FragsCondition>
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
		var filter = _world.Filter<RequestUpdateFragCountComponent>()
			.End();

		Debug.Log($"{nameof(FragsCondition)}: Iterate");
		foreach (var entity in filter)
		{
				TryAddFrag();
		}

		if (!IsFulfilled && _current >= _targetFrags)
			IsFulfilled = true;
	}

	private void TryAddFrag()
	{
		if (IsFulfilled)
			return;

		_current++;
		Debug.Log($"{nameof(FragsCondition)}: Add frag, current: {_current}, target: {_targetFrags}");
	}

	public int Current => _current;

	public override void Dispose()
	{
	}

	public override FragsCondition CloneTyped()
	{
		// Быстрый и предсказуемый клон "только конфигов"
		return new FragsCondition
		{
			_targetFrags = this._targetFrags
			// runtime-поля (_current, IsFulfilled) не копируем
		};
	}
}
