﻿namespace OpcPlc.DeterministicAlarms.Configuration
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class Configuration
    {
        public List<Folder> Folders { get; set; }

        public Script Script { get; set; }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    IgnoreNullValues = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters =
                    {
                        new JsonStringEnumConverter()
                    }
                });
        }

        public static Configuration FromJson(string json)
        {
            return JsonSerializer.Deserialize<Configuration>(json,
                new JsonSerializerOptions
                {
                    IgnoreNullValues = false,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters =
                    {
                        new JsonStringEnumConverter()
                    }
                });
        }
    }
}