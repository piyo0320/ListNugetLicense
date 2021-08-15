using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace ListNugetLicense
{
    public class Contents
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("encoding")]
        public string Encoding { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }
    }
}
