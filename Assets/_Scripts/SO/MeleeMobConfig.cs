using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Melee mob config. In all the "regular" parameters (health, speed, loot, modifiers) it is
/// identical to MobConfig - it inherits from it. It only adds the attack-start distance and a
/// reference to a MeleeConfig - the same melee "category" the player uses (damage, radius,
/// target, wind-up Delay and Cooldown are defined there).
/// Behaviour - see MeleeAttackerSystem.
/// </summary>
[CreateAssetMenu(fileName = "MeleeMobConfig", menuName = "Scriptable Objects/MeleeMobConfig", order = 3)]
public class MeleeMobConfig : MobConfig
{
	[Title("Melee Attacker")]
	[Tooltip("Distance at which the mob stops and starts attacking. Must be >= Range in MeleeConfig.")]
	[SerializeField, MinValue(0f)] private float _attackRange = 2f;

	[Required, Tooltip("Melee attack category: damage, radius, target, modifiers, wind-up (Delay) and cooldown (Cooldown). Same model as the player's attack.")]
	[SerializeField] private MeleeConfig _meleeConfig;

	public float AttackRange => _attackRange;
	public MeleeConfig MeleeConfig => _meleeConfig;
}
