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

	[Tooltip("Fallback muzzle (when the prefab has no WeaponModel): vertical offset above the mob origin.")]
	[SerializeField, MinValue(0f)] private float _muzzleHeight = 1f;

	[Tooltip("Fallback muzzle (when the prefab has no WeaponModel): forward offset from the mob origin.")]
	[SerializeField, MinValue(0f)] private float _muzzleForwardOffset = 0.5f;

	[Title("Burst")]
	[Tooltip("Shots per attack. 1 = single shot (classic). N = a burst of N shots, BurstInterval apart, after one windup.")]
	[SerializeField, MinValue(1)] private int _burstCount = 1;

	[Tooltip("Seconds between shots inside a burst.")]
	[SerializeField, MinValue(0f), ShowIf(nameof(IsBurst))] private float _burstInterval = 0.12f;

	[Tooltip("Replay the fire clip from the start on every shot of a burst (recoil per bullet). Off = the clip plays once.")]
	[SerializeField, ShowIf(nameof(IsBurst))] private bool _restartFireAnimationPerShot = true;

	[Title("Weapon View")]
	[Tooltip("Optional: replaces the prefab's held weapon (Mob.Weapon) on this config's instances — same model, different " +
		"gun. Must follow the WeaponModel convention: root = grip, barrel along +Z. Null = keep the prefab's weapon.")]
	[SerializeField] private WeaponModel _weaponOverride;

	[Title("Ranged Animations")]
	[Tooltip("Played while chasing the player (Animator state name = AnimationType name).")]
	[SerializeField] private Scene.Animation.AnimationType _chaseAnimation = Scene.Animation.AnimationType.Run;

	[Tooltip("Played during the windup (aim / telegraph).")]
	[SerializeField] private Scene.Animation.AnimationType _windupAnimation = Scene.Animation.AnimationType.Attack;

	[Tooltip("Played when the bullet leaves the muzzle (recoil). Same as Windup ⇒ the windup clip simply continues.")]
	[SerializeField] private Scene.Animation.AnimationType _fireAnimation = Scene.Animation.AnimationType.Attack;

	private bool IsBurst => _burstCount > 1;

	public float AttackRange => _attackRange;
	public GunConfig GunConfig => _gunConfig;
	public float WindupDelay => _windupDelay;
	public float Cooldown => _cooldown;
	public float MuzzleHeight => _muzzleHeight;
	public float MuzzleForwardOffset => _muzzleForwardOffset;
	public Scene.Animation.AnimationType ChaseAnimation => _chaseAnimation;
	public Scene.Animation.AnimationType WindupAnimation => _windupAnimation;
	public Scene.Animation.AnimationType FireAnimation => _fireAnimation;
	public int BurstCount => Mathf.Max(1, _burstCount);
	public float BurstInterval => _burstInterval;
	public bool RestartFireAnimationPerShot => _restartFireAnimationPerShot;
	public WeaponModel WeaponOverride => _weaponOverride;
}
