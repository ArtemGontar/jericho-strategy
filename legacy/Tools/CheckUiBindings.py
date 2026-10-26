#!/usr/bin/env python3
"""Static wiring check for JERICHO's runtime-generated Unity UI."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BOOTSTRAP = ROOT / "Assets/Game/Runtime/Client/GameBootstrap.cs"
UI_KIT = ROOT / "Assets/Game/Runtime/Client/RuntimeUI.cs"


def call_arguments(source: str, marker: str):
    position = 0
    while True:
        start = source.find(marker, position)
        if start < 0:
            return
        index = start + len(marker)
        depth = 1
        brace_depth = 0
        bracket_depth = 0
        argument = []
        arguments = []
        quote = None
        escaped = False
        line_comment = False
        block_comment = False
        while index < len(source) and depth:
            char = source[index]
            next_char = source[index + 1] if index + 1 < len(source) else ""
            if line_comment:
                if char == "\n":
                    line_comment = False
            elif block_comment:
                if char == "*" and next_char == "/":
                    block_comment = False
                    index += 1
            elif quote:
                if escaped:
                    escaped = False
                elif char == "\\":
                    escaped = True
                elif char == quote:
                    quote = None
            elif char == "/" and next_char == "/":
                line_comment = True
                index += 1
            elif char == "/" and next_char == "*":
                block_comment = True
                index += 1
            elif char in "\"'":
                quote = char
            elif char == "(":
                depth += 1
                argument.append(char)
            elif char == ")":
                depth -= 1
                if depth:
                    argument.append(char)
                else:
                    arguments.append("".join(argument).strip())
            elif char == "{":
                brace_depth += 1
                argument.append(char)
            elif char == "}":
                brace_depth -= 1
                argument.append(char)
            elif char == "[":
                bracket_depth += 1
                argument.append(char)
            elif char == "]":
                bracket_depth -= 1
                argument.append(char)
            elif char == "," and depth == 1 and brace_depth == 0 and bracket_depth == 0:
                arguments.append("".join(argument).strip())
                argument = []
            else:
                argument.append(char)
            index += 1
        if depth:
            raise AssertionError(f"Unclosed call at byte {start}: {marker}")
        yield arguments
        position = index


def main():
    bootstrap = BOOTSTRAP.read_text(encoding="utf-8")
    ui_kit = UI_KIT.read_text(encoding="utf-8")
    buttons = list(call_arguments(bootstrap, "UiKit.Button("))
    if not buttons:
        raise AssertionError("No UiKit.Button calls were found")
    missing = [i + 1 for i, args in enumerate(buttons) if len(args) < 3 or not args[2]]
    if missing:
        raise AssertionError("Buttons without an action at call indexes: " + ", ".join(map(str, missing)))

    direct_buttons = bootstrap.count("AddComponent<Button>()")
    direct_handlers = bootstrap.count("onClick.AddListener(")
    if direct_buttons != direct_handlers:
        raise AssertionError(f"Direct buttons/listeners differ: {direct_buttons} buttons, {direct_handlers} listeners")
    if "button.onClick.AddListener(" not in ui_kit or "if (action == null)" not in ui_kit:
        raise AssertionError("UiKit.Button must register its action and disable buttons with missing callbacks")
    if "typeof(GraphicRaycaster)" not in bootstrap or "typeof(StandaloneInputModule)" not in bootstrap:
        raise AssertionError("Canvas raycasting or EventSystem input module is not configured")

    print(f"PASS: {len(buttons)} UiKit.Button calls have action callbacks.")
    print(f"PASS: all {direct_buttons} directly-created buttons have click listeners.")
    print("PASS: Canvas raycaster, EventSystem input module, and missing-callback safeguard are configured.")


if __name__ == "__main__":
    main()
