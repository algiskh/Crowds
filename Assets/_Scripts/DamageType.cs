public enum DamageType
{
	Unknown = 0,
	Physical = 1,
	Fire = 2,
	Poison = 3,
	Magic = 4
}

/// <summary>
/// Источник урона — определяет, какой пул декалей применить к мобу при попадании.
/// Настраивается на каждом мобе в <see cref="MobConfig"/>.
/// </summary>
public enum DamageSourceType
{
	Bullet = 0,
	Melee = 1,
	Explosion = 2
}