using Sirenix.OdinInspector;
using UnityEngine;

// Per-caliber ammo config: the projectile prefab fired for this caliber and the icon shown on
// ammo loot. The gun (GunConfig) defines ballistics (damage/speed/spread); the ammo defines the
// projectile visual and the loot icon.
[CreateAssetMenu(fileName = "AmmoConfig", menuName = "Scriptable Objects/AmmoConfig")]
public class AmmoConfig : ScriptableObject
{
	[Title("Identity")]
	[PreviewField(60, ObjectFieldAlignment.Left), HideLabel, HorizontalGroup("Top", 70)]
	[Tooltip("Icon shown on ammo loot of this caliber.")]
	[SerializeField] private Sprite _lootIcon;

	[VerticalGroup("Top/Right"), LabelText("Caliber")]
	[SerializeField] private Caliber _caliber;

	[Title("Projectile")]
	[Tooltip("Projectile prefab spawned per shot for this caliber.")]
	[PreviewField(50), HideLabel]
	[SerializeField] private Bullet _projectilePrefab;

	public Caliber Caliber => _caliber;
	public Sprite LootIcon => _lootIcon;
	public Bullet ProjectilePrefab => _projectilePrefab;
}
