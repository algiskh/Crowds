using ECS;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "MainHolder", menuName = "Scriptable Objects/MainHolder")]
public class MainHolder : ScriptableObject
{

    [SerializeField, BoxGroup("Configs")] private MobConfig _mobConfig;
	[SerializeField, BoxGroup("Configs")] private EffectsHolder _effectsHolder;
	[SerializeField, BoxGroup("Configs")] private DecalsConfigHolder _decalsConfigHolder;

	[SerializeField,BoxGroup("Prefabs")] private Mob _prefab;
	[SerializeField, BoxGroup("Prefabs")] private Loot _lootPrefab;
	[SerializeField, BoxGroup("Prefabs")] private Bullet _bulletPrefab;

	[SerializeField, BoxGroup("TimerPresets")] private float _spawnCooldown = 5f;
	[SerializeField, BoxGroup("TimerPresets")] private float _pathRecalculationInterval = 0.5f;
	[SerializeField, BoxGroup("TimerPresets")] private float _utilizationTimer = 5f;
	[SerializeField, BoxGroup("TimerPresets")] private float _cameraSpeed = 3f;

	[SerializeField, BoxGroup("SpawnPresets")] private float _minSpawnCoolDown = 1f;
	[SerializeField, BoxGroup("SpawnPresets")] private float _maxSpawnCoolDown = 10f;

	[Header("Camera presets")]
	[SerializeField] private FollowTarget FollowTarget;

	[SerializeField, BoxGroup("Player")] private PlayerConfig _playerConfig;
	[SerializeField, BoxGroup("Player")] private GunConfig _gunConfig;


	[SerializeField] private SoundHolder _soundHolder;

	[SerializeField] private float _defaultCollisionRadius = 0.5f;

	public MobConfig MobConfig => _mobConfig;
	public Mob Prefab => _prefab;
	public Loot LootPrefab => _lootPrefab;
	public Bullet BulletPrefab => _bulletPrefab;
	public float SpawnCooldown => _spawnCooldown;
	public float PathRecalculationInterval => _pathRecalculationInterval;
	public float UtilizationTimer => _utilizationTimer;
	public float CameraSpeed => _cameraSpeed;
	public FollowTarget CameraFollowTarget => FollowTarget;
	public PlayerConfig PlayerConfig => _playerConfig;
	public DecalsConfigHolder DecalsConfigHolder => _decalsConfigHolder;
	public MobConfig GetConfig(string id) => _mobConfig;
	public GunConfig GunConfig => _gunConfig;
	public EffectsHolder EffectsHolder => _effectsHolder;
	public SoundHolder SoundHolder => _soundHolder;
	public float DefaultCollisionRadius => _defaultCollisionRadius;
	public float MinSpawnCoolDown => _minSpawnCoolDown;
	public float MaxSpawnCoolDown => _maxSpawnCoolDown;
}
