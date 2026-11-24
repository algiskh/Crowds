using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Producer side of ModifierZone (oil slicks, hockey ice, sewer hatches…): the player and every mob standing in a
	/// zone get the zone's modifiers. Each one is added once (clone) and refreshed every frame while inside — Value and
	/// Lifetime = zone Linger — so nothing stacks and ModifiersSystem removes it shortly after the owner steps out.
	/// Consumers stay the usual ones (MoveSystem / PlayerMovementSystem read SpeedModifier and TractionModifier,
	/// ModifiersSystem ticks DamageModifier…). Zones with AffectsMobs = false skip mobs; zones with EntryEffect spawn
	/// the modifier's VFX once per entry, linked to the owner (EffectsSystem keeps it while the modifier lasts).
	/// Registered right before ModifiersSystem. No-op without zones.
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
					Apply(world, entity, ref ownerPool.Get(entity), player.Value.transform.position, zones, isMob: false);
			}

			var mobFilter = world.Filter<MobComponent>().Inc<ModifierOwnerComponent>().End();
			foreach (var entity in mobFilter)
			{
				ref var owner = ref ownerPool.Get(entity);
				if (owner.Transform != null)
					Apply(world, entity, ref owner, owner.Transform.position, zones, isMob: true);
			}
		}

		private static void Apply(EcsWorld world, int entity, ref ModifierOwnerComponent owner, Vector3 position,
			List<ModifierZone> zones, bool isMob)
		{
			for (int z = 0; z < zones.Count; z++)
			{
				var zone = zones[z];
				if (zone == null || zone.Modifiers == null || (isMob && !zone.AffectsMobs) || !zone.Contains(position))
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
					copy.HasEffect = false; // ModifiersSystem never sees it as a request — the VFX, if any, is ours
					owner.Modifiers.Add(copy);

					if (zone.EntryEffect && template.HasEffect && !string.IsNullOrEmpty(template.EffectId))
						RequestLinkedEffect(world, entity, owner.Transform, copy, template.EffectId);
				}
			}
		}

		// One VFX per entry, linked like ModifiersSystem does: alive and following the owner while the modifier lasts.
		private static void RequestLinkedEffect(EcsWorld world, int entity, Transform parent, Modifier modifier, string effectId)
		{
			if (!world.TryGetAsSingleton<EffectsHolderComponent>(out var holder) || holder.Value == null
			    || holder.Value.GetEffect(effectId) == null)
				return;
			ref var request = ref world.CreateSimpleEntity<RequestEffectComponent>();
			request.EffectId = effectId;
			request.Parent = parent;
			request.DamageType = modifier is DamageModifier dmg ? dmg.Type : DamageType.Unknown;
			request.HasModifierLink = true;
			request.ModifierOwner = world.PackEntity(entity);
			request.LinkedModifier = modifier;
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
