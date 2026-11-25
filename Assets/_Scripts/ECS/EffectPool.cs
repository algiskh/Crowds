using System.Collections.Generic;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Pool of scene effects, keyed by effect id (FxWrapper.Id). A plain service: created once in
	/// EntryPoint.SetupSpawnData and injected into systems via ecslite-di (<c>EcsCustomInject&lt;EffectPool&gt;</c>).
	/// Systems not migrated yet reach the same instance through the bridge singleton
	/// <see cref="EffectPoolComponent"/> (see Docs/tasks/T-001.md).
	/// </summary>
	public sealed class EffectPool
	{
		public readonly Dictionary<string, Stack<SceneEffect>> Pools = new();
		public readonly Transform Parent;

		public EffectPool(Transform parent)
		{
			Parent = parent;
		}

		/// <summary>
		/// Pops an effect by config (or instantiates a new one). The effect stays hidden: the caller shows it.
		/// </summary>
		public SceneEffect Spawn(FxWrapper config)
		{
			if (config == null || config.Prefab == null)
				return null;

			if (Pools.TryGetValue(config.Id, out var stack) && stack.Count > 0)
				return stack.Pop();

			var effect = Object.Instantiate(config.Prefab, Parent);
			effect.Initialize(config.Id);
			return effect;
		}

		/// <summary>
		/// Hides the effect, re-parents it under the pool root and pushes it back by its id.
		/// </summary>
		public void Release(SceneEffect effect)
		{
			effect.Hide();
			effect.SetParent(Parent);
			if (!Pools.TryGetValue(effect.Id, out var stack))
			{
				stack = new Stack<SceneEffect>();
				Pools[effect.Id] = stack;
			}
			stack.Push(effect);
		}
	}
}
