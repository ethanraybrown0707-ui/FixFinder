program marks_check
  implicit none
  call show(70)
contains
  subroutine show(mark, total)
    integer, intent(in) :: mark, total
    print *, mark, total
  end subroutine show
end program marks_check
