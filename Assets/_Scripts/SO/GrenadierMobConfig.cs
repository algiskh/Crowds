using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Конфиг моба-гренадёра. Во всех «обычных» параметрах (здоровье, скорость, урон в ближнем
/// бою, лут, модификаторы) идентичен MobConfig — наследуется от него. Добавляет только
/// поведение броска гранат: дистанции X/Y, кулдаун между бросками и тип гранаты.
/// </summary>
[CreateAssetMenu(fileName = "GrenadierMobConfig", menuName = "Scriptable Objects/GrenadierMobConfig", order = 2)]
public class GrenadierMobConfig : MobConfig
{
	[Title("Grenadier")]
	[Tooltip("X: максимальная дистанция, с которой моб кидает гранату. Дальше — подходит ближе.")]
	[SerializeField, MinValue(0f)] private float _throwMaxDistance = 10f;

	[Tooltip("Y: минимальная дистанция. Если игрок ближе — моб отходит на свободное место.")]
	[SerializeField, MinValue(0f)] private float _throwMinDistance = 5f;

	[Tooltip("Кулдаун между бросками (сек). На это время моб останавливается (анимация throw_cooldown).")]
	[SerializeField, MinValue(0f)] private float _throwCooldown = 3f;

	[Tooltip("Время замаха анимации throw до момента вылета гранаты (сек).")]
	[SerializeField, MinValue(0f)] private float _throwWindup = 0.5f;

	[Required, Tooltip("Тип гранаты, которую бросает моб.")]
	[SerializeField] private GrenadeConfig _grenadeConfig;

	public float ThrowMaxDistance => _throwMaxDistance;
	public float ThrowMinDistance => _throwMinDistance;
	public float ThrowCooldown => _throwCooldown;
	public float ThrowWindup => _throwWindup;
	public GrenadeConfig GrenadeConfig => _grenadeConfig;
}
