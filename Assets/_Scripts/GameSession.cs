// Passes the mode and level chosen in the menu to the gameplay scene.
// static survives SceneManager.LoadScene (incl. restart on player death), so
// EntryPoint sees the same level even after the scene reloads. The value is reset only
// on application exit / domain reload (entering Play Mode).
public static class GameSession
{
	public static LevelDefinition SelectedLevel { get; private set; }
	public static GameMode SelectedMode { get; private set; } = GameMode.Campaign;

	// Compatibility with the old call: select only the level, don't touch the mode.
	public static void Select(LevelDefinition level) => SelectedLevel = level;

	// Select mode and level at the same time (from the main menu).
	public static void Select(GameMode mode, LevelDefinition level)
	{
		SelectedMode = mode;
		SelectedLevel = level;
	}

	public static void Clear() => SelectedLevel = null;
}
