/// <summary>
/// A weapon held by a mob (e.g. the Gunner's Mini-14). All data lives in <see cref="WeaponModel"/>
/// (grip, muzzle, left-hand target, magazine, muzzle effect); this subclass only marks the weapon as the mob's
/// and keeps old prefab references (same script guid, same serialized field names).
/// Usually attached under the hand bone by the model pipeline (MobModelSetupPreset.WeaponPrefab) and referenced
/// from <see cref="Mob.Weapon"/>. RangedAttackerSystem fires from <see cref="WeaponModel.Muzzle"/> when present
/// (fallback: RangedMobConfig muzzle offsets). <see cref="WeaponHandIK"/> puts the left hand on its forend.
/// </summary>
public class MobWeapon : WeaponModel
{
}
