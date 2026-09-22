using System.Windows;
using System.Windows.Controls;
using NetPriority.Models;

namespace NetPriority;

public partial class CreatePolicyWindow : Window
{
    public string RuleName { get; private set; } =
        string.Empty;

    public int DscpValue { get; private set; }

    public bool KeepRuleOnExit { get; private set; }

    public CreatePolicyWindow(
        ApplicationInfo application)
    {
        InitializeComponent();

        ApplicationNameText.Text =
            application.ProcessName;

        ApplicationPathText.Text =
            application.ExecutablePath;

        DscpComboBox.SelectedIndex = 0;
    }

    private void DscpComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DscpComboBox.SelectedItem
            is not ComboBoxItem item)
        {
            return;
        }

        if (!int.TryParse(
                item.Tag?.ToString(),
                out var value))
        {
            return;
        }

        if (value >= 0)
        {
            DscpTextBox.Text =
                value.ToString();

            DscpTextBox.IsEnabled =
                false;
        }
        else
        {
            DscpTextBox.IsEnabled =
                true;
        }
    }

    private void CreateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name =
            RuleNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                "Enter a name for the rule.",
                "NetPriority",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (!int.TryParse(
                DscpTextBox.Text.Trim(),
                out var dscp))
        {
            MessageBox.Show(
                "The DSCP value must be a number.",
                "NetPriority",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (dscp < 0 || dscp > 63)
        {
            MessageBox.Show(
                "The DSCP value must be between 0 and 63.",
                "NetPriority",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        RuleName = name;

        DscpValue = dscp;

        KeepRuleOnExit =
            KeepRuleCheckBox.IsChecked == true;

        DialogResult = true;
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}