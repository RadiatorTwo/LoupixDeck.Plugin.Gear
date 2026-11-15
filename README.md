# LoupixDeck.Plugin.Gear

Detects gaming mice and shows battery, polling rate and DPI on a Loupedeck device. Polling rate and
DPI can be switched with a press.

The plugin supports **Windows and Linux** and needs LoupixDeck with Plugin SDK 1.31 or newer. Mice
are found automatically; without a mouse the buttons show `NO DEVICE`.

## Supported devices

Pulsar mice that speak the 17-byte Nordic protocol:

| Device | USB id | Status |
|---|---|---|
| Pulsar X2 CrazyLight, 8K dongle | `3710:5406` | tested |
| Pulsar X2 CrazyLight, wired | `3710:3414` | untested |
| 1K dongle (X3) | `3710:5402` | untested |
| Wired | `3554:f507` | untested |
| 1K dongle | `3554:f508` | untested |
| 4K dongle | `3554:f509` | untested |

"Untested" means the id comes from the protocol references and has not been tried on hardware.
The X2A v3 (`3710:5403`, `3710:3408`) uses a different protocol and is not supported.

## Commands

All commands are touch-button tiles in the group **Gear**. The command menu lists one entry per
detected mouse, prefilled with that mouse's key.

| Command | Parameters | Meaning |
|---|---|---|
| `Gear.Pages` | `Pages` | Shows the pages of your mice; a press shows the next page. |
| `Gear.PollingRate` | `Rate`, `Device` | Shows the polling rate; a press sets it. |
| `Gear.Dpi` | `Dpi`, `Device` | Shows the active DPI value and stage; a press sets it. |

- **`Pages`** is empty or `all` (every mouse page and the overview), or `|`-separated tokens that
  are device keys or `overview`, for example `pulsar:model:5717|overview`. Several `Gear.Pages`
  commands chained on one button add up to one cycle in sequence order. Pages without a device are
  skipped.
- **`Rate`** is `next` (cycles the rates the mouse accepts on its current link) or a rate in Hz:
  `125`, `250`, `500`, `1000`, `2000`, `4000` or `8000`. The rates allowed depend on the link; 8K
  needs the 8K dongle or the cable.
- **`Dpi`** is `next` or `previous` (selects the next or previous stage, wrapping around) or a DPI
  value, for example `1600`.
- **`Device`** (optional) is the key of a mouse. Empty uses the first mouse.

The battery percentage matches the Pulsar app.

## Linux

`hidraw` devices belong to root by default. The plugin shows `NO ACCESS` and installs nothing
itself. To give your user access, create `/etc/udev/rules.d/70-loupixdeck-gear.rules`:

```text
KERNEL=="hidraw*", ATTRS{idVendor}=="3710", TAG+="uaccess"
KERNEL=="hidraw*", ATTRS{idVendor}=="3554", TAG+="uaccess"
```

Then reload the rules and replug the dongle:

```bash
sudo udevadm control --reload && sudo udevadm trigger
```

## Status

Reading the battery and the polling rate has been verified on a Pulsar X2 CrazyLight 8K dongle.
Reading the DPI stages has not been confirmed on hardware yet.
**Writing the polling rate and the DPI has not been confirmed on hardware yet**; treat it as
untested until users report that it works.

## Build

```bash
dotnet build -c Release
loupix pack          # validates plugin.json and writes the store files to dist/
loupix install       # copies the plugin into the LoupixDeck plugins folder
```

## Third-party

The protocol was re-implemented from MIT-licensed projects; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
