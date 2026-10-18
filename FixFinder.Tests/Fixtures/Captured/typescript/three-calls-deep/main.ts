function first(marks: any[]): number {
  return marks[0].value;
}
function report(marks: any[]): number {
  return first(marks);
}
console.log(report([]));
