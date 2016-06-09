using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Runtime.InteropServices;

namespace Devkun
{
    class Program
    {
        /// <summary>
        /// Session variable
        /// </summary>
        private static Session mSession;


        /// <summary>
        /// Application settings
        /// </summary>
        private static AppSettings.ApplicationSettings mSettings;


        /// <summary>
        /// Console event
        /// </summary>
        private static ConsoleEventDelegate mConsoleEvent;


        /// <summary>
        /// Console event type
        /// </summary>
        /// <param name="eventType">Event type</param>
        /// <returns>Returns bool</returns>
        private delegate bool ConsoleEventDelegate(int eventType);


        /// <summary>
        /// DllImport to catch the exit event
        /// </summary>
        /// <returns>Returns bool</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleEventDelegate callback, bool add);


        /// <summary>
        /// Catch console event
        /// In this case we only want to catch Exit event
        /// Realistically we have 4-5 seconds to preform this
        /// action before windows forces the program to close
        /// </summary>
        /// <param name="eventType">Event type</param>
        /// <returns>Retuurns bool</returns>
        private static bool ConsoleEventCallback(int eventType)
        {
            /*2 being Exit event*/
            if (eventType == 2)
            {
                Console.WriteLine("\n\n\nClosing...");
                //Thread.Sleep(1000);
            }

            return false;
        }


        /// <summary>
        /// Main entry
        /// </summary>
        /// <param name="args">No args</param>
        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Title = "==[ Devkun system ]==";

            EndPoints.Application.CreateLogFolders();
            mConsoleEvent = new ConsoleEventDelegate(ConsoleEventCallback);
            SetConsoleCtrlHandler(mConsoleEvent, true);

            if ((mSettings = Settings.GetSettings()) == null)
            {
                Console.WriteLine($"Wrote new settings file at\n{EndPoints.Application.SETTINGS_FILE_PATH}");
                Thread.Sleep(1500);
                return;
            }
            
            mSession = new Session(mSettings);

            /*When session dies we'll end up here*/
            Console.WriteLine("\n\n");
            string exitMessage = "      Press any key to exit the application. ";
            while (!Console.KeyAvailable)
            {
                Console.Write($"\r{exitMessage}");
                exitMessage = exitMessage.Substring(1, exitMessage.Length - 1) + exitMessage.Substring(0, 1);
                Thread.Sleep(100);
            }

            Environment.Exit(1);
        }
    }
}
