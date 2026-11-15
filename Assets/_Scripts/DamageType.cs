public enum DamageType
{
	Unknown = 0,
	Physical = 1,
	Fire = 2,
	Poison = 3,
	Magic = 4
}

/// <summary>
/// Damage source — determines which decal pool to apply to a mob on hit.
/// Configured per mob in <see cref="MobConfig"/>.
/// </summary>
public enum DamageSourceType
{
	Bullet = 0,
	Melee = 1,
	Explosion = 2
}
