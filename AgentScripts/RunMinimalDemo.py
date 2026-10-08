"""Run the browser synthetic pickup-to-dropoff demo locally."""

import argparse
import threading
import time
import urllib.error
import urllib.request
import webbrowser
from pathlib import Path

import uvicorn
from campus_sim.api import create_app

ROOT = Path(__file__).resolve().parents[1]


def open_when_ready(url: str) -> None:
    for _ in range(100):
        try:
            with urllib.request.urlopen(url, timeout=1) as response:
                if response.status == 200:
                    webbrowser.open(url)
                    return
        except (OSError, urllib.error.URLError):
            time.sleep(0.1)


def main() -> None:
    parser = argparse.ArgumentParser(description="Run the local six-stop synthetic trip demo")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--no-browser", action="store_true")
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        parser.error("port must be between 1024 and 65535")
    app = create_app(
        map_path=ROOT / "maps" / "fixtures" / "campus-synthetic-6.json",
        minimal_demo=True,
    )
    url = f"http://127.0.0.1:{args.port}/demo"
    print(f"Minimal synthetic demo: {url}", flush=True)
    print("Press Ctrl+C here to stop the server.", flush=True)
    if not args.no_browser:
        threading.Thread(target=open_when_ready, args=(url,), daemon=True).start()
    uvicorn.run(app, host="127.0.0.1", port=args.port, reload=False, log_level="warning")


if __name__ == "__main__":
    main()
