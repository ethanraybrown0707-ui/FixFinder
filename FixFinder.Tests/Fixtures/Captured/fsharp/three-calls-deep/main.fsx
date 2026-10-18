let average (marks: int list) = List.sum marks / List.length marks
let report marks = average marks
printfn "%d" (report [])
