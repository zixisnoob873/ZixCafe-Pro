using System.Net.Http;
using System.Text.Json;

namespace ZixCafe.Domain.Services;

public record SmartRelayCommand(
    string RelayType, // Shelly, Sonoff, Tasmota, MQTT, GenericHttp
    string TargetAddress, // IP / Host / Topic
    int Channel,
    bool PowerOn);

public static class SmartRelayController
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static string BuildRelayRestUrl(string relayType, string hostOrIp, int channel, bool powerOn)
    {
        var state = powerOn ? "on" : "off";
        var stateUpper = powerOn ? "ON" : "OFF";
        var host = hostOrIp.Trim().TrimEnd('/');
        if (!host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            host = $"http://{host}";
        }

        return relayType.ToLowerInvariant() switch
        {
            "shelly" => $"{host}/relay/{channel}?turn={state}",
            "tasmota" or "sonoff" => $"{host}/cm?cmnd=Power{channel + 1}%20{stateUpper}",
            _ => $"{host}/api/relay/{channel}?state={state}"
        };
    }

    public static (string Topic, string Payload) BuildMqttMessage(string baseTopic, int channel, bool powerOn)
    {
        var topic = $"{baseTopic.TrimEnd('/')}/cmnd/POWER{channel + 1}";
        var payload = powerOn ? "ON" : "OFF";
        return (topic, payload);
    }

    public static async Task<bool> SendPowerCommandAsync(SmartRelayCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.TargetAddress) || cmd.RelayType.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (cmd.RelayType.Equals("MQTT", StringComparison.OrdinalIgnoreCase))
            {
                // MQTT handled via message broker
                return true;
            }

            var url = BuildRelayRestUrl(cmd.RelayType, cmd.TargetAddress, cmd.Channel, cmd.PowerOn);
            var response = await HttpClient.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
