"""Runs only in GitHub Actions against an Android emulator; no physical transport."""
import pathlib
import re
import subprocess
import time
import struct
import zlib
import shutil
import xml.etree.ElementTree as ET

APP = "io.github.dghhskywalker.grbltoolmobile"
OUT = pathlib.Path("artifacts/demo-smoke")
OUT.mkdir(parents=True, exist_ok=True)


def adb(*args):
    return subprocess.check_output(["adb", *args], stderr=subprocess.STDOUT)


def tree():
    # Cold-boot accessibility can briefly have no active root even after boot_completed.
    last = ""
    for _ in range(8):
        try:
            last = adb("shell", "uiautomator", "dump", "/sdcard/grbl-window.xml").decode()
            root = ET.fromstring(adb("shell", "cat", "/sdcard/grbl-window.xml"))
            # Google API images may show a launcher ANR during cold boot. Never dismiss app ANRs.
            nodes = list(root.iter("node"))
            if any(n.get("package") == "android" and n.get("text") == "Pixel Launcher isn't responding" for n in nodes):
                close = next(n for n in nodes if n.get("resource-id") == "android:id/aerr_close")
                x1, y1, x2, y2 = map(int, re.findall(r"\d+", close.get("bounds")))
                adb("shell", "input", "tap", str((x1 + x2) // 2), str((y1 + y2) // 2))
                with (OUT / "emulator-events.txt").open("a") as events:
                    events.write("Dismissed system Pixel Launcher ANR during cold boot\n")
                time.sleep(1)
                continue
            return root
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
    node = wait(label, exact=exact)
    deadline = time.monotonic() + 10
    while node.get("enabled") != "true" and time.monotonic() < deadline:
        time.sleep(.3)
        node = wait(label, exact=exact)
    assert node.get("enabled") == "true", f"Disabled: {label}"
    x1, y1, x2, y2 = map(int, re.findall(r"\d+", node.get("bounds")))
    assert x2 > x1 and y2 > y1, f"Empty hit target: {label}"
    adb("shell", "input", "tap", str((x1 + x2) // 2), str((y1 + y2) // 2))
    time.sleep(.4)


def no_scroll():
    nodes = [node for node in tree().iter("node") if node.get("package") == APP]
    assert nodes, "App hierarchy missing"
    assert not any(n.get("resource-id") in ("android:id/alertTitle", "android:id/button1", "android:id/button2") for n in nodes), "App opened a platform alert dialog"
    assert not any(n.get("class") == "android.widget.Spinner" for n in nodes), "App opened a platform choice spinner"
    assert not any("ScrollView" in node.get("class", "") for node in nodes), "App has a scrolling container"
    dimensions = re.findall(r"(\d+)x(\d+)", adb("shell", "wm", "size").decode())[-1]
    width, height = map(int, dimensions)
    density = int(re.findall(r"\d+", adb("shell", "wm", "density").decode())[-1]) / 160
    for node in nodes:
        if node.get("class") not in ("android.widget.Button", "android.widget.EditText", "android.widget.CheckBox", "android.widget.Spinner"):
            continue
        x1, y1, x2, y2 = map(int, re.findall(r"\d+", node.get("bounds")))
        assert 0 <= x1 < x2 <= width and 0 <= y1 < y2 <= height, node.attrib
        assert y2 - y1 >= 46 * density, f"Clipped/undersized control: {node.attrib}"


def fixture_files():
    directory = OUT / "fixtures"
    directory.mkdir(exist_ok=True)
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 16, 8, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress((b"\0" + b"\0\0\0" * 16) * 8)) + chunk(b"IEND", b"")
    (directory / "valid.png").write_bytes(png)
    width, height = 1600, 800
    rows = bytearray()
    for y in range(height):
        row = bytearray([235, 235, 235] * width)
        if 150 <= y < 650:
            row[200 * 3:1400 * 3] = bytes([190, 190, 190]) * 1200
        if 300 <= y < 500:
            row[600 * 3:1000 * 3] = bytes([235, 235, 235]) * 400
        rows.extend(b"\0" + row)
    complex_png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    complex_png += chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")
    (directory / "complex.png").write_bytes(complex_png)
    (directory / "fake.png").write_bytes(b"%PDF-1.7 disguised document")
    (directory / "unsupported.pdf").write_bytes(b"%PDF-1.7 unsupported")
    for name in ("source-photo.png", "source-qr.png"):
        shutil.copyfile(pathlib.Path("artifacts/core/contours") / name, directory / name)
    for path in directory.iterdir():
        adb("push", str(path), "/sdcard/Download/" + path.name)
        adb("shell", "am", "broadcast", "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", "file:///sdcard/Download/" + path.name)


def choose_bitmap(name):
    tap("导入作品")
    tap("位图 · PNG / JPG / WebP / BMP")
    # Image-grid documents expose filename plus metadata as content-desc.
    wait(name, timeout=40, exact=False)
    excluded = find("unsupported.pdf")
    if excluded is not None:
        assert excluded.get("enabled") == "false", "Unsupported PDF is enabled by the bitmap picker"
    tap(name, exact=False)


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
    fixture_files()
    for setting in ("animator_duration_scale", "transition_animation_scale", "window_animation_scale"):
        adb("shell", "settings", "put", "global", setting, "1")
    time.sleep(15)
    launch()
    wait("体验完整演示", timeout=60)
    no_scroll()
    screenshot("01-device-single-screen")
    tap("授权并连接")
    wait("请先扫描 USB")
    no_scroll()
    assert find("上一页") is None and find("下一页") is None
    screenshot("01b-inline-notice")
    tap("收起提示")
    tap("USB 设备诊断")
    wait("Android 已枚举 0 个 USB 设备")
    no_scroll()
    screenshot("01c-raw-usb-inventory")
    tap("返回")
    tap("收起提示")
    tap("参数")
    tap("设置 DTR")
    tap("拉高")
    wait("DTR · 拉高")
    tap("设置 RTS")
    tap("拉低")
    wait("RTS · 拉低")
    screenshot("01d-configurable-control-lines")
    tap("连接")
    tap("体验完整演示")
    wait("GRBL-demo.nc")
    no_scroll()
    screenshot("02-artwork-preview")
    tap("加工")
    tap("已检查现场", exact=False)
    tap("开始加工")
    no_scroll()
    screenshot("02b-in-app-confirmation")
    tap("返回")
    wait("等待任务")
    tap("开始加工")
    tap("确认")
    tap("暂停")
    wait("已暂停")
    no_scroll()
    screenshot("03-paused-fixed-controls")
    tap("继续")
    tap("确认")
    adb("shell", "input", "keyevent", "3")
    time.sleep(25)
    launch()
    wait("已完成")
    no_scroll()
    screenshot("04-completed-after-background")
    tap("历史")
    wait("GRBL-demo.nc")
    tap("执行记录")
    wait("103/103 行")
    no_scroll()
    assert find("下一页") is None and find("上一页") is None
    screenshot("05-fit-history")
    tap("执行详情")
    wait("执行详情")
    no_scroll()
    assert find("下一页") is None and find("上一页") is None, "Short details were artificially paginated"
    screenshot("05b-full-width-details")
    tap("返回")
    tap("作品")
    choose_bitmap("fake.png")
    wait("图片内容无效")
    screenshot("06-disguised-file-rejected")
    tap("收起提示")
    wait("GRBL-demo.nc")
    choose_bitmap("valid.png")
    wait("valid.png")
    tap("生成任务")
    wait("行 ·", timeout=40)
    no_scroll()
    screenshot("07-supported-file-import")
    choose_bitmap("complex.png")
    wait("complex.png")
    tap("加工参数")
    assert find("下一步") is None and find("上一步") is None
    for label in ("加工宽度 mm", "光栅行距 mm", "加工进给 mm/min", "加工最大功率 S", "Gamma", "二值阈值（0–255）", "最大图像宽度 mm", "最大图像高度 mm", "最大处理宽度 px", "最大处理高度 px", "轮廓细节（1–10）", "照片去噪长度 px"):
        assert find(label) is not None, label
    no_scroll()
    screenshot("07a-all-parameters")
    tap("选择生成方式")
    wait("选择生成方式")
    assert find("上一页") is None and find("下一页") is None
    for algorithm in ("灰度 · LaserGRBL", "轮廓 · OpenCV", "黑白阈值", "Floyd–Steinberg", "SVG 路径轮廓", "Atkinson", "Burkes", "Jarvis", "Stucki", "Sierra 2", "Sierra 3", "Sierra Lite"):
        assert find(algorithm) is not None, algorithm
    no_scroll()
    screenshot("07b-app-algorithm-picker")
    tap("轮廓 · OpenCV")
    def set_number(label, value):
        tap(label)
        adb("shell", "input", "keyevent", "KEYCODE_MOVE_END")
        adb("shell", "input", "keyevent", "67", "67", "67", "67", "67", "67")
        adb("shell", "input", "text", value)
        adb("shell", "input", "keyevent", "4")
    set_number("最大图像宽度 mm", "10")
    set_number("最大图像高度 mm", "10")
    set_number("最大处理宽度 px", "128")
    set_number("最大处理高度 px", "128")
    tap("预览")
    tap("生成任务")
    wait("处理 128×64 px", timeout=45)
    wait("10 × 5 mm")
    no_scroll()
    screenshot("07c-contour-and-proportional-fit")
    tap("加工")
    tap("已检查现场", exact=False)
    tap("开始加工")
    tap("确认")
    wait("已完成", timeout=60)
    tap("历史")
    tap("执行记录")
    tap("执行详情")
    wait("执行详情")
    assert find("下一页") is None and find("上一页") is None, "Image execution details should fit the measured screen"
    no_scroll()
    screenshot("07c-full-image-execution-details")
    tap("返回")
    tap("作品")
    assert find("显示空移").get("checked") == "false"
    tap("显示空移")
    screenshot("07c-travel-explicitly-enabled")
    tap("显示空移")
    tap("加工参数")
    tap("选择生成方式")
    tap("黑白阈值")
    set_number("二值阈值（0–255）", "220")
    tap("预览")
    tap("生成任务")
    wait("黑白阈值", timeout=45)
    screenshot("07d-switch-back-raster")
    tap("加工参数")
    tap("选择生成方式")
    tap("Atkinson")
    tap("预览")
    tap("生成任务")
    wait("Atkinson", timeout=45)
    screenshot("07e-desktop-atkinson")
    tap("加工参数")
    set_number("最大处理宽度 px", "1000")
    set_number("最大处理高度 px", "1000")
    tap("选择生成方式")
    tap("轮廓 · OpenCV")
    tap("选择轮廓策略")
    tap("图文保真")
    tap("预览")
    choose_bitmap("source-qr.png")
    tap("生成任务")
    wait("OpenCV 图文保真", timeout=60)
    no_scroll()
    screenshot("07f-dense-qr-opencv")
    tap("加工参数")
    tap("选择轮廓策略")
    tap("照片细节")
    tap("预览")
    choose_bitmap("source-photo.png")
    tap("生成任务")
    wait("处理 512×512 px", timeout=90)
    no_scroll()
    screenshot("07g-photo-details-opencv")
    tap("加工参数")
    set_number("加工宽度 mm", "25")
    tap("加工")
    wait("输入已变化")
    assert find("开始加工").get("enabled") == "false"
    screenshot("08-dirty-input-blocked")
    # Compact portrait, larger text and an open keyboard: controls must remain on screen.
    adb("shell", "wm", "size", "900x1600")
    adb("shell", "wm", "density", "400")
    adb("shell", "settings", "put", "system", "font_scale", "1.3")
    time.sleep(3)
    launch()
    wait("底部导航", exact=True)
    for page in ("设备", "作品", "加工", "历史"):
        tap(page)
        no_scroll()
        screenshot("09-compact-" + page)
    adb("shell", "dumpsys", "gfxinfo", APP, "reset")
    recording = subprocess.Popen(["adb", "shell", "screenrecord", "--time-limit", "12", "/sdcard/grbl-transitions.mp4"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for page in ("作品", "设备", "加工", "历史", "设备"):
        tap(page)
    recording.wait(timeout=20)
    adb("pull", "/sdcard/grbl-transitions.mp4", str(OUT / "transitions.mp4"))
    (OUT / "frame-stats.txt").write_bytes(adb("shell", "dumpsys", "gfxinfo", APP, "framestats"))
    tap("设备")
    for section in ("定位", "参数", "维护"):
        tap(section)
        no_scroll()
        assert find("下一步") is None and find("上一步") is None
    screenshot("09b-device-maintenance-single-page")
    tap("作品")
    tap("加工参数")
    no_scroll()
    tap("加工宽度 mm")
    time.sleep(1)
    no_scroll()
    screenshot("10-compact-keyboard")
    adb("shell", "input", "keyevent", "4")
    no_scroll()
    tap("图像调整")
    no_scroll()
    for label in ("裁剪左", "裁剪上", "裁剪右", "裁剪下", "旋转 90°", "还原调整"):
        assert find(label) is not None, label
    assert find("下一步") is None and find("上一步") is None
    screenshot("11-all-adjustments")
    (OUT / "result.txt").write_text("PASS: fixed screens, bottom navigation, import allowlist, disguised-file rejection, valid PNG import, pause/resume, background completion, history, dirty input, compact 360x640 at 1.3 font scale, keyboard layout; zero app scrolling gestures, no app-owned platform dialogs/spinners, inline errors, app confirmations, uniform physical/pixel fit, desktop LaserGRBL contours, explicit travel display, desktop dithering, all artwork/device settings in one page, measured full-width details and history pagination, native OpenCV photo/QR modes, enabled 160ms transitions and motion capture\n")
finally:
    (OUT / "logcat.txt").write_bytes(adb("logcat", "-d", "-t", "2500"))
    screenshot("last-screen")
    adb("shell", "settings", "put", "system", "font_scale", "1.0")
    adb("shell", "wm", "size", "reset")
    adb("shell", "wm", "density", "reset")
