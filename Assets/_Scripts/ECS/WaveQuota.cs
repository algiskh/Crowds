using System.Collections.Generic;
using Leopotam.EcsLite;

namespace ECS
{
	/// <summary>
	/// Runtime side of DifficultyStage.MobQuota ("wave of exactly N mobs"): a WaveQuotaComponent singleton holding how
	/// many mobs of each id are still to spawn in the current stage. <see cref="Sync"/> rebuilds it when the stage
	/// changes (called by SpawnPointSystem, MobsClearedCondition, MissionSystem — whoever runs first).
	/// See Docs/HockeyLevelFeature.md.
	/// </summary>
	public static class WaveQuota
	{
		/// <summary>Brings the quota up to date with <paramref name="stage"/>. Returns false when the stage has no quota.</summary>
		public static bool Sync(EcsWorld world, DifficultyStage stage)
		{
			if (stage == null || !stage.HasMobQuota)
				return false;

			if (!world.TryGetAsSingleton<WaveQuotaComponent>(out _))
				world.CreateSimpleEntity<WaveQuotaComponent>();
			ref var quota = ref world.GetAsSingleton<WaveQuotaComponent>();
			if (quota.Stage == stage && quota.Remaining != null)
				return true;

			quota.Stage = stage;
			quota.Remaining ??= new Dictionary<string, int>();
			quota.Remaining.Clear();
			quota.RemainingTotal = 0;
			foreach (var entry in stage.MobQuota)
			{
				if (entry == null || string.IsNullOrEmpty(entry.MobId) || entry.Count <= 0)
					continue;
				quota.Remaining.TryGetValue(entry.MobId, out int count);
				quota.Remaining[entry.MobId] = count + entry.Count;
				quota.RemainingTotal += entry.Count;
			}
			return true;
		}

		/// <summary>Mobs still to spawn in <paramref name="stage"/> (0 for stages without a quota).</summary>
		public static int RemainingTotal(EcsWorld world, DifficultyStage stage)
		{
			if (!Sync(world, stage))
				return 0;
			return world.GetAsSingleton<WaveQuotaComponent>().RemainingTotal;
		}

		public static bool HasLeft(in WaveQuotaComponent quota, string mobId) =>
			quota.Remaining != null && quota.Remaining.TryGetValue(mobId, out int count) && count > 0;

		public static void Spend(ref WaveQuotaComponent quota, string mobId)
		{
			if (quota.Remaining == null || !quota.Remaining.TryGetValue(mobId, out int count) || count <= 0)
				return;
			quota.Remaining[mobId] = count - 1;
			quota.RemainingTotal--;
		}
	}
}
