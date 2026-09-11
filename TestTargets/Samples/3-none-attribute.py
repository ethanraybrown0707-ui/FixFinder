"""The single most common real crash: something was None when you assumed it was not.

FixFinder should show the amber warning that the crash is in your own code, because no issue
or answer anywhere is about this particular function. It is the honest-limits case.
"""

def find_user(users, name):
    for user in users:
        if user["name"] == name:
            return user
    return None            # the bug: callers assume this never happens


def greet(users, name):
    user = find_user(users, name)
    return "Hello, " + user.get("name").strip()


people = [{"name": "Ada"}, {"name": "Grace"}]

print(greet(people, "Ada"))
print(greet(people, "Alan"))
