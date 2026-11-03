using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RailroaderModSettings
{
    public sealed class SettingField
    {
        private readonly MemberInfo[] _path;
        private readonly FieldInfo? _field;
        private readonly PropertyInfo? _property;

        internal SettingField(IReadOnlyList<MemberInfo> parentPath, MemberInfo member,
            ModSettingAttribute metadata, string section, IReadOnlyList<ModSettingsGroupInfo> groups)
        {
            Member = member;
            Metadata = metadata;
            Section = section;
            Groups = groups.ToArray();
            _path = parentPath.Concat(new[] { member }).ToArray();
            _field = member as FieldInfo;
            _property = member as PropertyInfo;
            ValueType = _field?.FieldType ?? _property?.PropertyType
                ?? throw new ArgumentException("Expected a field or property.", nameof(member));
            // Dotted paths let us find and reset nested fields individually.
            Name = string.Join(".", _path.Select(part => part.Name));
        }

        internal MemberInfo Member { get; }
        public ModSettingAttribute Metadata { get; }
        public string Name { get; }
        public string Label => Metadata.Label;
        public string Section { get; }
        public IReadOnlyList<ModSettingsGroupInfo> Groups { get; }
        public Type ValueType { get; }

        private object GetOwner(object root)
        {
            object current = root;
            for (int i = 0; i < _path.Length - 1; i++)
            {
                MemberInfo parent = _path[i];
                object? next = ReadMember(parent, current);
                if (next == null)
                {
                    // Create missing group objects when a nested setting is accessed.
                    Type type = MemberType(parent);
                    next = Activator.CreateInstance(type)
                        ?? throw new InvalidOperationException("Unable to initialize settings group: " + parent.Name);
                    WriteMember(parent, current, next);
                }
                current = next;
            }
            return current;
        }

        public object? GetValue(object settings)
        {
            return ReadMember(Member, GetOwner(settings));
        }

        internal void SetValue(object settings, object? value)
        {
            WriteMember(Member, GetOwner(settings), value);
        }

        internal static Type MemberType(MemberInfo member)
        {
            if (member is FieldInfo field)
            {
                return field.FieldType;
            }
            if (member is PropertyInfo property)
            {
                return property.PropertyType;
            }
            throw new ArgumentException("Expected a field or property.", nameof(member));
        }

        internal static object? ReadMember(MemberInfo member, object owner)
        {
            if (member is FieldInfo field)
            {
                return field.GetValue(owner);
            }
            if (member is PropertyInfo property)
            {
                return property.GetValue(owner, null);
            }
            throw new ArgumentException("Expected a field or property.", nameof(member));
        }

        internal static void WriteMember(MemberInfo member, object owner, object? value)
        {
            if (member is FieldInfo field)
            {
                field.SetValue(owner, value);
            }
            else if (member is PropertyInfo property)
            {
                property.SetValue(owner, value, null);
            }
            else
            {
                throw new ArgumentException("Expected a field or property.", nameof(member));
            }
        }
    }
}
