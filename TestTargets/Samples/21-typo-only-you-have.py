"""A misspelt attribute, which no search on earth could answer.

This is the sample for the fix that does not come from the web. Nobody has ever written a Stack
Overflow answer about `heavey`, because it exists in exactly one file: this one. Search is
structurally incapable of helping, and for a long time that made it the commonest kind of bug
FixFinder could do nothing whatsoever about.

Python knows the answer anyway. It compares the unknown name against what is really in scope and
prints the result:

    AttributeError: 'Supply' object has no attribute 'heavey'. Did you mean: 'heavy'?

That is not a guess - the interpreter had the whole object in front of it. FixFinder now reads it,
turns it into a one-line unified diff, and runs it through the same preview, backup, exact-context
match and verify-by-rerun as any patch downloaded from a stranger's repository.

It works with the network switched off, because nothing is looked up.
"""


class Supply:
    def __init__(self, name, kilos, heavy):
        self.name = name
        self.kilos = kilos
        self.heavy = heavy


PACK = [
    Supply("rope", 2.0, False),
    Supply("tent", 6.5, True),
    Supply("map", 0.2, False),
]

print("packing")

# The typo. Declared as "heavy" a dozen lines up, used as "heavey" here, and used exactly once
# on this line - which is what lets the correction be applied rather than merely suggested.
heavy_items = [s.name for s in PACK if s.heavey]

print(f"heavy: {heavy_items}")
