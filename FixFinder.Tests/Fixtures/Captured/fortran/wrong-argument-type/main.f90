program marks_check
  implicit none
  call show("seventy")
contains
  subroutine show(mark)
    integer, intent(in) :: mark
    print *, mark
  end subroutine show
end program marks_check
