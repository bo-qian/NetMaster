#!/bin/bash
# NetMaster Linux 一键卸载脚本
set -e

INSTALL_DIR="$HOME/.netmaster"
SERVICE="netmaster.service"

# 绘制对齐的方框（正确处理 CJK 字符显示宽度）
print_box() {
    python3 << 'PYEOF'
import sys
import unicodedata
text = sys.stdin.read().rstrip('\n')
if not text:
    sys.exit(0)
def dw(s):
    return sum(2 if unicodedata.east_asian_width(c) in ('W','F') else 1 for c in s)
lines = text.split('\n')
width = max(dw(l) for l in lines) + 6
print('╔' + '═' * width + '╗')
for l in lines:
    pad = width - dw(l) - 2
    print('║  ' + l + ' ' * pad + ' ║')
print('╚' + '═' * width + '╝')
PYEOF
}

print_box << 'ENDOFBOX'
NetMaster Linux 一键卸载
ENDOFBOX
echo ""

# 确认
echo "将执行以下操作:"
echo "  1. 停止并禁用 systemd 服务"
echo "  2. 删除 systemd 服务文件"
echo "  3. 删除 ~/.netmaster/ 目录（含配置和日志）"
echo "  4. 删除 netmaster 命令别名"
echo ""
read -p "确认卸载? (输入 yes 继续): " confirm
confirm_lower=$(echo "$confirm" | tr '[:upper:]' '[:lower:]')
if [ "$confirm_lower" != "yes" ] && [ "$confirm_lower" != "y" ]; then
    echo "已取消"
    exit 0
fi

# 1. 停止并禁用服务
echo "[1/5] 停止服务..."
systemctl --user stop "$SERVICE" 2>/dev/null || true
systemctl --user disable "$SERVICE" 2>/dev/null || true
echo "      ✓ 已停止"

# 2. 删除服务文件
echo "[2/5] 删除服务文件..."
rm -f "$HOME/.config/systemd/user/$SERVICE"
systemctl --user daemon-reload
echo "      ✓ 已删除"

# 3. 删除安装目录
echo "[3/5] 删除数据目录..."
if [ -d "$INSTALL_DIR" ]; then
    rm -rf "$INSTALL_DIR"
    echo "      ✓ 已删除 $INSTALL_DIR"
else
    echo "      (目录不存在，跳过)"
fi

# 4. 删除 alias
echo "[4/5] 删除命令别名..."
rm -f "$HOME/.bashrc.d/netmaster"
# 如果 bashrc.d 为空则删除目录
rmdir "$HOME/.bashrc.d" 2>/dev/null || true
echo "      ✓ 已删除"

# 5. 清理 .bashrc 中的自动加载代码
echo "[5/5] 清理 shell 配置..."
if [ -f "$HOME/.bashrc" ]; then
    sed -i '/^# Auto-added by NetMaster installer$/,/^fi$/d' "$HOME/.bashrc"
    echo "      ✓ 已清理"
fi

echo ""
print_box << 'ENDOFBOX'
卸载完成！
ENDOFBOX
echo ""
source ~/.bashrc 2>/dev/null || true
