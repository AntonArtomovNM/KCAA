using KCAA.Models.MongoDB;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KCAA.Helpers
{
    public static class GameMessages
    {
        public static string GreetingsMessage => @"Ohayo Pokko 🐴
 • Use /rules to see basic rules of the game
 • Use /help to see all of the bot commands";

        public static string LobbyRegistrationMessage => "BOL`SHIE GORODA";

        public static string LobbyJoinedMessage => "You've joined the lobby in <b>{0}</b>";

        public static string GameCanceledMessage => "Game is canceled";

        public static string ChooseResourcesMessage => $"Choose resources: coins {GameSymbols.Coin} or quarter cards {GameSymbols.Card}";

        public static string ChooseActionMessage => "Choose action:";

        public static string ReplyButtonsMessage => "• Press <b>My-Hand</b> button to view all your cards and coins\n• Press <b>Table</b> button to view other players' cities";

        public static string CharactersRemovedMessage => "won't appear this round";

        public static string PlayerTurnPublicMessage => "<b>{0}'s</b> turn as <b>{1}</b>";

        public static string CrownMessage => "[{0}] <b>{1}</b> have received the crown\nThey will be the first to select a character next time";

        public static string KilledPersonalMessage => "You got killed(\nNo turn for you";

        public static string KilledPublicMessage => "[{0}] <b>{1}</b> decided to kill <b>{2}</b>";

        public static string RobbedPersonalMessage => "You got robbed(\nNo coins for you";

        public static string RobbedPublicMessage => "[{0}] <b>{1}</b> decided to steal from <b>{2}</b>";

        public static string ExchangedPersonalMessage => "Your cards in hand have been exchanged with <b>{0}'s</b> hand";

        public static string ExchangedPublicMessage => "[{0}] <b>{1}</b> exchanged their hand with <b>{2}'s</b> hand";

        public static string DestroyedPersonalMessage => "Your <b>{0}</b> have been destroyed by <b>{1}</b>";

        public static string DestroyedPublicMessage => "[{0}] <b>{1}</b> have destroyed <b>{2}'s {3}</b>";

        public static string QuarterBuiltMessage => "[{0}] <b>{1}</b> have built a <b>{2}</b>";

        public static string CityBuiltPublicMessage => "Player <b>{0}</b> have completed their city{1}🎉\nNow their quarters cannot be destroyed";

        public static string CityBuiltPersonalMessage => "You've got <b>{0} bonus points</b> for completing the city{1}";

        public static string GameEndedMessage => "The game has ended";

        public static string WinnerMessage => "💃🏻{0} have won!🕺\n\nThe scoreboard🏆:";

        public static string FarewellMessage => "Thanks for playing!";

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

        public static string LobbyOrPlayerNotFoundError => "Error occurred during player or lobby retrieval";

        public static string NoQuartersToAffordError => "You`re too poor to afford any quarter from hand";

        public static string AlreadyPlacedQuarterError => "You've already built a {0}";

        public static string NoPlayersForActionError => "There is no players suitable for this action";

        public static string PlayerBorders => "=========================";

        public static string BasicRules => $@"<u>On your turn</u>

<b>1) Gather resources:</b>
You must gather resources in 1 of 2 ways:
  • Take 2 coins {GameSymbols.Coin} from the bank
  • Draw 1 quarter card { GameSymbols.Card }

<b>2) Build:</b>
You may build 1 quarter by paying its cost.

<b>*) Use other abilities:</b>
You may use each of your character’s and special card's abilities according to it's description on the card

<u>Quarter types</u>

🟨 Noble
🟦 Religious
🟩 Trade
🟥 Military
🟪 Special [WIP]

<u>Scoring</u>

When a city has <b>7 quarters</b>, the game ends after the current round, and you score points:
  • <b>1 point</b> per coin on your placed quarters.
  • <b>4 points</b> for the first player who completed their city.
  • <b>2 points</b> for any other player who completed their city.";

        public static string GetPlayerCharactersInfo(IEnumerable<Character> characters, Player player, bool loadNames = true)
        { 
            var builder = new StringBuilder();

            if (player.HasCrown)
            {
                builder.Append(GameSymbols.Crown);
            }

            if (characters.Any())
            {
                builder.AppendLine();

                builder.Append($"{GameSymbols.Character}: ");

                builder.Append(string.Join(", ", characters.Select(c => $"{GetCharacterDisplayNameAndEffect(c, loadNames)}")));
            }

            return builder.ToString();
        }

        public static string GetPlayerInfoMessage(Player player)
        {
            var builder = new StringBuilder();
            var placedAmount = player.PlacedQuarters.Count;
            var score = player.Score;

            builder.Append($"{GameSymbols.Coin}: {player.Coins}");
            builder.Append($" | {GameSymbols.Card}: {player.QuarterHand.Count}");
            if (placedAmount != 0)
            {
                builder.Append($" | {GameSymbols.PlacedQuarter}: {placedAmount}");
            }
            if (score != 0)
            {
                builder.Append($" | {GameSymbols.Score}: {score}");
            }

            return builder.ToString();
        }

        private static string GetCharacterDisplayNameAndEffect(Character character, bool loadName = true)
        {
            if (character.Status == CharacterStatus.Selected)
            {
                return loadName ? character.CharacterBase.DisplayName : GameSymbols.UnknowCharacter;
            }

            var displayName = character.Status == CharacterStatus.Playing ? 
                $"<u>{character.CharacterBase.DisplayName}</u>" :
                $"<s>{character.CharacterBase.DisplayName}</s>";

            var effectSymbol = character.Effect switch
            {
                CharacterEffect.Killed => GameSymbols.Killed,
                CharacterEffect.Robbed => GameSymbols.Robbed,
                _ => string.Empty
            };

            return displayName + effectSymbol;
        }
    }
}
