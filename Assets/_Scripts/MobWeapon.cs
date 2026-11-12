using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// A weapon held by a mob (e.g. the Gunner's rifle). Pure view data: the grip is the point that sits in the
/// hand bone, the muzzle is where shots and the muzzle-flash effect come from. Usually attached under the hand
/// bone by the model pipeline (MobModelSetupPreset.WeaponPrefab) and referenced from <see cref="Mob.Weapon"/>.
/// RangedAttackerSystem fires from <see cref="Muzzle"/> when present (fallback: RangedMobConfig muzzle offsets).
/// </summary>
public class MobWeapon : MonoBehaviour
{
	[BoxGroup("Points"), Tooltip("Where the hand holds the weapon (the weapon root is usually placed here).")]
	[SerializeField] private Transform _grip;

	[BoxGroup("Points"), Required, Tooltip("Tip of the barrel: bullet spawn point and muzzle-flash anchor. +Z = shot direction.")]
	[SerializeField] private Transform _muzzle;

	[BoxGroup("Effects"), Tooltip("EffectsHolder id spawned at the muzzle on every shot. Empty = no flash.")]
	[SerializeField] private string _muzzleEffectId = "muzzle_flash";

	public Transform Grip => _grip;
	public Transform Muzzle => _muzzle;
	public string MuzzleEffectId => _muzzleEffectId;
}
