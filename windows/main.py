# -*- coding: utf-8 -*-
import sys
import os
import time
import datetime
import json
import subprocess
import re
import shutil
import requests
import ctypes
import winreg
import zipfile
import io
import tempfile
import glob
import argparse

# ================== 启动参数（必须最早解析，避免 daemon 模式加载 GUI） ==================
def parse_boot_args(argv):
    ap = argparse.ArgumentParser(add_help=False)
    ap.add_argument("--daemon", action="store_true")
    ap.add_argument("--config", default="")
    try:
        args, _ = ap.parse_known_args(argv[1:])
    except Exception:
        args = argparse.Namespace(daemon=False, config="")
    return args

BOOT_ARGS = parse_boot_args(sys.argv)

# ================== 全局配置 ==================
if getattr(sys, "frozen", False):
    APP_DIR = os.path.dirname(sys.executable)
else:
    APP_DIR = os.path.dirname(os.path.abspath(__file__))

TASK_NAME = "NetMaster_SHU_Auto_Login"
STOP_TOKEN_FILE = "stop.token"
DAEMON_CONFIG_NAME = "daemon_config.json"

LOGOUT_URL = "http://10.10.9.9/eportal/InterFace.do?method=logout"
LOGIN_URL_BASE = "http://10.10.9.9/eportal/InterFace.do?method=login"
MIRROR_BASE_URL = "https://npmmirror.com/mirrors/edgedriver"

SW_HIDE = 0  # 隐藏窗口常量


# ================== daemon 模式（不依赖 PySide6 / selenium） ==================
def _ensure_dir(p: str):
    os.makedirs(p, exist_ok=True)

def _log_factory(work_dir: str, max_log_days: int):
    log_dir = os.path.join(work_dir, "logs")
    _ensure_dir(log_dir)

    def _cleanup():
        try:
            now = time.time()
            for f in glob.glob(os.path.join(log_dir, "*.log")):
                if now - os.path.getmtime(f) > max_log_days * 86400:
                    try:
                        os.remove(f)
                    except:
                        pass
        except:
            pass

    def _log(msg: str):
        _cleanup()
        today = datetime.datetime.now().strftime("%Y-%m-%d")
        line = f"[{datetime.datetime.now().strftime('%H:%M:%S')}] {msg}"
        try:
            with open(os.path.join(log_dir, f"{today}.log"), "a", encoding="utf-8") as f:
                f.write(line + "\n")
        except:
            pass

    return _log

def _check_internet() -> bool:
    try:
        return requests.get("http://connect.rom.miui.com/generate_204", timeout=3).status_code == 204
    except:
        return False

def _check_stop(work_dir: str, log):
    token = os.path.join(work_dir, STOP_TOKEN_FILE)
    if os.path.exists(token):
        log("收到停止指令，退出。")
        try:
            os.remove(token)
        except:
            pass
        raise SystemExit(0)

def daemon_main(config_path: str) -> int:
    # 容错：如果没传 config，用默认 data_dir 下的
    if not config_path:
        # 尝试使用 APPDATA\NetMaster\daemon_config.json
        default_dir = os.path.join(os.environ.get("APPDATA", APP_DIR), "NetMaster")
        config_path = os.path.join(default_dir, DAEMON_CONFIG_NAME)

    if not os.path.exists(config_path):
        # 没有配置，直接退出（不弹窗）
        return 2

    try:
        with open(config_path, "r", encoding="utf-8") as f:
            cfg = json.load(f)
    except:
        return 3

    work_dir = cfg.get("work_dir") or os.path.dirname(config_path)
    _ensure_dir(work_dir)

    login_url = cfg.get("login_url", LOGIN_URL_BASE)
    headers = cfg.get("headers", {})
    payload = cfg.get("login_payload", "")
    max_log_days = int(cfg.get("max_log_days", 7))
    check_interval = int(cfg.get("check_interval", 20))

    log = _log_factory(work_dir, max_log_days)
    log(f"NetMaster 守护进程启动 (间隔: {check_interval}s)")
    log(f"Config: {config_path}")

    def do_login():
        log("断网重连中...")
        try:
            res = requests.post(login_url, data=payload, headers=headers, timeout=10).json()
            if res.get("result") == "success":
                log(">>> 登录成功")
            else:
                log(f"登录失败: {res}")
        except Exception as e:
            log(f"错误: {e}")

    # 启动即检查一次
    try:
        _check_stop(work_dir, log)
        if not _check_internet():
            do_login()
    except SystemExit:
        return 0

    # 主循环
    while True:
        try:
            _check_stop(work_dir, log)

            if not _check_internet():
                do_login()
                time.sleep(5)
                if _check_internet():
                    log("网络已恢复")
            else:
                # 你原逻辑是“网络正常也写一条日志”，这里保持一致
                log("网络正常，无需操作")

        except SystemExit:
            return 0
        except Exception as e:
            log(f"Err: {e}")

        # 分片 sleep，确保 stop.token 能快速响应
        for _ in range(max(1, check_interval)):
            time.sleep(1)
            try:
                _check_stop(work_dir, log)
            except SystemExit:
                return 0


