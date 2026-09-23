#!/usr/bin/env python3
"""Bounded X11 UI checks against one explicitly identified AWSIM process, never a human-drive claim."""
import argparse
import ctypes as c
import ctypes.util
import os
import re
import subprocess
import time


def own_window(pid):
    os.kill(pid, 0)
    tree = subprocess.check_output(["xwininfo", "-root", "-tree"], text=True)
    for window in re.findall(r'(0x[0-9a-f]+) "AWSIM"', tree):
        prop = subprocess.check_output(["xprop", "-id", window, "_NET_WM_PID"], text=True)
        if re.search(rf"= {pid}\s*$", prop):
            return window
    raise RuntimeError("No AWSIM window belongs to the requested live process")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True, help="PID of the validation player started by the operator")
    actions = parser.add_mutually_exclusive_group(required=True)
    actions.add_argument("--screenshot", help="new PNG destination; only the identified app window is captured")
    actions.add_argument("--click", nargs=2, type=int, metavar=("X", "Y"), help="client-area coordinates")
    actions.add_argument("--key", help="X11 key name, e.g. w, F8, Return")
    parser.add_argument("--hold", type=float, default=0.05, help="key/button hold seconds, maximum 0.5")
    args = parser.parse_args()
    if args.pid <= 1 or not 0.01 <= args.hold <= 0.5:
        parser.error("Specify a player PID and a hold duration from 0.01 to 0.5 seconds")
    window = own_window(args.pid)
    if args.screenshot:
        if os.path.exists(args.screenshot):
            parser.error("Screenshot destination already exists")
        # A disappearing window must not leave ImageMagick waiting for a click on another app.
        subprocess.run(["import", "-window", window, args.screenshot], timeout=3, check=True)
        print(f"captured AWSIM PID {args.pid}, window {window}")
        return
    x11, xtest = c.CDLL(ctypes.util.find_library("X11")), c.CDLL(ctypes.util.find_library("Xtst"))
    x11.XOpenDisplay.argtypes = [c.c_char_p]; x11.XOpenDisplay.restype = c.c_void_p
    x11.XCloseDisplay.argtypes = [c.c_void_p]
    x11.XFlush.argtypes = [c.c_void_p]
    x11.XRaiseWindow.argtypes = [c.c_void_p, c.c_ulong]
    x11.XSetInputFocus.argtypes = [c.c_void_p, c.c_ulong, c.c_int, c.c_ulong]
    x11.XGetInputFocus.argtypes = [c.c_void_p, c.POINTER(c.c_ulong), c.POINTER(c.c_int)]
    x11.XStringToKeysym.argtypes = [c.c_char_p]; x11.XStringToKeysym.restype = c.c_ulong
    x11.XKeysymToKeycode.argtypes = [c.c_void_p, c.c_ulong]; x11.XKeysymToKeycode.restype = c.c_uint
    xtest.XTestFakeKeyEvent.argtypes = [c.c_void_p, c.c_uint, c.c_int, c.c_ulong]
    xtest.XTestFakeButtonEvent.argtypes = [c.c_void_p, c.c_uint, c.c_int, c.c_ulong]
    xtest.XTestFakeMotionEvent.argtypes = [c.c_void_p, c.c_int, c.c_int, c.c_int, c.c_ulong]
    display = x11.XOpenDisplay(None)
    if not display:
        raise RuntimeError("Cannot open the X11 display")
    try:
        target = int(window, 16)
        x11.XRaiseWindow(display, target); x11.XSetInputFocus(display, target, 2, 0); x11.XFlush(display)
        time.sleep(0.1)
        def check_focus():
            if own_window(args.pid) != window:
                raise RuntimeError("Validation window changed")
            focused, revert = c.c_ulong(), c.c_int()
            x11.XGetInputFocus(display, c.byref(focused), c.byref(revert))
            if focused.value != target:
                raise RuntimeError("AWSIM is not focused; refusing to inject input into another app")
        check_focus()
        if args.click:
            info = subprocess.check_output(["xwininfo", "-id", window], text=True)
            def value(label):
                return int(re.search(label + r":\s*(-?\d+)", info).group(1))
            x, y = args.click
            if not 0 <= x < value("Width") or not 0 <= y < value("Height"):
                raise ValueError("Click must be inside the app client area")
            xtest.XTestFakeMotionEvent(display, -1, value("Absolute upper-left X") + x, value("Absolute upper-left Y") + y, 0)
            x11.XFlush(display); check_focus()
            xtest.XTestFakeButtonEvent(display, 1, 1, 0)
            x11.XFlush(display)
            try:
                time.sleep(args.hold)
                check_focus()
            finally:
                xtest.XTestFakeButtonEvent(display, 1, 0, 0)
                x11.XFlush(display)
        else:
            key = x11.XKeysymToKeycode(display, x11.XStringToKeysym(args.key.encode()))
            if not key:
                raise ValueError("Unknown key")
            xtest.XTestFakeKeyEvent(display, key, 1, 0); x11.XFlush(display)
            try:
                deadline = time.monotonic() + args.hold
                while time.monotonic() < deadline:
                    time.sleep(0.01); check_focus()
            finally:
                xtest.XTestFakeKeyEvent(display, key, 0, 0); x11.XFlush(display)
        print(f"synthetic UI validation input sent only to AWSIM PID {args.pid}, window {window}")
    finally:
        x11.XCloseDisplay(display)


if __name__ == "__main__":
    main()
