using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;
using System.Text.Json;

namespace MHServerEmu.Games.GameData.PatchManager
{
    public class JsonPrototype : ValueBase
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly PrototypeId _parentRef;
        private readonly Type _classType;
        private readonly ValueType _valueType;
        private readonly List<Field> _fields = new();

        private Prototype _instance;

        public override ValueType ValueType { get => _valueType; }

        public JsonPrototype(JsonElement jsonElement, ValueType valueType = ValueType.Prototype)
        {
            _valueType = valueType;

            // Two ways to say what to build:
            //   "ParentDataRef": <id>   - clone an existing prototype and override fields on top (original
            //                             behaviour; the new instance inherits everything that prototype had)
            //   "ClassName": "Foo"      - construct a bare instance of a prototype CLASS with no donor
            //
            // ClassName exists because ParentDataRef alone cannot express "I want a new FooPrototype" unless
            // some prototype of exactly that class already happens to exist in the data to clone from. That
            // blocked things like adding a new AllowedItemListInputPrototype recipe slot. Ported in concept
            // from Doodswh/MHServerEmu (COA_Build), but built on this fork's LAZY construction rather than
            // their eager version - see GetValue() for why that distinction matters.
            if (jsonElement.TryGetProperty("ParentDataRef", out JsonElement parentRefElement))
            {
                // Accepts a NAME as well as a raw id. Top-level patch values have taken names since
                // 2026-08-12 (see PatchEntryConverter.ParsePrototypeRef), but this nested case was missed,
                // so "ParentDataRef": "Eval/ControlFlow/IfElse.defaults" threw InvalidOperationException on
                // GetUInt64() - and because one throw fails the whole FILE, a single named ParentDataRef
                // took every other entry down with it. Found by parsing mtzimas92/TAHITI's patch set, which
                // writes them as names throughout: 9 of their 76 files died on exactly this.
                _parentRef = PatchEntryConverter.ParsePrototypeRefPublic(parentRefElement);
                _classType = GameDatabase.DataDirectory.GetPrototypeClassType(_parentRef);
            }
            else if (jsonElement.TryGetProperty("ProtoNameHash", out JsonElement protoHashElem))
            {
                string className = protoHashElem.GetString();
                uint hash = HashHelper.Djb2(className);
                _parentRef = PrototypeId.Invalid;
                _classType = GameDatabase.PrototypeClassManager.GetPrototypeClassTypeByNameHash(hash);
            }
            else if (jsonElement.TryGetProperty("ClassName", out JsonElement classNameElement))
            {
                _parentRef = PrototypeId.Invalid;
                _classType = GameDatabase.PrototypeClassManager.GetPrototypeClassTypeByName(classNameElement.GetString());

                if (_classType == null)
                    Logger.Warn($"JsonPrototype(): unknown ClassName '{classNameElement.GetString()}'");
            }
            else
            {
                Logger.Warn("JsonPrototype(): value object needs either a ParentDataRef, ProtoNameHash or a ClassName");
                return;
            }

            if (!Verify.IsNotNull(_classType)) return;

            foreach (JsonProperty jsonProperty in jsonElement.EnumerateObject())
            {
                string fieldName = jsonProperty.Name;

                // "ParentDataRef"/"ClassName" are consumed above. "Description" isn't a real field on any
                // prototype class - patch authors use it as an inline human-readable comment on individual
                // array elements (see e.g. PatchDataMod_Vendors_OftH_VendorItems.json), the same way the outer
                // PrototypePatchEntry has its own top-level Description. Skip these silently rather than
                // failing the GetProperty() lookup below and logging a Verify warning for something that's
                // working as intended.
                if (fieldName == "ParentDataRef" || fieldName == "ProtoNameHash" || fieldName == "ClassName" || fieldName == "Description")
                    continue;

                System.Reflection.PropertyInfo fieldInfo = _classType.GetProperty(fieldName);
                if (!Verify.IsNotNull(fieldInfo))
                    continue;

                Type fieldType = fieldInfo.PropertyType;

                // Array-typed fields need their own handling: ParseJsonElement() has no
                // JsonValueKind.Array case, so it falls through to value.ToString() and the array
                // arrives as a raw JSON string, which then fails the cast in ConvertValue(). That
                // failure is swallowed as a "Can't convert" warning and the field silently keeps
                // whatever ParentDataRef had - which is why a loot node's own nested Choices[]/
                // Modifiers[] stopped applying after upstream merge 34df4088b dropped mtzimas92's
                // ParseJsonArray() (b27e81dbe).
                // A nested single prototype OBJECT needs the same treatment as a nested prototype array, and
                // for the same reason. Without this case it falls through to ParseJsonElement(), which has no
                // JsonValueKind.Object handling, so the object arrives as a raw JSON string and then fails the
                // cast in ConvertValue() - leaving the field at whatever ParentDataRef had. That is how a
                // mission SuccessConditions patch produced a condition with Count and ParentDataRef set but no
                // EntityFilter at all, with --validatepatches reporting it clean (Civil War Phase 2, 2026-08-15).
                // Deferred like the array case: constructing a prototype needs GameDatabase initialized, and
                // this runs during LoadPatchDataFromDisk() before Globals are loaded.
                object fieldValue;
                // A PropertyId field written as an object - {"Property": "DamageMultOnPower"} or
                // {"PropertyEnum": ..., "Params": [...]}. Without this it fell through to ParseJsonElement,
                // came back as the object's ToString(), failed the cast in ConvertValue() and left the field
                // at its default - a SILENT no-op, which is worse than the parse error it hides behind.
                // A bare string is accepted too - {"Prop": "CritChancePctAdd"} - because that is the form
                // mtzimas92/MHServerEmu's patches use. It previously fell through to ParseJsonElement, came
                // back as a raw string, and ConvertValue turned it into a DEFAULT PropertyId. That is the
                // worst possible outcome: the patch reported APPLIED and silently wrote
                // AchievementScoreProp instead of the property named in the file.
                if (fieldType == typeof(PropertyId)
                    && jsonProperty.Value.ValueKind is JsonValueKind.Object or JsonValueKind.String)
                    fieldValue = PatchEntryConverter.ParseJsonPropertyIdSinglePublic(jsonProperty.Value);
                else if (fieldType.IsArray && jsonProperty.Value.ValueKind == JsonValueKind.Array)
                    fieldValue = ParseArrayField(jsonProperty.Value, fieldType);
                else if (typeof(Prototype).IsAssignableFrom(fieldType) && jsonProperty.Value.ValueKind == JsonValueKind.Object)
                    fieldValue = new JsonPrototype(jsonProperty.Value);
                else
                    fieldValue = PatchEntryConverter.ParseJsonElement(jsonProperty.Value, fieldType);

                Field field = new(fieldName, fieldValue, fieldType);
                _fields.Add(field);
            }
        }

