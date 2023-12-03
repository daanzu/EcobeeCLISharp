using CommandLine;
using I8Beef.Ecobee;
using I8Beef.Ecobee.Protocol;
using I8Beef.Ecobee.Protocol.Objects;
using I8Beef.Ecobee.Protocol.Functions;
using I8Beef.Ecobee.Protocol.Thermostat;
using ExtensionMethods;

namespace EcobeeCLISharp
{
    static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await Parser.Default.ParseArguments<Options>(args)
                .MapResult(
                    (Options options) => RunOptionsAndReturnExitCode(options),
                    errs => Task.FromResult(1));
        }

        public class Options
        {
            [Option('f', "fan", SetName = "holdparams", HelpText = "Set desired fan mode")]
            public string? Fan { get; set; }

            [Option('c', "cool", SetName = "holdparams", HelpText = "Set desired cool temperature")]
            public string? Cool { get; set; }

            [Option('h', "heat", SetName = "holdparams", HelpText = "Set desired heat temperature")]
            public string? Heat { get; set; }

            [Option("holdtype", Default = "nextTransition", SetName = "holdparams", HelpText = "Set desired hold type: nextTransition/next, indefinite")]
            public string? HoldType { get; set; }

            [Option('h', "hold", SetName = "holdprogram", HelpText = "Set desired hold program: resumeProgram/resume, [program name]")]
            public string? Hold { get; set; }

            [Option('v', "verbose", Default = false, HelpText = "Print all messages to standard output")]
            public bool Verbose { get; set; }

            [Option("infobefore", Default = false, HelpText = "Print thermostat info before updating")]
            public bool InfoBefore { get; set; }

            [Option("infoafter", Default = false, HelpText = "Print thermostat info after updating")]
            public bool InfoAfter { get; set; }

            [Option("infoaftertimeout", Default = 10*60, HelpText = "Timeout in seconds for printing thermostat info after updating")]
            public int InfoAfterTimeout { get; set; }

            [Option("wait", Default = false, HelpText = "Wait for key to be pressed before exiting")]
            public bool Wait { get; set; }

            [Option("hide", Default = false, HelpText = "Hide console window (Windows only)")]
            public bool HideConsole { get; set; }
        }

        private static bool _verbose = false;
        private static string? _appApiKey;
        private static StoredAuthToken? _currentAuthToken;

        private static readonly string CredentialsFilePath = Path.Combine(AppContext.BaseDirectory, "ecobee_credentials.txt");

        private static async Task<int> RunOptionsAndReturnExitCode(Options options)
        {
            _verbose = options.Verbose;

            if (options.HideConsole)
            {
                Daanzu.Utils.HideConsoleWindow();
            }

            if (!File.Exists(CredentialsFilePath))
            {
                Console.WriteLine("Credentials file not found. Please create ecobee_credentials.txt in the same directory as this executable.");
                return 1;
            }

            _appApiKey = await ReadApiKeyFileAsync();
            var client = new Client(_appApiKey, ReadTokenFileAsync, WriteTokenFileAsync);

            if (!await HaveTokenFileAsync())
            {
                Console.WriteLine("Getting new tokens");
                var pin = await client.GetPinAsync();

                Console.WriteLine("Pin: " + pin.EcobeePin);
                Console.WriteLine("You have " + pin.ExpiresIn + " minutes to enter this on the Ecobee site and hit enter.");
                Console.ReadLine();

                await client.GetAccessTokenAsync(pin.Code);
            }
            else
            {
                VerboseWriteLine("Loading existing tokens");
                await ReadTokenFileAsync();
            }

            var initialThermostatResponse = await GetThermostatAsync(client);

            if (options.InfoBefore)
            {
                PrintStatus(initialThermostatResponse);
            }

            var thermostat = initialThermostatResponse.GetFirstThermostat();
            var currentDesiredCoolTemp = ConvertTemperature(thermostat.Runtime.DesiredCool);
            var currentDesiredHeatTemp = ConvertTemperature(thermostat.Runtime.DesiredHeat);
            var heatCoolMinDelta = ConvertTemperature(thermostat.Settings.HeatCoolMinDelta);

            // https://github.com/i8beef/HomeAutio.Mqtt.Ecobee/blob/master/src/HomeAutio.Mqtt.Ecobee/EcobeeMqttService.cs#L107

            var holdParams = new SetHoldParams();

            // Handle input...

            if (options.Fan != null)
            {
                string mode;
                if (options.Fan == "auto" || options.Fan == "off")
                {
                    mode = "auto";
                }
                else if (options.Fan == "on")
                {
                    mode = "on";
                }
                else
                {
                    Console.WriteLine("Invalid fan mode");
                    return 1;
                }
                holdParams.Fan = mode;
            }

            if (options.Cool is not null)
            {
                decimal temperature;
                if (options.Cool.StartsWith("+"))
                {
                    temperature = currentDesiredCoolTemp + ConvertTemperature(options.Cool.Substring(1));
                }
                else if (options.Cool.StartsWith("-"))
                {
                    temperature = currentDesiredCoolTemp - ConvertTemperature(options.Cool.Substring(1));
                }
                else
                {
                    temperature = ConvertTemperature(options.Cool);
                }
                holdParams.CoolHoldTemp = ConvertTemperature(temperature);
            }

            if (options.Heat is not null)
            {
                decimal temperature;
                if (options.Heat.StartsWith("+"))
                {
                    temperature = currentDesiredHeatTemp + ConvertTemperature(options.Heat.Substring(1));
                }
                else if (options.Heat.StartsWith("-"))
                {
                    temperature = currentDesiredHeatTemp - ConvertTemperature(options.Heat.Substring(1));
                }
                else
                {
                    temperature = ConvertTemperature(options.Heat);
                }
                holdParams.HeatHoldTemp = ConvertTemperature(temperature);
            }

            if (options.HoldType != null)
            {
                string mode;
                if (options.HoldType == "nextTransition" || options.HoldType == "next")
                {
                    mode = "nextTransition";
                }
                else if (options.HoldType == "indefinite")
                {
                    mode = "indefinite";
                }
                else
                {
                    Console.WriteLine("Invalid hold mode");
                    return 1;
                }
                holdParams.HoldType = mode;
            }
            else
            {
                holdParams.HoldType = "nextTransition";
            }

            // if (options.Hold != null)
            // {
            //     string mode;
            //     if (options.Hold == "resumeProgram" || options.Hold == "resume")
            //     {
            //         mode = "resumeProgram";
            //     }
            //     else if (options.Hold == "hold")
            //     {
            //         mode = "hold";
            //     }
            //     else
            //     {
            //         Console.WriteLine("Invalid hold mode");
            //         return 1;
            //     }
            //     holdParams.HoldClimateRef = mode;
            // }

            // Validation...

            if (holdParams.HeatHoldTemp is not null && holdParams.CoolHoldTemp is not null && holdParams.CoolHoldTemp - holdParams.HeatHoldTemp < heatCoolMinDelta)
            {
                Console.WriteLine("Heat temperature must be less than cool temperature by at least " + heatCoolMinDelta + " degrees");
                return 1;
            }
            else if (holdParams.HeatHoldTemp is null && holdParams.CoolHoldTemp is not null)
            {
                holdParams.HeatHoldTemp = ConvertTemperature((ConvertTemperature(holdParams.CoolHoldTemp) - currentDesiredHeatTemp < heatCoolMinDelta) ? (ConvertTemperature(holdParams.CoolHoldTemp) - heatCoolMinDelta) : currentDesiredHeatTemp);
            }
            else if (holdParams.CoolHoldTemp is null && holdParams.HeatHoldTemp is not null)
            {
                holdParams.CoolHoldTemp = ConvertTemperature((currentDesiredCoolTemp - ConvertTemperature(holdParams.HeatHoldTemp) < heatCoolMinDelta) ? (ConvertTemperature(holdParams.HeatHoldTemp) + heatCoolMinDelta) : currentDesiredCoolTemp);
            }

            if (holdParams.CoolHoldTemp is not null && holdParams.CoolHoldTemp < thermostat.Settings.CoolRangeLow || holdParams.CoolHoldTemp > thermostat.Settings.CoolRangeHigh)
            {
                Console.WriteLine("Cool temperature out of range");
                return 1;
            }
            if (holdParams.HeatHoldTemp is not null && holdParams.HeatHoldTemp < thermostat.Settings.HeatRangeLow || holdParams.HeatHoldTemp > thermostat.Settings.HeatRangeHigh)
            {
                Console.WriteLine("Heat temperature out of range");
                return 1;
            }

            // Perform update...

            var updateRequest = new ThermostatUpdateRequest
            {
               Selection = new Selection
               {
                   SelectionType = "registered"
               },
            };
            updateRequest.Functions = new List<Function>
            {
                new SetHoldFunction
                {
                    Params = holdParams
                }
            };

            VerboseWriteLine(JsonSerializer<ThermostatUpdateRequest>.Serialize(updateRequest), true);
            var updateResponse = await client.PostAsync<ThermostatUpdateRequest, Response>(updateRequest);
            VerboseWriteLine(JsonSerializer<Response>.Serialize(updateResponse), true);

            if (options.InfoAfter)
            {
                var timeoutTime = DateTime.Now.AddSeconds(options.InfoAfterTimeout);
                var finalThermostatResponse = await GetThermostatAsync(client);
                while (finalThermostatResponse.GetFirstThermostat().Runtime.LastModified == initialThermostatResponse.GetFirstThermostat().Runtime.LastModified)
                {
                    if (DateTime.Now > timeoutTime)
                    {
                        Console.WriteLine("Timeout waiting for thermostat to update");
                        break;
                    }
                    if (_verbose || true)
                    {
                        PrintStatus(finalThermostatResponse);
                    }
                    Console.WriteLine("Waiting for thermostat to update");
                    await Task.Delay(1000);
                    finalThermostatResponse = await GetThermostatAsync(client);
                }
                await Task.Delay(1000);
                finalThermostatResponse = await GetThermostatAsync(client);
                PrintStatus(finalThermostatResponse);
            }

            if (options.Wait)
            {
                Console.WriteLine();
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
            }

            return 0;
        }

        private static void PrintStatus(ThermostatResponse thermostatResponse)
        {
            var thermostat = thermostatResponse.ThermostatList.First();
            Console.WriteLine("Current Status:");
            Console.WriteLine($"  Temperature: {thermostat.Runtime.ActualTemperature}");
            Console.WriteLine($"  Humidity: {thermostat.Runtime.ActualHumidity}");
            Console.WriteLine($"  Mode: {thermostat.Settings.HvacMode}");
            Console.WriteLine($"  Desired Temperature Range: {thermostat.Runtime.DesiredHeat} - {thermostat.Runtime.DesiredCool}");
            Console.WriteLine($"  Desired Fan: {thermostat.Runtime.DesiredFanMode}");
            Console.WriteLine($"  Equipment Status: {thermostat.EquipmentStatus}");
            Console.WriteLine($"  Last Status Modified: {thermostat.Runtime.LastStatusModified}");
            Console.WriteLine($"  Last Modified: {thermostat.Runtime.LastModified}");
            if (thermostat.Events.Count > 0)
            {
                var currentEvent = thermostat.Events.First();
                Console.WriteLine($"  Current Event: End Time: {currentEvent.EndTime}");
            }
        }

        private static decimal ConvertTemperature(int? temperature) => Convert.ToDecimal(temperature) / 10;
        private static int ConvertTemperature(decimal? temperature) => Convert.ToInt32(temperature * 10);
        private static decimal ConvertTemperature(string? temperature) => Convert.ToDecimal(temperature);

        private static async Task<ThermostatResponse> GetThermostatAsync(Client client)
        {
            var request = new ThermostatRequest
            {
                Selection = new Selection
                {
                    SelectionType = "registered",
                    IncludeSettings = true,
                    IncludeSensors = true,
                    IncludeEquipmentStatus = true,
                    IncludeWeather = true,
                    IncludeDevice = true,
                    IncludeEvents = true,
                    IncludeProgram = true,
                    IncludeRuntime = true,
                    IncludeEnergy = true,
                    IncludeElectricity = true,
                    IncludeExtendedRuntime = true,
                    IncludeNotificationSettings = true,
                    IncludeAlerts = true,
                    // Not authorized?
                    // IncludeAudio = true,
                    // IncludeSecuritySettings = true,
                    // IncludeVersion = true,
                    // IncludeOemCfg = true,
                    // IncludeHouseDetails = true,
                    // IncludeManagement = true,
                    // IncludeTechnician = true,
                    // IncludeLocation = true,
                    // IncludeUtility = true,
                    // IncludeCapabilities = true,
                }
            };
            var response = await client.GetAsync<ThermostatRequest, ThermostatResponse>(request);
            VerboseWriteLine(JsonSerializer<ThermostatResponse>.Serialize(response));

            return response;
        }

        public static async Task WriteTokenFileAsync(StoredAuthToken storedAuthToken, CancellationToken cancellationToken = default)
        {
            // Cache the returned tokens
            _currentAuthToken = storedAuthToken;

            // Write token to persistent store
            var text = new System.Text.StringBuilder();
            text.AppendLine(_appApiKey);
            text.AppendLine($"{storedAuthToken.TokenExpiration:MM/dd/yy hh:mm:ss tt}");
            text.AppendLine(storedAuthToken.AccessToken);
            text.AppendLine(storedAuthToken.RefreshToken);

            await File.WriteAllTextAsync(CredentialsFilePath, text.ToString());
        }

        public static async Task<StoredAuthToken?> ReadTokenFileAsync(CancellationToken cancellationToken = default)
        {
            if (_currentAuthToken == null && File.Exists(CredentialsFilePath))
            {
                var fileText = await File.ReadAllLinesAsync(CredentialsFilePath);
                _currentAuthToken = new StoredAuthToken
                {
                    TokenExpiration = DateTime.Parse(fileText[1]),
                    AccessToken = fileText[2],
                    RefreshToken = fileText[3],
                };

                VerboseWriteLine("Access Token: " + _currentAuthToken.AccessToken);
                VerboseWriteLine("Refresh Token: " + _currentAuthToken.RefreshToken);
            }

            return _currentAuthToken;
        }

        public static async Task<bool> HaveTokenFileAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(CredentialsFilePath))
            {
                return false;
            }
            var fileText = await File.ReadAllLinesAsync(CredentialsFilePath);
            if (fileText.Length < 4)
            {
                return false;
            }
            return true;
        }

        public static async Task<string> ReadApiKeyFileAsync(CancellationToken cancellationToken = default)
        {
            var fileText = await File.ReadAllLinesAsync(CredentialsFilePath);
            return fileText[0].Trim();
        }

        private static void VerboseWriteLine(string message, bool force = false)
        {
            if (_verbose || force)
            {
                Console.WriteLine(message);
            }
        }

    }
}

namespace ExtensionMethods
{
    public static class EcobeeExtensions
    {
        public static Thermostat GetFirstThermostat(this ThermostatResponse thermostatResponse)
        {
            return thermostatResponse.ThermostatList.First();
        }
    }
}

namespace Daanzu
{
    using System.Runtime.InteropServices;

    public class Utils
    {
        public static void HideConsoleWindow()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var handle = GetConsoleWindow();
                ShowWindow(handle, SW_HIDE);
            }
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;
    }
}
