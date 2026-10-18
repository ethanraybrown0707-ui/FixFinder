#include <iostream>

int twice(int n) {
    return n * 2;
}

int main() {
    std::cout << twice("three") << "\n";
    return 0;
}