        /// <summary>
        /// Builds the value for an array-typed field. Prototype-element arrays are deferred: we keep a
        /// <see cref="JsonPrototypeArray"/> and materialize it in <see cref="GetValue()"/>, because
        /// actually constructing a prototype (AllocatePrototype/CopyPrototypeDataRefFields) requires
        /// GameDatabase to be initialized, and this runs during LoadPatchDataFromDisk() - which on this
        /// fork happens BEFORE Globals are loaded (see GameDatabase's static ctor). Doing it eagerly here
        /// throws NullReferenceException on lootGlobalsProto/blueprint and fails the whole patch file.
        /// Element types that are plain values (PrototypeId, AssetId, ints, ...) touch no game data, so
        /// they are safe to build immediately.
        /// </summary>
        private static object ParseArrayField(JsonElement jsonElement, Type fieldType)
        {
            Type elementType = fieldType.GetElementType();
            if (elementType == null)
                return PatchEntryConverter.ParseJsonElement(jsonElement, fieldType);

            if (typeof(Prototype).IsAssignableFrom(elementType))
                return new JsonPrototypeArray(jsonElement);

            JsonElement[] jsonArray = jsonElement.EnumerateArray().ToArray();
            Array result = Array.CreateInstance(elementType, jsonArray.Length);

            for (int i = 0; i < jsonArray.Length; i++)
            {
                object element = PatchEntryConverter.ParseJsonElement(jsonArray[i], elementType);
                result.SetValue(PrototypePatchManager.ConvertValue(element, elementType), i);
            }

            return result;
        }

