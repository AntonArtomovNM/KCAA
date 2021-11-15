using System;
using System.Collections.Generic;
using KCAA.Services.Interfaces;
using Telegram.Bot.Types.Enums;

namespace KCAA.Services.Factories
{
    public class TelegramHandlerFactory : Dictionary<UpdateType, Func<ITelegramUpdateHandler>>, ITelegramHandlerFactory
    {
        public ITelegramUpdateHandler GetHandler(UpdateType type) 
            => TryGetValue(type, out Func<ITelegramUpdateHandler> func) ? func() : this[UpdateType.Unknown]();
    }
}
