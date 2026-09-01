using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Client.Agent;

public partial class PairWindow : Window
{
    private const string AgentVersion = "0.1.0";
    private readonly string _machineGuid;

    public string? ResultServerUrl { get; private set; }
    public string? ResultName { get; private set; }
    public string? ResultSecret { get; private set; }

    public PairWindow(string defaultServerUrl, string machineGuid)
    {
        InitializeComponent();
        ServerUrlBox.Text = defaultServerUrl;
        _machineGuid = machineGuid;
        Loaded += (_, _) => CodeBox.Focus();
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        PairButton.IsEnabled = false;

        var serverUrl = ServerUrlBox.Text.Trim().TrimEnd('/');
        var code = CodeBox.Text.Trim();

        if (code.Length != 6 || !code.All(char.IsDigit))
        {
            ErrorText.Text = "The pairing code is 6 digits.";
            PairButton.IsEnabled = true;
            return;
        }

        HubConnection? hub = null;
        try
        {
            hub = new HubConnectionBuilder()
                .WithUrl($"{serverUrl}/hubs/terminal")
                .Build();
            await hub.StartAsync();

            var result = await hub.InvokeAsync<RegisterResult>(
                nameof(ITerminalServer.RegisterAsync),
                new RegisterRequest(code, _machineGuid, AgentVersion));

            ResultServerUrl = serverUrl;
            ResultName = result.Name;
            ResultSecret = result.Secret
                ?? throw new InvalidOperationException("Server accepted the code but returned no secret.");

            await hub.StopAsync();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
        finally
        {
            if (hub is not null)
            {
                await hub.DisposeAsync();
            }
            PairButton.IsEnabled = true;
        }
    }
}
