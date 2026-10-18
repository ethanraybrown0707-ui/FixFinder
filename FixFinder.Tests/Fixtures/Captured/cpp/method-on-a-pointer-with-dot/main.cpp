#include <iostream>
#include <string>

int main() {
    std::string name = "Ada";
    std::string *pointer = &name;
    std::cout << pointer.size() << "\n";
    return 0;
}
