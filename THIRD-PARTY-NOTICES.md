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
