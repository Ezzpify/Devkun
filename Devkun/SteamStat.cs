using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Devkun
{
    static class SteamStat
    {
        public class Cms
        {
            public string status { get; set; }
            public string title { get; set; }
        }


        public class Community
        {
            public string status { get; set; }
            public string title { get; set; }
            public int time { get; set; }
        }


        public class Csgo
        {
            public string status { get; set; }
            public string title { get; set; }
            public int time { get; set; }
        }


        public class CsgoCommunity
        {
            public string status { get; set; }
            public string title { get; set; }
        }


        public class CsgoSessions
        {
            public string status { get; set; }
            public string title { get; set; }
        }


        public class Database
        {
            public string status { get; set; }
            public string title { get; set; }
            public int time { get; set; }
        }


        public class Online
        {
            public string status { get; set; }
            public string title { get; set; }
        }


        public class Steam
        {
            public string status { get; set; }
            public string title { get; set; }
            public int time { get; set; }
        }


        public class Webapi
        {
            public string status { get; set; }
            public string title { get; set; }
            public int time { get; set; }
        }


        public class Services
        {
            public Cms cms { get; set; }
            public Community community { get; set; }
            public Csgo csgo { get; set; }
            public CsgoCommunity csgo_community { get; set; }
            public CsgoSessions csgo_sessions { get; set; }
            public Database database { get; set; }
            public Online online { get; set; }
            public Steam steam { get; set; }
            public Webapi webapi { get; set; }
        }
        
        
        public class Status
        {
            public bool success { get; set; }
            public int time { get; set; }
            public double online { get; set; }
            public Services services { get; set; }
            public string psa { get; set; }
        }


        /// <summary>
        /// Returns the status class from SteamStat
        /// </summary>
        /// <returns>Returns steamstat</returns>
        public static Status GetStatus()
        {
            string statJson = Website.DownloadString(EndPoints.Steam.STEAM_STATUS_URL);
            if (statJson.Length > 0)
            {
                return JsonConvert.DeserializeObject<Status>(statJson);
            }

            return null;
        }
    }
}
