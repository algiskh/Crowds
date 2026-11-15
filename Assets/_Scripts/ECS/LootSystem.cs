using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class LootSystem : IEcsInitSystem, IEcsRunSystem
	{
		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var lootPool = world.GetPool<RequestLootSpawn>();
			var mapLootPool = world.GetAsSingleton<MapLootPoolComponent>();

			foreach (var loot in mapLootPool.Value)
			{
				var lootEntity = world.NewEntity();
				ref var request = ref lootPool.Add(lootEntity);
				request.Position = loot.transform.position;
				request.SourceEntity = -1;
				request.PossibleLoots = new[]
				{
					new PossibleLoot
					{
						LootType = loot.LootComponent.LootType,
						Count = loot.LootComponent.Count,
						Id = loot.LootComponent.Id,
						AmmoCaliber = loot.LootComponent.AmmoCaliber,
						Chance = 1f
					}
				};
				request.Source = RequestSpawnSource.MapLoot;

				loot.gameObject.SetActive(false);
			}
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			var requestLootSpawnPool = world.GetPool<RequestLootSpawn>();
			ref var lootMainPool = ref world.GetAsSingleton<LootPoolComponent>();
			ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
			ref var navmeshManager = ref world.GetAsSingleton<NavMeshManagerComponent>();
			var lootPool = world.GetPool<LootComponent>();
			var collisionPool = world.GetPool<ColliderComponent>();
			var disposablePool = world.GetPool<DisposableComponent>();
			var currentSectorPool = world.GetPool<CurrentSectorComponent>();
			var lookerPool = world.GetPool<LookerAtCamera>();
			var lifetimePool = world.GetPool<LifeTimeComponent>();

			#region CheckingDisposed
			// Check disposed loots and return them to the pool
			var disposedFilter = world.Filter<LootComponent>().Inc<DisposableComponent>().End();
			foreach (var disposedEntity in disposedFilter)
			{
				ref var loot = ref lootPool.Get(disposedEntity);
				ref var disposable = ref disposablePool.Get(disposedEntity);
				if (disposable.IsDisposed)
				{
					loot.Loot.gameObject.SetActive(false);
					lootMainPool.Value.Push(loot.Loot);

					//Request Effect
					ref var effectRequest = ref world.CreateSimpleEntity<RequestEffectComponent>();
					effectRequest.EffectId = loot.LootType == LootType.Health ? "collectHealth" : "collect";
					effectRequest.Position = loot.Loot.transform.position;

					world.DelEntity(disposedEntity); // delete entity
				}
			}
			#endregion

			#region CountingLifeTime
			// Mob-dropped loot carries a LifeTimeComponent. When it runs out, the loot is
			// silently returned to the pool (no "collect" effect — it wasn't picked up).
			var lifetimeFilter = world.Filter<LootComponent>().Inc<LifeTimeComponent>().Inc<DisposableComponent>().End();
			foreach (var lootEntity in lifetimeFilter)
			{
				ref var disposable = ref disposablePool.Get(lootEntity);
				if (disposable.IsDisposed)
					continue; // already picked up this frame; handled by CheckingDisposed above

				ref var lifetime = ref lifetimePool.Get(lootEntity);
				lifetime.Value -= world.GameDeltaTime();
				if (lifetime.Value <= 0)
				{
					ref var loot = ref lootPool.Get(lootEntity);
					loot.Loot.gameObject.SetActive(false);
					lootMainPool.Value.Push(loot.Loot);
					world.DelEntity(lootEntity);
				}
				else
				{
					// Pulse the icon toward the warning color over the last seconds before despawn.
					float warningTime = mainHolder.Value.LootDespawnWarningTime;
					if (warningTime > 0 && lifetime.Value <= warningTime)
					{
						ref var loot = ref lootPool.Get(lootEntity);
						// Sine over the remaining time → smooth 0..1 ping-pong, fully self-contained.
						float pulse = 0.5f * (1f + Mathf.Sin(lifetime.Value * mainHolder.Value.LootDespawnWarningPulseSpeed));
						loot.Loot.SetWarningTint(mainHolder.Value.LootDespawnWarningColor, pulse);
					}
				}
			}
			#endregion

			#region HandlingRequests
			var filter = world.Filter<RequestLootSpawn>().End();
			foreach (var entity in filter)
			{
				ref var requestLootSpawn = ref requestLootSpawnPool.Get(entity);

				// Snapshot the request fields into locals: loot spawn creates new entities and
				// may invalidate the requestLootSpawn reference (pool resize).
				var requestSource = requestLootSpawn.Source;
				var requestSourceEntity = requestLootSpawn.SourceEntity;

				var possibleLoots = requestLootSpawn.PossibleLoots;
				PossibleLoot selectedLoot = null;

				// Level weapon restriction (LevelConfig.AllowedMobWeaponDrops): banned weapon drops roll as "no loot".
				if (requestSource == RequestSpawnSource.Mob && possibleLoots != null)
					possibleLoots = FilterMobWeaponDrops(world, possibleLoots);

				if (possibleLoots.Length > 1)
				{
					float totalChance = 0f;
					for (int i = 0; i < possibleLoots.Length; i++)
						totalChance += possibleLoots[i].Chance;

					// If the total chance < 1, normalize to 1: roll the "no loot" possibility.
					float rollRange = Mathf.Max(totalChance, 1f);
					float roll = Random.value * rollRange;

					if (roll > totalChance)
					{
						// Rolled the "empty" sector outside the total chance. No loot appeared, but
						// the AdditionalSpawn observer must learn the result, otherwise it hangs forever
						// in Requesting (hence "no loot when the condition is met").
						EmitSpawnResult(world, requestSource, requestSourceEntity, -1);
						world.DelEntity(entity);
						continue;
					}

					// Cumulative pick: the first segment covering the roll.
					float acc = 0f;
					for (int i = 0; i < possibleLoots.Length; i++)
					{
						acc += possibleLoots[i].Chance;
						if (roll <= acc)
						{
							selectedLoot = possibleLoots[i];
							break;
						}
					}
					// Numerical-drift safeguard — usually doesn't trigger.
					if (selectedLoot == null)
						selectedLoot = possibleLoots[possibleLoots.Length - 1];
					if (selectedLoot == BannedDrop)
						selectedLoot = null;
				}
				else if (possibleLoots != null && possibleLoots.Length > 0)
				{
					selectedLoot = possibleLoots[0];
				}

				if (selectedLoot != null)
				{
					Loot loot;
					if (lootMainPool.Value != null &&
						lootMainPool.Value.Count > 0)
					{
						loot = lootMainPool.Value.Pop();
					}
					else
					{
						loot = Object.Instantiate( // Fixed ambiguous reference
							mainHolder.Value.LootPrefab,
							lootMainPool.Parent);
					}

					// Pooled loot may carry a leftover warning tint from a previous life — clear it.
					loot.ResetColor();

					var lootEntity = world.NewEntity();
					ref var lootComponent = ref lootPool.Add(lootEntity);
					ref var collisionComponent = ref collisionPool.Add(lootEntity);
					ref var disposableComponent = ref disposablePool.Add(lootEntity);

					// Ensure LootComponent has LootType and Value properties
					lootComponent.LootType = selectedLoot.LootType;
					loot.SetAuraColor(LootAura.ColorFor(selectedLoot.LootType));
					lootComponent.Count = selectedLoot.Count;
					lootComponent.Loot = loot;
					lootComponent.Id = selectedLoot.Id;
					lootComponent.PickupHintShown = false;
					// Ammo caliber is fixed at spawn: an unassigned (None) ammo loot becomes the
					// current weapon's caliber (or the holder's first), so its icon and pickup match.
					var resolvedAmmoCaliber = selectedLoot.AmmoCaliber;
					if (selectedLoot.LootType == LootType.Ammo && resolvedAmmoCaliber == Caliber.None)
						resolvedAmmoCaliber = ResolveAmmoCaliber(world, mainHolder.Value);
					lootComponent.AmmoCaliber = resolvedAmmoCaliber;

					if (loot.SpriteLooker != null)
					{
						ref var lookerComponent = ref lookerPool.Add(lootEntity);
						lookerComponent.Transform = loot.SpriteLooker.transform;
						lookerComponent.FlatBillboard = true;

						Sprite sprite;
						switch (lootComponent.LootType)
						{
							case LootType.Weapon:
								sprite = mainHolder.Value.GunConfigHolder.GetConfig(selectedLoot.Id).Preview;
								break;
							case LootType.Grenade:
								var grenadeCfg = mainHolder.Value.GrenadeConfigHolder != null
									? (string.IsNullOrEmpty(selectedLoot.Id)
										? mainHolder.Value.GrenadeConfigHolder.Default
										: mainHolder.Value.GrenadeConfigHolder.GetConfig(selectedLoot.Id))
									: null;
								sprite = grenadeCfg != null && grenadeCfg.Preview != null
									? grenadeCfg.Preview
									: mainHolder.Value.SpriteHolder.GetSpriteById(selectedLoot.LootType.ToString());
								break;
							case LootType.Ammo:
								var ammoCfg = mainHolder.Value.AmmoConfigHolder != null
									? mainHolder.Value.AmmoConfigHolder.GetConfig(resolvedAmmoCaliber)
									: null;
								sprite = ammoCfg != null && ammoCfg.LootIcon != null
									? ammoCfg.LootIcon
									: mainHolder.Value.SpriteHolder.GetSpriteById(selectedLoot.LootType.ToString());
								break;
							case LootType.Bonus:
								var bonusCfg = mainHolder.Value.BonusConfigHolder != null
									? (string.IsNullOrEmpty(selectedLoot.Id)
										? mainHolder.Value.BonusConfigHolder.Default
										: mainHolder.Value.BonusConfigHolder.GetConfig(selectedLoot.Id))
									: null;
								sprite = bonusCfg != null && bonusCfg.Preview != null
									? bonusCfg.Preview
									: mainHolder.Value.SpriteHolder.GetSpriteById(selectedLoot.LootType.ToString());
								break;
							default:
								sprite = mainHolder.Value.SpriteHolder.GetSpriteById(selectedLoot.LootType.ToString());
								break;
						}

						loot.SetSprite(sprite);
					}

					// Mob-dropped loot despawns after a per-type, configurable timer.
					// Loot from other sources (map loot, additional spawns) stays until picked up.
					if (requestLootSpawn.Source == RequestSpawnSource.Mob)
					{
						float lifetime = mainHolder.Value.GetMobLootLifetime(lootComponent.LootType);
						if (lifetime > 0)
						{
							ref var lifetimeComponent = ref lifetimePool.Add(lootEntity);
							lifetimeComponent.Value = lifetime;
						}
					}

					disposableComponent.IsDisposed = false;
					loot.gameObject.SetActive(true);
					loot.transform.position = requestLootSpawn.Position;

					collisionComponent.CollisionType = CollisionType.Loot;
					//collisionComponent.Radius = mainHolder.Value.DefaultCollisionRadius;

					EmitSpawnResult(world, requestSource, requestSourceEntity, lootEntity);
				}
				else
				{
					// No loot selected (empty table) — still report the result to the observer.
					EmitSpawnResult(world, requestSource, requestSourceEntity, -1);
				}
				world.DelEntity(entity); // delete request entity
			}
			#endregion
		}

		// Zero-chance stand-in for a banned weapon drop: keeps the table length (single-entry tables are taken
		// unconditionally) while the cumulative roll can never land on it.
		private static readonly PossibleLoot BannedDrop = new() { LootType = LootType.Ammo, Chance = 0f };

		/// <summary>
		/// Applies the level's mob-loot rules (LevelConfig.IsMobLootAllowed: weapon whitelist, banned ammo calibers,
		/// banned bonus ids): forbidden entries get chance 0 (rolled as "no loot").
		/// Returns the original array (no allocation) when nothing is banned.
		/// </summary>
		private static PossibleLoot[] FilterMobWeaponDrops(EcsWorld world, PossibleLoot[] loots)
		{
			if (!world.TryGetAsSingleton<CurrentLevelConfigComponent>(out var level) || level.Value == null)
				return loots;

			PossibleLoot[] filtered = null;
			for (int i = 0; i < loots.Length; i++)
			{
				var loot = loots[i];
				if (loot == null || level.Value.IsMobLootAllowed(loot))
					continue;
				filtered ??= (PossibleLoot[])loots.Clone();
				filtered[i] = BannedDrop;
			}
			if (filtered == null)
				return loots;
			// A single banned entry would be taken unconditionally (no roll for length 1) — drop nothing instead.
			return filtered.Length == 1 ? System.Array.Empty<PossibleLoot>() : filtered;
		}

		// Reports the spawn result back to the requesting AdditionalSpawn observer.
		// The event is needed only by this source (the only consumer), so for other
		// sources we create nothing — otherwise events would pile up without being deleted.
		// lootEntity < 0 means "no loot appeared" (empty drop-table roll).
		private void EmitSpawnResult(EcsWorld world, RequestSpawnSource source, int sourceEntity, int lootEntity)
		{
			if (source != RequestSpawnSource.AdditionalSpawn)
				return;

			ref var finishEvent = ref world.CreateSimpleEntity<LootSpawnedEventComponent>();
			finishEvent.Source = source;
			finishEvent.SourceEntity = sourceEntity;
			finishEvent.LootEntity = lootEntity;
		}

		// Caliber for an unassigned ammo loot, decided at spawn: current weapon's caliber,
		// or the first AmmoConfig in the holder when there's no weapon.
		private Caliber ResolveAmmoCaliber(EcsWorld world, MainHolder mainHolder)
		{
			if (world.TryGetAsSingleton(out WeaponComponent weapon)
				&& weapon.GunConfig != null
				&& weapon.GunConfig.Caliber != Caliber.None)
				return weapon.GunConfig.Caliber;

			var firstAmmo = mainHolder.AmmoConfigHolder != null ? mainHolder.AmmoConfigHolder.First : null;
			return firstAmmo != null ? firstAmmo.Caliber : Caliber.None;
		}
	}
}
