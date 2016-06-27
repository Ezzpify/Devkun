using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Devkun
{
    static class EndPoints
    {
        /// <summary>
        /// Application variables
        /// </summary>
        public static class Application
        {
            /// <summary>
            /// Stores the path to application settings file
            /// </summary>
            public static string SETTINGS_FILE_PATH = Functions.GetStartFolder() + "Settings\\Application.json";


            /// <summary>
            /// Creates all the folders used by the application
            /// </summary>
            public static void CreateLogFolders()
            {
                Directory.CreateDirectory(Functions.GetStartFolder() + "Database");
                Directory.CreateDirectory(Functions.GetStartFolder() + "Settings");
                Directory.CreateDirectory(Functions.GetStartFolder() + "Sentryfiles");
                Directory.CreateDirectory(Functions.GetStartFolder() + "Logs");
                Directory.CreateDirectory(Functions.GetStartFolder() + "Logs\\Bot");
                Directory.CreateDirectory(Functions.GetStartFolder() + "Logs\\Discord");
            }
        }


        /// <summary>
        /// Class for holding website stuff
        /// </summary>
        public static class Website
        {
            /// <summary>
            /// Domain name
            /// </summary>
            public static string DOMAIN = "mesosus";


            /// <summary>
            /// Base url
            /// </summary>
            public static string HOST_URL = "http://www.mesosus.com/";


            /// <summary>
            /// Process url end
            /// </summary>
            public static string PROCESS_URL_END = "API/BOTAPI/process.php";


            /// <summary>
            /// Used to post updates for trades
            /// </summary>
            public static string INVENTORY_URL_END = "action=invtradecallback&Status=";


            /// <summary>
            /// Website bot chat location
            /// </summary>
            public static string WEBSITE_CHAT_END = "/botchatmess.php?msg=";


            /// <summary>
            /// Website bot chat token
            /// </summary>
            public static string WEBSITE_CHAT_TOKEN = "&sec=sectokenlelurmomneverlovedu";


            /// <summary>
            /// Returns the process url
            /// </summary>
            /// <returns>Returns uri string</returns>
            public static string GetProcessUrl()
            {
                return HOST_URL + PROCESS_URL_END;
            }


            /// <summary>
            /// Returns formatted chat url
            /// </summary>
            /// <param name="encodedMsg">UrlEncoded msg</param>
            /// <returns>Returns uri string</returns>
            public static string GetChatUrl(string encodedMsg)
            {
                return $"{HOST_URL + WEBSITE_CHAT_END + encodedMsg + WEBSITE_CHAT_TOKEN}";
            }
        }


        /// <summary>
        /// Class for holding steam endpoints
        /// </summary>
        public static class Steam
        {
            /// <summary>
            /// String for steam status json page
            /// </summary>
            public static string STEAM_STATUS_URL = "https://crowbar.steamdb.info/Barney";
        }
    }
}
