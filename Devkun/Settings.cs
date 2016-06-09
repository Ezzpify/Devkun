using Newtonsoft.Json;
using System.IO;

namespace Devkun
{
    static class Settings
    {
        /// <summary>
        /// Reads the application settings file
        /// </summary>
        /// <returns>Returns null if failed</returns>
        public static AppSettings.ApplicationSettings GetSettings()
        {
            if (File.Exists(EndPoints.Application.SETTINGS_FILE_PATH))
            {
                string jsonStr = File.ReadAllText(EndPoints.Application.SETTINGS_FILE_PATH);
                return JsonConvert.DeserializeObject<AppSettings.ApplicationSettings>(jsonStr);
            }
            else
            {
                var settings = new AppSettings.ApplicationSettings();
                for (int i = 0; i < 3; i++)
                    settings.bots.Add(new AppSettings.BotSettings());
                
                string jsonStr = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(EndPoints.Application.SETTINGS_FILE_PATH, jsonStr);
                return null;
            }
        }
    }
}
