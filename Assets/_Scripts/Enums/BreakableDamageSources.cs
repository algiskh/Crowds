using System;

/// <summary>
/// Which damage sources are allowed to hurt a breakable environment object.
/// Configured per <see cref="BreakableConfig"/> and checked at each damage site
/// (bullets, explosions, melee, mob contact) before a RequestDamageComponent is raised.
/// </summary>
[Flags]
public enum BreakableDamageSources
{
	None = 0,
	Bullet = 1,
	Explosion = 2,
	Melee = 4,
	MobContact = 8
}
