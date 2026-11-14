using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>Controller slot a clip goes into (see PlayerAnimatorControllerBuilder / Docs/PlayerAnimation.md).</summary>
	public enum PlayerClipSlot
	{
		Rifle_Idle, Rifle_Forward, Rifle_Backward, Rifle_Left, Rifle_Right,
		Rifle_ForwardLeft, Rifle_ForwardRight, Rifle_BackwardLeft, Rifle_BackwardRight,
		Rifle_Aim, Rifle_Fire, Rifle_Reload, Rifle_Hit,

		Pistol_Idle, Pistol_Forward, Pistol_Backward, Pistol_Left, Pistol_Right,
		Pistol_ForwardLeft, Pistol_ForwardRight, Pistol_BackwardLeft, Pistol_BackwardRight,
		Pistol_Aim, Pistol_Fire, Pistol_Reload, Pistol_Hit,

		Melee, ThrowCharge, Throw, Death,
	}

	/// <summary>
	/// Editor-only recipe for the player body: a Mixamo-rigged FBX + animation-only Mixamo FBXs sorted into
	/// sub-folders <c>Rifle/</c>, <c>Pistol/</c>, <c>Common/</c> next to the model. "Scan Clips" maps files to controller
	/// slots by keywords (and fills gaps with mirrors / fallbacks), "Run Setup" imports everything as Humanoid,
	/// fills Player.controller and builds the body under the Player in the open scene. See <see cref="PlayerModelPipeline"/>
	/// and Docs/PlayerAnimation.md.
	/// </summary>
	[CreateAssetMenu(menuName = "Crowds/Player Model Setup Preset", fileName = "PlayerModelSetup")]
	public class PlayerModelSetupPreset : ScriptableObject
	{
		[Serializable]
		public class ClipEntry
		{
			[HorizontalGroup("A", Width = 170), HideLabel]
			public PlayerClipSlot Slot;
			[HorizontalGroup("A"), HideLabel, Tooltip("Animation FBX (Mixamo, Without Skin).")]
			public GameObject Fbx;
			[HorizontalGroup("B", Width = 70), LabelWidth(40)]
			public bool Mirror;
			[HorizontalGroup("B", Width = 70), LabelWidth(40)]
			public bool Loop;
			[HorizontalGroup("B"), LabelWidth(40), Tooltip("Trim start, seconds.")]
			public float From;
			[HorizontalGroup("B"), LabelWidth(25), Tooltip("Trim end, seconds. 0 = till the end.")]
			public float To;
		}

		[Title("Source art")]
		[Required, Tooltip("Mixamo FBX with skin (T-pose).")]
		public GameObject Model;
		public Texture2D BaseColor;
		public Texture2D Normal;
		[Tooltip("Material created next to the model. Empty ⇒ <model>Material.")]
		public string MaterialName;

		[Title("Clips")]
		[Tooltip("Fire clips are cut to this many seconds from the start when scanned (one recoil). 0 = whole clip.")]
		public float FireClipLength = 0.3f;
		[Tooltip("Where the grenade clip is split into ThrowCharge | Throw, fraction of its length (set at scan). " +
		         "0.15 = Mixamo 'Toss Grenade' (underhand): the hand is furthest back at ~0.2 s of 1.4 s.")]
		[Range(0.05f, 0.9f)] public float ThrowSplit = 0.15f;
		[TableList(AlwaysExpanded = true)]
		public List<ClipEntry> Clips = new();

		[Title("Body in the scene")]
		[Tooltip("Body height, m.")]
		public float TargetHeight = 1.85f;
		[Tooltip("Name of the body child created under the Player.")]
		public string BodyName = "Body";
		[Tooltip("Children of the Player (any depth) whose renderers get disabled (placeholders). Colliders and muzzles stay.")]
		public List<string> HideRenderers = new() { "Capsule", "Weapon" };
		[Tooltip("Add WeaponHandIK (left hand on the forend) to the body.")]
		public bool LeftHandIK = true;

		[Button(ButtonSizes.Medium), PropertyOrder(-10), HorizontalGroup("Buttons")]
		private void ScanClips() => PlayerModelPipeline.ScanClips(this);

		[Button(ButtonSizes.Medium), PropertyOrder(-10), HorizontalGroup("Buttons"), GUIColor(0.6f, 1f, 0.6f)]
		private void RunSetup() => PlayerModelPipeline.Run(this);
	}
}
