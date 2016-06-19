using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamTrade;
using SteamTrade.TradeOffer;
using SteamKit2;
using Newtonsoft.Json;
using System.Data.SQLite;
using System.Data.Linq.Mapping;

namespace Devkun
{
    public class Config
    {
        /// <summary>
        /// Enum for representing the trade type
        /// </summary>
        public enum TradeType
        {
            /// <summary>
            /// Deposit meaning we will request the users items
            /// </summary>
            Deposit,

            /// <summary>
            /// Withdraw meaning we will request items from storage beloning to the user and send them
            /// </summary>
            Withdraw
        }


        /// <summary>
        /// Enum for trade status type
        /// </summary>
        public enum TradeStatusType
        {
            /*For deposit*/
            DepositPending = 1,
            DepositDeclined = 2,
            DepositSent = 3,
            DepositAccepted = 4,

            /*For withdraw*/
            WithdrawPending = 6,
            WithdrawDeclined = 7,
            WithdrawSent = 8,
            WithdrawAccepted = 9
        }


        /// <summary>
        /// Class for sorting db response
        /// </summary>
        public class ItemSortClass
        {
            /// <summary>
            /// List of items
            /// </summary>
            public List<Item> list { get; set; }


            /// <summary>
            /// Owner score
            /// </summary>
            public int score { get; set; }
        }


        /// <summary>
        /// Holds trade offer info
        /// </summary>
        public class TradeStatusHolder
        {
            /// <summary>
            /// List of trade status
            /// </summary>
            public List<TradeStatus> Trades { get; set; } = new List<TradeStatus>();
        }


        /// <summary>
        /// Class for holding a trade status
        /// </summary>
        public class TradeStatus
        {
            /// <summary>
            /// Queue id of trade
            /// </summary>
            public string Id { get; set; }


            /// <summary>
            /// Steam id of user owning the trade
            /// </summary>
            public string SteamId { get; set; }


            /// <summary>
            /// Status of trade
            /// </summary>
            public TradeStatusType Status { get; set; }


            /// <summary>
            /// Trade offer id
            /// This should only be sent if status is 3
            /// </summary>
            public string Tradelink { get; set; }
        }


        /// <summary>
        /// Stores information about an item
        /// </summary>
        public class Item
        {
            /// <summary>
            /// Database ID
            /// </summary>
            public int ID { get; set; }


            /// <summary>
            /// Inventory asset id
            /// Temporary and is inventory bound
            /// </summary>
            public long AssetId { get; set; }


            /// <summary>
            /// Item class id
            /// Stays the same for all items of the same type
            /// </summary>
            public long ClassId { get; set; }


            /// <summary>
            /// The bot (storage) that holds the item
            /// Represented as SteamId64
            /// </summary>
            public ulong BotOwner { get; set; }


            /// <summary>
            /// If the item is active, ergo is in our inventory
            /// </summary>
            public bool Active { get; set; }
        }


        /// <summary>
        /// Deposit/Withdraw object received from website
        /// </summary>
        public class TradeObject
        {
            /// <summary>
            /// SteamId of user
            /// </summary>
            [JsonProperty]
            public ulong SteamId { get; set; }


            /// <summary>
            /// Queue id of user
            /// </summary>
            [JsonProperty]
            public string QueId { get; set; }


            /// <summary>
            /// Security string of user
            /// </summary>
            [JsonProperty]
            public string SecurityToken { get; set; }


            /// <summary>
            /// Trade token of user
            /// </summary>
            [JsonProperty]
            public string RU_Token { get; set; }


            /// <summary>
            /// List of item ids
            /// Each entry is formatted assetid;classid (Example: 6008197668;350467130)
            /// </summary>
            [JsonProperty]
            public List<string> item_Ids { get; set; }


            /// <summary>
            /// List of items
            /// </summary>
            public List<Item> Items { get; set; } = new List<Item>();


            /// <summary>
            /// Trade status
            /// </summary>
            public TradeStatus tradeStatus { get; set; }


            /// <summary>
            /// What type of trade this is
            /// </summary>
            public TradeType tradeType { get; set; }


            /// <summary>
            /// Trade offer state
            /// </summary>
            public TradeOfferState offerState { get; set; }
        }


        /// <summary>
        /// Class for holding list of trade objects from website
        /// </summary>
        public class Trade
        {
            /// <summary>
            /// List of deposit requests
            /// </summary>
            [JsonProperty]
            public List<TradeObject> Deposits { get; set; }


            /// <summary>
            /// List of withdraw requests
            /// </summary>
            [JsonProperty]
            public List<TradeObject> withdrawal { get; set; }
        }
    }
}
