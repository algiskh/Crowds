using Localization;

/// <summary>
/// Builds the human-readable log line for a picked-up loot item (e.g. "Picked up 9mm Ammo (10)",
/// "Obtained Shield bonus"). Item names are resolved through the <see cref="LocalizationHolder"/> by
/// id with a safe fallback to the raw id, so a missing localization key never spams the console.
/// </summary>
public static class LootLogFormatter
{
	public static string Format(in LootComponent loot, in WeaponComponent muzzle, MainHolder holder)
	{
		switch (loot.LootType)
		{
			case LootType.Ammo:
			{
				var caliber = loot.AmmoCaliber != Caliber.None
					? loot.AmmoCaliber
					: (muzzle.GunConfig != null ? muzzle.GunConfig.Caliber : Caliber.None);
				return $"Picked up {caliber.ToDisplay()} Ammo ({loot.Count})";
			}

			case LootType.Weapon:
				return $"Picked up {Loc(holder, loot.Id, loot.Id)}";

			case LootType.Grenade:
			{
				var key = string.IsNullOrEmpty(loot.Id) ? "grenade" : loot.Id;
				return $"Picked up {Loc(holder, key, "Grenade")}";
			}

			case LootType.Bonus:
				return $"Obtained {ResolveBonusName(holder, loot.Id)} bonus";

			case LootType.Health:
				return $"Picked up Health (+{loot.Count})";

			default:
				return $"Picked up {loot.LootType}";
		}
	}

	private static string ResolveBonusName(MainHolder holder, string id)
	{
		if (!string.IsNullOrEmpty(id))
			return Loc(holder, id, id);

		// Default bonus (empty id): use the holder's default config silently (no error-logging lookup).
		var def = holder != null && holder.BonusConfigHolder != null ? holder.BonusConfigHolder.Default : null;
		return def != null ? Loc(holder, def.Id, def.Type.ToString()) : "Bonus";
	}

	/// <summary>Localized text for <paramref name="key"/>, or <paramref name="fallback"/> when absent.</summary>
	private static string Loc(MainHolder holder, string key, string fallback)
	{
		var localization = holder != null ? holder.Localization : null;
		if (localization != null && localization.TryGetKey(key, localization.DefaultLanguage, out var value))
			return value;
		return fallback;
	}
}
