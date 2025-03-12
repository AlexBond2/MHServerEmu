﻿using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Prototypes;
using System.ComponentModel;
using System.Reflection;

namespace MHServerEmu.Games.GameData.PatchManager
{
    public class PrototypePatchManager
    {

        private static readonly Logger Logger = LogManager.CreateLogger();
        private PrototypeId _currentProtoRef;
        private readonly Dictionary<PrototypeId, List<PrototypePatchUpdateValue>> _patchDict = new();
        private Dictionary<Prototype, string> _pathDict = new ();

        public static PrototypePatchManager Instance { get; } = new();

        public void Initialize()
        {
            LoadPatchDataFromDisk();
        }

        private bool LoadPatchDataFromDisk()
        {
            string patchDirectory = Path.Combine(FileHelper.DataDirectory, "Game");
            if (Directory.Exists(patchDirectory) == false)
                return Logger.WarnReturn(false, "LoadPatchDataFromDisk(): Game data directory not found");

            int count = 0;

            // Read all .json files that start with PatchData
            foreach (string filePath in FileHelper.GetFilesWithPrefix(patchDirectory, "PatchData", "json"))
            {
                string fileName = Path.GetFileName(filePath);

                PrototypePatchUpdateValue[] updateValues = FileHelper.DeserializeJson<PrototypePatchUpdateValue[]>(filePath);
                if (updateValues == null)
                {
                    Logger.Warn($"LoadPatchDataFromDisk(): Failed to parse {fileName}, skipping");
                    continue;
                }

                foreach (PrototypePatchUpdateValue value in updateValues)
                {
                    PrototypeId prototypeId = GameDatabase.GetPrototypeRefByName(value.Prototype);
                    if (prototypeId == PrototypeId.Invalid) continue;
                    AddPatchValue(prototypeId, value);
                    count++;
                }

                Logger.Trace($"Parsed patch data from {fileName}");
            }

            return Logger.InfoReturn(true, $"Loaded {count} patches");
        }

        private void AddPatchValue(PrototypeId prototypeId, in PrototypePatchUpdateValue value)
        {
            if (_patchDict.TryGetValue(prototypeId, out var patchList) == false)
            {
                patchList = [];
                _patchDict[prototypeId] = patchList;
            }
            patchList.Add(value);
        }

        public bool PreCheck(PrototypeId protoRef)
        {
            if (protoRef == PrototypeId.Invalid) 
                return _currentProtoRef != PrototypeId.Invalid;

            if (_patchDict.ContainsKey(protoRef))
                _currentProtoRef = protoRef;
            else
                _currentProtoRef = PrototypeId.Invalid;

            _pathDict.Clear();

            return _currentProtoRef != PrototypeId.Invalid;
        }

        public void PostOverride(Prototype prototype)
        {
            if (_patchDict.TryGetValue(_currentProtoRef, out var list) == false) return; 
            if (_pathDict.TryGetValue(prototype, out var currentPath) == false) return;

            foreach (var entry in list)
                CheckAndUpdate(entry, prototype, currentPath);
        }

        private static bool CheckAndUpdate(PrototypePatchUpdateValue entry, Prototype prototype, string currentPath)
        {
            if (currentPath.StartsWith('.')) currentPath = currentPath[1..];

            if (entry.СlearPath != currentPath) return false;

            var fieldInfo = prototype.GetType().GetProperty(entry.FieldName);
            if (fieldInfo == null) return false;

            UpdateValue(prototype, fieldInfo, entry);

            return true;
        }

        private static object ConvertValue(string stringValue, Type targetType)
        {
            TypeConverter converter = TypeDescriptor.GetConverter(targetType);

            if (converter != null && converter.CanConvertFrom(typeof(string)))
                return converter.ConvertFrom(stringValue);

            return Convert.ChangeType(stringValue, targetType);
        }

        private static void UpdateValue(Prototype prototype, PropertyInfo fieldInfo, PrototypePatchUpdateValue entry)
        {
            try
            {
                Type fieldType = fieldInfo.PropertyType;
                object convertedValue = ConvertValue(entry.Value, fieldType);
                fieldInfo.SetValue(prototype, convertedValue);
            }
            catch (Exception ex)
            {
                Logger.WarnException(ex, $"Failed UpdateValue: [{entry.Prototype}] [{entry.Path}] {ex.Message}");
            }
        }

        public void SetPath(Prototype parent, Prototype child, string fieldName)
        {
            string parentPath = _pathDict.TryGetValue(parent, out var path) ? path : string.Empty;
            _pathDict[child] = $"{parentPath}.{fieldName}";
        }

        public void SetPathIndex(Prototype parent, Prototype child, string fieldName, int index)
        {
            string parentPath = _pathDict.TryGetValue(parent, out var path) ? path : string.Empty;
            _pathDict[child] = $"{parentPath}.{fieldName}[{index}]";
        }
    }
}