# 如果是 daemon 模式：直接运行并退出（关键）
if BOOT_ARGS.daemon:
    raise SystemExit(daemon_main(BOOT_ARGS.config))


# ================== GUI 模式：仅当非 daemon 才导入 PySide6 / selenium ==================
from PySide6.QtWidgets import (QApplication, QMainWindow, QWidget, QVBoxLayout,
                               QHBoxLayout, QLabel, QPushButton, QLineEdit,
                               QTextEdit, QGroupBox,
                               QGridLayout, QSizePolicy, QFileDialog)
from PySide6.QtCore import Qt, QThread, Signal, QTimer
from PySide6.QtGui import QFont

from selenium import webdriver
from selenium.webdriver.edge.service import Service as EdgeService
from selenium.webdriver.edge.options import Options as EdgeOptions
from selenium.webdriver.common.by import By


# ================== 样式表 (Dark) ==================
DARK_STYLE = """
QWidget { font-family: 'Microsoft YaHei UI', 'Segoe UI', sans-serif; font-size: 14px; color: #d4d4d4; }
QMainWindow, QDialog { background-color: #1e1e1e; }
QLabel { background: transparent; border: none; color: #d4d4d4; }
QLabel#TitleLabel { font-size: 26px; font-weight: bold; color: #ffffff; padding: 10px 0; }
QGroupBox { background-color: #252526; border: 1px solid #3e3e42; border-radius: 8px; margin-top: 20px; font-weight: bold; color: #9cdcfe; }
QGroupBox::title { subcontrol-origin: margin; subcontrol-position: top left; padding: 0 5px; left: 15px; top: 0px; }
QPushButton { background-color: #3c3c3c; border: 1px solid #3e3e42; border-radius: 6px; padding: 8px 16px; color: #ffffff; font-weight: bold; }
QPushButton:hover { background-color: #505050; border-color: #505050; }
QPushButton:pressed { background-color: #2d2d2d; }
QPushButton:disabled { background-color: #252526; color: #6e6e6e; border-color: #2d2d2d; }
QPushButton#BtnCapture { background-color: #d7ba7d; color: #1e1e1e; }
QPushButton#BtnCapture:hover { background-color: #e5c07b; }
QPushButton#BtnGenerate { background-color: #4ec9b0; color: #1e1e1e; }
QPushButton#BtnGenerate:hover { background-color: #5cdebd; }
QPushButton#BtnStart { background-color: #569cd6; color: white; }
QPushButton#BtnStart:hover { background-color: #6caeff; }
QPushButton#BtnStop { background-color: #ce9178; color: #1e1e1e; }
QPushButton#BtnStop:hover { background-color: #d6a38c; }
QPushButton#BtnTest { background-color: #c586c0; color: white; }
QPushButton#BtnTest:hover { background-color: #d69cd1; }
QPushButton#BtnUninstall { background-color: #f44747; color: white; }
QPushButton#BtnUninstall:hover { background-color: #ff6666; }
QLineEdit { background-color: #3c3c3c; border: 1px solid #3e3e42; border-radius: 4px; padding: 6px; color: #cccccc; }
QLineEdit:focus { border: 1px solid #007fd4; }
QTextEdit { background-color: #000000; color: #4ec9b0; font-family: 'Consolas', 'Courier New', monospace; border: 1px solid #3e3e42; border-radius: 6px; padding: 5px; font-size: 13px; }
QScrollBar:vertical { border: none; background: #1e1e1e; width: 10px; }
QScrollBar::handle:vertical { background: #424242; min-height: 20px; border-radius: 5px; }
QLabel#StatusLabel { font-size: 14px; font-weight: bold; padding: 6px 12px; border-radius: 15px; }
"""


