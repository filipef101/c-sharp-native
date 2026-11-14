#!/usr/bin/env python3
"""Fail if the default button or field paints a transparent first frame.

The reverted deferral drew a transparent placeholder and reported that
frame. StartupButton and AndroidField must paint an opaque drawable
before the first draw. A deferred chrome path, if one comes back, has
to paint the solid fill rather than a transparent placeholder.
"""

import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]


def body(text, signature):
    start = text.find(signature)
    if start < 0:
        return None
    brace = text.find("{", start)
    if brace < 0:
        return None
    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[brace : index + 1]
    return None


def transparent(source):
    banned = (
        "Background = null",
        "Color.Transparent",
        "new Color(0)",
        "new Color(0x0)",
        "SetColor(0)",
    )
    return any(token in source for token in banned)


def check_sources(button, field, defer):
    errors = []
    if button is None:
        errors.append("missing StartupButton constructor")
    elif transparent(button) or "FlatChrome.Palette" not in button or "RippleDrawable" not in button:
        errors.append("StartupButton first frame is not the opaque card fill")
    if field is None:
        errors.append("missing AndroidField constructor")
    elif transparent(field) or "new FieldRule" not in field or "ThemeTokens.Accent" not in field:
        errors.append("AndroidField first frame is not the opaque accent rule")
    if defer is not None and (transparent(defer) or "ApplySolid(" not in defer):
        errors.append("DeferChrome does not paint the solid fill")
    return errors


def main():
    gesture = (ROOT / "src/NativePoc.Android/GesturePlayer.cs").read_text()
    host = (ROOT / "src/NativePoc.Android/AndroidHost.cs").read_text()
    errors = check_sources(
        body(gesture, "public StartupButton(Context context)"),
        body(host, "public AndroidField(Context context)"),
        body(host, "void DeferChrome(View view, Style style)"),
    )
    fixture = check_sources(
        "{\n Background = null;\n FlatChrome.Palette RippleDrawable\n}",
        "{\n new FieldRule ThemeTokens.Accent\n}",
        "{\n ApplySolid(\n}",
    )
    if fixture != ["StartupButton first frame is not the opaque card fill"]:
        errors.append("fixture did not reject a transparent button: " + repr(fixture))
    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        return 1
    print("first-frame opaque")
    return 0


if __name__ == "__main__":
    sys.exit(main())
