fn main() {
    let mark: Option<i32> = Some(70);
    match mark {
        Some(value) => println!("{}", value),
    }
}
