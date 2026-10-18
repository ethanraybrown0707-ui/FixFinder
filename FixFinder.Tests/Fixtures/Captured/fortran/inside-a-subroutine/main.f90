program marks_check
  implicit none
  integer :: marks(3)
  marks = 70
  call show(marks, 4)
contains
  subroutine show(values, position)
    integer, intent(in) :: values(3)
    integer, intent(in) :: position
    print *, values(position)
  end subroutine show
end program marks_check
