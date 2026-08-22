#!/bin/sh
if [ -z "$PLUGIN_ROOT" ]; then
  exit 0
fi
"$PLUGIN_ROOT/bin/win-x64/CodexQuotaTitle" --hook-launcher >/dev/null 2>&1
exit 0
