using CommandLine;
using I8Beef.Ecobee;
using I8Beef.Ecobee.Protocol;
using I8Beef.Ecobee.Protocol.Objects;
using I8Beef.Ecobee.Protocol.Functions;
using I8Beef.Ecobee.Protocol.Thermostat;

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
            public string? Cool { get; set; }

            [Option('h', "heat", HelpText = "Set desired heat temperature.")]
            public string? Heat { get; set; }

            [Option('v', "verbose", HelpText = "Prints all messages to standard output.")]
            public bool Verbose { get; set; }
        }

        private static bool _verbose = false;
        private static StoredAuthToken? _currentAuthToken;

        private static async Task<int> RunOptionsAndReturnExitCode(Options options)
        {
            _verbose = options.Verbose;

            var appApiKey = "";
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

            VerboseWriteLine("Access Token: " + _currentAuthToken?.AccessToken);
            VerboseWriteLine("Refresh Token: " + _currentAuthToken?.RefreshToken);

            var initialThermostatRequest = new ThermostatRequest
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
            var initialThermostatResponse = await client.GetAsync<ThermostatRequest, ThermostatResponse>(initialThermostatRequest);
            VerboseWriteLine(JsonSerializer<ThermostatResponse>.Serialize(initialThermostatResponse));

            var updateRequest = new ThermostatUpdateRequest
            {
               Selection = new Selection
               {
                   SelectionType = "registered"
               },
               Functions = new List<Function>(),
            //    Thermostat = new { Settings = new { HvacMode = "auto" } }
            };

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
                            HoldType = "nextTransition",
                            Fan = mode
                        }
                    }
                );
            }

            var updateResponse = await client.PostAsync<ThermostatUpdateRequest, Response>(updateRequest);
            VerboseWriteLine(JsonSerializer<Response>.Serialize(updateResponse));

            var finalThermostatRequest = new ThermostatRequest
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
            var finalThermostatResponse = await client.GetAsync<ThermostatRequest, ThermostatResponse>(finalThermostatRequest);
            VerboseWriteLine(JsonSerializer<ThermostatResponse>.Serialize(finalThermostatResponse));

            return 0;
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

                Console.WriteLine("Access Token: " + _currentAuthToken.AccessToken);
                Console.WriteLine("Refresh Token: " + _currentAuthToken.RefreshToken);
            }

            return _currentAuthToken;
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
