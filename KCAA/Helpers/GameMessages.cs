﻿using KCAA.Models.MongoDB;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KCAA.Helpers
{
    public static class GameMessages
    {
        public static string GreetingsMessage => "Ohayo Pokko";

        public static string LobbyRegistrationMessage => "BOL`SHIE GORODA";

        public static string LobbyJoinedMessage => "You've joined the lobby in {0}";

        public static string GameCanceledMessage => "Game is canceled";

        public static string GameStartMessage => "Poehali!";

        public static string ChooseResourcesMessage => $"Choose resources: coins {GameSymbols.Coin} or quarter cards {GameSymbols.Card}";

        public static string ChooseActionMessage => "Choose action:";

        public static string MyHandMessage => "Press \"My-Hand\" button to view all your cards and coins";

        public static string KilledMessage => "You got killed(\nNo turn for you";

        public static string RobbedMessage => "You got robbed(\nNo coins for you";

        public static string ExchangedMessage => "Your cards in hand have been exchanged with {0}'s hand";

        public static string DestroyedMessage => "Your {0} have been destroyed by {1}";

        public static string CityBuiltMessage => "Player {0} have completed their city🎉\nNow their quarters cannot be destroyed";

        public static string GameEndedMessage => "The game has ended";

        public static string WinnerMessage => "💃🏻{0} have won!🕺";

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

        public static string MyHandClose => $"Close {GameSymbols.Close}";

        public static string GetPlayerCharactersInfo(IEnumerable<Character> characters, Player player)
        { 
            var builder = new StringBuilder();

            if (player.HasCrown)
            {
                builder.Append(GameSymbols.Crown);
            }

            builder.AppendLine();

            if (characters.Any())
            {
                builder.Append($"{GameSymbols.Character}: ");

                builder.Append(string.Join(", ", characters.Select(c => $"{GetCharacterDisplayName(c)}{GetCharacterEffectSymbol(c.Effect)}")));
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

        private static string GetCharacterDisplayName(Character character)
        {
            var displayName = character.CharacterBase.DisplayName;

            return character.Status == CharacterStatus.Selected ? displayName : $"<s>{displayName}</s>";
        }

        private static string GetCharacterEffectSymbol(CharacterEffect effect)
        {
            return effect switch
            {
                CharacterEffect.Killed => GameSymbols.Killed,
                CharacterEffect.Robbed => GameSymbols.Robbed,
                _ => string.Empty
            };
        }
    }
}
