using UnityEngine;

//[CreateAssetMenu(fileName = "PlayerConfig", menuName = "Scriptable Objects/PlayerConfig", order = 1)]
public class PlayerConfig : ScriptableObject
{
	[Header("Player Basic Settings")]
	[SerializeField] private float _basicSpeed = 5f;
    [SerializeField] private float _basicMaxHealth = 100f;

	public float Speed => _basicSpeed;
	public float MaxHealth => _basicMaxHealth;

	[Header("Grenade — inventory")]
	[SerializeField] private int _startGrenades = 0;

	[Header("Grenade — throw")]
	[Tooltip("Минимальная дальность броска (при мгновенном отпускании).")]
	[SerializeField] private float _minThrowDistance = 3f;
	[Tooltip("Максимальная дальность броска (при полном заряде).")]
	[SerializeField] private float _maxThrowDistance = 10f;
	[Tooltip("Время удержания Throw (сек) для набора полной дальности.")]
	[SerializeField] private float _maxThrowChargeTime = 1.2f;
	[Tooltip("Задержка-фитиль перед взрывом после приземления (сек).")]
	[SerializeField] private float _grenadeFuseDelay = 0.4f;
	[Tooltip("Горизонтальная скорость полёта гранаты (ед/сек). Время полёта = дистанция / скорость.")]
	[SerializeField] private float _grenadeThrowSpeed = 12f;
	[Tooltip("Высота дуги полёта гранаты в наивысшей точке (ед).")]
	[SerializeField] private float _grenadeArcHeight = 2.5f;

	[Header("Grenade — explosion")]
	[SerializeField] private float _explosionRadius = 3.5f;
	[Tooltip("Урон в эпицентре.")]
	[SerializeField] private float _explosionMaxDamage = 120f;
	[Tooltip("Урон на краю радиуса.")]
	[SerializeField] private float _explosionMinDamage = 30f;
	[Tooltip("Id эффекта взрыва в EffectsHolder.")]
	[SerializeField] private string _explosionEffectId = "explosion";

	public int StartGrenades => _startGrenades;
	public float MinThrowDistance => _minThrowDistance;
	public float MaxThrowDistance => _maxThrowDistance;
	public float MaxThrowChargeTime => _maxThrowChargeTime;
	public float GrenadeFuseDelay => _grenadeFuseDelay;
	public float GrenadeThrowSpeed => _grenadeThrowSpeed;
	public float GrenadeArcHeight => _grenadeArcHeight;
	public float ExplosionRadius => _explosionRadius;
	public float ExplosionMaxDamage => _explosionMaxDamage;
	public float ExplosionMinDamage => _explosionMinDamage;
	public string ExplosionEffectId => _explosionEffectId;

}
