#include <iostream>

class Account {
    int balance = 0;
};

int main() {
    Account account;
    std::cout << account.balance << "\n";
    return 0;
}
