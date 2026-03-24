
using System;

[Serializable]
public abstract class Modifier
{
	public string Id;
	public bool ReadyToDelete;
	public BuffSource Source;
	public float Value;
	public float Lifetime;

	public T Clone<T>() where T : Modifier
	{
		return (T)MemberwiseClone();
	}
}

[Serializable]
public class SpeedModifier : Modifier
{

}

[Serializable]
public class HealthModifier : Modifier
{
	public HealthModifierType Type;
}

[Serializable]
public class DamageModifier : Modifier
{
	public DamageType Type;
	public float Chance; // Chance to apply this modifier on hit, from 0 to 1
}

[Serializable]
public class ShieldModifier : Modifier
{
	public DamageType ImmuneType;
}
