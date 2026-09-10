using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MHServerEmu.Games.GameData.PatchManager
{
    public class PrototypePatchManager
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        
        private readonly Dictionary<PrototypeId, List<PrototypePatchEntry>> _patchDict = new();
        private PatchContext _context = new();
        private bool _initialized = false;

        public static PrototypePatchManager Instance { get; } = new();

        /// <summary>
        /// Reads patch files from this folder instead of Data/Game/Patches. Diagnostics only.
        /// </summary>
        /// <remarks>
        /// Exists so another fork's patch set can be exercised against this patcher WITHOUT copying their
        /// files into our Data directory - that copy is what has contaminated a live server before.
        /// </remarks>
        public static string PatchDirectoryOverride { get; set; }

        /// <summary>
        /// When false, mixin list members are not registered in the path dictionary. Diagnostics only.
        /// </summary>
        /// <remarks>
        /// Reproduces the behaviour of a patcher that has no <see cref="SetPathMixin"/> - notably
        /// mtzimas92/MHServerEmu's. Without registration, <see cref="SetPath"/> falls back to an empty parent
        /// path at the mixin boundary, so nodes inside a condition accumulate paths that LOOK top-level. That
        /// is not a cosmetic difference: it means the same patch file resolves to different nodes on the two
        /// patchers, and it is the only way to measure which entries depend on which behaviour.
        /// </remarks>
        public static bool RegisterMixinPaths { get; set; } = true;

        /// <summary>
        /// When true, an entry keeps counting matches after the first one instead of stopping. Diagnostics
        /// only - used to detect paths that are AMBIGUOUS (match more than one distinct node), which is the
        /// risk a "try strict, then fall back" compatibility shim would introduce.
        /// </summary>
        public static bool CountAllMatches { get; set; } = false;

        /// <summary>
        /// When true, records the full mixin-qualified path for entries written in the short (unregistered)
        /// form, so an existing patch set can be mechanically rewritten instead of hand-audited.
        /// </summary>
        public static bool SuggestMixinRewrites { get; set; } = false;

        /// <summary>Suggested rewrites: (prototype, original path, mixin-qualified path).</summary>
        public static List<(string Prototype, string OldPath, string NewPath)> MixinRewrites { get; } = new();

        /// <summary>
        /// The path a node would have had on a patcher that does not register mixin members: everything after
        /// the last mixin segment, because SetPath resets to an empty parent path at that boundary.
        /// </summary>
        private static string ToLegacyPath(string fullPath)
        {
            int lastMixin = fullPath.LastIndexOf("[BlueprintId=", StringComparison.Ordinal);
            if (lastMixin < 0) return null;

            int close = fullPath.IndexOf(']', lastMixin);
            if (close < 0 || close + 1 >= fullPath.Length) return string.Empty;

            return fullPath[(close + 1)..].TrimStart('.');
        }

        /// <summary>
        /// Loads patches. Must run before Globals are loaded - the patcher fully populates
        /// <see cref="_patchDict"/> in one pass here before any prototype construction can consult it
        /// (see also <see cref="JsonPrototype"/>'s lazy instance construction and the subcontext
        /// reset in <see cref="ConvertValue"/>), so every downstream GetPrototype() call - including
        /// eager sub-prototype construction that happens as a side effect of loading GlobalsPrototype
        /// itself - goes through the normal PreCheck()/PostOverride() flow already patched-and-ready.
        /// </summary>
        public void Initialize(bool enablePatchManager)
        {
            if (enablePatchManager) _initialized |= LoadPatchDataFromDisk("PatchData");

            // Curves are already loaded by this point (the DataDirectory read them before Globals), and unlike
            // prototypes they are not constructed lazily - there is no PreCheck hook to hang them off. So they
            // are applied here and now, in one pass, rather than on demand.
            if (enablePatchManager) ApplyCurvePatchesFromDisk("PatchCurve");
        }

        /// <summary>
        /// Applies curve edits read from PatchCurve*.json files.
        /// </summary>
        private static void ApplyCurvePatchesFromDisk(string prefix)
        {
            string patchDirectory = Path.Combine(FileHelper.DataDirectory, "Game", "Patches");
            if (Directory.Exists(patchDirectory) == false)
                return;

            int applied = 0;

            foreach (string filePath in FileHelper.GetFilesWithPrefix(patchDirectory, prefix, "json"))
            {
                string fileName = Path.GetFileName(filePath);

                CurvePatchEntry[] entries = FileHelper.DeserializeJson<CurvePatchEntry[]>(filePath);
                if (entries == null)
                {
                    Logger.Warn($"ApplyCurvePatchesFromDisk(): Failed to parse {fileName}, skipping");
                    continue;
                }

                foreach (CurvePatchEntry entry in entries)
                {
                    if (entry.Enabled == false)
                        continue;

                    if (ApplyCurvePatch(entry, fileName))
                        applied++;
                }
            }

            if (applied > 0)
                Logger.Info($"Applied {applied} curve patch(es)");
        }

        private static bool ApplyCurvePatch(CurvePatchEntry entry, string fileName)
        {
            if (string.IsNullOrWhiteSpace(entry.Curve))
            {
                Logger.Warn($"ApplyCurvePatch(): entry in {fileName} has no Curve name");
                return false;
            }

            CurveId curveId = GameDatabase.CurveRefManager.GetDataRefByName(entry.Curve);
            if (curveId == CurveId.Invalid)
            {
                Logger.Warn($"ApplyCurvePatch(): no curve named '{entry.Curve}' ({fileName})");
                return false;
            }

            Curve curve = GameDatabase.GetCurve(curveId);
            if (curve == null)
            {
                Logger.Warn($"ApplyCurvePatch(): curve '{entry.Curve}' failed to load ({fileName})");
                return false;
            }

            if (entry.Scale.HasValue)
            {
                curve.ScaleAll(entry.Scale.Value);
                Logger.Trace($"Patch Curve: {entry.Curve} scaled by {entry.Scale.Value}");
                return true;
            }

            if (entry.Position.HasValue == false || entry.Value.HasValue == false)
            {
                Logger.Warn($"ApplyCurvePatch(): '{entry.Curve}' needs either Scale, or both Position and Value ({fileName})");
                return false;
            }

            int position = entry.Position.Value;

            // Same drift check prototype patches get: state what you expected to find, and hear about it if the
            // curve has changed underneath the patch.
            if (entry.OriginalValue.HasValue)
            {
                float actual = curve.GetAt(position);
                if (actual != entry.OriginalValue.Value)
                    Logger.Warn($"OriginalValue mismatch: curve '{entry.Curve}' at position {position} expected " +
                                $"'{entry.OriginalValue.Value}' but found '{actual}'. Applying anyway.");
            }

            if (curve.SetAt(position, entry.Value.Value) == false)
            {
                Logger.Warn($"ApplyCurvePatch(): position {position} is outside '{entry.Curve}' " +
                            $"({curve.MinPosition}..{curve.MaxPosition}) ({fileName})");
                return false;
            }

            Logger.Trace($"Patch Curve: {entry.Curve}[{position}] = {entry.Value.Value}");
            return true;
        }

        private bool LoadPatchDataFromDisk(string prefix)
        {
            string patchDirectory = PatchDirectoryOverride ?? Path.Combine(FileHelper.DataDirectory, "Game", "Patches");
            if (Directory.Exists(patchDirectory) == false)
            {
                Logger.Warn("LoadPatchDataFromDisk(): Game data directory not found");
                return false;
            }

            int count = 0;
            int skippedDisabled = 0;
            int skippedUnknownPrototype = 0;
            var options = new JsonSerializerOptions { Converters = { new PatchEntryConverter() } };

            // Read all .json files that start with the specified prefix
            foreach (string filePath in FileHelper.GetFilesWithPrefix(patchDirectory, prefix, "json"))
            {
                string fileName = Path.GetFileName(filePath);

                PrototypePatchEntry[] updateValues = FileHelper.DeserializeJson<PrototypePatchEntry[]>(filePath, options);
                if (updateValues == null)
                {
                    Logger.Warn($"LoadPatchDataFromDisk(): Failed to parse {fileName}, skipping");
                    continue;
                }

                foreach (PrototypePatchEntry value in updateValues)
                {
                    if (value.Enabled == false)
                    {
                        skippedDisabled++;
                        continue;
                    }

                    PrototypeId prototypeId = GameDatabase.GetPrototypeRefByName(value.Prototype);
                    if (prototypeId == PrototypeId.Invalid)
                    {
                        // Used to be a silent `continue`, which meant a typo'd or renamed prototype name made
                        // the whole entry vanish with no trace anywhere - it never even reached _patchDict, so
                        // --patchstatus could not report it either. Say so out loud.
                        Logger.Warn($"LoadPatchDataFromDisk(): {fileName} targets unknown prototype '{value.Prototype}' (Path={value.Path}) - entry skipped");
                        skippedUnknownPrototype++;
                        continue;
                    }

                    AddPatchValue(prototypeId, value);
                    count++;
                }

                Logger.Trace($"Parsed patch data from {fileName}");
            }

            if (count == 0)
                return false;

            string skipSummary = string.Empty;
            if (skippedDisabled > 0 || skippedUnknownPrototype > 0)
                skipSummary = $" (skipped {skippedDisabled} disabled, {skippedUnknownPrototype} targeting unknown prototypes)";

            Logger.Info($"Loaded {count} {prefix} patches{skipSummary}");
            return true;
        }

        private void AddPatchValue(PrototypeId prototypeId, in PrototypePatchEntry value)
        {
            if (_patchDict.TryGetValue(prototypeId, out var patchList) == false)
            {
                patchList = [];
                _patchDict[prototypeId] = patchList;
            }
            patchList.Add(value);
        }

        public bool CheckProperties(PrototypeId protoRef, out Properties.PropertyCollection prop)
        {
            prop = null;
            if (_initialized == false) return false;

            if (protoRef != PrototypeId.Invalid && _patchDict.TryGetValue(protoRef, out var list))
                foreach (var entry in list)
                    if (entry.Value.ValueType == ValueType.Properties)
                    {
                        prop = entry.Value.GetValue() as Properties.PropertyCollection;
                        return prop != null;
                    }

            return false;
        }

        public bool PreCheck(PrototypeId protoRef)
        {
            if (_initialized == false) return false;

            if (protoRef != PrototypeId.Invalid && _patchDict.TryGetValue(protoRef, out var list))
            {
                if (NotPatched(list))
                    _context.ProtoStack.Push(protoRef);
            }

            return _context.ProtoStack.Count > 0;
        }

        private static bool NotPatched(List<PrototypePatchEntry> list)
        {
            foreach (var entry in list)
                if (entry.Patched == false) return true;
            return false;
        }

        public void PostOverride(Prototype prototype)
        {
            if (_context.ProtoStack.Count == 0) return;

            string currentPath = string.Empty;
            if (prototype.DataRef == PrototypeId.Invalid
                && _context.PathDict.TryGetValue(prototype, out currentPath) == false) return;

            PrototypeId patchProtoRef = _context.ProtoStack.Peek();
            if (prototype.DataRef != PrototypeId.Invalid)
            {
                if (prototype.DataRef != patchProtoRef) return;
                if (_patchDict.ContainsKey(prototype.DataRef))
                    patchProtoRef = _context.ProtoStack.Pop();
            }

            if (_patchDict.TryGetValue(patchProtoRef, out var list) == false) return;

            foreach (var entry in list)
                if (entry.Patched == false || CountAllMatches)
                    CheckAndUpdate(entry, prototype, currentPath);

            if (_context.ProtoStack.Count == 0)
                _context.PathDict.Clear();
        }

        private static bool CheckAndUpdate(PrototypePatchEntry entry, Prototype prototype, string currentPath)
        {
            if (currentPath.StartsWith('.')) currentPath = currentPath[1..];
            if (entry.СlearPath != currentPath)
            {
                // The entry did not match here, but it WOULD have on a patcher without mixin registration.
                // Record the qualified path that reaches this node, but only if the field actually exists on
                // it - otherwise this suggests rewrites pointing at nodes that could never have been the
                // target, which is how a "helpful" migration silently moves a patch somewhere new.
                if (SuggestMixinRewrites && entry.Patched == false)
                {
                    string legacy = ToLegacyPath(currentPath);
                    if (legacy != null && entry.СlearPath == legacy
                        && prototype.GetType().GetProperty(entry.FieldName) != null)
                    {
                        // Keep the ORIGINAL final segment verbatim, including any [0] or [] marker.
                        // Rebuilding it from FieldName drops those, because the constructor strips the
                        // bracket off into ArrayIndex/ArrayValue - which silently turned
                        // "StackingBehavior.StacksByKeyword[0]" into a path with no index at all.
                        string suffix = entry.СlearPath.Length == 0
                            ? $".{entry.Path}"
                            : entry.Path[entry.СlearPath.Length..];
                        string qualified = $"{currentPath}{suffix}";
                        MixinRewrites.Add((entry.Prototype, entry.Path, qualified));
                    }
                }

                return false;
            }

            var fieldInfo = prototype.GetType().GetProperty(entry.FieldName);
            if (fieldInfo == null)
            {
                // List what IS available - a mistyped or renamed field is the single most common patch-authoring
                // error, and "not found" alone gives the author nothing to work from.
                string available = string.Join(", ", prototype.GetType()
                    .GetProperties()
                    .Where(p => p.CanWrite)
                    .Select(p => p.Name)
                    .OrderBy(n => n));

                // Recorded rather than warned: the search visits every node whose accumulated path matches, and
                // sibling nodes of different types legitimately lack the field. Warning here fires on patches
                // that then apply correctly one node later. Surfaced by ReportUnappliedEntries() instead, which
                // only speaks up for entries that never landed at all.
                entry.LastFieldMiss = $"{entry.FieldName} not found on {prototype.GetType().Name}. Writable fields: {available}";
                Logger.Trace($"CheckAndUpdate: {entry.LastFieldMiss} for {entry.Prototype} (Path={entry.Path})");
                return false;
            }

            // Count every distinct node this path matches, not just the first. An entry that matches more
            // than once is AMBIGUOUS: which node wins depends on construction order, which is exactly the
            // wrong-node class of bug this patcher has produced before.
            entry.MatchCount++;

            // Already applied and we are only here to count - do not write a second time.
            if (entry.Patched && CountAllMatches)
                return true;

            if (UpdateValue(prototype, fieldInfo, entry))
                Logger.Trace($"Patch Prototype: {entry.Prototype} {entry.Path} = {entry.Value.GetValue()}");

            return true;
        }

        public static object ConvertValue(object rawValue, Type targetType)
        {
            if (targetType.IsInstanceOfType(rawValue))
                return rawValue;

            // Calligraphy has no enum type - it stores enum values as ASSETS, so a field that is an enum on
            // this side arrives as an AssetId. Resolve the asset back to its name and parse the enum from it.
            //
            // This is what lets a patch entry exported straight out of OpenCalligraphy apply as-is: the
            // exporter can see the field is asset-backed but has no way to know whether the server declares it
            // as an AssetId or as an enum. The server does know, so it is the right place to reconcile.
            if (targetType.IsEnum && rawValue is AssetId enumAssetId)
            {
                string assetName = GameDatabase.GetAssetName(enumAssetId);

                // An id that resolves to no name at all used to fall straight through to the generic
                // conversion below and skip the patch without a word - indistinguishable from success, and
                // --tracepatch still reported the entry as applied. Say so instead. This is what an
                // OpenCalligraphy export hits when it emits an AssetId this data set does not contain.
                if (string.IsNullOrEmpty(assetName))
                {
                    Logger.Warn($"ConvertValue(): asset id 0x{(ulong)enumAssetId:X} does not resolve to any asset name, " +
                                $"so it cannot be converted to enum {targetType.Name}. Write the value as " +
                                $"\"ValueType\": \"Enum\" with the member name instead.");
                }
                else
                {
                    // Asset names can be qualified (e.g. "PickMethodType.PickWeight"); the enum member is the
                    // last segment.
                    int separatorIndex = assetName.LastIndexOf('.');
                    if (separatorIndex != -1 && separatorIndex < assetName.Length - 1)
                        assetName = assetName[(separatorIndex + 1)..];

                    if (Enum.TryParse(targetType, assetName, true, out object enumValue))
                        return enumValue;

                    Logger.Warn($"ConvertValue(): asset '{assetName}' (0x{(ulong)enumAssetId:X}) is not a member of enum {targetType.Name}");
                }
            }

            // Handle array types - convert to correct element type if needed
            if (targetType.IsArray)
            {
                if (rawValue is JsonPrototypeArray jsonArray)
                {
                    Type elementType = targetType.GetElementType();
                    return jsonArray.GetTypedPrototypes(elementType);
                }

                if (rawValue.GetType().IsArray)
                {
                    Type targetElementType = targetType.GetElementType();
                    Type sourceElementType = rawValue.GetType().GetElementType();

                    // If element types are compatible, create new array with correct type
                    if (targetElementType != sourceElementType &&
                        (targetElementType.IsAssignableFrom(sourceElementType) || sourceElementType.IsAssignableFrom(targetElementType)))
                    {
                        Array sourceArray = (Array)rawValue;
                        Array targetArray = Array.CreateInstance(targetElementType, sourceArray.Length);

                        for (int i = 0; i < sourceArray.Length; i++)
                        {
                            object element = sourceArray.GetValue(i);
                            if (element != null && targetElementType.IsInstanceOfType(element))
                                targetArray.SetValue(element, i);
                            else if (element != null)
                                targetArray.SetValue(ConvertValue(element, targetElementType), i);
                            else
                                targetArray.SetValue(null, i);
                        }

                        return targetArray;
                    }

                    return rawValue;
                }
            }

            if (typeof(Prototype).IsAssignableFrom(targetType))
            {
                // IsAssignableFrom (not IsSubclassOf) so this also covers fields declared as the base
                // Prototype class itself (e.g. ScoringEventPrototype.Proto0/Proto1/Proto2) - IsSubclassOf
                // is always false when targetType IS Prototype, not just a derived type, which silently
                // broke PrototypeId-based patches on any such field (falls through to the generic
                // converter below and throws "Invalid cast from PrototypeId to Prototype" - caught and
                // logged as a warning by UpdateValue, but CheckAndUpdate's Trace log right after still
                // unconditionally prints as if the patch succeeded, making this very easy to miss).
                PrototypeId? protoRef = null;
                switch (rawValue)
                {
                    case PrototypeId protoId:   protoRef = protoId; break;
                    case ulong dataId:          protoRef = (PrototypeId)dataId; break;
                }

                if (protoRef.HasValue)
                {
                    PatchContext contextBefore = Instance.CreateSubContext();

                    Prototype proto = GameDatabase.GetPrototype<Prototype>(protoRef.Value);

                    Instance.RestoreContext(contextBefore);

                    return proto;
                }
            }

            // MarkerPrototype.Rotation is an Orientation (Yaw/Pitch/Roll), not a Vector3, but there's no
            // dedicated "Orientation" ValueType in patch data - JSON authors reuse ValueType:Vector3 for it
            // (same 3-float array shape), which used to fail here since Vector3 doesn't implement
            // IConvertible and there's no TypeConverter between the two distinct structs.

            // TODO: Remove this absolute bio-hazard once all district JSONs use ValueType.Orientation
            if (targetType == typeof(Orientation) && rawValue is Vector3 vec)
                return new Orientation(vec.X, vec.Y, vec.Z);

            TypeConverter converter = TypeDescriptor.GetConverter(targetType);
            if (converter != null && converter.CanConvertFrom(rawValue.GetType()))
                return converter.ConvertFrom(rawValue);

            return Convert.ChangeType(rawValue, targetType);
        }

        private static bool UpdateValue(Prototype prototype, PropertyInfo fieldInfo, PrototypePatchEntry entry)
        {
            try
            {
                Type fieldType = fieldInfo.PropertyType;

                // Curve properties are retargeted in place on the prototype's own collection. They cannot go
                // through ConvertValue/SetValue like a normal field: the value is not a number but a
                // (CurveId, index property) pair held in a separate list inside the collection.
                if (entry.Value is CurvePropertiesValue curveValue)
                {
                    if (fieldInfo.GetValue(prototype) is not Properties.PropertyCollection collection)
                        throw new InvalidOperationException(
                            $"Field {fieldInfo.Name} is not a PropertyCollection, so curve properties cannot be set on it.");

                    foreach (CurvePropertySpec spec in curveValue.Specs)
                    {
                        Properties.PropertyInfo info = GameDatabase.PropertyInfoTable.LookupPropertyInfo(spec.PropertyId.Enum);

                        // Default to the index the property already uses. Retargeting the curve and
                        // retargeting the index are separate intents, and silently resetting the index to
                        // something arbitrary would change the value in a way the entry never asked for.
                        Properties.PropertyId indexPropId = spec.IndexPropertyId
                            ?? collection.GetIndexPropertyIdForCurveProperty(spec.PropertyId);

                        collection.SetCurveProperty(spec.PropertyId, spec.CurveId, indexPropId,
                            info, Properties.SetPropertyFlags.None, true);
                    }

                    entry.Patched = true;
                    return true;
                }

                if (entry.ArrayValue)
                {
                    if (entry.ArrayIndex != -1)
                        SetIndexValue(prototype, fieldInfo, entry.ArrayIndex, entry.Value);
                    else
                        InsertValue(prototype, fieldInfo, entry);
                }
                else
                {
                    VerifyOriginalValue(prototype, fieldInfo, entry, fieldType);

                    object convertedValue = ConvertValue(entry.Value.GetValue(), fieldType);
                    fieldInfo.SetValue(prototype, convertedValue);
                }
                entry.Patched = true;
                return true;
            }
            catch (Exception ex)
            {
                Logger.WarnException(ex, $"Failed UpdateValue: [{entry.Prototype}] [{entry.Path}] {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Warns if the field does not currently hold the entry's declared <c>OriginalValue</c>.
        /// </summary>
        /// <remarks>
        /// This is the guard against a patch that quietly writes to the wrong place. A path like
        /// "Choices[17].NoDropPercent" was found to resolve to a node three levels deep in an unrelated branch,
        /// editing data nobody intended while --validatepatches and --tracepatch both reported success. Stating
        /// the value the author saw when they wrote the entry makes that detectable.
        ///
        /// It also catches an entry that has gone stale: if the underlying data changes so the original no
        /// longer matches, the patch is probably no longer doing what its description claims.
        ///
        /// Deliberately warn-and-continue rather than skip. A false mismatch - a float formatted differently,
        /// say - would otherwise silently disable a working patch, trading one invisible failure for another.
        /// </remarks>
        private static void VerifyOriginalValue(Prototype prototype, PropertyInfo fieldInfo, PrototypePatchEntry entry, Type fieldType)
        {
            if (entry.OriginalValue == null)
                return;

            object expected = ConvertValue(entry.OriginalValue.GetValue(), fieldType);
            object actual = fieldInfo.GetValue(prototype);

            if (Equals(expected, actual))
                return;

            Logger.Warn($"OriginalValue mismatch: [{entry.Prototype}] [{entry.Path}] expected '{expected ?? "null"}' " +
                        $"but found '{actual ?? "null"}'. The patch is being applied anyway, but it may be aimed at the " +
                        $"wrong node or written against data that has since changed.");
        }

        private static void SetIndexValue(Prototype prototype, PropertyInfo fieldInfo, int index, ValueBase value)
        {
            Type fieldType = fieldInfo.PropertyType;
            if (fieldType.IsArray == false)
                throw new InvalidOperationException($"Field {fieldInfo.Name} is not array.");

            Array array = (Array)fieldInfo.GetValue(prototype);
            if (array == null || index < 0 || index >= array.Length)
                throw new IndexOutOfRangeException($"Invalid index {index} for array {fieldInfo.Name}.");

            object valueEntry = value.GetValue();

            var entryType = valueEntry.GetType();
            Type elementType = fieldType.GetElementType();

            if (elementType == null || IsTypeCompatible(elementType, entryType, value.ValueType) == false)
                throw new InvalidOperationException($"Type {value.ValueType} is not assignable to {elementType?.Name}.");

            object converted = GetElementValue(valueEntry, elementType);
            array.SetValue(converted, index);
        }

        private static void InsertValue(Prototype prototype, PropertyInfo fieldInfo, PrototypePatchEntry entry)
        {
            ValueBase value = entry.Value;
            Type fieldType = fieldInfo.PropertyType; 

            if (fieldType == typeof(PrototypeMixinList))
            {
                InsertMixinValue(prototype, fieldInfo, value);
                return;
            }

            if (fieldType.IsArray == false)
                throw new InvalidOperationException($"Field {fieldInfo.Name} is not array.");     

            var valueEntry = value.GetValue();

            var entryType = valueEntry.GetType();
            Type elementType = fieldType.GetElementType(); 

            if (elementType == null || IsTypeCompatible(elementType, entryType, value.ValueType) == false)
                throw new InvalidOperationException($"Type {value.ValueType} is not assignable for {elementType?.Name}.");

            var currentArray = (Array)fieldInfo.GetValue(prototype);

            int newLength = CalcNewLength(currentArray, valueEntry);
            var newArray = Array.CreateInstance(elementType, newLength);

            if (currentArray != null)
                Array.Copy(currentArray, newArray, currentArray.Length);

            AddElements(newArray, elementType, valueEntry, currentArray?.Length ?? 0);

            fieldInfo.SetValue(prototype, newArray);
        }

        private static void InsertMixinValue(Prototype owner, PropertyInfo fieldInfo, ValueBase value)
        {
            if (fieldInfo.GetValue(owner) is not PrototypeMixinList mixinList)
            {
                mixinList = new PrototypeMixinList();
                fieldInfo.SetValue(owner, mixinList);
                owner.SetDynamicFieldOwner(mixinList);
            }

            object valueEntry = value.GetValue();
            if (valueEntry is Array array)
            {
                foreach (object entry in array)
                    AddMixinElement(owner, mixinList, entry);
            }
            else
            {
                AddMixinElement(owner, mixinList, valueEntry);
            }
        }

        private static void AddMixinElement(Prototype owner, PrototypeMixinList mixinList, object valueEntry)
        {
            if (valueEntry is PrototypeId dataRef)
                valueEntry = GameDatabase.GetPrototype<Prototype>(dataRef);

            if (valueEntry is not Prototype mixinPrototype)
                throw new InvalidOperationException($"Type {valueEntry?.GetType().Name ?? "null"} is not assignable for PrototypeMixinList.");

            PrototypeId blueprintSourceRef = mixinPrototype.ParentDataRef != PrototypeId.Invalid
                ? mixinPrototype.ParentDataRef
                : mixinPrototype.DataRef;

            BlueprintId blueprintRef = blueprintSourceRef != PrototypeId.Invalid
                ? GameDatabase.DataDirectory.GetPrototypeBlueprintDataRef(blueprintSourceRef)
                : BlueprintId.Invalid;

            if (blueprintRef == BlueprintId.Invalid)
                throw new InvalidOperationException($"Could not resolve mixin blueprint for {mixinPrototype.GetType().Name}.");

            byte copyNum = 0;
            foreach (PrototypeMixinListItem item in mixinList)
            {
                if (item.BlueprintRef == blueprintRef && item.BlueprintCopyNum >= copyNum)
                    copyNum = (byte)(item.BlueprintCopyNum + 1);
            }

            owner.SetDynamicFieldOwner(mixinPrototype);
            PatchContext contextBefore = Instance.CreateSubContext();
            mixinPrototype.PostProcess();
            Instance.RestoreContext(contextBefore);
            mixinList.Add(new PrototypeMixinListItem
            {
                Prototype = mixinPrototype,
                BlueprintRef = blueprintRef,
                BlueprintCopyNum = copyNum
            });
        }

        private static int CalcNewLength(Array currentArray, object valueEntry)
        {
            int currentLength = currentArray?.Length ?? 0;
            int valuesCount = 1;
            if (valueEntry is Array array)
            {
                int length = array.Length;
                if (length > 1) valuesCount = length;
            }
            return currentLength + valuesCount;
        }

        private static bool IsTypeCompatible(Type baseType, Type entryType, ValueType valueType)
        {
            if (entryType.IsArray) entryType = entryType.GetElementType();

            if (valueType == ValueType.PrototypeDataRef || valueType == ValueType.PrototypeDataRefArray)
            {
                // A PrototypeDataRef value is a PrototypeId, and it can land in two different shapes of
                // field: one holding ids (PrototypeId[], e.g. PowerPrototype.Keywords) or one holding
                // constructed instances (Prototype[]). Rewriting entryType to Prototype covers only the
                // second, so appending to an id array always failed the check below and threw - swallowed by
                // UpdateValue's catch, leaving the entry silently unapplied. Every Keywords[] append written
                // as PrototypeDataRefArray has been a no-op because of this.
                if (baseType == typeof(PrototypeId))
                    return true;

                entryType = typeof(Prototype);
            }

            return baseType.IsAssignableFrom(entryType) || entryType.IsAssignableFrom(baseType);
        }

        private static void AddElements(Array newArray, Type elementType, object valueEntry, int lastIndex)
        {
            if (valueEntry is Array array)
            {
                foreach (var entry in array)
                {
                    object elementValue = GetElementValue(entry, elementType);
                    newArray.SetValue(elementValue, lastIndex++);
                }
            }
            else
            {
                object elementValue = GetElementValue(valueEntry, elementType);
                newArray.SetValue(elementValue, lastIndex);
            }
        }

        private static object GetElementValue(object valueEntry, Type elementType)
        {
            if (elementType.IsClass && valueEntry is PrototypeId dataRef)
            {
                // Same subcontext isolation as ConvertValue()'s Prototype-by-id case: constructing this
                // referenced prototype (e.g. appending an existing item into another table's Choices[])
                // can recursively trigger its own PreCheck()/PostOverride() if it has pending patches of
                // its own, which must not interact with the outer prototype's in-progress ProtoStack/PathDict.
                PatchContext contextBefore = Instance.CreateSubContext();
                Prototype prototype = GameDatabase.GetPrototype<Prototype>(dataRef);
                Instance.RestoreContext(contextBefore);

                if (prototype == null)
                    throw new InvalidOperationException($"DataRef {dataRef} is not Prototype.");
                valueEntry = prototype;
            }

            // If valueEntry is already the correct type (e.g., Prototype), return it directly
            if (elementType.IsInstanceOfType(valueEntry))
                return valueEntry;

            return ConvertValue(valueEntry, elementType);            
        }

        public void SetPath(Prototype parent, Prototype child, string fieldName)
        {
            string parentPath = _context.PathDict.TryGetValue(parent, out var path) ? path : string.Empty;
            if (parent.DataRef != PrototypeId.Invalid && _patchDict.ContainsKey(parent.DataRef))
                parentPath = string.Empty;
            _context.PathDict[child] = $"{parentPath}.{fieldName}";
        }

        public void SetPathIndex(Prototype parent, Prototype child, string fieldName, int index)
        {
            string parentPath = _context.PathDict.TryGetValue(parent, out var path) ? path : string.Empty;
            if (parent.DataRef != PrototypeId.Invalid && _patchDict.ContainsKey(parent.DataRef))
                parentPath = string.Empty;
            _context.PathDict[child] = $"{parentPath}.{fieldName}[{index}]";
        }

        /// <summary>
        /// Registers every member of a mixin list so patches can address them, keyed by blueprint id and copy
        /// number rather than by position: <c>Field[BlueprintId=12345,Copy=0].SubField</c>.
        /// </summary>
        /// <remarks>
        /// Mixin list members are traversed during PostProcess but were never registered here, which made
        /// everything inside a ListMixin field silently unpatchable - a patch targeting one would simply never
        /// find a matching path and sit at Patched=false forever.
        ///
        /// Position would be a poor key for these: a mixin list's order is not a stable, author-visible thing
        /// the way an array's is, whereas the blueprint id identifies the mixin itself. Ported from
        /// Doodswh/MHServerEmu (COA_Build).
        /// </remarks>
        public void SetPathMixin(Prototype parent, PrototypeMixinList mixinList, string fieldName)
        {
            if (mixinList == null)
                return;

            if (RegisterMixinPaths == false)
                return;

            string parentPath = _context.PathDict.TryGetValue(parent, out var path) ? path : string.Empty;
            if (parent.DataRef != PrototypeId.Invalid && _patchDict.ContainsKey(parent.DataRef))
                parentPath = string.Empty;

            foreach (PrototypeMixinListItem item in mixinList)
            {
                if (item?.Prototype == null)
                    continue;

                // Keep the "BlueprintId=" label from the upstream format so patch files stay portable, even
                // though the field is called BlueprintRef here.
                string mixinKey = $"{fieldName}[BlueprintId={(ulong)item.BlueprintRef},Copy={item.BlueprintCopyNum}]";
                _context.PathDict[item.Prototype] = string.IsNullOrEmpty(parentPath) ? mixinKey : $"{parentPath}.{mixinKey}";
            }
        }

        private PatchContext CreateSubContext()
        {
            PatchContext oldContext = _context;
            _context = new();
            return oldContext;
        }

        private void RestoreContext(in PatchContext context)
        {
            _context = context;
        }

        private readonly struct PatchContext
        {
            public readonly Stack<PrototypeId> ProtoStack;
            public readonly Dictionary<Prototype, string> PathDict;

            public PatchContext()
            {
                ProtoStack = new();
                PathDict = new();
            }
        }

        /// <summary>
        /// Returns every registered patch entry across all resolved prototypes, for external tooling
        /// (e.g. LootTableDumper's --patchstatus) to report on Patched status after a full load pass.
        /// Not used by any runtime game logic.
        /// </summary>
        /// <summary>
        /// Warns about every enabled entry that never applied to anything, and returns how many there were.
        /// </summary>
        /// <remarks>
        /// Call once all prototypes have been constructed - patching happens during construction, which is lazy,
        /// so an entry that has not applied yet is not necessarily broken until everything has been loaded.
        /// </remarks>
        /// <summary>
        /// Builds a human-readable status report of every registered patch entry, optionally filtered by a
        /// substring of the target prototype name.
        /// </summary>
        /// <remarks>
        /// <paramref name="forceLoad"/> matters more than it looks. Patching happens during prototype
        /// CONSTRUCTION, which is lazy - so an entry reads as unapplied simply because nothing has touched
        /// its prototype yet, which is indistinguishable from a genuinely broken path. Forcing every target
        /// to build first removes that ambiguity, and is what makes the report trustworthy on a freshly
        /// started server.
        ///
        /// Note what "applied" does and does not mean: it says the entry found a node and wrote to it. It
        /// does NOT say the node is one the game reads. A patch can apply, match exactly one node, write the
        /// correct property, and still change nothing in game - see the Colossus Call-In case, where the
        /// buff landed on the talent power's own eval instead of the condition it applies.
        /// </remarks>
        public string BuildPatchStatusReport(string filter = null, bool forceLoad = false)
        {
            filter ??= string.Empty;

            // Snapshot before force-loading: constructing prototypes can register further entries, and
            // enumerating the dictionary while that happens would throw.
            List<(PrototypeId ProtoRef, PrototypePatchEntry Entry)> all = EnumerateAllEntries().ToList();

            if (forceLoad)
            {
                foreach ((PrototypeId protoRef, _) in all)
                    GameDatabase.GetPrototype<Prototype>(protoRef);

                all = EnumerateAllEntries().ToList();
            }

            if (filter != string.Empty)
            {
                all = all.Where(e => e.Entry.Prototype != null
                                  && e.Entry.Prototype.Contains(filter, StringComparison.OrdinalIgnoreCase))
                         .ToList();
            }

            int enabled = all.Count(e => e.Entry.Enabled);
            int applied = all.Count(e => e.Entry.Enabled && e.Entry.Patched);
            int never = enabled - applied;

            StringBuilder sb = new();
            sb.AppendLine($"Patch status{(filter == string.Empty ? string.Empty : $" (filter '{filter}')")}: " +
                          $"{all.Count} registered, {enabled} enabled, {applied} applied, {never} never matched" +
                          $"{(forceLoad ? string.Empty : " - WITHOUT forceLoad, unapplied may just mean not yet constructed")}");

            foreach ((_, PrototypePatchEntry entry) in all)
            {
                if (entry.Enabled == false || entry.Patched)
                    continue;

                string reason = entry.LastFieldMiss ?? "no prototype in the graph matched this path";
                sb.AppendLine($"  NEVER: {entry.Prototype} (Path={entry.Path}) - {reason}");
            }

            // An entry matching more than one node is ambiguous - which one wins depends on construction
            // order, so it may be writing somewhere different from run to run.
            foreach ((_, PrototypePatchEntry entry) in all)
            {
                if (entry.MatchCount > 1)
                    sb.AppendLine($"  AMBIGUOUS ({entry.MatchCount} nodes matched): {entry.Prototype} (Path={entry.Path})");
            }

            return sb.ToString();
        }

        public int ReportUnappliedEntries()
        {
            int unapplied = 0;

            foreach ((PrototypeId protoRef, PrototypePatchEntry entry) in EnumerateAllEntries())
            {
                if (entry.Enabled == false || entry.Patched) continue;

                unapplied++;

                string reason = entry.LastFieldMiss ?? "no prototype in the graph matched this path";
                Logger.Warn($"Patch never applied: [{entry.Prototype}] Path={entry.Path} - {reason}");
            }

            if (unapplied > 0)
                Logger.Warn($"{unapplied} patch entr{(unapplied == 1 ? "y" : "ies")} never applied - see the lines above.");

            return unapplied;
        }

        public IEnumerable<(PrototypeId ProtoRef, PrototypePatchEntry Entry)> EnumerateAllEntries()
        {
            foreach (var kvp in _patchDict)
                foreach (PrototypePatchEntry entry in kvp.Value)
                    yield return (kvp.Key, entry);
        }
    }
}

