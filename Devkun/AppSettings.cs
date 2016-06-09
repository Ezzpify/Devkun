using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

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
            public int tradeOfferExpireTime { get; set; }


            /// <summary>
            /// How many items we should store on each bot
            /// </summary>
            public int itemLimitPerBot { get; set; }


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
            /// Discord login email
            /// </summary>
            public string email { get; set; }


            /// <summary>
            /// Discord login password
            /// </summary>
            public string password { get; set; }


            /// <summary>
            /// Discord server on which we should operate on
            /// The bot has to manually join the server before hand
            /// </summary>
            public string serverName { get; set; }


            /// <summary>
            /// Discord channel on the server on which we should operate on
            /// </summary>
            public string channelName { get; set; }
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
            /// 0 = None
            /// 1 = Withdraw
            /// 2 = Deposit
            /// 3 = Storage
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
