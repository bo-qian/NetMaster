#!/usr/bin/env python3
"""NetMaster TUI — 终端可视化管理界面"""

import os
import sys
import time
import json
import shutil
import subprocess
import datetime
import unicodedata

import requests

WORK_DIR = os.environ.get("NETMASTER_DIR", os.path.expanduser("~/.netmaster"))
CONFIG_PATH = os.path.join(WORK_DIR, "daemon_config.json")
LOG_FILE = os.path.join(WORK_DIR, "logs", "netmaster.log")
SERVICE = "netmaster.service"

CHECK_URL = "http://connect.rom.miui.com/generate_204"
LOGIN_URL = "http://10.10.9.9/eportal/InterFace.do?method=login"
REPO_URL = "https://github.com/bo-qian/NetMaster"

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


def display_width(s: str) -> int:
    """计算字符串的终端显示宽度，CJK 字符和中文标点算 2 列"""
    w = 0
    for c in s:
        w += 2 if unicodedata.east_asian_width(c) in ('W', 'F') else 1
    return w


def box(text: str, color: str = C_GREEN):
    lines = text.strip().split("\n")
    width = max(display_width(l) for l in lines) + 4
    print(f"{color}{C_BOLD}┌{'─' * width}┐")
    for l in lines:
        dw = display_width(l)
        pad = width - dw - 2
        print(f"│  {l}{' ' * pad}│")
    print(f"└{'─' * width}┘{C_RESET}")


def banner():
    """NetMaster 彩色 ASCII 艺术字 banner"""

    letters = {
        'N': [
            "███╗    ██╗",
            "████╗   ██║",
            "██╔██╗  ██║",
            "██║╚██╗ ██║",
            "██║ ╚██╗██║",
            "╚═╝   ╚═══╝",
        ],
        'E': [
            "███████╗ ",
            "██╔════╝ ",
            "█████╗   ",
            "██╔══╝   ",
            "███████╗ ",
            "╚══════╝ ",
        ],
        'T': [
            "████████╗",
            "╚══██╔══╝",
            "   ██║   ",
            "   ██║   ",
            "   ██║   ",
            "   ╚═╝   ",
        ],
        'M': [
            "███╗   ███╗",
            "████╗ ████║",
            "██╔████╔██║",
            "██║╚██╔╝██║",
            "██║ ╚═╝ ██║",
            "╚═╝     ╚═╝",
        ],
        'A': [
            " █████╗  ",
            "██╔══██╗ ",
            "███████║ ",
            "██╔══██║ ",
            "██║  ██║ ",
            "╚═╝  ╚═╝ ",
        ],
        'S': [
            " ██████╗ ",
            "██╔════╝ ",
            "╚█████╗  ",
            " ╚═══██╗ ",
            "██████╔╝ ",
            "╚═════╝  ",
        ],
        'R': [
            "██████╗  ",
            "██╔══██╗ ",
            "██████╔╝ ",
            "██╔══██╗ ",
            "██║  ██║ ",
            "╚═╝  ╚═╝ ",
        ],
    }

    name = "NETMASTER"
    height = 6
    gap = "  "
    colors = [196, 202, 208, 214, 220, 226, 190, 154, 118]

    widths = {ch: len(letters[ch][0]) for ch in name}
    total_content = sum(widths[ch] for ch in name) + len(gap) * (len(name) - 1)

    pad_left = 3
    pad_right = 3
    box_inner = total_content + pad_left + pad_right

    _border = f"{C_BOLD}{C_CYAN}"
    print()
    print(f"{_border}╔{'═' * box_inner}╗{C_RESET}")
    print(f"{_border}║{' ' * box_inner}║{C_RESET}")

    for row in range(height):
        line = ""
        for i, ch in enumerate(name):
            c = colors[i]
            line += f"\033[38;5;{c};1m{letters[ch][row]}\033[0m"
            if i < len(name) - 1:
                line += gap
        print(f"{_border}║{C_RESET}{' ' * pad_left}{line}{' ' * pad_right}{_border}║{C_RESET}")

    print(f"{_border}║{' ' * box_inner}║{C_RESET}")

    subtitle = "Campus Network Auto-Guardian"
    sub_pad = (box_inner - len(subtitle)) // 2
    print(f"{_border}║{C_RESET}{' ' * sub_pad}{subtitle}{' ' * (box_inner - len(subtitle) - sub_pad)}{_border}║{C_RESET}")

    print(f"{_border}╚{'═' * box_inner}╝{C_RESET}")
    print()


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
    if not os.path.exists(LOG_FILE):
        return []
    with open(LOG_FILE, "r") as f:
        lines = f.readlines()
    return [l.strip() for l in lines[-n:]] if lines else []


