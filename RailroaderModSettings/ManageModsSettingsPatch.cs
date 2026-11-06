using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Game.State;
using UI.Builder;
using UI.Common;
using UI.ModSelection;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderModSettings
{
    /// <summary>Adds registered settings to Manage Mods.</summary>
    [HarmonyPatch]
    internal static partial class ManageModsSettingsPatch
    {
        private static string? _expandedModIdentifier;
        private static readonly HashSet<string> ExpandedGroups =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Top-level sections and nested groups need separate expansion states.
        private static readonly HashSet<string> ExpandedSections =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // UIPanelBuilder doesn't expose its container. Needed to find the scroll list.
        private static readonly FieldInfo? BuilderContainerField =
            AccessTools.Field(typeof(UIPanelBuilder), "_container");

        internal static void ResetViewState()
        {
            _expandedModIdentifier = null;
            ExpandedGroups.Clear();
            ExpandedSections.Clear();
            ModKeybindCapture.Shutdown();
        }

        // Fail early if RenderRow's arguments change in a game update.
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ModSelectionContentBuilder),
                "RenderRow",
                new[]
                {
                    typeof(UIPanelBuilder),
                    typeof(ModSelectionController),
                    typeof(Action),
                    typeof(ModRow)
                }) ?? throw new MissingMethodException(
                    "Railroader's ModSelectionContentBuilder.RenderRow signature has changed.");
        }

        [HarmonyPostfix]
        private static void AfterRenderRow(UIPanelBuilder builder, Action onRebuild, ModRow row)
        {
            SettingsRegistration? registration = SettingsRegistry.Get(row.Identifier);
            if (registration == null || registration.Fields.Count == 0)
            {
                return;
            }

            string identifier = row.Identifier;
            bool expanded = string.Equals(_expandedModIdentifier, identifier, StringComparison.OrdinalIgnoreCase);
            Action rebuild = () => RebuildPreservingScroll(builder, onRebuild);

            AddClickableHeader(builder, "Settings", expanded, true, () =>
            {
                ModKeybindCapture.Cancel(rebuild: false);
                _expandedModIdentifier = expanded ? null : identifier;
                rebuild();
            });

            if (expanded)
            {
                RenderSettings(builder, registration, rebuild);
            }
        }

        // The game destroys the list on rebuild. Keep its scroll offset around.
        private static void RebuildPreservingScroll(UIPanelBuilder builder, Action onRebuild)
        {
            ManageModsMenu? menu = UnityEngine.Object.FindFirstObjectByType<ManageModsMenu>();
            if (menu == null)
            {
                onRebuild();
                return;
            }

            var container = BuilderContainerField?.GetValue(builder) as RectTransform;
            ScrollRect? scroll = null;
            // Keybind callbacks can outlive their original builder container.
            if (container != null)
            {
                scroll = container.GetComponentInParent<ScrollRect>();
            }
            if (scroll == null || !scroll.vertical)
            {
                scroll = menu.GetComponentsInChildren<ScrollRect>(true)
                    .FirstOrDefault(s => s.vertical && s.isActiveAndEnabled);
            }
            if (scroll == null || scroll.content == null)
            {
                onRebuild();
                return;
            }

            ModSettingsScrollRestorer restorer = menu.GetComponent<ModSettingsScrollRestorer>();
            if (restorer == null)
            {
                restorer = menu.gameObject.AddComponent<ModSettingsScrollRestorer>();
            }
            restorer.Capture(scroll);
            onRebuild();
            restorer.RestoreAfterRebuild();
        }

        private static void RenderSettings(
            UIPanelBuilder builder,
            SettingsRegistration registration,
            Action onRebuild,
            bool inGame = false)
        {
            // Host-only settings stay available in Manage Mods, but aren't shown to clients in-game.
            IEnumerable<SettingField> visibleFields = inGame && !StateManager.IsHost
                ? registration.Fields.Where(field => !field.Metadata.HostOnly)
                : registration.Fields;

            RectTransform panel = builder.VStack(settingsBuilder =>
            {
                settingsBuilder.ConfigureLayout(8f, 16);
                settingsBuilder.HStack(hBuilder =>
                {
                    hBuilder.ConfigureLayout(8f);
                    hBuilder.AddLabel("<size=82%><color=#AAA69E>Changes are saved immediately.</color></size>")
                        .FlexibleWidth();
                    hBuilder.AddButtonCompact("Reset to Defaults", () =>
                    {
                        try
                        {
                            ModKeybindCapture.Cancel(rebuild: false);
                            // Don't reset settings that a non-host can't access from this menu.
                            if (inGame && !StateManager.IsHost)
                            {
                                registration.ResetToDefaults(field => !field.Metadata.HostOnly);
                            }
                            else
                            {
                                registration.ResetToDefaults();
                            }
                            onRebuild();
                        }
                        catch (Exception ex)
                        {
                            ShowError("Could not reset settings: " + ex.Message);
                        }
                    }).Width(150f);
                });

                if (!string.IsNullOrEmpty(registration.LoadError))
                {
                    settingsBuilder.AddLabel("<color=#FFB454>Saved settings could not be loaded. Editing will overwrite the saved file.</color>");
                }

                // One background for both the section header and its expanded fields.
                foreach (var sectionFields in visibleFields.GroupBy(
                    field => field.Section, StringComparer.Ordinal))
                {
                    string section = sectionFields.Key;
                    bool sectionExpanded = ExpandedSections.Contains(
                        SectionKey(registration, section));

                    settingsBuilder.Spacer(2f);
                    RectTransform sectionPanel = settingsBuilder.VStack(sectionBuilder =>
                    {
                        sectionBuilder.ConfigureLayout(0f);
                        RenderSectionHeader(sectionBuilder, registration, section, onRebuild);
                        if (sectionExpanded)
                        {
                            sectionBuilder.VStack(contentBuilder =>
                            {
                                contentBuilder.ConfigureLayout(7f, 12);
                                RenderSectionFields(contentBuilder, registration,
                                    sectionFields, onRebuild);
                            });
                        }
                    });

                    Image sectionBackground = sectionPanel.GetComponent<Image>()
                        ?? sectionPanel.gameObject.AddComponent<Image>();
                    sectionBackground.color = new Color32(58, 55, 51, 205);
                    sectionBackground.raycastTarget = false;
                }

            });
            // The panel background shouldn't eat clicks meant for the controls.
            Image background = panel.gameObject.GetComponent<Image>()
                ?? panel.gameObject.AddComponent<Image>();
            background.color = new Color32(43, 41, 37, 230);
            background.raycastTarget = false;
        }

        private static string SectionKey(SettingsRegistration registration, string section)
        {
            return registration.ModIdentifier + ":section:" + section;
        }

        private static void RenderSectionFields(
            UIPanelBuilder builder,
            SettingsRegistration registration,
            IEnumerable<SettingField> fields,
            Action onRebuild)
        {
            // Only draw a group header when we first enter that group.
            var previousGroups = new List<string>();
            foreach (SettingField field in fields)
            {
                int commonGroups = 0;
                while (commonGroups < previousGroups.Count &&
                       commonGroups < field.Groups.Count &&
                       string.Equals(previousGroups[commonGroups],
                           field.Groups[commonGroups].Path, StringComparison.Ordinal))
                {
                    commonGroups++;
                }
                for (int depth = commonGroups; depth < field.Groups.Count; depth++)
                {
                    if (field.Groups.Take(depth).Any(group => group.Collapsible &&
                            !ExpandedGroups.Contains(GroupKey(registration, group, field.Section))))
                    {
                        break;
                    }
                    RenderGroupHeader(builder, registration, field.Groups[depth],
                        field.Section, onRebuild);
                }
                previousGroups = field.Groups.Select(group => group.Path).ToList();

                // Don't render a field under a collapsed parent group.
                if (field.Groups.All(group => !group.Collapsible ||
                        ExpandedGroups.Contains(GroupKey(registration, group, field.Section))))
                {
                    RenderField(builder, registration, field, onRebuild);
                }
            }
        }

        private static void RenderSectionHeader(
            UIPanelBuilder builder,
            SettingsRegistration registration,
            string section,
            Action onRebuild)
        {
            string key = SectionKey(registration, section);
            bool expanded = ExpandedSections.Contains(key);
            AddClickableHeader(builder, section, expanded, false, () =>
            {
                ModKeybindCapture.Cancel(rebuild: false);
                if (!ExpandedSections.Add(key))
                {
                    ExpandedSections.Remove(key);
                }
                onRebuild();
            });
        }

        private static void AddClickableHeader(
            UIPanelBuilder builder,
            string title,
            bool expanded,
            bool primary,
            Action onClick)
        {
            string size = primary ? "101%" : "91%";
            string color = primary ? "#D0CDC6" : "#BDB9B2";
            string label = (expanded ? "▼ " : "▶ ") +
                EscapeMarkup(title.ToUpperInvariant());
            RectTransform header = builder.HStack(hBuilder =>
            {
                hBuilder.ConfigureLayout(0f);
                hBuilder.Spacer(primary ? 12f : 10f);
                hBuilder.AddLabel("<size=" + size + "><color=" + color +
                    "><b>" + label + "</b></color></size>")
                    .Height(primary ? 22f : 20f).FlexibleWidth();
            });
            header.Height(primary ? 30f : 34f);
            HorizontalLayoutGroup? layout = header.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.childControlHeight = false;
            }

            // Transparent click target so the section background stays visible.
            Image clickTarget = header.GetComponent<Image>()
                ?? header.gameObject.AddComponent<Image>();
            clickTarget.color = Color.clear;
            clickTarget.raycastTarget = true;
            Button button = header.GetComponent<Button>()
                ?? header.gameObject.AddComponent<Button>();
            button.targetGraphic = clickTarget;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick());
        }

        private static string GroupKey(SettingsRegistration registration, ModSettingsGroupInfo group, string section)
        {
            return registration.ModIdentifier + ":" + section + ":" + group.Path;
        }

        private static void RenderGroupHeader(
            UIPanelBuilder builder,
            SettingsRegistration registration,
            ModSettingsGroupInfo group,
            string section,
            Action onRebuild)
        {
            string key = GroupKey(registration, group, section);
            bool expanded = !group.Collapsible || ExpandedGroups.Contains(key);
            if (group.Collapsible)
            {
                // Give the nested group a little room above its button.
                builder.Spacer(5f);
                builder.AddButtonCompact((expanded ? "- " : "+ ") + EscapeMarkup(group.Label), () =>
                {
                    ModKeybindCapture.Cancel(rebuild: false);
                    if (!ExpandedGroups.Add(key))
                    {
                        ExpandedGroups.Remove(key);
                    }
                    onRebuild?.Invoke();
                });
            }
            else
            {
                builder.AddLabel("<b>" + EscapeMarkup(group.Label) + "</b>");
            }

            if (expanded && !string.IsNullOrWhiteSpace(group.Warning))
            {
                builder.AddLabel("<color=#FF8888>" + EscapeMarkup(group.Warning) + "</color>");
            }
        }

        // Let labels take the remaining width; the controls stay on the right.
        private static RectTransform AddSettingRow(
            UIPanelBuilder builder,
            SettingField field,
            Action<UIPanelBuilder> content)
        {
            RectTransform row = builder.HStack(hBuilder =>
            {
                hBuilder.ConfigureLayout(8f);
                hBuilder.Spacer(12f); // inset rows beneath their section header
                hBuilder.AddLabel(EscapeMarkup(field.Label)).FlexibleWidth();
                content(hBuilder);
            });
            AddTooltip(row, field);
            return row;
        }

        private static void RenderField(
            UIPanelBuilder builder,
            SettingsRegistration registration,
            SettingField field,
            Action onRebuild)
        {
            Type type = field.ValueType;

            if (field.Metadata is ModColorAttribute)
            {
                string currentHex = (string?)field.GetValue(registration.Values) ?? "#FFFFFF";
                AddSettingRow(builder, field, hBuilder =>
                {
                    hBuilder.AddColorDropdown(currentHex,
                        () => { },
                        hex =>
                        {
                            if (string.IsNullOrWhiteSpace(hex))
                            {
                                // The game's Default button passes null.
                                if (!registration.TryResetFieldToDefault(field.Name, out string? resetError))
                                {
                                    ShowError(resetError ?? "Unable to reset " + field.Label + ".");
                                }
                                return;
                            }

                            // Save live changes without destroying the open picker.
                            if (!registration.TrySet(field.Name, hex, out string? error))
                            {
                                ShowError(error ?? "Unable to update " + field.Label + ".");
                            }
                        },
                        _ => onRebuild()).Width(195f);
                });
                return;
            }

            if (type == typeof(ModKeyBinding))
            {
                ModKeyBinding binding = field.GetValue(registration.Values) as ModKeyBinding
                    ?? new ModKeyBinding();
                bool capturing = ModKeybindCapture.IsCapturing(registration, field);
                // Keybind rows are tall enough for wrapped labels.
                RectTransform keybindRow = AddSettingRow(builder, field, hBuilder =>
                {
                    hBuilder.AddButtonCompact(capturing ? "Press a key..." : EscapeMarkup(binding.ToString()), () =>
                    {
                        if (ModKeybindCapture.IsCapturing(registration, field))
                        {
                            ModKeybindCapture.Cancel();
                        }
                        else
                        {
                            ModKeybindCapture.Begin(registration, field, onRebuild);
                        }
                    }).Width(145f);
                    hBuilder.AddButtonCompact("Clear", () =>
                    {
                        ModKeybindCapture.Cancel(rebuild: false);
                        ApplySetting(registration, field, new ModKeyBinding(), onRebuild);
                    }).Width(54f);
                    hBuilder.AddButtonCompact("Reset", () =>
                    {
                        ModKeybindCapture.Cancel(rebuild: false);
                        if (!registration.TryResetFieldToDefault(field.Name, out string? error))
                        {
                            ShowError(error ?? "Unable to reset " + field.Label + ".");
                            return;
                        }
                        onRebuild();
                    }).Width(54f);
                });
                keybindRow.Height(54f);
                HorizontalLayoutGroup? keybindLayout = keybindRow.GetComponent<HorizontalLayoutGroup>();
                if (keybindLayout != null)
                {
                    keybindLayout.childAlignment = TextAnchor.MiddleLeft;
                }
                if (capturing)
                {
                    builder.AddLabel("<color=#888888>Press a key combination, or release Ctrl/Shift/Alt alone. Escape: cancel. Backspace/Delete: clear.</color>");
                }
                return;
            }

            if (type == typeof(bool))
            {
                RectTransform row = AddSettingRow(builder, field, hBuilder =>
                {
                    hBuilder.AddToggle(
                        () => (bool)(field.GetValue(registration.Values) ?? false),
                        value => ApplySetting(registration, field, value, onRebuild))
                        .Width(30f);
                });
                // Don't force 30px here; long labels were overlapping the next row.
                LayoutElement rowHeight = row.GetComponent<LayoutElement>()
                    ?? row.gameObject.AddComponent<LayoutElement>();
                rowHeight.minHeight = 34f;
                HorizontalLayoutGroup? rowLayout = row.GetComponent<HorizontalLayoutGroup>();
                if (rowLayout != null)
                {
                    rowLayout.childControlHeight = true;
                    rowLayout.childForceExpandHeight = false;
                    rowLayout.childAlignment = TextAnchor.MiddleLeft;
                }
                builder.Spacer(5f);
                return;
            }

            if (type.IsEnum)
            {
                List<string> names = Enum.GetNames(type).ToList();
                object currentValue = field.GetValue(registration.Values) ?? Activator.CreateInstance(type)!;
                string? current = Enum.GetName(type, currentValue);
                int selected = Math.Max(0, names.IndexOf(current ?? ""));
                AddSettingRow(builder, field, hBuilder =>
                {
                    hBuilder.AddDropdown(names, selected, index =>
                    {
                        if (index >= 0 && index < names.Count)
                        {
                            ApplySetting(registration, field, names[index], onRebuild);
                        }
                    }).Width(195f);
                });
                return;
            }

            // Only bounded numeric fields get a slider. Keep the input for exact values.
            bool isNumber = type == typeof(int) || type == typeof(float) || type == typeof(double);
            double min = field.Metadata.Min;
            double max = field.Metadata.Max;
            double step = double.IsNaN(field.Metadata.Step)
                ? (field.Metadata.Precision >= 0 ? Math.Pow(10, -field.Metadata.Precision) : 1)
                : field.Metadata.Step;
            bool canSlider = isNumber &&
                !double.IsNaN(min) && !double.IsNaN(max) &&
                !double.IsInfinity(min) && !double.IsInfinity(max) &&
                !double.IsNaN(step) && !double.IsInfinity(step) &&
                min < max && step > 0 &&
                min >= -float.MaxValue && max <= float.MaxValue &&
                (max - min) / step <= 10000 &&
                (type != typeof(int) ||
                    (min >= int.MinValue && max <= int.MaxValue && step == Math.Truncate(step)));

            string currentText = FormatValue(field.GetValue(registration.Values), field.Metadata);
            float preview = 0f;
            float increment = 0f;
            if (canSlider)
            {
                preview = Convert.ToSingle(field.GetValue(registration.Values), CultureInfo.InvariantCulture);
                increment = (float)step;
                canSlider = increment > 0f;
            }

            AddSettingRow(builder, field, hBuilder =>
            {
                if (canSlider)
                {
                    hBuilder.AddSliderQuantized(
                        () => preview,
                        () => FormatValue(preview, field.Metadata),
                        value => preview = value,
                        increment,
                        (float)min,
                        (float)max,
                        value => ApplySetting(registration, field,
                            type == typeof(int) ? (object)checked((int)Math.Round(value)) : value,
                            onRebuild)).Width(160f);
                }

                hBuilder.AddInputField(currentText,
                    value => ApplySetting(registration, field, value, onRebuild))
                    .Width(isNumber ? 76f : 195f);
            });
        }

        private static string FormatValue(object? value, ModSettingAttribute metadata)
        {
            if (value == null)
            {
                return "";
            }
            if (metadata.Precision >= 0 &&
                (value is float || value is double || value is decimal))
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    .ToString("F" + metadata.Precision, CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        private static void ApplySetting(
            SettingsRegistration registration,
            SettingField field,
            object value,
            Action onRebuild)
        {
            try
            {
                if (!registration.TrySet(field.Name, value, out string? error))
                {
                    ShowError(error ?? "Unable to update " + field.Label + ".");
                    return;
                }

                onRebuild?.Invoke();
            }
            catch (Exception ex)
            {
                ShowError("Unable to update " + field.Label + ": " + ex.Message);
            }
        }

        private static void AddTooltip(RectTransform row, SettingField field)
        {
            if (!string.IsNullOrWhiteSpace(field.Metadata.Tooltip))
            {
                row.Tooltip(field.Label, field.Metadata.Tooltip);
            }
        }

        private static void ShowError(string message)
        {
            ModalAlertController.PresentOkay("Mod Settings", message);
        }

        private static string EscapeMarkup(string text)
        {
            // Labels are rich text. Escape mod-supplied names before rendering.
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }

    // Lives on the menu so a rebuilt list doesn't destroy our scroll state.
    public sealed class ModSettingsScrollRestorer : MonoBehaviour
    {
        private ScrollRect? _scroll;
        private int _scrollIndex;
        private float _offsetFromTop;
        private int _framesRemaining;

        internal void Capture(ScrollRect scroll)
        {
            Canvas.ForceUpdateCanvases();
            ScrollRect[] scrolls = GetComponentsInChildren<ScrollRect>(true)
                .Where(s => s.vertical).ToArray();
            // The old ScrollRect may get destroyed; keep its index to find the replacement.
            _scrollIndex = Array.IndexOf(scrolls, scroll);
            _scroll = scroll;
            float height = Mathf.Max(0f, scroll.content.rect.height - ViewportHeight(scroll));
            _offsetFromTop = (1f - scroll.verticalNormalizedPosition) * height;
            _framesRemaining = 0;
        }

        internal void RestoreAfterRebuild()
        {
            // ContentSizeFitter may update again after the first layout frame.
            _framesRemaining = 2;
            Restore();
        }

        private void LateUpdate()
        {
            if (_framesRemaining > 0)
            {
                Restore();
                _framesRemaining--;
            }
        }

        private void Restore()
        {
            ScrollRect? scroll = _scroll;
            if (scroll == null)
            {
                ScrollRect[] scrolls = GetComponentsInChildren<ScrollRect>(true)
                    .Where(s => s.vertical).ToArray();
                if (_scrollIndex < 0 || _scrollIndex >= scrolls.Length)
                {
                    _framesRemaining = 0;
                    return;
                }
                scroll = scrolls[_scrollIndex];
                _scroll = scroll;
            }
            if (scroll.content == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            float height = Mathf.Max(0f, scroll.content.rect.height - ViewportHeight(scroll));
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = height > 0f
                ? 1f - Mathf.Clamp01(_offsetFromTop / height)
                : 1f;
        }

        private void OnDisable()
        {
            _framesRemaining = 0;
        }

        private static float ViewportHeight(ScrollRect scroll)
        {
            return scroll.viewport != null
                ? scroll.viewport.rect.height
                : ((RectTransform)scroll.transform).rect.height;
        }
    }
}
