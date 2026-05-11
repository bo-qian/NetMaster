#!/usr/bin/env python3
"""NetMaster TUI — 终端可视化管理界面"""

import os
import sys
import time
import json
import subprocess
import datetime

import requests

WORK_DIR = os.environ.get("NETMASTER_DIR", os.path.expanduser("~/.netmaster"))
CONFIG_PATH = os.path.join(WORK_DIR, "daemon_config.json")
SERVICE = "netmaster.service"

CHECK_URL = "http://connect.rom.miui.com/generate_204"
LOGIN_URL = "http://10.10.9.9/eportal/InterFace.do?method=login"

C_RESET = "\033[0m"
C_BOLD = "\033[1m"
C_DIM = "\033[2m"
C_RED = "\033[31m"
C_GREEN = "\033[32m"
C_YELLOW = "\033[33m"
C_BLUE = "\033[34m"
C_CYAN = "\033[36m"
C_BG_BLACK = "\033[40m"


def clear():
    print("\033[2J\033[H", end="")


def box(text: str, color: str = C_GREEN):
    lines = text.strip().split("\n")
    width = max(len(l) for l in lines) + 4
    print(f"{color}{C_BOLD}┌{'─' * width}┐")
    for l in lines:
        print(f"│  {l}{' ' * (width - len(l) - 2)}│")
    print(f"└{'─' * width}┘{C_RESET}")


def banner():
    W = 40  # 可视宽度（含边框）
    top = "╔" + "═" * (W - 2) + "╗"
    bot = "╚" + "═" * (W - 2) + "╝"

    def dsp(w):  # 计算字符串的可视宽度（中文=2）
        n = 0
        for c in w:
            n += 2 if '一' <= c <= '鿿' or '　' <= c <= '〿' or '＀' <= c <= '￯' else 1
        return n

    def row(text):
        pad = W - 2 - dsp(text)
        left = pad // 2
        right = pad - left
        return "║" + " " * left + text + " " * right + "║"

    print(f"{C_BOLD}{C_CYAN}")
    print(top)
    print(row("NetMaster Auto-Guardian"))
    print(row("校园网自动守护 · Linux"))
    print(bot)
    print(C_RESET)


def osd():
    """Check service status"""
    try:
        r = subprocess.run(
            ["systemctl", "--user", "status", SERVICE, "--no-pager"],
            capture_output=True, text=True, timeout=5
        )
        return r.stdout
    except Exception:
        return ""


def net_ok():
    try:
        return requests.get(CHECK_URL, timeout=3).status_code == 204
    except Exception:
        return False


def last_logs(n=5):
    today = datetime.datetime.now().strftime("%Y-%m-%d")
    logfile = os.path.join(WORK_DIR, "logs", f"{today}.log")
    if not os.path.exists(logfile):
        return ["(暂无日志)"]
    with open(logfile, "r") as f:
        lines = f.readlines()
    return [l.strip() for l in lines[-n:]] if lines else ["(暂无日志)"]


def test_login():
    if not os.path.exists(CONFIG_PATH):
        return "fail", "配置文件不存在"
    with open(CONFIG_PATH, "r") as f:
        cfg = json.load(f)
    try:
        res = requests.post(
            LOGIN_URL,
            data=cfg["login_payload"],
            headers=cfg.get("headers", {}),
            timeout=10
        ).json()
        if res.get("result") == "success":
            return "ok", "登录成功"
        msg = res.get("message", "") or str(res)
        if "已经在线" in str(msg):
            return "ok", "账号已在线"
        return "fail", f"服务器返回: {msg}"
    except Exception as e:
        return "fail", str(e)


