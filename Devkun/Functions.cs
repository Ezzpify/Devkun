using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;

namespace Devkun
{
    static class Functions
    {
        /// <summary>
        /// Returns the startup location of the application
        /// </summary>
        /// <returns>Returns path</returns>
        public static string GetStartFolder()
        {
            return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\";
        }


        /// <summary>
        /// Returns a datetime timestmap
        /// </summary>
        /// <returns>Returns timestamp string</returns>
        public static string GetTimestamp()
        {
            return DateTime.Now.ToString("d/M/yyyy HH:mm:ss");
        }


        /// <summary>
        /// Returns unix timestamp
        /// </summary>
        /// <returns>Unix int</returns>
        public static int GetUnixTimestamp()
        {
            return (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        }


        /// <summary>
        /// Splits an item string from website
        /// Format for items are (assetId;classId) Example: 1234;5678
        /// </summary>
        /// <param name="item">Item id string</param>
        /// <returns>Returns null if failed</returns>
        public static Config.Item SplitItem(string item)
        {
            string[] itemSpl = item.Split(';');

            long assetId;
            long classId;

            if (long.TryParse(itemSpl[0], out assetId) && long.TryParse(itemSpl[1], out classId))
            {
                return new Config.Item()
                {
                    AssetId = assetId,
                    ClassId = classId
                };
            }

            return null;
        }


        /// <summary>
        /// Parse escrow response and return the number of days user has to wait to trade
        /// </summary>
        /// <param name="resp">Escrow response</param>
        /// <returns>Returns int</returns>
        public static int ParseEscrowResponse(string resp)
        {
            if (string.IsNullOrWhiteSpace(resp))
                return 123;
            
            var theirDaysRegex = Regex.Match(resp, @"g_daysTheirEscrow(?:[\s=]+)(?<days>[\d]+);", RegexOptions.IgnoreCase);
            if (!theirDaysRegex.Groups["days"].Success)
                return 123;

            return int.Parse(theirDaysRegex.Groups["days"].Value);
        }


        /// <summary>
        /// Sorts the database to find the best items
        /// This is heavily commented else I'll forget what it does
        /// </summary>
        /// <param name="dbItems">Db item list</param>
        /// <param name="requestItems">Items to find</param>
        /// <returns>Returns list config.item</returns>
        public static List<Config.Item> SortDBItems(List<Config.Item> dbItems, List<Config.Item> requestItems)
        {
            /*Final list that will contain the best items*/
            var finalList = new List<Config.Item>();

            /*List that we'll be adding items to and sorting by BotOwner*/
            var sortedList = new List<Config.ItemSortClass>();

            /*Group lists by BotOwner*/
            var ownerList = dbItems.GroupBy(o => o.BotOwner).Select(o => o.ToList());

            /*Go through all the owners and give them a score depending on how many items they have that we're requesting*/
            foreach (var owner in ownerList)
            {
                int localScore = 0;
                foreach (var item in owner)
                {
                    if (requestItems.Any(o => o.ClassId == item.ClassId))
                        localScore++;
                }
                
                /*Add owner to ownerList*/
                sortedList.Add(new Config.ItemSortClass() { list = owner, score = localScore });
            }

            /*Order the list by their given score*/
            sortedList = sortedList.OrderBy(o => o.score).Reverse().ToList();

            /*Go through all the requested items*/
            var busyItems = new List<long>();
            foreach (var requestItem in requestItems)
            {
                /*We have a local bool here because we'll need to break out of two loops when we find an item*/
                bool dobreak = false;
                foreach (var owner in sortedList)
                {
                    if (dobreak)
                    {
                        /*Break needed, so set it back to false and break out of this loop too*/
                        dobreak = false;
                        break;
                    }

                    /*Go through all the items that the BotOwner has*/
                    foreach (var item in owner.list)
                    {
                        /*If the item ids match then we'll add it to the final list and break out of the two loops*/
                        if (requestItem.ClassId == item.ClassId && !busyItems.Contains(item.AssetId))
                        {
                            finalList.Add(item);
                            busyItems.Add(item.AssetId);
                            dobreak = true;
                            break;
                        }
                    }
                }
            }

            return finalList;
        }
    }
}
