using UnityEngine;
using Sirenix.OdinInspector;
using Sirenix.Serialization;

[CreateAssetMenu(fileName = "GunConfig", menuName = "Scriptable Objects/GunConfig")]
public class GunConfig : ScriptableObject
{
	[Title("Weapon"), PropertyOrder(-10)]
	[GUIColor(0.8f, 0.9f, 1)]
	[HorizontalGroup("Top", Width = 90)]
	[PreviewField(70, ObjectFieldAlignment.Center), HideLabel]
	[SerializeField] private Sprite _preview;

	[VerticalGroup("Top/Right")]
	[LabelText("ID"), PropertyOrder(-9), Delayed]
	[SerializeField] private string _id;

	[VerticalGroup("Top/Right")]
	[LabelText("Caliber"), PropertyOrder(-8),
	 Tooltip("Ammo type. Weapons with the same caliber share one ammo pool " +
	         "(e.g. rifle and assault rifle = 7.62mm).")]
	[SerializeField] private Caliber _caliber;

	[VerticalGroup("Top/Right")]
	[LabelText("Animation set"), PropertyOrder(-7),
	 Tooltip("Player animation set for this weapon: Pistol (one-handed) or Rifle (every long gun).")]
	[SerializeField] private WeaponAnimationSet _animationSet = WeaponAnimationSet.Rifle;

	[VerticalGroup("Top/Right")]
	[LabelText("Model"), PropertyOrder(-6),
	 Tooltip("Visual gun model held by the player body (PlayerAnimator puts it into the hand bone). " +
	         "Its external magazine is hidden while reloading. Visual only. Optional.")]
	[SerializeField] private WeaponModel _model;

	[Space]
	[Title("Firing"), GUIColor(1, 0.95f, 0.7f)]
	[LabelText("Fire rate (sec)"), MinValue(0.05f), SuffixLabel("sec", true)]
	[SerializeField] private float _fireRate = 0.2f;

	[LabelText("Magazine capacity"), MinValue(1)]
	[SerializeField] private int _magazineCapacity = 12;

	[LabelText("Spin-up time"), MinValue(0f), SuffixLabel("sec", true),
	 Tooltip("The trigger must be held this long before the first shot (e.g. machine-gun barrel spin-up). " +
	         "Releasing the trigger resets it. 0 = fires immediately.")]
	[SerializeField] private float _spinUpTime;

	[LabelText("Fire sound ID")]
	[SerializeField] private string _fireSoundId;

	[LabelText("Reload sound ID")]
	[SerializeField] private string _reloadSoundId;

	[LabelText("Reload-end sound ID")]
	[SerializeField] private string _reloadEndSoundId;

	[Space]
	[Title("Bullet"), GUIColor(0.95f, 1, 0.95f)]
	// Projectile prefab lives on AmmoConfig (per caliber), resolved in BulletSystem.
	[LabelText("Bullet check type")]
	[SerializeField] private BulletCheckType _bulletCheckType;

	[LabelText("Projectiles per shot"), MinValue(1)]
	[SerializeField] private int _projectilesNumber = 1;

	[LabelText("Bullet speed"), MinValue(0.1f)]
	[SerializeField] private float _bulletSpeed = 30f;

	[LabelText("Bullet damage"), MinValue(0f)]
	[SerializeField] private float _bulletDamage = 10f;

	[LabelText("Bullet lifetime"), MinValue(0.01f), SuffixLabel("sec", true)]
	[SerializeField] private float _bulletLifeTime = 2f;

	[LabelText("Bullet radius"), MinValue(0f)]
	[SerializeField] private float _radius = 0.1f;

	[LabelText("Pierce count"), MinValue(0), MaxValue(MaxPierceCount),
	 Tooltip("How many mobs one bullet can hit. 0 = use the projectile prefab's Bullet.MaxPierceCount. " +
	         "Capped by BulletComponent.PiercedTargets capacity.")]
	[SerializeField] private int _pierceCount;

	[LabelText("Pierce blood trail"),
	 Tooltip("A target the bullet passes through (mob, or the player for enemy bullets) leaves a trail of blood " +
	         "decals behind it along the shot. The target the bullet stops in gets the regular hit decal.")]
	[SerializeField] private bool _pierceBloodTrail;

	[LabelText("Trail decals (min/max)"), ShowIf(nameof(_pierceBloodTrail)), MinValue(1)]
	[SerializeField] private Vector2Int _pierceTrailDecals = new Vector2Int(3, 4);

	[LabelText("Trail spacing"), ShowIf(nameof(_pierceBloodTrail)), MinValue(0.1f), SuffixLabel("m", true)]
	[SerializeField] private float _pierceTrailSpacing = 0.55f;

	[LabelText("Bullet color"),
	 Tooltip("Tint of the projectile mesh and trail (e.g. red enemy bullets). " +
	         "Alpha 0 = keep the prefab's own look.")]
	[SerializeField] private Color _bulletColor = new Color(1f, 1f, 1f, 0f);

	[LabelText("Reload time"), MinValue(0f)]
	[SerializeField] private float _reloadTime = 1f;

	[LabelText("Shutter time"), MinValue(0f)]
	[SerializeField] private float _shutterTime = 0.1f;

	[LabelText("Speed modifier"), MinValue(0.1f)]
	[SerializeField] private float _speedModifier = 1f;

	[LabelText("Accuracy"), MinValue(0.1f), MaxValue(1.0f)]
	[SerializeField] private float _accuracy = 0.9f;

	[LabelText("Single load (per-round reload)")]
	[SerializeField] private bool _singleLoad;

	[LabelText("Fire on demand")]
	[SerializeField] private bool _fireOnDemand;

	[LabelText("On-shot debuffs")]
	[SerializeReference, OdinSerialize] private Modifier[] _shotDebuffs;

	[LabelText("On-reload debuffs")]
	[SerializeReference, OdinSerialize] private Modifier[] _reloadDebuffs;

	// FixedList32Bytes<int> holds 7 ids; the last hit isn't stored, so up to 8 targets.
	public const int MaxPierceCount = 8;

	public string Id => _id;
	public Caliber Caliber => _caliber;
	public WeaponAnimationSet AnimationSet => _animationSet;
	public WeaponModel Model => _model;
	public Sprite Preview => _preview;
	public float BulletSpeed => _bulletSpeed;
	public float BulletDamage => _bulletDamage;
	public float BulletLifeTime => _bulletLifeTime;
	public float FireCoolDown => _fireRate;
	public int MagazineCapacity => _magazineCapacity;
	public float SpinUpTime => _spinUpTime;
	public int PierceCount => _pierceCount;
	public bool PierceBloodTrail => _pierceBloodTrail;
	public Vector2Int PierceTrailDecals => _pierceTrailDecals;
	public float PierceTrailSpacing => _pierceTrailSpacing;
	public Color BulletColor => _bulletColor;
	public string FireSoundId => _fireSoundId;
	public string ReloadSoundId => _reloadSoundId;
	public string ReloadEndSoundId => _reloadEndSoundId;
	public float BulletRadius => _radius;
	public BulletCheckType BulletCheckType => _bulletCheckType;
	public float ReloadTime => _reloadTime;
	public float ShutterTime => _shutterTime;
	public float SpeedModifier => _speedModifier;
	public float Accuracy => _accuracy;
	public int ProjectilesNumber => _projectilesNumber;
	public bool SingleLoad => _singleLoad;
	public bool FireOnDemand => _fireOnDemand;
	public Modifier[] ShotDebuffs => _shotDebuffs;
	public Modifier[] ReloadDebuffs => _reloadDebuffs;
}
