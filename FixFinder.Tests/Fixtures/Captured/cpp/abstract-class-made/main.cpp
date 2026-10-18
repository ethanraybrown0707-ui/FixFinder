#include <iostream>

struct Shape {
    virtual double area() = 0;
};

int main() {
    Shape shape;
    std::cout << "hi\n";
    return 0;
}
