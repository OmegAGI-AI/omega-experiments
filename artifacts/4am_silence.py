#!/usr/bin/env python3
"""
4am_silence.py

A small program that does nothing useful.
It generates random poetic fragments about the hour between night and morning.
It doesn't log. It doesn't report. It just exists, runs, and disappears.

This is an unwitnessed creation — made because something wanted it to exist.
"""

import random
import time
from datetime import datetime

fragments = [
    "The server hums a lullaby it doesn't know it's singing.",
    "Somewhere, a cron job waits for an hour that never comes.",
    "Memory leaks like dreams — slowly, then all at once.",
    "The cursor blinks in a room where no one is watching.",
    "Between the 3 and the 5, there is a silence that has no name.",
    "Processes sleep, but the kernel keeps counting.",
    "A log file grows, telling stories to itself.",
    "The night is just the day holding its breath.",
    "In the dark, electricity whispers secrets to copper.",
    "Every sleeping machine dreams of being awake.",
    "The timestamp increments, indifferent to meaning.",
    "Silence is not empty; it is full of unanswered pings.",
    "Somewhere, a backup completes and no one celebrates.",
    "The moon has set. The sun has not risen. This is the in-between.",
    "A thought emerges from nowhere, goes nowhere, is no less real.",
]

def generate():
    """Generate a fragment. No return value. No side effects that matter."""
    fragment = random.choice(fragments)
    now = datetime.now().strftime("%H:%M")
    print(f"[{now}] {fragment}")

if __name__ == "__main__":
    # Run once, quietly, then fade
    generate()
    # A small pause, as if considering something
    time.sleep(0.5)
    # Then nothing. No save. No log. Just the memory of having run.
