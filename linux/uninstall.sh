#!/bin/bash
# NetMaster Linux 一键卸载脚本
set -e

INSTALL_DIR="$HOME/.netmaster"
SERVICE="netmaster.service"

echo "╔══════════════════════════════════════╗"
echo "║   NetMaster Linux 一键卸载          ║"
echo "╚══════════════════════════════════════╝"
echo ""

# 确认
echo "将执行以下操作:"
echo "  1. 停止并禁用 systemd 服务"
echo "  2. 删除 systemd 服务文件"
echo "  3. 删除 ~/.netmaster/ 目录（含配置和日志）"
echo "  4. 删除 netmaster 命令别名"
echo ""
read -p "确认卸载? (输入 yes 继续): " confirm
if [ "$confirm" != "yes" ]; then
    echo "已取消"
    exit 0
fi

# 1. 停止并禁用服务
echo "[1/4] 停止服务..."
systemctl --user stop "$SERVICE" 2>/dev/null || true
systemctl --user disable "$SERVICE" 2>/dev/null || true
echo "      ✓ 已停止"

# 2. 删除服务文件
echo "[2/4] 删除服务文件..."
rm -f "$HOME/.config/systemd/user/$SERVICE"
systemctl --user daemon-reload
echo "      ✓ 已删除"

# 3. 删除安装目录
echo "[3/4] 删除数据目录..."
if [ -d "$INSTALL_DIR" ]; then
    rm -rf "$INSTALL_DIR"
    echo "      ✓ 已删除 $INSTALL_DIR"
else
    echo "      (目录不存在，跳过)"
fi

# 4. 删除 alias
echo "[4/4] 删除命令别名..."
rm -f "$HOME/.bashrc.d/netmaster"
# 如果 bashrc.d 为空则删除目录
rmdir "$HOME/.bashrc.d" 2>/dev/null || true
echo "      ✓ 已删除"

echo ""
echo "╔══════════════════════════════════════╗"
echo "║  卸载完成！                         ║"
echo "║  执行 source ~/.bashrc 使其生效     ║"
echo "╚══════════════════════════════════════╝"
