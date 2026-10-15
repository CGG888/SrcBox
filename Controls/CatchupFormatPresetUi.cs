using System;
using System.Windows.Controls;
using LibmpvIptvClient.Helpers;
using LibmpvIptvClient.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace LibmpvIptvClient.Controls
{
    /// <summary>
    /// Shared wiring of the "common formats" combo box in the replay and timeshift drawers. Picking an
    /// entry only fills the url format box, so the user can still edit the result before saving.
    /// </summary>
    internal static class CatchupFormatPresetUi
    {
        internal static void Fill(ComboBox box)
        {
            box.Items.Clear();
            foreach (var preset in CatchupFormatPresets.All)
            {
                box.Items.Add(new ComboBoxItem
                {
                    Content = ResxLocalizer.Get(preset.LabelKey, preset.Template),
                    Tag = preset.Template
                });
            }
        }

        /// <summary>Template of the selected entry, or null when nothing (usable) is selected.</summary>
        internal static string? TemplateOf(ComboBox box)
            => box.SelectedItem is ComboBoxItem item && item.Tag is string template ? template : null;

        /// <summary>Highlights a saved template when it equals a preset; a hand written one leaves the box empty.</summary>
        internal static void SelectMatching(ComboBox box, string? template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                box.SelectedIndex = -1;
                return;
            }

            for (var i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is ComboBoxItem item && item.Tag is string candidate &&
                    string.Equals(candidate, template.Trim(), StringComparison.Ordinal))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            box.SelectedIndex = -1;
        }
    }
}
