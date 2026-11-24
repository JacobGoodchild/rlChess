"""Freeze a copy of one of your bots, so this version stays playable while you keep improving it.

Usage (from the repository root):

    python3 tools/freeze-bot.py Minimax v1

This copies   src/RlChess.Players/Bots/Minimax/   (all files, e.g. .cs and model files)
to            src/RlChess.Players/Frozen/Minimax_v1/
and renames the namespace inside the .cs files from
              RlChess.Players.Bots.Minimax   to   RlChess.Players.Frozen.Minimax_v1
so the copy and the live bot can exist side by side. It then prints the one line to add to
PlayerRegistry.cs. Nothing else is changed. A bot can be frozen as often as you like (v1, v2, ...).

Rule of thumb: keep everything a bot needs inside its own folder, so the frozen copy really
doesn't change when you edit the live bot.
"""
import os
import re
import shutil
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
BOTS = os.path.join(ROOT, "src", "RlChess.Players", "Bots")
FROZEN = os.path.join(ROOT, "src", "RlChess.Players", "Frozen")


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 1
    name, version = sys.argv[1], sys.argv[2]
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
        print(f"'{name}' should be the bot's folder name, e.g. Minimax")
        return 1
    if not re.fullmatch(r"[A-Za-z0-9_.]+", version):
        print(f"'{version}' should be a short label like v1 or v2")
        return 1
    source = os.path.join(BOTS, name)
    if not os.path.isdir(source):
        print(f"No folder {os.path.relpath(source, ROOT)}. Bots live in src/RlChess.Players/Bots/<Name>/.")
        return 1
    safe_version = version.replace(".", "_")
    frozen_name = f"{name}_{safe_version}"
    target = os.path.join(FROZEN, frozen_name)
    if os.path.exists(target):
        print(f"{os.path.relpath(target, ROOT)} already exists: pick a new version label.")
        return 1

    shutil.copytree(source, target, ignore=shutil.ignore_patterns("bin", "obj"))
    old_ns = f"RlChess.Players.Bots.{name}"
    new_ns = f"RlChess.Players.Frozen.{frozen_name}"
    changed = 0
    for folder, _, files in os.walk(target):
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(folder, f)
            with open(path, encoding="utf-8-sig") as fh:
                text = fh.read()
            new_text = re.sub(rf"\b{re.escape(old_ns)}\b", new_ns, text)
            if new_text != text:
                changed += 1
            header = f"// Frozen copy of {name} ({version}). Don't edit: improve the live bot in Bots/{name} instead.\n"
            with open(path, "w", encoding="utf-8") as fh:
                fh.write(header + new_text)

    print(f"Frozen {name} as {os.path.relpath(target, ROOT)} ({changed} file(s) had their namespace renamed).")
    print()
    print("Now add a line like this to src/RlChess.Players/PlayerRegistry.cs (copy your live bot's line and change")
    print("the id, the class's namespace and add the version):")
    print()
    print(f'    new("{name.lower()}-{safe_version.lower()}", "{name}", "Frozen copy.", PlayerCategory.Computer,')
    print(f'        level => new Frozen.{frozen_name}.YourPlayerClass(/* same arguments as the live bot */), Version: "{version}"),')
    return 0


if __name__ == "__main__":
    sys.exit(main())
