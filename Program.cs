using CommandLine;
using I8Beef.Ecobee;
using I8Beef.Ecobee.Protocol;
using I8Beef.Ecobee.Protocol.Objects;
using I8Beef.Ecobee.Protocol.Functions;
using I8Beef.Ecobee.Protocol.Thermostat;
using ExtensionMethods;
using System.Globalization;

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

            [Option('c', "cool", SetName = "holdparams", HelpText = "Set desired cool temperature (should be heat < cool temperature)")]
            public string? Cool { get; set; }

            [Option('h', "heat", SetName = "holdparams", HelpText = "Set desired heat temperature (should be heat < cool temperature)")]
            public string? Heat { get; set; }

            [Option("holdtype", Default = "nextTransition", SetName = "holdparams", HelpText = "Set desired hold type: nextTransition/next, indefinite")]
            public string? HoldType { get; set; }

            // [Option('h', "hold", SetName = "holdprogram", HelpText = "Set desired hold program: resumeProgram/resume, [program name]")]
            // public string? Hold { get; set; }

            [Option("daemon", Default = false, SetName = "holdparams", HelpText = "Run as daemon")]
            public bool Daemon { get; set; }

            [Option("daemonendtime", SetName = "holdparams", HelpText = "End time for daemon (format HH:mm 24-hour)")]
            public string? DaemonEndTime { get; set; }

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
                WriteLine("Credentials file not found. Please create ecobee_credentials.txt in the same directory as this executable.");
                return 1;
            }

            _appApiKey = await ReadApiKeyFileAsync();
            var client = new Client(_appApiKey, ReadTokenFileAsync, WriteTokenFileAsync);

            if (!await HaveTokenFileAsync())
            {
                WriteLine("Getting new tokens");
                var pin = await client.GetPinAsync();

                WriteLine("Pin: " + pin.EcobeePin);
                WriteLine("You have " + pin.ExpiresIn + " minutes to enter this on the Ecobee site and hit enter.");
                Console.ReadLine();

                await client.GetAccessTokenAsync(pin.Code);
            }
            else
            {
                VerboseWriteLine("Loading existing tokens");
            }

            var initialThermostatResponse = await GetThermostatAsync(client);

            if (options.InfoBefore)
            {
                PrintStatus(initialThermostatResponse);
            }

            if (options.Daemon)
            {
                return await RunDaemon(options, client);
            }

            var thermostat = initialThermostatResponse.GetFirstThermostat();

            if (thermostat.Runtime.DesiredCool is null)
            {
                WriteLine("Desired cool temperature not set");
                return 1;
            }
            if (thermostat.Runtime.DesiredHeat is null)
            {
                WriteLine("Desired heat temperature not set");
                return 1;
            }
            if (thermostat.Settings.HeatCoolMinDelta is null)
            {
                WriteLine("Heat cool min delta not set");
                return 1;
            }
            var currentDesiredCoolTemp = ConvertTemperature(thermostat.Runtime.DesiredCool.Value);
            var currentDesiredHeatTemp = ConvertTemperature(thermostat.Runtime.DesiredHeat.Value);
            var heatCoolMinDelta = ConvertTemperature(thermostat.Settings.HeatCoolMinDelta.Value);

            // https://github.com/i8beef/HomeAutio.Mqtt.Ecobee/blob/master/src/HomeAutio.Mqtt.Ecobee/EcobeeMqttService.cs#L107

            var holdParams = new SetHoldParams();

            // Handle input to set settings............................................................................

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
                    WriteLine("Invalid fan mode");
                    return 1;
                }
                holdParams.Fan = mode;
            }

            if (options.Cool is not null)
            {
                holdParams.CoolHoldTemp = ConvertTemperature(ParsePossiblyRelativeTemperature(options.Cool, currentDesiredCoolTemp));
            }

            if (options.Heat is not null)
            {
                holdParams.HeatHoldTemp = ConvertTemperature(ParsePossiblyRelativeTemperature(options.Heat, currentDesiredHeatTemp));
            }

            // NOTE: This appears to have no effect?
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
                    WriteLine("Invalid hold mode");
                    return 1;
                }
                holdParams.HoldType = mode;
            }
            else
            {
                // NOTE: Omitting this parameter seems to behave differently from any of the choices, but not sure exactly how it behaves
                // holdParams.HoldType = "nextTransition";
            }

            // NOTE: This doesn't work?
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
            //         WriteLine("Invalid hold mode");
            //         return 1;
            //     }
            //     holdParams.HoldClimateRef = mode;
            // }

            // Validation..............................................................................................

            if (holdParams.HeatHoldTemp is not null && holdParams.CoolHoldTemp is not null && holdParams.CoolHoldTemp - holdParams.HeatHoldTemp < heatCoolMinDelta)
            {
                WriteLine("Heat temperature must be less than cool temperature by at least " + heatCoolMinDelta + " degrees");
                return 1;
            }
            else if (holdParams.HeatHoldTemp is null && holdParams.CoolHoldTemp is not null)
            {
                holdParams.HeatHoldTemp = ConvertTemperature((ConvertTemperature(holdParams.CoolHoldTemp.Value) - currentDesiredHeatTemp < heatCoolMinDelta) ? (ConvertTemperature(holdParams.CoolHoldTemp.Value) - heatCoolMinDelta) : currentDesiredHeatTemp);
            }
            else if (holdParams.CoolHoldTemp is null && holdParams.HeatHoldTemp is not null)
            {
                holdParams.CoolHoldTemp = ConvertTemperature((currentDesiredCoolTemp - ConvertTemperature(holdParams.HeatHoldTemp.Value) < heatCoolMinDelta) ? (ConvertTemperature(holdParams.HeatHoldTemp.Value) + heatCoolMinDelta) : currentDesiredCoolTemp);
            }

            if (holdParams.CoolHoldTemp is not null && holdParams.CoolHoldTemp < thermostat.Settings.CoolRangeLow || holdParams.CoolHoldTemp > thermostat.Settings.CoolRangeHigh)
            {
                WriteLine("Cool temperature out of range");
                return 1;
            }
            if (holdParams.HeatHoldTemp is not null && holdParams.HeatHoldTemp < thermostat.Settings.HeatRangeLow || holdParams.HeatHoldTemp > thermostat.Settings.HeatRangeHigh)
            {
                WriteLine("Heat temperature out of range");
                return 1;
            }

            // Perform update..........................................................................................

            await UpdateThermostatAsync(client, holdParams);

            if (options.InfoAfter)
            {
                var timeoutTime = DateTime.Now.AddSeconds(options.InfoAfterTimeout);
                var finalThermostatResponse = await GetThermostatAsync(client);
                while (finalThermostatResponse.GetFirstThermostat().Runtime.LastModified == initialThermostatResponse.GetFirstThermostat().Runtime.LastModified)
                {
                    if (DateTime.Now > timeoutTime)
                    {
                        WriteLine("Timeout waiting for thermostat to update");
                        break;
                    }
                    if (_verbose || true)
                    {
                        PrintStatus(finalThermostatResponse);
                    }
                    WriteLine("Waiting for thermostat to update");
                    await Task.Delay(1000);
                    finalThermostatResponse = await GetThermostatAsync(client);
                }
                await Task.Delay(1000);
                finalThermostatResponse = await GetThermostatAsync(client);
                PrintStatus(finalThermostatResponse);
            }

            if (options.Wait)
            {
                WriteLine();
                WriteLine("Press any key to continue...");
                Console.ReadKey();
            }

            return 0;
        }

        private static async Task<int> RunDaemon(Options options, Client client)
        {
            if (options.Heat is null || options.Cool is null)
            {
                WriteLine("Heat and cool temperatures must be specified when running as daemon");
                return 1;
            }
            var targetHeat = ParseTemperature(options.Heat);
            var targetCool = ParseTemperature(options.Cool);
            if (targetHeat >= targetCool)
            {
                WriteLine("Heat temperature must be less than cool temperature");
                return 1;
            }

            DateTime? endTime = (options.DaemonEndTime is null) ? null : DateTime.ParseExact(options.DaemonEndTime, "HH:mm", CultureInfo.InvariantCulture);
            endTime = (endTime is not null && endTime < DateTime.Now) ? endTime.Value.AddDays(1) : endTime;

            var thermostatResponse = await GetThermostatAsync(client);
            var thermostat = thermostatResponse.GetFirstThermostat();
            if (thermostat.Settings.HeatCoolMinDelta is null)
            {
                WriteLine("Heat cool min delta not set");
                return 1;
            }
            var heatCoolMinDelta = ConvertTemperature(thermostat.Settings.HeatCoolMinDelta.Value);

            while (true)
            {
                await Task.Delay(60*1000);

                try
                {
                    if (endTime is not null && DateTime.Now > endTime)
                    {
                        WriteLine("Daemon ending");
                        break;
                    }

                    thermostatResponse = await GetThermostatAsync(client);
                    // PrintStatus(thermostatResponse);
                    thermostat = thermostatResponse.GetFirstThermostat();

                    if (thermostat.Runtime.ActualTemperature == null)
                    {
                        WriteLine("Actual temperature not available");
                        continue;
                    }
                    var currentTemperature = ConvertTemperature(thermostat.Runtime.ActualTemperature.Value);
                    VerboseWriteLine($"Temperature: {currentTemperature}");

                    if (currentTemperature <= targetHeat && (thermostat.Runtime.DesiredHeat is null || ConvertTemperature(thermostat.Runtime.DesiredHeat.Value) != targetHeat))
                    {
                        WriteLine("Setting hold to heat");
                        await UpdateThermostatAsync(client, new SetHoldParams
                        {
                            HeatHoldTemp = ConvertTemperature(targetHeat),
                            CoolHoldTemp = ConvertTemperature(targetHeat + heatCoolMinDelta),
                        });
                    }
                    else if (currentTemperature >= targetCool && (thermostat.Runtime.DesiredCool is null || ConvertTemperature(thermostat.Runtime.DesiredCool.Value) != targetCool))
                    {
                        WriteLine("Setting hold to cool");
                        await UpdateThermostatAsync(client, new SetHoldParams
                        {
                            HeatHoldTemp = ConvertTemperature(targetCool - heatCoolMinDelta),
                            CoolHoldTemp = ConvertTemperature(targetCool),
                        });
                    }
                }
                catch (HttpRequestException e)
                {
                    WriteLine("Error: " + e.Message);
                    continue;
                }
            }

            return 0;
        }

        private static void PrintStatus(ThermostatResponse thermostatResponse)
        {
            var thermostat = thermostatResponse.ThermostatList.First();
            WriteLine("Current Status:");
            // WriteLine($"  Name: {thermostat.Name}");
            WriteLine($"  Temperature: {thermostat.Runtime.ActualTemperature}");
            WriteLine($"  Humidity: {thermostat.Runtime.ActualHumidity}");
            WriteLine($"  Mode: {thermostat.Settings.HvacMode}");
            WriteLine($"  Desired Temperature Range: {thermostat.Runtime.DesiredHeat} - {thermostat.Runtime.DesiredCool}");
            WriteLine($"  Desired Fan: {thermostat.Runtime.DesiredFanMode}");
            WriteLine($"  Equipment Status: {thermostat.EquipmentStatus}");
            WriteLine($"  Last Status Modified: {thermostat.Runtime.LastStatusModified}");
            WriteLine($"  Last Modified: {thermostat.Runtime.LastModified}");
            if (thermostat.Events.Count > 0)
            {
                var currentEvent = thermostat.Events.First();
                WriteLine($"  Current Event: End Time: {currentEvent.EndTime}");
            }
        }

        private static decimal ConvertTemperature(int temperature) => Convert.ToDecimal(temperature) / 10;  // Convert from API's tenths of a degree to degrees
        private static int ConvertTemperature(decimal temperature) => Convert.ToInt32(temperature * 10);  // Convert from degrees to API's tenths of a degree

        private static decimal ParseTemperature(string temperature) => Convert.ToDecimal(temperature);

        private static decimal ParsePossiblyRelativeTemperature(string temperature, decimal currentTemperature)
        {
            if (temperature.StartsWith("+"))
            {
                return currentTemperature + ParseTemperature(temperature.Substring(1));
            }
            else if (temperature.StartsWith("-"))
            {
                return currentTemperature - ParseTemperature(temperature.Substring(1));
            }
            else
            {
                return ParseTemperature(temperature);
            }
        }

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
                    // NOTE: Not authorized?
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

        private static async Task UpdateThermostatAsync(Client client, SetHoldParams holdParams)
        {
            var updateRequest = new ThermostatUpdateRequest
            {
                Selection = new Selection
                {
                    SelectionType = "registered"
                },
                Functions = new List<Function>
                {
                    new SetHoldFunction
                    {
                        Params = holdParams
                    }
                }
            };

            VerboseWriteLine(JsonSerializer<ThermostatUpdateRequest>.Serialize(updateRequest), true);
            var updateResponse = await client.PostAsync<ThermostatUpdateRequest, Response>(updateRequest);
            VerboseWriteLine(JsonSerializer<Response>.Serialize(updateResponse), true);
        }

        private static async Task WriteTokenFileAsync(StoredAuthToken storedAuthToken, CancellationToken cancellationToken = default)
        {
            // Cache the returned tokens
            _currentAuthToken = storedAuthToken;

            // Write token to persistent store
            var text = new System.Text.StringBuilder();
            text.AppendLine(_appApiKey);
            text.AppendLine($"{storedAuthToken.TokenExpiration:MM/dd/yy hh:mm:ss tt}");
            text.AppendLine(storedAuthToken.AccessToken);
            text.AppendLine(storedAuthToken.RefreshToken);

            await File.WriteAllTextAsync(CredentialsFilePath, text.ToString(), cancellationToken);
        }

        private static async Task<StoredAuthToken?> ReadTokenFileAsync(CancellationToken cancellationToken = default)
        {
            if (_currentAuthToken == null && File.Exists(CredentialsFilePath))
            {
                var fileText = await File.ReadAllLinesAsync(CredentialsFilePath, cancellationToken);
                _currentAuthToken = new StoredAuthToken
                {
                    TokenExpiration = DateTime.Parse(fileText[1]),
                    AccessToken = fileText[2],
                    RefreshToken = fileText[3],
                };

                VerboseWriteLine("Access Token: " + _currentAuthToken.AccessToken);
                VerboseWriteLine("Refresh Token: " + _currentAuthToken.RefreshToken);
                VerboseWriteLine("Token Expiration: " + _currentAuthToken.TokenExpiration);
            }

            return _currentAuthToken;
        }

        private static async Task<bool> HaveTokenFileAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(CredentialsFilePath))
            {
                return false;
            }
            var fileText = await File.ReadAllLinesAsync(CredentialsFilePath, cancellationToken);
            if (fileText.Length < 4)
            {
                return false;
            }
            return true;
        }

        private static async Task<string> ReadApiKeyFileAsync(CancellationToken cancellationToken = default)
        {
            var fileText = await File.ReadAllLinesAsync(CredentialsFilePath, cancellationToken);
            return fileText[0].Trim();
        }

        private static void WriteLine(string? message = null)
        {
            if (message is null)
            {
                Console.WriteLine();
            }
            else
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Console.WriteLine($"[{timestamp}] {message}");
            }
        }

        private static void VerboseWriteLine(string message, bool force = false)
        {
            if (_verbose || force)
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Console.WriteLine($"[{timestamp}] {message}");
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
