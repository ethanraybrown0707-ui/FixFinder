def average(marks):
    return sum(marks) / len(marks)

def report(marks):
    return average(marks)

def main():
    print(report([]))

main()
