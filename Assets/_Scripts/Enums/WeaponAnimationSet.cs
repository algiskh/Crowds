/// <summary>
/// Which set of player animations a weapon uses (Animator int parameter "WeaponSet").
/// Rifle = default for every long gun (AR, SMG, shotgun, machine gun...), Pistol = one-handed set.
/// Add a member + a matching branch in PlayerAnimatorControllerBuilder to introduce a new set.
/// </summary>
public enum WeaponAnimationSet
{
	Rifle = 0,
	Pistol = 1,
}