# ================== 核心工作线程 ==================
class CaptureWorker(QThread):
    log_signal = Signal(str)
    result_signal = Signal(bool, str)

    # 1. 修改 __init__ 接收 work_dir
    def __init__(self, current_user_id, work_dir):
        super().__init__()
        self.current_user_id = current_user_id
        self.work_dir = work_dir  # 保存目标目录

    def get_edge_version(self):
        try:
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Edge\BLBeacon")
            version, _ = winreg.QueryValueEx(key, "version")
            return version
        except:
            return None

    def download_driver_from_mirror(self):
        version = self.get_edge_version()
        if not version:
            self.log_signal.emit("❌ 未找到 Edge 浏览器")
            return None
        self.log_signal.emit(f"Edge 版本: {version}")
        download_url = f"{MIRROR_BASE_URL}/{version}/edgedriver_win64.zip"
        try:
            resp = requests.get(download_url, timeout=15)
            if resp.status_code != 200:
                return None
            with zipfile.ZipFile(io.BytesIO(resp.content)) as z:
                for filename in z.namelist():
                    if "msedgedriver.exe" in filename:
                        # 2. 修改保存路径为 self.work_dir
                        target_path = os.path.join(self.work_dir, "msedgedriver.exe")
                        
                        # 确保目录存在
                        if not os.path.exists(self.work_dir):
                            os.makedirs(self.work_dir, exist_ok=True)
                            
                        with open(target_path, "wb") as f:
                            f.write(z.read(filename))
                        return target_path
            return None
        except Exception as e:
            self.log_signal.emit(f"下载驱动失败: {e}")
            return None

    def get_silent_startupinfo(self):
        si = subprocess.STARTUPINFO()
        si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        si.wShowWindow = SW_HIDE
        return si

    def run(self):
        self.log_signal.emit(">>> 初始化...")
        si = self.get_silent_startupinfo()

        try:
            subprocess.run("taskkill /F /IM msedgedriver.exe", shell=True, capture_output=True, startupinfo=si)
            subprocess.run("taskkill /F /IM msedge.exe", shell=True, capture_output=True, startupinfo=si)
        except:
            pass

        driver_path = None
        # 3. 优先检查 work_dir 下的驱动
        local_driver = os.path.join(self.work_dir, "msedgedriver.exe")
        
        # 兼容性：如果 work_dir 没找到，也可以检查一下 APP_DIR (可选)
        legacy_driver = os.path.join(APP_DIR, "msedgedriver.exe")

        if os.path.exists(local_driver):
            driver_path = local_driver
        elif os.path.exists(legacy_driver):
            driver_path = legacy_driver
        else:
            driver_path = self.download_driver_from_mirror()

        if not driver_path:
            self.log_signal.emit("❌ 没驱动，无法运行")
            self.result_signal.emit(False, "")
            return

        # 2) 临时目录隔离
        user_data_dir = tempfile.mkdtemp()

        try:
            service = EdgeService(executable_path=driver_path)
            service.creation_flags = subprocess.CREATE_NO_WINDOW

            options = EdgeOptions()
            options.add_argument(f"--user-data-dir={user_data_dir}")
            options.add_argument("--no-first-run")
            options.add_argument("--no-default-browser-check")
            options.add_argument("--disable-infobars")
            options.add_argument("--disable-extensions")
            options.add_argument("--start-maximized")
            options.add_argument("--disable-gpu")
            options.add_argument("--disable-software-rasterizer")
            options.add_argument("--disable-features=RendererCodeIntegrity")
            options.add_argument("--ignore-certificate-errors")
            options.add_argument("--no-sandbox")
            options.add_argument("--disable-dev-shm-usage")
            options.add_experimental_option("excludeSwitches", ["enable-automation"])
            options.add_experimental_option("useAutomationExtension", False)
            options.page_load_strategy = "normal"

            driver = webdriver.Edge(service=service, options=options)
            self.log_signal.emit(">>> 浏览器启动中...")

            # 暴力导航
            target_url = "http://10.10.9.9"
            for i in range(5):
                try:
                    current = driver.current_url
                    if "10.10.9.9" not in current and "eportal" not in current:
                        self.log_signal.emit(f"正在跳转校园网 (第{i+1}次)...")
                        driver.get(target_url)
                        time.sleep(2)
                    else:
                        break
                except:
                    time.sleep(1)

            # 自动注销（若已在线）
            try:
                body = driver.find_element(By.TAG_NAME, "body").text
                if "注销" in body or "成功连接" in body:
                    self.log_signal.emit("检测到已在线，执行注销...")
                    match = re.search(r"\d{6,}", body)
                    if match:
                        uid = match.group(0)
                        requests.post(LOGOUT_URL, data=f"userId={uid}", timeout=2)
                        time.sleep(1)
                        driver.refresh()
            except:
                pass

            try:
                driver.execute_script("sessionStorage.removeItem('nm_captured');")
            except:
                pass

            self.log_signal.emit(">>> 请登录！")

            # JS hook
            js_hook = r"""
            if (!window._hooked) {
                window._hooked = true;
                const originalSend = XMLHttpRequest.prototype.send;
                XMLHttpRequest.prototype.send = function(body) {
                    if (this._method === 'POST' && body) {
                        window.sessionStorage.setItem('nm_captured', body);
                    }
                    return originalSend.apply(this, arguments);
                };
                const originalOpen = XMLHttpRequest.prototype.open;
                XMLHttpRequest.prototype.open = function(method, url) {
                    this._method = method ? method.toUpperCase() : method;
                    return originalOpen.apply(this, arguments);
                };
                const originalFetch = window.fetch;
                window.fetch = async function(input, init) {
                    if (init && init.method && init.method.toUpperCase() === 'POST' && init.body) {
                        window.sessionStorage.setItem('nm_captured', init.body);
                    }
                    return originalFetch.apply(this, arguments);
                };
            }
            """

            captured_data = None
            found = False
            start_time = time.time()

            while not found:
                if time.time() - start_time > 180:
                    self.log_signal.emit("❌ 超时退出")
                    break

                try:
                    if not driver.window_handles:
                        break

                    driver.execute_script(js_hook)
                    data = driver.execute_script("return window.sessionStorage.getItem('nm_captured');")

                    if data:
                        is_valid = False
                        if "password=" in data or "pwd=" in data or "mm=" in data:
                            is_valid = True
                        elif "userId=" in data and "userIndex=" not in data:
                            is_valid = True
                        if "userIndex=" in data and "password=" not in data:
                            is_valid = False
                            driver.execute_script("window.sessionStorage.removeItem('nm_captured');")

                        if is_valid:
                            self.log_signal.emit(f"★ 成功捕获: {data[:30]}...")
                            captured_data = data
                            found = True
                            break

                    time.sleep(0.5)
                except:
                    time.sleep(0.5)
                    continue

            self.log_signal.emit("正在关闭浏览器...")
            try:
                driver.quit()
            except:
                pass
            try:
                shutil.rmtree(user_data_dir, ignore_errors=True)
            except:
                pass

            if found:
                self.result_signal.emit(True, captured_data)
            else:
                self.result_signal.emit(False, "")

        except Exception as e:
            self.log_signal.emit(f"运行错误: {str(e)}")
            self.result_signal.emit(False, "")


