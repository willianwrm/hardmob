using System;
using System.ServiceProcess;

namespace Hardmob
{
    /// <summary>
    /// Main APP
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application
        /// </summary>
        static void Main(params string[] args)
        {
            // Check the start args
            if (args != null && args.Length > 0)
            {
                // Read params
                switch (args[0].ToLower())
                {
                    // Console mode
                    case "console":
                    case "debug":
                        {
                            // Creates and run on the go
                            using MainService debugservice = new();
                            debugservice.StartDebug();

                            // Shows info about service
                            Console.WriteLine("Press ESC to exit service");

                            // Wait until canceled
                            while(true)
                            {
                                // Check if canceled
                                if (Console.ReadKey(true).Key == ConsoleKey.Escape)
                                {
                                    // Stop the service
                                    debugservice.StopDebug();
                                    break;
                                }
                            }
                        }
                        return;
                }
            }

            // Creates and start the main service
            using MainService service = new();
            ServiceBase.Run(service);
        }
    }
}
