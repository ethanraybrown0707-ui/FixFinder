function first(marks) {
  return marks[0].value;
}
function report(marks) {
  return first(marks);
}
function main() {
  console.log(report([]));
}
main();
