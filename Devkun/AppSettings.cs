using System.Collections.Generic;

namespace Devkun
{
    public class AppSettings
    {
        /// <summary>
        /// Stores settings for the application
        /// This will be set on application start
        /// </summary>
        public class ApplicationSettings
        {
            /// <summary>
            /// How long a trade offer should remain active
            /// </summary>
            public int tradeOfferExpireTimeSeconds { get; set; }


            /// <summary>
            /// How many items can be sent at once in a storage trade offer
            /// </summary>
            public int storageTradeOfferMaxItems { get; set; }


            /// <summary>
            /// How many items we should store on each bot
            /// </summary>
            public int itemLimitPerBot { get; set; }


            /// <summary>
            /// How many items the host bot can have before before we
            /// send items to storage bots
            /// </summary>
            public int hostItemLimit { get; set; }


            /// <summary>
            /// Discord settings
            /// </summary>
            public DiscordSettings discord { get; set; } = new DiscordSettings();


            /// <summary>
            /// Bot settings
            /// </summary>
            public List<BotSettings> bots { get; set; } = new List<BotSettings>();
        }


        /// <summary>
        /// Stores settings for the application
        /// This will be set on application start
        /// </summary>
        public class DiscordSettings
        {
            /// <summary>
            /// Discord bot token
            /// </summary>
            public string accessToken { get; set; }


            /// <summary>
            /// Discord display name
            /// </summary>
            public string displayName { get; set; }


            /// <summary>
            /// Discord display game title
            /// </summary>
            public string gameName { get; set; }


            /// <summary>
            /// Discord channel on the server on which we post message to
            /// </summary>
            public ulong mainChannelId { get; set; }
        }


        /// <summary>
        /// Stores information about a running bot
        /// This will be set on application start
        /// </summary>
        public class BotSettings
        {
            /// <summary>
            /// Steam account username
            /// </summary>
            public string username { get; set; }


            /// <summary>
            /// Steam account password
            /// </summary>
            public string password { get; set; }


            /// <summary>
            /// Represents what job the bot has
            /// 1 = Main
            /// 2 = Storage
            /// </summary>
            public int jobId { get; set; }


            /// <summary>
            /// Account api key
            /// https://www.steamcommunity.com/dev/apikey
            /// </summary>
            public string apiKey { get; set; }


            /// <summary>
            /// Steam account display name
            /// </summary>
            public string displayName { get; set; }


            /// <summary>
            /// Steam trade offer trade token
            /// http://steamcommunity.com/id/me/tradeoffers/privacy
            /// Example: qHVCsbIs
            /// </summary>
            public string tradeToken { get; set; }


            /// <summary>
            /// Class constructor
            /// </summary>
            public BotSettings() { }


            /// <summary>
            /// Quick check to make sure no property in this class is null/empty
            /// </summary>
            /// <returns>Returns a bool</returns>
            public bool HasEmptyProperties()
            {
                if (username == null || username.Length == 0)
                    return true;
                if (password == null || password.Length == 0)
                    return true;
                if (apiKey == null || apiKey.Length == 0)
                    return true;
                if (displayName == null || displayName.Length == 0)
                    return true;
                if (tradeToken == null || tradeToken.Length == 0)
                    return true;

                return false;
            }
        }
    }
}
