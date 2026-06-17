using UnityEngine;

namespace Scene.Animation
{
	public enum AnimationType
	{
		Idle,
		Walk,
		Run,
		Attack,
		Die,
		Throw,
		ThrowCooldown
	}

	/// <summary>
	/// Maps <see cref="AnimationType"/> to Animator state names and their precomputed hashes.
	/// Names MUST match the state names in the mob Animator controllers. Hashes are built once
	/// so the per-frame reconciliation loop never calls Animator.StringToHash.
	/// </summary>
	public static class AnimationTypes
	{
		private static readonly string[] _names =
		{
			"idle",            // Idle
			"walk",            // Walk
			"run",             // Run
			"attack",          // Attack
			"die",             // Die
			"throw",           // Throw
			"throw_cooldown"   // ThrowCooldown
		};

		private static readonly int[] _hashes = BuildHashes();

		private static int[] BuildHashes()
		{
			var hashes = new int[_names.Length];
			for (int i = 0; i < _names.Length; i++)
				hashes[i] = Animator.StringToHash(_names[i]);
			return hashes;
		}

		public static int ToHash(this AnimationType type) => _hashes[(int)type];
		public static string ToStateName(this AnimationType type) => _names[(int)type];
	}
}
