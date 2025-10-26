using BetterTriggers.Containers;
using BetterTriggers.Models;
using BetterTriggers.Models.EditorData;
using BetterTriggers.Models.SaveableData;
using BetterTriggers.Models.War3Data;
using BetterTriggers.Utility;
using BetterTriggers.WorldEdit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using War3Net.Build;
using War3Net.Build.Environment;
using War3Net.Build.Extensions;
using War3Net.Build.Object;
using War3Net.Build.Widget;
using War3Net.IO.Mpq;

namespace BetterTriggers
{
    public class CustomMapData
    {
        internal static Map MPQMap;
        private static FileSystemWatcher watcher;
        public static event Action OnSaving;

        private static System.Timers.Timer ThresholdBeforeReloadingTimer;
        private const int THRESHOLD_BEFORE_SAVING_MS = 50;
        private static bool isVanillaWESaving;

        /// <summary>
        /// Method used for detecting the vanilla WE saving the map.
        /// </summary>
        private static void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            // this try-block is only here because of the TriggerConverter.
            try
            {
                var mapPath = Project.CurrentProject.GetFullMapPath();
                if (e.Name == Path.GetFileName(mapPath) + "Temp")
                {
                    isVanillaWESaving = true;
                    OnSaving?.Invoke();
                }
            }
            catch (Exception)
            {

            }
        }

