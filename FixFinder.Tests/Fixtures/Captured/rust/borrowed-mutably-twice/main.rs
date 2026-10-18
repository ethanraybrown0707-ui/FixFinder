fn main() {
    let mut marks = vec![70];
    let first = &mut marks;
    let second = &mut marks;
    first.push(80);
    second.push(90);
}
