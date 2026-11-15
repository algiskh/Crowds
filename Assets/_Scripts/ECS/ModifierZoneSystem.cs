using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Producer side of ModifierZone (oil slicks, hockey ice, …): the player and every mob standing in a zone get the
	/// zone's modifiers. Each one is added once (clone) and refreshed every frame while inside — Value and
	/// Lifetime = zone Linger — so nothing stacks and ModifiersSystem removes it shortly after the owner steps out.
	/// Consumers stay the usual ones (MoveSystem / PlayerMovementSystem read SpeedModifier and TractionModifier,
	/// ModifiersSystem ticks DamageModifier…). Registered right before ModifiersSystem. No-op without zones.
	/// Cost: (player + mobs) × zones cheap shape tests per frame. See Docs/HockeyLevelFeature.md.
	/// </summary>
	public sealed class ModifierZoneSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var zones = ModifierZone.Active;
			if (zones.Count == 0)
				return;

			var world = systems.GetWorld();
			var ownerPool = world.GetPool<ModifierOwnerComponent>();

			if (world.TryGetAsSingleton<PlayerComponent>(out var player) && player.Value != null)
			{
				var playerFilter = world.Filter<PlayerComponent>().Inc<ModifierOwnerComponent>().End();
				foreach (var entity in playerFilter)
					Apply(ref ownerPool.Get(entity), player.Value.transform.position, zones);
			}

			var mobFilter = world.Filter<MobComponent>().Inc<ModifierOwnerComponent>().End();
			foreach (var entity in mobFilter)
			{
				ref var owner = ref ownerPool.Get(entity);
				if (owner.Transform != null)
					Apply(ref owner, owner.Transform.position, zones);
			}
		}

		private static void Apply(ref ModifierOwnerComponent owner, Vector3 position, List<ModifierZone> zones)
		{
			for (int z = 0; z < zones.Count; z++)
			{
				var zone = zones[z];
				if (zone == null || zone.Modifiers == null || !zone.Contains(position))
					continue;

				owner.Modifiers ??= new List<Modifier>();
				foreach (var template in zone.Modifiers)
				{
					if (template == null)
						continue;
					var existing = Find(owner.Modifiers, template);
					if (existing != null)
					{
						existing.Value = template.Value;
						existing.Lifetime = Mathf.Max(existing.Lifetime, zone.Linger);
						continue;
					}
					var copy = template.Clone<Modifier>();
					copy.Lifetime = zone.Linger;
					copy.HasEffect = false; // re-added on every entry — no VFX spam
					owner.Modifiers.Add(copy);
				}
			}
		}

		// Same concrete type + same Id = the same zone modifier.
		private static Modifier Find(List<Modifier> modifiers, Modifier template)
		{
			var type = template.GetType();
			for (int i = 0; i < modifiers.Count; i++)
			{
				var m = modifiers[i];
				if (m != null && m.GetType() == type && m.Id == template.Id)
					return m;
			}
			return null;
		}
	}
}
