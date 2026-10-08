"""One owned Python process for Unity Editor; persist errors without a terminal window."""
import argparse
import os
import sys
import traceback
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error("port must be between 1 and 65535")
    root = Path(__file__).resolve().parents[1]
    os.chdir(root)
    sys.path.insert(0, str(root / "backend" / "src"))
    folder = root / "artifacts" / "editor-server"
    folder.mkdir(parents=True, exist_ok=True)
    with (folder / f"server-{args.port}.log").open("a", encoding="utf-8", buffering=1) as log:
        original_stdout, original_stderr = sys.stdout, sys.stderr
        sys.stdout = sys.stderr = log
        try:
            print("\nUnity Editor development server starting", flush=True)
            sys.argv = ["campus-sim", "serve", "--host", "0.0.0.0", "--port", str(args.port)]
            from campus_sim.cli import main as serve

            serve()
        except Exception:  # noqa: BLE001 - preserve any startup traceback in the hidden process log
            traceback.print_exc(file=log)
            raise SystemExit(1)
        finally:
            sys.stdout, sys.stderr = original_stdout, original_stderr


if __name__ == "__main__":
    main()
