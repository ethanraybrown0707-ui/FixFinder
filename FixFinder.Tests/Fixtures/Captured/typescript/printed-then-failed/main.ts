const students: ({ name: string } | null)[] = [{ name: "Ada" }, null];
for (const student of students) {
  console.log("Student:", (student as any).name);
}
