using IniParser.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BetterTriggers.WorldEdit
{
    /// <summary>
    /// Custom parser for YDWE trigger data files.
    /// YDWE files use a TOML-like format that is not compatible with standard TOML parsers.
    /// </summary>
    public static class YDWEParser
    {
        public class YDWEFunction
        {
            public string Name { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string Comment { get; set; }
            public string Category { get; set; }
            public string ScriptName { get; set; }
            public List<YDWEArgument> Arguments { get; set; } = new List<YDWEArgument>();
        }

        public class YDWEArgument
        {
            public string Type { get; set; }
            public string Default { get; set; }
            public string Min { get; set; }
            public string Max { get; set; }
        }

        /// <summary>
        /// Parses a YDWE file and converts it to IniData format compatible with TriggerData loading.
        /// </summary>
        /// <param name="filePath">Path to the YDWE file</param>
        /// <param name="sectionName">Name of the section to create (e.g., "TriggerEvents", "TriggerActions")</param>
        /// <returns>IniData object containing parsed functions</returns>
        public static IniData ParseYDWEFile(string filePath, string sectionName)
        {
            var functions = ParseYDWEFunctions(filePath);
            return ConvertToIniData(functions, sectionName);
        }

        /// <summary>
        /// Parses YDWE file and returns list of functions.
        /// </summary>
        private static List<YDWEFunction> ParseYDWEFunctions(string filePath)
        {
            var functions = new List<YDWEFunction>();

            if (!File.Exists(filePath))
                return functions;

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            YDWEFunction currentFunction = null;
            YDWEArgument currentArg = null;
            bool inArgsSection = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // Check for new function section [FunctionName]
                if (line.StartsWith("[") && line.EndsWith("]") && !line.StartsWith("[["))
                {
                    // Save previous function if exists
                    if (currentFunction != null)
                    {
                        functions.Add(currentFunction);
                    }

                    // Start new function
                    string functionName = line.Substring(1, line.Length - 2);
                    currentFunction = new YDWEFunction { Name = functionName };
                    inArgsSection = false;
                    currentArg = null;
                    continue;
                }

                // Check for args section [[.args]]
                if (line == "[[.args]]")
                {
                    // Create a new argument
                    currentArg = new YDWEArgument();
                    if (currentFunction != null)
                    {
                        currentFunction.Arguments.Add(currentArg);
                    }
                    inArgsSection = true;
                    continue;
                }

                // Parse key-value pair
                if (line.Contains("=") && currentFunction != null)
                {
                    int equalIndex = line.IndexOf('=');
                    string key = line.Substring(0, equalIndex).Trim();
                    string value = line.Substring(equalIndex + 1).Trim();

                    // Remove quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }

                    // If we're in an args section, parse arg properties
                    if (inArgsSection && currentArg != null)
                    {
                        switch (key.ToLower())
                        {
                            case "type":
                                currentArg.Type = value;
                                break;
                            case "default":
                                currentArg.Default = value;
                                break;
                            case "min":
                                currentArg.Min = value;
                                break;
                            case "max":
                                currentArg.Max = value;
                                break;
                        }
                    }
                    else
                    {
                        // Parse function properties
                        switch (key.ToLower())
                        {
                            case "title":
                                currentFunction.Title = value;
                                break;
                            case "description":
                                currentFunction.Description = value;
                                break;
                            case "comment":
                                currentFunction.Comment = value;
                                break;
                            case "category":
                                currentFunction.Category = value;
                                break;
                            case "script_name":
                                currentFunction.ScriptName = value;
                                break;
                        }
                    }
                }
            }

            // Add the last function
            if (currentFunction != null)
            {
                functions.Add(currentFunction);
            }

            return functions;
        }

        /// <summary>
        /// Converts parsed YDWE functions to IniData format.
        /// Format: FunctionName=0,type1,type2,type3
        /// </summary>
        private static IniData ConvertToIniData(List<YDWEFunction> functions, string sectionName)
        {
            var iniData = new IniData();
            var section = new SectionData(sectionName);

            foreach (var func in functions)
            {
                // Build the function definition line
                // Format: FunctionName=0,type1,type2,type3
                var paramTypes = new List<string> { "0" }; // First parameter is always 0
                foreach (var arg in func.Arguments)
                {
                    if (!string.IsNullOrEmpty(arg.Type))
                    {
                        paramTypes.Add(arg.Type);
                    }
                }

                string functionValue = string.Join(",", paramTypes);
                section.Keys.AddKey(func.Name, functionValue);

                // Add DisplayName
                if (!string.IsNullOrEmpty(func.Title))
                {
                    section.Keys.AddKey($"_{func.Name}DisplayName", $"\"{func.Title}\"");
                }

                // Add Parameters (description)
                if (!string.IsNullOrEmpty(func.Description))
                {
                    section.Keys.AddKey($"_{func.Name}Parameters", $"\"{func.Description}\"");
                }

                // Add Category
                if (!string.IsNullOrEmpty(func.Category))
                {
                    section.Keys.AddKey($"_{func.Name}Category", func.Category);
                }

                // Add ScriptName
                if (!string.IsNullOrEmpty(func.ScriptName))
                {
                    section.Keys.AddKey($"_{func.Name}ScriptName", func.ScriptName);
                }

                // Build defaults string if any arguments have defaults
                var defaults = new List<string>();
                foreach (var arg in func.Arguments)
                {
                    if (!string.IsNullOrEmpty(arg.Default))
                    {
                        defaults.Add(arg.Default);
                    }
                    else
                    {
                        defaults.Add("_");
                    }
                }

                if (defaults.Count > 0 && defaults.Exists(d => d != "_"))
                {
                    string defaultsValue = string.Join(",", defaults);
                    section.Keys.AddKey($"_{func.Name}Defaults", defaultsValue);
                }
            }

            iniData.Sections.Add(section);
            return iniData;
        }
    }
}
