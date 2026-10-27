#!/usr/bin/env bash
# The packaged FbStudio answers as an MCP server: «FbStudio mcp» over stdin/stdout, with no window (F-37, D-114).
# Usage: tools/mcp-smoke.sh <FbStudio executable>
set -euo pipefail
exe="$1"
data="$(mktemp -d)"
init='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"ci","version":"1"}}}'
# stdin stays open for a while: the server stops when it closes.
reply="$( (printf '%s\n' "$init"; sleep 15) | "$exe" mcp --data "$data" | head -n 1 )"
rm -rf "$data"
echo "$reply"
grep -q '"serverInfo":{"name":"fbstudio"' <<< "$reply"
