
using System;

[Serializable]
public abstract class Modifier
{
	public string Id;
	public BuffSource Source;
	public float Value;
	public float Lifetime;
}

public class SpeedModifier: Modifier
{

}

public class HealthModifier: Modifier
{
	public HealthModifierType Type;
}

public class DamageModifier: Modifier
{

}

public class ShieldModifier: Modifier
{

}