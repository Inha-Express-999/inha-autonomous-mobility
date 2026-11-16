"""Summarize recorded trials without re-running Unity."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent


def elapsed(line):
    return float(re.search(r"elapsed=([\d.]+)", line)[1])


rows = []
for n in range(1, 4):
    folder = root / f"run-{n}"
    result = folder / "result.txt"
    row = {"trial": n, "result": result.read_text(encoding="utf-8-sig") if result.exists() else "INCOMPLETE"}
    summary = folder / "summary.json"
    if summary.exists():
        data = json.loads(summary.read_text(encoding="utf-8"))
        row.update({k: data[k] for k in ("run_id", "request_id", "vehicle_id", "checks", "final_goal_distance_m")})
        row["evidence_result"] = data["result"]
        events = (folder / "events.txt").read_text(encoding="utf-8-sig").splitlines()
        observations = (folder / "observations.txt").read_text(encoding="utf-8-sig").splitlines()
        timeline = (folder / "timeline.txt").read_text(encoding="utf-8-sig")
        generations = [int(x) for x in re.findall(r"generation=(\d+)", timeline)]
        row["max_connection_generation"] = max(generations)
        row["single_disconnect_condition_met"] = max(generations) == 2
        disconnect = elapsed(next(x for x in events if x.startswith("disconnect_click ")))
        restore = elapsed(next(x for x in events if x.startswith("restore_click ")))
        held = elapsed(next(x for x in observations if x.startswith("02-held ")))
        connected = elapsed(next(x for x in observations if x.startswith("03-restored ")))
        home = elapsed(next(x for x in observations if x.startswith("04-completed-home ")))
        row.update(stop_observed_after_click_s=round(held-disconnect, 3),
                   connected_observed_after_restore_s=round(connected-restore, 3),
                   resume_observed_after_restore_s=round(float(data["first_resumed_sample"]["elapsed"])-restore, 3),
                   home_observed_elapsed_s=home)
    rows.append(row)
output = {"scope": "Three independent Play sessions, same passenger route, normal FIFO vehicle selection",
          "timing_limits": "Editor wall time rounded to 0.1 s; event/stage polling >=0.2 s, timeline >=1 s. Observed delays, not exact braking latency.",
          "passed": sum(r.get("evidence_result") == "PASS" for r in rows), "requested_trials": 3, "trials": rows}
(root / "summary.json").write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(output, ensure_ascii=False, indent=2))
