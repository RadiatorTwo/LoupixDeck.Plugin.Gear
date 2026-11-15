# Third-party notices

The Pulsar protocol in this plugin was re-implemented in C# from the following MIT-licensed
projects. Their frame layout, memory addresses and model tables served as the reference
(see the comments in `Providers/Pulsar/`). No code was copied.

- **bluesceada/pulsar-mouse-linux** (https://github.com/bluesceada/pulsar-mouse-linux),
  Copyright (c) 2026 Packerlschupfer
- **ryanlewis/mousebatt** (https://github.com/ryanlewis/mousebatt),
  Copyright (c) 2026 Ryan Lewis

The battery voltage table (`PulsarBattery`) is not taken from either project. It comes from
values measured in the vendor app's configuration (`BatteryParam`); no code was taken from it.

## Logitech provider

Unlike the Pulsar protocol, code and logic of the Logitech provider (`Providers/Logitech/`) **were
ported** from the following projects. Each ported file names its sources in its header comment. The
HID++ framing and the feature payload layouts follow them; the key, the DPI ladder, the link rules
and the battery voltage curve are this plugin's own.

- **loxsmoke/openlogi-net** (https://github.com/loxsmoke/openlogi-net), MIT License,
  Copyright (c) 2026 LoxSmoke (license text below).
- **openlogi-hidpp**, the HID++ crate of **AprilNEA/OpenLogi** (https://github.com/AprilNEA/OpenLogi),
  BSD Zero Clause License, Copyright (c) 2025 Lukas Schulte Pelkum; a hard fork of **lus/logy**
  (license text below).

No code was taken from Solaar.

## SteelSeries provider

The device table of the SteelSeries provider (`Providers/SteelSeries/`) was **ported** from
**flozz/rivalcfg** (https://github.com/flozz/rivalcfg), Copyright (c) Fabien LOISON and
contributors, released under the WTFPL (license text below): product ids, USB interfaces, command
bytes, the battery decoding, the DPI and polling-rate encodings and the sensor tables in
`rivalcfg/devices/dpi/`. The C# code, the write-only state handling and the "set every slot"
DPI behaviour are this plugin's own. The battery reply of the Rival 650 was checked on real hardware.

The plugin icon (`icon.png`) is rendered by `tools/icon/make_icon.py` from the computer mouse of
**microsoft/fluentui-emoji** (https://github.com/microsoft/fluentui-emoji),
Copyright (c) Microsoft Corporation, also under the MIT License below.

## MIT License

```text
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## MIT License (openlogi-net)

```text
MIT License

Copyright (c) 2026 LoxSmoke

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## BSD Zero Clause License (openlogi-hidpp)

```text
BSD Zero Clause License

Copyright (c) 2025 Lukas Schulte Pelkum

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY
AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
PERFORMANCE OF THIS SOFTWARE.
```

## WTFPL (rivalcfg)

```text
            DO WHAT THE FUCK YOU WANT TO PUBLIC LICENSE
                    Version 2, December 2004

 Copyright (C) 2004 Sam Hocevar <sam@hocevar.net>

 Everyone is permitted to copy and distribute verbatim or modified
 copies of this license document, and changing it is allowed as long
 as the name is changed.

            DO WHAT THE FUCK YOU WANT TO PUBLIC LICENSE
   TERMS AND CONDITIONS FOR COPYING, DISTRIBUTION AND MODIFICATION

  0. You just DO WHAT THE FUCK YOU WANT TO.
```
