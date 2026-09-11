"""A program that starts, prints, and then never finishes.

Covers the two run outcomes a crashing program cannot: TimedOut, and Cancelled when you
press Stop. Both have to kill the process rather than wait for it, so this is also what
proves the process-tree kill actually works.

The `input()` at the end is deliberate. It is the classic way a supervised program hangs
invisibly - waiting for a line nobody will ever type - and TargetRunner closes the child's
stdin immediately so that it fails fast instead. If that ever regresses, this fixture
stops reaching the sleep at all and the timeout test silently changes meaning.
"""

import sys
import time


def main() -> None:
    print("hang.py starting")
    print("this program never finishes on its own", file=sys.stderr)
    sys.stdout.flush()

    try:
        # Returns immediately with EOFError because FixFinder closes stdin - it should NOT
        # block here. Any hang at this line is a bug in the runner, not in this fixture.
        input()
    except EOFError:
        print("stdin was closed, as expected", file=sys.stderr)

    sys.stderr.flush()
    time.sleep(600)


if __name__ == "__main__":
    main()
