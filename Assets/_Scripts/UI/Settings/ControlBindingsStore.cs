using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Settings
{
	// Кастомные настройки управления, сериализуемые в JSON.
	// Формат — биндинг-оверрайды Unity Input System (SaveBindingOverridesAsJson):
	// компактный JSON, который хранит только изменённые пользователем биндинги.
	// Абстракция IControlSettingsStore позволяет позже перенести этот JSON куда угодно
	// (файл, облачный сейв, бэкенд) — достаточно подменить реализацию через ControlSettings.SetStore.
	public interface IControlSettingsStore
	{
		bool HasData { get; }
		string Load();
		void Save(string json);
		void Clear();
	}

	// Реализация по умолчанию — PlayerPrefs.
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

	// Статический фасад: применяет/сохраняет/сбрасывает оверрайды биндингов на InputActionAsset.
	// Вызывается из EntryPoint (геймплей) и из меню настроек (сцена меню), чтобы кастомные
	// биндинги подхватывались в обеих сценах.
	public static class ControlSettings
	{
		private static IControlSettingsStore _store = new PlayerPrefsControlSettingsStore();

		public static IControlSettingsStore Store => _store;

		// Подменить хранилище (например, чтобы перенести JSON в файл/облако).
		public static void SetStore(IControlSettingsStore store)
		{
			if (store != null)
				_store = store;
		}

		// Загрузить сохранённые оверрайды и наложить их на ассет.
		public static void Apply(InputActionAsset actions)
		{
			if (actions == null || !_store.HasData)
				return;

			actions.LoadBindingOverridesFromJson(_store.Load());
		}

		// Сохранить текущие оверрайды ассета в хранилище.
		public static void Save(InputActionAsset actions)
		{
			if (actions == null)
				return;

			_store.Save(actions.SaveBindingOverridesAsJson());
		}

		// Сбросить все кастомные биндинги (и на ассете, и в хранилище).
		public static void ResetAll(InputActionAsset actions)
		{
			if (actions != null)
				actions.RemoveAllBindingOverrides();

			_store.Clear();
		}
	}
}
