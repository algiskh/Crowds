using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Settings
{
	// Custom control settings serialized to JSON.
	// The format is Unity Input System binding overrides (SaveBindingOverridesAsJson):
	// a compact JSON that stores only the user-changed bindings.
	// The IControlSettingsStore abstraction allows moving this JSON anywhere later
	// (file, cloud save, backend) — just swap the implementation via ControlSettings.SetStore.
	public interface IControlSettingsStore
	{
		bool HasData { get; }
		string Load();
		void Save(string json);
		void Clear();
	}

	// Default implementation — PlayerPrefs.
	public class PlayerPrefsControlSettingsStore : IControlSettingsStore
	{
		public const string DefaultKey = "controls.bindings.v1";

		private readonly string _key;

		public PlayerPrefsControlSettingsStore(string key = DefaultKey) => _key = key;

		public bool HasData =>
			PlayerPrefs.HasKey(_key) && !string.IsNullOrEmpty(PlayerPrefs.GetString(_key));

		public string Load() => PlayerPrefs.GetString(_key, string.Empty);

		public void Save(string json)
		{
			PlayerPrefs.SetString(_key, json ?? string.Empty);
			PlayerPrefs.Save();
		}

		public void Clear()
		{
			PlayerPrefs.DeleteKey(_key);
			PlayerPrefs.Save();
		}
	}

	// Static facade: applies/saves/resets binding overrides on an InputActionAsset.
	// Called from EntryPoint (gameplay) and from the settings menu (menu scene), so custom
	// bindings are picked up in both scenes.
	public static class ControlSettings
	{
		private static IControlSettingsStore _store = new PlayerPrefsControlSettingsStore();

		public static IControlSettingsStore Store => _store;

		// Swap the store (e.g. to move the JSON to a file/cloud).
		public static void SetStore(IControlSettingsStore store)
		{
			if (store != null)
				_store = store;
		}

		// Load the saved overrides and apply them to the asset.
		public static void Apply(InputActionAsset actions)
		{
			if (actions == null || !_store.HasData)
				return;

			actions.LoadBindingOverridesFromJson(_store.Load());
		}

		// Save the asset's current overrides to the store.
		public static void Save(InputActionAsset actions)
		{
			if (actions == null)
				return;

			_store.Save(actions.SaveBindingOverridesAsJson());
		}

		// Reset all custom bindings (both on the asset and in the store).
		public static void ResetAll(InputActionAsset actions)
		{
			if (actions != null)
				actions.RemoveAllBindingOverrides();

			_store.Clear();
		}
	}
}
