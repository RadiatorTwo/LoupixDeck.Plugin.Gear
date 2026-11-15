# LoupixDeck.Plugin.Gear

Detects gaming mice (Pulsar, Logitech and SteelSeries) and shows battery, polling rate and DPI on a Loupedeck device. Polling rate and
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

### Logitech

Logitech mice that speak HID++ 2.0, either through a Unifying, Bolt or LIGHTSPEED receiver (all six
slots are checked; keyboards and empty slots are not shown) or directly on a USB cable. Bluetooth
is not covered. The plugin reads these HID++ features and offers what the mouse has:

| Feature | HID++ id |
|---|---|
| Battery (first one the mouse has) | `0x1004`, `0x1001`, `0x1000` |
| Polling rate (first one the mouse has) | `0x8061`, `0x8060` |
| DPI | `0x2201` |

Everything about Logitech is **untested** except what the read-only hardware smoke test covers
(a G502 Proteus Spectrum on a cable: name, polling rate and DPI read). Writing the polling rate
and the DPI, the battery and the receiver routes have not been tried.

G-series mice with onboard profiles (`0x8100`) refuse polling-rate and DPI writes while they run
their onboard profile. When you press a polling-rate or DPI button on such a mouse, the plugin
first switches it to host mode, as G HUB does while it runs. It never switches back. In host mode
the buttons and DPI stages stored in the onboard profile may not apply until the mouse is
replugged or G HUB takes over again. Reading never changes the mode.

- The key of a mouse is `logitech:model:<model id>:<unit id>`; it is the same over receiver and
  cable, so a button keeps working when you change the connection.
- HID++ has no list of DPI stages, so `next` and `previous` cycle a fixed ladder (400, 800, 1600,
  3200, 6400, 12800, 25600 DPI, limited to what the sensor supports, plus the current value). A
  DPI value snaps to the nearest value the sensor supports; a value outside the sensor range is
  rejected.
- The battery percentage of mice that only report a voltage is estimated with a generic Li-ion
  curve and may differ from G HUB.
- Mice that only report DPI through `0x2202` (extended DPI) show no DPI, and devices that only
  speak HID++ 1.0 are not supported.

### SteelSeries

SteelSeries mice from the [rivalcfg](https://github.com/flozz/rivalcfg) device list (76 USB ids:
Aerox 3/5/9, Prime, Prime+, Prime Mini, Prime Wireless, Rival 3/5/95/100/105/106/110/300/300S/310/
500/600/650/700/710, Rival 3 (Wireless) Gen 2, Sensei 310/TEN/[RAW], Kana v2, Kinzu v2). Only the
**Rival 650 Wireless on the cable (`1038:172b`) is tested**; every other id, including the Rival 650
dongle (`1038:1726`), is untested.

- Battery is shown for the mice that report it: Rival 650, Rival 3 Wireless (Gen 2), Aerox 3/5/9
  Wireless and Prime (Mini) Wireless. Wired-only models show no battery.
- These mice **cannot report their polling rate or DPI**. Both stay empty until you set them with
  a Gear button; after that the plugin shows the value it wrote. A change made in SteelSeries GG or
  with the DPI button on the mouse is not seen, and `next` / `previous` only work once a value is
  known. (The Rival 650 does send an event on interface 3 when its DPI button is pressed, but it
  carries no stage, so it is not used.)
- Setting the DPI makes the mouse run at that value: two-slot models (Rival 650, Rival 300/310/
  500/600/700, Sensei 310, ...) get the value in both slots, newer models get a single preset. A
  value snaps to the nearest one the sensor supports.
- Nothing is saved to the mouse's memory, so a replug restores the settings stored in the mouse.
- The key of a mouse is `steelseries:model:<product id>`, plus the USB serial if the mouse has a
  real one. Cable and dongle have different product ids, so they count as two devices.

## Commands

All commands are touch-button tiles in the group **Gear**. The command menu offers one paging
tile over all mice ("All mice"), then one group per detected mouse with its device page, polling
rate and DPI tiles, prefilled with that mouse's key.

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
KERNEL=="hidraw*", ATTRS{idVendor}=="046d", TAG+="uaccess"
KERNEL=="hidraw*", ATTRS{idVendor}=="1038", TAG+="uaccess"
```

The `046d` line is for Logitech receivers and mice, the `1038` line for SteelSeries.

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

The Pulsar protocol was re-implemented from MIT-licensed projects. The Logitech provider ports
code and logic from openlogi-net (MIT) and openlogi-hidpp (0BSD). The SteelSeries provider ports
the device table and value encodings of rivalcfg (WTFPL); see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
