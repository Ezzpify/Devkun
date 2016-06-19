using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net;

namespace SteamTrade
{
    public class SimpleInventory
    {
        /// <summary>
        /// Class for holding item inventory information
        /// </summary>
        public class InventoryItem
        {
            /// <summary>
            /// Item inventory id
            /// </summary>
            public long assetId { get; set; }


            /// <summary>
            /// Item id
            /// </summary>
            public long classId { get; set; }


            /// <summary>
            /// Stack count
            /// </summary>
            public int amount { get; set; }


            /// <summary>
            /// Item position in inventory
            /// </summary>
            public int pos { get; set; }
        }


        /// <summary>
        /// SteamWeb session
        /// </summary>
        private SteamWeb mSteamWeb;


        /// <summary>
        /// Items in inventory
        /// </summary>
        public List<InventoryItem> Items = new List<InventoryItem>();


        /// <summary>
        /// List of errors
        /// </summary>
        public List<string> Errors = new List<string>();


        /// <summary>
        /// SimpleInventory constructor
        /// </summary>
        /// <param name="steamweb">SteamWeb session</param>
        public SimpleInventory(SteamWeb steamweb)
        {
            mSteamWeb = steamweb;
        }


        /// <summary>
        /// Loads the inventory
        /// </summary>
        /// <param name="steamid">Steamid64 of user</param>
        /// <param name="appid">Steam app id</param>
        /// <param name="contextid">Inventory context id</param>
        /// <returns>Returns true if successful</returns>
        public bool Load(ulong steamid, int appid, long contextid)
        {
            Items.Clear();
            Errors.Clear();

            try
            {
                string response = mSteamWeb.Fetch($"http://steamcommunity.com/profiles/{steamid}/inventory/json/{appid}/{contextid}/", "GET", null, true);
                dynamic invResponse = JsonConvert.DeserializeObject(response);

                if (invResponse.success == false)
                {
                    Errors.Add($"Unable to load inventory: {invResponse?.Error}");
                    return false;
                }

                foreach (var item in invResponse.rgInventory)
                {
                    foreach (var info in item)
                    {
                        Items.Add(new InventoryItem()
                        {
                            assetId = (long)info.id,
                            classId = (long)info.classid,
                            amount = (int)info.amount,
                            pos = (int)info.pos
                        });
                    }
                }

                return true;
            }
            catch (WebException ex)
            {
                Errors.Add($"Web exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                Errors.Add($"Exception: {ex.Message}");
            }

            return false;
        }
    }
}
