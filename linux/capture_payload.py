#!/usr/bin/env python3
"""抓取校园网登录 POST payload — 会打开 Firefox，你手动输入账号密码登录即可自动捕获"""

import os
import sys
import time
import json
import re
import tempfile
import shutil

import requests

try:
    from selenium import webdriver
    from selenium.webdriver.firefox.service import Service
    from selenium.webdriver.firefox.options import Options
    from selenium.webdriver.common.by import By
except ModuleNotFoundError:
    print("缺少 selenium 库，请先安装：")
    print("  pip3 install selenium")
    sys.exit(10)

WORK_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(WORK_DIR, "daemon_config.json")
GECKODRIVER = os.path.join(WORK_DIR, "geckodriver")

LOGOUT_URL = "http://10.10.9.9/eportal/InterFace.do?method=logout"
PORTAL_URL = "http://10.10.9.9"


def main():
    print("=" * 50)
    print("NetMaster Payload 抓取工具 (Linux)")
    print("=" * 50)

    # 1. 先读取当前在线 userId，用于注销
    print("\n[1] 检查当前在线状态...")
    try:
        resp = requests.get(PORTAL_URL, timeout=5, allow_redirects=False)
        if resp.status_code == 302:
            loc = resp.headers.get("Location", "")
            if "redirectortosuccess" in loc or "success" in loc:
                print(f"    当前已在线，需要先注销才能看到登录页面")
                # 尝试从各种来源获取 userId
                user_id = None
                # 方法1: 获取成功页面看看有没有 userId
                cookies = resp.cookies.get_dict()
                jsession = cookies.get("JSESSIONID", "")
                try:
                    resp2 = requests.get(PORTAL_URL, timeout=5)
                    body = resp2.text
                    match = re.search(r"(\d{6,})", body)
                    if match:
                        user_id = match.group(0)
                except Exception:
                    pass

                if not user_id:
                    # 方法2: 尝试不带 userId 注销（有些系统支持）
                    print("    未能获取 userId，尝试通用注销...")
                else:
                    print(f"    检测到 userId: {user_id}")

                if user_id:
                    try:
                        out = requests.post(LOGOUT_URL, data=f"userId={user_id}", timeout=5)
                        print(f"    注销结果: {out.text[:100]}")
                    except Exception as e:
                        print(f"    注销失败: {e}")
                else:
                    # 直接访问 logout URL
                    try:
                        out = requests.get(LOGOUT_URL, timeout=5)
                        print(f"    注销结果: {out.text[:100]}")
                    except Exception as e:
                        print(f"    注销失败: {e}")

                time.sleep(2)
            else:
                print(f"    状态: {resp.status_code} -> {loc}")
    except Exception as e:
        print(f"    检测失败: {e}")

    # 2. 启动浏览器
    print("\n[2] 启动 Firefox...")
    options = Options()
    # 不设 headless，让你能看到浏览器窗口
    service = Service(executable_path=GECKODRIVER)
    driver = webdriver.Firefox(service=service, options=options)

    try:
        # 3. 导航到 portal
        print("[3] 导航到认证页面...")
        driver.get(PORTAL_URL)
        time.sleep(2)

        # 如果自动跳转了，再导航一次
        current = driver.current_url
        print(f"    当前 URL: {current}")

        if "redirectortosuccess" in current or "success" in current:
            print("    仍在成功页，可能注销未生效，继续尝试...")
            # 再次尝试导航
            driver.get(PORTAL_URL)
            time.sleep(2)
            print(f"    当前 URL: {driver.current_url}")

        # 4. 注入 JS hook
        print("[4] 注入抓包 Hook...")
        js_hook = """
        if (!window._hooked) {
            window._hooked = true;
            const origSend = XMLHttpRequest.prototype.send;
            XMLHttpRequest.prototype.send = function(body) {
                if (this._method === 'POST' && body) {
                    window.sessionStorage.setItem('nm_captured', body);
                    console.log('[NetMaster] Captured:', body);
                }
                return origSend.apply(this, arguments);
            };
            const origOpen = XMLHttpRequest.prototype.open;
            XMLHttpRequest.prototype.open = function(method, url) {
                this._method = (method || '').toUpperCase();
                return origOpen.apply(this, arguments);
            };
            const origFetch = window.fetch;
            window.fetch = async function(input, init) {
                if (init && init.body && (init.method || '').toUpperCase() === 'POST') {
                    window.sessionStorage.setItem('nm_captured', init.body);
                    console.log('[NetMaster] Captured:', init.body);
                }
                return origFetch.apply(this, arguments);
            };
        }
        """
        driver.execute_script(js_hook)

        print("""
╔══════════════════════════════════════════════╗
║  浏览器已打开，请在页面中输入账号密码登录   ║
║  登录成功后脚本会自动捕获 POST 请求体        ║
║  等待中...（最多 5 分钟）                    ║
╚══════════════════════════════════════════════╝
""")

        # 5. 轮询抓包
        captured = None
        start = time.time()
        while not captured:
            if time.time() - start > 300:
                print("❌ 超时（5分钟），未捕获到登录请求")
                break

            try:
                driver.execute_script(js_hook)
                data = driver.execute_script(
                    "return window.sessionStorage.getItem('nm_captured');"
                )
                if data:
                    # 简单校验：包含密码相关字段
                    has_pwd = any(kw in data for kw in ["password=", "pwd=", "pass="])
                    has_user = any(kw in data for kw in ["userId=", "user=", "username="])
                    if has_pwd and has_user:
                        captured = data
                        print(f"\n★ 捕获成功!")
                        print(f"  Payload: {data[:80]}...")
                        break
                    else:
                        # 可能是其他无关请求，清掉重来
                        driver.execute_script(
                            "window.sessionStorage.removeItem('nm_captured');"
                        )
                time.sleep(0.5)
            except Exception as e:
                print(f"  轮询异常: {e}")
                time.sleep(1)

        if captured:
            # 6. 保存到配置
            print("\n[5] 保存配置...")
            if os.path.exists(CONFIG_PATH):
                with open(CONFIG_PATH, "r") as f:
                    cfg = json.load(f)
            else:
                cfg = {}

            cfg["login_payload"] = captured

            with open(CONFIG_PATH, "w", encoding="utf-8") as f:
                json.dump(cfg, f, ensure_ascii=False, indent=2)

            print(f"    已写入: {CONFIG_PATH}")
            print("\n你现在可以运行: ~/.netmaster/ctl.sh start")
        else:
            print("\n❌ 未捕获到有效 payload，请重试")

    finally:
        print("\n[6] 关闭浏览器...")
        try:
            driver.quit()
        except Exception:
            pass

    print("完成")


if __name__ == "__main__":
    main()
