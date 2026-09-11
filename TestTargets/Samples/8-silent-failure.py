"""Fails, and says nothing a machine can use.

Exits non-zero after printing a human sentence with no traceback in it. There is nothing here
to look up, and FixFinder should say exactly that rather than searching for the sentence.
"""
import sys

print("checking the licence file...")
print("ERROR: licence expired. Contact your administrator.", file=sys.stderr)

sys.exit(3)
