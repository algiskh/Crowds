using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Grenadier mob config. In all the "regular" parameters (health, speed, melee damage, loot,
/// modifiers) it is identical to MobConfig - it inherits from it. It only adds the grenade-throw
/// behaviour: X/Y distances, cooldown between throws and the grenade type.
/// </summary>
[CreateAssetMenu(fileName = "GrenadierMobConfig", menuName = "Scriptable Objects/GrenadierMobConfig", order = 2)]
public class GrenadierMobConfig : MobConfig
{
	[Title("Grenadier")]
	[Tooltip("X: maximum distance from which the mob throws a grenade. Farther than that - it moves closer.")]
	[SerializeField, MinValue(0f)] private float _throwMaxDistance = 10f;

	[Tooltip("Y: minimum distance. If the player is closer - the mob backs off to open space.")]
	[SerializeField, MinValue(0f)] private float _throwMinDistance = 5f;

	[Tooltip("Cooldown between throws (sec). During it the mob stops (throw_cooldown animation).")]
	[SerializeField, MinValue(0f)] private float _throwCooldown = 3f;

	[Tooltip("Wind-up time of the throw animation before the grenade is released (sec).")]
	[SerializeField, MinValue(0f)] private float _throwWindup = 0.5f;

	[Required, Tooltip("Grenade type the mob throws.")]
	[SerializeField] private GrenadeConfig _grenadeConfig;

	public float ThrowMaxDistance => _throwMaxDistance;
	public float ThrowMinDistance => _throwMinDistance;
	public float ThrowCooldown => _throwCooldown;
	public float ThrowWindup => _throwWindup;
	public GrenadeConfig GrenadeConfig => _grenadeConfig;
}
