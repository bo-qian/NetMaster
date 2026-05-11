#!/bin/bash
# NetMaster 管理脚本
# 用法: ./ctl.sh {start|stop|restart|status|logs}

NETMASTER_DIR="${NETMASTER_DIR:-$HOME/.netmaster}"
SERVICE="netmaster.service"
LOG_DIR="$NETMASTER_DIR/logs"

case "$1" in
    start)
        systemctl --user start "$SERVICE"
        systemctl --user status "$SERVICE" --no-pager
        ;;
    stop)
        touch "$NETMASTER_DIR/stop.token"
        sleep 2
        systemctl --user stop "$SERVICE"
        ;;
    restart)
        systemctl --user restart "$SERVICE"
        systemctl --user status "$SERVICE" --no-pager
        ;;
    status)
        systemctl --user status "$SERVICE" --no-pager
        ;;
    logs)
        logfile="$LOG_DIR/$(date +%Y-%m-%d).log"
        [ -f "$logfile" ] && tail -f "$logfile" || echo "(暂无日志)"
        ;;
    *)
        echo "用法: $0 {start|stop|restart|status|logs}"
        exit 1
        ;;
esac
