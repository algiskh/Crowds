using ECS;
using Localization;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "MainHolder", menuName = "Scriptable Objects/MainHolder")]
public class MainHolder : ScriptableObject
{

    [SerializeField, BoxGroup("Configs")] private MobConfig _mobConfig;
	[SerializeField, BoxGroup("Configs")] private EffectsHolder _effectsHolder;
	[SerializeField, BoxGroup("Configs")] private DecalsConfigHolder _decalsConfigHolder;
	[SerializeField, BoxGroup("Configs")] private SpriteHolder _spriteHolder;
	[SerializeField, BoxGroup("Configs")] private MobConfigHolder _mobConfigHolder;
	[SerializeField, BoxGroup("Configs")] private GrenadeConfigHolder _grenadeConfigHolder;
	[SerializeField, BoxGroup("Configs")] private BonusConfigHolder _bonusConfigHolder;
	[SerializeField, BoxGroup("Configs")] private AmmoConfigHolder _ammoConfigHolder;
	[SerializeField, BoxGroup("Configs")] private LocalizationHolder _localizationHolder;

	[SerializeField,BoxGroup("Prefabs")] private Mob _prefab;
	[SerializeField, BoxGroup("Prefabs")] private Loot _lootPrefab;
	[SerializeField, BoxGroup("Prefabs")] private Bullet _bulletPrefab;
	[SerializeField, BoxGroup("Prefabs")] private Grenade _grenadePrefab;

	[SerializeField, BoxGroup("TimerPresets")] private float _spawnCooldown = 5f;
	[SerializeField, BoxGroup("TimerPresets")] private float _pathRecalculationInterval = 0.5f;
	[SerializeField, BoxGroup("TimerPresets")] private float _utilizationTimer = 5f;
	[SerializeField, BoxGroup("TimerPresets")] private float _cameraSpeed = 3f;

	[SerializeField, BoxGroup("SpawnPresets")] private float _minSpawnCoolDown = 0.75f;
	[SerializeField, BoxGroup("SpawnPresets")] private float _maxSpawnCoolDown = 5f;

	[SerializeField, BoxGroup("GameplayParameters")] private float _lootRadius = 0.5f;
	[SerializeField, BoxGroup("GameplayParameters")] private float _sectorUpdateOffset = 5f;
	[SerializeField, BoxGroup("GameplayParameters")] private int _startAmmo = 10;

	[Tooltip("Despawn time (sec) for loot dropped by mobs, per loot type. <= 0 means the loot never despawns. Types not listed fall back to Default.")]
	[SerializeField, BoxGroup("LootLifetime")] private float _defaultMobLootLifetime = 10f;
	[SerializeField, BoxGroup("LootLifetime")] private LootLifetimeEntry[] _mobLootLifetimes;

	[Tooltip("Seconds before despawn when mob loot starts pulsing toward the warning color. <= 0 disables the warning.")]
	[SerializeField, BoxGroup("LootLifetime")] private float _lootDespawnWarningTime = 3f;
	[Tooltip("Pulse speed of the despawn warning tint (radians/sec of the sine).")]
	[SerializeField, BoxGroup("LootLifetime")] private float _lootDespawnWarningPulseSpeed = 8f;
	[SerializeField, BoxGroup("LootLifetime")] private Color _lootDespawnWarningColor = Color.red;

	[SerializeField, BoxGroup("DifficultyParameters")] private float _difficultyIncreaseTime = 60f;
	[SerializeField, BoxGroup("DifficultyParameters")] private int _activeMobLimit = 60;

	[SerializeField, BoxGroup("DifficultyParameters"), MinValue(0),
	 Tooltip("Сколько неактивных клонов каждого типа мобов уровня заранее создать в пуле при старте, " +
		"чтобы убрать хитчи от Instantiate во время волн. 0 = выключено (ленивое создание). " +
		"На тип не превышает ActiveMobLimit.")]
	private int _mobPrewarmPerType = 10;

	[Header("Camera presets")]
	[SerializeField] private FollowTarget FollowTarget;

	[SerializeField, BoxGroup("Player")] private PlayerConfig _playerConfig;
	[SerializeField, BoxGroup("Player")] private GunConfigHolder _gunConfigHolder;


	[SerializeField] private SoundHolder _soundHolder;

	[SerializeField] private float _defaultCollisionRadius = 0.5f;

	[SerializeField, BoxGroup("GameplayParameters")] private LayerMask _mobLayerMask = ~0;
	public LayerMask MobLayerMask => _mobLayerMask;

	public MobConfig MobConfig => _mobConfig;
	public Mob Prefab => _prefab;
	public Loot LootPrefab => _lootPrefab;
	public Bullet BulletPrefab => _bulletPrefab;
	public Grenade GrenadePrefab => _grenadePrefab;
	public float SpawnCooldown => _spawnCooldown;
	public float PathRecalculationInterval => _pathRecalculationInterval;
	public float UtilizationTimer => _utilizationTimer;
	public float CameraSpeed => _cameraSpeed;
	public FollowTarget CameraFollowTarget => FollowTarget;
	public PlayerConfig PlayerConfig => _playerConfig;
	public DecalsConfigHolder DecalsConfigHolder => _decalsConfigHolder;
	public MobConfig GetConfig(string id) => _mobConfig;
	public GunConfigHolder GunConfigHolder => _gunConfigHolder;
	public EffectsHolder EffectsHolder => _effectsHolder;
	public SoundHolder SoundHolder => _soundHolder;
	public SpriteHolder SpriteHolder => _spriteHolder;
	public float DefaultCollisionRadius => _defaultCollisionRadius;
	public float MinSpawnCoolDown => _minSpawnCoolDown;
	public float MaxSpawnCoolDown => _maxSpawnCoolDown;
	public float LootRadius => _lootRadius;
	public float SectorUpdateOffset => _sectorUpdateOffset;
	public int StartAmmo => _startAmmo;
	public float DifficultyIncreaseTime => _difficultyIncreaseTime;
	public int ActiveMobLimit => _activeMobLimit;
	public int MobPrewarmPerType => _mobPrewarmPerType;
	public MobConfigHolder MobConfigHolder => _mobConfigHolder;
	public GrenadeConfigHolder GrenadeConfigHolder => _grenadeConfigHolder;
	public BonusConfigHolder BonusConfigHolder => _bonusConfigHolder;
	public AmmoConfigHolder AmmoConfigHolder => _ammoConfigHolder;
	public LocalizationHolder Localization => _localizationHolder;

	/// <summary>
	/// Despawn time (seconds) for mob-dropped loot of the given type. Returns the per-type
	/// override if configured, otherwise the default. A value &lt;= 0 means "never despawn".
	/// </summary>
	public float GetMobLootLifetime(LootType type)
	{
		if (_mobLootLifetimes != null)
		{
			for (int i = 0; i < _mobLootLifetimes.Length; i++)
			{
				if (_mobLootLifetimes[i].LootType == type)
					return _mobLootLifetimes[i].Lifetime;
			}
		}
		return _defaultMobLootLifetime;
	}

	public float LootDespawnWarningTime => _lootDespawnWarningTime;
	public float LootDespawnWarningPulseSpeed => _lootDespawnWarningPulseSpeed;
	public Color LootDespawnWarningColor => _lootDespawnWarningColor;
}

[System.Serializable]
public class LootLifetimeEntry
{
	public LootType LootType;
	[Min(0f)] public float Lifetime = 10f;
}
