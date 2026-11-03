using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;

namespace RailroaderModSettings
{
    /// <summary>Live settings and JSON storage for one mod.</summary>
    public sealed class SettingsRegistration
    {
        private readonly Action<object>? _onChanged;
        private readonly Action<string>? _onError;
        private readonly Type _settingsType;
        private readonly string _settingsFile;

        internal SettingsRegistration(
            string modIdentifier,
            string displayName,
            Type settingsType,
            Action<object>? onChanged,
            Action<string>? onError,
            string settingsRoot)
        {
            ModIdentifier = modIdentifier;
            DisplayName = displayName;
            _settingsType = settingsType;
            _onChanged = onChanged;
            _onError = onError;
            // Split the mod ID into an author folder and a JSON filename.
            int slash = modIdentifier.IndexOf('/');
            if (slash <= 0 || slash != modIdentifier.LastIndexOf('/') || slash == modIdentifier.Length - 1)
            {
                throw new ArgumentException("Mod identifier must use author/mod-name format.", nameof(modIdentifier));
            }

            string author = MakeSafeFileName(modIdentifier.Substring(0, slash));
            string modName = MakeSafeFileName(modIdentifier.Substring(slash + 1));
            _settingsFile = Path.Combine(settingsRoot, author, modName + ".json");

            Fields = DiscoverSettings(settingsType);
            Values = Activator.CreateInstance(settingsType)
                ?? throw new InvalidOperationException("Unable to instantiate settings type: " + settingsType.FullName);
            TryLoad();
        }

        public string ModIdentifier { get; }
        public string DisplayName { get; }
        public object Values { get; }
        public string FilePath => _settingsFile;
        public string? LoadError { get; private set; }
        public IReadOnlyList<SettingField> Fields { get; }

        private static string MakeSafeFileName(string modIdentifier)
        {
            var result = new System.Text.StringBuilder(modIdentifier.Length);
            char[] invalidChars = Path.GetInvalidFileNameChars();

            foreach (char c in modIdentifier)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (c == '/' || c == '\\')
                {
                    result.Append('_');
                }
                else if (char.IsControl(c) || "<>:\"|?*".IndexOf(c) >= 0 || Array.IndexOf(invalidChars, c) >= 0)
                {
                    result.Append('_');
                }
                else
                {
                    result.Append(c);
                }
            }

            string fileName = result.ToString().Trim('.');
            if (fileName.Length == 0)
            {
                throw new ArgumentException("Mod identifier must contain filename-safe characters.", nameof(modIdentifier));
            }

            return fileName;
        }

        public T GetValues<T>() where T : class
        {
            return (T)Values;
        }

        public bool TrySet(string fieldName, object? rawValue, out string? error)
        {
            SettingField? field = Fields.FirstOrDefault(f => f.Name == fieldName);
            if (field == null)
            {
                error = "Unknown setting: " + fieldName;
                return false;
            }

            if (!TryConvert(field, rawValue, out object? converted, out error))
            {
                return false;
            }

            // Keep the old value around in case writing the JSON fails.
            object? previous = Snapshot(field.GetValue(Values));
            if (Equals(previous, converted))
            {
                return true;
            }

            try
            {
                UpdateValue(field, converted);
                Save();
            }
            catch (Exception ex)
            {
                try { UpdateValue(field, previous); } catch { }
                error = "Could not save " + field.Label + ": " + ex.Message;
                _onError?.Invoke(error);
                return false;
            }

            NotifyChange();
            return true;
        }

        /// <summary>Resets one setting without changing the others.</summary>
        public bool TryResetFieldToDefault(string fieldName, out string? error)
        {
            SettingField? field = Fields.FirstOrDefault(f => f.Name == fieldName);
            if (field == null)
            {
                error = "Unknown setting: " + fieldName;
                return false;
            }

            try
            {
                object defaults = Activator.CreateInstance(_settingsType)
                    ?? throw new InvalidOperationException("Unable to instantiate settings type: " + _settingsType.FullName);
                return TrySet(fieldName, field.GetValue(defaults), out error);
            }
            catch (Exception ex)
            {
                error = "Unable to reset " + field.Label + ": " + ex.Message;
                _onError?.Invoke(error);
                return false;
            }
        }

        public void ResetToDefaults()
        {
            object defaults = Activator.CreateInstance(_settingsType)
                ?? throw new InvalidOperationException("Unable to instantiate settings type: " + _settingsType.FullName);
            // Reset in place, but roll everything back if saving fails.
            Dictionary<SettingField, object?> oldValues = Fields.ToDictionary(f => f, f => Snapshot(f.GetValue(Values)));

            try
            {
                foreach (SettingField field in Fields)
                {
                    UpdateValue(field, field.GetValue(defaults));
                }
                Save();
            }
            catch
            {
                foreach (KeyValuePair<SettingField, object?> previous in oldValues)
                {
                    UpdateValue(previous.Key, previous.Value);
                }
                throw;
            }

            NotifyChange();
        }

