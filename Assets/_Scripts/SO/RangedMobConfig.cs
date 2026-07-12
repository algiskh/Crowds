using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Ranged (shooter) mob config. In all the "regular" parameters (health, speed, loot, modifiers) it
/// is identical to MobConfig — it inherits from it. It only adds the engage distance and the shot
/// definition: a <see cref="GunConfig"/> (the *same* type the player's weapon uses — damage, speed,
/// radius, check type, spread/accuracy, projectiles-per-shot, caliber → projectile prefab), plus the
/// telegraph timings (wind-up before the shot and recovery after it).
///
/// Behaviour — see RangedAttackerSystem (Chase → Windup → fire → Cooldown, mirroring the melee mob).
/// </summary>
[CreateAssetMenu(fileName = "RangedMobConfig", menuName = "Scriptable Objects/RangedMobConfig", order = 4)]
public class RangedMobConfig : MobConfig
{
	[Title("Ranged Attacker")]
	[Tooltip("Distance at which the mob stops chasing and starts shooting. Beyond it, it chases.")]
	[SerializeField, MinValue(0f)] private float _attackRange = 8f;

	[Required, Tooltip("Shot definition: damage, speed, radius, check type, accuracy, projectiles-per-shot " +
		"and caliber (→ projectile prefab / fire sound). Same GunConfig model the player's weapon uses.")]
	[SerializeField] private GunConfig _gunConfig;

	[Tooltip("Wind-up (telegraph) seconds before the shot leaves the muzzle. The 'attack' clip plays here.")]
	[SerializeField, MinValue(0f)] private float _windupDelay = 0.4f;

	[Tooltip("Recovery seconds the mob stays stopped after firing, before it re-engages.")]
	[SerializeField, MinValue(0f)] private float _cooldown = 0.8f;

	[Tooltip("Vertical offset of the muzzle above the mob origin (so the shot leaves at chest/gun height).")]
	[SerializeField, MinValue(0f)] private float _muzzleHeight = 1f;

	[Tooltip("Forward offset of the muzzle from the mob origin (so the shot doesn't spawn inside its own collider).")]
	[SerializeField, MinValue(0f)] private float _muzzleForwardOffset = 0.5f;

	public float AttackRange => _attackRange;
	public GunConfig GunConfig => _gunConfig;
	public float WindupDelay => _windupDelay;
	public float Cooldown => _cooldown;
	public float MuzzleHeight => _muzzleHeight;
	public float MuzzleForwardOffset => _muzzleForwardOffset;
}
