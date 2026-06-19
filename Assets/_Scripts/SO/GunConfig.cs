using UnityEngine;
using Sirenix.OdinInspector;

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

	[Space]
	[Title("Firing"), GUIColor(1, 0.95f, 0.7f)]
	[LabelText("Fire rate (sec)"), MinValue(0.05f), SuffixLabel("sec", true)]
	[SerializeField] private float _fireRate = 0.2f;

	[LabelText("Magazine capacity"), MinValue(1)]
	[SerializeField] private int _magazineCapacity = 12;

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
	[SerializeField] private Modifier[] _shotDebuffs;

	[LabelText("On-reload debuffs")]
	[SerializeField] private Modifier[] _reloadDebuffs;

	public string Id => _id;
	public Caliber Caliber => _caliber;
	public Sprite Preview => _preview;
	public float BulletSpeed => _bulletSpeed;
	public float BulletDamage => _bulletDamage;
	public float BulletLifeTime => _bulletLifeTime;
	public float FireCoolDown => _fireRate;
	public int MagazineCapacity => _magazineCapacity;
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
