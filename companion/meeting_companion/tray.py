from __future__ import annotations

import ctypes
from ctypes import wintypes
import os


LRESULT = ctypes.c_ssize_t
WM_APP = 0x8000
WM_DESTROY = 0x0002
WM_COMMAND = 0x0111
WM_LBUTTONUP = 0x0202
WM_RBUTTONUP = 0x0205
TRAY_CALLBACK = WM_APP + 1
NIM_ADD = 0x00000000
NIM_DELETE = 0x00000002
NIF_MESSAGE = 0x00000001
NIF_ICON = 0x00000002
NIF_TIP = 0x00000004
IDI_APPLICATION = 32512
MF_STRING = 0x0000
TPM_RETURNCMD = 0x0100
TPM_NONOTIFY = 0x0080
MENU_OPEN = 1001
MENU_EXIT = 1002


class POINT(ctypes.Structure):
    _fields_ = [("x", wintypes.LONG), ("y", wintypes.LONG)]


class MSG(ctypes.Structure):
    _fields_ = [
        ("hwnd", wintypes.HWND),
        ("message", wintypes.UINT),
        ("wParam", wintypes.WPARAM),
        ("lParam", wintypes.LPARAM),
        ("time", wintypes.DWORD),
        ("pt", POINT),
    ]


WNDPROC = ctypes.WINFUNCTYPE(LRESULT, wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)


class WNDCLASSW(ctypes.Structure):
    _fields_ = [
        ("style", wintypes.UINT),
        ("lpfnWndProc", WNDPROC),
        ("cbClsExtra", ctypes.c_int),
        ("cbWndExtra", ctypes.c_int),
        ("hInstance", wintypes.HINSTANCE),
        ("hIcon", wintypes.HICON),
        ("hCursor", wintypes.HCURSOR),
        ("hbrBackground", wintypes.HBRUSH),
        ("lpszMenuName", wintypes.LPCWSTR),
        ("lpszClassName", wintypes.LPCWSTR),
    ]


class NOTIFYICONDATAW(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.DWORD),
        ("hWnd", wintypes.HWND),
        ("uID", wintypes.UINT),
        ("uFlags", wintypes.UINT),
        ("uCallbackMessage", wintypes.UINT),
        ("hIcon", wintypes.HICON),
        ("szTip", wintypes.WCHAR * 128),
        ("dwState", wintypes.DWORD),
        ("dwStateMask", wintypes.DWORD),
        ("szInfo", wintypes.WCHAR * 256),
        ("uTimeoutOrVersion", wintypes.UINT),
        ("szInfoTitle", wintypes.WCHAR * 64),
        ("dwInfoFlags", wintypes.DWORD),
        ("guidItem", ctypes.c_byte * 16),
        ("hBalloonIcon", wintypes.HICON),
    ]


def run_tray(application) -> int:  # pragma: no cover - Windows specific
    user32 = ctypes.windll.user32
    shell32 = ctypes.windll.shell32
    kernel32 = ctypes.windll.kernel32

    class_name = "MeetingRecorderTrayWindow"
    instance = kernel32.GetModuleHandleW(None)
    icon = user32.LoadIconW(None, ctypes.c_wchar_p(IDI_APPLICATION))

    @WNDPROC
    def window_proc(hwnd, message, wparam, lparam):
        if message == TRAY_CALLBACK:
            if lparam == WM_LBUTTONUP:
                os.startfile(str(application.config.meetings_root))
                return 0
            if lparam == WM_RBUTTONUP:
                menu = user32.CreatePopupMenu()
                user32.AppendMenuW(menu, MF_STRING, MENU_OPEN, "Open meetings folder")
                user32.AppendMenuW(menu, MF_STRING, MENU_EXIT, "Exit companion")
                cursor = POINT()
                user32.GetCursorPos(ctypes.byref(cursor))
                user32.SetForegroundWindow(hwnd)
                command = user32.TrackPopupMenu(
                    menu,
                    TPM_RETURNCMD | TPM_NONOTIFY,
                    cursor.x,
                    cursor.y,
                    0,
                    hwnd,
                    None,
                )
                user32.DestroyMenu(menu)
                if command == MENU_OPEN:
                    os.startfile(str(application.config.meetings_root))
                elif command == MENU_EXIT:
                    user32.DestroyWindow(hwnd)
                return 0

        if message == WM_DESTROY:
            application.stop()
            user32.PostQuitMessage(0)
            return 0

        if message == WM_COMMAND:
            command = wparam & 0xFFFF
            if command == MENU_OPEN:
                os.startfile(str(application.config.meetings_root))
                return 0
            if command == MENU_EXIT:
                user32.DestroyWindow(hwnd)
                return 0

        return user32.DefWindowProcW(hwnd, message, wparam, lparam)

    window_class = WNDCLASSW()
    window_class.lpfnWndProc = window_proc
    window_class.hInstance = instance
    window_class.lpszClassName = class_name
    atom = user32.RegisterClassW(ctypes.byref(window_class))
    if not atom:
        raise OSError("Unable to register tray window class")

    hwnd = user32.CreateWindowExW(
        0,
        class_name,
        class_name,
        0,
        0,
        0,
        0,
        0,
        None,
        None,
        instance,
        None,
    )
    if not hwnd:
        raise OSError("Unable to create tray window")

    notify_data = NOTIFYICONDATAW()
    notify_data.cbSize = ctypes.sizeof(NOTIFYICONDATAW)
    notify_data.hWnd = hwnd
    notify_data.uID = 1
    notify_data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP
    notify_data.uCallbackMessage = TRAY_CALLBACK
    notify_data.hIcon = icon
    notify_data.szTip = "Meeting Recorder Companion"
    shell32.Shell_NotifyIconW(NIM_ADD, ctypes.byref(notify_data))

    message = MSG()
    try:
        while user32.GetMessageW(ctypes.byref(message), None, 0, 0) != 0:
            user32.TranslateMessage(ctypes.byref(message))
            user32.DispatchMessageW(ctypes.byref(message))
    finally:
        shell32.Shell_NotifyIconW(NIM_DELETE, ctypes.byref(notify_data))
    return 0
