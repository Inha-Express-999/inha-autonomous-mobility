"""Summarize CampusDriveTelemetry CSV without treating planned speed as measured speed."""
import argparse
import csv
import json
import math
from pathlib import Path


def percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    index = (len(ordered) - 1) * fraction
    lower = int(index)
    return ordered[lower] + (ordered[min(lower + 1, len(ordered) - 1)] - ordered[lower]) * (index - lower)


def summarize(path):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        raw = list(csv.DictReader(stream))
    rows = [{key: value if key == "status" else float(value) for key, value in row.items()} for row in raw]
    if len(rows) < 3:
        raise ValueError("At least three measured samples are required")
    if any(not math.isfinite(value) for row in rows for key, value in row.items() if key != "status"):
        raise ValueError("Non-finite telemetry")
    if any(b["elapsed_s"] <= a["elapsed_s"] for a, b in zip(rows, rows[1:])):
        raise ValueError("Telemetry time must increase")
    # Remove a centered one-second mean to describe short-period motion.
    # This is a diagnostic metric, not a calibrated ride-comfort pass threshold.
    residuals = {}
    camera_mode_known = all('camera_live_mode' in row for row in rows)
    invalid_camera = [0]
    for row in rows:
        valid = not camera_mode_known or (row['camera_live_mode'] == 1 and
            row.get('camera_follow_requested') == 1 and row.get('camera_blending') == 0)
        invalid_camera.append(invalid_camera[-1] + int(not valid))
    for field in ("y", "camera_y", "pitch_deg", "roll_deg", "camera_pitch_deg", "camera_roll_deg"):
        prefix = [0.0]
        for row in rows:
            prefix.append(prefix[-1] + row[field])
        left = right = 0
        values = []
        for row in rows:
            while rows[left]["elapsed_s"] < row["elapsed_s"] - .5:
                left += 1
            while right < len(rows) and rows[right]["elapsed_s"] <= row["elapsed_s"] + .5:
                right += 1
            # Exclude overview/blends and windows touching a mode change. Their
            # height jump describes intentional camera motion, not road vibration.
            if field.startswith('camera_') and invalid_camera[right] != invalid_camera[left]:
                values.append(None)
            else:
                values.append(row[field] - (prefix[right] - prefix[left]) / (right - left))
        residuals[field] = values
    bands = []
    for low, high in ((0, 5), (5, 30), (30, 40), (40, 50), (50, math.inf)):
        indices = [i for i, row in enumerate(rows) if low <= row["speed_kmh"] < high]
        if not indices:
            continue
        bands.append({
            "speed_band_kmh": [low, None if math.isinf(high) else high],
            "samples": len(indices),
            "camera_follow_samples": sum(residuals['camera_y'][i] is not None for i in indices),
            "all_wheels_airborne_samples": sum(rows[i]["grounded_wheels"] == 0 for i in indices),
            "lane_error_abs_p95_m": percentile([abs(rows[i]["lane_error_m"]) for i in indices], .95),
            "vertical_speed_abs_p95_mps": percentile([abs(rows[i]["vertical_mps"]) for i in indices], .95),
            "one_second_detrended_rms": {
                key: (math.sqrt(sum(values[i] ** 2 for i in indices if values[i] is not None) /
                    sum(values[i] is not None for i in indices))
                    if any(values[i] is not None for i in indices) else None)
                for key, values in residuals.items()
            },
        })
    distance = sum(math.hypot(b["x"] - a["x"], b["z"] - a["z"]) for a, b in zip(rows, rows[1:]))
    return {
        "source": str(path), "samples": len(rows),
        "elapsed_s": rows[-1]["elapsed_s"] - rows[0]["elapsed_s"],
        "horizontal_distance_m": distance,
        "peak_measured_speed_kmh": max(row["speed_kmh"] for row in rows),
        "largest_sample_gap_s": max(b["elapsed_s"] - a["elapsed_s"] for a, b in zip(rows, rows[1:])),
        "last_status": rows[-1]["status"], "speed_bands": bands,
        "camera_measurement_scope": "Stable vehicle-follow windows only" if camera_mode_known else "Camera mode unavailable; unfiltered legacy measurement",
        "limitation": "Measured run only. No automatic route-coverage or comfort pass/fail claim.",
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("csv", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = json.dumps(summarize(args.csv), ensure_ascii=False, indent=2, allow_nan=False)
    if args.output:
        args.output.write_text(result + "\n", encoding="utf-8")
    else:
        print(result)
