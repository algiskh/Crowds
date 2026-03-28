
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class ModifierConstants
{
	public static string SpeedMeleeAttackerDebuff = "SpeedMeleeAttackerDebuff";
	public static string SpeedMeleeRecieverDebuff = "SpeedMeleeRecieverDebuff";
	public static string SpeedLowHealthDebuff = "SpeedLowHealthDebuff";
	public static string SpeedShotDebuff = "SpeedShotDebuff";
	public static string SpeedReloadDebuff = "SpeedReloadDebuff";

	public static string DamageMeleeBleeding = "DamageMeleeBleeding";
	public static string DamageMeleeBurning = "DamageMeleeBurning";
	public static string DamageShotBleeding = "DamageShotBleeding";
	public static string DamagePoisoning = "DamagePoisoning";

	public static string HealthBleeding = "HealthBleeding";
	public static string HealthPoisoning = "HealthPoisoning";
	public static string HealthBurning = "HealthBurning";
	public static string HealthRegeneration = "HealthRegeneration";
	public static string HealthMagicDebuff = "HealthMagicDebuff";
	public static string HealthMagicBuff = "HealthMagicBuff";

	public static string ShieldElectricity = "ShieldElectricity";
	public static string ShieldFire = "ShieldFire";
	public static string ShieldMagic = "ShieldMagic";
	public static string ShieldPhysical = "ShieldPhysical";
}

public interface IIteratableModifier
{
	public bool TryIterate(float deltaTime, out float value);
}

[Serializable]
public abstract class Modifier
{
	[ValueDropdown(nameof(GetModifierIds))]
	public string Id;
	public BuffSource Source;
	public float Value;
	public float Lifetime;

	public T Clone<T>() where T : Modifier
	{
		return (T)MemberwiseClone();
	}

	private IEnumerable<string> GetModifierIds()
	{
		var type = this.GetType();

		if (type == typeof(SpeedModifier))
			return GetConstantsByPrefix("Speed");

		if (type == typeof(HealthModifier))
			return GetConstantsByPrefix("Health");

		if (type == typeof(DamageModifier))
			return GetConstantsByPrefix("Damage");

		if (type == typeof(ShieldModifier))
			return GetConstantsByPrefix("Shield");

		return Enumerable.Empty<string>();
	}

	private IEnumerable<string> GetConstantsByPrefix(string prefix)
	{
		return typeof(ModifierConstants)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.FieldType == typeof(string) && f.Name.Contains(prefix))
			.Select(f => (string)f.GetValue(null));
	}
}

[Serializable]
public class SpeedModifier : Modifier
{

}

[Serializable]
public class HealthModifier : Modifier
{
	public float Interval;
	public HealthModifierType Type;
}

[Serializable]
public class DamageModifier : Modifier, IIteratableModifier
{
	public float Interval;
	public DamageType Type;
	public float Chance; // Chance to apply this modifier on hit, from 0 to 1
	private float _iterationTimer;

	public DamageModifier()
	{
		_iterationTimer = 0;
	}

	public bool TryIterate(float deltaTime, out float value)
	{
		value = 0;
		if (_iterationTimer > Interval)
		{
			_iterationTimer -= Interval;
			value = Value;
			return true;
		}
		return false;
	}
}

[Serializable]
public class ShieldModifier : Modifier
{
	public DamageType ImmuneType;
}
