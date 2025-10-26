using BetterTriggers.Models.EditorData;
using BetterTriggers.Models.SaveableData;
using BetterTriggers.Models.Templates;
using BetterTriggers.Utility;
using CASCLib;
using IniParser.Model;
using IniParser.Parser;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using War3Net.Build.Info;
using System.Linq;
using System.Text.RegularExpressions;
using BetterTriggers.Containers;
using War3Net.Common.Extensions;
using System.Windows.Documents;
using BetterTriggers.WorldEdit.GameDataReader;
using System.Windows.Input;

namespace BetterTriggers.WorldEdit
{
    public class TriggerData
    {
        internal static Dictionary<string, PresetTemplate> PresetTemplates = new Dictionary<string, PresetTemplate>();
        internal static Dictionary<string, FunctionTemplate> EventTemplates = new Dictionary<string, FunctionTemplate>();
        internal static Dictionary<string, FunctionTemplate> ConditionTemplates = new Dictionary<string, FunctionTemplate>();
        internal static Dictionary<string, FunctionTemplate> ActionTemplates = new Dictionary<string, FunctionTemplate>();
        internal static Dictionary<string, FunctionTemplate> CallTemplates = new Dictionary<string, FunctionTemplate>();
        internal static Dictionary<string, FunctionTemplate> FunctionsAll = new Dictionary<string, FunctionTemplate>();
        internal static HashSet<string> BoolExprTempaltes = new HashSet<string>();

        internal static Dictionary<string, string> ParamDisplayNames = new Dictionary<string, string>();
        internal static Dictionary<string, string> ParamCodeText = new Dictionary<string, string>();
        internal static Dictionary<string, string> FunctionCategories = new Dictionary<string, string>();

        internal static List<CustomPreset> customPresets = new List<CustomPreset>();

        private static Dictionary<FunctionTemplate, string> Defaults = new Dictionary<FunctionTemplate, string>(); // saves the raw default values so we can operate on them later.
        internal static string customBJFunctions_Jass;
        internal static string customBJFunctions_Lua;

        public static string pathCommonJ;
        public static string pathBlizzardJ;

        private static HashSet<string> btOnlyData = new HashSet<string>();
        private static bool isBT = false;
        private static bool isYDWE = false;

        // Debug logging to file
        private static string debugLogPath = Path.Combine(Directory.GetCurrentDirectory(), "ydwe_debug.log");
        private static void DebugLog(string message)
        {
            string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Console.WriteLine(logMessage);
            try
            {
                File.AppendAllText(debugLogPath, logMessage + Environment.NewLine);
            }
            catch { /* Ignore file write errors */ }
        }

