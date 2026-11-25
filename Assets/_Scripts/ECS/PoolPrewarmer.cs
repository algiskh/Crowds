using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Loading-time warm-up, run by EntryPoint.InitializeAsync under the loading curtain (after the navmesh bake,
	/// before systems start). Removes two classic "first time" hitches:
	///  1. <b>Instantiate</b> — pre-creates pooled effects (per EffectsHolder id), decals (per DecalsConfigHolder id),
	///     bullets (per projectile prefab) and loot pickups straight into their pools (mobs are prewarmed separately,
	///     EntryPoint.PrewarmMobPool).
	///  2. <b>Shader / pipeline-state compilation</b> — the new instances are shown in front of the camera for a few
	///     rendered frames (effects silently, audio off), so their materials get compiled now and not on the first
	///     explosion / poison cloud / decal. Plus MainHolder.ShaderVariants (optional recorded collection) is warmed.
	/// Counts: MainHolder "Warm-up" box. Everything ends inactive in the pools, exactly as the systems expect.
	/// </summary>
	public static class PoolPrewarmer
	{
		private const int RenderFrames = 3;

		private sealed class State
		{
			public readonly List<SceneEffect> ShownEffects = new();
			public readonly List<GameObject> ShownObjects = new();
			public readonly List<(Decal decal, string id)> Decals = new();
		}

		public static async UniTask RunAsync(EcsWorld world, MainHolder holder, Vector3 anchor, CancellationToken token)
		{
			if (holder == null)
				return;

			var state = Prepare(world, holder, anchor);

			// Let the camera render them (compiles shaders / pipeline states under the curtain).
			for (int f = 0; f < RenderFrames; f++)
				await UniTask.Yield(PlayerLoopTiming.Update, token);

			Finish(world, state);
		}

		private static State Prepare(EcsWorld world, MainHolder holder, Vector3 anchor)
		{
			var state = new State();
			var shownEffects = state.ShownEffects;
			var shownObjects = state.ShownObjects;
			var decalStacks = state.Decals;

			var variants = holder.ShaderVariants;
			if (variants != null && !variants.isWarmedUp)
				variants.WarmUp();

			int slot = 0;
			// Spread the instances on a small grid around the anchor (the player's position: in the camera view).
			Vector3 NextPosition()
			{
				int i = slot++;
				return anchor + new Vector3((i % 8 - 3.5f) * 0.8f, 0.3f, (i / 8 % 6 - 2.5f) * 0.8f);
			}

			// --- Effects ---
			ref var fxPool = ref world.GetAsSingleton<EffectPoolComponent>();
			if (holder.EffectsHolder != null && holder.EffectPrewarmPerId > 0)
			{
				foreach (var fx in holder.EffectsHolder.GetAll())
				{
					if (fx == null || fx.Prefab == null || string.IsNullOrEmpty(fx.Id))
						continue;
					for (int i = 0; i < holder.EffectPrewarmPerId; i++)
					{
						var effect = Object.Instantiate(fx.Prefab, fxPool.Value.Parent);
						effect.Initialize(fx.Id);
						effect.transform.position = NextPosition();
						if (i == 0)
						{
							effect.BeginWarmUp();
							shownEffects.Add(effect);
						}
						else
						{
							fxPool.Pool(effect); // hidden straight away
						}
					}
				}
			}

			// --- Decals ---
			ref var decalPool = ref world.GetAsSingleton<DecalPoolComponent>();
			decalPool.Pools ??= new Dictionary<string, Stack<Decal>>();
			var decals = holder.DecalsConfigHolder;
			if (decals != null && decals.Prefab != null && decals.Configs != null && holder.DecalPrewarmPerId > 0)
			{
				foreach (var config in decals.Configs)
				{
					if (config == null || string.IsNullOrEmpty(config.Id))
						continue;
					for (int i = 0; i < holder.DecalPrewarmPerId; i++)
					{
						var decal = Object.Instantiate(decals.Prefab, decalPool.Parent);
						decal.Initialize(config);
						decal.ApplyVariation(config);
						decal.transform.SetPositionAndRotation(NextPosition(), Quaternion.LookRotation(Vector3.down));
						if (i == 0)
						{
							decal.Show();
							shownObjects.Add(decal.gameObject);
						}
						else
						{
							decal.Hide();
						}
						decalStacks.Add((decal, config.Id));
					}
				}
			}

			// --- Bullets (per projectile prefab: AmmoConfigHolder + the legacy BulletPrefab) ---
			ref var bulletPool = ref world.GetAsSingleton<BulletPoolComponent>();
			bulletPool.ByPrefab ??= new Dictionary<Bullet, Stack<Bullet>>();
			var bulletPrefabs = new HashSet<Bullet>();
			if (holder.AmmoConfigHolder != null && holder.AmmoConfigHolder.Configs != null)
				foreach (var ammo in holder.AmmoConfigHolder.Configs)
					if (ammo != null && ammo.ProjectilePrefab != null)
						bulletPrefabs.Add(ammo.ProjectilePrefab);
			if (holder.BulletPrefab != null)
				bulletPrefabs.Add(holder.BulletPrefab);
			if (holder.BulletPrewarmPerPrefab > 0)
			{
				foreach (var prefab in bulletPrefabs)
				{
					if (!bulletPool.ByPrefab.TryGetValue(prefab, out var stack))
						bulletPool.ByPrefab[prefab] = stack = new Stack<Bullet>();
					for (int i = 0; i < holder.BulletPrewarmPerPrefab; i++)
					{
						var bullet = Object.Instantiate(prefab, bulletPool.Parent);
						bullet.SourcePrefab = prefab;
						bullet.transform.position = NextPosition();
						bool show = i == 0;
						bullet.gameObject.SetActive(show);
						if (show)
							shownObjects.Add(bullet.gameObject);
						stack.Push(bullet);
					}
				}
			}

			// --- Loot pickups ---
			ref var lootPool = ref world.GetAsSingleton<LootPoolComponent>();
			lootPool.Value ??= new Stack<Loot>();
			if (holder.LootPrefab != null)
			{
				for (int i = lootPool.Value.Count; i < holder.LootPrewarm; i++)
				{
					var loot = Object.Instantiate(holder.LootPrefab, lootPool.Parent);
					loot.transform.position = NextPosition();
					bool show = i == 0;
					loot.gameObject.SetActive(show);
					if (show)
						shownObjects.Add(loot.gameObject);
					lootPool.Value.Push(loot);
				}
			}

			return state;
		}

		// Back into the pools, inactive.
		private static void Finish(EcsWorld world, State state)
		{
			ref var fxPoolAfter = ref world.GetAsSingleton<EffectPoolComponent>();
			foreach (var effect in state.ShownEffects)
			{
				if (effect == null)
					continue;
				effect.EndWarmUp();
				fxPoolAfter.Pool(effect);
			}
			foreach (var go in state.ShownObjects)
				if (go != null)
					go.SetActive(false);

			ref var decalPoolAfter = ref world.GetAsSingleton<DecalPoolComponent>();
			foreach (var (decal, id) in state.Decals)
			{
				if (decal == null)
					continue;
				if (!decalPoolAfter.Pools.TryGetValue(id, out var stack))
					decalPoolAfter.Pools[id] = stack = new Stack<Decal>();
				stack.Push(decal);
			}
		}
	}
}