class TestConfigWorker(QThread):
    log_signal = Signal(str)
    finish_signal = Signal(str, str)

    def __init__(self, data):
        super().__init__()
        self.data = data

    def run(self):
        self.log_signal.emit("=" * 30)
        self.log_signal.emit("★ 开始配置有效性测试")
        is_online = False
        try:
            if requests.get("http://connect.rom.miui.com/generate_204", timeout=2).status_code == 204:
                is_online = True
        except:
            pass
        self.log_signal.emit(f"网络状态: {'[联网]' if is_online else '[断网]'}")
        try:
            headers = {
                "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0",
                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                "Origin": "http://10.10.9.9",
                "Referer": "http://10.10.9.9/eportal/index.jsp",
            }
            res = requests.post(LOGIN_URL_BASE, data=self.data, headers=headers, timeout=10)
            res_json = res.json()
            result_code = res_json.get("result", "unknown")
            msg = res_json.get("message", "") or "无"
            self.log_signal.emit(f"认证结果: {result_code} | 消息: {msg}")
            if result_code == "success":
                self.finish_signal.emit("测试通过", "配置有效！")
            elif msg and "已经在线" in str(msg):
                self.finish_signal.emit("测试通过", "账号已在线。")
            else:
                self.finish_signal.emit("测试失败", f"服务器拒绝: {msg}")
        except Exception as e:
            self.finish_signal.emit("错误", f"请求异常: {str(e)}")
        self.log_signal.emit("=" * 30)


