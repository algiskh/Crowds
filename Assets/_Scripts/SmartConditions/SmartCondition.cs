using Leopotam.EcsLite;
using System;

public interface ISmartCondition : IDisposable
{
	bool IsFulfilled { get; }
	void Initialize(EcsWorld world);
	void Iterate();
	ISmartCondition Clone(); // универсальный
}

[Serializable]
public abstract class SmartCondition<TSelf> : ISmartCondition
	where TSelf : SmartCondition<TSelf>
{
	protected EcsWorld _world;
	public virtual bool IsFulfilled { get; protected set; }
	public virtual void Initialize(EcsWorld world)
	{
		_world = world;
		IsFulfilled = false; // по умолчанию не выполнено
	}
	public abstract void Iterate();
	public virtual void Reset() => IsFulfilled = false;

	// Типобезопасный клон (реализации возвращают именно TSelf)
	public virtual TSelf CloneTyped()
	{
		return null;
	}

	// Универсальный клон для контейнеров/обёрток
	ISmartCondition ISmartCondition.Clone() => CloneTyped();
	public virtual void Dispose()
	{
		IsFulfilled = false;
		_world = null;
	}
}
