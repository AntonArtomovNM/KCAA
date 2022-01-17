namespace KCAA.Services.TelegramApi
{
    public static class GameMessages
    {
        public static string GreetingsMessage => "Ohayo Pokko";

        public static string LobbyRegistrationMessage => "BOL`SHIE GORODA";

        public static string LobbyJoinedMessage => "You've joined the lobby in {0}";

        public static string LobbyCanceledMessage => "Lobby is canceled";

        public static string GameStartMessage => "Poehali!";

        public static string ChooseResourcesMessage => "Choose resources: {0} " + GameSymbolConstants.Coin + " or {1}" + GameSymbolConstants.Card;

        public static string ChooseActionMessage => "Choose action:";

        public static string CommandOnlyForGroupsError => "This command can only be used in a group chat";

        public static string LobbyAlreadyCreatedError => "The lobby is already created";

        public static string LobbyAlreadyJoinedError => "You've already joined a Citadels lobby";

        public static string LobbyNotFoundError => "This group doesn`t have an active lobby";

        public static string LobbyIsFullError => "Sorry, but lobby is already full";

        public static string NotEnoughPlayersError => "You need at least {0} players to start the game";

        public static string GameIsRunningError => "Game is already running";

        public static string GameNotStartedError => "Game is not yet started";

        public static string IdrakError => "Too late((";

        public static string NotValidLobbyStateError => "Not valid lobby state";

        public static string NotInGameError => "You`re not currently in the game";

        public static string PlayerNotFoundError => "Player not found";

        public static string CharacterNotFoundError => "Character not found";

        public static string NoQuartersToAffordError => "You`re too poor to afford any quarter from hand";

        public static string GetPlayerTurnMessage(int coinAmount, int cardAmount, int score)
        {
            return $@"
Your turn!

{GameSymbolConstants.Coin}: {coinAmount} | {GameSymbolConstants.Card}: {cardAmount} | {GameSymbolConstants.Score}: {score}";
        }

    }
}
