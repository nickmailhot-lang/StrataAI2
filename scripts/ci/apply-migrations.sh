#!/usr/bin/env bash
set -euo pipefail

scripts/migration-stream.sh | psql -X -v ON_ERROR_STOP=1
