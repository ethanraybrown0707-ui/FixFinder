interface Student {
  name: string;
}
const ada = JSON.parse("null") as Student;
console.log(ada.name);
