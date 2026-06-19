// Передаёт выбранный в меню уровень в геймплейную сцену.
// static переживает SceneManager.LoadScene (в т.ч. рестарт по гибели игрока), поэтому
// EntryPoint видит тот же уровень и после перезагрузки сцены. Значение сбрасывается только
// при выходе из приложения / domain reload (вход в Play Mode).
public static class GameSession
{
	public static LevelDefinition SelectedLevel { get; private set; }

	public static void Select(LevelDefinition level) => SelectedLevel = level;

	public static void Clear() => SelectedLevel = null;
}
