use std::io;

fn main() {
    let mut typed = String::new();
    io::stdin().read_line(&mut typed).unwrap();
    let age: i32 = typed.trim().parse().unwrap();
    println!("{}", age + 1);
}
