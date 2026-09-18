using System.Net;
using System.Net.Sockets;

namespace JobHunter.Prefill;

/// <summary>Which browser prefill works in.</summary>
public enum PrefillBrowserMode
{
    /// <summary>Prefill starts its own Chromium with the profile under the data folder and closes it when the application stops.</summary>
    Launch,

    /// <summary>Prefill attaches over the DevTools protocol to a Chromium already running on the desktop and lets go of it without closing it.</summary>
    Connect
}

/// <summary>Where prefill's browser comes from: the debug endpoint of a Chromium already running on the desktop when one is configured, otherwise a Chromium prefill starts itself.</summary>
/// <param name="Mode">Whether prefill starts a browser of its own or attaches to one that is already running.</param>
/// <param name="Endpoint">The configured debug endpoint, null when prefill starts its own browser.</param>
public sealed record PrefillBrowserSource(PrefillBrowserMode Mode, string? Endpoint)
{
    /// <summary>The configuration key that carries the debug endpoint of the browser on the desktop.</summary>
    public const string EndpointKey = "Prefill:BrowserEndpoint";

    /// <summary>Reads the choice from configuration; an absent or blank endpoint leaves prefill starting its own browser.</summary>
    public static PrefillBrowserSource From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? endpoint = configuration[EndpointKey];

        return string.IsNullOrWhiteSpace(endpoint)
            ? new PrefillBrowserSource(PrefillBrowserMode.Launch, null)
            : new PrefillBrowserSource(PrefillBrowserMode.Connect, endpoint.Trim());
    }

    /// <summary>Whether the browser closes when the application stops: a browser prefill started belongs to the application, a browser on the desktop belongs to the person sitting at it.</summary>
    public bool ClosesOnShutdown => Mode is PrefillBrowserMode.Launch;

    /// <summary>The endpoint with its host replaced by an address, because the browser's debug port answers 404 to a host header that is neither localhost nor an address.</summary>
    public async Task<string> ResolveEndpointAsync(CancellationToken cancellationToken = default)
    {
        if (Endpoint is null)
        {
            throw new InvalidOperationException("Prefill starts its own browser, so there is no endpoint to resolve.");
        }

        Uri endpoint = new(Endpoint, UriKind.Absolute);
        string configured = endpoint.Host.Trim('[', ']');
        IPAddress address = IPAddress.TryParse(configured, out IPAddress? literal) ? literal : await LookUpAsync(configured, cancellationToken);
        string host = address.AddressFamily is AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

        return endpoint.PathAndQuery is "/"
            ? $"{endpoint.Scheme}://{host}:{endpoint.Port}"
            : $"{endpoint.Scheme}://{host}:{endpoint.Port}{endpoint.PathAndQuery}";
    }

    private static async Task<IPAddress> LookUpAsync(string host, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);

        return addresses.FirstOrDefault(address => address.AddressFamily is AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException($"The prefill browser host {host} resolves to no address.");
    }
}
