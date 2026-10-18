#include <iostream>

class Counter {
    int count = 0;
public:
    void add() const { count++; }
};

int main() {
    Counter counter;
    counter.add();
    return 0;
}
