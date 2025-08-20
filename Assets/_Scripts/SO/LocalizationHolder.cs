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

#if UNITY_EDITOR
		[Button("Apply Parsed Data", ButtonSizes.Large)]
		[ShowIf("@UnityEditor.EditorApplication.isPlaying == false")]
		[PropertySpace(15)]
		public void ApplyParsedData(string data)
		{
			// Логика применения данных из csv/json
			UnityEditor.EditorUtility.SetDirty(this);
		}
#endif
	}
}