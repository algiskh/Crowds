using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Feeds the level's seven-segment scoreboards (<see cref="ArenaScoreboard"/>, e.g. on the hockey stand):
	/// home = player deaths (1 once the fail sequence has started), away = frags (FragCountComponent).
	/// "0:81" while alive, "1:81" after death. During a breather (a timed stage with the timer on screen and no mob
	/// quota) the away side shows the countdown in whole seconds instead of the frags, while the player is alive.
	/// Does nothing on levels without a scoreboard.
	/// </summary>
	public sealed class ScoreboardSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var boards = ArenaScoreboard.Active;
			if (boards.Count == 0)
				return;

			var world = systems.GetWorld();
			int frags = world.TryGetAsSingleton<FragCountComponent>(out var fragCount) ? fragCount.Value : 0;
			int deaths = world.TryGetAsSingleton<FailSequenceComponent>(out var fail) &&
			             fail.Phase != FailSequencePhase.Inactive ? 1 : 0;

			int away = frags;
			if (deaths == 0 && world.TryGetAsSingleton<DifficultyComponent>(out var difficulty) && IsBreather(difficulty.Stage))
				away = Mathf.CeilToInt(Mathf.Max(0f, difficulty.DifficultyTimer));

			for (int i = 0; i < boards.Count; i++)
				boards[i].SetScore(deaths, away);
		}

		// Breather = rest stage between waves: timer shown, no quota, no end conditions (ends on the timer).
		private static bool IsBreather(DifficultyStage stage) =>
			stage != null && stage.ShowTimer && !stage.HasMobQuota && !stage.HasEndConditions;
	}
}