# ================== UI 主逻辑 ==================
class NetMasterUI(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("NetMaster (SHU Campus Network Auto-Guardian)")
        self.resize(760, 960)
        self.captured_data = None
        self.current_user_id = ""

        self.default_data_dir = os.path.join(os.environ.get("APPDATA"), "NetMaster")
        if not os.path.exists(self.default_data_dir):
            try:
                os.makedirs(self.default_data_dir)
            except:
                pass

        self.setStyleSheet(DARK_STYLE)
        self.init_ui()

        self.status_timer = QTimer()
        self.status_timer.timeout.connect(self.check_system_status)
        self.status_timer.start(2000)

        QTimer.singleShot(100, self.check_system_status)
        self.log(f">>> 默认数据目录: {self.default_data_dir}")
        self.load_ui_prefs()

    # === 新增：保存当前设置的路径到配置文件 ===
    def save_ui_prefs(self):
        try:
            # 无论用户把数据存在哪，我们都把“位置记录”存在默认的 AppData 目录下
            prefs_file = os.path.join(self.default_data_dir, "gui_prefs.json")
            
            # 确保默认目录存在
            if not os.path.exists(self.default_data_dir):
                os.makedirs(self.default_data_dir, exist_ok=True)

            current_path = self.input_path.text().strip()
            with open(prefs_file, "w", encoding="utf-8") as f:
                json.dump({"last_work_dir": current_path}, f)
        except Exception as e:
            pass # 记录失败不影响主流程

    # === 新增：启动时加载上次的路径 ===
    def load_ui_prefs(self):
        try:
            prefs_file = os.path.join(self.default_data_dir, "gui_prefs.json")
            if os.path.exists(prefs_file):
                with open(prefs_file, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    last_path = data.get("last_work_dir", "")
                    # 如果记录的路径存在，就恢复它
                    if last_path and os.path.exists(last_path):
                        self.input_path.setText(last_path)
                        self.log(f"已恢复上次路径: {last_path}")
        except:
            pass

    def get_silent_si(self):
        si = subprocess.STARTUPINFO()
        si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        si.wShowWindow = SW_HIDE
        return si

    def show_native_message(self, title, message, icon_type="info"):
        flags = 0x40 if icon_type == "info" else (0x30 if icon_type == "warning" else 0x10)
        ctypes.windll.user32.MessageBoxW(0, message, title, flags | 0x40000)

    def show_native_question(self, title, message):
        ret = ctypes.windll.user32.MessageBoxW(0, message, title, 0x04 | 0x20 | 0x40000)
        return ret == 6

    # 路径选择
    def select_path(self):
        root_dir = QFileDialog.getExistingDirectory(self, "选择数据保存根目录", self.input_path.text())
        if root_dir:
            target_path = os.path.join(root_dir, "NetMaster")
            target_path = os.path.normpath(target_path)
            self.input_path.setText(target_path)
            if not os.path.exists(target_path):
                try:
                    os.makedirs(target_path)
                except:
                    pass
            self.log(f"已更新数据保存路径: {target_path}")
            self.save_ui_prefs()

    def init_ui(self):
        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        main_layout = QVBoxLayout(central_widget)
        main_layout.setSpacing(20)
        main_layout.setContentsMargins(25, 25, 25, 25)

        header_layout = QHBoxLayout()
        title = QLabel("NetMaster Auto-Guardian")
        title.setObjectName("TitleLabel")
        header_layout.addWidget(title)
        header_layout.addStretch()
        self.status_label = QLabel("INIT...")
        self.status_label.setObjectName("StatusLabel")
        header_layout.addWidget(self.status_label)
        main_layout.addLayout(header_layout)

        # 第一步：凭证获取
        group_capture = QGroupBox("第一步：凭证获取")
        layout_capture = QHBoxLayout()
        layout_capture.setContentsMargins(20, 30, 20, 20)

        self.btn_capture = QPushButton("🚀 启动抓包 (请直接双击运行)")
        self.btn_capture.setObjectName("BtnCapture")
        self.btn_capture.setMinimumHeight(45)
        self.btn_capture.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Fixed)
        self.btn_capture.clicked.connect(self.start_capture)

        layout_capture.addWidget(self.btn_capture)
        group_capture.setLayout(layout_capture)
        main_layout.addWidget(group_capture)

        # 第二步：参数配置
        group_settings = QGroupBox("第二步：参数配置")
        layout_settings = QGridLayout()
        layout_settings.setContentsMargins(20, 30, 20, 20)
        self.input_interval = QLineEdit("20")
        self.input_days = QLineEdit("7")
        self.input_path = QLineEdit(self.default_data_dir)

        self.btn_select_path = QPushButton("📂 更改")
        self.btn_select_path.clicked.connect(self.select_path)

        layout_settings.addWidget(QLabel("检测间隔(s):"), 0, 0)
        layout_settings.addWidget(self.input_interval, 0, 1)
        layout_settings.addWidget(QLabel("日志保留(天):"), 0, 2)
        layout_settings.addWidget(self.input_days, 0, 3)
        layout_settings.addWidget(QLabel("保存路径:"), 1, 0)
        layout_settings.addWidget(self.input_path, 1, 1, 1, 2)
        layout_settings.addWidget(self.btn_select_path, 1, 3)

        group_settings.setLayout(layout_settings)
        main_layout.addWidget(group_settings)

        # 第三步：智能部署
        group_deploy = QGroupBox("第三步：智能部署")
        layout_deploy = QGridLayout()
        layout_deploy.setContentsMargins(20, 30, 20, 20)

        self.btn_generate = QPushButton("📥 生成配置 & 更新任务")
        self.btn_generate.setObjectName("BtnGenerate")
        self.btn_generate.clicked.connect(self.install_service)

        self.btn_uninstall = QPushButton("🗑️ 卸载任务")
        self.btn_uninstall.setObjectName("BtnUninstall")
        self.btn_uninstall.clicked.connect(self.uninstall_service)

        self.btn_start = QPushButton("▶️ 立即启动")
        self.btn_start.setObjectName("BtnStart")
        self.btn_start.clicked.connect(self.start_daemon)

        self.btn_stop = QPushButton("⏹️ 停止进程")
        self.btn_stop.setObjectName("BtnStop")
        self.btn_stop.clicked.connect(self.stop_daemon)

        layout_deploy.addWidget(self.btn_generate, 0, 0)
        layout_deploy.addWidget(self.btn_uninstall, 0, 1)
        layout_deploy.addWidget(self.btn_start, 1, 0)
        layout_deploy.addWidget(self.btn_stop, 1, 1)

        group_deploy.setLayout(layout_deploy)
        main_layout.addWidget(group_deploy)

        # 调试
        group_debug = QGroupBox("调试")
        layout_debug = QHBoxLayout()
        layout_debug.setContentsMargins(20, 30, 20, 20)

        self.btn_test = QPushButton("🛠️ 测试配置")
        self.btn_test.setObjectName("BtnTest")
        self.btn_test.setMinimumHeight(45)
        self.btn_test.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Fixed)
        self.btn_test.clicked.connect(self.test_config)

        layout_debug.addWidget(self.btn_test)
        group_debug.setLayout(layout_debug)
        main_layout.addWidget(group_debug)

        self.txt_log = QTextEdit()
        self.txt_log.setReadOnly(True)
        main_layout.addWidget(self.txt_log)

    def log(self, msg):
        time_str = datetime.datetime.now().strftime("%H:%M:%S")
        self.txt_log.append(f"[{time_str}] {msg}")
        self.txt_log.verticalScrollBar().setValue(self.txt_log.verticalScrollBar().maximum())

    def _is_task_running(self):
        try:
            cmd = f'schtasks /Query /TN "{TASK_NAME}" /FO CSV /NH'
            si = self.get_silent_si()
            result = subprocess.run(cmd, capture_output=True, text=True, startupinfo=si)
            return ("Running" in result.stdout) or ("运行" in result.stdout)
        except:
            return False

    def _is_task_installed(self):
        """仅检测任务计划是否存在，不管它是否在运行"""
        si = self.get_silent_si()
        cmd = f'schtasks /Query /TN "{TASK_NAME}"'
        # 如果返回码是 0，说明任务存在；否则说明找不到任务
        res = subprocess.run(cmd, capture_output=True, text=True, startupinfo=si)
        return res.returncode == 0

    def get_data_dir(self):
        path = self.input_path.text().strip()
        if not path:
            return self.default_data_dir
        if not os.path.exists(path):
            try:
                os.makedirs(path)
            except:
                return self.default_data_dir
        return path

    def _config_path(self):
        return os.path.join(self.get_data_dir(), DAEMON_CONFIG_NAME)

    def check_system_status(self):
        data_dir = self.get_data_dir()
        cfg_path = os.path.join(data_dir, DAEMON_CONFIG_NAME)
        has_cfg = os.path.exists(cfg_path)

        # 若已有 config 且内存没有 captured_data，则读取
        if has_cfg and not self.captured_data:
            try:
                with open(cfg_path, "r", encoding="utf-8") as f:
                    cfg = json.load(f)
                self.captured_data = cfg.get("login_payload", None)
                if self.captured_data:
                    try:
                        self.current_user_id = self.captured_data.split("userId=")[1].split("&")[0]
                    except:
                        pass
            except:
                pass

        if self._is_task_running():
            self.status_label.setText("🚀 正常守护中")
            self.status_label.setStyleSheet("background-color: #064e3b; color: #34d399; border: 1px solid #10b981;")
        elif not self.captured_data:
            self.status_label.setText("⚠️ 未配置")
            self.status_label.setStyleSheet("background-color: #450a0a; color: #f87171; border: 1px solid #ef4444;")
        else:
            self.status_label.setText("⏸️ 就绪 (未运行)")
            self.status_label.setStyleSheet("background-color: #431407; color: #fb923c; border: 1px solid #f97316;")

    def start_capture(self):
        self.btn_capture.setEnabled(False)
        work_dir = self.get_data_dir()
        self.worker = CaptureWorker(self.current_user_id, work_dir)
        self.worker.log_signal.connect(self.log)
        self.worker.result_signal.connect(self.on_capture_finished)
        self.worker.start()

    def on_capture_finished(self, success, data):
        self.btn_capture.setEnabled(True)
        if success:
            self.captured_data = data
            try:
                self.current_user_id = data.split("userId=")[1].split("&")[0]
            except:
                pass
            self.log("★ 抓包成功！凭证已保存内存。")
            self.show_native_message("NetMaster", "凭证捕获成功！")
        else:
            self.log("× 抓包失败。")
            self.show_native_message("NetMaster", "未能捕获凭证。", "warning")

    def test_config(self):
        if not self.captured_data:
            return self.show_native_message("警告", "无配置数据", "warning")
        self.btn_test.setEnabled(False)
        self.t_worker = TestConfigWorker(self.captured_data)
        self.t_worker.log_signal.connect(self.log)
        self.t_worker.finish_signal.connect(
            lambda t, m: (self.btn_test.setEnabled(True), self.show_native_message(t, m, "info"))
        )
        self.t_worker.start()

    def _write_config(self, payload: str, interval: int, days: int, work_dir: str):
        cfg = {
            "login_url": LOGIN_URL_BASE,
            "headers": {
                "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0",
                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                "Origin": "http://10.10.9.9",
                "Referer": "http://10.10.9.9/eportal/index.jsp",
            },
            "login_payload": payload,
            "max_log_days": int(days),
            "check_interval": int(interval),
            "work_dir": work_dir,
        }
        cfg_path = os.path.join(work_dir, DAEMON_CONFIG_NAME)
        with open(cfg_path, "w", encoding="utf-8") as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
        return cfg_path

    def install_service(self):
        self.save_ui_prefs()
        if not self.captured_data:
            return self.show_native_message("警告", "无凭证，请先抓包", "warning")

        data_dir = self.get_data_dir()

        # 1) 写入 JSON 配置
        try:
            inv = int(self.input_interval.text())
            days = int(self.input_days.text())
            cfg_path = self._write_config(self.captured_data, inv, days, data_dir)
        except Exception as e:
            return self.show_native_message("错误", f"配置写入失败: {e}", "error")

        # ================== 核心修复开始 ==================
        # 优先级 1: Nuitka OneFile 特有环境变量 (最准确)
        nuitka_binary = os.environ.get("NUITKA_ONEFILE_BINARY")
        
        # 优先级 2: sys.executable (PyInstaller 或 Nuitka 非 OneFile)
        # 注意：在某些 Nuitka 版本中 sys.executable 可能指向内部 python，所以放在后面判断
        sys_exec = sys.executable

        exe_path = ""
        is_frozen_exe = False

        if nuitka_binary and os.path.exists(nuitka_binary):
            exe_path = nuitka_binary
            is_frozen_exe = True
            self.log(f"检测到 Nuitka OneFile: {exe_path}")
        elif getattr(sys, "frozen", False):
            exe_path = sys_exec
            is_frozen_exe = True
        elif sys.argv[0].lower().endswith(".exe") and os.path.exists(sys.argv[0]):
            # 最后的保底：如果 argv[0] 是 exe，那它很可能就是程序本身
            exe_path = os.path.abspath(sys.argv[0])
            is_frozen_exe = True
            self.log(f"通过 argv[0] 检测到执行文件: {exe_path}")
        
        # 构造命令
        if is_frozen_exe:
            # 如果是打包后的 EXE，直接运行它，不加 script 路径
            # 必须处理路径空格，加引号
            command = f'"{exe_path}"'
            arguments = f'--daemon --config "{cfg_path}"'
        else:
            # 纯开发环境 (运行 .py)
            command = sys.executable
            script = os.path.abspath(__file__)
            # 同样处理空格
            command = f'"{command}"' if " " in command else command
            arguments = f'"{script}" --daemon --config "{cfg_path}"'
            self.log("⚠️ 警告: 未检测到打包环境，任务将指向临时路径(重启后可能失效)")

        self.log(f"任务执行程序: {command}")
        self.log(f"任务参数: {arguments}")
        # ================== 核心修复结束 ==================

        xml = self.get_xml_content(command, arguments, data_dir)
        xml_path = os.path.join(data_dir, "task_config.xml")

        try:
            with open(xml_path, "w", encoding="utf-16") as f:
                f.write(xml)
        except Exception as e:
            return self.show_native_message("错误", f"XML 写入失败: {e}", "error")

        cmd = f'schtasks /Create /TN "{TASK_NAME}" /XML "{xml_path}" /F'
        si = self.get_silent_si()

        try:
            subprocess.run(cmd, capture_output=True, check=True, text=True, startupinfo=si)
            try:
                os.remove(xml_path)
            except:
                pass
            self.log("★ 生成配置成功（任务计划已更新）")
            self.show_native_message("成功", "NetMaster 配置已更新")
        except subprocess.CalledProcessError:
            self.log("⚠️ 正在请求管理员权限以覆盖任务...")
            ret = ctypes.windll.shell32.ShellExecuteW(
                None, "runas", "schtasks", f'/Create /TN "{TASK_NAME}" /XML "{xml_path}" /F', None, 0
            )

            if ret > 32:
                QTimer.singleShot(2500, lambda: self.log("★ 生成配置成功（已请求管理员权限）"))
                QTimer.singleShot(3500, lambda: (os.remove(xml_path) if os.path.exists(xml_path) else None))
            else:
                self.show_native_message("错误", "请求管理员权限失败，无法创建任务。", "error")

        if self._is_task_running():
            self.stop_daemon()
            QTimer.singleShot(2500, self.start_daemon)

    def start_daemon(self):
        # === 新增判断：如果没有任务，提示先生成 ===
        if not self._is_task_installed():
            self.show_native_message("提示", "尚未创建后台任务！\n请先点击【生成配置 & 更新任务】按钮。", "warning")
            return

        cmd = f'schtasks /Run /TN "{TASK_NAME}"'
        si = self.get_silent_si()
        subprocess.run(cmd, capture_output=True, startupinfo=si)
        self.log("★ 启动指令已发送")
        QTimer.singleShot(1000, self.check_system_status)

    def stop_daemon(self):
        if not self._is_task_running():
            self.log("⚠️ 进程当前未运行或已卸载，无需执行停止操作。")
            self.show_native_message("提示", "守护进程未在运行，无需停止。", "info")
            return
        data_dir = self.get_data_dir()
        try:
            with open(os.path.join(data_dir, STOP_TOKEN_FILE), "w", encoding="utf-8") as f:
                f.write("stop")
            self.log("已发送停止信号...")
        except Exception as e:
            self.log(f"❌ 发送停止信号失败: {e}")
        QTimer.singleShot(2000, self.check_system_status)

    def uninstall_service(self):
        # 1. 停止“心跳”检测 (防止文件夹被后台定时器自动复活)
        self.status_timer.stop()
        self.status_label.setText("🛑 正在卸载...")
        self.status_label.setStyleSheet("background-color: #450a0a; color: #f87171;")

        # === 收集需要删除的所有路径 ===
        # 使用 set 集合自动去重（防止当前路径就是默认路径时重复删除）
        paths_to_delete = set()
        
        # 1) 当前配置的工作目录 (例如 D:\Software\NetMaster)
        current_data_dir = self.get_data_dir()
        if current_data_dir:
            paths_to_delete.add(os.path.abspath(current_data_dir))
            
        # 2) C盘默认数据目录 (包含 gui_prefs.json)
        # 通常是 C:\Users\xxx\AppData\Roaming\NetMaster
        if hasattr(self, 'default_data_dir') and self.default_data_dir:
            paths_to_delete.add(os.path.abspath(self.default_data_dir))

        # 判断是否已经清理干净了（任务没了 且 文件夹也没了）
        has_task = self._is_task_installed()
        files_exist = any(os.path.exists(p) for p in paths_to_delete)

        if not has_task and not files_exist:
            self.show_native_message("提示", "当前已是纯净状态，无需重复操作。", "info")
            # 恢复定时器（万一用户想反悔重新生成）
            self.status_timer.start(2000)
            return

        # 2. 确认卸载
        if not self.show_native_question("彻底卸载", "确定要删除所有数据吗？\n\n将清理：\n1. Windows 任务计划\n2. D盘/自定义路径下的驱动与日志\n3. C盘 AppData 下的配置文件(gui_prefs.json)"):
            self.status_timer.start(2000) # 用户取消，恢复心跳
            self.check_system_status()
            return

        self.log(">>> 开始执行彻底卸载...")
        si = self.get_silent_si()
        
        # [关键]：逃离当前目录到 Temp，防止占用
        try:
            os.chdir(tempfile.gettempdir())
        except:
            pass

        # 3. 强制杀进程
        try:
            subprocess.run("taskkill /F /IM msedgedriver.exe", shell=True, capture_output=True, startupinfo=si)
            subprocess.run("taskkill /F /IM msedge.exe", shell=True, capture_output=True, startupinfo=si)
            time.sleep(0.5)
        except:
            pass

        # 4. 删除任务计划
        if has_task:
            cmd = f'schtasks /Delete /TN "{TASK_NAME}" /F'
            try:
                subprocess.run(cmd, check=True, capture_output=True, startupinfo=si)
                self.log("√ 任务计划已移除")
            except:
                ctypes.windll.shell32.ShellExecuteW(None, "runas", "schtasks", f'/Delete /TN "{TASK_NAME}" /F', None, 0)
        
        # 5. 遍历删除所有相关文件夹 (C盘 和 D盘)
        current_exe = os.path.abspath(sys.executable)
        
        for target_dir in paths_to_delete:
            if not os.path.exists(target_dir):
                continue
                
            self.log(f"正在清理目录: {target_dir}")
            
            # 保护机制：如果程序自己就在这个文件夹里，只删内容不删壳
            if target_dir in current_exe:
                self.log(f"⚠️ 程序运行于 {target_dir}，仅清空内容...")
                for item in os.listdir(target_dir):
                    item_path = os.path.join(target_dir, item)
                    if os.path.abspath(item_path) == current_exe:
                        continue
                    try:
                        if os.path.isdir(item_path):
                            shutil.rmtree(item_path, ignore_errors=True)
                        else:
                            os.remove(item_path)
                    except:
                        pass
            else:
                # 暴力删除文件夹
                success = False
                for i in range(5):
                    try:
                        if os.path.exists(target_dir):
                            shutil.rmtree(target_dir, ignore_errors=False)
                        success = True
                        break
                    except:
                        # 再次尝试杀进程并改权限
                        subprocess.run("taskkill /F /IM msedgedriver.exe", shell=True, capture_output=True, startupinfo=si)
                        try:
                            os.chmod(target_dir, 0o777)
                        except:
                            pass
                        time.sleep(0.5)
                
                # 补刀空文件夹
                if not success and os.path.exists(target_dir):
                    try:
                        os.rmdir(target_dir)
                    except:
                        pass

                if not os.path.exists(target_dir):
                    self.log(f"√ 已删除: {target_dir}")
                else:
                    self.log(f"× 删除失败 (可能被占用): {target_dir}")

        # 6. 收尾
        self.captured_data = None
        self.status_label.setText("已卸载")
        self.show_native_message("完成", "NetMaster 已彻底卸载。\n(配置、日志、驱动及任务计划均已清除)")

    def get_xml_content(self, command_path, arguments, work_dir):
        now = datetime.datetime.now().isoformat()
        try:
            current_user = os.environ.get("USERNAME")
        except:
            current_user = "Interactive User"

        # 关键点：Arguments 不要再用 pythonw + script.pyw；而是直接传 --daemon --config
        return f"""<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Date>{now}</Date>
    <Author>NetMaster_User</Author>
    <Description>NetMaster Auto Login Service</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>{current_user}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>true</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{command_path}</Command>
      <Arguments>{arguments}</Arguments>
      <WorkingDirectory>{work_dir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>"""


# ================== GUI 入口 ==================
if __name__ == "__main__":
    app = QApplication(sys.argv)
    font = QFont("Microsoft YaHei UI")
    font.setStyleStrategy(QFont.PreferAntialias)
    app.setFont(font)
    window = NetMasterUI()
    window.show()
    sys.exit(app.exec())
