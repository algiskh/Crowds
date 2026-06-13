using UnityEngine;

//[CreateAssetMenu(fileName = "PlayerConfig", menuName = "Scriptable Objects/PlayerConfig", order = 1)]
public class PlayerConfig : ScriptableObject
{
	[Header("Player Basic Settings")]
	[SerializeField] private float _basicSpeed = 5f;
    [SerializeField] private float _basicMaxHealth = 100f;

	public float Speed => _basicSpeed;
	public float MaxHealth => _basicMaxHealth;

	// Per-grenade stats (speed, damage, radius, effects) live in GrenadeConfig.
	// Here only player-level throw mechanics: starting count, distance range, charge time.
	[Header("Grenade — inventory")]
	[SerializeField] private int _startGrenades = 0;
	[Tooltip("Id грэнад-конфига для стартовых гранат (из GrenadeConfigHolder). Пусто = первый в холдере.")]
	[SerializeField] private string _startGrenadeId = "";

	[Header("Grenade — throw")]
	[Tooltip("Минимальная дальность броска (при мгновенном отпускании).")]
	[SerializeField] private float _minThrowDistance = 3f;
	[Tooltip("Максимальная дальность броска (при полном заряде).")]
	[SerializeField] private float _maxThrowDistance = 10f;
	[Tooltip("Время удержания Throw (сек) для набора полной дальности.")]
	[SerializeField] private float _maxThrowChargeTime = 1.2f;

	public int StartGrenades => _startGrenades;
	public string StartGrenadeId => _startGrenadeId;
	public float MinThrowDistance => _minThrowDistance;
	public float MaxThrowDistance => _maxThrowDistance;
	public float MaxThrowChargeTime => _maxThrowChargeTime;

}
