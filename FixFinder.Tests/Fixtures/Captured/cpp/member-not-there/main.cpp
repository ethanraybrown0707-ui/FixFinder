#include <iostream>

struct Point { int x; };

int main() {
    Point p{1};
    std::cout << p.y << "\n";
    return 0;
}
