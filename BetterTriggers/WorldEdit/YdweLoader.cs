using BetterTriggers.Models.EditorData;
using BetterTriggers.Models.SaveableData;
using BetterTriggers.Models.Templates;
using BetterTriggers.Utility;
using IniParser.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace BetterTriggers.WorldEdit
{
    /// <summary>
    /// Loads YDWE (Youdian World Editor) trigger data from TOML files
    /// </summary>
    public static class YdweLoader
    {
        // Set this to false to completely disable YDWE loading (for testing)
        private const bool ENABLE_YDWE = true;

        public static void LoadYdweData(bool isTest)
        {
            if (isTest)
                return; // Skip YDWE loading in tests for now

            if (!ENABLE_YDWE)
            {
                Console.WriteLine("YDWE loading is disabled");
                return;
            }

            string ydwePath = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "WorldEditorData", "YDWE");

            if (!Directory.Exists(ydwePath))
            {
                Console.WriteLine($"YDWE directory not found at: {ydwePath}");
                return; // YDWE files not present
            }

            Console.WriteLine($"Loading YDWE data from: {ydwePath}");

            try
            {
                // Load categories and types from define.txt (INI format)
                LoadYdweDefinitions(Path.Combine(ydwePath, "define.txt"));

                // Load events from event.txt (TOML format)
                LoadYdweTomlFunctions(Path.Combine(ydwePath, "event.txt"), TriggerData.EventTemplates, TriggerElementType.Event);

                // Load conditions from condition.txt (TOML format)
                LoadYdweTomlFunctions(Path.Combine(ydwePath, "condition.txt"), TriggerData.ConditionTemplates, TriggerElementType.Condition);

                // Load actions from action.txt (TOML format)
                LoadYdweTomlFunctions(Path.Combine(ydwePath, "action.txt"), TriggerData.ActionTemplates, TriggerElementType.Action);

                // Load calls (functions that return values) from call.txt (TOML format)
                LoadYdweTomlFunctions(Path.Combine(ydwePath, "call.txt"), TriggerData.CallTemplates, TriggerElementType.None);

                Console.WriteLine("YDWE data loaded successfully");
            }
            catch (Exception ex)
            {
                // Log error but don't crash - just skip YDWE if there's a problem
                Console.WriteLine($"Error loading YDWE data: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private static void LoadYdweDefinitions(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            string text = File.ReadAllText(filePath);
            var data = IniFileConverter.GetIniData(text);

            // Load Trigger Categories
            var categories = data.Sections["TriggerCategories"];
            if (categories != null)
            {
                foreach (var category in categories)
                {
                    try
                    {
                        string[] values = category.Value.Split(",");
                        string displayName = values[0];
                        string iconPath = values.Length > 1 ? values[1] : "none";
                        bool shouldDisplay = values.Length < 3; // If no third value, should display

                        // Check if category already exists
                        var existingCategory = Category.Get(category.KeyName);
                        if (existingCategory != null && existingCategory.Icon != null && existingCategory.Icon.Length == 0)
                        {
                            // Category doesn't exist yet (Get returns empty category as hack)
                            // For now, use an empty icon - YDWE categories will use default icons
                            // In the future, we could load custom YDWE icons from the Warcraft 3 installation
                            if (iconPath != "none")
                            {
                                byte[] emptyIcon = new byte[0]; // Use empty icon to avoid file system issues
                                Category.Create(category.KeyName, emptyIcon, displayName, shouldDisplay);
                                Console.WriteLine($"Created YDWE category: {category.KeyName} - {displayName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to load YDWE category {category.KeyName}: {ex.Message}");
                    }
                }
            }

            // Load Trigger Types
            var triggerTypes = data.Sections["TriggerTypes"];
            if (triggerTypes != null)
            {
                foreach (var type in triggerTypes)
                {
                    try
                    {
                        string[] values = type.Value.Split(",");
                        string key = type.KeyName;

                        // Skip if type already exists
                        if (Types.Get(key) != null)
                            continue;

                        bool canBeGlobal = values[1] == "1";
                        bool canBeCompared = values[2] == "1";
                        string displayName = values[3];
                        string baseType = values.Length >= 5 ? values[4] : null;

                        Types.Create(key, canBeGlobal, canBeCompared, displayName, baseType);
                        Console.WriteLine($"Created YDWE type: {key} - {displayName}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to load YDWE type {type.KeyName}: {ex.Message}");
                    }
                }
            }
        }

        private static void LoadYdweTomlFunctions(string filePath, Dictionary<string, FunctionTemplate> dictionary, TriggerElementType elementType)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"YDWE file not found: {filePath}");
                return;
            }

            Console.WriteLine($"Loading YDWE {elementType} from: {Path.GetFileName(filePath)}");

            string tomlText = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            TomlTable table;

            try
            {
                table = Toml.ToModel(tomlText);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing TOML file {filePath}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return;
            }

            int loadedCount = 0;

            foreach (var entry in table)
            {
                string functionName = entry.Key;
                if (!(entry.Value is TomlTable functionData))
                    continue;

                // Skip if function already exists (don't override base WE functions)
                if (dictionary.ContainsKey(functionName))
                    continue;

                try
                {
                    string title = GetTomlString(functionData, "title", "");
                    string description = GetTomlString(functionData, "description", "");
                    string comment = GetTomlString(functionData, "comment", "");
                    string category = GetTomlString(functionData, "category", "TC_NOTHING");
                    string scriptName = GetTomlString(functionData, "script_name", null);
                    string returns = GetTomlString(functionData, "returns", null);

                    // Determine return type based on element type
                    string returnType;
                    if (elementType == TriggerElementType.Event)
                        returnType = "event";
                    else if (elementType == TriggerElementType.Condition)
                        returnType = "boolean";
                    else if (elementType == TriggerElementType.Action)
                        returnType = "nothing";
                    else // TriggerElementType.None (calls)
                        returnType = returns ?? "nothing";

                    // Parse parameters
                    List<ParameterTemplate> parameters = new List<ParameterTemplate>();
                    if (functionData.TryGetValue("args", out object argsObj) && argsObj is TomlTableArray argsArray)
                    {
                        foreach (var argItem in argsArray)
                        {
                            if (argItem is TomlTable argTable)
                            {
                                string argType = GetTomlString(argTable, "type", "");

                                // Skip "nothing" parameters
                                if (argType == "nothing")
                                    continue;

                                parameters.Add(new ParameterTemplate() { returnType = argType });
                            }
                        }
                    }

                    // Create function template
                    FunctionTemplate template = new FunctionTemplate(elementType)
                    {
                        value = functionName,
                        name = title,
                        paramText = description,
                        category = category,
                        scriptName = scriptName ?? functionName,
                        returnType = returnType,
                        parameters = parameters
                    };

                    // Add to dictionaries
                    dictionary.Add(functionName, template);
                    TriggerData.FunctionsAll.Add(functionName, template);

                    // Add to display name mapping
                    if (!TriggerData.ParamDisplayNames.ContainsKey(functionName))
                        TriggerData.ParamDisplayNames.Add(functionName, title);
                    if (!TriggerData.ParamCodeText.ContainsKey(functionName))
                        TriggerData.ParamCodeText.Add(functionName, description);
                    if (!TriggerData.FunctionCategories.ContainsKey(functionName))
                        TriggerData.FunctionCategories.Add(functionName, category);

                    // Mark as BoolExpr if it's a condition
                    if (elementType == TriggerElementType.Condition)
                    {
                        TriggerData.BoolExprTempaltes.Add(functionName);
                    }

                    loadedCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading YDWE function {functionName}: {ex.Message}");
                }
            }

            Console.WriteLine($"Loaded {loadedCount} YDWE {elementType} functions");
        }

        private static string GetTomlString(TomlTable table, string key, string defaultValue)
        {
            if (table.TryGetValue(key, out object value) && value is string str)
                return str;
            return defaultValue;
        }
    }
}
