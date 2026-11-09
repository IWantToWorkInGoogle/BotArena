"""Check FIFO status and recovery after a failed job through the local API."""

from pathlib import Path
from time import monotonic, sleep

from smoke import request


def enqueue(code):
    status, job = request("/api/match-jobs", {"botCode": code, "opponentId": "defensive"})
    assert status == 202, (status, job)
    return f'/api/match-jobs/{job["id"]}'


def wait_for(path, expected):
    deadline = monotonic() + 8
    job = None
    while monotonic() < deadline:
        status, job = request(path)
        assert status == 200, (status, job)
        if job["state"] == expected:
            return job
        sleep(0.05)
    raise AssertionError(f"Job never reached {expected}: {job}")


if __name__ == "__main__":
    fixture = Path(__file__).with_name("strategies") / "worker-timeout.js"
    wait_code = fixture.read_text(encoding="utf-8")
    blocked = enqueue(wait_code)
    wait_for(blocked, "running")

    first = enqueue(wait_code)
    second = enqueue("function tick({ tick }) { if (tick === 2) throw Error(); return {}; }")
    assert request(first)[1]["position"] == 1
    assert request(second)[1]["position"] == 2

    failed = wait_for(blocked, "completed")
    assert "лимит в 5 секунд" in failed["error"], failed
    wait_for(first, "running")
    assert request(second)[1]["position"] == 1
    first_result = wait_for(first, "completed")
    assert "лимит в 5 секунд" in first_result["error"], first_result
    second_result = wait_for(second, "completed")
    assert second_result["match"]["error"]["tick"] == 2
    assert request(first)[1] == first_result
    print("FIFO queue positions, results and worker recovery passed")