def test_login():
    if not os.path.exists(CONFIG_PATH):
        return "fail", "配置文件不存在"
    with open(CONFIG_PATH, "r") as f:
        cfg = json.load(f)
    try:
        resp = requests.post(
            LOGIN_URL,
            data=cfg["login_payload"],
            headers=cfg.get("headers", {}),
            timeout=10
        )
        res = json.loads(resp.content)
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
    logs = last_logs(3)

    # 终端宽度自适应
    term_w = shutil.get_terminal_size().columns
    panel_w = min(50, term_w - 4)

    print()

    # ─── 标题行 ───
    print(f"  {C_BOLD}{C_DIM}─── 系统状态 ───{C_RESET}")

    # 辅助函数：打印一行状态
    def status_row(icon, label, value, v_color):
        print(f"  {icon}  {label:<8}{v_color}{value}{C_RESET}")

    # 服务守护
    if is_running:
        status_row(f"{C_GREEN}●{C_RESET}", "服务守护", "运行中", f"{C_GREEN}{C_BOLD}")
    else:
        status_row(f"{C_RED}●{C_RESET}", "服务守护", "已停止", f"{C_RED}{C_BOLD}")

    # 启动方式
    if is_enabled:
        status_row(f"{C_GREEN}⇱{C_RESET}", "启动方式", "开机自启", C_GREEN)
    else:
        status_row(f"{C_YELLOW}⇱{C_RESET}", "启动方式", "手动启动", C_YELLOW)

    # 网络状态
    if online:
        status_row(f"{C_GREEN}●{C_RESET}", "网络连接", "已联网", f"{C_GREEN}{C_BOLD}")
    else:
        status_row(f"{C_RED}●{C_RESET}", "网络连接", "已断网", f"{C_RED}{C_BOLD}")

    # 登录账号
    if os.path.exists(CONFIG_PATH):
        with open(CONFIG_PATH, "r") as f:
            cfg = json.load(f)
        uid = "未识别"
        try:
            uid = cfg["login_payload"].split("userId=")[1].split("&")[0]
        except Exception:
            pass
        if uid == "YOUR_STUDENT_ID" or uid == "未识别":
            status_row(f"{C_YELLOW}⚠{C_RESET}", "登录账号", "未配置凭证 (按6抓取)", C_YELLOW)
        else:
            status_row(f"{C_CYAN}●{C_RESET}", "登录账号", uid, f"{C_CYAN}{C_BOLD}")
    else:
        status_row(f"{C_RED}●{C_RESET}", "登录账号", "未配置", C_RED)

    print(f"  {C_DIM}{'─' * (panel_w)}{C_RESET}")

    # GitHub 仓库
    print(f"  {C_DIM}仓库 {C_RESET}{C_CYAN}{REPO_URL}{C_RESET}")

    # 最近日志
    if logs:
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
    log_dir = os.path.join(WORK_DIR, "logs")

    # 日志文件不存在时只显示一次提示就返回
    if not os.path.exists(LOG_FILE):
        clear()
        title_text = "NetMaster · 日志查看器"
        title_dw = display_width(title_text)
        log_box_w = max(title_dw + 4, 50)
        print(f"{C_BOLD}{C_CYAN}")
        print("╔" + "═" * log_box_w + "╗")
        left_pad = (log_box_w - title_dw) // 2
        right_pad = log_box_w - title_dw - left_pad
        print("║" + " " * left_pad + title_text + " " * right_pad + "║")
        print("╚" + "═" * log_box_w + "╝")
        print(C_RESET)
        print(f"\n  {C_DIM}日志文件: {LOG_FILE}{C_RESET}")
        print(f"\n  {C_YELLOW}⚠ 日志文件不存在，守护进程尚未运行{C_RESET}")
        print(f"  {C_DIM}提示: 在主页按 [1] 启动守护进程即可开始记录{C_RESET}")
        print()
        input(f"\n  {C_DIM}按 Enter 返回主页...{C_RESET}")
        return

    while True:
        clear()
        # 每次渲染时重新读取日志（tail -f / nano 可能改变内容）
        size = os.path.getsize(LOG_FILE)
        with open(LOG_FILE, "r") as f:
            all_lines = f.readlines()
        total = len(all_lines)

        title_text = "NetMaster · 日志查看器"
        title_dw = display_width(title_text)
        log_box_w = max(title_dw + 4, 50)

        print(f"{C_BOLD}{C_CYAN}")
        print("╔" + "═" * log_box_w + "╗")
        left_pad = (log_box_w - title_dw) // 2
        right_pad = log_box_w - title_dw - left_pad
        print("║" + " " * left_pad + title_text + " " * right_pad + "║")
        print("╚" + "═" * log_box_w + "╝")
        print(C_RESET)

        print(f"\n  {C_DIM}日志文件: {LOG_FILE}{C_RESET}")
        print(f"  {C_DIM}文件大小: {size} bytes  |  总行数: {total}{C_RESET}")

        if all_lines:
            print(f"\n  {C_BOLD}最近日志 (最后 10 行):{C_RESET}")
            for l in all_lines[-10:]:
                text = l.strip()[:110]
                if "登录成功" in text:
                    print(f"  {C_GREEN}{text}{C_RESET}")
                elif "断网" in text or "登录失败" in text or "登录错误" in text:
                    print(f"  {C_RED}{text}{C_RESET}")
                elif "网络已恢复" in text:
                    print(f"  {C_GREEN}{text}{C_RESET}")
                elif "守护进程启动" in text or "收到停止" in text:
                    print(f"  {C_YELLOW}{text}{C_RESET}")
                else:
                    print(f"  {C_DIM}{text}{C_RESET}")

        # 选项菜单（循环显示，q 才退出）
        print(f"\n  {C_BOLD}可选操作:{C_RESET}")
        print(f"  {C_GREEN}{C_BOLD}[f]{C_RESET} 实时追踪 (tail -f)  ")
        print(f"  {C_GREEN}{C_BOLD}[a]{C_RESET} 查看全部日志 (nano)  ")
        print(f"  {C_GREEN}{C_BOLD}[g]{C_RESET} 搜索断网记录    ")
        print(f"  {C_YELLOW}{C_BOLD}[q]{C_RESET} 返回主页")

        choice = input(f"\n  {C_BOLD}>>> {C_RESET}").strip()

        if choice == "f":
            print(f"\n  {C_GREEN}▶ 实时追踪中 (Ctrl+C 退出)...{C_RESET}\n")
            print(f"  {C_DIM}{'─' * (log_box_w + 2)}{C_RESET}")
            try:
                subprocess.run(["tail", "-f", LOG_FILE])
            except KeyboardInterrupt:
                print(f"\n  {C_DIM}已停止追踪{C_RESET}")
                time.sleep(0.5)
        elif choice == "a":
            subprocess.run(["nano", "-v", LOG_FILE])
        elif choice == "g":
            keyword = input(f"\n  {C_DIM}搜索关键词 (回车=断网): {C_RESET}").strip() or "断网"
            print(f"\n  {C_BOLD}包含 \"{keyword}\" 的记录:{C_RESET}\n")
            count = 0
            for l in all_lines:
                if keyword in l:
                    print(f"  {C_RED}{l.strip()[:110]}{C_RESET}")
                    count += 1
            if count == 0:
                print(f"  {C_DIM}未找到匹配记录{C_RESET}")
            print(f"\n  {C_DIM}共 {count} 条{C_RESET}")
            input(f"\n  {C_DIM}按 Enter 继续...{C_RESET}")
        elif choice == "q":
            break


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
