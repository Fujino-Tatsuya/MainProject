public enum NetworkLoadingPhase : byte
{
    Idle = 0,
    LoadingScene = 1,
    LoadingGame = 2,
    WaitingForPlayers = 3,
    Ready = 4,
    Activating = 5,
    Completed = 6
}