        public static void Load(bool isTest)
        {
            // Clear previous debug log
            try { if (File.Exists(debugLogPath)) File.Delete(debugLogPath); } catch { }

            DebugLog($"=== TriggerData.Load() STARTED ===");
            DebugLog($"IsTest: {isTest}");

            IniData data = null;

            Types.Clear();
            PresetTemplates.Clear();
            EventTemplates.Clear();
            ConditionTemplates.Clear();
            BoolExprTempaltes.Clear();
            ActionTemplates.Clear();
            CallTemplates.Clear();
            FunctionsAll.Clear();
            ParamDisplayNames.Clear();
            ParamCodeText.Clear();
            FunctionCategories.Clear();
            customPresets.Clear();
            Category.Clear();

            DebugLog($"Clearing dictionaries and collections...");

            if (isTest)
            {
                DebugLog($"Test mode: loading test resources");
                string path = Path.Combine(Directory.GetCurrentDirectory(), "TestResources\\triggerdata.txt");
                string triggerdata = File.ReadAllText(path);
                data = IniFileConverter.GetIniData(triggerdata);

                string baseDir = Directory.GetCurrentDirectory() + "\\Resources\\JassHelper\\";
                pathCommonJ = Path.Combine(baseDir, "common.txt");
                pathBlizzardJ = Path.Combine(baseDir, "Blizzardj.txt");
                ScriptGenerator.PathCommonJ = pathCommonJ;
                ScriptGenerator.PathBlizzardJ = pathBlizzardJ;
                ScriptGenerator.JassHelper = $"{System.IO.Directory.GetCurrentDirectory()}\\Resources\\JassHelper\\clijasshelper.exe";
            }
            else
            {
                DebugLog($"Production mode: loading from CASC");
                string baseDir = Directory.GetCurrentDirectory() + "\\Resources\\JassHelper\\";
                if (!Directory.Exists(baseDir))
                {
                    Directory.CreateDirectory(baseDir);
                }

                pathCommonJ = baseDir + "common.j";
                pathBlizzardJ = baseDir + "Blizzard.j";
                ScriptGenerator.PathCommonJ = pathCommonJ;
                ScriptGenerator.PathBlizzardJ = pathBlizzardJ;
                ScriptGenerator.JassHelper = $"{System.IO.Directory.GetCurrentDirectory()}\\Resources\\JassHelper\\jasshelper.exe";

                DebugLog($"Exporting common.j and Blizzard.j from CASC...");
                try
                {
                    WarcraftStorageReader.Export(@"scripts\common.j", pathCommonJ);
                    WarcraftStorageReader.Export(@"scripts\Blizzard.j", pathBlizzardJ);
                    DebugLog($"CASC export completed successfully");
                }
                catch (Exception ex)
                {
                    DebugLog($"ERROR exporting from CASC: {ex.Message}");
                    throw;
                }

                DebugLog($"Opening triggerdata.txt from CASC (with 30 second timeout)...");
                Stream file = null;
                try
                {
                    var fileTask = System.Threading.Tasks.Task.Run(() => WarcraftStorageReader.OpenFile(@"ui\triggerdata.txt"));
                    if (fileTask.Wait(TimeSpan.FromSeconds(30)))
                    {
                        file = fileTask.Result;
                        DebugLog($"triggerdata.txt opened successfully from CASC");
                    }
                    else
                    {
                        DebugLog($"TIMEOUT: triggerdata.txt failed to open within 30 seconds");
                        DebugLog($"This usually indicates CASC is trying to download from CDN and failing");
                        DebugLog($"Check your internet connection or Warcraft 3 installation");
                        throw new TimeoutException("CASC OpenFile operation timed out after 30 seconds");
                    }
                }
                catch (TimeoutException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    DebugLog($"ERROR opening triggerdata.txt from CASC: {ex.Message}");
                    DebugLog($"Exception type: {ex.GetType().Name}");
                    DebugLog($"Stack trace: {ex.StackTrace}");
                    throw;
                }

                var reader = new StreamReader(file);
                DebugLog($"Reading triggerdata.txt content...");
                var text = reader.ReadToEnd();
                DebugLog($"triggerdata.txt content read: {text.Length} characters");

                DebugLog($"Converting triggerdata.txt to IniData...");
                data = IniFileConverter.GetIniData(text);
                DebugLog($"TriggerData.txt loaded successfully");

                // --- TRIGGER CATEGORIES --- //

                var triggerCategories = data.Sections["TriggerCategories"];
                string imageExt = WarcraftStorageReader.ImageExt;
                foreach (var category in triggerCategories)
                {
                    string[] values = category.Value.Split(",");

                    if (values[1] == "none")
                        continue;

                    string WE_STRING = values[0];
                    string texturePath = Path.GetFileName(values[1] + imageExt);
                    bool shouldDisplay = true;
                    if (values.Length == 3)
                        shouldDisplay = false;

                    Stream stream = WarcraftStorageReader.OpenFile(Path.Combine(@"replaceabletextures\worldeditui", texturePath));
                    byte[] image;
                    if (imageExt == ".blp")
                    {
                        image = Images.ReadImage(stream);
                    }
                    else
                    {
                        image = new byte[stream.Length];
                        stream.CopyTo(image, 0, (int)stream.Length);
                    }

                    Category.Create(category.KeyName, image, WE_STRING, shouldDisplay);
                }

                byte[] img;
                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/_map.png");
                Category.Create(TriggerCategory.TC_MAP, img, "???", false);
                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/_editor-triggeraction.png");
                Category.Create(TriggerCategory.TC_ACTION, img, "???", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/_editor-triggercondition.png");
                Category.Create(TriggerCategory.TC_CONDITION_NEW, img, "???", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/_editor-triggerevent.png");
                Category.Create(TriggerCategory.TC_EVENT, img, "Event", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/trigger-error.png");
                Category.Create(TriggerCategory.TC_ERROR, img, "Error", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/trigger-invalid.png");
                Category.Create(TriggerCategory.TC_INVALID, img, "???", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/_ui-editoricon-triggercategories_element.png");
                Category.Create(TriggerCategory.TC_TRIGGER_NEW, img, "???", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/_ui-editoricon-triggercategories_folder.png");
                Category.Create(TriggerCategory.TC_DIRECTORY, img, "???", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/_editor-triggerscript.png");
                Category.Create(TriggerCategory.TC_SCRIPT, img, "???", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/actions-setvariables-alpha.png");
                Category.Create(TriggerCategory.TC_LOCAL_VARIABLE, img, "???", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/ui-editoricon-triggercategories_dialog.png");
                Category.Create(TriggerCategory.TC_FRAMEHANDLE, img, "Frame", true);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/ui-editoricon-triggercategories_actiondefinition.png");
                Category.Create(TriggerCategory.TC_ACTION_DEF, img, "Custom", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/ui-editoricon-triggercategories_conditiondefinition.png");
                Category.Create(TriggerCategory.TC_CONDITION_DEF, img, "Custom", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/ui-editoricon-triggercategories_functiondefinition.png");
                Category.Create(TriggerCategory.TC_FUNCTION_DEF, img, "Custom", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/ui-editoricon-triggercategories_tbd.png");
                Category.Create(TriggerCategory.TC_UNKNOWN, img, "???", false);

                img = File.ReadAllBytes(System.IO.Directory.GetCurrentDirectory() + "/Resources/Icons/actions-parameter-alpha.png");
                Category.Create(TriggerCategory.TC_PARAMETER, img, "???", false);
            }



            DebugLog($"Loading base trigger data from IniData...");
            LoadTriggerDataFromIni(data, isTest);
            DebugLog($"Base trigger data loaded.");

            DebugLog($"Loading translations...");
            LoadTranslations(isTest);
            DebugLog($"Translations loaded.");


            // --- LOAD CUSTOM DATA --- //

            DebugLog($"=== Loading custom BetterTriggers data ===");
            isBT = true;

            // --- Loads in all editor versions --- //

            customBJFunctions_Jass = string.Empty;
            customBJFunctions_Lua = string.Empty;

            DebugLog($"Loading triggerdata_custom.txt...");
            var textCustom = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/triggerdata_custom.txt"));
            var dataCustom = IniFileConverter.GetIniData(textCustom);
            LoadTriggerDataFromIni(dataCustom, isTest);
            DebugLog($"triggerdata_custom.txt loaded");

            textCustom = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/Globals_custom.txt"));
            dataCustom = IniFileConverter.GetIniData(textCustom);
            LoadCustomBlizzardJ(dataCustom);


            // --- Loads depending on version --- //

            if (WarcraftStorageReader.GameVersion >= WarcraftVersion._1_31)
            {
                textCustom = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/triggerdata_custom_31.txt"));
                dataCustom = IniFileConverter.GetIniData(textCustom);
                LoadTriggerDataFromIni(dataCustom, isTest);

                textCustom = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/Globals_custom_31.txt"));
                dataCustom = IniFileConverter.GetIniData(textCustom);
                LoadCustomBlizzardJ(dataCustom);

                customBJFunctions_Jass += File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/FunctionDef_BT_31.txt"));
                customBJFunctions_Lua += File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/FunctionDef_BT_31_Lua.txt"));
            }
            if (WarcraftStorageReader.GameVersion >= WarcraftVersion._1_32)
            {
                textCustom = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/triggerdata_custom_32.txt"));
                dataCustom = IniFileConverter.GetIniData(textCustom);
                LoadTriggerDataFromIni(dataCustom, isTest);
            }
            if (WarcraftStorageReader.GameVersion >= WarcraftVersion._1_33)
            {
                textCustom = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/triggerdata_custom_33.txt"));
                dataCustom = IniFileConverter.GetIniData(textCustom);
                LoadTriggerDataFromIni(dataCustom, isTest);

                //textCustom = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/BlizzardJ_custom_33.txt"));
                //dataCustom = IniFileConverter.GetIniData(textCustom);
                //LoadCustomBlizzardJ(dataCustom);


                customBJFunctions_Jass += File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/FunctionDef_BT_33.txt"));
                customBJFunctions_Lua += File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/Custom/FunctionDef_BT_33_Lua.txt"));
            }


            // --- LOAD YDWE DATA --- //

            LoadYDWEData();
            DebugLog($"[MAIN] YDWE data loading completed, continuing with type extensions...");


            // --- Adds extends to types --- //

            DebugLog($"[MAIN] Creating 'agent' type if it doesn't exist...");
            if (Types.Get("agent") == null)
            {
                Types.Create("agent", false, false, "Agent", string.Empty); // hack
                DebugLog($"[MAIN] 'agent' type created.");
            }
            else
            {
                DebugLog($"[MAIN] 'agent' type already exists (likely from YDWE), skipping creation.");
            }

            DebugLog($"[MAIN] Reading common.j file from: {pathCommonJ}");
            string[] commonJfile = File.ReadAllLines(pathCommonJ);
            DebugLog($"[MAIN] common.j file read successfully, {commonJfile.Length} lines");
            List<string> types = new List<string>();
            DebugLog($"[MAIN] Processing common.j file to extract type definitions...");
            for (int i = 0; i < commonJfile.Length; i++)
            {
                commonJfile[i] = Regex.Replace(commonJfile[i], @"\s+", " ");
                if (commonJfile[i].StartsWith("type"))
                {
                    types.Add(commonJfile[i]);
                }
            }
            DebugLog($"[MAIN] Found {types.Count} type definitions in common.j");

            DebugLog($"[MAIN] Setting type extensions...");
            types.ForEach(line =>
            {
                string[] split = line.Split(" ");
                string type = split[1];
                string extends = split[3];

                var _type = Types.Get(type);
                if (_type != null)
                    Types.Get(type).Extends = extends;
            });
            DebugLog($"[MAIN] Type extensions set successfully");
            DebugLog($"=== TriggerData.Load() COMPLETED SUCCESSFULLY ===");
        }

        private static void LoadTranslations(bool isTest)
        {
            if (isTest)
            {
                return;
            }

            string file;
            if (WarcraftStorageReader.GameVersion >= WarcraftVersion._1_31)
            {
                file = WarcraftStorageReader.ReadAllText(@"_locales\enus.w3mod\ui\triggerstrings.txt", "War3xLocal.mpq");
            }
            else if (WarcraftStorageReader.GameVersion >= WarcraftVersion._1_30 && WarcraftStorageReader.GameVersion < WarcraftVersion._1_31)
            {
                file = WarcraftStorageReader.ReadAllText_Local_1_30(@"ui\triggerstrings.txt");
            }
            else
            {
                file = WarcraftStorageReader.ReadAllText(@"ui\triggerstrings.txt", "War3xLocal.mpq");
            }

            var iniData = new Utility.IniParser.IniData(file);
            foreach (var section in iniData.Sections.Values)
            {
                string lastKeyword = string.Empty;
                foreach (var key in section.Keys)
                {
                    FunctionsAll.TryGetValue(key.Key, out var functionTemplate);
                    if (functionTemplate == null)
                    {
                        continue;
                    }
                    else if (key.Key == lastKeyword)
                    {
                        functionTemplate.paramText = key.Value.Replace("\"", "");
                        ParamCodeText.TryAdd(key.Key, functionTemplate.paramText);
                        continue;
                    }

                    lastKeyword = key.Key;
                    functionTemplate.name = key.Value.Replace("\"", "");
                }
            }
        }

        private static void LoadTriggerDataFromIni(IniData data, bool isTest)
        {
            DebugLog($"[LoadTriggerDataFromIni] Starting to load trigger data from IniData...");

            // --- TRIGGER TYPES (GUI VARIABLE TYPE DEFINITIONS) --- //

            var triggerTypes = data.Sections["TriggerTypes"];
            if (triggerTypes != null)
            {
                DebugLog($"[LoadTriggerDataFromIni] Loading {triggerTypes.Count} trigger types...");
                foreach (var type in triggerTypes)
            {
                string[] values = type.Value.Split(",");
                string key = type.KeyName;
                bool canBeGlobal;
                bool canBeCompared;
                string displayName;
                string baseType = null;
                if (WarcraftStorageReader.GameVersion >= WarcraftVersion._1_28)
                {
                    canBeGlobal = values[1] == "1" ? true : false;
                    canBeCompared = values[2] == "1" ? true : false;
                    displayName = values[3];
                    if (values.Length >= 5)
                        baseType = values[4];
                }
                else
                {
                    canBeGlobal = values[0] == "1" ? true : false;
                    canBeCompared = values[1] == "1" ? true : false;
                    displayName = values[2];
                    if (values.Length >= 4)
                        baseType = values[3];
                }


                Types.Create(key, canBeGlobal, canBeCompared, displayName, baseType);
                }
            }



            // --- TRIGGER PARAMS (CONSTANTS OR PRESETS) --- //

            var triggerParams = data.Sections["TriggerParams"];
            if (triggerParams != null)
            {
                DebugLog($"[LoadTriggerDataFromIni] Loading {triggerParams.Count} trigger params...");
                foreach (var preset in triggerParams)
            {
                string[] values = preset.Value.Split(",");
                string key = preset.KeyName;

                string variableType;
                string codeText;
                string displayText;
                if (WarcraftStorageReader.GameVersion >= WarcraftVersion._1_28)
                {
                    variableType = values[1];
                    codeText = values[2].Replace("\"", "").Replace("`", "").Replace("|", "\"");
                    displayText = Locale.Translate(values[3]);
                }
                else
                {
                    variableType = values[0];
                    codeText = values[1].Replace("\"", "").Replace("`", "").Replace("|", "\"");
                    displayText = Locale.Translate(values[2]);
                }

                PresetTemplate presetTemplate = new PresetTemplate()
                {
                    value = key,
                    returnType = variableType,
                    name = displayText,
                    codeText = codeText,
                };

                // Allow YDWE to override existing presets
                if (isYDWE && PresetTemplates.ContainsKey(key))
                {
                    DebugLog($"[LoadTriggerDataFromIni] YDWE override: replacing existing preset {key}");
                    PresetTemplates[key] = presetTemplate;
                    ParamDisplayNames[key] = displayText;
                    ParamCodeText[key] = codeText;
                }
                else if (!PresetTemplates.ContainsKey(key))
                {
                    PresetTemplates.Add(key, presetTemplate);
                    ParamDisplayNames.Add(key, displayText);
                    ParamCodeText.Add(key, codeText);
                }

                if (isBT || isYDWE)
                {
                    btOnlyData.Add(key);
                }
                }
            }


            // --- TRIGGER FUNCTIONS --- //

            DebugLog($"[LoadTriggerDataFromIni] Loading trigger functions...");
            LoadFunctions(data, "TriggerEvents", EventTemplates, TriggerElementType.Event);
            LoadFunctions(data, "TriggerConditions", ConditionTemplates, TriggerElementType.Condition);
            LoadFunctions(data, "TriggerActions", ActionTemplates, TriggerElementType.Action);
            LoadFunctions(data, "TriggerCalls", CallTemplates, TriggerElementType.None);
            DebugLog($"[LoadTriggerDataFromIni] Completed loading trigger functions.");



            // --- INIT DEFAULTS --- //
            foreach (var function in Defaults)
            {
                string[] defaultsTxt = function.Value.Split(",");
                FunctionTemplate template = function.Key;
                List<ParameterTemplate> defaults = new List<ParameterTemplate>();
                for (int i = 0; i < template.parameters.Count; i++)
                {
                    if (defaultsTxt.Length < template.parameters.Count)
                        continue;

                    string def = defaultsTxt[i].Replace("\"", string.Empty); // Some default values are quoted - e.g. we don't want quotes around string values
                    ParameterTemplate oldParameter = template.parameters[i];
                    PresetTemplate constantTemplate = GetPresetTemplate(def);
                    FunctionTemplate functionTemplate = GetFunctionTemplate(def);
                    if (functionTemplate != null)
                        defaults.Add(functionTemplate);
                    else if (constantTemplate != null)
                        defaults.Add(constantTemplate);
                    else if (def != "_")
                        defaults.Add(new ValueTemplate() { value = def, returnType = oldParameter.returnType });
                    else
                        defaults.Add(new ParameterTemplate() { returnType = oldParameter.returnType });

                    /* hackfix because of Blizzard default returntype mismatch for some parameters...
                     * 'RectContainsItem' has 'GetRectCenter' for a 'rect' parameter, but returns location.
                     */
                    if (defaults[i].returnType != oldParameter.returnType)
                        defaults[i] = oldParameter;
                }

                if (defaultsTxt.Length != template.parameters.Count)
                    continue;

                template.parameters = defaults;
            }
        }


        private static void LoadFunctions(IniData data, string sectionName, Dictionary<string, FunctionTemplate> dictionary, TriggerElementType Type)
        {
            var section = data.Sections[sectionName];
            if (section == null)
                return;

            DebugLog($"[LoadFunctions] Processing {sectionName} with {section.Count} keys...");
            string name = string.Empty;
            FunctionTemplate functionTemplate = null;
            int processed = 0;
            foreach (var _func in section)
            {
                processed++;
                if (processed % 100 == 0)
                {
                    DebugLog($"[LoadFunctions] Processed {processed}/{section.Count} keys in {sectionName}...");
                }
                string key = _func.KeyName;


                if (key.ToLower().StartsWith(string.Concat("_", name).ToLower())) // ToLower here because Blizzard typo.
                {
                    if (key.EndsWith("DisplayName"))
                    {
                        functionTemplate.name = _func.Value.Replace("\"", "");
                        if (isYDWE)
                            ParamDisplayNames[name] = functionTemplate.name;
                        else
                            ParamDisplayNames.TryAdd(name, functionTemplate.name);
                    }
                    else if (key.EndsWith("Parameters"))
                    {
                        functionTemplate.paramText = _func.Value.Replace("\"", "");
                        if (isYDWE)
                            ParamCodeText[name] = functionTemplate.paramText;
                        else
                            ParamCodeText.TryAdd(name, functionTemplate.paramText);
                    }
                    else if (key.EndsWith("Category"))
                    {
                        functionTemplate.category = _func.Value;
                        if (isYDWE)
                            FunctionCategories[name] = functionTemplate.category;
                        else
                            FunctionCategories.TryAdd(name, functionTemplate.category);
                    }
                    else if (key.EndsWith("Defaults"))
                    {
                        string[] defaultsTxt = _func.Value.Split(",");
                        //if (defaultsTxt.Length >= 1 && defaultsTxt[0] != "" && defaultsTxt[0] != "_" && defaultsTxt[0] != "_true")
                        if (defaultsTxt.Length >= 1 && defaultsTxt[0] != "" && defaultsTxt[0] != "_true")
                            Defaults.TryAdd(functionTemplate, _func.Value);
                    }
                    else if (key.EndsWith("ScriptName"))
                    {
                        functionTemplate.scriptName = _func.Value;
                    }

                    FunctionTemplate controlValue;
                    if (!dictionary.TryGetValue(name, out controlValue))
                    {
                        dictionary.Add(name, functionTemplate);
                        if (!FunctionsAll.ContainsKey(name))
                        {
                            FunctionsAll.Add(name, functionTemplate);
                        }
                        else
                        {
                            DebugLog($"[LoadFunctions] Skipping duplicate function in FunctionsAll: {name}");
                        }
                    }
                    else if (isYDWE)
                    {
                        // YDWE data should override base game functions
                        DebugLog($"[LoadFunctions] YDWE override: replacing existing function {name}");
                        dictionary[name] = functionTemplate;
                        FunctionsAll[name] = functionTemplate;
                    }
                }
                else
                {
                    string returnType = string.Empty;
                    string[] _params = _func.Value.Split(",");
                    List<ParameterTemplate> parameters = new List<ParameterTemplate>();

                    if (sectionName == "TriggerEvents")
                    {
                        returnType = "event";

                        for (int i = 1; i < _params.Length; i++)
                        {
                            parameters.Add(new ParameterTemplate() { returnType = _params[i] });
                        }
                    }
                    else if (sectionName == "TriggerConditions")
                    {
                        returnType = "boolean";
                        BoolExprTempaltes.Add(key);

                        for (int i = 1; i < _params.Length; i++)
                        {
                            parameters.Add(new ParameterTemplate() { returnType = _params[i] });
                        }
                    }
                    else if (sectionName == "TriggerActions")
                    {
                        returnType = "nothing";

                        for (int i = 1; i < _params.Length; i++)
                        {
                            parameters.Add(new ParameterTemplate() { returnType = _params[i] });
                        }
                    }
                    else if (sectionName == "TriggerCalls")
                    {
                        if (WarcraftStorageReader.GameVersion >= WarcraftVersion._1_28)
                            returnType = _params[2];
                        else
                            returnType = _params[1];
                        for (int i = 3; i < _params.Length; i++)
                        {
                            parameters.Add(new ParameterTemplate() { returnType = _params[i] });
                        }
                    }
                    // Some actions have 'nothing' as a parameter type. We don't want that.
                    parameters = parameters.Where(p => p.returnType != "nothing").ToList();
                    name = key;
                    functionTemplate = new FunctionTemplate(Type);
                    functionTemplate.value = key;
                    functionTemplate.parameters = parameters;
                    functionTemplate.returnType = returnType;
                    if (isBT)
                    {
                        btOnlyData.Add(key);
                    }
                }
            }
            DebugLog($"[LoadFunctions] Completed processing {sectionName}. Processed {processed} keys total.");
        }

        /// <summary>
        /// Loads custom constants similar to 'bj_lastCreatedUnit', 'bj_lastCreatedItem' etc.
        /// </summary>
        /// <param name="iniData"></param>
        private static void LoadCustomBlizzardJ(IniData iniData)
        {
            var section = iniData.Sections["Presets"];
            foreach (var key in section)
            {
                string keyName = key.KeyName;

                string[] split = key.Value.Split(',');
                string type = split[0];

                var preset = new CustomPreset()
                {
                    Identifier = keyName,
                    Type = type,
                };

                customPresets.Add(preset);
                btOnlyData.Add(keyName);
            }
        }

        /// <summary>
        /// Loads YDWE trigger data files using custom parser.
        /// </summary>
        private static void LoadYDWEData()
        {
            string ydweDir = Path.Combine(Directory.GetCurrentDirectory(), "Resources/WorldEditorData/YDWE");

            DebugLog($"[YDWE] Attempting to load YDWE data from: {ydweDir}");

            if (!Directory.Exists(ydweDir))
            {
                // YDWE directory doesn't exist, skip loading
                DebugLog($"[YDWE] YDWE directory not found, skipping YDWE loading");
                return;
            }

            DebugLog($"[YDWE] YDWE directory found, loading YDWE trigger data...");
            DebugLog($"[YDWE] Setting isYDWE flag to allow overriding base game functions");
            isYDWE = true;

            try
            {
                // Load define.txt for types and categories
                string definePath = Path.Combine(ydweDir, "define.txt");
                if (File.Exists(definePath))
                {
                    DebugLog($"[YDWE] Loading define.txt...");
                    // define.txt uses standard INI format, so use IniFileConverter
                    string defineText = File.ReadAllText(definePath);
                    var defineData = Utility.IniFileConverter.GetIniData(defineText);

                    // Load types
                    if (defineData.Sections.ContainsSection("TriggerTypes"))
                    {
                        var triggerTypes = defineData.Sections["TriggerTypes"];
                        int typeCount = 0;
                        foreach (var type in triggerTypes)
                        {
                            string[] values = type.Value.Split(",");
                            string key = type.KeyName;

                            if (values.Length >= 4)
                            {
                                bool canBeGlobal = values[0] == "1";
                                bool canBeCompared = values[1] == "1";
                                string displayName = values[3];
                                string baseType = values.Length >= 5 ? values[4] : null;

                                // Only add if it doesn't already exist
                                if (Types.Get(key) == null)
                                {
                                    Types.Create(key, canBeGlobal, canBeCompared, displayName, baseType);
                                    typeCount++;
                                }
                            }
                        }
                        DebugLog($"[YDWE] Loaded {typeCount} YDWE types");
                    }

                    // Load categories
                    if (defineData.Sections.ContainsSection("TriggerCategories"))
                    {
                        var triggerCategories = defineData.Sections["TriggerCategories"];
                        int categoryCount = 0;
                        foreach (var category in triggerCategories)
                        {
                            string[] values = category.Value.Split(",");

                            if (values.Length >= 2 && values[1] != "none")
                            {
                                string WE_STRING = values[0];
                                string texturePath = values[1];
                                bool shouldDisplay = values.Length < 3;

                                // Try to load icon if it exists
                                string iconPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources/Icons", Path.GetFileName(texturePath) + ".png");
                                if (File.Exists(iconPath))
                                {
                                    byte[] img = File.ReadAllBytes(iconPath);
                                    try
                                    {
                                        Category.Create(category.KeyName, img, WE_STRING, shouldDisplay);
                                        categoryCount++;
                                    }
                                    catch (ArgumentException)
                                    {
                                        // Category already exists (e.g., TC_ARITHMETIC from base game), skip it
                                        DebugLog($"[YDWE] Skipping duplicate category: {category.KeyName}");
                                    }
                                }
                                else
                                {
                                    // Use a default icon if specific one doesn't exist
                                    string defaultIconPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources/Icons/_ui-editoricon-triggercategories_tbd.png");
                                    if (File.Exists(defaultIconPath))
                                    {
                                        byte[] img = File.ReadAllBytes(defaultIconPath);
                                        try
                                        {
                                            Category.Create(category.KeyName, img, WE_STRING, shouldDisplay);
                                            categoryCount++;
                                        }
                                        catch (ArgumentException)
                                        {
                                            // Category already exists, skip it
                                            DebugLog($"[YDWE] Skipping duplicate category: {category.KeyName}");
                                        }
                                    }
                                }
                            }
                        }
                        DebugLog($"[YDWE] Loaded {categoryCount} YDWE categories");
                    }
                }

                // Load event.txt
                string eventPath = Path.Combine(ydweDir, "event.txt");
                if (File.Exists(eventPath))
                {
                    DebugLog($"[YDWE] Loading event.txt...");
                    var eventData = YDWEParser.ParseYDWEFile(eventPath, "TriggerEvents");
                    DebugLog($"[YDWE] Passing event.txt to LoadTriggerDataFromIni...");
                    LoadTriggerDataFromIni(eventData, false);
                    DebugLog($"[YDWE] Event.txt loaded successfully");
                }

                // Load condition.txt
                string conditionPath = Path.Combine(ydweDir, "condition.txt");
                if (File.Exists(conditionPath))
                {
                    DebugLog($"[YDWE] Loading condition.txt...");
                    var conditionData = YDWEParser.ParseYDWEFile(conditionPath, "TriggerConditions");
                    LoadTriggerDataFromIni(conditionData, false);
                    DebugLog($"[YDWE] Loaded YDWE conditions");
                }

                // Load action.txt
                string actionPath = Path.Combine(ydweDir, "action.txt");
                if (File.Exists(actionPath))
                {
                    DebugLog($"[YDWE] Loading action.txt...");
                    var actionData = YDWEParser.ParseYDWEFile(actionPath, "TriggerActions");
                    LoadTriggerDataFromIni(actionData, false);
                    DebugLog($"[YDWE] Loaded YDWE actions");
                }

                // Load call.txt
                string callPath = Path.Combine(ydweDir, "call.txt");
                if (File.Exists(callPath))
                {
                    DebugLog($"[YDWE] Loading call.txt...");
                    var callData = YDWEParser.ParseYDWEFile(callPath, "TriggerCalls");
                    LoadTriggerDataFromIni(callData, false);
                    DebugLog($"[YDWE] Loaded YDWE calls");
                }

                DebugLog($"[YDWE] YDWE trigger data loaded successfully!");

                // Debug: Verify specific functions are loaded
                if (ActionTemplates.ContainsKey("CameraSetupApplyForceDuration"))
                {
                    DebugLog($"[YDWE] SUCCESS: CameraSetupApplyForceDuration is in ActionTemplates");
                }
                else
                {
                    DebugLog($"[YDWE] WARNING: CameraSetupApplyForceDuration is NOT in ActionTemplates!");
                }

                if (FunctionsAll.ContainsKey("CameraSetupApplyForceDuration"))
                {
                    DebugLog($"[YDWE] SUCCESS: CameraSetupApplyForceDuration is in FunctionsAll");
                }
                else
                {
                    DebugLog($"[YDWE] WARNING: CameraSetupApplyForceDuration is NOT in FunctionsAll!");
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash - YDWE data is optional
                DebugLog($"Error loading YDWE data: {ex.Message}");
            }
            finally
            {
                isYDWE = false;
                DebugLog($"[YDWE] Reset isYDWE flag");
            }
        }




        public static string GetReturnType(string value)
        {
            if (value == null)
                return null;

            FunctionTemplate function;
            FunctionsAll.TryGetValue(value, out function);
            if (function != null)
                return function.returnType;

            PresetTemplate constant;
            PresetTemplates.TryGetValue(value, out constant);
            if (constant != null)
                return constant.returnType;

            return "nothing"; // hack?
        }

        public static List<string> GetParameterReturnTypes(Function f, ExplorerElement ex)
        {
            List<string> list = new List<string>();

            // TODO: This is slow.
            var actionDefs = Project.CurrentProject.ActionDefinitions.GetAll();
            for (int i = 0; i < actionDefs.Count(); i++)
            {
                var actionDef = actionDefs[i];
                if (actionDef.GetName() == f.value)
                {
                    actionDef.actionDefinition.Parameters.Elements.ForEach(el =>
                    {
                        var parameter = (ParameterDefinition)el;
                        list.Add(parameter.ReturnType.Type);
                    });
                    return list;
                }
            }
            var conditionDefs = Project.CurrentProject.ConditionDefinitions.GetAll();
            for (int i = 0; i < conditionDefs.Count(); i++)
            {
                var conditionDef = conditionDefs[i];
                if (conditionDef.GetName() == f.value)
                {
                    conditionDef.conditionDefinition.Parameters.Elements.ForEach(el =>
                    {
                        var parameter = (ParameterDefinition)el;
                        list.Add(parameter.ReturnType.Type);
                    });
                    return list;
                }
            }

            if (f.value == "SetVariable")
            {
                VariableRef varRef = f.parameters[0] as VariableRef;
                if (varRef != null)
                {
                    Variable variable = Project.CurrentProject.Variables.GetByReference(f.parameters[0] as VariableRef, ex);
                    if (variable != null)
                    {
                        list.Add(variable.War3Type.Type);
                        list.Add(variable.War3Type.Type);
                    }
                    else
                    {
                        list.Add("null");
                        list.Add("null");
                    }
                    return list;
                }
                else
                {
                    list.Add("null");
                    list.Add("null");
                    return list;
                }
            }
            else if (f.value == "ReturnStatement" && ex != null)
            {
                switch (ex.ElementType)
                {
                    case ExplorerElementEnum.ConditionDefinition:
                        list.Add("boolean");
                        return list;
                    case ExplorerElementEnum.FunctionDefinition:
                        list.Add(ex.functionDefinition.ReturnType.War3Type.Type);
                        return list;
                    default:
                        break;
                }
            }

            FunctionTemplate functionTemplate;
            FunctionsAll.TryGetValue(f.value, out functionTemplate);
            if (functionTemplate != null)
                functionTemplate.parameters.ForEach(p => list.Add(p.returnType));

            return list;
        }


        private static FunctionTemplate GetFunctionTemplate(string key)
        {
            FunctionTemplate functionTemplate;
            FunctionsAll.TryGetValue(key, out functionTemplate);
            return functionTemplate;
        }

        private static PresetTemplate GetPresetTemplate(string key)
        {
            PresetTemplate constantTemplate;
            PresetTemplates.TryGetValue(key, out constantTemplate);
            return constantTemplate;
        }

        public static List<FunctionTemplate> GetFunctionTemplatesAll()
        {
            return FunctionsAll.Select(f => f.Value).ToList();
        }

        internal static string GetConstantCodeText(string identifier, ScriptLanguage language)
        {
            string codeText = string.Empty;
            PresetTemplate constant;
            PresetTemplates.TryGetValue(identifier, out constant);
            codeText = constant.codeText;
            if (language == ScriptLanguage.Lua)
            {
                if (constant.codeText == "!=")
                    codeText = "~=";
                else if (constant.codeText == "null")
                    codeText = "nil";
            }

            return codeText;
        }

        internal static bool ConstantExists(string value)
        {
            PresetTemplate temp;
            bool exists = PresetTemplates.TryGetValue(value, out temp);
            return exists;
        }

        internal static bool FunctionExists(Function function)
        {
            if (function == null)
                return false;

            bool exists = false;
            if (function.value != null)
            {
                exists = FunctionsAll.ContainsKey(function.value);
            }

            var project = Project.CurrentProject;
            if (!exists)
                exists = project.ActionDefinitions.Contains(function.value);
            if (!exists)
                exists = project.ConditionDefinitions.Contains(function.value);
            if (!exists)
                exists = project.FunctionDefinitions.Contains(function.value);


            return exists;
        }




        public static List<Types> LoadAllVariableTypes()
        {
            return Types.GetGlobalTypes();
        }


        public static List<FunctionTemplate> LoadAllEvents()
        {
            List<FunctionTemplate> list = new List<FunctionTemplate>();
            var enumerator = TriggerData.EventTemplates.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var template = enumerator.Current.Value;
                if (template.value != "InvalidECA")
                    list.Add(template.Clone());
            }
            return list;
        }

        public static List<FunctionTemplate> LoadAllCalls(string returnType)
        {
            List<FunctionTemplate> list = new List<FunctionTemplate>();

            if (returnType == "handle")
            {
                var enumerator = TriggerData.CallTemplates.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var template = enumerator.Current.Value;
                    if (Types.IsHandle(template.returnType))
                    {
                        list.Add(template);
                    }
                }

                return list;
            }

            // Special case for for GUI "Matching" parameter
            bool wasBoolCall = false;
            if (returnType == "boolcall")
            {
                wasBoolCall = true;
                returnType = "boolexpr";
            }

            // Special case for GUI "Action" parameter
            else if (returnType == "code")
            {
                var enumerator = TriggerData.ActionTemplates.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var template = enumerator.Current.Value;
                    if (!template.value.Contains("Multiple"))
                        list.Add(template.Clone());
                }
                list.ForEach(call => call.returnType = "code");

                return list;
            }

            // Special case for GUI 'eventcall' parameter
            else if (returnType == "eventcall")
            {
                var enumerator = TriggerData.EventTemplates.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var template = enumerator.Current.Value;
                    list.Add(template.Clone());
                }
                list.ForEach(call => call.returnType = "eventcall");

                return list;
            }

            string baseType = Types.GetBaseType(returnType);
            var enumCalls = TriggerData.CallTemplates.GetEnumerator();
            while (enumCalls.MoveNext())
            {
                var template = enumCalls.Current.Value;
                if (baseType == Types.GetBaseType(template.returnType))
                    list.Add(template.Clone());
            }
            var enumConditions = TriggerData.ConditionTemplates.GetEnumerator();
            while (enumConditions.MoveNext())
            {
                var template = enumConditions.Current.Value;
                if (returnType == template.returnType || (returnType == "boolexpr" && !template.value.EndsWith("Multiple")))
                    list.Add(template.Clone());
            }
            if (wasBoolCall)
            {
                list.ForEach(call => call.returnType = "boolcall");
            }

            var functionDefinitions = Project.CurrentProject.FunctionDefinitions.GetAll();
            foreach (var funcDef in functionDefinitions)
            {
                if (Types.GetBaseType(funcDef.functionDefinition.ReturnType.War3Type.Type) == baseType)
                {
                    FunctionTemplate template = new FunctionTemplate(TriggerElementType.ParameterDef)
                    {
                        name = funcDef.GetName(),
                        value = funcDef.GetName(),
                        paramText = funcDef.functionDefinition.ParamText,
                        returnType = returnType,
                    };
                    list.Add(template);
                }
            }

            return list;
        }

        public static List<PresetTemplate> LoadAllPresets()
        {
            List<PresetTemplate> list = new List<PresetTemplate>();
            var enumerator = TriggerData.PresetTemplates.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var template = enumerator.Current.Value;
                if (template.value != "InvalidECA")
                    list.Add(template.Clone());
            }
            return list;
        }

        public static List<FunctionTemplate> LoadAllConditions()
        {
            List<FunctionTemplate> list = new List<FunctionTemplate>();
            var enumerator = TriggerData.ConditionTemplates.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var template = enumerator.Current.Value;
                if (template.value != "InvalidECA")
                    list.Add(template.Clone());
            }
            var conditionDefs = Project.CurrentProject.ConditionDefinitions.GetAll();
            foreach (var conditionDef in conditionDefs)
            {
                var functionTemplate = new FunctionTemplate(TriggerElementType.Condition)
                {
                    name = conditionDef.GetName(),
                    value = conditionDef.GetName(),
                    paramText = conditionDef.conditionDefinition.ParamText,
                    category = conditionDef.conditionDefinition.explorerElement.CategoryStr,
                    description = conditionDef.conditionDefinition.Comment,
                };

                list.Add(functionTemplate);
            }

            return list;
        }

        public static List<FunctionTemplate> LoadAllActions(ExplorerElementEnum type)
        {
            List<FunctionTemplate> list = new List<FunctionTemplate>();
            var enumerator = TriggerData.ActionTemplates.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var template = enumerator.Current.Value;
                if (type == ExplorerElementEnum.Trigger && template.value == "ReturnStatement")
                    continue;

                if (template.value == "InvalidECA")
                    continue;

                list.Add(template.Clone());
            }
            var actionDefs = Project.CurrentProject.ActionDefinitions.GetAll();
            foreach (var actionDef in actionDefs)
            {
                var functionTemplate = new FunctionTemplate(TriggerElementType.Action)
                {
                    name = actionDef.GetName(),
                    value = actionDef.GetName(),
                    paramText = actionDef.actionDefinition.ParamText,
                    category = actionDef.actionDefinition.explorerElement.CategoryStr,
                    description = actionDef.actionDefinition.Comment,
                };

                list.Add(functionTemplate);
            }

            return list;
        }

        public static string GetFuntionDisplayName(string key)
        {
            FunctionTemplate functionTemplate;
            FunctionsAll.TryGetValue(key, out functionTemplate);
            if (functionTemplate == null)
            {
                return string.Empty;
            }

            return functionTemplate.name;
        }

        public static string GetParamDisplayName(Parameter parameter)
        {
            if (parameter is Value)
                return parameter.value;

            string displayName;
            TriggerData.ParamDisplayNames.TryGetValue(parameter.value, out displayName);
            if (displayName == null)
                displayName = parameter.value;

            return displayName;
        }

        public static string GetParamText(TriggerElement triggerElement)
        {
            string paramText = string.Empty;
            if (triggerElement is ECA)
            {
                var element = (ECA)triggerElement;
                var function = element.function;
                paramText = GetParamText(function);
            }
            else if (triggerElement is LocalVariable)
            {
                var element = (LocalVariable)triggerElement;
                paramText = element.variable.Name;
            }

            return paramText;
        }

        public static string GetParamText(Function function)
        {
            string paramText = string.Empty;
            TriggerData.ParamCodeText.TryGetValue(function.value, out paramText);
            if (paramText == null)
            {
                List<string> returnTypes = TriggerData.GetParameterReturnTypes(function, null);
                paramText = function.value + "(";
                for (int i = 0; i < function.parameters.Count; i++)
                {
                    var p = function.parameters[i];
                    paramText += ",~" + returnTypes[i] + ",";
                    if (i != function.parameters.Count - 1)
                        paramText += ", ";
                }
                paramText += ")";
            }

            return paramText;
        }

        public static string GetCategoryTriggerElement(TriggerElement triggerElement)
        {
            string category = string.Empty;
            if (triggerElement is ECA)
            {
                var element = (ECA)triggerElement;
                TriggerData.FunctionCategories.TryGetValue(element.function.value, out category);
            }
            else if (triggerElement is LocalVariable)
                category = TriggerCategory.TC_LOCAL_VARIABLE;

            return category;
        }

        public static bool IsBTOnlyData(string value)
        {
            bool isBTOnlyData = btOnlyData.Contains(value);
            return isBTOnlyData;
        }
    }
}
