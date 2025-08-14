using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEngine;

public abstract class SmartConditionWrapper : ScriptableObject
{
	public abstract ISmartCondition GetCopyUntyped();
}

public class SmartConditionWrapper<T> : SmartConditionWrapper where T : class, ISmartCondition
{
	[SerializeField]
	private T _prototype;

	public bool HasPrototype => _prototype != null;

	public sealed override ISmartCondition GetCopyUntyped()
	{
		if (_prototype == null) return null;

		// Нормальный путь: использовать контрактный Clone()  
		var copy = _prototype.Clone();

		// Страховка (на случай нестандартной реализации, вернувшей null):  
		if (copy == null)
			copy = (ISmartCondition)SerializationUtility.CreateCopy(_prototype);

		return copy;
	}

	// Опционально: типобезопасная версия
	public TSelf GetCopy<TSelf>() where TSelf : class, ISmartCondition
	{
		if (_prototype is TSelf)
		{
			var copy = _prototype.Clone();         // вернёт ISmartCondition
			return copy as TSelf;                  // безопасное приведение
		}
		return null;
	}
}
