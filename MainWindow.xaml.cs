using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NetPriority.Models;
using NetPriority.Services;

namespace NetPriority;

public partial class MainWindow : Window
{
    private readonly ProcessService _processService = new();

    private readonly QosService _qosService = new();

    private List<ApplicationInfo> _applications = [];

    private List<QosRule> _policies = [];

    private ApplicationInfo? _selectedApplication;

    public MainWindow()
    {
        InitializeComponent();

        CheckAdministratorStatus();

        LoadApplications();

        _ = LoadPoliciesAsync();
    }

    // =========================================================
    // APPLICATIONS
    // =========================================================

    private void LoadApplications()
    {
        try
        {
            _applications = _processService
                .GetRunningApplications()
                .ToList();

            ApplyApplicationFilter();

            StatusText.Text =
                $"{_applications.Count} applications detected.";
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Error detecting applications: {ex.Message}";
        }
    }

    private void ApplyApplicationFilter()
    {
        var search =
            SearchBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(search))
        {
            ApplicationsList.ItemsSource =
                _applications;

            return;
        }

        ApplicationsList.ItemsSource =
            _applications
                .Where(application =>
                    application.ProcessName
                        .Contains(
                            search,
                            StringComparison.OrdinalIgnoreCase)
                    ||
                    application.ExecutablePath
                        .Contains(
                            search,
                            StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private void SearchButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ApplyApplicationFilter();
    }

    private void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadApplications();
    }

    private void ApplicationsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _selectedApplication =
            ApplicationsList.SelectedItem
            as ApplicationInfo;

        if (_selectedApplication == null)
        {
            SelectedApplicationText.Text =
                "No application selected.";

            SelectedApplicationPathText.Text =
                string.Empty;

            return;
        }

        SelectedApplicationText.Text =
            _selectedApplication.ProcessName;

        SelectedApplicationPathText.Text =
            _selectedApplication.ExecutablePath;

        StatusText.Text =
            $"Selected: {_selectedApplication.ProcessName}";
    }

    // =========================================================
    // MANUAL EXE SELECTION
    // =========================================================

    private void SelectExeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new OpenFileDialog
            {
                Title =
                    "Select application",

                Filter =
                    "Executable files (*.exe)|*.exe",

                CheckFileExists = true,

                Multiselect = false
            };

        if (dialog.ShowDialog() != true)
            return;

        var executablePath =
            dialog.FileName;

        _selectedApplication =
            new ApplicationInfo
            {
                ProcessId = 0,

                ProcessName =
                    System.IO.Path
                        .GetFileName(
                            executablePath),

                ExecutablePath =
                    executablePath
            };

        SelectedApplicationText.Text =
            _selectedApplication.ProcessName;

        SelectedApplicationPathText.Text =
            _selectedApplication.ExecutablePath;

        StatusText.Text =
            "Application selected manually.";
    }

    // =========================================================
    // CREATE POLICY
    // =========================================================

    private async void CreatePolicyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedApplication == null)
        {
            MessageBox.Show(
                "Select an application first.",
                "NetPriority",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var dialog =
            new CreatePolicyWindow(
                _selectedApplication);

        dialog.Owner = this;

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            StatusText.Text =
                "Creating QoS policy...";

            await _qosService.CreatePolicyAsync(
                dialog.RuleName,
                _selectedApplication.ExecutablePath,
                dialog.DscpValue);

            StatusText.Text =
                "QoS policy created successfully.";

            await LoadPoliciesAsync();

            MessageBox.Show(
                $"Policy '{dialog.RuleName}' has been created.",
                "NetPriority",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError(
                "Unable to create QoS policy.",
                ex);
        }
    }

    // =========================================================
    // POLICIES
    // =========================================================

    private async Task LoadPoliciesAsync()
    {
        try
        {
            _policies =
                (await _qosService
                    .GetPoliciesAsync())
                .ToList();

            PoliciesList.ItemsSource =
                null;

            PoliciesList.ItemsSource =
                _policies;

            StatusText.Text =
                $"{_applications.Count} applications detected. " +
                $"{_policies.Count} QoS rules.";
        }
        catch (Exception ex)
        {
            ShowError(
                "Unable to read QoS policies.",
                ex);
        }
    }

    private async void RefreshPoliciesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadPoliciesAsync();
    }

    // =========================================================
    // REMOVE POLICY
    // =========================================================

    private async void RemovePolicyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var selectedPolicy =
            PoliciesList.SelectedItem as QosRule;

        if (selectedPolicy == null)
        {
            MessageBox.Show(
                "Select a QoS rule first.",
                "NetPriority",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (!selectedPolicy.IsManagedByNetPriority)
        {
            var answer =
                MessageBox.Show(
                    "This policy was not created by " +
                    "NetPriority.\n\n" +
                    "It might be a Windows policy, " +
                    "an administrator policy, or a policy " +
                    "created by another program.\n\n" +
                    "Do you really want to remove it?",
                    "NetPriority",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;
        }
        else
        {
            var answer =
                MessageBox.Show(
                    $"Do you want to remove the policy:\n\n" +
                    $"{selectedPolicy.Name}?",
                    "NetPriority",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;
        }

        try
        {
            StatusText.Text =
                "Removing policy...";

            await _qosService
                .RemovePolicyAsync(
                    selectedPolicy.Name);

            await LoadPoliciesAsync();

            StatusText.Text =
                "Policy removed successfully.";
        }
        catch (Exception ex)
        {
            ShowError(
                "Unable to remove QoS policy.",
                ex);
        }
    }

    // =========================================================
    // ADMINISTRATOR
    // =========================================================

    private void CheckAdministratorStatus()
    {
        var isAdmin =
            _qosService
                .IsRunningAsAdministrator();

        if (isAdmin)
        {
            AdminStatusText.Text =
                "● Administrator";

            AdminStatusText.Foreground =
                System.Windows.Media.Brushes.Green;
        }
        else
        {
            AdminStatusText.Text =
                "● Not administrator";

            AdminStatusText.Foreground =
                System.Windows.Media.Brushes.DarkOrange;
        }
    }

    // =========================================================
    // ABOUT
    // =========================================================

    private void AboutButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var about = new AboutWindow
        {
            Owner = this
        };

        about.ShowDialog();
    }

    // =========================================================
    // ERRORS
    // =========================================================

    private void ShowError(
        string title,
        Exception exception)
    {
        StatusText.Text =
            $"{title}";

        MessageBox.Show(
            $"{title}\n\n" +
            $"{exception.Message}",
            "NetPriority",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}