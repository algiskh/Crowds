using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Ammo calibers. Weapons with the same caliber share one ammo pool.
// None = "unset": on a weapon it's a config error; on ammo loot it means "ammo for the current weapon".
// To add a caliber: add a member here (the [InspectorName] is what's shown in dropdowns and UI).
public enum Caliber
{
	None = 0,
	[InspectorName("9mm")] Mm9,
	[InspectorName("5.56mm")] Cal556,
	[InspectorName("7.62mm")] Cal762,
	[InspectorName("12gauge")] Gauge12,
	[InspectorName(".45 ACP")] Acp45,
	[InspectorName(".50 cal")] Cal50,
}

public static class CaliberExtensions
{
	private static readonly Dictionary<Caliber, string> _displayCache = new();

	// Human-readable name (the [InspectorName] value), for UI. Falls back to the enum name.
	public static string ToDisplay(this Caliber caliber)
	{
		if (_displayCache.TryGetValue(caliber, out var cached))
			return cached;

		var member = typeof(Caliber).GetMember(caliber.ToString());
		var attr = member.Length > 0 ? member[0].GetCustomAttribute<InspectorNameAttribute>() : null;
		var display = attr != null ? attr.displayName : caliber.ToString();

		_displayCache[caliber] = display;
		return display;
	}
}
