# Plugin icon generator

`make_icon.py` renders `icon.png` (repo root) from `computer_mouse_flat.svg`, in the same polished
silver style as the other LoupixDeck plugin icons.

```bash
pip install pillow numpy resvg-py
python tools/icon/make_icon.py
```

It also writes `preview.png` (light and dark background) next to the script; that file is not committed.

`computer_mouse_flat.svg` is the computer mouse from [Fluent Emoji](https://github.com/microsoft/fluentui-emoji),
MIT License, Copyright (c) Microsoft Corporation.
