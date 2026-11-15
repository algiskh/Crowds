using Leopotam.EcsLite;

namespace ECS
{
	/// <summary>
	/// Feeds the level's seven-segment scoreboards (<see cref="ArenaScoreboard"/>, e.g. on the hockey stand):
	/// home = player deaths (1 once the fail sequence has started), away = frags (FragCountComponent).
	/// "0:81" while alive, "1:81" after death. Does nothing on levels without a scoreboard.
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

			for (int i = 0; i < boards.Count; i++)
				boards[i].SetScore(deaths, frags);
		}
	}
}
