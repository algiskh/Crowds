// Верхнеуровневый режим игры, выбираемый в главном меню.
// Campaign — последовательные сюжетные уровни, Survival — выживание/счёт на очки.
// Прокидывается в геймплейную сцену через GameSession; уровни для каждого режима
// берутся из своей LevelLibrary в MainMenuController.
public enum GameMode
{
    Campaign = 0,
    Survival = 1,
}
