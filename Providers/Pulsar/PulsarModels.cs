using LoupixDeck.Plugin.Gear.Devices;

namespace LoupixDeck.Plugin.Gear.Providers.Pulsar;

/// <summary>
/// Static knowledge about Pulsar mice. The VID:PID list is taken from the MIT-licensed
/// bluesceada/pulsar-mouse-linux (Nordic 17-byte protocol); see THIRD-PARTY-NOTICES.md.
/// </summary>
internal static class PulsarModels
{
    public const ushort VendorUsagePage = 0xFF02;
    public const int VendorReportLength = 17;

    /// <summary>Model id of the X2 CrazyLight in the Info reply.</summary>
    public const ushort X2CrazyLightModelId = 0x5717;

    private static readonly Dictionary<(ushort Vid, ushort Pid), (string Name, bool Tested)> Receivers = new()
    {
        [(0x3710, 0x5406)] = ("Pulsar X2 CrazyLight", true),
        [(0x3554, 0xF507)] = ("Pulsar (wired)", false),
        [(0x3554, 0xF508)] = ("Pulsar (1K dongle)", false),
        [(0x3554, 0xF509)] = ("Pulsar (4K dongle)", false),
        [(0x3710, 0x5402)] = ("Pulsar X3 (1K dongle)", false),
        [(0x3710, 0x3414)] = ("Pulsar X2 CrazyLight (wired)", false),
    };

    // Model ids reported by the Info command; unknown ids are shown as "Pulsar <hex id>".
    private static readonly Dictionary<ushort, string> ModelNames = new()
    {
        [0x5717] = "Pulsar X2 CrazyLight", // Info reply of the 3710:5406 dongle, read on real hardware.
    };

    /// <summary>(code, Hz) pairs of the polling-rate register.</summary>
    private static readonly (byte Code, int Hz)[] PollingCodes =
    [
        (0x08, 125), (0x04, 250), (0x02, 500), (0x01, 1000), (0x10, 2000), (0x20, 4000), (0x40, 8000),
    ];

    public static bool TryGetReceiver(ushort vid, ushort pid, out string name, out bool tested)
    {
        if (Receivers.TryGetValue((vid, pid), out (string Name, bool Tested) entry))
        {
            name = entry.Name;
            tested = entry.Tested;
            return true;
        }

        name = string.Empty;
        tested = false;
        return false;
    }

    public static string NameForModel(ushort modelId)
    {
        return ModelNames.TryGetValue(modelId, out string? name) ? name : $"Pulsar {modelId:x4}";
    }

    public static GearLink LinkFromByte(byte link)
    {
        return link switch
        {
            0 => GearLink.Wireless1K,
            1 => GearLink.Wireless4K,
            2 => GearLink.Wired,
            3 => GearLink.Wired,
            4 => GearLink.Wireless2K,
            5 => GearLink.Wireless8K,
            _ => GearLink.Unknown,
        };
    }

    /// <summary>Allowed polling rates for the raw link byte; unknown links get the conservative 1K set.</summary>
    public static IReadOnlyList<int> AllowedRates(byte? linkByte)
    {
        int max = linkByte switch
        {
            1 => 4000,
            3 => 8000,
            4 => 2000,
            5 => 8000,
            _ => 1000,
        };

        return PollingCodes.Select(c => c.Hz).Where(hz => hz <= max).Order().ToArray();
    }

    public static bool TryCodeForHz(int hz, out byte code)
    {
        foreach ((byte c, int h) in PollingCodes)
        {
            if (h == hz)
            {
                code = c;
                return true;
            }
        }

        code = 0;
        return false;
    }

    public static bool TryHzForCode(byte code, out int hz)
    {
        foreach ((byte c, int h) in PollingCodes)
        {
            if (c == code)
            {
                hz = h;
                return true;
            }
        }

        hz = 0;
        return false;
    }
}
