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
            [Option('f', "fan", HelpText = "Set desired fan mode.")]
            public string? Fan { get; set; }

            [Option('c', "cool", HelpText = "Set desired cool temperature.")]
            public decimal? Cool { get; set; }

            [Option('h', "heat", HelpText = "Set desired heat temperature.")]
            public decimal? Heat { get; set; }

            [Option('v', "verbose", Default = false, HelpText = "Prints all messages to standard output.")]
            public bool Verbose { get; set; }
        }

        private static bool _verbose = false;
        private static StoredAuthToken? _currentAuthToken;

        private static async Task<int> RunOptionsAndReturnExitCode(Options options)
        {
            _verbose = options.Verbose;

            var appApiKey = await ReadApiKeyFileAsync();
            var client = new Client(appApiKey, ReadTokenFileAsync, WriteTokenFileAsync);

            if (!File.Exists(@"token.txt"))
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
            PrintStatus(initialThermostatResponse);

            var thermostat = initialThermostatResponse.GetFirstThermostat();
            var currentDesiredCoolTemp = ConvertTemperature(thermostat.Runtime.DesiredCool);
            var currentDesiredHeatTemp = ConvertTemperature(thermostat.Runtime.DesiredHeat);
            var heatCoolMinDelta = ConvertTemperature(thermostat.Settings.HeatCoolMinDelta);

            // https://github.com/i8beef/HomeAutio.Mqtt.Ecobee/blob/master/src/HomeAutio.Mqtt.Ecobee/EcobeeMqttService.cs#L107

            var updateRequest = new ThermostatUpdateRequest
            {
               Selection = new Selection
               {
                   SelectionType = "registered"
               },
               Functions = new List<Function>(),
            //    Thermostat = new { Settings = new { HvacMode = "auto" } }
            };

            var holdType = "nextTransition";

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
                updateRequest.Functions.Add(new SetHoldFunction
                    {
                        Params = new SetHoldParams
                        {
                            HoldType = holdType,
                            Fan = mode
                        }
                    }
                );
            }

            if (options.Cool != null || true)
            {
                decimal temperature;
                temperature = 75.5m;
                updateRequest.Functions.Add(new SetHoldFunction
                    {
                        Params = new SetHoldParams
                        {
                            HoldType = holdType,
                            CoolHoldTemp = ConvertTemperature(temperature),
                            HeatHoldTemp = ConvertTemperature((temperature - currentDesiredHeatTemp < heatCoolMinDelta) ? (temperature - heatCoolMinDelta) : currentDesiredHeatTemp)
                        }
                    }
                );
            }

            var updateResponse = await client.PostAsync<ThermostatUpdateRequest, Response>(updateRequest);
            // VerboseWriteLine(JsonSerializer<Response>.Serialize(updateResponse));
            Console.WriteLine(JsonSerializer<Response>.Serialize(updateResponse));

            var finalThermostatResponse = await GetThermostatAsync(client);
            while (finalThermostatResponse.GetFirstThermostat().Runtime.LastModified == initialThermostatResponse.GetFirstThermostat().Runtime.LastModified)
            {
                if (_verbose || true)
                {
                    PrintStatus(finalThermostatResponse);
                }
                Console.WriteLine("Waiting for thermostat to update");
                await Task.Delay(5000);
                finalThermostatResponse = await GetThermostatAsync(client);
            }
            await Task.Delay(5000);
            finalThermostatResponse = await GetThermostatAsync(client);
            PrintStatus(finalThermostatResponse);

            Console.ReadLine();

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
        }

        private static decimal ConvertTemperature(int? temperature) => Convert.ToDecimal(temperature) / 10;
        private static int ConvertTemperature(decimal? temperature) => Convert.ToInt32(temperature * 10);

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
            text.AppendLine($"{storedAuthToken.TokenExpiration:MM/dd/yy hh:mm:ss tt}");
            text.AppendLine(storedAuthToken.AccessToken);
            text.AppendLine(storedAuthToken.RefreshToken);

            await File.WriteAllTextAsync(@"token.txt", text.ToString());
        }

        public static async Task<StoredAuthToken?> ReadTokenFileAsync(CancellationToken cancellationToken = default)
        {
            if (_currentAuthToken == null && File.Exists(@"token.txt"))
            {
                var tokenText = await File.ReadAllLinesAsync(@"token.txt");
                _currentAuthToken = new StoredAuthToken
                {
                    TokenExpiration = DateTime.Parse(tokenText[0]),
                    AccessToken = tokenText[1],
                    RefreshToken = tokenText[2]
                };

                VerboseWriteLine("Access Token: " + _currentAuthToken.AccessToken);
                VerboseWriteLine("Refresh Token: " + _currentAuthToken.RefreshToken);
            }

            return _currentAuthToken;
        }

        public static async Task<string> ReadApiKeyFileAsync(CancellationToken cancellationToken = default)
        {
            var fileText = await File.ReadAllLinesAsync(@"apikey.txt");
            return fileText[0].Trim();
        }

        private static void VerboseWriteLine(string message)
        {
            if (_verbose)
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
