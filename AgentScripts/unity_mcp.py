"""Project-targeted stdio client for the locally installed Unity MCP relay."""
import argparse
import json
import os
from pathlib import Path
import queue
import subprocess
import threading


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("method", choices=["tools/list", "tools/call"])
    parser.add_argument("--params-file", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    relay = Path.home() / ".unity/relay/relay_win.exe"
    process = subprocess.Popen([str(relay), "--mcp", "--project-path", str(root)],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
        text=True, encoding="utf-8", creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    messages = queue.Queue()
    def read():
        for line in process.stdout:
            try:
                messages.put(json.loads(line))
            except ValueError:
                pass
    threading.Thread(target=read, daemon=True).start()
    def send(payload):
        process.stdin.write(json.dumps(payload) + "\n")
        process.stdin.flush()
    def request(identifier, method, params):
        send(dict(jsonrpc="2.0", id=identifier, method=method, params=params))
        while True:
            reply = messages.get(timeout=45)
            if reply.get("id") == identifier:
                if "error" in reply:
                    raise RuntimeError(reply["error"])
                return reply["result"]
    try:
        request(1, "initialize", dict(protocolVersion="2024-11-05", capabilities={},
            clientInfo=dict(name="campus-map-preflight", version=(root / "VERSION").read_text().strip())))
        send(dict(jsonrpc="2.0", method="notifications/initialized"))
        params = json.loads(args.params_file.read_text(encoding="utf-8")) if args.params_file else {}
        result = request(2, args.method, params)
        content = json.dumps(result, ensure_ascii=False, indent=2)
        if args.output:
            args.output.write_text(content, encoding="utf-8")
            print(f"Saved {args.output}")
        else:
            print(content)
    finally:
        process.terminate()
        process.wait(timeout=5)


if __name__ == "__main__":
    main()
