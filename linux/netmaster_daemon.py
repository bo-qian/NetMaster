#!/usr/bin/env python3
"""NetMaster Linux Daemon — 校园网自动重连后台脚本"""

import os
import sys
import time
import json
import signal
import datetime

import requests

WORK_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(WORK_DIR, "daemon_config.json")
STOP_TOKEN = os.path.join(WORK_DIR, "stop.token")
LOG_DIR = os.path.join(WORK_DIR, "logs")
LOG_FILE = os.path.join(LOG_DIR, "netmaster.log")
MAX_LOG_SIZE = 1 * 1024 * 1024  # 1MB 自动裁剪
KEEP_LINES = 3000

LOGIN_URL = "http://10.10.9.9/eportal/InterFace.do?method=login"
CHECK_URL = "http://connect.rom.miui.com/generate_204"


def log(msg: str):
    os.makedirs(LOG_DIR, exist_ok=True)
    ts = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    line = f"[{ts}] {msg}"
    try:
        with open(LOG_FILE, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except Exception:
        pass
    print(line)


def rotate_log():
    """日志文件超过 MAX_LOG_SIZE 时保留末尾 KEEP_LINES 行"""
    try:
        if os.path.exists(LOG_FILE) and os.path.getsize(LOG_FILE) > MAX_LOG_SIZE:
            with open(LOG_FILE, "r", encoding="utf-8") as f:
                lines = f.readlines()
            if len(lines) > KEEP_LINES:
                with open(LOG_FILE, "w", encoding="utf-8") as f:
                    f.writelines(lines[-KEEP_LINES:])
    except Exception:
        pass


def check_internet() -> bool:
    try:
        return requests.get(CHECK_URL, timeout=5).status_code == 204
    except Exception:
        return False


def do_login(cfg: dict):
    log("断网，尝试登录...")
    try:
        headers = cfg.get("headers", {
            "User-Agent": "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36",
            "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
            "Origin": "http://10.10.9.9",
            "Referer": "http://10.10.9.9/eportal/index.jsp",
        })
        payload = cfg["login_payload"]
        resp = requests.post(LOGIN_URL, data=payload, headers=headers, timeout=10)
        res = json.loads(resp.content)
        if res.get("result") == "success":
            log(">>> 登录成功")
        else:
            msg = res.get("message", "") or str(res)
            log(f"登录失败: {msg}")
    except Exception as e:
        log(f"登录错误: {e}")


def handle_stop(signum=None, frame=None):
    log("收到退出信号")
    with open(STOP_TOKEN, "w") as f:
        f.write("stop")
    sys.exit(0)


def main():
    signal.signal(signal.SIGTERM, handle_stop)
    signal.signal(signal.SIGINT, handle_stop)

    if not os.path.exists(CONFIG_PATH):
        print(f"配置文件不存在: {CONFIG_PATH}")
        print("请创建 daemon_config.json，格式见示例。")
        sys.exit(2)

    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    check_interval = int(cfg.get("check_interval", 20))

    log(f"NetMaster 守护进程启动 (间隔: {check_interval}s)")

    # 启动即检查一次
    if os.path.exists(STOP_TOKEN):
        os.remove(STOP_TOKEN)
        log("清除残留停止标记")

    if not check_internet():
        do_login(cfg)

    while True:
        if os.path.exists(STOP_TOKEN):
            log("收到停止指令，退出。")
            os.remove(STOP_TOKEN)
            break

        try:
            if not check_internet():
                do_login(cfg)
                time.sleep(5)
                if check_internet():
                    log("网络已恢复")
            else:
                rotate_log()
        except Exception as e:
            log(f"Err: {e}")

        # 分片 sleep，让停止信号能快速响应
        for _ in range(max(1, check_interval)):
            time.sleep(1)
            if os.path.exists(STOP_TOKEN):
                break


if __name__ == "__main__":
    main()
