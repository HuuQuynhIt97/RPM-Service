using MessagePack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace IoTService.Dtos
{

    public class StirRawDataDto
    {

        [JsonProperty("m")]
        public int MachineID { get; set; }
        [JsonProperty("r")]
        public double RPM { get; set; }
        [JsonProperty("b")]
        public string Building { get; set; }
        [JsonProperty("d")]
        public int Duration { get; set; }
        [JsonProperty("t")]
        public DateTime CreatedTime { get; set; }

    }
}
