// Передаёт выбранный в меню режим и уровень в геймплейную сцену.
// static переживает SceneManager.LoadScene (в т.ч. рестарт по гибели игрока), поэтому
// EntryPoint видит тот же уровень и после перезагрузки сцены. Значение сбрасывается только
// при выходе из приложения / domain reload (вход в Play Mode).
public static class GameSession
{
	public static LevelDefinition SelectedLevel { get; private set; }
	public static GameMode SelectedMode { get; private set; } = GameMode.Campaign;

	// Совместимость со старым вызовом: выбрать только уровень, режим не трогаем.
	public static void Select(LevelDefinition level) => SelectedLevel = level;

	// Выбрать режим и уровень одновременно (из главного меню).
	public static void Select(GameMode mode, LevelDefinition level)
	{
		SelectedMode = mode;
		SelectedLevel = level;
	}

	public static void Clear() => SelectedLevel = null;
}
