using ECS;
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

	[SerializeField, BoxGroup("DifficultyParameters")] private float _difficultyIncreaseTime = 60f;
	[SerializeField, BoxGroup("DifficultyParameters")] private int _activeMobLimit = 60;

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
	public MobConfigHolder MobConfigHolder => _mobConfigHolder;
	public GrenadeConfigHolder GrenadeConfigHolder => _grenadeConfigHolder;
}
