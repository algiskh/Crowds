using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Editor-only recipe for turning a rigged (Mixamo) FBX into a mob visual: which model, which animation
	/// FBXs map to which Animator states, textures, the target mob prefab and the MobConfigs that get the baked
	/// VAT library. Press "Run Setup" (or Tools ▸ Crowds ▸ Run Selected Mob Model Preset) — see
	/// <see cref="MobModelPipeline"/>. Presets live in Assets/_Data/Crowd/Setup/ and are re-runnable after
	/// the art changes (e.g. a decimated FBX). Full guide: Docs/CrowdRenderingFeature.md.
	/// </summary>
	[CreateAssetMenu(menuName = "Crowds/Mob Model Setup Preset", fileName = "MobModelSetup")]
	public class MobModelSetupPreset : ScriptableObject
	{
		[Serializable]
		public class ClipSource
		{
			[Tooltip("Animator state name; must match an AnimationType (idle/walk/run/attack/die/throw/throw_cooldown).")]
			public string State = "run";
			[Tooltip("FBX holding the take (animation-only Mixamo export, or the model itself).")]
			public GameObject Fbx;
			public bool Loop = true;
			[Tooltip("If the hips travel on XZ (Mixamo 'In Place' off), write an in-place copy of the clip.")]
			public bool RemoveRootDrift = true;
		}

		[Title("Source art")]
		[Required] public GameObject Model;
		public Texture2D BaseColor;
		public Texture2D Normal;
		[Tooltip("Material created next to the model. Empty ⇒ <VisualName>Material.")]
		public string MaterialName;
		public List<ClipSource> Clips = new();

		[Title("Target")]
		[Required] public Mob Prefab;
		[Tooltip("Name of the visual child inside the prefab.")]
		public string VisualName = "Visual";
		[Tooltip("Visual height in prefab space. 0 ⇒ keep the height of the current visual; if it's missing, measure HeightReference.")]
		public float TargetHeight;
		[Tooltip("Fallback for auto height (e.g. the previous model FBX).")]
		public GameObject HeightReference;
		[Tooltip("Direct children of the prefab to delete (e.g. placeholder meshes).")]
		public List<string> RemoveObjects = new();
		[Tooltip("Direct children whose renderers get disabled but which stay (e.g. a capsule that carries the collider).")]
		public List<string> HideRenderers = new();

		[Title("Weapon (optional)")]
		[Tooltip("Held weapon prefab (WeaponModel). Attached under WeaponBone and assigned to Mob.Weapon. " +
		         "An existing attached instance is kept with its hand-tuned transform.")]
		public WeaponModel WeaponPrefab;
		public string WeaponBone = "mixamorig:RightHand";
		[Tooltip("State whose first frame is used to align the weapon: barrel along the character's forward, level. " +
		         "Empty/missing ⇒ WeaponLocalEuler is used as the local rotation under the bone.")]
		public string WeaponAlignState = "aim";
		[Tooltip("Initial offset of the weapon root from the bone, in the weapon's own axes (meters). Only when first attached.")]
		public Vector3 WeaponLocalPosition;
		[Tooltip("Local rotation under the bone when there is no align state. Only when first attached.")]
		public Vector3 WeaponLocalEuler;
		[Tooltip("World-space size of the weapon (compensates the model's import scale).")]
		public float WeaponScale = 1f;

		[Title("VAT")]
		public bool BakeVat = true;
		public float BakeFps = 30f;
		[Tooltip("Configs that receive the baked CrowdLibrary (they should use Prefab).")]
		public List<MobConfig> Configs = new();

		[Button(ButtonSizes.Large), PropertySpace(10)]
		[ContextMenu("Run Setup")]
		public void RunSetup() => MobModelPipeline.Run(this);

		public enum ReferenceViewSide { Front, Back, Left, Right }
		public enum ReferenceOverlayMode { Overlay, SideBySide }

		[Title("Reference check (optional)", "Picture (e.g. a Figma frame export) laid over the prefab for a visual match")]
		[Tooltip("Reference picture. Margins are trimmed automatically (transparency, or the corner colour); the drawn character is scaled to the visual's height.")]
		[OnValueChanged(nameof(RefreshReference))]
		public Texture2D ReferenceImage;
		[Tooltip("Side the picture shows the character from. The mob faces +Z of the prefab root.")]
		[OnValueChanged(nameof(RefreshReference))]
		public ReferenceViewSide ReferenceView = ReferenceViewSide.Front;
		[Tooltip("Overlay: semi-transparent picture over the model. SideBySide: opaque picture next to it.")]
		[OnValueChanged(nameof(RefreshReference))]
		public ReferenceOverlayMode ReferenceMode = ReferenceOverlayMode.Overlay;
		[Range(0.05f, 1f), OnValueChanged(nameof(RefreshReference))]
		public float ReferenceOpacity = 0.5f;
		[Tooltip("Picture height relative to the visual's height (1 = same).")]
		[Range(0.5f, 1.5f), OnValueChanged(nameof(RefreshReference))]
		public float ReferenceScale = 1f;
		[Tooltip("Nudge: X in meters (screen right), Y in fractions of the height.")]
		[OnValueChanged(nameof(RefreshReference))]
		public Vector2 ReferenceOffset;
		[OnValueChanged(nameof(RefreshReference))]
		public bool ReferenceFlipX;

		[ButtonGroup("Reference"), Button("Show Reference")]
		[ContextMenu("Show Reference")]
		public void ShowReference() => MobReferenceOverlay.Show(this);

		[ButtonGroup("Reference"), Button("Hide Reference")]
		[ContextMenu("Hide Reference")]
		public void HideReference() => MobReferenceOverlay.Hide();

		// Live update while tuning — only if the overlay is already shown for this preset's prefab.
		private void RefreshReference()
		{
			if (MobReferenceOverlay.IsShownFor(this))
				MobReferenceOverlay.Show(this, frameCamera: false);
		}
	}
}
