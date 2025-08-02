using UnityEngine;
using Sirenix.OdinInspector;

[CreateAssetMenu(fileName = "GunConfig", menuName = "Scriptable Objects/GunConfig")]
public class GunConfig : ScriptableObject
{
	[Title("Основное"), PropertyOrder(-10)]
	[GUIColor(0.8f, 0.9f, 1)]
	[HorizontalGroup("Top", Width = 90)]
	[PreviewField(70, ObjectFieldAlignment.Center), HideLabel]
	[SerializeField] private Sprite _preview;

	[VerticalGroup("Top/Right")]
	[LabelText("ID"), PropertyOrder(-9), Delayed]
	[SerializeField] private string _id;

	[VerticalGroup("Top/Right")]
	[LabelText("Название оружия"), PropertyOrder(-8)]
	[SerializeField][InlineButton(nameof(SetDefaultName), "По ID")] private string _gunName;

	[Space]
	[Title("Стрельба"), GUIColor(1, 0.95f, 0.7f)]
	[LabelText("Огневая скорость (сек)"), MinValue(0.05f), SuffixLabel("сек", true)]
	[SerializeField] private float _fireRate = 0.2f;

	[LabelText("Ёмкость магазина"), MinValue(1)]
	[SerializeField] private int _magazineCapacity = 12;

	[LabelText("ID звука выстрела")]
	[SerializeField] private string _fireSoundId;

	[Space]
	[Title("Параметры пули"), GUIColor(0.95f, 1, 0.95f)]
	[LabelText("Префаб пули")]
	[PreviewField(50), HideLabel]
	[SerializeField] private Bullet _bulletPrefab;

	[LabelText("Тип проверки попадания")]
	[SerializeField] private BulletCheckType _bulletCheckType;

	[LabelText("Скорость пули"), MinValue(0.1f)]
	[SerializeField] private float _bulletSpeed = 30f;

	[LabelText("Урон пули"), MinValue(0f)]
	[SerializeField] private float _bulletDamage = 10f;

	[LabelText("Время жизни пули"), MinValue(0.01f), SuffixLabel("сек", true)]
	[SerializeField] private float _bulletLifeTime = 2f;

	[LabelText("Радиус поражения"), MinValue(0f)]
	[SerializeField] private float _radius = 0.1f;

	[LabelText("Время перезарядки"), MinValue(0f)]
	[SerializeField] private float _reloadTime = 1f;

	public string Id => _id;
	public Sprite Preview => _preview;
	public float BulletSpeed => _bulletSpeed;
	public float BulletDamage => _bulletDamage;
	public float BulletLifeTime => _bulletLifeTime;
	public float FireCoolDown => _fireRate;
	public int MagazineCapacity => _magazineCapacity;
	public string FireSoundId => _fireSoundId;
	public Bullet BulletPrefab => _bulletPrefab;
	public float BulletRadius => _radius;
	public BulletCheckType BulletCheckType => _bulletCheckType;
	public float ReloadTime => _reloadTime;

#if UNITY_EDITOR
	private void SetDefaultName()
	{
		_gunName = _id;
	}
#endif
}
