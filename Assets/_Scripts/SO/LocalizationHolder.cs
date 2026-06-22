using System;
using System.Linq;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Localization
{
	public enum Language
	{
		English,
		Russian,
		Portuguese
	}

	public interface ILocalizationHolder
	{
		Language DefaultLanguage { get; }
		string GetKey(string key, Language language);

		/// <summary>
		/// Non-logging lookup: returns true and the localized text when the key exists and has a
		/// non-empty translation; otherwise returns false and leaves <paramref name="value"/> = key.
		/// Use this for optional/fallback text (e.g. loot names) so a missing key doesn't spam errors.
		/// </summary>
		bool TryGetKey(string key, Language language, out string value);

#if UNITY_EDITOR
		void ApplyParsedData(string data);
#endif
	}



	[Serializable]
	public struct Entry
	{
		[LabelText("Key")]
		public string Key;

		[LabelText("En")]
		[TextArea(2, 8)]
		public string Text;

		[LabelText("Ru")]
		[TextArea(2, 8)]
		public string RuText;

		[LabelText("Pt")]
		[TextArea(2, 8)]
		public string PoText;
	}

	[CreateAssetMenu(
		fileName = "Localization",
		menuName = "ScriptableObjects/LocalizationHolder"
	)]
	public class LocalizationHolder : ScriptableObject, ILocalizationHolder
	{
		[Title("Localization Settings")]
		[EnumToggleButtons]
		[PropertyOrder(-10)]
		[SerializeField]
		private Language _defaultLanguage = Language.English;

		[ListDrawerSettings(Expanded = true, NumberOfItemsPerPage = 10, DraggableItems = true)]
		[SerializeField]
		private Entry[] _entries;

		public Language DefaultLanguage => _defaultLanguage;

		[Button(ButtonSizes.Medium), GUIColor(0.2f, 0.8f, 1f)]
		[PropertySpace(10)]
		private void SortByKey()
		{
			_entries = _entries.OrderBy(e => e.Key).ToArray();
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
#endif
		}

		public string GetKey(string key, Language language)
		{
			var entry = _entries.FirstOrDefault(e => e.Key == key);

			if (string.IsNullOrEmpty(entry.Key))
			{
				Debug.LogError($"Localization key '{key}' not found!");
				return key;
			}

			return language switch
			{
				Language.Russian => entry.RuText,
				Language.Portuguese => entry.PoText,
				_ => entry.Text,
			};
		}

		public bool TryGetKey(string key, Language language, out string value)
		{
			value = key;
			if (string.IsNullOrEmpty(key) || _entries == null)
				return false;

			foreach (var entry in _entries)
			{
				if (entry.Key != key)
					continue;

				var text = language switch
				{
					Language.Russian => entry.RuText,
					Language.Portuguese => entry.PoText,
					_ => entry.Text,
				};

				if (string.IsNullOrEmpty(text))
					return false;

				value = text;
				return true;
			}

			return false;
		}

#if UNITY_EDITOR
		[Button("Apply Parsed Data", ButtonSizes.Large)]
		[ShowIf("@UnityEditor.EditorApplication.isPlaying == false")]
		[PropertySpace(15)]
		public void ApplyParsedData(string data)
		{
			// ������ ���������� ������ �� csv/json
			UnityEditor.EditorUtility.SetDirty(this);
		}
#endif
	}
}