using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Wading: whoever stands in a <see cref="ShallowWater"/> pool (player or mob) visibly sinks into it — up to
	/// ShallowWater.SinkDepth (~1/3 of the player's height) in the middle, ramping in from the edges, eased over time.
	/// Only the visual moves: the root transform, navmesh agent, colliders and muzzle stay on the floor.
	///  • skinned visuals (player Body, mob model with the SimpleAnimator) — their local Y is lowered;
	///  • VAT crowds — CrowdRenderSystem lowers the draw matrix by WadeComponent.Depth.
	/// The opaque water surface hides the sunk part. Base local Y per visual transform is cached on first sight, so
	/// pooled mobs reused after dying in a pool are restored. Idle when the level has no pools. Registered right
	/// before CrowdRenderSystem. See Docs/SuburbLevelsFeature.md.
	/// </summary>
	public sealed class WadeSystem : IEcsRunSystem
	{
		private const float EaseSpeed = 2.5f; // metres per second of sinking / rising

		private readonly List<ShallowWater> _pools = new();
		private readonly Dictionary<Transform, float> _baseY = new();
		private bool _anyWading;

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			_pools.Clear();
			foreach (var zone in ModifierZone.Active)
				if (zone is ShallowWater water && water.SinkDepth > 0f)
					_pools.Add(water);
			if (_pools.Count == 0 && !_anyWading)
				return;

			float step = EaseSpeed * Time.deltaTime;
			var wadePool = world.GetPool<WadeComponent>();
			_anyWading = false;

			foreach (var entity in world.Filter<PlayerComponent>().End())
			{
				var player = world.GetPool<PlayerComponent>().Get(entity).Value;
				if (player == null)
					continue;
				var visual = player.BodyAnimator != null ? player.BodyAnimator.transform
					: player.Animator != null ? player.Animator.transform : null;
				Apply(wadePool, entity, player.transform, visual, step);
			}

			var mobPool = world.GetPool<MobComponent>();
			foreach (var entity in world.Filter<MobComponent>().End())
			{
				var mob = mobPool.Get(entity).Value;
				if (mob == null)
					continue;
				Apply(wadePool, entity, mob.transform, mob.Animator != null ? mob.Animator.transform : null, step);
			}
		}

		private void Apply(EcsPool<WadeComponent> wadePool, int entity, Transform root, Transform visual, float step)
		{
			float target = 0f;
			Vector3 position = root.position;
			for (int i = 0; i < _pools.Count; i++)
				target = Mathf.Max(target, _pools[i].SinkAt(position));

			bool has = wadePool.Has(entity);
			if (!has && target <= 0f)
			{
				// A pooled mob reused after dying in a pool: put its visual back up.
				if (visual != null && _baseY.TryGetValue(visual, out float restY) && visual.localPosition.y != restY)
				{
					var rest = visual.localPosition;
					rest.y = restY;
					visual.localPosition = rest;
				}
				return;
			}
			ref var wade = ref has ? ref wadePool.Get(entity) : ref wadePool.Add(entity);
			wade.Depth = Mathf.MoveTowards(wade.Depth, target, step);
			if (wade.Depth > 0f)
				_anyWading = true;

			// Skinned visual: lower its local Y (never the root itself).
			if (visual != null && visual != root)
			{
				if (!_baseY.TryGetValue(visual, out float baseY))
				{
					baseY = visual.localPosition.y;
					_baseY[visual] = baseY;
				}
				var local = visual.localPosition;
				// The offset is in world metres; the visual's parent may be scaled.
				float scale = visual.parent != null ? Mathf.Max(0.0001f, visual.parent.lossyScale.y) : 1f;
				local.y = baseY - wade.Depth / scale;
				visual.localPosition = local;
			}

			if (wade.Depth <= 0f)
				wadePool.Del(entity);
		}
	}
}