        /// <summary>
        /// Method used for detecting other tools changing the map.
        /// </summary>
        private static void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            // this try-block is only here because of the TriggerConverter.
            try
            {
                if (!isVanillaWESaving)
                {
                    string mapPath = Project.CurrentProject.GetFullMapPath();
                    bool fileIsInMap = e.FullPath.StartsWith(mapPath);
                    if (fileIsInMap)
                    {
                        if (ThresholdBeforeReloadingTimer == null)
                        {
                            ThresholdBeforeReloadingTimer = new System.Timers.Timer();
                            ThresholdBeforeReloadingTimer.AutoReset = false;
                            ThresholdBeforeReloadingTimer.Elapsed += ThresholdBeforeReloadingTimer_Elapsed;
                        }
                        ThresholdBeforeReloadingTimer.Stop();
                        ThresholdBeforeReloadingTimer.Interval = THRESHOLD_BEFORE_SAVING_MS;
                        ThresholdBeforeReloadingTimer.Start();

                        isThresholdTimerRunning = true;
                        OnSaving?.Invoke();
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        private static bool isThresholdTimerRunning;
        private static void ThresholdBeforeReloadingTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            isThresholdTimerRunning = false;
            ThresholdBeforeReloadingTimer.Stop();
        }

        public static bool IsMapSaving(string fullMapPath = null)
        {
            if (string.IsNullOrEmpty(fullMapPath))
            {
                fullMapPath = Project.CurrentProject.GetFullMapPath();
            }

            if (Directory.Exists(fullMapPath + "Temp"))
                return true;
            else if (Directory.Exists(fullMapPath + "Backup"))
                return true;
            else if (isThresholdTimerRunning)
                return true;
            else
                return false;
        }


        public static void Load(string fullMapPath = null, bool isFilesystemWatcherEnabled = true)
        {
            if (string.IsNullOrEmpty(fullMapPath))
            {
                fullMapPath = Project.CurrentProject.GetFullMapPath();
            }

            while (IsMapSaving(fullMapPath))
            {
                Thread.Sleep(1000);
            }

            // Exclude Triggers flag to avoid War3Net parsing triggers with TriggerData.Default
            // War3Net's TriggerData.Default doesn't have YDWE functions, causing KeyNotFoundException
            // We'll load triggers manually after loading BetterTriggers' TriggerData
            MPQMap = Map.Open(fullMapPath, MapFiles.All & ~MapFiles.Triggers);

            // Manually load triggers using BetterTriggers' TriggerData which includes YDWE functions
            // This must be done AFTER WorldEdit.TriggerData is initialized
            LoadTriggersManually(fullMapPath);

            Info.Load();
            MapStrings.Load();
            UnitTypes.Load(fullMapPath);
            ItemTypes.Load();
            DestructibleTypes.Load();
            DoodadTypes.Load(fullMapPath);
            AbilityTypes.Load();
            BuffTypes.Load();
            UpgradeTypes.Load();
            SkinFiles.Load();

            Cameras.Load();
            Destructibles.Load();
            Regions.Load();
            Sounds.Load();
            Units.Load();

            isVanillaWESaving = false;

            if (isFilesystemWatcherEnabled)
            {
                if (watcher != null)
                {
                    watcher.Created -= Watcher_Created;
                    watcher.Changed -= Watcher_Changed;
                }

                watcher = new System.IO.FileSystemWatcher();
                watcher.Path = Path.GetDirectoryName(fullMapPath);
                watcher.EnableRaisingEvents = true;
                watcher.IncludeSubdirectories = true;
                watcher.Created += Watcher_Created;
                watcher.Changed += Watcher_Changed;
            }
        }

        private static string manualLoadLogPath = Path.Combine(Directory.GetCurrentDirectory(), "manual_trigger_load.log");

        private static void ManualLoadDebugLog(string message)
        {
            string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            try
            {
                File.AppendAllText(manualLoadLogPath, logMessage + Environment.NewLine);
            }
            catch { }
        }

        private static void LoadTriggersManually(string fullMapPath)
        {
            try
            {
                // Clear previous log
                try { if (File.Exists(manualLoadLogPath)) File.Delete(manualLoadLogPath); } catch { }

                ManualLoadDebugLog("=== LoadTriggersManually STARTED ===");
                ManualLoadDebugLog($"Map path: {fullMapPath}");

                // Check if trigger file exists in the map
                using var mpqArchive = MpqArchive.Open(fullMapPath);
                ManualLoadDebugLog($"MPQ archive opened successfully");

                if (!MpqFile.Exists(mpqArchive, War3Net.Build.Script.MapTriggers.FileName))
                {
                    ManualLoadDebugLog($"No {War3Net.Build.Script.MapTriggers.FileName} file found in map");
                    return;
                }

                ManualLoadDebugLog($"Found {War3Net.Build.Script.MapTriggers.FileName} in map");

                // Read the trigger file from MPQ
                using var triggerStream = MpqFile.OpenRead(mpqArchive, War3Net.Build.Script.MapTriggers.FileName);
                using var triggerReader = new BinaryReader(triggerStream);
                ManualLoadDebugLog($"Trigger file opened for reading");

                // Create custom TriggerData from BetterTriggers' data (includes YDWE functions)
                ManualLoadDebugLog($"Creating custom TriggerData...");
                ManualLoadDebugLog($"  EventTemplates count: {WorldEdit.TriggerData.EventTemplates.Count}");
                ManualLoadDebugLog($"  ConditionTemplates count: {WorldEdit.TriggerData.ConditionTemplates.Count}");
                ManualLoadDebugLog($"  ActionTemplates count: {WorldEdit.TriggerData.ActionTemplates.Count}");
                ManualLoadDebugLog($"  CallTemplates count: {WorldEdit.TriggerData.CallTemplates.Count}");

                var customTriggerData = CreateWar3NetTriggerData();
                ManualLoadDebugLog($"Custom TriggerData created successfully");

                // Parse triggers using custom TriggerData
                ManualLoadDebugLog($"Parsing triggers with custom TriggerData...");
                var mapTriggers = triggerReader.ReadMapTriggers(customTriggerData);
                ManualLoadDebugLog($"Triggers parsed successfully");
                ManualLoadDebugLog($"  Trigger count: {mapTriggers?.TriggerItems?.Count ?? 0}");
                ManualLoadDebugLog($"  Variable count: {mapTriggers?.Variables?.Count ?? 0}");

                // Assign to MPQMap
                MPQMap.Triggers = mapTriggers;
                ManualLoadDebugLog($"Triggers assigned to MPQMap.Triggers");
                ManualLoadDebugLog($"=== LoadTriggersManually COMPLETED ===");
            }
            catch (Exception ex)
            {
                ManualLoadDebugLog($"ERROR: {ex.GetType().Name}");
                ManualLoadDebugLog($"ERROR Message: {ex.Message}");
                ManualLoadDebugLog($"ERROR Stack Trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    ManualLoadDebugLog($"INNER Exception: {ex.InnerException.GetType().Name}");
                    ManualLoadDebugLog($"INNER Message: {ex.InnerException.Message}");
                    ManualLoadDebugLog($"INNER Stack Trace: {ex.InnerException.StackTrace}");
                }

                // Re-throw to see if this is causing the issue
                throw;
            }
        }

        private static War3Net.Build.Script.TriggerData CreateWar3NetTriggerData()
        {
            // Export BetterTriggers' TriggerData to War3Net's INI format
            var sb = new StringBuilder();

            // TriggerCategories section (minimal, not critical for parsing)
            sb.AppendLine("[TriggerCategories]");
            sb.AppendLine();

            // TriggerTypes section (minimal, not critical for parsing)
            sb.AppendLine("[TriggerTypes]");
            sb.AppendLine();

            // TriggerParams section
            sb.AppendLine("[TriggerParams]");
            sb.AppendLine();

            // TriggerTypeDefaults section
            sb.AppendLine("[TriggerTypeDefaults]");
            sb.AppendLine();

            // TriggerEvents section
            sb.AppendLine("[TriggerEvents]");
            foreach (var evt in WorldEdit.TriggerData.EventTemplates.Values)
            {
                // Format: key=category,returnType,isEnabled,displayText
                sb.AppendLine($"{evt.name}={evt.category ?? "TC_NOTHING"},{evt.returnType ?? "nothing"},1,{evt.value}");
            }
            sb.AppendLine();

            // TriggerConditions section
            sb.AppendLine("[TriggerConditions]");
            foreach (var cond in WorldEdit.TriggerData.ConditionTemplates.Values)
            {
                // Format: key=category,returnType,isEnabled,displayText
                sb.AppendLine($"{cond.name}={cond.category ?? "TC_NOTHING"},{cond.returnType ?? "boolean"},1,{cond.value}");
            }
            sb.AppendLine();

            // TriggerActions section
            sb.AppendLine("[TriggerActions]");
            foreach (var action in WorldEdit.TriggerData.ActionTemplates.Values)
            {
                // Format: key=category,returnType,isEnabled,displayText
                sb.AppendLine($"{action.name}={action.category ?? "TC_NOTHING"},nothing,1,{action.value}");
            }
            sb.AppendLine();

            // TriggerCalls section
            sb.AppendLine("[TriggerCalls]");
            foreach (var call in WorldEdit.TriggerData.CallTemplates.Values)
            {
                // Format: key=category,returnType,isEnabled,displayText
                sb.AppendLine($"{call.name}={call.category ?? "TC_NOTHING"},{call.returnType ?? "nothing"},1,{call.value}");
            }
            sb.AppendLine();

            // DefaultTriggerCategories and DefaultTriggers (skip, not needed for parsing)
            sb.AppendLine("[DefaultTriggerCategories]");
            sb.AppendLine();
            sb.AppendLine("[DefaultTriggers]");
            sb.AppendLine();

            // Save generated TriggerData to file for debugging
            string generatedData = sb.ToString();
            try
            {
                string debugPath = Path.Combine(Directory.GetCurrentDirectory(), "generated_triggerdata.txt");
                File.WriteAllText(debugPath, generatedData);
                ManualLoadDebugLog($"Generated TriggerData saved to: {debugPath}");
                ManualLoadDebugLog($"Generated TriggerData length: {generatedData.Length} chars");
            }
            catch (Exception ex)
            {
                ManualLoadDebugLog($"Failed to save generated TriggerData: {ex.Message}");
            }

            // Create War3Net TriggerData using reflection (constructor is internal)
            using var stringReader = new StringReader(generatedData);
            var triggerDataType = typeof(War3Net.Build.Script.TriggerData);
            var constructor = triggerDataType.GetConstructor(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(StringReader) },
                null);

            if (constructor == null)
            {
                throw new Exception("Could not find TriggerData constructor via reflection");
            }

            return (War3Net.Build.Script.TriggerData)constructor.Invoke(new object[] { stringReader });
        }

        /// <summary>
        /// Removes all used map data that no longer exists in the map.
        /// Also checks for ID collisions.
        /// </summary>
        /// <returns>A list of modified triggers.</returns>
        public static List<ExplorerElement> ReloadMapData()
        {
            // Check for ID collisions
            List<Tuple<ExplorerElement, ExplorerElement>> idCollision = new();
            List<ExplorerElement> checkedTriggers = new List<ExplorerElement>();
            List<ExplorerElement> checkedVariables = new List<ExplorerElement>();
            List<ExplorerElement> checkedActionDefs = new List<ExplorerElement>();
            List<ExplorerElement> checkedConditionDefs = new List<ExplorerElement>();
            List<ExplorerElement> checkedFunctionDefs = new List<ExplorerElement>();

            var triggers = Project.CurrentProject.Triggers.GetAll();
            var variables = Project.CurrentProject.Variables.GetGlobals();
            var actionsDefs = Project.CurrentProject.ActionDefinitions.GetAll();
            var conditionDefs = Project.CurrentProject.ConditionDefinitions.GetAll();
            var functionDefs = Project.CurrentProject.FunctionDefinitions.GetAll();
            triggers.ForEach(t =>
            {
                checkedTriggers.ForEach(check =>
                {
                    if (t.GetId() == check.GetId())
                        idCollision.Add(new Tuple<ExplorerElement, ExplorerElement>(t, check));
                });

                checkedTriggers.Add(t);
            });
            variables.ForEach(v =>
            {
                checkedVariables.ForEach(check =>
                {
                    if (v.GetId() == check.GetId())
                        idCollision.Add(new Tuple<ExplorerElement, ExplorerElement>(v, check));
                });

                checkedVariables.Add(v);
            });
            actionsDefs.ForEach(v =>
            {
                checkedActionDefs.ForEach(check =>
                {
                    if (v.GetId() == check.GetId())
                        idCollision.Add(new Tuple<ExplorerElement, ExplorerElement>(v, check));
                });

                checkedActionDefs.Add(v);
            });
            conditionDefs.ForEach(v =>
            {
                checkedConditionDefs.ForEach(check =>
                {
                    if (v.GetId() == check.GetId())
                        idCollision.Add(new Tuple<ExplorerElement, ExplorerElement>(v, check));
                });

                checkedConditionDefs.Add(v);
            });
            functionDefs.ForEach(v =>
            {
                checkedFunctionDefs.ForEach(check =>
                {
                    if (v.GetId() == check.GetId())
                        idCollision.Add(new Tuple<ExplorerElement, ExplorerElement>(v, check));
                });

                checkedFunctionDefs.Add(v);
            });

            if (idCollision.Count > 0)
            {
                throw new IdCollisionException(idCollision);
            }

            Project.CurrentProject.CommandManager.Reset();
            CustomMapData.Load();
            var changed = CustomMapData.RemoveInvalidReferences();
            changed.ForEach(trig => trig.AddToUnsaved());

            return changed;
        }

        private static List<ExplorerElement> RemoveInvalidReferences()
        {
            List<ExplorerElement> modified = new List<ExplorerElement>();
            var explorerElements = Project.CurrentProject.GetAllExplorerElements();
            for (int i = 0; i < explorerElements.Count; i++)
            {
                TriggerValidator validator = new TriggerValidator(explorerElements[i]);
                int invalidCount = validator.RemoveInvalidReferences();
                if (invalidCount > 0)
                    modified.Add(explorerElements[i]);

                explorerElements[i].Notify();
            }
            var variables = Project.CurrentProject.Variables.GetGlobals();
            for (int i = 0; i < variables.Count; i++)
            {
                bool wasRemoved = Project.CurrentProject.Variables.RemoveInvalidReference(variables[i]);
                if (wasRemoved)
                    modified.Add(variables[i]);
            }

            return modified;
        }

        /// <summary>
        /// TODO: This function is hella expensive.
        /// </summary>
        /// <param name="value">Reference to map data.</param>
        /// <returns></returns>
        internal static bool ReferencedDataExists(Value value, string returnType)
        {
            if (returnType == "unitcode")
            {
                List<UnitType> unitTypes = UnitTypes.GetAll();
                for (int i = 0; i < unitTypes.Count; i++)
                {
                    if (value.value == unitTypes[i].Id)
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "unit")
            {
                var units = Units.GetAll();
                for (int i = 0; i < units.Count; i++)
                {
                    if (value.value == $"{units[i].ToString()}_{units[i].CreationNumber.ToString("D4")}")
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "destructablecode")
            {
                List<DestructibleType> destTypes = DestructibleTypes.GetAll();
                for (int i = 0; i < destTypes.Count; i++)
                {
                    if (value.value == destTypes[i].DestCode)
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "destructable")
            {
                var dests = Destructibles.GetAll();
                for (int i = 0; i < dests.Count; i++)
                {
                    if (value.value == $"{dests[i].ToString()}_{dests[i].CreationNumber.ToString("D4")}")
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "itemcode")
            {
                List<ItemType> itemTypes = ItemTypes.GetAll();
                for (int i = 0; i < itemTypes.Count; i++)
                {
                    if (value.value == itemTypes[i].ItemCode)
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "item")
            {
                List<UnitData> itemTypes = Units.GetMapItemsAll();
                for (int i = 0; i < itemTypes.Count; i++)
                {
                    if (value.value == $"{itemTypes[i].ToString()}_{itemTypes[i].CreationNumber.ToString("D4")}")
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "doodadcode")
            {
                List<DoodadType> doodadTypes = DoodadTypes.GetAll();
                for (int i = 0; i < doodadTypes.Count; i++)
                {
                    if (value.value == doodadTypes[i].DoodCode)
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "abilcode")
            {
                var abilities = AbilityTypes.GetAll();
                for (int i = 0; i < abilities.Count; i++)
                {
                    if (value.value == abilities[i].AbilCode)
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "buffcode")
            {
                var buffs = BuffTypes.GetAll();
                for (int i = 0; i < buffs.Count; i++)
                {
                    if (value.value == buffs[i].BuffCode)
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "techcode")
            {
                var tech = UpgradeTypes.GetAll();
                for (int i = 0; i < tech.Count; i++)
                {
                    if (value.value == tech[i].UpgradeCode)
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "rect")
            {
                var regions = Regions.GetAll();
                for (int i = 0; i < regions.Count; i++)
                {
                    /* The string Replace exists because values converted with 'TriggerConverter' from a map
                     * have '_' in variable references, but War3Net values have spaces ' ' in them.
                     * Same goes for 'camerasetup' below.
                     */
                    if (value.value.Replace(" ", "_") == regions[i].ToString().Replace(" ", "_"))
                    {
                        return true;
                    }
                }
            }
            else if (returnType == "camerasetup")
            {
                var cameras = Cameras.GetAll();
                for (int i = 0; i < cameras.Count; i++)
                {
                    if (value.value.Replace(" ", "_") == cameras[i].ToString().Replace(" ", "_"))
                    {
                        return true;
                    }
                }
            }
            else
                return true;

            return false;
        }
    }
}
