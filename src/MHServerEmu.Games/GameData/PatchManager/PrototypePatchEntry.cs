using MHServerEmu.Core.Logging;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Properties;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MHServerEmu.Games.GameData.PatchManager
{
    public class PrototypePatchEntry
    {
        public bool Enabled { get; }
        public string Prototype { get; }
        public string Path { get; }
        public string Description { get; }
        public ValueBase Value { get; }

        /// <summary>
        /// Optional. The value this entry expects to find before it writes, used to catch a patch that has
        /// landed on the wrong thing.
        /// </summary>
        /// <remarks>
        /// Entries without it behave exactly as before, so this is safe to add to a file at a time. Worth
        /// having because this patcher's failures are usually silent: a path that resolves to an unintended
        /// node, or a value that does not convert, both report success and change something else or nothing.
        /// A mismatch here warns, it does NOT skip the write - a false mismatch from formatting would
        /// otherwise turn a working patch off with no warning, which is the same class of problem.
        /// </remarks>
        public ValueBase OriginalValue { get; }

        [JsonIgnore]
        public string СlearPath { get; }
        [JsonIgnore]
        public string FieldName { get; }
        [JsonIgnore]
        public bool ArrayValue { get; }
        [JsonIgnore]
        public int ArrayIndex { get; }
        [JsonIgnore]
        public bool Patched { get; set; }

        /// <summary>
        /// How many distinct nodes this entry's path matched. Only counts past 1 when
        /// <see cref="PrototypePatchManager.CountAllMatches"/> is on. More than 1 means the path is
        /// ambiguous and which node wins depends on construction order.
        /// </summary>
        [JsonIgnore]
        public int MatchCount { get; set; }

        /// <summary>
        /// Why the last field lookup missed, kept so it can be reported if the entry never lands anywhere.
        /// </summary>
        /// <remarks>
        /// A miss is not by itself a failure. Several prototypes in a graph can share an accumulated path, so
        /// the search legitimately visits nodes that lack the field before reaching the one that has it -
        /// warning on each of those cries wolf on patches that applied perfectly.
        /// </remarks>
        [JsonIgnore]
        public string LastFieldMiss { get; set; }

        [JsonConstructor]
        public PrototypePatchEntry(bool enabled, string prototype, string path, string description, ValueBase value,
            ValueBase originalValue = null)
        {
            Enabled = enabled;
            Prototype = prototype;
            Path = path;
            Description = description;
            Value = value;
            OriginalValue = originalValue;

            int lastDotIndex = path.LastIndexOf('.');
            if (lastDotIndex == -1)
            {
                СlearPath = string.Empty;
                FieldName = path;
            }
            else
            {
                СlearPath = path[..lastDotIndex];
                FieldName = path[(lastDotIndex + 1)..];
            }

            ArrayIndex = -1;
            ArrayValue = false;
            int index = FieldName.LastIndexOf('[');
            if (index != -1)
            {
                ArrayValue = true;

                int endIndex = FieldName.LastIndexOf(']');
                if (endIndex > index)
                {
                    string indexStr = FieldName.Substring(index + 1, endIndex - index - 1);
                    if (int.TryParse(indexStr, out int parsedIndex))
                        ArrayIndex = parsedIndex;
                }

                FieldName = FieldName[..index];
            }

            Patched = false;
        }
    }

    public class PatchEntryConverter : JsonConverter<PrototypePatchEntry>
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        public override PrototypePatchEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            string valueTypeString = root.GetProperty("ValueType").GetString();
            valueTypeString = valueTypeString.Replace("[]", "Array");
            var valueType = Enum.Parse<ValueType>(valueTypeString);
            // Optional, and parsed with the entry's own ValueType so it is written the same way as Value.
            ValueBase originalValue = null;
            if (root.TryGetProperty("OriginalValue", out JsonElement originalValueElement))
                originalValue = GetValueBase(originalValueElement, valueType);

            var entry = new PrototypePatchEntry
            (
                root.GetProperty("Enabled").GetBoolean(),
                root.GetProperty("Prototype").GetString(),
                root.GetProperty("Path").GetString(),
                root.GetProperty("Description").GetString(),
                GetValueBase(root.GetProperty("Value"), valueType),
                originalValue
            );

            if (valueType == ValueType.Properties) entry.Patched = true;

            return entry;
        }

        public static ValueBase GetValueBase(JsonElement jsonElement, ValueType valueType)
        {
            return valueType switch
            {
                // Simple types
                ValueType.String => new SimpleValue<string>(jsonElement.GetString(), valueType),
                ValueType.Boolean => new SimpleValue<bool>(jsonElement.GetBoolean(), valueType),
                ValueType.Float => new SimpleValue<float>(jsonElement.GetSingle(), valueType),
                ValueType.Double => new SimpleValue<double>(jsonElement.GetDouble(), valueType),
                ValueType.Integer => new SimpleValue<int>(jsonElement.GetInt32(), valueType),
                ValueType.Long => new SimpleValue<long>(jsonElement.GetInt64(), valueType),
                ValueType.ULong => new SimpleValue<ulong>(jsonElement.GetUInt64(), valueType),
                ValueType.Enum => new SimpleValue<string>(jsonElement.GetString(), valueType),
                ValueType.Asset or
                ValueType.AssetId => new SimpleValue<AssetId>(ParseAssetRef(jsonElement), valueType),
                ValueType.PrototypeGuid => new SimpleValue<PrototypeGuid>((PrototypeGuid)jsonElement.GetUInt64(), valueType),
                ValueType.PrototypeId or
                ValueType.PrototypeDataRef => new SimpleValue<PrototypeId>(ParsePrototypeRef(jsonElement), valueType),
                ValueType.LocaleStringId => new SimpleValue<LocaleStringId>((LocaleStringId)jsonElement.GetUInt64(), valueType),
                ValueType.Vector3 => new SimpleValue<Vector3>(ParseJsonVector3(jsonElement), valueType),
                ValueType.Orientation => new SimpleValue<Orientation>(ParseJsonOrientation(jsonElement), valueType),
                ValueType.PropertyId => new SimpleValue<PropertyId>(ParseJsonPropertyIdSingle(jsonElement), valueType),
                // Kept as a detached copy: the JsonDocument this element belongs to is disposed once parsing
                // finishes, and reading a JsonElement after that throws.
                ValueType.RawJson => new SimpleValue<JsonElement>(jsonElement.Clone(), valueType),

                // Complex types. ComplexObject and Eval are the same lazy machinery as Prototype - they differ
                // only in accepting a "ClassName" instead of a "ParentDataRef", so a brand-new instance can be
                // built without needing some existing prototype of that class to clone.
                ValueType.Prototype or
                ValueType.ComplexObject or
                ValueType.Eval => new JsonPrototype(jsonElement, valueType),
                ValueType.Properties => new SimpleValue<PropertyCollection>(ParseJsonProperties(jsonElement), valueType),
                ValueType.CurveProperties => new CurvePropertiesValue(ParseJsonCurveProperties(jsonElement)),

                // Array types
                ValueType.PrototypeIdArray or
                ValueType.PrototypeDataRefArray => new ArrayValue<PrototypeId>(jsonElement, valueType, ParsePrototypeRef),
                ValueType.PrototypeArray => new JsonPrototypeArray(jsonElement),
                ValueType.StringArray => new ArrayValue<string>(jsonElement, valueType, x => x.GetString()),
                ValueType.BooleanArray => new ArrayValue<bool>(jsonElement, valueType, x => x.GetBoolean()),
                ValueType.FloatArray => new ArrayValue<float>(jsonElement, valueType, x => x.GetSingle()),
                ValueType.DoubleArray => new ArrayValue<double>(jsonElement, valueType, x => x.GetDouble()),
                ValueType.IntegerArray => new ArrayValue<int>(jsonElement, valueType, x => x.GetInt32()),
                ValueType.LongArray => new ArrayValue<long>(jsonElement, valueType, x => x.GetInt64()),
                ValueType.ULongArray => new ArrayValue<ulong>(jsonElement, valueType, x => x.GetUInt64()),
                ValueType.Vector3Array => new ArrayValue<Vector3>(jsonElement, valueType, ParseJsonVector3),
                ValueType.RawJsonArray => new ArrayValue<JsonElement>(jsonElement, valueType, x => x.Clone()),

                _ => throw new NotSupportedException($"Type {valueType} not support.")
            };
        }

        private static Orientation ParseJsonOrientation(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Json element is not array");

            var jsonArray = jsonElement.EnumerateArray().ToArray();
            if (jsonArray.Length != 3)
                throw new InvalidOperationException("Json element is not Orientation");

            return new Orientation(jsonArray[0].GetSingle(), jsonArray[1].GetSingle(), jsonArray[2].GetSingle());
        }

        private static Vector3 ParseJsonVector3(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Json element is not array");

            var jsonArray = jsonElement.EnumerateArray().ToArray();
            if (jsonArray.Length != 3) 
                throw new InvalidOperationException("Json element is not Vector3");

            return new Vector3(jsonArray[0].GetSingle(), jsonArray[1].GetSingle(), jsonArray[2].GetSingle());
        }

        public static PropertyCollection ParseJsonProperties(JsonElement jsonElement)
        {
            PropertyCollection properties = new ();
            var infoTable = GameDatabase.PropertyInfoTable;

            foreach (var property in jsonElement.EnumerateObject())
            {
                var propEnum = (PropertyEnum)Enum.Parse(typeof(PropertyEnum), property.Name);
                PropertyInfo propertyInfo = infoTable.LookupPropertyInfo(propEnum);
                PropertyId propId = ParseJsonPropertyId(property.Value, propEnum, propertyInfo);
                PropertyValue propValue = ParseJsonPropertyValue(property.Value, propertyInfo);
                properties.SetProperty(propValue, propId);
            }

            return properties;
        }

        /// <summary>
        /// Parses curve-property retargets: which curve a curve-backed property should read from.
        /// </summary>
        /// <remarks>
        /// A curve property stores a CurveId plus an index property rather than a number, so it cannot be
        /// written with a plain Properties patch - ParseJsonPropertyValue rejects it as "not int/float/bool".
        /// The engine's own idiom for making a power free is to point its EnduranceCost at
        /// EnduranceCostRank0NoCost.curve, which is what this expresses. Retargeting the reference is also
        /// the SAFE half of that idea: editing the shared cost curve itself would change every power reading
        /// it.
        ///
        /// <code>
        /// "Value": {
        ///   "EnduranceCost": {
        ///     "Params": [ "Type1" ],
        ///     "Curve": "Powers/Curves/EnduranceCosts/EnduranceCostRank0NoCost.curve"
        ///   }
        /// }
        /// </code>
        ///
        /// "IndexProperty" is optional and defaults to whatever the property already uses, since the index
        /// is almost always PowerRank and changing it is a different intent from changing the curve.
        /// </remarks>
        public static List<CurvePropertySpec> ParseJsonCurveProperties(JsonElement jsonElement)
        {
            List<CurvePropertySpec> specs = new();

            // Allocated once outside the loop. A stackalloc inside a foreach accumulates a frame per
            // iteration and can overflow the stack on a large object (CA2014); it is cleared and refilled
            // per entry instead.
            Span<PropertyParam> paramValues = stackalloc PropertyParam[Property.MaxParamCount];

            foreach (JsonProperty property in jsonElement.EnumerateObject())
            {
                if (Enum.TryParse(property.Name, true, out PropertyEnum propEnum) == false)
                {
                    Logger.Warn($"ParseJsonCurveProperties(): unknown PropertyEnum '{property.Name}'");
                    continue;
                }

                PropertyInfo propInfo = GameDatabase.PropertyInfoTable.LookupPropertyInfo(propEnum);
                if (propInfo == null)
                {
                    Logger.Warn($"ParseJsonCurveProperties(): no PropertyInfo for '{property.Name}'");
                    continue;
                }

                // Say so loudly rather than writing a curve into a property that never reads one - it would
                // apply "successfully" and change nothing.
                if (propInfo.IsCurveProperty == false)
                {
                    Logger.Warn($"ParseJsonCurveProperties(): '{property.Name}' is not a curve property, skipping");
                    continue;
                }

                if (property.Value.TryGetProperty("Curve", out JsonElement curveElement) == false)
                {
                    Logger.Warn($"ParseJsonCurveProperties(): '{property.Name}' needs a Curve");
                    continue;
                }

                CurveId curveId = curveElement.ValueKind == JsonValueKind.String
                    ? GameDatabase.CurveRefManager.GetDataRefByName(curveElement.GetString())
                    : (CurveId)curveElement.GetUInt64();

                if (curveId == CurveId.Invalid)
                {
                    Logger.Warn($"ParseJsonCurveProperties(): no curve named '{curveElement}'");
                    continue;
                }

                // Build the full property id, including params - EnduranceCost is per mana type, so
                // EnduranceCost[Type1] and EnduranceCost[Type2] are different properties.
                paramValues.Clear();
                propInfo.DefaultParamValues.CopyTo(paramValues);

                if (property.Value.TryGetProperty("Params", out JsonElement paramsElement)
                    && paramsElement.ValueKind == JsonValueKind.Array)
                {
                    JsonElement[] jsonArray = paramsElement.EnumerateArray().ToArray();
                    for (int i = 0; i < propInfo.ParamCount && i < Property.MaxParamCount && i < jsonArray.Length; i++)
                        paramValues[i] = ParseJsonPropertyParam(jsonArray[i], propEnum, propInfo, i);
                }

                PropertyId propId = new(propEnum, paramValues);

                PropertyId? indexPropId = null;
                if (property.Value.TryGetProperty("IndexProperty", out JsonElement indexElement))
                {
                    if (Enum.TryParse(indexElement.GetString(), true, out PropertyEnum indexEnum))
                        indexPropId = new(indexEnum);
                    else
                        Logger.Warn($"ParseJsonCurveProperties(): unknown IndexProperty '{indexElement}'");
                }

                specs.Add(new CurvePropertySpec(propId, curveId, indexPropId));
            }

            return specs;
        }

        /// <summary>
        /// Recovers a <see cref="PropertyEnum"/> from a property prototype NAME, for the mixin form
        /// (<c>Property/Mixin/DamageMultForPowerKeywordProp.defaults</c>) that the id lookup table does not
        /// contain. Returns <see cref="PropertyEnum.Invalid"/> if the name is not a property.
        /// </summary>
        private static PropertyEnum ParsePropertyEnumFromMixinName(string name)
        {
            if (string.IsNullOrEmpty(name)) return PropertyEnum.Invalid;

            int slash = name.LastIndexOf('/');
            if (slash >= 0 && slash < name.Length - 1)
                name = name[(slash + 1)..];

            if (name.EndsWith(".defaults", StringComparison.OrdinalIgnoreCase))
                name = name[..^".defaults".Length];

            // Try the name as-is first: a property whose own name genuinely ends in "Prop" would otherwise
            // be mangled by the suffix strip below.
            if (Enum.TryParse(name, true, out PropertyEnum propEnum))
                return propEnum;

            if (name.EndsWith("Prop", StringComparison.Ordinal)
                && Enum.TryParse(name[..^"Prop".Length], true, out propEnum))
                return propEnum;

            return PropertyEnum.Invalid;
        }

        public static PropertyId ParseJsonPropertyId(JsonElement jsonElement, PropertyEnum propEnum, PropertyInfo propInfo)
        {
            int paramCount = propInfo.ParamCount;
            if (paramCount == 0) return new(propEnum);

            var jsonArray = jsonElement.EnumerateArray().ToArray();
            Span<PropertyParam> paramValues = stackalloc PropertyParam[Property.MaxParamCount];
            propInfo.DefaultParamValues.CopyTo(paramValues);

            for (int i = 0; i < paramCount; i++)
            {
                if (i >= 4) break;
                if (i >= jsonArray.Length) continue;

                paramValues[i] = ParseJsonPropertyParam(jsonArray[i], propEnum, propInfo, i);
            }

            return new(propEnum, paramValues);
        }

        /// <summary>
        /// Parses a single property parameter according to its declared param type.
        /// </summary>
        private static PropertyParam ParseJsonPropertyParam(JsonElement paramValue, PropertyEnum propEnum, PropertyInfo propInfo, int index)
        {
            switch (propInfo.GetParamType(index))
            {
                case PropertyParamType.Asset:
                    if (paramValue.TryGetInt64(out long enumValue))
                        return (PropertyParam)(int)enumValue;

                    var assetParam = (AssetId)ParseJsonElement(paramValue, typeof(AssetId));
                    return Property.ToParam(assetParam);

                case PropertyParamType.Prototype:
                    var protoRefParam = (PrototypeId)ParseJsonElement(paramValue, typeof(PrototypeId));
                    return Property.ToParam(propEnum, index, protoRefParam);

                case PropertyParamType.Integer:
                    if (paramValue.TryGetInt64(out long decimalValue))
                        return (PropertyParam)(int)decimalValue;
                    return default;

                default:
                    throw new InvalidOperationException("Encountered an unknown prop param type in an ParseJsonPropertyId!");
            }
        }

        /// <summary>
        /// Parses a standalone <see cref="PropertyId"/> value, for patching a field that holds a property id
        /// rather than a whole PropertyCollection.
        /// </summary>
        /// <remarks>
        /// Accepts <c>{ "PropertyEnum": "Health" }</c>, with parameters given either as
        /// <c>"Params": [ ... ]</c> or as individual <c>"Param0"</c>, <c>"Param1"</c>, ... keys (the latter
        /// matching Doodswh/MHServerEmu's format, so patch files stay interchangeable).
        /// </remarks>
        /// <summary>
        /// Reads a prototype reference written either as a name or as a raw id.
        /// </summary>
        /// <remarks>
        /// Patch values used to be numeric ids only, which made files unreviewable - "Value": 8643734431032020798
        /// says nothing about what it does, and checking it meant a lookup in a separate tool. Names are accepted
        /// so an entry can read "Entity/Items/Rarity/R5Cosmic.prototype" instead. Numeric ids still work
        /// unchanged, so every existing patch file keeps applying exactly as before.
        /// </remarks>
        /// <summary>
        /// Name-or-id prototype reference parsing, exposed for <see cref="JsonPrototype"/>'s ParentDataRef.
        /// </summary>
        public static PrototypeId ParsePrototypeRefPublic(JsonElement jsonElement) => ParsePrototypeRef(jsonElement);

        /// <summary>
        /// Object-form PropertyId parsing, exposed for <see cref="JsonPrototype"/>'s PropertyId fields.
        /// </summary>
        public static PropertyId ParseJsonPropertyIdSinglePublic(JsonElement jsonElement) => ParseJsonPropertyIdSingle(jsonElement);

        private static PrototypeId ParsePrototypeRef(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.String)
                return (PrototypeId)jsonElement.GetUInt64();

            string name = jsonElement.GetString();
            PrototypeId protoRef = GameDatabase.GetPrototypeRefByName(name);

            // Warn rather than fail silently. A mistyped name would otherwise resolve to Invalid and the patch
            // would apply "successfully" while doing nothing - the failure mode this format is meant to avoid.
            if (protoRef == PrototypeId.Invalid)
                Logger.Warn($"ParsePrototypeRef(): no prototype named '{name}'");

            return protoRef;
        }

        /// <summary>
        /// Reads an asset reference written either as a name or as a raw id.
        /// </summary>
        /// <remarks>
        /// Asset names are not unique - the reverse lookup is a linear search that returns the FIRST match
        /// (see <see cref="DataRefManager{T}.GetDataRefByName"/>, which keeps no dictionary for assets for
        /// exactly that reason). For the qualified names assets normally carry this is unambiguous, but if a
        /// bare name ever collides, use the numeric id instead.
        /// </remarks>
        private static AssetId ParseAssetRef(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.String)
                return (AssetId)jsonElement.GetUInt64();

            string name = jsonElement.GetString();
            AssetId assetRef = GameDatabase.StringRefManager.GetDataRefByName(name);

            if (assetRef == AssetId.Invalid)
                Logger.Warn($"ParseAssetRef(): no asset named '{name}'");

            return assetRef;
        }

        private static PropertyId ParseJsonPropertyIdSingle(JsonElement jsonElement)
        {
            // Bare string form: "CritChancePctAdd". Only valid for a property with no parameters, which is
            // why it is resolved and then verified below rather than assumed.
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                string bareName = jsonElement.GetString();
                if (Enum.TryParse(bareName, true, out PropertyEnum bareEnum) == false)
                {
                    Logger.Warn($"ParseJsonPropertyIdSingle(): unknown PropertyEnum '{bareName}'");
                    return PropertyId.Invalid;
                }

                PropertyInfo bareInfo = GameDatabase.PropertyInfoTable.LookupPropertyInfo(bareEnum);
                if (bareInfo != null && bareInfo.ParamCount > 0)
                    Logger.Warn($"ParseJsonPropertyIdSingle(): '{bareName}' takes {bareInfo.ParamCount} " +
                                $"parameter(s) but was written as a bare string, so they default. Use " +
                                $"{{\"PropertyEnum\": \"{bareName}\", \"Params\": [ ... ]}} to set them.");

                return new(bareEnum);
            }

            if (jsonElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Json element for PropertyId must be an object with a PropertyEnum, or a bare property name string.");

            PropertyEnum propEnum;

            // Three accepted spellings of the same thing, because three tools emit three of them:
            //   {"PropertyEnum": "Health"}                              - ours
            //   {"Property": "Health"}                                  - mtzimas92/MHServerEmu
            //   {"ParentDataRef": "Property/Mixin/HealthProp.defaults"} - OpenCalligraphy-style, by prototype
            // The third is resolved through PropertyInfoTable's reverse lookup. Supporting all three costs
            // nothing and means nobody has to rewrite a working patch file to move it between forks.
            if (jsonElement.TryGetProperty("PropertyEnum", out JsonElement propEnumElement)
                || jsonElement.TryGetProperty("Property", out propEnumElement))
            {
                string enumString = propEnumElement.GetString();
                if (Enum.TryParse(enumString, true, out propEnum) == false)
                {
                    Logger.Warn($"ParseJsonPropertyIdSingle(): unknown PropertyEnum '{enumString}'");
                    return PropertyId.Invalid;
                }
            }
            else if (jsonElement.TryGetProperty("ParentDataRef", out JsonElement propRefElement))
            {
                PrototypeId propDataRef = ParsePrototypeRef(propRefElement);
                propEnum = GameDatabase.PropertyInfoTable.GetPropertyEnumFromPrototype(propDataRef);

                // The lookup table is keyed on Property/Info prototypes, but patch files in the wild write
                // the MIXIN instead - "Property/Mixin/DamageMultForPowerKeywordProp.defaults". Those resolve
                // to a real prototype, just not one in the table, so the enum came back Invalid and the
                // AssignProp silently targeted PropertyId.Invalid. Recover the enum from the name, which is
                // the same string with the path, the ".defaults" suffix and the trailing "Prop" removed.
                if (propEnum == PropertyEnum.Invalid && propRefElement.ValueKind == JsonValueKind.String)
                    propEnum = ParsePropertyEnumFromMixinName(propRefElement.GetString());

                if (propEnum == PropertyEnum.Invalid)
                {
                    Logger.Warn($"ParseJsonPropertyIdSingle(): ParentDataRef '{propRefElement}' is not a property prototype");
                    return PropertyId.Invalid;
                }
            }
            else
            {
                throw new InvalidOperationException(
                    "Json element for PropertyId must contain a PropertyEnum, a Property, or a ParentDataRef.");
            }

            PropertyInfo propInfo = GameDatabase.PropertyInfoTable.LookupPropertyInfo(propEnum);
            if (propInfo == null)
            {
                Logger.Warn($"ParseJsonPropertyIdSingle(): no PropertyInfo for '{propEnum}'");
                return PropertyId.Invalid;
            }

            int paramCount = propInfo.ParamCount;
            if (paramCount == 0) return new(propEnum);

            Span<PropertyParam> paramValues = stackalloc PropertyParam[Property.MaxParamCount];
            propInfo.DefaultParamValues.CopyTo(paramValues);

            // "Params": [ ... ] form
            if (jsonElement.TryGetProperty("Params", out JsonElement paramsElement) && paramsElement.ValueKind == JsonValueKind.Array)
            {
                JsonElement[] jsonArray = paramsElement.EnumerateArray().ToArray();
                for (int i = 0; i < paramCount && i < Property.MaxParamCount && i < jsonArray.Length; i++)
                    paramValues[i] = ParseJsonPropertyParam(jsonArray[i], propEnum, propInfo, i);
            }
            else
            {
                // "Param0"/"Param1"/... form
                for (int i = 0; i < paramCount && i < Property.MaxParamCount; i++)
                {
                    if (jsonElement.TryGetProperty($"Param{i}", out JsonElement paramValue))
                        paramValues[i] = ParseJsonPropertyParam(paramValue, propEnum, propInfo, i);
                }
            }

            return new(propEnum, paramValues);
        }

        public static PropertyValue ParseJsonPropertyValue(JsonElement jsonElement, PropertyInfo propInfo)
        {
            if (propInfo.ParamCount > 0)
            {
                var jsonArray = jsonElement.EnumerateArray().ToArray();
                jsonElement = jsonArray[^1];
            }

            switch (propInfo.DataType)
            {
                case PropertyDataType.Integer:
                    if (jsonElement.TryGetInt64(out long decimalValue))
                        return (PropertyValue)decimalValue;
                    break;

                case PropertyDataType.Real:
                    if (jsonElement.TryGetDouble(out double doubleValue))
                        return (PropertyValue)(float)doubleValue;
                    break;

                case PropertyDataType.Boolean:
                    return (PropertyValue)jsonElement.GetBoolean();

                case PropertyDataType.Prototype:
                    var protoRefValue = (PrototypeId)ParseJsonElement(jsonElement, typeof(PrototypeId));
                    return (PropertyValue)protoRefValue;

                case PropertyDataType.Asset:
                    AssetId assetValue = (AssetId)ParseJsonElement(jsonElement, typeof(AssetId));
                    return (PropertyValue)assetValue;

                default:
                    throw new InvalidOperationException($"[ParseJsonPropertyValue] Assignment into invalid property (property type is not int/float/bool)! Property: {propInfo.PropertyName}");
            }

            return propInfo.DefaultValue;
        }

        public static object ParseJsonElement(JsonElement value, Type fieldType)
        {
            // Names are resolved here as well as for numeric ids. Previously a string fell through to the
            // generic JsonValueKind.String case below and was returned as a raw string, which then blew up in
            // ConvertValue with "X is not a valid value for PrototypeId" - and that exception fails the whole
            // FILE, not just the entry. Any nested prototype-typed field can now be written by name, matching
            // what top-level values have accepted since 2026-08-12.
            if (fieldType == typeof(PrototypeId))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong ulongValue))
                    return (PrototypeId)ulongValue;

                if (value.ValueKind == JsonValueKind.String)
                    return ParsePrototypeRef(value);
            }

            if (fieldType == typeof(AssetId))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong ulongValue))
                    return (AssetId)ulongValue;

                if (value.ValueKind == JsonValueKind.String)
                    return ParseAssetRef(value);
            }

            if (fieldType == typeof(PrototypeGuid))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong ulongValue))
                    return (PrototypeGuid)ulongValue;
            }

            if (fieldType == typeof(LocaleStringId))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong ulongValue))
                    return (LocaleStringId)ulongValue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.Number:
                    if (value.TryGetUInt64(out ulong ulongValue))
                        return ulongValue;
                    else if (value.TryGetInt64(out long decimalValue))
                        return decimalValue;
                    else if (value.TryGetDouble(out double doubleValue))
                        return doubleValue;
                    else
                        return value.GetRawText();
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return value.GetBoolean();
                default:
                    return value.ToString();
            }
        }

        public override void Write(Utf8JsonWriter writer, PrototypePatchEntry value, JsonSerializerOptions options)
        {
            throw new NotImplementedException(); 
        }
    }

    public enum ValueType
    {
        // Simple types
        String,
        Boolean,
        Float,
        Double,
        Integer,
        Long,
        ULong,
        Enum,
        Asset,
        // Alias for Asset, kept only so patch files written against mtzimas92/MHServerEmu's enum - which
        // still has this member - parse here unchanged. Ours resolves either a name or a raw id, so it is a
        // strict superset of what that fork does with it. Prefer "Asset" in new files.
        AssetId,
        PrototypeGuid,
        PrototypeId,
        LocaleStringId,
        PrototypeDataRef,
        Vector3,
        Orientation,
        PropertyId,
        RawJson,

        // Complex types
        Prototype,
        ComplexObject,
        Eval,
        Properties,
        CurveProperties,

        // Array types
        PrototypeIdArray,
        PrototypeDataRefArray,
        PrototypeArray,
        StringArray,
        BooleanArray,
        FloatArray,
        DoubleArray,
        IntegerArray,
        LongArray,
        ULongArray,
        Vector3Array,
        RawJsonArray
    }

    public abstract class ValueBase
    {
        public abstract ValueType ValueType { get; }
        public abstract object GetValue();
    }

    /// <summary>
    /// One curve-property retarget: which property, which curve, and optionally which index property.
    /// A null <see cref="IndexPropertyId"/> means "keep whatever the property already uses".
    /// </summary>
    public sealed class CurvePropertySpec
    {
        public PropertyId PropertyId { get; }
        public CurveId CurveId { get; }
        public PropertyId? IndexPropertyId { get; }

        public CurvePropertySpec(PropertyId propertyId, CurveId curveId, PropertyId? indexPropertyId)
        {
            PropertyId = propertyId;
            CurveId = curveId;
            IndexPropertyId = indexPropertyId;
        }
    }

    public sealed class CurvePropertiesValue : ValueBase
    {
        public override ValueType ValueType => ValueType.CurveProperties;
        public List<CurvePropertySpec> Specs { get; }

        public CurvePropertiesValue(List<CurvePropertySpec> specs) { Specs = specs; }

        public override object GetValue() => Specs;
    }

    public class SimpleValue<T> : ValueBase
    {
        public override ValueType ValueType { get; }
        public T Value { get; }

        public SimpleValue(T value, ValueType valueType)
        {
            Value = value;
            ValueType = valueType;
        }

        public override object GetValue() => Value;
    }

    public class ArrayValue<T> : SimpleValue<T[]>
    {
        public ArrayValue(JsonElement jsonElement, ValueType valueType, Func<JsonElement, T> elementParser)
            : base(ParseJsonElement(jsonElement, elementParser), valueType) { }

        private static T[] ParseJsonElement(JsonElement jsonElement, Func<JsonElement, T> elementParser)
        {
            if (jsonElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Json element is not array");

            var jsonArray = jsonElement.EnumerateArray().ToArray();
            if (jsonArray.Length == 0) return [];

            var result = new T[jsonArray.Length];
            for (int i = 0; i < jsonArray.Length; i++)
                result[i] = elementParser(jsonArray[i]);

            return result;
        }
    }
}

