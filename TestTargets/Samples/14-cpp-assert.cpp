// A C++ program that builds cleanly and then fails an assertion at run time.
//
// Worth contrasting with 12-c-segfault.c. A native access violation prints absolutely nothing
// and exits with 0xC0000005, so there is nothing to look up and FixFinder says so. An assertion
// is the opposite: it names the condition, the file and the line, which is text a search engine
// can do something with.
#include <cassert>
#include <cstdio>
#include <vector>

static int value_at(const std::vector<int> &values, size_t index)
{
    assert(index < values.size() && "index out of range");
    return values[index];
}

int main()
{
    std::vector<int> values = {1, 2, 3};

    printf("reading values\n");

    // The bug: there is no index 7.
    printf("the eighth value is %d\n", value_at(values, 7));

    return 0;
}
