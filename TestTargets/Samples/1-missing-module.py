"""A missing third-party package.

The case web search is genuinely good at: the error names something outside your own code,
thousands of people have hit it, and the fix is the same for all of them. The quoted 'yaml'
is kept in FixFinder's precise query, because the name of the missing module IS the error.
"""
import yaml     # PyYAML is not installed on this machine

print("loading config...")

with open("config.yml") as handle:
    config = yaml.safe_load(handle)

print(config)
