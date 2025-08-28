using UnityEngine;
using System.Security.Cryptography;
using System.Text;

public static class DeviceIds
{
	public static string GetDeviceId()
	{
		// 1) ѕытаемс€ вз€ть Unity ID
		var id = SystemInfo.deviceUniqueIdentifier;

		// 2) ≈сли мало ли пусто/нестабильно Ч храним свой GUID
		if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier)
		{
			if (!PlayerPrefs.HasKey("lb_device_guid"))
				PlayerPrefs.SetString("lb_device_guid", System.Guid.NewGuid().ToString("N"));
			id = PlayerPrefs.GetString("lb_device_guid");
		}
		return id;
	}

	public static string MakeClientEntryId(string deviceId, string name, string bucket = "global")
	{
		// компактный и стабильный ключ: SHA256(deviceId|name|bucket) -> hex(16)
		using var sha = SHA256.Create();
		var raw = $"{deviceId}|{name}|{bucket}";
		var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
		var sb = new StringBuilder(32);
		for (int i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2"));
		return sb.ToString();
	}
}
