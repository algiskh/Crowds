using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Campaign order + menu icons — Tools ▸ Crowds ▸ Update Campaign (Order + Icons). Also run at the end of the
	/// tutorial and suburb-level builders.
	///  • Order: known levels by difficulty (KnownOrder: tutorial, Infinite Scroll, Key, Baseball, Mall, Hockey,
	///    Cul-de-sac, Backyards) if built, then any other library entries in their current order.
	///  • Icons: <c>Assets/Sprites/LevelIcons/&lt;Definition&gt;.png</c> (256², drawn level logos) — imported as
	///    sprites and assigned to the matching LevelDefinition.
	/// See Docs/TutorialLevelFeature.md.
	/// </summary>
	public static class CampaignSetup
	{
		private const string Log = "[CampaignSetup]";
		private const string LibraryPath = "Assets/_Data/Levels/CampaignLevelLibrary.asset";
		private const string DefinitionFolder = "Assets/_Data/Levels/Definitions";
		internal const string IconFolder = "Assets/Sprites/LevelIcons";

		// Campaign definitions in their intended order (asset names in DefinitionFolder).
		// Campaign order by difficulty: tutorial → Infinite Scroll → Key → Baseball → Mall → Hockey → suburb levels.
		private static readonly string[] KnownOrder =
		{
			"Level_Tutorial", "LevelDefinition", "Level_Key", "Level_Baseball", "Level_Mall", "Level_Hockey",
			"Level_CulDeSac", "Level_Backyards",
		};

		[MenuItem("Tools/Crowds/Update Campaign (Order + Icons)")]
		public static void Apply()
		{
			AssignIcons();
			Order();
			AssetDatabase.SaveAssets();
		}

		private static void Order()
		{
			var library = AssetDatabase.LoadAssetAtPath<LevelLibrary>(LibraryPath);
			if (library == null)
			{
				Debug.LogWarning($"{Log} {LibraryPath} not found.");
				return;
			}

			// Known levels (already in the library or built) in KnownOrder, then any other library entries as they were.
			var ordered = new List<LevelDefinition>();
			foreach (var name in KnownOrder)
			{
				var level = Load(name);
				if (level != null && (level.LevelPrefab != null || Contains(library, level)))
					ordered.Add(level);
			}
			foreach (var level in library.Levels)
				if (level != null && !ordered.Contains(level))
					ordered.Add(level);

			var so = new SerializedObject(library);
			var levels = so.FindProperty("_levels");
			levels.arraySize = ordered.Count;
			for (int i = 0; i < ordered.Count; i++)
				levels.GetArrayElementAtIndex(i).objectReferenceValue = ordered[i];
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(library);
			Debug.Log($"{Log} Campaign: {string.Join(", ", ordered.ConvertAll(l => l.DisplayName))}");
		}

		private static void AssignIcons()
		{
			foreach (var name in KnownOrder)
			{
				var definition = Load(name);
				string iconPath = $"{IconFolder}/{name}.png";
				if (definition == null || AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath) == null)
					continue;

				if (AssetImporter.GetAtPath(iconPath) is TextureImporter importer &&
				    (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled ||
				     importer.spriteImportMode != SpriteImportMode.Single))
				{
					importer.textureType = TextureImporterType.Sprite;
					importer.spriteImportMode = SpriteImportMode.Single;
					importer.mipmapEnabled = false;
					importer.alphaIsTransparency = true;
					importer.maxTextureSize = 256;
					importer.textureCompression = TextureImporterCompression.Uncompressed;
					importer.SaveAndReimport();
				}

				var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
				if (sprite == null)
				{
					Debug.LogWarning($"{Log} {iconPath} is not a sprite.");
					continue;
				}
				var so = new SerializedObject(definition);
				so.FindProperty("_icon").objectReferenceValue = sprite;
				so.ApplyModifiedPropertiesWithoutUndo();
				EditorUtility.SetDirty(definition);
			}
		}

		private static bool Contains(LevelLibrary library, LevelDefinition level)
		{
			foreach (var l in library.Levels)
				if (l == level)
					return true;
			return false;
		}

		private static LevelDefinition Load(string name) =>
			AssetDatabase.LoadAssetAtPath<LevelDefinition>($"{DefinitionFolder}/{name}.asset");
	}
}
