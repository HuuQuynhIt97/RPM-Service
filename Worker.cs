using System;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IoTService.Helpers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IoTService.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Data.SQLite;
using System.Data;
using Dapper;

namespace IoTService
{
    public class Worker : BackgroundService
    {
        SerialPort _serialPort;
        bool _continue;
        float before;
        DateTime lastTime;
        private readonly AppSettings _appSettings;
        private readonly ILogger<Worker> _logger;
        HubConnectionState state = HubConnectionState.Disconnected;
        HubConnection _connection;

        public Worker(ILogger<Worker> logger, AppSettings appSettings)
        {
            _logger = logger;
            _appSettings = appSettings;
        }
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker Start: {time}", DateTimeOffset.Now);
            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _logger.LogInformation("Worker Stop: {time}", DateTimeOffset.Now);
            await base.StopAsync(cancellationToken);
        }

        public async Task<bool> ConnectWithRetryAsync(CancellationToken token)
        {
            // Keep trying to until we can start or the token is canceled.
            while (true)
            {

                try
                {
                    await _connection.StartAsync(token);
                    // Debug.Assert(_connection.State == HubConnectionState.Connected);
                    _logger.LogInformation($"The signalr client connected at {DateTime.Now.ToString()}");

                    state = HubConnectionState.Connected;
                    // await _connection.InvokeAsync("JoinHub", _appSettings.MachineID);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);

                    // Failed to connect, trying again in 5000 ms.
                    // Debug.Assert(_connection.State == HubConnectionState.Disconnected);
                    await Task.Delay(5000);
                    _logger.LogInformation($"The signalr client is reconnecting at {DateTime.Now.ToString()}");

                }
                if (state == HubConnectionState.Connected)
                {
                    return true;
                }
            }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _connection = new HubConnectionBuilder()
         .WithUrl(_appSettings.SignalrUrl)
         .WithAutomaticReconnect()
         .Build();
            MyConsole.Info(JsonConvert.SerializeObject(_appSettings));
            _connection.On<string, string>("Welcome", (scalingMachineID, message) =>
            {
                if (scalingMachineID == _appSettings.MachineID.ToString())
                {
                    var receiveValue = JsonConvert.SerializeObject(new
                    {
                        scalingMachineID,
                        message
                    });

                    MyConsole.Info($"#### ### Data => {receiveValue}");
                }
            });

            _connection.Closed += async (error) =>
            {
                _logger.LogError(error.Message);
                await Task.Delay(new Random().Next(0, 5) * 1000);
                _logger.LogError($"Envent: Closed - The signalr client is restarting!");
                await _connection.StartAsync();
            };
            _connection.Reconnecting += async error =>
            {
                _logger.LogInformation($"State Hub {_connection.State} - State Global {state}");
                _logger.LogInformation($"Connection started reconnecting due to an error: {error.Message}");

                if (_connection.State == HubConnectionState.Reconnecting)
                    state = HubConnectionState.Disconnected;
                while (state == HubConnectionState.Disconnected)
                {
                    if (await ConnectWithRetryAsync(stoppingToken))
                    {
                        break;
                    }
                }
            };
            _connection.Reconnected += async (connectionId) =>
            {
                _logger.LogInformation($"Connection successfully reconnected. The ConnectionId is now: {connectionId}");
                state = HubConnectionState.Connected;
                while (true)
                    if (await ConnectWithRetryAsync(stoppingToken)) break;
            };

            while (state == HubConnectionState.Disconnected)
                if (await ConnectWithRetryAsync(stoppingToken)) break;

            _logger.LogInformation($"#### ### ClientId: {_connection.ConnectionId}");
            string message;
            StringComparer stringComparer = StringComparer.OrdinalIgnoreCase;
            Thread readThread = new Thread(async () =>
                 await Read()
                );

            
            // Create a new SerialPort object with default settings.
            _logger.LogInformation($"#### ### Serial Port is established");

            _serialPort = new SerialPort();

            // Allow the user to set the appropriate properties.
            string port = _appSettings.PortName;
            _serialPort.PortName = port;

            _logger.LogInformation($"#### ### {port}");

            _serialPort.ReadTimeout = 2000;
            _serialPort.WriteTimeout = 2000;
            _serialPort.Open();
            _logger.LogWarning($"#### ### #### ### Serial Port is already open at {DateTime.Now.ToString()}");

            _continue = true;
            readThread.Start();
            while (_continue)
            {
                // emit ve client
                message = Console.ReadLine();

                if (stringComparer.Equals("quit", message))
                {
                    _continue = false;
                }
                else
                {
                    //_serialPort.WriteLine(
                    //    String.Format("<{0}>: {1}", name, message));
                }
                Thread.Sleep(10);//sleep thread at every 10ml
            }
            readThread.Join();
            _serialPort.Close();

        }
        private static double TimeDifferent(DateTime lastDateTime)
        {
            var time = DateTime.Now - lastDateTime;
            return time.TotalSeconds;

        }
        public static int RandomNumber(int minimum = 200, int maximum = 250)
        {
            Random rnd = new Random();
            int data = rnd.Next(minimum, maximum);
            return data;
        }
        public async Task Read()
        {
            int duration = 1;

            while (_continue)
            {
                try
                {
                    string message = _serialPort.ReadLine();

                    //var rpm = RandomNumber();
                    var rpm = System.Text.RegularExpressions.Regex.Replace(message, @"[^0-9a-zA-Z:,]+", "").ToDouble();
                    _logger.LogInformation("--------------------------Serial port value: " + rpm);
                    if (rpm > 0)
                    {
                        lastTime = DateTime.Now;

                        if (TimeDifferent(lastTime) > 1)
                        {
                            duration = 1;
                        }
                        var data = new StirRawDataDto
                        {
                            MachineID = _appSettings.MachineID,
                            Building = _appSettings.Building,
                            CreatedTime = DateTime.Now,
                            Duration = duration++,
                            RPM = rpm
                        };
                        await _connection.InvokeAsync("Welcome", _appSettings.MachineID.ToString(), rpm.ToString());
                        await Create(data);
                        await Task.Delay(100);
                    }

                }
                catch (TimeoutException ex)
                {
                    duration = 1;
                    _logger.LogError("Can not read data, " + ex.Message);
                }

            }

        }

        private async Task Create(StirRawDataDto model)
        {
            using (IDbConnection db = new SQLiteConnection(_appSettings.ConnectionStrings))
            {
                string sqlQuery = @"Insert Into StirRawData 
                                    ( MachineID, RPM, Building, Duration, CreatedTime) 
                                    Values(@MachineID, @RPM, @Building, @Duration, @CreatedTime)";
                try
                {
                    var rowsAffected = await db.ExecuteAsync(sqlQuery, model);

                    Console.WriteLine("insert! " + rowsAffected);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to insert! " + ex.Message);
                }
            }
        }
        private string ScanPortName()
        {
            string portResult = string.Empty;
            // Get a list of serial port names.
            string[] ports = SerialPort.GetPortNames();

            // Display each port name to the console.
            foreach (string port in ports)
            {
                if (_appSettings.PortName.ToLower() == port.ToLower())
                {
                    return port;
                }
            }
            return portResult;
        }

    }
}