        public override object GetValue()
        {
            if (!Verify.IsNotNull(_classType)) return null;

            if (_instance == null)
            {
                Prototype instance = GameDatabase.PrototypeClassManager.AllocatePrototype(_classType);
                if (!Verify.IsNotNull(instance)) return null;

                // Only a ParentDataRef-based value has a donor to inherit from. A ClassName-based one is a
                // bare instance whose fields start at their defaults, and the JSON supplies whatever it needs.
                if (_parentRef != PrototypeId.Invalid)
                {
                    CalligraphySerializer.CopyPrototypeDataRefFields(instance, _parentRef);
                    instance.ParentDataRef = _parentRef;
                }

                foreach (Field field in _fields)
                {
                    System.Reflection.PropertyInfo fieldInfo = _classType.GetProperty(field.Name);
                    if (!Verify.IsNotNull(fieldInfo))
                        continue;

                    try
                    {
                        // Materialize a deferred prototype-element array now that GameDatabase is ready.
                        // JsonPrototypeArray.GetValue() returns Prototype[]; ConvertValue() narrows it to
                        // the field's concrete element type (e.g. LootNodePrototype[]). Nested levels
                        // recurse naturally: each element is itself a JsonPrototype whose own array fields
                        // were deferred the same way.
                        object rawValue = field.Value switch
                        {
                            JsonPrototypeArray deferredArray => deferredArray.GetValue(),
                            JsonPrototype deferredPrototype => deferredPrototype.GetValue(),
                            _ => field.Value
                        };

                        object convertedValue = PrototypePatchManager.ConvertValue(rawValue, field.Type);
                        fieldInfo.SetValue(instance, convertedValue);
                    }
                    catch (Exception e)
                    {
                        // An array-typed field reaching here means ParseArrayField() did not handle it and
                        // the value arrived as a raw JSON string. That is always a regression, never normal
                        // patch data - it silently leaves the field at ParentDataRef's value, which is how
                        // every nested Choices[]/Modifiers[] in every patch file stopped applying for a day
                        // without a single error (upstream merge 34df4088b, 2026-07-30). Log it loudly so it
                        // is caught in minutes rather than by noticing loot has quietly gone missing.
                        if (field.Type.IsArray)
                            Logger.Error($"Can't convert ARRAY field {field.Name} in {_classType.Name} - {e.Message}. " +
                                         $"Nested array support has regressed - see JsonPrototype.ParseArrayField().");
                        else if (typeof(Prototype).IsAssignableFrom(field.Type))
                            Logger.Error($"Can't convert NESTED PROTOTYPE field {field.Name} in {_classType.Name} - {e.Message}. " +
                                         $"Nested object support has regressed - see the JsonValueKind.Object case in the constructor.");
                        else
                            Logger.Warn($"Can't convert {field.Name} in {_classType.Name} - {e.Message}");
                    }
                }

                _instance = instance;
            }

            return _instance;
        }

        private readonly struct Field(string name, object value, Type type)
        {
            public readonly string Name = name;
            public readonly object Value = value;
            public readonly Type Type = type;
        }
    }

    public class JsonPrototypeArray : ValueBase
    {
        private readonly JsonPrototype[] _jsonPrototypes;
        private Prototype[] _instances;

        public override ValueType ValueType { get => ValueType.PrototypeArray; }

        public JsonPrototypeArray(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Json element is not array");

            JsonElement[] jsonArray = jsonElement.EnumerateArray().ToArray();
            if (jsonArray.Length == 0)
            {
                _jsonPrototypes = [];
                _instances = [];
                return;
            }

            _jsonPrototypes = new JsonPrototype[jsonArray.Length];
            for (int i = 0; i < jsonArray.Length; i++)
                _jsonPrototypes[i] = new(jsonArray[i]);
        }

        public override object GetValue()
        {
            if (_instances == null)
            {
                Prototype[] instances = new Prototype[_jsonPrototypes.Length];
                for (int i = 0; i < _jsonPrototypes.Length; i++)
                    instances[i] = (Prototype)_jsonPrototypes[i].GetValue();
                _instances = instances;
            }

            return _instances;
        }

        public object GetTypedPrototypes(Type protoType)
        {
            var instances = (Prototype[])GetValue();
            if (instances == null) return null;
            var newArray = Array.CreateInstance(protoType, instances.Length);
            for (int i = 0; i < instances.Length; i++)
                newArray.SetValue(instances[i], i);

            return newArray;
        }
    }
}
