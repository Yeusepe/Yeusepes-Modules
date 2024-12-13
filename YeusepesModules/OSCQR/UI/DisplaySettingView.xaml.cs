using System.Linq;
using System.Collections.Generic;
using System.Windows.Controls;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using System.Windows;
using System.Windows.Input;

namespace VIRAModules.OSCQR.UI
{
    public partial class DisplaySettingView : UserControl
    {
        private List<string> availableDisplays = new();
        private StringModuleSetting? _setting;

        public DisplaySettingView(Module module, ModuleSetting setting)
        {
            InitializeComponent();

            // Ensure the setting is a StringModuleSetting
            if (setting is StringModuleSetting stringSetting)
            {
                _setting = stringSetting;
                DataContext = stringSetting;
            }
            else
            {
                throw new InvalidOperationException("DisplaySettingView requires a StringModuleSetting.");
            }

            // Populate display suggestions
            if (module is OSCQR oscqrModule)
            {
                availableDisplays = oscqrModule.GetDisplays();
            }
        }

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string input = InputBox.Text.ToLower();

            // Filter displays based on user input
            var filteredDisplays = availableDisplays
                .Where(display => display.ToLower().Contains(input))
                .ToList();

            // Show or hide suggestions
            if (filteredDisplays.Any())
            {
                SuggestionList.ItemsSource = filteredDisplays;
                SuggestionList.Visibility = Visibility.Visible;
            }
            else
            {
                SuggestionList.Visibility = Visibility.Collapsed;
            }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                // Focus on the list and select the first item
                SuggestionList.Focus();
                SuggestionList.SelectedIndex = 0;
            }
        }

        private void SuggestionList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SuggestionList.SelectedItem is string selectedDisplay)
            {
                // Update the TextBox and hide the suggestions
                InputBox.Text = selectedDisplay;
                SuggestionList.Visibility = Visibility.Collapsed;
                InputBox.Focus();
            }
            else if (e.Key == Key.Escape)
            {
                // Hide suggestions on Escape key
                SuggestionList.Visibility = Visibility.Collapsed;
                InputBox.Focus();
            }
        }

        private void SuggestionList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (SuggestionList.SelectedItem is string selectedDisplay)
            {
                // Update the TextBox and hide the suggestions
                InputBox.Text = selectedDisplay;
                SuggestionList.Visibility = Visibility.Collapsed;
            }
        }
    }
}
