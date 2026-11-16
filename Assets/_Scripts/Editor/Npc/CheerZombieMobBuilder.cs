using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Builds the zombie cheerleader mob — Tools ▸ Crowds ▸ Build Cheer Zombie Mob. Safe to re-run.
	///  1. Mob prefab Assets/_Prefabs/Mobs/CheerZombieMob.prefab — copied once from FastMob.prefab (collider, health
	///     bar, Mob component), its visual is then swapped by the standard pipeline.
	///  2. Preset Assets/_Data/Crowd/Setup/CheerZombieMob_ModelSetup.asset (created/updated in code) for
	///     Assets/Model/Cheerleader Zombie: idle = "Zombie Idle", run = "Treadmill Running", die = "Dying";
	///     VAT-baked (they also come in Infinite Scroll waves), visual 1.8 m like the cheerleaders.
	///  3. MobModelPipeline.Run(preset): material, Generic clips, controller, prefab visual, VAT bake into
	///     Assets/_Data/Crowd/CheerZombieMob/ and CrowdLibrary on the config.
	///  4. MobConfig CheerZombie (Fast stats, created from Fast.asset if missing): prefab → CheerZombieMob, 150 HP,
	///     own VAT library, death animation 2 s (skinned fallback only).
	/// No pompoms on the zombie version. See Docs/CheerleaderFeature.md.
	/// </summary>
	public static class CheerZombieMobBuilder
	{
		private const string Log = "[CheerZombie]";
		private const string ModelFolder = "Assets/Model/Cheerleader Zombie";
		private const string ModelPath = ModelFolder + "/cheerleader_zombie_5k (1).fbx"; // the rigged one (the other FBX has no skeleton)
		private const string TakePrefix = ModelFolder + "/cheerleader_zombie_5k@";
		private const string SourcePrefabPath = "Assets/_Prefabs/Mobs/FastMob.prefab";
		private const string PrefabPath = "Assets/_Prefabs/Mobs/CheerZombieMob.prefab";
		private const string PresetPath = "Assets/_Data/Crowd/Setup/CheerZombieMob_ModelSetup.asset";
		private const string FastConfigPath = "Assets/_Data/Mobs/Fast.asset";
		private const string ConfigPath = "Assets/_Data/Mobs/CheerZombie.asset";
		private const string ConfigId = "CheerZombie";
		private const float Health = 150f;
		private const float DeathAnimationTime = 2f;
		private const float Height = 1.8f;

		[MenuItem("Tools/Crowds/Build Cheer Zombie Mob")]
		public static void Build()
		{
			var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
			if (model == null)
			{
				Debug.LogError($"{Log} Model not found: {ModelPath}");
				return;
			}

			// 1. Prefab (copy of FastMob once).
			if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null && !AssetDatabase.CopyAsset(SourcePrefabPath, PrefabPath))
			{
				Debug.LogError($"{Log} Can't copy {SourcePrefabPath} → {PrefabPath}");
				return;
			}
			var mob = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<Mob>();

			// 4a. Config (needed by the preset).
			var config = AssetDatabase.LoadAssetAtPath<MobConfig>(ConfigPath);
			if (config == null)
			{
				if (!AssetDatabase.CopyAsset(FastConfigPath, ConfigPath))
				{
					Debug.LogError($"{Log} Can't copy {FastConfigPath} → {ConfigPath}");
					return;
				}
				config = AssetDatabase.LoadAssetAtPath<MobConfig>(ConfigPath);
			}

			// 2. Preset.
			var preset = AssetDatabase.LoadAssetAtPath<MobModelSetupPreset>(PresetPath);
			if (preset == null)
			{
				preset = ScriptableObject.CreateInstance<MobModelSetupPreset>();
				AssetDatabase.CreateAsset(preset, PresetPath);
			}
			preset.Model = model;
			preset.BaseColor = FindTexture("_BaseColor");
			preset.Normal = FindTexture("_Normal");
			preset.MaterialName = "CheerleaderZombieMaterial";
			preset.Clips = new List<MobModelSetupPreset.ClipSource>
			{
				Clip("idle", "Zombie Idle", loop: true),
				Clip("run", "Treadmill Running", loop: true),
				Clip("die", "Dying", loop: false),
			};
			preset.Prefab = mob;
			preset.VisualName = "CheerleaderZombie";
			preset.TargetHeight = Height;
			preset.WeaponPrefab = null;
			preset.BakeVat = true;
			preset.Configs = new List<MobConfig> { config };
			EditorUtility.SetDirty(preset);
			AssetDatabase.SaveAssets();

			// 3. Pipeline.
			if (!MobModelPipeline.Run(preset))
			{
				Debug.LogError($"{Log} MobModelPipeline failed — see [MobModel] errors above.");
				return;
			}

			// 4b. Config values.
			var so = new SerializedObject(config);
			so.FindProperty("_id").stringValue = ConfigId;
			so.FindProperty("_prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<Mob>();
			so.FindProperty("_health").floatValue = Health;
			// _crowdLibrary is set by the pipeline (own VAT bake). VAT mobs ignore DeathAnimationTime (no live Animator);
			// it still applies if the library is cleared to fall back to the skinned visual.
			so.FindProperty("_deathAnimationTime").floatValue = DeathAnimationTime;
			var headwear = so.FindProperty("_headwear");
			if (headwear != null) headwear.objectReferenceValue = null;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(config);
			AssetDatabase.SaveAssets();
			Debug.Log($"{Log} Done: {PrefabPath}, {ConfigPath} ({Health} HP).");
		}

		private static MobModelSetupPreset.ClipSource Clip(string state, string take, bool loop)
		{
			var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{TakePrefix}{take}.fbx");
			if (fbx == null)
				Debug.LogWarning($"{Log} Take '{take}' not found ({TakePrefix}{take}.fbx).");
			return new MobModelSetupPreset.ClipSource { State = state, Fbx = fbx, Loop = loop, RemoveRootDrift = loop };
		}

		private static Texture2D FindTexture(string suffix)
		{
			foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ModelFolder }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (System.IO.Path.GetFileNameWithoutExtension(path).EndsWith(suffix))
					return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
			}
			Debug.LogWarning($"{Log} No *{suffix} texture in {ModelFolder}.");
			return null;
		}
	}
}
