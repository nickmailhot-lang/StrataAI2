#!/usr/bin/env python3
"""Check declared SignalR routes against the edge's upgrade/forwarding boundary."""
import argparse
import re
from pathlib import Path

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument("--nginx-config", type=Path, default=root / "apps/web/nginx.conf")
args = parser.parse_args()
routes = set()
for source in (root / "src/StrataAI.Api").rglob("*.cs"):
    routes.update(re.findall(r'MapHub<[^>]+>\s*\(\s*"(/[^\"]+)"', source.read_text(encoding="utf-8")))
if not routes:
    raise SystemExit("No declared realtime routes found; coverage cannot be verified.")
config = args.nginx_config.read_text(encoding="utf-8")
locations = re.findall(r"location\s+\^~\s+(\S+)\s*\{([^{}]*)\}", config)
required = [
    r"proxy_pass\s+http://\$api_upstream\s*;",
    r"proxy_http_version\s+1\.1\s*;",
    r"proxy_set_header\s+Host\s+\$http_host\s*;",
    r"proxy_set_header\s+Upgrade\s+\$http_upgrade\s*;",
    r"proxy_set_header\s+Connection\s+\$work_upgrade_connection\s*;",
    r"proxy_set_header\s+X-Forwarded-Proto\s+\$scheme\s*;",
    r"proxy_buffering\s+off\s*;",
]
failures = []
for route in sorted(routes):
    candidates = [(prefix, body) for prefix, body in locations if route.startswith(prefix)]
    if not candidates:
        failures.append(f"{route}: no priority realtime proxy location")
        continue
    prefix, body = max(candidates, key=lambda location: len(location[0]))
    if not all(re.search(directive, body) for directive in required):
        failures.append(f"{route}: {prefix} lacks an upgrade, origin forwarding or unbuffered API proxy directive")
if failures:
    raise SystemExit("Realtime proxy coverage failed:\n" + "\n".join(failures))
print(f"Realtime proxy coverage passed for {len(routes)} declared hubs.")
