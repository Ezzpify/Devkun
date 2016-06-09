using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Net;
using Newtonsoft.Json;

namespace Devkun
{
    static class Website
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static string GetTrades()
        {
            return DownloadString("http://www.mesosus.com/API/BOTAPI/GetDeposits.php");
        }


        /// <summary>
        /// Updates a list of trades
        /// </summary>
        /// <param name="trades">List of trade objects</param>
        public static void UpdateTrade(List<Config.TradeObject> trades)
        {
            var statusHolder = new Config.TradeStatusHolder();
            trades.ForEach(o => statusHolder.Trades.Add(o.tradeStatus));
            UpdateWebTrades(JsonConvert.SerializeObject(statusHolder, Formatting.Indented));
        }


        /// <summary>
        /// Updates a single trade
        /// </summary>
        /// <param name="trade">Trade object</param>
        public static void UpdateTrade(Config.TradeObject trade)
        {
            var statusHolder = new Config.TradeStatusHolder();
            statusHolder.Trades.Add(trade.tradeStatus);
            UpdateWebTrades(JsonConvert.SerializeObject(statusHolder, Formatting.Indented));
        }
        
        
        /// <summary>
        /// Updates trade states
        /// </summary>
        /// <param name="jsonArr">Serialized json string of trades</param>
        /// <returns>Returns true if successful</returns>
        private static bool UpdateWebTrades(string jsonArr)
        {
            string response = UploadString(EndPoints.Website.GetProcessUrl(),
                EndPoints.Website.INVENTORY_URL_END + jsonArr);

            return !string.IsNullOrEmpty(response);
        }


        /// <summary>
        /// Downloads a string from the internet from the specified url
        /// </summary>
        /// <param name="url">Url to download string from</param>
        /// <returns>Returns string of url website source</returns>
        public static string DownloadString(string url)
        {
            using (WebClient wc = new WebClient())
            {
                try
                {
                    wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    wc.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/42.0.2311.135 Safari/537.36");
                    return wc.DownloadString(url);
                }
                catch (WebException ex)
                {
                    Console.WriteLine(ex.Message);
                    return string.Empty;
                }
            }
        }


        /// <summary>
        /// Upload a string with arguments to a specified url
        /// </summary>
        /// <param name="url">Url to upload to</param>
        /// <param name="args">Arguments to upload</param>
        /// <returns>Returns string of website response to upload</returns>
        public static string UploadString(string url, string args = "")
        {
            using (WebClient wc = new WebClient())
            {
                try
                {
                    wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    wc.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");
                    return wc.UploadString(url, args);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }
    }
}
