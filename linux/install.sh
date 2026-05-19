#!/bin/bash
# NetMaster Linux 一键安装脚本
set -e

INSTALL_DIR="$HOME/.netmaster"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "╔══════════════════════════════════════╗"
echo "║   NetMaster Linux 一键安装          ║"
echo "╚══════════════════════════════════════╝"
echo ""

# 1. 检查依赖
echo "[1/5] 检查依赖..."
if ! python3 -c "import requests" 2>/dev/null; then
    echo "      安装 requests..."
    pip3 install requests
fi

# 检查 geckodriver (抓包需要)
if ! command -v geckodriver &>/dev/null && [ ! -f "$INSTALL_DIR/geckodriver" ]; then
    echo "      下载 geckodriver..."
    GECKO_URL="https://github.com/mozilla/geckodriver/releases/download/v0.36.0/geckodriver-v0.36.0-linux64.tar.gz"
    mkdir -p "$INSTALL_DIR"
    curl -sL "$GECKO_URL" | tar xz -C "$INSTALL_DIR"
    chmod +x "$INSTALL_DIR/geckodriver"
fi

echo "      ✓ 依赖就绪"

# 2. 创建安装目录
echo "[2/5] 创建目录..."
mkdir -p "$INSTALL_DIR/logs"

# 3. 复制文件
echo "[3/5] 复制脚本..."
cp "$SCRIPT_DIR/netmaster_daemon.py" "$INSTALL_DIR/"
cp "$SCRIPT_DIR/capture_payload.py" "$INSTALL_DIR/"
cp "$SCRIPT_DIR/netmaster_tui.py" "$INSTALL_DIR/"
cp "$SCRIPT_DIR/ctl.sh" "$INSTALL_DIR/"
cp "$SCRIPT_DIR/uninstall.sh" "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/netmaster_daemon.py"
chmod +x "$INSTALL_DIR/capture_payload.py"
chmod +x "$INSTALL_DIR/netmaster_tui.py"
chmod +x "$INSTALL_DIR/ctl.sh"

# 配置模板
if [ ! -f "$INSTALL_DIR/daemon_config.json" ]; then
    cp "$SCRIPT_DIR/daemon_config.example.json" "$INSTALL_DIR/daemon_config.json"
    echo "      ✓ 已创建配置模板"
else
    echo "      ✓ 配置文件已存在，跳过"
fi

# 4. 安装 systemd 服务
echo "[4/5] 安装 systemd 服务..."
mkdir -p "$HOME/.config/systemd/user"

# 生成适配当前路径的服务文件
sed "s|REPLACE_PYTHON|$(which python3)|g; s|REPLACE_WORKDIR|$INSTALL_DIR|g" \
    "$SCRIPT_DIR/netmaster.service" > "$HOME/.config/systemd/user/netmaster.service"

systemctl --user daemon-reload
systemctl --user enable netmaster.service 2>/dev/null || true

# 5. 设置 alias
echo "[5/5] 设置终端命令..."

# 确保 ~/.bashrc 会加载 ~/.bashrc.d/ (并非所有发行版默认支持)
if [ -f "$HOME/.bashrc" ]; then
    if ! grep -q "bashrc\.d" "$HOME/.bashrc" 2>/dev/null; then
        echo "" >> "$HOME/.bashrc"
        echo "# Auto-added by NetMaster installer" >> "$HOME/.bashrc"
        echo "if [ -d ~/.bashrc.d ]; then" >> "$HOME/.bashrc"
        echo "    for rc in ~/.bashrc.d/*; do" >> "$HOME/.bashrc"
        echo "        if [ -f \"\$rc\" ]; then" >> "$HOME/.bashrc"
        echo "            . \"\$rc\"" >> "$HOME/.bashrc"
        echo "        fi" >> "$HOME/.bashrc"
        echo "    done" >> "$HOME/.bashrc"
        echo "fi" >> "$HOME/.bashrc"
        echo "      ✓ 已启用 ~/.bashrc.d/ 支持"
    fi
fi

mkdir -p "$HOME/.bashrc.d"
echo "alias netmaster='python3 $INSTALL_DIR/netmaster_tui.py'" > "$HOME/.bashrc.d/netmaster"

echo ""
echo "╔══════════════════════════════════════╗"
echo "║  安装完成！                         ║"
echo "║  请执行 source ~/.bashrc 后         ║"
echo "║  输入 netmaster 打开控制面板        ║"
echo "╚══════════════════════════════════════╝"
