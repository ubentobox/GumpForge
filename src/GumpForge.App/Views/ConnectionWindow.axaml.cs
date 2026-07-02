using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GumpForge.App.Services;

namespace GumpForge.App.Views;

public partial class ConnectionWindow : Window
{
    public bool ConnectionSuccess { get; private set; }

    public ConnectionWindow()
    {
        InitializeComponent();
    }

    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        var host = HostText.Text?.Trim();
        var portStr = PortText.Text?.Trim();
        var username = UsernameText.Text?.Trim();
        var password = PasswordText.Text;

        if (string.IsNullOrEmpty(host))
        {
            StatusText.Text = "Please enter a host address.";
            return;
        }

        if (string.IsNullOrEmpty(portStr) || !int.TryParse(portStr, out int port))
        {
            StatusText.Text = "Please enter a valid port number.";
            return;
        }

        if (string.IsNullOrEmpty(username))
        {
            StatusText.Text = "Please enter your staff account username.";
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            StatusText.Text = "Please enter your staff account password.";
            return;
        }

        StatusText.Text = "Connecting...";
        StatusText.Foreground = Avalonia.Media.Brushes.Orange;

        try
        {
            var serverLink = ServerLinkService.Instance;
            
            // Temporary handlers to check success/failure
            Action<bool, string>? authHandler = null;
            authHandler = (success, error) =>
            {
                serverLink.AuthCompleted -= authHandler;
                
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (success)
                    {
                        ConnectionSuccess = true;
                        Close(true);
                    }
                    else
                    {
                        StatusText.Text = $"Failed: {error}";
                        StatusText.Foreground = Avalonia.Media.Brushes.Red;
                    }
                });
            };

            serverLink.AuthCompleted += authHandler;

            await serverLink.ConnectAsync(host, port, username, password);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Connection error: {ex.Message}";
            StatusText.Foreground = Avalonia.Media.Brushes.Red;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
