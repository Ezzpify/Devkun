using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamAuth;
using System.IO;
using Newtonsoft.Json;

namespace Devkun
{
    class Authentication
    {
        /// <summary>
        /// Steam guard account
        /// </summary>
        public SteamGuardAccount mAccount;


        /// <summary>
        /// Class constructor
        /// Gets the steam guard account from file
        /// </summary>
        /// <param name="settings">Settings class for bot</param>
        public Authentication(AppSettings.BotSettings settings)
        {
            if ((mAccount = GetAccount(settings.username)) == null)
            {
                Console.WriteLine("An authenticator needs to be added to this account manually.");
                return;
            }
        }


        /// <summary>
        /// Finds the SGA file for an account if it exists
        /// </summary>
        /// <param name="username">Username of account</param>
        /// <returns>Returns null if no file found</returns>
        private SteamGuardAccount GetAccount(string username)
        {
            DirectoryInfo dInfo = new DirectoryInfo(Functions.GetStartFolder() + "Sentryfiles");
            FileInfo[] fInfo = dInfo.GetFiles("*.SGA");

            foreach (var file in fInfo)
            {
                if (file.Name.ToLower().Contains(username.ToLower()))
                {
                    return JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(file.FullName));
                }
            }

            return null;
        }


        /// <summary>
        /// Returns steam guard code for account
        /// </summary>
        /// <returns>Returns string</returns>
        public string GetSteamGuardCode()
        {
            return mAccount.GenerateSteamGuardCodeForTime(TimeAligner.GetSteamTime());
        }


        /// <summary>
        /// Gets all confirmations available
        /// </summary>
        /// <returns>Returns list, or if failed returns null</returns>
        public List<Confirmation> GetConfirmationList()
        {
            try
            {
                mAccount.RefreshSession();
                return mAccount.FetchConfirmations().ToList();
            }
            catch (SteamGuardAccount.WGTokenInvalidException)
            {
                if (mAccount.RefreshSession())
                {
                    SaveSGAFile();
                }

                return null;
            }
        }


        /// <summary>
        /// Saves new authenticated account
        /// Will re-save when session becomes invalid
        /// </summary>
        private void SaveSGAFile()
        {
            string fileName = Functions.GetStartFolder() + $"Sentryfiles\\{mAccount.AccountName}.SGA";
            File.WriteAllText(fileName, JsonConvert.SerializeObject(mAccount, Formatting.Indented));
        }
    }
}
