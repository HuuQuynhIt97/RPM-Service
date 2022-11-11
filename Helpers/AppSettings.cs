using System;
using System.Collections.Generic;
using System.Text;

namespace IoTService.Helpers
{
    public class AppSettings
    {
        public string PortName { get; set; }
        public string Building { get; set; }
        public int MachineID { get; set; }
        public string SignalrUrl { get; set; }
        public string ConnectionStrings { get; set; }
    }
}
