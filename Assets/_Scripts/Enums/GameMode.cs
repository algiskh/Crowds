// Top-level game mode, chosen in the main menu.
// Campaign — sequential story levels, Survival — survival/score attack.
// Passed to the gameplay scene via GameSession; levels for each mode
// are taken from their own LevelLibrary in MainMenuController.
public enum GameMode
{
    Campaign = 0,
    Survival = 1,
}