def draw_status():
    svc_text = osd()
    is_running = "active (running)" in svc_text or "Active: active" in svc_text
    is_enabled = "enabled;" in svc_text
    online = net_ok()
    logs = last_logs(4)

    print()
    # 状态栏
    bar_w = 38
    print(f"{C_BOLD}  {'─' * bar_w}{C_RESET}")

    # Service status
    if is_running:
        svc_icon = f"{C_GREEN}●{C_RESET}"
        svc_text2 = "运行中"
    else:
        svc_icon = f"{C_RED}●{C_RESET}"
        svc_text2 = "未运行"

    boot_icon = f"{C_GREEN}✓{C_RESET}" if is_enabled else f"{C_DIM}✗{C_RESET}"
    boot_text = "开机自启" if is_enabled else "手动启动"

    print(f"  {svc_icon} 服务状态: {C_BOLD}{svc_text2}{C_RESET}      {boot_icon} 启动方式: {boot_text}")

    # Network status
    if online:
        print(f"  {C_GREEN}●{C_RESET} 网络状态: {C_BOLD}已联网{C_RESET}")
    else:
        print(f"  {C_RED}●{C_RESET} 网络状态: {C_RED}已断网{C_RESET}")

    # Config
    if os.path.exists(CONFIG_PATH):
        with open(CONFIG_PATH, "r") as f:
            cfg = json.load(f)
        uid = "未识别"
        try:
            uid = cfg["login_payload"].split("userId=")[1].split("&")[0]
        except Exception:
            pass
        print(f"  {C_GREEN}●{C_RESET} 配置文件: 已就绪 (账号: {C_BOLD}{uid}{C_RESET})")
    else:
        print(f"  {C_RED}●{C_RESET} 配置文件: 未创建")

    print(f"{C_BOLD}  {'─' * bar_w}{C_RESET}")

    # Recent logs
    print(f"\n  {C_DIM}最近日志:{C_RESET}")
    for l in logs:
        print(f"  {C_DIM}│{C_RESET} {l[:100]}")

    print()


def menu():
    items = [
        ("1", "启动守护", "start"),
        ("2", "停止守护", "stop"),
        ("3", "重启守护", "restart"),
        ("4", "实时日志", "logs"),
        ("5", "测试登录", "test"),
        ("6", "抓取凭证", "capture"),
        ("q", "退出", "quit"),
    ]
    print(f"\n  {C_BOLD}操作:{C_RESET}")
    for key, label, _ in items:
        c = C_GREEN if key.isdigit() else C_YELLOW
        print(f"  {c}{C_BOLD}[{key}]{C_RESET} {label}  ", end="")
    print("\n")
    return dict((k, a) for k, _, a in items)


def do_start():
    subprocess.run(["systemctl", "--user", "start", SERVICE], capture_output=True)
    time.sleep(1)


def do_stop():
    with open(os.path.join(WORK_DIR, "stop.token"), "w") as f:
        f.write("stop")
    time.sleep(2)
    subprocess.run(["systemctl", "--user", "stop", SERVICE], capture_output=True)


def do_restart():
    subprocess.run(["systemctl", "--user", "restart", SERVICE], capture_output=True)
    time.sleep(1)


def do_test():
    print(f"\n  {C_YELLOW}正在测试登录...{C_RESET}")
    status, msg = test_login()
    if status == "ok":
        print(f"  {C_GREEN}✓ {msg}{C_RESET}")
    else:
        print(f"  {C_RED}✗ {msg}{C_RESET}")
    input(f"\n  {C_DIM}按 Enter 返回...{C_RESET}")


def do_logs():
    print(f"\n  {C_DIM}实时日志 (Ctrl+C 退出)...{C_RESET}\n")
    today = datetime.datetime.now().strftime("%Y-%m-%d")
    logfile = os.path.join(WORK_DIR, "logs", f"{today}.log")
    if os.path.exists(logfile):
        try:
            subprocess.run(["tail", "-f", logfile])
        except KeyboardInterrupt:
            pass
    else:
        print("  (暂无日志文件)")


def do_capture():
    script = os.path.join(WORK_DIR, "capture_payload.py")
    if os.path.exists(script):
        subprocess.run([sys.executable, script])
    else:
        print(f"  {C_RED}抓包脚本不存在: {script}{C_RESET}")
    input(f"\n  {C_DIM}按 Enter 返回...{C_RESET}")


def main():
    while True:
        clear()
        banner()
        draw_status()
        actions = menu()

        try:
            choice = input(f"  {C_BOLD}>>> {C_RESET}").strip()
        except (EOFError, KeyboardInterrupt):
            print()
            sys.exit(0)

        action = actions.get(choice)
        if not action:
            continue
        if action == "quit":
            print()
            sys.exit(0)
        elif action == "start":
            do_start()
        elif action == "stop":
            do_stop()
        elif action == "restart":
            do_restart()
        elif action == "test":
            do_test()
        elif action == "logs":
            do_logs()
        elif action == "capture":
            do_capture()


if __name__ == "__main__":
    main()
