#!/bin/bash
# DeskMeter(macOS) 安装 + 开机启动（LaunchAgent，登录即运行，无 Dock 图标）
# 用法:
#   ./scripts/install-macos.sh                 # 用 ./dist/DeskMeter.app
#   ./scripts/install-macos.sh /path/App.app   # 指定 app
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SRC="${1:-$REPO_DIR/dist/DeskMeter.app}"
APP_DEST="/Applications/DeskMeter.app"
LABEL="io.deskmeter"
LAUNCH_DIR="$HOME/Library/LaunchAgents"
PLIST="$LAUNCH_DIR/$LABEL.plist"

if [ ! -d "$SRC" ]; then
  echo "错误: 找不到 $SRC（请先运行 ./scripts/package-macos.sh）" >&2
  exit 1
fi

# 1) 停掉正在运行的旧实例
pkill -x DeskMeterMac 2>/dev/null || true

# 2) 拷贝到 /Applications（保留权限/签名）
echo "== 安装到 $APP_DEST =="
rm -rf "$APP_DEST"
ditto "$SRC" "$APP_DEST"
echo "已安装。"

# 3) 注册 LaunchAgent（登录自启）
echo "== 注册开机启动 $PLIST =="
mkdir -p "$LAUNCH_DIR"
cat > "$PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>io.deskmeter</string>
  <key>ProgramArguments</key>
  <array>
    <string>$APP_DEST/Contents/MacOS/DeskMeterMac</string>
  </array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><false/>
  <key>ProcessType</key><string>Interactive</string>
  <key>StandardOutPath</key><string>/tmp/deskmeter.log</string>
  <key>StandardErrorPath</key><string>/tmp/deskmeter.err.log</string>
</dict>
</plist>
PLIST

# 4) 加载
launchctl bootout "gui/$(id -u)" "$PLIST" 2>/dev/null || true
if launchctl bootstrap "gui/$(id -u)" "$PLIST" 2>/dev/null; then
  echo "已用 launchctl bootstrap 加载。"
else
  launchctl load -w "$PLIST"
  echo "已用 launchctl load -w 加载。"
fi

# 5) RunAtLoad 在 bootstrap 时已拉起一次；若没起来用 kickstart 拉一次（不再手动 open，避免双实例）
sleep 1
launchctl kickstart "gui/$(id -u)/$LABEL" 2>/dev/null || true
echo "== 完成 =="
echo "DeskMeter 已安装并注册开机启动，应立即出现在桌面右上角。"
echo "日志: /tmp/deskmeter.log（错误: /tmp/deskmeter.err.log）"
echo "卸载提示见 docs/MIGRATION_MAC.md 或执行: launchctl bootout gui/$(id -u) $PLIST; rm -f $PLIST; rm -rf $APP_DEST"
