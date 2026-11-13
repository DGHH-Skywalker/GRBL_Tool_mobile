"""Runs only in GitHub Actions against an Android emulator; no physical transport."""
import pathlib
import re
import subprocess
import time
import xml.etree.ElementTree as ET

APP = "io.github.dghhskywalker.grbltoolmobile"
OUT = pathlib.Path("artifacts/demo-smoke")
OUT.mkdir(parents=True, exist_ok=True)


def adb(*args):
    return subprocess.check_output(["adb", *args], stderr=subprocess.STDOUT)


def tree():
    # Cold-boot accessibility can briefly have no active root even after boot_completed.
    last = ""
    for _ in range(5):
        try:
            last = adb("shell", "uiautomator", "dump", "/sdcard/grbl-window.xml").decode()
            return ET.fromstring(adb("shell", "cat", "/sdcard/grbl-window.xml"))
        except (subprocess.CalledProcessError, ET.ParseError) as error:
            last += str(error)
            time.sleep(1)
    raise AssertionError(f"Accessibility hierarchy unavailable after retries: {last}")


def find(label, exact=True):
    nodes = list(tree().iter("node"))
    for attribute in ("content-desc", "text"):
        for node in nodes:
            value = node.get(attribute, "")
            if label == value if exact else label in value:
                return node
    return None


def wait(label, timeout=25, exact=False):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        node = find(label, exact)
        if node is not None:
            return node
        time.sleep(.3)
    raise AssertionError(f"UI text not found: {label}")


def tap(label, exact=True):
    for _ in range(10):
        node = find(label, exact)
        if node is not None:
            assert node.get("enabled") == "true", f"Disabled: {label}"
            x1, y1, x2, y2 = map(int, re.findall(r"\d+", node.get("bounds")))
            adb("shell", "input", "tap", str((x1 + x2) // 2), str((y1 + y2) // 2))
            time.sleep(.3)
            return
        adb("shell", "input", "swipe", "500", "1500", "500", "600", "250")
    raise AssertionError(f"Cannot tap: {label}")


def screenshot(name):
    (OUT / f"{name}.png").write_bytes(adb("exec-out", "screencap", "-p"))
    try:
        (OUT / f"{name}.xml").write_bytes(adb("shell", "cat", "/sdcard/grbl-window.xml"))
    except subprocess.CalledProcessError as error:
        (OUT / f"{name}-xml-error.txt").write_text(str(error))


def launch():
    adb("shell", "monkey", "-p", APP, "-c", "android.intent.category.LAUNCHER", "1")


try:
    apks = sorted(pathlib.Path("artifacts/apk").rglob("*-Signed.apk"))
    assert apks, "Signed APK missing"
    adb("install", "-r", str(apks[0]))
    adb("shell", "pm", "grant", APP, "android.permission.POST_NOTIFICATIONS")
    launch()
    wait("体验完整演示")
    screenshot("01-welcome")
    tap("体验完整演示")
    wait("GRBL-demo.nc")
    screenshot("02-preview")
    tap("加工")
    tap("已检查现场", exact=False)
    tap("开始加工")
    tap("确认")
    tap("暂停")
    wait("已暂停")
    screenshot("03-paused")
    tap("继续")
    tap("确认")
    adb("shell", "input", "keyevent", "3")
    time.sleep(25)
    launch()
    wait("已完成")
    screenshot("04-completed-after-background")
    tap("历史")
    wait("GRBL-demo.nc")
    wait("已完成")
    screenshot("05-history")
    tap("作品")
    tap("图像/矢量加工宽度 mm")
    adb("shell", "input", "keyevent", "KEYCODE_MOVE_END")
    adb("shell", "input", "keyevent", "KEYCODE_DEL")
    adb("shell", "input", "keyevent", "KEYCODE_DEL")
    adb("shell", "input", "text", "25")
    adb("shell", "input", "keyevent", "4")
    tap("加工")
    wait("输入已变化")
    button = find("开始加工")
    assert button is not None and button.get("enabled") == "false"
    screenshot("06-dirty-input-blocked")
    (OUT / "result.txt").write_text("PASS: install, demo, preview, start, pause, resume, background completion, history, dirty input block\n")
finally:
    (OUT / "logcat.txt").write_bytes(adb("logcat", "-d", "-t", "2500"))
    screenshot("last-screen")