        // Mods may hold references to keybindings, so don't replace those objects.
        private static object? Snapshot(object? value)
        {
            return value is ModKeyBinding binding ? binding.Clone() : value;
        }

        private void UpdateValue(SettingField field, object? value)
        {
            if (field.GetValue(Values) is ModKeyBinding current && value is ModKeyBinding next)
            {
                current.Change(next.keyCode, next.modifiers);
            }
            else
            {
                field.SetValue(Values, value);
            }
        }

        public void Save()
        {
            string directory = Path.GetDirectoryName(_settingsFile)
                ?? throw new InvalidOperationException("Settings file path has no parent directory: " + _settingsFile);
            Directory.CreateDirectory(directory);

            // Write a temp file first so a failed save doesn't truncate the old JSON.
            string temporaryFile = _settingsFile + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = new DataContractJsonSerializer(_settingsType);
                    serializer.WriteObject(stream, Values);
                    stream.Flush(true);
                }

                if (File.Exists(_settingsFile))
                {
                    File.Replace(temporaryFile, _settingsFile, null);
                }
                else
                {
                    File.Move(temporaryFile, _settingsFile);
                }
            }
            finally
            {
                if (File.Exists(temporaryFile))
                {
                    File.Delete(temporaryFile);
                }
            }
        }

        private void TryLoad()
        {
            if (!File.Exists(_settingsFile))
            {
                return;
            }

            try
            {
                using (var stream = File.OpenRead(_settingsFile))
                {
                    var serializer = new DataContractJsonSerializer(_settingsType);
                    object? loaded = serializer.ReadObject(stream);
                    if (loaded == null)
                    {
                        throw new InvalidDataException("Settings file contained a null value.");
                    }

                    // Apply to the existing instance; mods may already hold a reference.
                    var loadedValues = Fields.ToDictionary(field => field, field => field.GetValue(loaded));
                    var previousValues = Fields.ToDictionary(field => field, field => Snapshot(field.GetValue(Values)));
                    try
                    {
                        foreach (KeyValuePair<SettingField, object?> pair in loadedValues)
                        {
                            UpdateValue(pair.Key, pair.Value);
                        }
                    }
                    catch
                    {
                        foreach (KeyValuePair<SettingField, object?> pair in previousValues)
                        {
                            UpdateValue(pair.Key, pair.Value);
                        }
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                string message = "Could not read " + _settingsFile + ": " + ex.Message;
                LoadError = message;
                _onError?.Invoke(message);
                // Don't overwrite a bad file until someone actually edits a setting.
            }
        }

        private void NotifyChange()
        {
            try
            {
                _onChanged?.Invoke(Values);
            }
            catch (Exception ex)
            {
                _onError?.Invoke("Settings change callback failed for " + ModIdentifier + ": " + ex);
            }
        }

        private static IReadOnlyList<SettingField> DiscoverSettings(Type type)
        {
            var result = new List<SettingField>();
            DiscoverSettingsRecursive(type, new List<MemberInfo>(),
                new List<ModSettingsGroupInfo>(), "General", new HashSet<Type>(), result);
            return result;
        }

        private static void DiscoverSettingsRecursive(
            Type type,
            List<MemberInfo> path,
            List<ModSettingsGroupInfo> groups,
            string section,
            HashSet<Type> visited,
            List<SettingField> result)
        {
            // Catch groups that refer back to an ancestor type (A -> B -> A).
            if (!visited.Add(type))
            {
                throw new InvalidOperationException("Recursive settings group type: " + type.FullName);
            }

            try
            {
                // When Order ties, keep the fields in declaration order.
                var members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public)
                    .Where(member => member.GetCustomAttribute<ModSettingAttribute>() != null ||
                                     member.GetCustomAttribute<ModSettingsGroupAttribute>() != null)
                    .OrderBy(member => member.GetCustomAttribute<ModSettingAttribute>()?.Order ??
                                       member.GetCustomAttribute<ModSettingsGroupAttribute>()?.Order ?? 0)
                    .ThenBy(member => member.MetadataToken);

                foreach (MemberInfo member in members)
                {
                    ModSettingAttribute? setting = member.GetCustomAttribute<ModSettingAttribute>();
                    ModSettingsGroupAttribute? group = member.GetCustomAttribute<ModSettingsGroupAttribute>();
                    if (setting != null && group != null)
                    {
                        throw new InvalidOperationException("A member cannot be a setting and group: " + member.Name);
                    }

                    if (member is FieldInfo fieldInfo && fieldInfo.IsInitOnly)
                    {
                        throw new InvalidOperationException("Setting must be writable: " + member.Name);
                    }
                    if (!(member is FieldInfo) && !(member is PropertyInfo propertyInfo &&
                          propertyInfo.GetIndexParameters().Length == 0 &&
                          propertyInfo.GetGetMethod() != null && propertyInfo.GetSetMethod() != null))
                    {
                        throw new InvalidOperationException("Setting must be a writable public field or property: " + member.Name);
                    }

                    Type valueType = SettingField.MemberType(member);
                    if (group != null)
                    {
                        if (!valueType.IsClass || valueType.IsAbstract ||
                            valueType.GetConstructor(Type.EmptyTypes) == null)
                        {
                            throw new NotSupportedException("Settings group requires a concrete class with a public default constructor: " + member.Name);
                        }
                        string groupSection = string.IsNullOrWhiteSpace(group.Section) ? section : group.Section;
                        string groupPath = string.Join(".", path.Select(part => part.Name)
                            .Concat(new[] { member.Name }));
                        var nextPath = new List<MemberInfo>(path) { member };
                        var nextGroups = new List<ModSettingsGroupInfo>(groups)
                        {
                            new ModSettingsGroupInfo(groupPath, group, groupSection)
                        };
                        DiscoverSettingsRecursive(valueType, nextPath, nextGroups,
                            groupSection, visited, result);
                        continue;
                    }

                    if (setting == null)
                    {
                        continue;
                    }
                    if (!(valueType == typeof(bool) || valueType == typeof(int) ||
                          valueType == typeof(float) || valueType == typeof(double) ||
                          valueType == typeof(string) || valueType == typeof(ModKeyBinding) || valueType.IsEnum))
                    {
                        throw new NotSupportedException("Unsupported setting type for " + member.Name + ": " + valueType);
                    }
                    if (setting is ModKeybindAttribute && valueType != typeof(ModKeyBinding))
                    {
                        throw new InvalidOperationException("[ModKeybind] requires ModKeyBinding: " + member.Name);
                    }
                    if (setting.Precision < -1 || setting.Precision > 8)
                    {
                        throw new ArgumentOutOfRangeException(member.Name, "Precision must be between 0 and 8, or -1 for automatic formatting.");
                    }

                    string settingSection = string.IsNullOrWhiteSpace(setting.Section) ? section : setting.Section;
                    result.Add(new SettingField(path, member, setting, settingSection, groups));
                }
            }
            finally
            {
                visited.Remove(type);
            }
        }

        private static bool TryConvert(SettingField field, object? raw, out object? value, out string? error)
        {
            value = null;
            error = null;
            Type type = field.ValueType;

            if (raw == null && type != typeof(string))
            {
                error = field.Label + " cannot be null.";
                return false;
            }

            try
            {
                if (type == typeof(ModKeyBinding))
                {
                    if (!(raw is ModKeyBinding binding))
                    {
                        throw new FormatException("Expected a ModKeyBinding value.");
                    }
                    var copy = new ModKeyBinding();
                    copy.Change(binding.keyCode, binding.modifiers);
                    value = copy;
                }
                else if (type.IsEnum)
                {
                    object enumValue = raw is string s ? Enum.Parse(type, s, true) : Enum.ToObject(type, raw!);
                    // Reject undefined enum values even if the underlying number is valid.
                    if (!Enum.IsDefined(type, enumValue))
                    {
                        throw new ArgumentOutOfRangeException(nameof(raw), "Undefined choice.");
                    }
                    value = enumValue;
                }
                else if (type == typeof(bool))
                {
                    value = raw is bool b ? b : bool.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "");
                }
                else if (type == typeof(string))
                {
                    value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
                }
                else
                {
                    double number = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                    if (double.IsNaN(number) || double.IsInfinity(number) ||
                        (!double.IsNaN(field.Metadata.Min) && number < field.Metadata.Min) ||
                        (!double.IsNaN(field.Metadata.Max) && number > field.Metadata.Max))
                    {
                        throw new ArgumentOutOfRangeException(nameof(raw), "Value is outside the permitted range.");
                    }

                    if (type == typeof(int))
                    {
                        if (number != Math.Truncate(number))
                        {
                            throw new FormatException("Expected a whole number.");
                        }
                        value = checked((int)number);
                    }
                    else if (type == typeof(float))
                    {
                        value = checked((float)number);
                    }
                    else
                    {
                        value = number;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Invalid value for " + field.Label + ": " + ex.Message;
                return false;
            }
        }
    }
}
