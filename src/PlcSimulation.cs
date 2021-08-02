namespace OpcPlc
{
    using System.Diagnostics;
    using static OpcPlc.Program;

    public class PlcSimulation
    {
        /// <summary>
        /// Flags for node generation.
        /// </summary>
        public static bool AddComplexTypeBoiler { get; set; }
        public static bool AddAlarmSimulation { get; set; }
        public static bool AddSimpleEventsSimulation { get; set; }
        public static bool AddReferenceTestSimulation { get; set; }
        public static string DeterministicAlarmSimulationFile { get; set; }

        public static uint EventInstanceCount { get; set; } = 0;
        public static uint EventInstanceRate { get; set; } = 1000; // ms.

        /// <summary>
        /// Simulation data.
        /// </summary>
        public static int SimulationCycleCount { get; set; } = SIMULATION_CYCLECOUNT_DEFAULT;
        public static int SimulationCycleLength { get; set; } = SIMULATION_CYCLELENGTH_DEFAULT;

        /// <summary>
        /// Ctor for simulation server.
        /// </summary>
        public PlcSimulation(PlcServer plcServer)
        {
            _plcServer = plcServer;
        }

        /// <summary>
        /// Start the simulation.
        /// </summary>
        public void Start()
        {
            if (EventInstanceCount > 0)
            {
                _eventInstanceGenerator = EventInstanceRate >= 50 || !Stopwatch.IsHighResolution ?
                    _plcServer.TimeService.NewTimer(_plcServer.PlcNodeManager.UpdateEventInstances, EventInstanceRate) :
                    _plcServer.TimeService.NewFastTimer(_plcServer.PlcNodeManager.UpdateVeryFastEventInstances, EventInstanceRate);
            }

            if (AddComplexTypeBoiler)
            {
                _boiler1Generator = _plcServer.TimeService.NewTimer(_plcServer.PlcNodeManager.UpdateBoiler1, 1000);
            }

            // Start simulation of nodes from plugin nodes list.
            foreach (var pluginNodes in Program.PluginNodes)
            {
                pluginNodes.StartSimulation();
            }
        }

        /// <summary>
        /// Stop the simulation.
        /// </summary>
        public void Stop()
        {
            Disable(_eventInstanceGenerator);
            Disable(_boiler1Generator);

            // Stop simulation of nodes from plugin nodes list.
            foreach (var pluginNodes in Program.PluginNodes)
            {
                pluginNodes.StopSimulation();
            }
        }

        private void Disable(ITimer timer)
        {
            if (timer == null)
            {
                return;
            }

            timer.Enabled = false;
        }

        private const int SIMULATION_CYCLECOUNT_DEFAULT = 50;          // in cycles
        private const int SIMULATION_CYCLELENGTH_DEFAULT = 100;        // in msec

        private readonly PlcServer _plcServer;

        private ITimer _eventInstanceGenerator;
        private ITimer _boiler1Generator;
    }
}