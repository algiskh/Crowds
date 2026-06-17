using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Конфиг отдельного типа гранаты: скорость полёта, урон, радиус и эффекты.
/// Лут-граната ссылается на конфиг по Id (LootComponent.Id / PossibleLoot.Id),
/// бросок берёт все параметры отсюда.
/// </summary>
[CreateAssetMenu(fileName = "GrenadeConfig", menuName = "Scriptable Objects/GrenadeConfig")]
public class GrenadeConfig : ScriptableObject
{
	[Title("Identity")]
	[PreviewField(60, ObjectFieldAlignment.Left), HideLabel, HorizontalGroup("Top", 70)]
	[SerializeField] private Sprite _preview;

	[VerticalGroup("Top/Right"), LabelText("ID"), Delayed]
	[SerializeField] private string _id;

	[Title("Flight")]
	[Tooltip("Горизонтальная скорость полёта (ед/сек). Время полёта = дистанция / скорость.")]
	[SerializeField, MinValue(0.1f)] private float _throwSpeed = 12f;
	[Tooltip("Высота дуги в наивысшей точке (ед).")]
	[SerializeField, MinValue(0f)] private float _arcHeight = 2.5f;
	[Tooltip("Задержка-фитиль перед взрывом после приземления (сек).")]
	[SerializeField, MinValue(0f)] private float _fuseDelay = 0f;

	[Title("Explosion")]
	[SerializeField, MinValue(0f)] private float _radius = 3.5f;
	[Tooltip("Урон в эпицентре.")]
	[SerializeField, MinValue(0f)] private float _maxDamage = 120f;
	[Tooltip("Урон на краю радиуса.")]
	[SerializeField, MinValue(0f)] private float _minDamage = 30f;
	[Tooltip("Id эффекта взрыва в EffectsHolder.")]
	[SerializeField] private string _explosionEffectId = "explosion";

	[Title("Damage targets")]
	[Tooltip("Доля урона по мобам (0 = не задевает мобов, 1 = полный урон). Взрыв бьёт всех в радиусе.")]
	[SerializeField, Range(0f, 1f)] private float _mobDamageScale = 1f;
	[Tooltip("Доля урона по игроку (0 = не задевает игрока, 1 = полный урон). Взрыв бьёт всех в радиусе.")]
	[SerializeField, Range(0f, 1f)] private float _playerDamageScale = 1f;

	[Title("Trail / escort effect")]
	[Tooltip("Id эффекта из EffectsHolder, который при броске становится ребёнком гранаты " +
	         "и сопровождает её в полёте; при взрыве возвращается в пул. Пусто = без эффекта.")]
	[SerializeField] private string _trailEffectId = "";

	public string Id => _id;
	public Sprite Preview => _preview;
	public float ThrowSpeed => _throwSpeed;
	public float ArcHeight => _arcHeight;
	public float FuseDelay => _fuseDelay;
	public float Radius => _radius;
	public float MaxDamage => _maxDamage;
	public float MinDamage => _minDamage;
	public float MobDamageScale => _mobDamageScale;
	public float PlayerDamageScale => _playerDamageScale;
	public string ExplosionEffectId => _explosionEffectId;
	public string TrailEffectId => _trailEffectId;
}
