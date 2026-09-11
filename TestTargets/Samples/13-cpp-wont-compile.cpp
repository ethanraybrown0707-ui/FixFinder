// A C++ file that does not compile.
//
// A missing <vector> include is one of the most-asked C++ questions there is, and the compiler
// says so in a form worth searching: MSVC reports C2065 and C3861, gcc reports
// "'vector' was not declared in this scope".
#include <iostream>
#include <string>

int main()
{
    // The bug: <vector> was never included.
    std::vector<std::string> names;

    names.push_back("Ada");
    names.push_back("Grace");

    for (const auto &name : names)
    {
        std::cout << name << std::endl;
    }

    return 0;
}
