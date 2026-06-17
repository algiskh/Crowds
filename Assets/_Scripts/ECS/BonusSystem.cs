using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Подбираемые бонусы (speed up, shield) поверх общей системы модификаторов.
	/// Подбор Bonus-лута эмитит RequestApplyBonusComponent (см. CollisionSystem); здесь мы:
	///  1) применяем сконфигурированный модификатор игроку (с семантикой "refresh" — один бонус на тип);
	///  2) каждый кадр гоним прогресс в PlayerStats (бар = доля оставшегося времени, подпись = секунды),
	///     убирая протухшие бонусы. Сам Lifetime тикает в ModifiersSystem — здесь только читаем.
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

			if (activeBonuses.Value == null)
				activeBonuses.Value = new List<ActiveBonus>();

			var modifierPool = world.GetPool<ModifierOwnerComponent>();
			var requestPool = world.GetPool<RequestApplyBonusComponent>();
			int playerEntity = player.Value.Entity;

			#region Apply pickup requests
			foreach (var entity in world.Filter<RequestApplyBonusComponent>().End())
			{
				ref var request = ref requestPool.Get(entity);
				ApplyBonus(world, mainHolder.Value, playerEntity, modifierPool, ref activeBonuses, request.ConfigId);
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

		private void ApplyBonus(EcsWorld world, MainHolder mainHolder, int playerEntity,
			EcsPool<ModifierOwnerComponent> modifierPool, ref ActiveBonusesComponent activeBonuses, string configId)
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

			// Refresh: убираем уже активный бонус того же типа (его модификатор + запись).
			// Бар не сбрасываем — новый бонус того же типа тут же его перерисует в UI-проходе.
			var list = activeBonuses.Value;
			for (int i = list.Count - 1; i >= 0; i--)
			{
				if (list[i].Type != config.Type)
					continue;
				modifierOwner.Modifiers.Remove(list[i].Modifier);
				list.RemoveAt(i);
			}

			modifierOwner.Modifiers.Add(modifier);
			list.Add(new ActiveBonus
			{
				Type = config.Type,
				Modifier = modifier,
				TotalDuration = modifier.Lifetime
			});

			// Опциональный VFX подбора (как в ModifiersSystem для модификаторов с эффектом).
			if (modifier.HasEffect && !string.IsNullOrEmpty(modifier.EffectId))
			{
				ref var fx = ref world.CreateSimpleEntity<RequestEffectComponent>();
				fx.EffectId = modifier.EffectId;
				fx.Parent = modifierOwner.Transform;
				fx.ModifierEntity = playerEntity;
			}
		}
	}
}
