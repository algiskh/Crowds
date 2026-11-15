using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Pickable bonuses (speed up, shield) on top of the shared modifier system.
	/// Picking up Bonus loot emits RequestApplyBonusComponent (see CollisionSystem); here we:
	///  1) apply the configured modifier to the player (with "refresh" semantics — one bonus per type);
	///  2) every frame drive the progress in PlayerStats (bar = remaining-time fraction, caption = seconds),
	///     removing expired bonuses. The Lifetime itself ticks in ModifiersSystem — we only read it.
	/// </summary>
	public class BonusSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			ref var playerStats = ref world.GetAsSingleton<PlayerStatsComponent>();
			ref var activeBonuses = ref world.GetAsSingleton<ActiveBonusesComponent>();
			ref var effectPool = ref world.GetAsSingleton<EffectPoolComponent>();
			var effectsHolder = world.GetAsSingleton<EffectsHolderComponent>();

			if (activeBonuses.Value == null)
				activeBonuses.Value = new List<ActiveBonus>();

			var modifierPool = world.GetPool<ModifierOwnerComponent>();
			var requestPool = world.GetPool<RequestApplyBonusComponent>();
			int playerEntity = player.Value.Entity;

			#region Apply pickup requests
			foreach (var entity in world.Filter<RequestApplyBonusComponent>().End())
			{
				ref var request = ref requestPool.Get(entity);
				ApplyBonus(mainHolder.Value, playerEntity, modifierPool,
					ref activeBonuses, ref effectPool, effectsHolder.Value, request.ConfigId);
				world.DelEntity(entity);
			}
			#endregion

			#region Drive UI / prune expired
			var list = activeBonuses.Value;
			for (int i = list.Count - 1; i >= 0; i--)
			{
				var bonus = list[i];
				float remaining = bonus.Modifier != null ? bonus.Modifier.Lifetime : 0f;

				if (remaining <= 0f)
				{
					playerStats.Value.ClearBonus(bonus.Type);
					// The accompanying VFX ended together with the bonus — return it to the pool.
					if (bonus.Effect != null)
						effectPool.Pool(bonus.Effect);
					list.RemoveAt(i);
					continue;
				}

				float fraction = bonus.TotalDuration > 0f
					? Mathf.Clamp01(remaining / bonus.TotalDuration)
					: 1f;
				playerStats.Value.SetBonus(bonus.Type, fraction, remaining);
			}
			#endregion
		}

		private void ApplyBonus(MainHolder mainHolder, int playerEntity,
			EcsPool<ModifierOwnerComponent> modifierPool, ref ActiveBonusesComponent activeBonuses,
			ref EffectPoolComponent effectPool, EffectsHolder effectsHolder, string configId)
		{
			var holder = mainHolder.BonusConfigHolder;
			if (holder == null)
				return;

			var config = string.IsNullOrEmpty(configId) ? holder.Default : holder.GetConfig(configId);
			if (config == null)
				return;

			var modifier = config.CreateModifierInstance();
			if (modifier == null)
			{
				Debug.LogError($"BonusConfig '{config.Id}' has no modifier assigned.");
				return;
			}

			if (!modifierPool.Has(playerEntity))
				return;

			ref var modifierOwner = ref modifierPool.Get(playerEntity);
			if (modifierOwner.Modifiers == null)
				modifierOwner.Modifiers = new List<Modifier>();

			// Refresh: remove the already active bonus of the same type (its modifier + record).
			// Don't reset the bar — a new bonus of the same type redraws it right away in the UI pass.
			// Don't recreate the accompanying VFX, REUSE it — so there are no duplicates on
			// picking up the same type again. Touch the pool only when the bonus expires.
			SceneEffect reusedEffect = null;
			var list = activeBonuses.Value;
			for (int i = list.Count - 1; i >= 0; i--)
			{
				if (list[i].Type != config.Type)
					continue;
				modifierOwner.Modifiers.Remove(list[i].Modifier);
				if (list[i].Effect != null)
					reusedEffect = list[i].Effect;
				list.RemoveAt(i);
			}

			modifierOwner.Modifiers.Add(modifier);

			// Player-accompanying VFX: reuse from the previous bonus of the same type,
			// otherwise take from the pool and parent to the player — the effect rides with them the whole duration.
			SceneEffect effect = reusedEffect;
			if (effect == null && modifier.HasEffect && !string.IsNullOrEmpty(modifier.EffectId))
				effect = SpawnFollowEffect(ref effectPool, effectsHolder, modifier.EffectId, modifierOwner.Transform);

			list.Add(new ActiveBonus
			{
				Type = config.Type,
				Modifier = modifier,
				TotalDuration = modifier.Lifetime,
				Effect = effect
			});
		}

		/// <summary>
		/// Pops a VFX from the effect pool and parents it to the player, so it accompanies the character
		/// for the whole bonus duration. BonusSystem returns it to the pool when the bonus expires.
		/// </summary>
		private SceneEffect SpawnFollowEffect(ref EffectPoolComponent effectPool,
			EffectsHolder effectsHolder, string effectId, Transform player)
		{
			if (effectsHolder == null || player == null)
				return null;

			var wrapper = effectsHolder.GetEffect(effectId);
			if (wrapper == null)
				return null;

			var effect = effectPool.SpawnFromPool(wrapper);
			if (effect == null)
				return null;

			effect.SetParent(player);
			effect.transform.localPosition = Vector3.zero;
			effect.transform.localEulerAngles = Vector3.zero;
			effect.Show();
			return effect;
		}
	}
}
