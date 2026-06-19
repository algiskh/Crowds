using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Config of a single grenade type: flight speed, damage, radius and effects.
/// A grenade loot references the config by Id (LootComponent.Id / PossibleLoot.Id);
/// the throw reads all its parameters from here.
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
	[Tooltip("Horizontal flight speed (units/sec). Flight time = distance / speed.")]
	[SerializeField, MinValue(0.1f)] private float _throwSpeed = 12f;
	[Tooltip("Arc height at its peak (units).")]
	[SerializeField, MinValue(0f)] private float _arcHeight = 2.5f;
	[Tooltip("Fuse delay before exploding after landing (sec).")]
	[SerializeField, MinValue(0f)] private float _fuseDelay = 0f;

	[Title("Explosion")]
	[SerializeField, MinValue(0f)] private float _radius = 3.5f;
	[Tooltip("Damage at the epicenter.")]
	[SerializeField, MinValue(0f)] private float _maxDamage = 120f;
	[Tooltip("Damage at the edge of the radius.")]
	[SerializeField, MinValue(0f)] private float _minDamage = 30f;
	[Tooltip("Explosion effect id in EffectsHolder.")]
	[SerializeField] private string _explosionEffectId = "explosion";

	[Title("Damage targets")]
	[Tooltip("Damage fraction against mobs (0 = doesn't hit mobs, 1 = full damage). The blast hits everyone in radius.")]
	[SerializeField, Range(0f, 1f)] private float _mobDamageScale = 1f;
	[Tooltip("Damage fraction against the player (0 = doesn't hit the player, 1 = full damage). The blast hits everyone in radius.")]
	[SerializeField, Range(0f, 1f)] private float _playerDamageScale = 1f;

	[Title("Trail / escort effect")]
	[Tooltip("Id of an effect from EffectsHolder that becomes a child of the grenade on throw " +
	         "and follows it in flight; on explosion it returns to the pool. Empty = no effect.")]
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
