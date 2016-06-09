using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Internal;
using SteamTrade;
using SteamTrade.TradeOffer;

namespace Devkun
{
    /// <summary>
    /// Class for holding steam information
    /// </summary>
    class Steam
    {
        /// <summary>
        /// Path to the sentry file for the account
        /// </summary>
        public string sentryPath { get; set; }


        /// <summary>
        /// Web api unique id
        /// </summary>
        public string uniqueId { get; set; }


        /// <summary>
        /// Web api nounce
        /// </summary>
        public string nounce { get; set; }


        /// <summary>
        /// Our steam client
        /// </summary>
        public SteamClient Client { get; set; }


        /// <summary>
        /// Steam trade for trading
        /// </summary>
        public SteamTrading Trade { get; set; }


        /// <summary>
        /// Steam callback manger
        /// </summary>
        public CallbackManager CallbackManager { get; set; }


        /// <summary>
        /// Steam user
        /// </summary>
        public SteamUser User { get; set; }


        /// <summary>
        /// Login details for the account
        /// </summary>
        public SteamUser.LogOnDetails logOnDetails { get; set; }


        /// <summary>
        /// Steam friends
        /// </summary>
        public SteamFriends Friends { get; set; }


        /// <summary>
        /// Trade offer manager
        /// </summary>
        public TradeOfferManager TradeOfferManager { get; set; }


        /// <summary>
        /// Steam web
        /// </summary>
        public SteamWeb Web { get; set; }


        /// <summary>
        /// Steam authentication
        /// </summary>
        public Authentication Auth { get; set; }


        /// <summary>
        /// Steam inventory
        /// </summary>
        public GenericInventory Inventory { get; set; }
    }
}
