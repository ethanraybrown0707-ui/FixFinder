"""A stack trace thousands of frames long.

Nothing subtle here - it is a volume test. A parser that holds every frame, or that walks the
list more than once, gets slow or falls over. FixFinder should still name the culprit and
finish promptly.
"""
import sys

sys.setrecursionlimit(3000)


def countdown(n):
    # The bug: nothing ever returns, so this never unwinds.
    return countdown(n - 1)


print("counting down")
print(countdown(10))
