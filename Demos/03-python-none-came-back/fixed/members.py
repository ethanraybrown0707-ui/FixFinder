"""Looks a member up by name and prints how old they are."""


def find_member(members, name):
    for member in members:
        if member["name"] == name:
            return member
    return None


members = [
    {"name": "Ada", "age": 36},
    {"name": "Alan", "age": 41},
]

member = find_member(members, "Grace")
if member is None:
    print("No member of that name.")
else:
    print("Age:", member["age"])
