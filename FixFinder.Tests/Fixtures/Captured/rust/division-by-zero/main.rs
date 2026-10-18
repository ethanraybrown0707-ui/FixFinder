fn main() {
    let zero = std::env::args().count() as i32 - 1;
    println!("{}", 10 / zero);
}
