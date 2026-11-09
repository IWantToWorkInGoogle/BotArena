"""Translate newline-delimited observations into actions inside WASI."""

import json
import math
import sys

read = sys.stdin.readline
write = sys.stdout.write
# Ordinary print() calls must not interfere with the protocol.
sys.stdout = sys.stderr
dumps = json.dumps
loads = json.loads
strategy_file = "strategy.py"


def send(message):
    write(dumps(message, allow_nan=False, separators=(",", ":")) + "\n")


def failure(error):
    line = error.lineno if isinstance(error, SyntaxError) else None
    column = error.offset if isinstance(error, SyntaxError) else None
    if line is None:
        frame = error.__traceback__
        while frame:
            if frame.tb_frame.f_code.co_filename == strategy_file:
                line = frame.tb_lineno
            frame = frame.tb_next
    send({"error": "Ошибка Python (" + type(error).__name__[:80] + ").", "line": line, "column": column})


def finite(value):
    return type(value) in (int, float) and math.isfinite(value)


def action_from(value):
    if not isinstance(value, dict):
        return None
    left = value.get("leftTrack", 0)
    right = value.get("rightTrack", 0)
    aim = value.get("aimDirection", {"x": 0, "y": 0})
    shoot = value.get("shoot", False)
    if not finite(left) or not finite(right) or not isinstance(aim, dict) or type(shoot) is not bool:
        return None
    x, y = aim.get("x"), aim.get("y")
    if not finite(x) or not finite(y):
        return None
    return {"leftTrack": min(1, max(-1, left)), "rightTrack": min(1, max(-1, right)),
            "aimDirection": {"x": x, "y": y}, "shoot": shoot}


scope = {"__name__": "__strategy__"}
try:
    exec(compile(loads(read()), strategy_file, "exec"), scope)
    tick = scope.get("tick")
    if not callable(tick):
        send({"error": "Объявите функцию tick(state)."})
        sys.exit(0)
except Exception as error:
    failure(error)
    sys.exit(0)

send({"ready": True})
while request := read():
    try:
        action = action_from(tick(loads(request)))
        if action is None:
            send({"error": "tick(state) должна вернуть словарь с числовыми командами и логическим shoot."})
        else:
            send({"action": action})
    except Exception as error:
        failure(error)
        break
