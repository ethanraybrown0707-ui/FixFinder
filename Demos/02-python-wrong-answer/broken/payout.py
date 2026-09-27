"""Splits a prize between the winners."""


def share(prize, winners):
    return prize // (winners - 1)


print("Each winner gets", share(120, 4))
