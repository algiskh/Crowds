using UnityEditor;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Small "what is it made of" textures for the level builders (Assets/Textures, power-of-two, low detail — the
	/// raw sources live outside Assets in ArtSource/Textures). <see cref="Apply"/> pins the importer to a small max
	/// size with mipmaps, block compression and repeat wrap, then puts the texture on a Lit (See-Through) material.
	/// </summary>
	internal static class LevelTextures
	{
		private const string Folder = "Assets/Textures";
		internal const string Ice = Folder + "/Ice.jpg";                         // seamless, 512
		internal const string CardboardMarked = Folder + "/Cardboard_Marked.jpg"; // box side with handling marks, 512×256
		internal const string CardboardPlain = Folder + "/Cardboard_Plain.jpg";   // 256
		internal const string PlasticWrap = Folder + "/Plastic_Wrap.jpg";         // grey film, tinted per material, 256
		internal const string PalletWood = Folder + "/Pallet_Wood.jpg";           // pallet top, 256
		// Suburb houses — greyscale, tinted per material; seamless on both axes, 256. The houses have metric planar
		// UVs (SuburbBuilder.PlanarUVs), so the material tiling = 1 / metres per tile.
		internal const string SidingLap = Folder + "/Siding_Lap.jpg";             // clapboard, 8 boards per tile
		internal const string SidingPanel = Folder + "/Siding_Panel.jpg";         // wide panels, 2 per tile
		internal const string Stucco = Folder + "/Stucco.jpg";                    // rough render
		internal const string RoofShingles = Folder + "/Roof_Shingles.jpg";       // shingles, 8 rows per tile

		/// <summary>Mean brightness (sRGB, 0..1) of each greyscale texture: tint = wanted average colour / mean.</summary>
		internal static float Mean(string path) => path switch
		{
			SidingLap => 0.86f,
			SidingPanel => 0.835f,
			Stucco => 0.80f,
			RoofShingles => 0.585f,
			_ => 1f,
		};

		/// <summary>Tint that keeps <paramref name="average"/> as the material's average colour on a greyscale texture.</summary>
		internal static Color TintFor(string path, Color average)
		{
			float mean = Mean(path);
			return new Color(Mathf.Min(1f, average.r / mean), Mathf.Min(1f, average.g / mean), Mathf.Min(1f, average.b / mean), 1f);
		}

		/// <summary>Puts <paramref name="path"/> on <paramref name="material"/> as the albedo, tinted and tiled.</summary>
		internal static void Apply(Material material, string path, Color tint, float tiling = 1f)
		{
			var texture = Load(path);
			if (material == null || texture == null)
				return;
			material.SetTexture("_BaseMap", texture);
			material.SetTexture("_MainTex", texture);
			material.SetTextureScale("_BaseMap", Vector2.one * tiling);
			material.SetTextureScale("_MainTex", Vector2.one * tiling);
			material.SetColor("_BaseColor", tint);
			material.SetColor("_Color", tint);
			EditorUtility.SetDirty(material);
		}

		internal static Texture2D Load(string path)
		{
			var importer = AssetImporter.GetAtPath(path) as TextureImporter;
			if (importer == null)
			{
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
				importer = AssetImporter.GetAtPath(path) as TextureImporter;
			}
			if (importer == null)
			{
				Debug.LogWarning($"[LevelTextures] {path} not found — the material stays untextured.");
				return null;
			}
			int maxSize = path == Ice || path == CardboardMarked ? 512 : 256;
			bool dirty = importer.textureType != TextureImporterType.Default || importer.maxTextureSize != maxSize ||
			             !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Repeat ||
			             importer.textureCompression != TextureImporterCompression.Compressed || importer.anisoLevel != 1 ||
			             importer.isReadable || importer.npotScale != TextureImporterNPOTScale.ToNearest;
			if (dirty)
			{
				importer.textureType = TextureImporterType.Default;
				importer.sRGBTexture = true;
				importer.alphaSource = TextureImporterAlphaSource.None;
				importer.maxTextureSize = maxSize;
				importer.mipmapEnabled = true;
				importer.wrapMode = TextureWrapMode.Repeat;
				importer.filterMode = FilterMode.Bilinear;
				importer.anisoLevel = 1;
				importer.isReadable = false;
				importer.npotScale = TextureImporterNPOTScale.ToNearest;
				importer.textureCompression = TextureImporterCompression.Compressed;
				importer.SaveAndReimport();
			}
			return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
		}
	}
}
