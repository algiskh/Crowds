using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Crowds.EditorTools
{
	/// <summary>
	/// Generates the player's Animator Controller skeleton + upper-body Avatar Mask:
	/// Tools ▸ Crowds ▸ Create Player Animator Controller.
	///
	/// Every state, blend-tree slot, parameter and transition is created; motions are left empty —
	/// drag the Mixamo clips into them (Docs/PlayerAnimation.md has the clip ↔ slot table).
	/// Parameter names come from <see cref="PlayerAnimator"/>, which drives them at runtime.
	///
	///   Base layer (full body):  Locomotion_Rifle ⇄ Locomotion_Pistol (2D strafe blend trees), AnyState → Death
	///   UpperBody layer (mask):  Aim_X ⇄ Fire_X / Reload_X / Hit, AnyState → Melee / ThrowCharge / Throw
	/// </summary>
	public static class PlayerAnimatorControllerBuilder
	{
		private const string Folder = "Assets/_Animations/Player";
		private const string ControllerPath = Folder + "/Player.controller";
		private const string MaskPath = Folder + "/PlayerUpperBody.mask";

		private static readonly (WeaponAnimationSet set, string suffix)[] Sets =
		{
			(WeaponAnimationSet.Rifle, "Rifle"),
			(WeaponAnimationSet.Pistol, "Pistol"),
		};

		[MenuItem("Tools/Crowds/Create Player Animator Controller")]
		public static void Create()
		{
			if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null &&
			    !EditorUtility.DisplayDialog("Player Animator Controller",
				    $"{ControllerPath} already exists.\nOverwrite it? Clips assigned to it will be lost.",
				    "Overwrite", "Cancel"))
				return;

			EnsureFolder(Folder);
			AssetDatabase.DeleteAsset(ControllerPath);
			AssetDatabase.DeleteAsset(MaskPath);

			var mask = CreateUpperBodyMask();
			var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

			AddParameters(controller);
			BuildBaseLayer(controller);
			BuildUpperBodyLayer(controller, mask);

			EditorUtility.SetDirty(controller);
			AssetDatabase.SaveAssets();
			Selection.activeObject = controller;
			Debug.Log($"[PlayerAnimator] Created {ControllerPath} and {MaskPath}. Fill the empty motions with clips.");
		}

		// ---------------------------------------------------------------- parameters

		private static void AddParameters(AnimatorController c)
		{
			c.AddParameter(PlayerAnimator.MoveXParam, AnimatorControllerParameterType.Float);
			c.AddParameter(PlayerAnimator.MoveYParam, AnimatorControllerParameterType.Float);
			c.AddParameter(PlayerAnimator.MoveSpeedParam, AnimatorControllerParameterType.Float);
			c.AddParameter(PlayerAnimator.WeaponSetParam, AnimatorControllerParameterType.Int);
			c.AddParameter(PlayerAnimator.IsFiringParam, AnimatorControllerParameterType.Bool);
			c.AddParameter(PlayerAnimator.FireParam, AnimatorControllerParameterType.Trigger);
			c.AddParameter(PlayerAnimator.IsReloadingParam, AnimatorControllerParameterType.Bool);
			c.AddParameter(PlayerAnimator.ReloadSpeedParam, AnimatorControllerParameterType.Float);
			c.AddParameter(PlayerAnimator.MeleeParam, AnimatorControllerParameterType.Trigger);
			c.AddParameter(PlayerAnimator.MeleeSpeedParam, AnimatorControllerParameterType.Float);
			c.AddParameter(PlayerAnimator.IsChargingThrowParam, AnimatorControllerParameterType.Bool);
			c.AddParameter(PlayerAnimator.ThrowParam, AnimatorControllerParameterType.Trigger);
			c.AddParameter(PlayerAnimator.HitParam, AnimatorControllerParameterType.Trigger);
			c.AddParameter(PlayerAnimator.IsDeadParam, AnimatorControllerParameterType.Bool);

			// Playback-rate multipliers default to 1 (0 would freeze the states before the first update).
			var ps = c.parameters;
			for (int i = 0; i < ps.Length; i++)
				if (ps[i].name == PlayerAnimator.MoveSpeedParam || ps[i].name == PlayerAnimator.ReloadSpeedParam ||
				    ps[i].name == PlayerAnimator.MeleeSpeedParam)
					ps[i].defaultFloat = 1f;
			c.parameters = ps;
		}

		// ---------------------------------------------------------------- base layer

		private static void BuildBaseLayer(AnimatorController c)
		{
			var layers = c.layers;
			layers[0].name = "Base";
			c.layers = layers;
			var sm = c.layers[0].stateMachine;

			var rifle = CreateLocomotion(c, "Locomotion_Rifle", new Vector3(300, 0));
			var pistol = CreateLocomotion(c, "Locomotion_Pistol", new Vector3(300, 120));
			sm.defaultState = rifle;
			SetWeaponSwitch(rifle, pistol, WeaponAnimationSet.Pistol);
			SetWeaponSwitch(pistol, rifle, WeaponAnimationSet.Rifle);

			var death = sm.AddState("Death", new Vector3(650, 60));
			var toDeath = sm.AddAnyStateTransition(death);
			toDeath.canTransitionToSelf = false;
			toDeath.hasExitTime = false;
			toDeath.duration = 0.1f;
			toDeath.AddCondition(AnimatorConditionMode.If, 0, PlayerAnimator.IsDeadParam);
		}

		/// <summary>2D Freeform Directional strafe tree: idle in the center, 8 directions around it.</summary>
		private static AnimatorState CreateLocomotion(AnimatorController c, string name, Vector3 position)
		{
			var state = c.CreateBlendTreeInController(name, out var tree, 0);
			tree.name = name;
			tree.blendType = BlendTreeType.FreeformDirectional2D;
			tree.blendParameter = PlayerAnimator.MoveXParam;
			tree.blendParameterY = PlayerAnimator.MoveYParam;

			const float d = 0.7071f;
			tree.AddChild(null, new Vector2(0, 0));     // Idle
			tree.AddChild(null, new Vector2(0, 1));     // Run Forward
			tree.AddChild(null, new Vector2(0, -1));    // Run Backward
			tree.AddChild(null, new Vector2(-1, 0));    // Strafe Left
			tree.AddChild(null, new Vector2(1, 0));     // Strafe Right
			tree.AddChild(null, new Vector2(-d, d));    // Run Forward Left
			tree.AddChild(null, new Vector2(d, d));     // Run Forward Right
			tree.AddChild(null, new Vector2(-d, -d));   // Run Backward Left
			tree.AddChild(null, new Vector2(d, -d));    // Run Backward Right

			state.speedParameter = PlayerAnimator.MoveSpeedParam;
			state.speedParameterActive = true;
			MoveState(c.layers[0].stateMachine, state, position);
			return state;
		}

		// ---------------------------------------------------------------- upper body layer

		private static void BuildUpperBodyLayer(AnimatorController c, AvatarMask mask)
		{
			c.AddLayer(PlayerAnimator.UpperBodyLayer);
			var layers = c.layers;
			int index = layers.Length - 1;
			layers[index].avatarMask = mask;
			layers[index].defaultWeight = 1f;
			layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
			c.layers = layers;
			var sm = c.layers[index].stateMachine;

			var aim = new AnimatorState[Sets.Length];
			for (int i = 0; i < Sets.Length; i++)
			{
				var (set, suffix) = Sets[i];
				float y = i * 260;
				aim[i] = sm.AddState("Aim_" + suffix, new Vector3(300, y));
				var fire = sm.AddState("Fire_" + suffix, new Vector3(560, y - 60));
				var reload = sm.AddState("Reload_" + suffix, new Vector3(560, y + 20));
				var hit = sm.AddState("Hit_" + suffix, new Vector3(560, y + 100));

				reload.speedParameter = PlayerAnimator.ReloadSpeedParam;
				reload.speedParameterActive = true;

				// Aim → Fire on every shot; Fire restarts itself on the next shot (auto fire).
				Instant(aim[i].AddTransition(fire), 0.05f).AddCondition(AnimatorConditionMode.If, 0, PlayerAnimator.FireParam);
				Instant(fire.AddTransition(fire), 0.02f).AddCondition(AnimatorConditionMode.If, 0, PlayerAnimator.FireParam);
				AfterClip(fire.AddTransition(aim[i]));

				// Reload while the flag is up (playback time-scaled to GunConfig.ReloadTime by PlayerAnimator).
				Instant(aim[i].AddTransition(reload), 0.1f).AddCondition(AnimatorConditionMode.If, 0, PlayerAnimator.IsReloadingParam);
				Instant(fire.AddTransition(reload), 0.1f).AddCondition(AnimatorConditionMode.If, 0, PlayerAnimator.IsReloadingParam);
				Instant(reload.AddTransition(aim[i]), 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, PlayerAnimator.IsReloadingParam);

				// Hit flinch only from aim (doesn't break a reload/throw/melee).
				Instant(aim[i].AddTransition(hit), 0.05f).AddCondition(AnimatorConditionMode.If, 0, PlayerAnimator.HitParam);
				AfterClip(hit.AddTransition(aim[i]));
			}
			sm.defaultState = aim[0];

			// Weapon swap between aim poses.
			for (int i = 0; i < Sets.Length; i++)
				for (int j = 0; j < Sets.Length; j++)
					if (i != j)
						SetWeaponSwitch(aim[i], aim[j], Sets[j].set);

			// Shared actions (same clip for every weapon), entered from anywhere, return to the right aim pose.
			var melee = sm.AddState("Melee", new Vector3(900, 0));
			// Swing rate (Rage bonus attack speed, set by PlayerAnimator.TriggerMelee).
			melee.speedParameter = PlayerAnimator.MeleeSpeedParam;
			melee.speedParameterActive = true;
			var charge = sm.AddState("ThrowCharge", new Vector3(900, 120));
			var toss = sm.AddState("Throw", new Vector3(900, 240));

			var anyMelee = Instant(sm.AddAnyStateTransition(melee), 0.05f);
			anyMelee.canTransitionToSelf = false;
			anyMelee.AddCondition(AnimatorConditionMode.If, 0, PlayerAnimator.MeleeParam);

			// Throw is checked before ThrowCharge: on release both change in the same frame.
			var anyThrow = Instant(sm.AddAnyStateTransition(toss), 0.05f);
			anyThrow.canTransitionToSelf = false;
			anyThrow.AddCondition(AnimatorConditionMode.If, 0, PlayerAnimator.ThrowParam);

			var anyCharge = Instant(sm.AddAnyStateTransition(charge), 0.15f);
			anyCharge.canTransitionToSelf = false;
			anyCharge.AddCondition(AnimatorConditionMode.If, 0, PlayerAnimator.IsChargingThrowParam);

			for (int i = 0; i < Sets.Length; i++)
			{
				ReturnToAim(melee, aim[i], Sets[i].set, afterClip: true);
				ReturnToAim(toss, aim[i], Sets[i].set, afterClip: true);
				// Charge cancelled without a throw (e.g. no grenades left).
				ReturnToAim(charge, aim[i], Sets[i].set, afterClip: false)
					.AddCondition(AnimatorConditionMode.IfNot, 0, PlayerAnimator.IsChargingThrowParam);
			}
		}

		// ---------------------------------------------------------------- mask

		private static AvatarMask CreateUpperBodyMask()
		{
			var mask = new AvatarMask { name = "PlayerUpperBody" };
			for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
			{
				var part = (AvatarMaskBodyPart)i;
				bool upper = part == AvatarMaskBodyPart.Body
				             || part == AvatarMaskBodyPart.Head
				             || part == AvatarMaskBodyPart.LeftArm
				             || part == AvatarMaskBodyPart.RightArm
				             || part == AvatarMaskBodyPart.LeftFingers
				             || part == AvatarMaskBodyPart.RightFingers
				             || part == AvatarMaskBodyPart.LeftHandIK
				             || part == AvatarMaskBodyPart.RightHandIK;
				mask.SetHumanoidBodyPartActive(part, upper);
			}
			AssetDatabase.CreateAsset(mask, MaskPath);
			return mask;
		}

		// ---------------------------------------------------------------- helpers

		private static AnimatorStateTransition Instant(AnimatorStateTransition t, float duration)
		{
			t.hasExitTime = false;
			t.duration = duration;
			return t;
		}

		private static AnimatorStateTransition AfterClip(AnimatorStateTransition t)
		{
			t.hasExitTime = true;
			t.exitTime = 0.9f;
			t.duration = 0.1f;
			return t;
		}

		private static void SetWeaponSwitch(AnimatorState from, AnimatorState to, WeaponAnimationSet target)
		{
			Instant(from.AddTransition(to), 0.15f)
				.AddCondition(AnimatorConditionMode.Equals, (int)target, PlayerAnimator.WeaponSetParam);
		}

		private static AnimatorStateTransition ReturnToAim(AnimatorState from, AnimatorState aim,
			WeaponAnimationSet set, bool afterClip)
		{
			var t = from.AddTransition(aim);
			if (afterClip) AfterClip(t); else Instant(t, 0.15f);
			t.AddCondition(AnimatorConditionMode.Equals, (int)set, PlayerAnimator.WeaponSetParam);
			return t;
		}

		private static void MoveState(AnimatorStateMachine sm, AnimatorState state, Vector3 position)
		{
			var states = sm.states;
			for (int i = 0; i < states.Length; i++)
				if (states[i].state == state)
					states[i].position = position;
			sm.states = states;
		}

		private static void EnsureFolder(string path)
		{
			if (AssetDatabase.IsValidFolder(path))
				return;
			var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
		}
	}
}
