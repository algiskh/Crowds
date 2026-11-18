
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

public static class ModifierConstants
{
	public static string SpeedMeleeAttackerDebuff = "SpeedMeleeAttackerDebuff";
	public static string SpeedMeleeRecieverDebuff = "SpeedMeleeRecieverDebuff";
	public static string SpeedLowHealthDebuff = "SpeedLowHealthDebuff";
	public static string SpeedShotDebuff = "SpeedShotDebuff";
	public static string SpeedReloadDebuff = "SpeedReloadDebuff";
	public static string SpeedBonusBuff = "SpeedBoostBuff";
	public static string SpeedOilSlick = "SpeedOilSlick";

	public static string TractionIce = "TractionIce";

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

	public static string StrengthMeleeRage = "StrengthMeleeRage";
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
	public bool HasEffect;
	[ShowIf(nameof(HasEffect), true), ValueDropdown(nameof(GetFxIds))]
	public string EffectId;

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

		if (type == typeof(TractionModifier))
			return GetConstantsByPrefix("Traction");

		if (type == typeof(MeleeStrengthModifier))
			return GetConstantsByPrefix("Strength");

		return Enumerable.Empty<string>();
	}

	private IEnumerable<string> GetConstantsByPrefix(string prefix)
	{
		return typeof(ModifierConstants)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.FieldType == typeof(string) && f.Name.Contains(prefix))
			.Select(f => (string)f.GetValue(null));
	}

	private IEnumerable<string> GetFxIds()
	{
		var holder = EffectsHolder.Instance;

		if (holder == null)
			yield break;

		foreach (var fx in holder.GetAll())
		{
			yield return fx.Id;
		}
	}
}

[Serializable]
public class SpeedModifier : Modifier
{

}

/// <summary>
/// Slippery ground (ice): Value = grip, the acceleration in m/s² with which the owner can change its velocity.
/// While present, movement becomes inertial (Inertia.Step in PlayerMovementSystem / MoveSystem): the owner keeps
/// sliding and turns/stops slowly. Several → the lowest grip wins (Utils.GetMinModifier). See Docs/HockeyLevelFeature.md.
/// </summary>
[Serializable]
public class TractionModifier : Modifier
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
		if (_iterationTimer <= 0)
		{
			UnityEngine.Debug.Log($"Iterate {Id} modifier with value == {value}");
			_iterationTimer += Interval;
			value = Value;
			return true;
		}
		_iterationTimer -= deltaTime;
		return false;
	}
}

[Serializable]
public class ShieldModifier : Modifier
{
	public DamageType ImmuneType;
}

/// <summary>
/// Melee strength (the "Rage" bonus): Value = multiplier of the owner's melee damage (e.g. 3 = triple),
/// AttackSpeed = melee rate multiplier (windup and recovery are divided by it, the swing animation plays faster).
/// Damage is consumed in MeleeSpawnSystem for the attacker (RequestMeleeComponent.SourceEntity), the rate in
/// PlayerSystem / PlayerAnimationSystem (Utils.GetMeleeAttackSpeed). Several → multiplied. See Docs/BonusFeature.md.
/// </summary>
[Serializable]
public class MeleeStrengthModifier : Modifier
{
	[UnityEngine.Tooltip("Melee rate multiplier: 2.5 = windup and recovery 2.5× shorter, the swing animation 2.5× faster.")]
	public float AttackSpeed = 1f;
}
