"""Stand-in emulator for automated launch tests.

Usage: python fake_emulator.py --record OUT.json [--exit-code N] [--sleep S] -g GAME
Records the arguments and working directory it received, then exits.
"""
import json
import os
import sys
import time


def main() -> int:
    argv = sys.argv[1:]
    record = None
    exit_code = 0
    sleep_s = 0.0
    rest = []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--record":
            record = argv[i + 1]
            i += 2
        elif a == "--exit-code":
            exit_code = int(argv[i + 1])
            i += 2
        elif a == "--sleep":
            sleep_s = float(argv[i + 1])
            i += 2
        else:
            rest.append(a)
            i += 1
    game = rest[rest.index("-g") + 1] if "-g" in rest else None
    if record:
        with open(record, "w", encoding="utf-8") as f:
            json.dump({"args": rest, "game": game, "game_exists": bool(game and os.path.isfile(game)),
                       "cwd": os.getcwd()}, f)
    time.sleep(sleep_s)
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
