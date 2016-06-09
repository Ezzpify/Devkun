using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    }
}
